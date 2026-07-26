param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$BaseUrl = "http://localhost:8080",
    [decimal]$Amount = 22.50,
    [int]$TimeoutSeconds = 180
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

    throw "No se encontró token."
}

function Get-EntityId {
    param([object]$ResponseObject)

    foreach ($propertyName in @("id", "accountId")) {
        if ($ResponseObject.PSObject.Properties.Name -contains $propertyName) {
            return [Guid]$ResponseObject.$propertyName
        }
    }

    throw "No se encontró id."
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

function Wait-Rabbit {
    param([int]$Timeout)

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $containerId = (
            & docker compose `
                -f compose.yaml `
                -f compose.practico4.yaml `
                ps -q rabbitmq
        ).Trim()

        if (-not [string]::IsNullOrWhiteSpace($containerId)) {
            $health = (
                & docker inspect `
                    --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' `
                    $containerId
            ).Trim()

            if ($health -eq "healthy") {
                return
            }
        }

        Start-Sleep -Seconds 3
    }

    throw "RabbitMQ no quedó saludable."
}

function Wait-Transfer {
    param(
        [Guid]$TransferId,
        [hashtable]$Headers
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 3

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
                return
            }

            if ($status -in @("Failed", "3")) {
                throw "La transferencia terminó Failed."
            }
        }
    }

    throw "La transferencia no llegó a Completed."
}

Set-Location -LiteralPath $RepoRoot

$folderName = "evidencias\practico4-outbox-{0}" -f (
    Get-Date -Format "yyyyMMdd-HHmmss"
)
$evidenceDirectory = Join-Path $RepoRoot $folderName
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null

$rabbitStopped = $false

try {
    $timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $email = "outbox.{0}@finbank.local" -f $timestamp

    $registerBody = @{
        email = $email
        password = "FinBank123!"
        name = "Prueba Outbox"
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

    & docker compose `
        -f compose.yaml `
        -f compose.practico4.yaml `
        stop rabbitmq

    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo detener RabbitMQ."
    }

    $rabbitStopped = $true

    $transferBody = @{
        sourceAccountId = $sourceId
        targetAccountId = $targetId
        amount = $Amount
        reference = "Broker detenido"
    } | ConvertTo-Json

    $createdTransfer = Invoke-RestMethod `
        -Uri ("{0}/transfers" -f $BaseUrl) `
        -Headers $headers `
        -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -Body $transferBody `
        -TimeoutSec 30

    $transferId = [Guid]$createdTransfer.id
    Write-Host ("Transferencia creada con broker detenido: {0}" -f $transferId)

    Start-Sleep -Seconds 5

    $pendingSql = @"
SELECT COUNT(*)
FROM transfers.outbox_messages
WHERE routing_key = 'transfers.requested.v1'
  AND payload::text LIKE '%$transferId%'
  AND processed_at IS NULL;
"@

    $pending = Invoke-Sql `
        -Service "postgres-transfers" `
        -Sql $pendingSql

    if ([int]$pending -ne 1) {
        throw "No se encontró el mensaje pendiente en Outbox."
    }

    & docker compose `
        -f compose.yaml `
        -f compose.practico4.yaml `
        start rabbitmq

    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo iniciar RabbitMQ."
    }

    $rabbitStopped = $false

    Wait-Rabbit -Timeout 90
    Wait-Transfer -TransferId $transferId -Headers $headers

    $processedSql = @"
SELECT COUNT(*)
FROM transfers.outbox_messages
WHERE routing_key = 'transfers.requested.v1'
  AND payload::text LIKE '%$transferId%'
  AND processed_at IS NOT NULL;
"@

    $processed = Invoke-Sql `
        -Service "postgres-transfers" `
        -Sql $processedSql

    if ([int]$processed -ne 1) {
        throw "El mensaje Outbox no fue publicado después de recuperar RabbitMQ."
    }

    $transferIdText = $transferId.ToString()
    $logPatterns = @(
        "Outbox"
        "publicado"
        [regex]::Escape($transferIdText)
    )

    & docker compose `
        -f compose.yaml `
        -f compose.practico4.yaml `
        logs --since=15m --timestamps --no-color transfers-service rabbitmq |
        Select-String -Pattern $logPatterns |
        ForEach-Object { $_.Line } |
        Set-Content `
            -Path (Join-Path $evidenceDirectory "logs-outbox.txt") `
            -Encoding UTF8

    $result = @{
        result = "PASS"
        transferId = $transferId
        pendingWhileBrokerDown = [int]$pending
        processedAfterRecovery = [int]$processed
        validatedAt = [DateTimeOffset]::Now
    }

    $result |
        ConvertTo-Json |
        Set-Content `
            -Path (Join-Path $evidenceDirectory "resultado.json") `
            -Encoding UTF8

    Write-Host "VALIDACIÓN OUTBOX CON BROKER CAÍDO SATISFACTORIA" -ForegroundColor Green
    Write-Host ("Evidencia: {0}" -f $evidenceDirectory) -ForegroundColor Cyan
}
finally {
    if ($rabbitStopped) {
        & docker compose `
            -f compose.yaml `
            -f compose.practico4.yaml `
            start rabbitmq |
            Out-Null
    }
}
