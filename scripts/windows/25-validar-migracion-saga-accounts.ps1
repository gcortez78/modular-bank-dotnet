param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$MigrationName = "AddSagaChoreographyToAccounts",
    [string]$EfToolVersion = "10.0.4",
    [switch]$BuildAndStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptVersion = "ADR002-SAGA-VALIDATE-MIGRATION-V1-20260725"

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
            ForEach-Object {
                Write-Host $_
            }

        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exitCode -ne 0) {
        throw "Falló: $Description. Docker devolvió ExitCode=$exitCode."
    }
}

Write-Host "Versión del script: $ScriptVersion" -ForegroundColor Green
Write-Host "Repositorio: $RepoRoot"

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde a un repositorio Git."
}

$projectRoot = Join-Path $RepoRoot "src\ModularBank"
$projectPath = Join-Path $projectRoot "ModularBank.csproj"

if (-not (Test-Path $projectPath)) {
    throw "No se encontró el proyecto: $projectPath"
}

Write-Step "Localizando la migración Saga generada"

$allMigrationFiles = @(
    Get-ChildItem `
        -Path $projectRoot `
        -Recurse `
        -File `
        -Filter "*$MigrationName*.cs" `
        -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
)

$migration = @(
    $allMigrationFiles |
    Where-Object {
        $_.Name -notlike "*.Designer.cs"
    }
) | Select-Object -First 1

$designer = @(
    $allMigrationFiles |
    Where-Object {
        $_.Name -like "*.Designer.cs"
    }
) | Select-Object -First 1

if (-not $migration) {
    throw "No se encontró la migración principal $MigrationName en src\ModularBank."
}

if (-not $designer) {
    throw "No se encontró el archivo Designer de $MigrationName."
}

Write-Host "Migración:" -ForegroundColor Green
Write-Host "  $($migration.FullName)"
Write-Host "Designer:" -ForegroundColor Green
Write-Host "  $($designer.FullName)"

Write-Step "Localizando el ModelSnapshot real de AccountsDbContext"

$allSnapshots = @(
    Get-ChildItem `
        -Path $projectRoot `
        -Recurse `
        -File `
        -Filter "*ModelSnapshot.cs" `
        -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
)

if ($allSnapshots.Count -eq 0) {
    throw "No existe ningún ModelSnapshot dentro de src\ModularBank."
}

$accountsSnapshot = @(
    $allSnapshots |
    Where-Object {
        (Select-String `
            -Path $_.FullName `
            -Pattern "AccountsDbContext" `
            -Quiet) -or
        (Select-String `
            -Path $_.FullName `
            -Pattern "ProcessedTransferCommand" `
            -Quiet)
    }
) | Select-Object -First 1

if (-not $accountsSnapshot) {
    Write-Host "Snapshots encontrados:" -ForegroundColor Yellow

    $allSnapshots |
        Select-Object FullName, LastWriteTime |
        Format-Table -AutoSize

    throw "Ningún snapshot encontrado corresponde a AccountsDbContext."
}

Write-Host "Snapshot seleccionado:" -ForegroundColor Green
Write-Host "  $($accountsSnapshot.FullName)"

Write-Step "Validando el contenido de la migración y el snapshot"

$validationGroups = @(
    @{
        Name = "Outbox de Accounts"
        Patterns = @(
            "account_outbox_messages",
            "AccountOutboxMessage"
        )
    },
    @{
        Name = "Inbox de integración"
        Patterns = @(
            "inbox_messages",
            "SagaInboxMessage"
        )
    },
    @{
        Name = "RequestEventId"
        Patterns = @(
            "request_event_id",
            "RequestEventId"
        )
    },
    @{
        Name = "FailureReason"
        Patterns = @(
            "failure_reason",
            "FailureReason"
        )
    },
    @{
        Name = "Result"
        Patterns = @(
            '"result"',
            "Result"
        )
    }
)

foreach ($group in $validationGroups) {
    $found = $false

    foreach ($pattern in $group.Patterns) {
        if (
            (Select-String `
                -Path $migration.FullName `
                -Pattern $pattern `
                -SimpleMatch `
                -Quiet) -or
            (Select-String `
                -Path $designer.FullName `
                -Pattern $pattern `
                -SimpleMatch `
                -Quiet) -or
            (Select-String `
                -Path $accountsSnapshot.FullName `
                -Pattern $pattern `
                -SimpleMatch `
                -Quiet)
        ) {
            $found = $true
            break
        }
    }

    if (-not $found) {
        throw "No se encontró en los artefactos EF Core: $($group.Name)"
    }

    Write-Host "OK: $($group.Name)" -ForegroundColor Green
}

Write-Step "Validando cambios pendientes con dotnet-ef"

$repoForDocker = (Resolve-Path $RepoRoot).Path.Replace("\", "/")

$checkCommand = (
    "set -e; " +
    "export PATH=/tools:`$PATH; " +
    "dotnet tool install --tool-path /tools dotnet-ef --version $EfToolVersion; " +
    "dotnet restore src/ModularBank/ModularBank.csproj --disable-parallel; " +
    "/tools/dotnet-ef migrations has-pending-model-changes " +
    "--project src/ModularBank/ModularBank.csproj " +
    "--startup-project src/ModularBank/ModularBank.csproj " +
    "--context AccountsDbContext"
)

Invoke-Docker `
    -Description "validación del ModelSnapshot de Accounts" `
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
        $checkCommand
    )

Write-Host ""
Write-Host "La migración Saga y AccountsDbContext están sincronizados." `
    -ForegroundColor Green

if ($BuildAndStart) {
    Write-Step "Construyendo y levantando la Saga ADR-002"

    $buildScript = Join-Path $RepoRoot `
        "scripts\windows\21-construir-levantar-saga-adr002.ps1"

    if (-not (Test-Path $buildScript)) {
        throw "No se encontró: $buildScript"
    }

    & $buildScript

    if ($LASTEXITCODE -ne 0) {
        throw "El script 21 no terminó correctamente."
    }
}
else {
    Write-Host ""
    Write-Host "Siguiente comando:" -ForegroundColor Cyan
    Write-Host "  .\scripts\windows\21-construir-levantar-saga-adr002.ps1"
}
