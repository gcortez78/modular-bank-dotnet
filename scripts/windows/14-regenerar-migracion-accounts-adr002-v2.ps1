param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$MigrationName = "AddProcessedTransferCommands",
    [string]$EfToolVersion = "10.0.0",
    [switch]$BuildAndStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

function Invoke-DockerChecked {
    param(
        [string]$Description,
        [string[]]$Arguments
    )

    & docker @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Falló: $Description"
    }
}

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde a la raíz del repositorio."
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker no está instalado o no está disponible en PATH."
}

$projectPath = Join-Path $RepoRoot "src\ModularBank\ModularBank.csproj"
$migrationsPath = Join-Path $RepoRoot "src\ModularBank\Modules\Accounts\Migrations"
$factoryPath = Join-Path $RepoRoot `
    "src\ModularBank\Modules\Accounts\Infrastructure\AccountsDbContextFactory.cs"

if (-not (Test-Path $projectPath)) {
    throw "No se encontró el proyecto: $projectPath"
}

if (-not (Test-Path $migrationsPath)) {
    throw "No se encontró la carpeta de migraciones: $migrationsPath"
}

Write-Step "Deteniendo el monolito"

& docker compose stop monolith 2>$null

Write-Step "Respaldando las migraciones actuales"

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $RepoRoot `
    ".adr002-backup-manual\accounts-migrations-$timestamp"

New-Item `
    -ItemType Directory `
    -Force `
    -Path $backupPath |
    Out-Null

Get-ChildItem `
    -Path $migrationsPath `
    -File `
    -ErrorAction SilentlyContinue |
ForEach-Object {
    Copy-Item `
        -LiteralPath $_.FullName `
        -Destination $backupPath `
        -Force
}

Write-Host "Respaldo:" -ForegroundColor Green
Write-Host "  $backupPath"

Write-Step "Eliminando la migración manual incompleta"

$manualMigrations = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -File `
        -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -like "*$MigrationName*.cs"
    }
)

if ($manualMigrations.Count -eq 0) {
    Write-Warning "No se encontró una migración previa con nombre $MigrationName."
}
else {
    foreach ($file in $manualMigrations) {
        Write-Host "Eliminando: $($file.Name)"
        Remove-Item `
            -LiteralPath $file.FullName `
            -Force
    }
}

Write-Step "Creando la fábrica de diseño de AccountsDbContext"

$factoryContent = @'
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModularBank.Modules.Accounts.Infrastructure;

public sealed class AccountsDbContextFactory
    : IDesignTimeDbContextFactory<AccountsDbContext>
{
    public AccountsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5433;Database=modular_bank;Username=bank;Password=bank-local";

        var optionsBuilder =
            new DbContextOptionsBuilder<AccountsDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new AccountsDbContext(optionsBuilder.Options);
    }
}
'@

Set-Content `
    -Path $factoryPath `
    -Value $factoryContent `
    -Encoding UTF8

Write-Host "Fábrica creada:" -ForegroundColor Green
Write-Host "  $factoryPath"

Write-Step "Preparando el comando de EF Core"

$repoForDocker = (Resolve-Path $RepoRoot).Path.Replace("\", "/")

$containerCommandTemplate = @'
set -e
export PATH="/tools:$PATH"

dotnet tool install \
  --tool-path /tools \
  dotnet-ef \
  --version "__EF_VERSION__"

dotnet restore \
  src/ModularBank/ModularBank.csproj \
  --disable-parallel

/tools/dotnet-ef migrations add "__MIGRATION_NAME__" \
  --project src/ModularBank/ModularBank.csproj \
  --startup-project src/ModularBank/ModularBank.csproj \
  --context AccountsDbContext \
  --output-dir Modules/Accounts/Migrations
'@

$containerCommand = $containerCommandTemplate `
    .Replace("__EF_VERSION__", $EfToolVersion) `
    .Replace("__MIGRATION_NAME__", $MigrationName)

Write-Step "Generando la migración mediante dotnet ef dentro de Docker"

$dockerArguments = @(
    "run",
    "--rm",
    "-e", "NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10",
    "-e", "NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=2000",
    "-v", "${repoForDocker}:/workspace",
    "-v", "finbank_nuget_cache:/root/.nuget/packages",
    "-w", "/workspace",
    "mcr.microsoft.com/dotnet/sdk:10.0",
    "bash",
    "-lc",
    $containerCommand
)

Invoke-DockerChecked `
    -Description "generación de la migración de Accounts" `
    -Arguments $dockerArguments

Write-Step "Validando los archivos generados"

$generatedMigration = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -File `
        -Filter "*$MigrationName.cs" |
    Sort-Object LastWriteTime -Descending
) | Select-Object -First 1

