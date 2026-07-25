param(
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Text)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

Set-Location -LiteralPath $RepoRoot

Write-Step "Preflight Práctico 4"

if (-not (Test-Path -LiteralPath ".git")) {
    throw ("RepoRoot no corresponde a un repositorio Git: {0}" -f $RepoRoot)
}

$required = @(
    "compose.yaml"
    "src\TransfersService\TransfersService.csproj"
    "src\TransfersService\Messaging\AccountTransferResultConsumer.cs"
    "src\TransfersService\Application\TransfersApplicationService.cs"
    "src\NotificationsService\NotificationsService.csproj"
    "src\NotificationsService\Messaging\TransferCompletedConsumer.cs"
    "src\ModularBank\ModularBank.csproj"
    "src\ModularBank\Messaging\AccountsTransferRequestedConsumer.cs"
    "src\ModularBank\Messaging\AuditTransferResultConsumer.cs"
    "src\ModularBank\Messaging\AccountsOutboxPublisher.cs"
)

$missing = @()
foreach ($relativePath in $required) {
    if (-not (Test-Path -LiteralPath $relativePath)) {
        $missing += $relativePath
    }
}

if ($missing.Count -gt 0) {
    foreach ($relativePath in $missing) {
        Write-Host ("FALTA: {0}" -f $relativePath) -ForegroundColor Red
    }

    throw "El repositorio no contiene todos los artefactos de la Saga ADR-002."
}

$legacyEndpoint = "src\ModularBank\Modules\Accounts\Api\InternalTransfersEndpoints.cs"
if (Test-Path -LiteralPath $legacyEndpoint) {
    throw "Todavía existe InternalTransfersEndpoints.cs. Retire el flujo HTTP síncrono antes de aplicar el Práctico 4."
}

$programPath = "src\ModularBank\Program.cs"
if (-not (Test-Path -LiteralPath $programPath)) {
    throw ("No se encontró {0}." -f $programPath)
}

$program = Get-Content -LiteralPath $programPath -Raw
$registrations = [regex]::Matches($program, "AddSagaMessaging\s*\(").Count
if ($registrations -ne 1) {
    throw ("AddSagaMessaging debe aparecer una sola vez; se encontraron {0}." -f $registrations)
}

$transferCsFiles = @(
    Get-ChildItem -LiteralPath "src\TransfersService" -Recurse -File -Filter "*.cs"
)

$duplicatePostRoutes = @(
    $transferCsFiles | Select-String -SimpleMatch 'MapPost("/")'
)

$duplicateGetRoutes = @(
    $transferCsFiles | Select-String -SimpleMatch 'MapGet("/")'
)

$duplicateRoutes = @($duplicatePostRoutes + $duplicateGetRoutes)
if ($duplicateRoutes.Count -gt 0) {
    $duplicateRoutes |
        Select-Object Path, LineNumber, Line |
        Format-Table -AutoSize

    throw 'Se detectaron rutas raíz duplicables ("/" dentro de MapGroup). Mantenga solamente MapPost("") y MapGet("").'
}

Write-Host "Preflight aprobado." -ForegroundColor Green
