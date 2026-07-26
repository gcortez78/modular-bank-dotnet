param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$MigrationName = "AddProcessedTransferCommands",
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

function Invoke-Checked {
    param(
        [string]$Description,
        [scriptblock]$Command
    )

    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "Falló: $Description"
    }
}

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde a la raíz del repositorio."
}

$projectPath = Join-Path $RepoRoot "src\ModularBank\ModularBank.csproj"
$migrationsPath = Join-Path $RepoRoot "src\ModularBank\Modules\Accounts\Migrations"
$factoryPath = Join-Path $RepoRoot `
    "src\ModularBank\Modules\Accounts\Infrastructure\AccountsDbContextFactory.cs"

if (-not (Test-Path $projectPath)) {
    throw "No se encontró $projectPath"
}

if (-not (Test-Path $migrationsPath)) {
    throw "No se encontró $migrationsPath"
}

Write-Step "Deteniendo el monolito para evitar reinicios continuos"

& docker compose stop monolith 2>$null
$global:LASTEXITCODE = 0

Write-Step "Detectando la versión de Entity Framework Core"

[xml]$projectXml = Get-Content $projectPath -Raw

$designReference = @(
    $projectXml.Project.ItemGroup.PackageReference |
    Where-Object {
        $_.Include -eq "Microsoft.EntityFrameworkCore.Design"
    }
) | Select-Object -First 1

$efVersion = $null

if ($designReference) {
    if ($designReference.Version) {
        $efVersion = [string]$designReference.Version
    }
    elseif ($designReference.VersionOverride) {
        $efVersion = [string]$designReference.VersionOverride
    }
}

if ([string]::IsNullOrWhiteSpace($efVersion) -or
    $efVersion.Contains("$(")) {
    $efVersion = "10.0.0"
    Write-Warning "No se pudo leer la versión exacta de EF Core; se utilizará $efVersion."
}

Write-Host "Versión dotnet-ef: $efVersion" -ForegroundColor Green

Write-Step "Respaldando las migraciones actuales de Accounts"

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $RepoRoot `
    ".adr002-backup-manual\accounts-migrations-$timestamp"

New-Item -ItemType Directory -Force -Path $backupPath | Out-Null

Copy-Item `
    -Path (Join-Path $migrationsPath "*") `
    -Destination $backupPath `
    -Recurse `
    -Force

Write-Host "Respaldo creado en:" -ForegroundColor Green
Write-Host "  $backupPath"

Write-Step "Comprobando que la migración manual no haya sido aplicada"

$historyQuery = @'
SELECT schemaname, tablename
FROM pg_tables
WHERE tablename = '__EFMigrationsHistory'
ORDER BY schemaname;
'@

$historyQuery |
    docker compose exec -T postgres-monolith `
        sh -lc 'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'

if ($LASTEXITCODE -ne 0) {
    Write-Warning "No se pudo consultar el historial; se continuará porque el monolito falló antes de migrar."
}

Write-Step "Eliminando la migración manual incompleta"

$manualMigrations = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -Filter "*$MigrationName*.cs" `
        -File `
        -ErrorAction SilentlyContinue
)

foreach ($file in $manualMigrations) {
    Write-Host "Eliminando: $($file.Name)"
    Remove-Item -LiteralPath $file.FullName -Force
}

if (-not $manualMigrations) {
    Write-Warning "No se encontró una migración previa con nombre $MigrationName."
}

Write-Step "Creando fábrica de diseño para AccountsDbContext"

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

Write-Step "Regenerando la migración con dotnet ef dentro de Docker"

$repoForDocker = (Resolve-Path $RepoRoot).Path.Replace("\", "/")

$containerCommand = @"
set -e
export PATH="/tools:`$PATH"
dotnet tool install --tool-path /tools dotnet-ef --version "$efVersion"
dotnet restore src/ModularBank/ModularBank.csproj --disable-parallel
/tools/dotnet-ef migrations add "$MigrationName" \
  --project src/ModularBank/ModularBank.csproj \
  --startup-project src/ModularBank/ModularBank.csproj \
  --context AccountsDbContext \
  --output-dir Modules/Accounts/Migrations
"@

& docker run --rm `
    -e NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10 `
    -e NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=2000 `
    -v "${repoForDocker}:/workspace" `
    -v "finbank_nuget_cache:/root/.nuget/packages" `
    -w /workspace `
    mcr.microsoft.com/dotnet/sdk:10.0 `
    bash -lc $containerCommand

if ($LASTEXITCODE -ne 0) {
    throw @"
No se pudo generar la migración con dotnet ef.

Las migraciones originales están respaldadas en:
$backupPath
"@
}

Write-Step "Validando los archivos generados"

$generatedMigration = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -Filter "*$MigrationName.cs" `
        -File
) | Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

$generatedDesigner = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -Filter "*$MigrationName.Designer.cs" `
        -File
) | Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

$snapshot = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -Filter "*ModelSnapshot.cs" `
        -File
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

$snapshotContainsEntity = Select-String `
    -Path $snapshot.FullName `
    -Pattern "ProcessedTransferCommand" `
    -Quiet

if (-not $snapshotContainsEntity) {
    throw "El ModelSnapshot no contiene ProcessedTransferCommand."
}

Write-Host "Migración:" -ForegroundColor Green
Write-Host "  $($generatedMigration.FullName)"
Write-Host "Designer:" -ForegroundColor Green
Write-Host "  $($generatedDesigner.FullName)"
Write-Host "Snapshot actualizado:" -ForegroundColor Green
Write-Host "  $($snapshot.FullName)"

Write-Step "Comprobando cambios pendientes del modelo"

$pendingCommand = @"
set -e
export PATH="/tools:`$PATH"
dotnet tool install --tool-path /tools dotnet-ef --version "$efVersion"
/tools/dotnet-ef migrations has-pending-model-changes \
  --project src/ModularBank/ModularBank.csproj \
  --startup-project src/ModularBank/ModularBank.csproj \
  --context AccountsDbContext
"@

& docker run --rm `
    -e NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10 `
    -e NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=2000 `
    -v "${repoForDocker}:/workspace" `
    -v "finbank_nuget_cache:/root/.nuget/packages" `
    -w /workspace `
    mcr.microsoft.com/dotnet/sdk:10.0 `
    bash -lc $pendingCommand

if ($LASTEXITCODE -ne 0) {
    throw "EF Core todavía detecta cambios pendientes en AccountsDbContext."
}

Write-Host ""
Write-Host "AccountsDbContext y sus migraciones están sincronizados." `
    -ForegroundColor Green

if ($BuildAndStart) {
    Write-Step "Construyendo el monolito"

    Invoke-Checked `
        -Description "docker compose build monolith" `
        -Command {
            docker compose --progress plain build monolith
        }

    Write-Step "Recreando el contenedor monolith"

    Invoke-Checked `
        -Description "docker compose up monolith" `
        -Command {
            docker compose up -d --no-deps --force-recreate monolith
        }

    Start-Sleep -Seconds 10

    docker compose ps monolith
    docker compose logs --since=2m --no-color monolith
}
else {
    Write-Host ""
    Write-Host "Ejecute ahora:" -ForegroundColor Cyan
    Write-Host "  docker compose --progress plain build monolith"
    Write-Host "  docker compose up -d --no-deps --force-recreate monolith"
    Write-Host "  docker compose logs --since=2m --no-color monolith"
}