$generatedDesigner = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -File `
        -Filter "*$MigrationName.Designer.cs" |
    Sort-Object LastWriteTime -Descending
) | Select-Object -First 1

$snapshot = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -File `
        -Filter "*ModelSnapshot.cs"
) | Select-Object -First 1

if (-not $generatedMigration) {
    throw "No se generó el archivo principal de la migración."
}

if (-not $generatedDesigner) {
    throw "No se generó el archivo Designer de la migración."
}

if (-not $snapshot) {
    throw "No se encontró el ModelSnapshot de Accounts."
}

if (-not (Select-String `
    -Path $snapshot.FullName `
    -Pattern "ProcessedTransferCommand" `
    -Quiet)) {

    throw "El ModelSnapshot no contiene ProcessedTransferCommand."
}

Write-Host "Migración:" -ForegroundColor Green
Write-Host "  $($generatedMigration.FullName)"

Write-Host "Designer:" -ForegroundColor Green
Write-Host "  $($generatedDesigner.FullName)"

Write-Host "Snapshot:" -ForegroundColor Green
Write-Host "  $($snapshot.FullName)"

Write-Step "Verificando cambios pendientes del modelo"

$pendingCommandTemplate = @'
set -e
export PATH="/tools:$PATH"

dotnet tool install \
  --tool-path /tools \
  dotnet-ef \
  --version "__EF_VERSION__"

/tools/dotnet-ef migrations has-pending-model-changes \
  --project src/ModularBank/ModularBank.csproj \
  --startup-project src/ModularBank/ModularBank.csproj \
  --context AccountsDbContext
'@

$pendingCommand = $pendingCommandTemplate.Replace(
    "__EF_VERSION__",
    $EfToolVersion
)

$pendingDockerArguments = @(
    "run",
    "--rm",
    "-e", "NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10",
    "-e", "NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=2000",
    "-v", "${repoForDocker}:/workspace",
    "-v", "finbank_nuget_cache:/root/.nuget/packages",
    "-w", "/workspace",
    "mcr.microsoft.com/dotnet/sdk:10.0",
    "bash",
    "-lc",
    $pendingCommand
)

Invoke-DockerChecked `
    -Description "validación de cambios pendientes del modelo" `
    -Arguments $pendingDockerArguments

Write-Host ""
Write-Host "AccountsDbContext y sus migraciones están sincronizados." `
    -ForegroundColor Green

if ($BuildAndStart) {
    Write-Step "Construyendo el monolito"

    Invoke-DockerChecked `
        -Description "construcción del monolito" `
        -Arguments @(
            "compose",
            "--progress",
            "plain",
            "build",
            "monolith"
        )

    Write-Step "Recreando el contenedor monolith"

    Invoke-DockerChecked `
        -Description "inicio del monolito" `
        -Arguments @(
            "compose",
            "up",
            "-d",
            "--no-deps",
            "--force-recreate",
            "monolith"
        )

    Start-Sleep -Seconds 10

    & docker compose ps monolith
    & docker compose logs --since=2m --no-color monolith
}
else {
    Write-Host ""
    Write-Host "Ejecute ahora:" -ForegroundColor Cyan
    Write-Host "  docker compose --progress plain build monolith"
    Write-Host "  docker compose up -d --no-deps --force-recreate monolith"
    Write-Host "  docker compose logs --since=2m --no-color monolith"
}
