param(
    [string]$RepoRoot = (Get-Location).Path,
    [switch]$RunBuild,
    [switch]$RunMigration
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

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde a la raíz del repositorio."
}

$legacyEndpoint = Join-Path $RepoRoot `
    "src\ModularBank\Modules\Accounts\Api\InternalTransfersEndpoints.cs"

$programPath = Join-Path $RepoRoot "src\ModularBank\Program.cs"
$backupRoot = Join-Path $RepoRoot `
    ".adr002-saga-backup\retirar-endpoint-sincrono-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

Write-Step "Respaldando el endpoint síncrono legado"

if (Test-Path $legacyEndpoint) {
    $backupPath = Join-Path $backupRoot `
        "src\ModularBank\Modules\Accounts\Api\InternalTransfersEndpoints.cs"

    New-Item `
        -ItemType Directory `
        -Force `
        -Path (Split-Path $backupPath -Parent) |
    Out-Null

    Copy-Item `
        -LiteralPath $legacyEndpoint `
        -Destination $backupPath `
        -Force

    Write-Host "Respaldo:" -ForegroundColor Green
    Write-Host "  $backupPath"
}
else {
    Write-Host "El endpoint legado ya no existe." -ForegroundColor Yellow
}

Write-Step "Eliminando el endpoint HTTP interno de Transfers"

if (Test-Path $legacyEndpoint) {
    Remove-Item `
        -LiteralPath $legacyEndpoint `
        -Force

    Write-Host "Eliminado:" -ForegroundColor Green
    Write-Host "  $legacyEndpoint"
}

Write-Step "Eliminando cualquier mapeo residual en Program.cs"

if (-not (Test-Path $programPath)) {
    throw "No se encontró Program.cs: $programPath"
}

$program = Get-Content $programPath -Raw

$program = [regex]::Replace(
    $program,
    '(?m)^[ \t]*app\.MapInternalTransfersEndpoints\s*\(\s*\)\s*;[ \t]*\r?\n',
    ''
)

Set-Content `
    -Path $programPath `
    -Value $program `
    -Encoding UTF8

Write-Step "Validando que no queden referencias al flujo síncrono"

$references = @(
    Get-ChildItem `
        -Path (Join-Path $RepoRoot "src") `
        -Recurse `
        -File `
        -Filter "*.cs" |
    Select-String `
        -Pattern `
            "InternalTransfersEndpoints",
            "MapInternalTransfersEndpoints"
)

if ($references.Count -gt 0) {
    Write-Host "Referencias residuales encontradas:" -ForegroundColor Red

    $references |
        Select-Object Path, LineNumber, Line |
        Format-Table -AutoSize

    throw "Todavía existen referencias al endpoint HTTP síncrono."
}

Write-Host "No quedan referencias al endpoint síncrono." -ForegroundColor Green

Write-Step "Comprobando componentes de la Saga"

$requiredSagaFiles = @(
    "src\ModularBank\Messaging\AccountsTransferRequestedConsumer.cs",
    "src\ModularBank\Messaging\AccountsOutboxPublisher.cs",
    "src\ModularBank\Messaging\AuditTransferResultConsumer.cs",
    "src\ModularBank\Messaging\SagaMessagingExtensions.cs"
)

foreach ($relativePath in $requiredSagaFiles) {
    $fullPath = Join-Path $RepoRoot $relativePath

    if (-not (Test-Path $fullPath)) {
        throw "Falta el componente Saga: $relativePath"
    }

    Write-Host "OK: $relativePath" -ForegroundColor Green
}

if ($RunBuild) {
    Write-Step "Ejecutando diagnóstico de compilación"

    & ".\scripts\windows\23-diagnosticar-build-saga-adr002.ps1"

    if ($LASTEXITCODE -ne 0) {
        throw "La compilación todavía presenta errores."
    }
}

if ($RunMigration) {
    Write-Step "Generando migración Saga de Accounts"

    & ".\scripts\windows\20-generar-migracion-saga-accounts.ps1"

    if ($LASTEXITCODE -ne 0) {
        throw "No fue posible generar la migración Saga."
    }
}

Write-Host ""
Write-Host "Corrección completada." -ForegroundColor Green
Write-Host ""
Write-Host "Siguientes comandos:" -ForegroundColor Cyan
Write-Host "  .\scripts\windows\23-diagnosticar-build-saga-adr002.ps1"
Write-Host "  .\scripts\windows\20-generar-migracion-saga-accounts.ps1"
Write-Host "  .\scripts\windows\21-construir-levantar-saga-adr002.ps1"
