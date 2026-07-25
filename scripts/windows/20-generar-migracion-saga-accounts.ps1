param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$MigrationName = "AddSagaChoreographyToAccounts",
    [string]$EfToolVersion = "10.0.4"
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

function Invoke-Docker {
    param(
        [string]$Description,
        [string[]]$DockerArguments
    )

    $previousPreference = $ErrorActionPreference

    try {
        $ErrorActionPreference = "Continue"

        & docker @DockerArguments 2>&1 |
            ForEach-Object { Write-Host $_ }

        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exitCode -ne 0) {
        throw "Falló: $Description. Docker devolvió ExitCode=$exitCode."
    }
}

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde al repositorio."
}

$projectRoot = Join-Path $RepoRoot "src\ModularBank"
$migrationsPath = Join-Path $projectRoot "Modules\Accounts\Migrations"

if (-not (Test-Path $migrationsPath)) {
    throw "No se encontró la carpeta de migraciones de Accounts."
}

$existing = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -File `
        -Filter "*$MigrationName*.cs" `
        -ErrorAction SilentlyContinue
)

if ($existing.Count -gt 0) {
    Write-Host "La migración ya existe:" -ForegroundColor Yellow
    $existing | Select-Object FullName
    exit 0
}

Write-Step "Deteniendo monolith"

$previousPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & docker compose stop monolith 2>&1 |
        ForEach-Object { Write-Host $_ }
}
finally {
    $ErrorActionPreference = $previousPreference
}

Write-Step "Respaldando migraciones de Accounts"

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backup = Join-Path $RepoRoot ".adr002-saga-backup\accounts-migrations-$timestamp"

New-Item -ItemType Directory -Force -Path $backup | Out-Null

Get-ChildItem -Path $migrationsPath -File |
ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $backup -Force
}

Write-Host "Respaldo: $backup" -ForegroundColor Green

Write-Step "Generando migración con dotnet-ef"

$repoForDocker = (Resolve-Path $RepoRoot).Path.Replace("\", "/")

$command = (
    "set -e; " +
    "export PATH=/tools:`$PATH; " +
    "dotnet tool install --tool-path /tools dotnet-ef --version $EfToolVersion; " +
    "dotnet restore src/ModularBank/ModularBank.csproj --disable-parallel; " +
    "/tools/dotnet-ef migrations add $MigrationName " +
    "--project src/ModularBank/ModularBank.csproj " +
    "--startup-project src/ModularBank/ModularBank.csproj " +
    "--context AccountsDbContext " +
    "--output-dir Modules/Accounts/Migrations"
)

Invoke-Docker `
    -Description "generación de la migración Saga de Accounts" `
    -DockerArguments @(
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
        $command
    )

Write-Step "Validando migración generada"

$migration = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -File `
        -Filter "*$MigrationName.cs" |
    Sort-Object LastWriteTime -Descending
) | Select-Object -First 1

$designer = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -File `
        -Filter "*$MigrationName.Designer.cs" |
    Sort-Object LastWriteTime -Descending
) | Select-Object -First 1

$snapshot = @(
    Get-ChildItem `
        -Path $migrationsPath `
        -Recurse `
        -File `
        -Filter "*ModelSnapshot.cs"
) | Select-Object -First 1

if (-not $migration -or -not $designer -or -not $snapshot) {
    throw "La migración, Designer o ModelSnapshot no fueron generados correctamente."
}

$requiredPatterns = @(
    "account_outbox_messages",
    "inbox_messages",
    "request_event_id",
    "failure_reason",
    "result"
)

foreach ($pattern in $requiredPatterns) {
    $found = (Select-String -Path $migration.FullName -Pattern $pattern -Quiet) -or
             (Select-String -Path $snapshot.FullName -Pattern $pattern -Quiet)

    if (-not $found) {
        throw "La migración no contiene el elemento esperado: $pattern"
    }
}

Write-Host "Migración:" -ForegroundColor Green
Write-Host "  $($migration.FullName)"
Write-Host "Snapshot:" -ForegroundColor Green
Write-Host "  $($snapshot.FullName)"

Write-Host ""
Write-Host "Migración Saga de Accounts generada correctamente." -ForegroundColor Green
Write-Host "Siguiente paso:" -ForegroundColor Cyan
Write-Host "  .\scripts\windows\21-construir-levantar-saga-adr002.ps1"
