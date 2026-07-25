param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$BaseUrl = "http://localhost:8080",
    [string]$RabbitApi = "http://localhost:15672",
    [string]$RabbitUser = "finbank",
    [string]$RabbitPassword = "finbank-local",
    [decimal]$Amount = 37.25,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-Token {
    param([object]$Response)

    foreach ($propertyName in @("accessToken", "token", "access_token")) {
        if ($Response.PSObject.Properties.Name -contains $propertyName) {
            $value = [string]$Response.$propertyName

            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }

    throw "No se encontró token en la respuesta de autenticación."
}

function Get-EntityId {
    param([object]$ResponseObject)

    foreach ($propertyName in @("id", "accountId")) {
        if ($ResponseObject.PSObject.Properties.Name -contains $propertyName) {
            return [Guid]$ResponseObject.$propertyName
        }
    }

    throw "No se encontró id en la respuesta."
}

function Invoke-Sql {
    param(
        [string]$Service,
        [string]$Sql
    )

    $output = @(
        $Sql |
            docker compose `
                -f compose.yaml `
                -f compose.practico4.yaml `
                exec -T $Service `
                sh -lc 'psql -At -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' `
                2>&1
    )

    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }

    return ($output -join [Environment]::NewLine).Trim()
}

function Get-RabbitAuthHeader {
    $pair = "{0}:{1}" -f $RabbitUser, $RabbitPassword
    $bytes = [Text.Encoding]::ASCII.GetBytes($pair)
    $encoded = [Convert]::ToBase64String($bytes)

    return @{
        Authorization = "Basic {0}" -f $encoded
    }
}

function Publish-Payload {
    param([string]$Payload)

    $bodyObject = @{
        properties = @{
            delivery_mode = 2
            content_type = "application/cloudevents+json; charset=utf-8"
        }
        routing_key = "transfers.requested.v1"
        payload = $Payload
        payload_encoding = "string"
    }

    $body = $bodyObject | ConvertTo-Json -Depth 10
    $uri = "{0}/api/exchanges/%2F/finbank.events/publish" -f $RabbitApi

    $response = Invoke-RestMethod `
        -Uri $uri `
        -Headers (Get-RabbitAuthHeader) `
        -Method Post `
        -ContentType "application/json" `
        -Body $body `
        -TimeoutSec 20

    if (-not $response.routed) {
        throw "RabbitMQ no enrutó el mensaje duplicado."
    }
}

function Wait-Transfer {
    param(
        [Guid]$TransferId,
        [hashtable]$Headers
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 2

        $items = @(
            Invoke-RestMethod `
                -Uri ("{0}/transfers" -f $BaseUrl) `
                -Headers $Headers `
                -Method Get `
                -TimeoutSec 20
        )

        $item = $items |
            Where-Object { [Guid]$_.id -eq $TransferId } |
            Select-Object -First 1

        if ($null -ne $item) {
            $status = [string]$item.status

            if ($status -in @("Completed", "2")) {
                return $item
            }

            if ($status -in @("Failed", "3")) {
                throw "La transferencia terminó Failed."
            }
        }
    }

    throw "La transferencia no llegó a Completed."
}

Set-Location -LiteralPath $RepoRoot

$folderName = "evidencias\practico4-idempotencia-{0}" -f (
    Get-Date -Format "yyyyMMdd-HHmmss"
)
$evidenceDirectory = Join-Path $RepoRoot $folderName
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null

$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$email = "idempotencia.{0}@finbank.local" -f $timestamp

$registerBody = @{
    email = $email
    password = "FinBank123!"
    name = "Prueba Idempotencia"
} | ConvertTo-Json

$authResponse = Invoke-RestMethod `
    -Uri ("{0}/auth/register" -f $BaseUrl) `
    -Method Post `
    -ContentType "application/json; charset=utf-8" `
    -Body $registerBody `
    -TimeoutSec 30

$token = Get-Token -Response $authResponse
$headers = @{
    Authorization = "Bearer {0}" -f $token
}

$sourceResponse = Invoke-RestMethod `
    -Uri ("{0}/accounts" -f $BaseUrl) `
    -Headers $headers `
    -Method Post `
    -TimeoutSec 30

$targetResponse = Invoke-RestMethod `
    -Uri ("{0}/accounts" -f $BaseUrl) `
    -Headers $headers `
    -Method Post `
    -TimeoutSec 30

$sourceId = Get-EntityId -ResponseObject $sourceResponse
$targetId = Get-EntityId -ResponseObject $targetResponse

$fundingSql = @"
UPDATE accounts.accounts
SET "Balance" = 1000.00
WHERE "Id" = '$sourceId';
"@

Invoke-Sql -Service "postgres-monolith" -Sql $fundingSql | Out-Null

$transferBody = @{
    sourceAccountId = $sourceId
    targetAccountId = $targetId
    amount = $Amount
    reference = "Idempotencia"
} | ConvertTo-Json

$createdTransfer = Invoke-RestMethod `
    -Uri ("{0}/transfers" -f $BaseUrl) `
    -Headers $headers `
    -Method Post `
    -ContentType "application/json; charset=utf-8" `
    -Body $transferBody `
    -TimeoutSec 30

$transferId = [Guid]$createdTransfer.id

Wait-Transfer `
    -TransferId $transferId `
    -Headers $headers |
    Out-Null

$payloadSql = @"
SELECT payload::text
FROM transfers.outbox_messages
WHERE routing_key = 'transfers.requested.v1'
  AND payload::text LIKE '%$transferId%'
ORDER BY occurred_at DESC
LIMIT 1;
"@

$payload = Invoke-Sql -Service "postgres-transfers" -Sql $payloadSql

if ([string]::IsNullOrWhiteSpace($payload)) {
    throw "No se encontró TransferRequested en el Outbox."
}

$stateSql = @"
SELECT
    (SELECT "Balance" FROM accounts.accounts WHERE "Id" = '$sourceId')
    || '|' ||
    (SELECT "Balance" FROM accounts.accounts WHERE "Id" = '$targetId')
    || '|' ||
    (
        SELECT COUNT(*)
        FROM accounts.processed_transfer_commands
        WHERE transfer_id = '$transferId'
    );
"@

$before = Invoke-Sql -Service "postgres-monolith" -Sql $stateSql
Write-Host ("Antes del duplicado: {0}" -f $before)

Publish-Payload -Payload $payload
Publish-Payload -Payload $payload

Start-Sleep -Seconds 8

$after = Invoke-Sql -Service "postgres-monolith" -Sql $stateSql
Write-Host ("Después del duplicado: {0}" -f $after)

if ($before -ne $after) {
    throw "Los saldos o el conteo cambiaron al reprocesar el mismo EventId."
}

$result = @{
    result = "PASS"
    transferId = $transferId
    stateBeforeDuplicate = $before
    stateAfterDuplicate = $after
    validatedAt = [DateTimeOffset]::Now
}

$result |
    ConvertTo-Json |
    Set-Content `
        -Path (Join-Path $evidenceDirectory "resultado.json") `
        -Encoding UTF8

Write-Host "VALIDACIÓN DE IDEMPOTENCIA SATISFACTORIA" -ForegroundColor Green
Write-Host ("Evidencia: {0}" -f $evidenceDirectory) -ForegroundColor Cyan
