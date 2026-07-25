param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$MigrationName = "AddProcessedTransferCommands",
    [string]$EfToolVersion = "10.0.4",
    [switch]$BuildAndStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptVersion = "ADR002-MIGRATIONS-CONTINUE-V1-20260725"

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
Write-Host "Archivo ejecutado: $($MyInvocation.MyCommand.Path)"

Set-Location $RepoRoot

$projectRoot = Join-Path $RepoRoot "src\ModularBank"
$projectPath = Join-Path $projectRoot "ModularBank.csproj"

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde a la raíz del repositorio."
}

if (-not (Test-Path $projectPath)) {
    throw "No se encontró: $projectPath"
}

Write-Step "Localizando la migración generada"

$migrationFiles = @(
    Get-ChildItem `
        -Path $projectRoot `
        -Recurse `
        -File `
        -Filter "*$MigrationName*.cs" `
        -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
)

if ($migrationFiles.Count -eq 0) {
    throw @"
No se encontró la migración $MigrationName.
No vuelva a ejecutar la regeneración todavía; revise el contenido de:
$projectRoot
"@
}

$migrationFiles |
    Select-Object FullName, LastWriteTime |
    Format-Table -AutoSize

Write-Step "Buscando snapshots de EF Core en todo el proyecto"

$allSnapshots = @(
    Get-ChildItem `
        -Path $projectRoot `
        -Recurse `
        -File `
        -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -like "*ModelSnapshot.cs" -or
        $_.Name -like "*Snapshot.cs"
    } |
    Sort-Object LastWriteTime -Descending
)

if ($allSnapshots.Count -eq 0) {
    Write-Host "Archivos C# creados o modificados recientemente:" -ForegroundColor Yellow

    Get-ChildItem `
        -Path $projectRoot `
        -Recurse `
        -File `
        -Filter "*.cs" |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 20 FullName, LastWriteTime |
    Format-Table -AutoSize

    throw @"
EF Core generó la migración, pero no existe ningún archivo Snapshot dentro del proyecto.

No construya ni inicie el monolito todavía.
El diagnóstico anterior asumía una ruta incorrecta, pero esta búsqueda ya cubrió todo src\ModularBank.
"@
}

Write-Host "Snapshots encontrados:" -ForegroundColor Green

$allSnapshots |
    Select-Object FullName, LastWriteTime |
    Format-Table -AutoSize

$accountsSnapshot = @(
    $allSnapshots |
    Where-Object {
        Select-String `
            -Path $_.FullName `
            -Pattern "AccountsDbContext" `
            -Quiet
    }
) | Select-Object -First 1

if (-not $accountsSnapshot) {
    throw "Se encontraron snapshots, pero ninguno corresponde a AccountsDbContext."
}

Write-Host "Snapshot de Accounts seleccionado:" -ForegroundColor Green
Write-Host "  $($accountsSnapshot.FullName)"

$modelIncluded = Select-String `
    -Path $accountsSnapshot.FullName `
    -Pattern "ProcessedTransferCommand" `
    -Quiet

if (-not $modelIncluded) {
    throw @"
El snapshot de Accounts existe, pero no contiene ProcessedTransferCommand:
$($accountsSnapshot.FullName)
"@
}

Write-Host "El snapshot contiene ProcessedTransferCommand." -ForegroundColor Green

Write-Step "Validando el modelo con dotnet-ef 10.0.4"

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
    $checkCommand
)

Invoke-Docker `
    -Description "validación de cambios pendientes del modelo" `
    -DockerArguments $dockerArguments

Write-Host ""
Write-Host "AccountsDbContext y el ModelSnapshot están sincronizados." `
    -ForegroundColor Green

if ($BuildAndStart) {
    Write-Step "Construyendo el monolito"

    Invoke-Docker `
        -Description "construcción del monolito" `
        -DockerArguments @(
            "compose",
            "--progress",
            "plain",
            "build",
            "monolith"
        )

    Write-Step "Recreando el contenedor monolith"

    Invoke-Docker `
        -Description "inicio del monolito" `
        -DockerArguments @(
            "compose",
            "up",
            "-d",
            "--no-deps",
            "--force-recreate",
            "monolith"
        )

    Start-Sleep -Seconds 10

    Invoke-Docker `
        -Description "estado del monolito" `
        -DockerArguments @(
            "compose",
            "ps",
            "monolith"
        )

    Invoke-Docker `
        -Description "logs del monolito" `
        -DockerArguments @(
            "compose",
            "logs",
            "--since=2m",
            "--no-color",
            "monolith"
        )
}
else {
    Write-Host ""
    Write-Host "La validación terminó. Ejecute:" -ForegroundColor Cyan
    Write-Host "  docker compose --progress plain build monolith"
    Write-Host "  docker compose up -d --no-deps --force-recreate monolith"
    Write-Host "  docker compose logs --since=2m --no-color monolith"
}
