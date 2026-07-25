param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$EvidenceDirectory = ""
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

function Invoke-ComposeCapture {
    param(
        [string[]]$ComposeArguments,
        [string]$FailureMessage
    )

    $output = @(
        & docker compose -f compose.yaml -f compose.practico4.yaml @ComposeArguments 2>&1
    )

    if ($LASTEXITCODE -ne 0) {
        foreach ($line in $output) {
            Write-Host $line -ForegroundColor Red
        }

        throw $FailureMessage
    }

    return $output
}

Set-Location -LiteralPath $RepoRoot

if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $folderName = "evidencias\practico4-topologia-{0}" -f (
        Get-Date -Format "yyyyMMdd-HHmmss"
    )
    $EvidenceDirectory = Join-Path $RepoRoot $folderName
}
elseif (-not [System.IO.Path]::IsPathRooted($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $RepoRoot $EvidenceDirectory
}

New-Item -ItemType Directory -Force -Path $EvidenceDirectory | Out-Null

Write-Step "Consultando exchanges"

$exchanges = Invoke-ComposeCapture `
    -ComposeArguments @(
        "exec"
        "-T"
        "rabbitmq"
        "rabbitmqctl"
        "-q"
        "list_exchanges"
        "name"
        "type"
        "durable"
    ) `
    -FailureMessage "No se pudieron listar exchanges."

$exchanges |
    Tee-Object -FilePath (Join-Path $EvidenceDirectory "exchanges.txt") |
    ForEach-Object { Write-Host $_ }

$expectedExchanges = @(
    "finbank.events"
    "finbank.events.retry"
    "finbank.events.dlx"
)

foreach ($expectedExchange in $expectedExchanges) {
    $pattern = "^{0}\s" -f [regex]::Escape($expectedExchange)
    if (-not ($exchanges -match $pattern)) {
        throw ("Falta exchange: {0}" -f $expectedExchange)
    }
}

Write-Step "Consultando colas"

$queues = Invoke-ComposeCapture `
    -ComposeArguments @(
        "exec"
        "-T"
        "rabbitmq"
        "rabbitmqctl"
        "-q"
        "list_queues"
        "name"
        "durable"
        "messages_ready"
        "messages_unacknowledged"
        "consumers"
    ) `
    -FailureMessage "No se pudieron listar colas."

$queues |
    Tee-Object -FilePath (Join-Path $EvidenceDirectory "queues.txt") |
    ForEach-Object { Write-Host $_ }

$mainQueues = @(
    "accounts.transfer-requested.v1"
    "transfers.account-results.v1"
    "notifications.transfer-completed.v1"
    "audit.transfer-results.v1"
)

$expectedQueues = [System.Collections.Generic.List[string]]::new()

foreach ($mainQueue in $mainQueues) {
    $expectedQueues.Add($mainQueue)
    $expectedQueues.Add(("{0}.dlq" -f $mainQueue))

    foreach ($attempt in 1..3) {
        $expectedQueues.Add(("{0}.retry.{1}" -f $mainQueue, $attempt))
    }
}

foreach ($expectedQueue in $expectedQueues) {
    $pattern = "^{0}\s" -f [regex]::Escape($expectedQueue)
    if (-not ($queues -match $pattern)) {
        throw ("Falta cola: {0}" -f $expectedQueue)
    }
}

Write-Step "Consultando bindings"

$bindings = Invoke-ComposeCapture `
    -ComposeArguments @(
        "exec"
        "-T"
        "rabbitmq"
        "rabbitmqctl"
        "-q"
        "list_bindings"
        "source_name"
        "destination_name"
        "routing_key"
    ) `
    -FailureMessage "No se pudieron listar bindings."

$bindings |
    Tee-Object -FilePath (Join-Path $EvidenceDirectory "bindings.txt") |
    ForEach-Object { Write-Host $_ }

$routingKeys = @(
    "transfers.requested.v1"
    "accounts.transfer-applied.v1"
    "accounts.transfer-rejected.v1"
    "transfers.completed.v1"
    "transfers.failed.v1"
)

foreach ($routingKey in $routingKeys) {
    if (-not ($bindings -match [regex]::Escape($routingKey))) {
        throw ("No se encontró binding para routing key: {0}" -f $routingKey)
    }
}

Write-Host ""
Write-Host "VALIDACIÓN DE TOPOLOGÍA SATISFACTORIA" -ForegroundColor Green
Write-Host ("Evidencias: {0}" -f $EvidenceDirectory) -ForegroundColor Cyan
