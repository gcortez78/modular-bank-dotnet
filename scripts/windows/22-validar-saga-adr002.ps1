param(
    [decimal]$Amount = 25.50,
    [int]$TimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Get-Token {
    param([object]$Response)

    foreach ($property in @("accessToken", "token", "access_token")) {
        if ($Response.PSObject.Properties.Name -contains $property) {
            $value = [string]$Response.$property

            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }

    throw "La autenticación no devolvió un token."
}

function Get-AccountId {
    param([object]$Account)

    foreach ($property in @("id", "accountId")) {
        if ($Account.PSObject.Properties.Name -contains $property) {
            return [Guid]$Account.$property
        }
    }

    throw "Accounts no devolvió un identificador."
}

function Get-Transfer {
    param(
        [string]$BaseUrl,
        [hashtable]$Headers,
        [Guid]$TransferId
    )

    $items = Invoke-RestMethod `
        -Uri "$BaseUrl/transfers" `
        -Method Get `
        -Headers $Headers `
        -TimeoutSec 20

    foreach ($item in @($items)) {
        if ([Guid]$item.id -eq $TransferId) {
            return $item
        }
    }

    return $null
}

function Wait-TransferStatus {
    param(
        [string]$BaseUrl,
        [hashtable]$Headers,
        [Guid]$TransferId,
        [string[]]$ExpectedStatuses,
        [int]$Timeout
    )

    $elapsed = 0

    while ($elapsed -lt $Timeout) {
        Start-Sleep -Seconds 3
        $elapsed += 3

        $transfer = Get-Transfer `
            -BaseUrl $BaseUrl `
            -Headers $Headers `
            -TransferId $TransferId

        if ($null -ne $transfer) {
            $status = [string]$transfer.status
            Write-Host "Transferencia $TransferId - estado $status"

            if ($ExpectedStatuses -contains $status) {
                return $transfer
            }
        }
    }

    throw "La transferencia $TransferId no alcanzó: $($ExpectedStatuses -join ', ')."
}

function Wait-Notification {
    param(
        [string]$BaseUrl,
        [hashtable]$Headers,
        [Guid]$TransferId,
        [int]$Timeout
    )

    $elapsed = 0

    while ($elapsed -lt $Timeout) {
        Start-Sleep -Seconds 3
        $elapsed += 3

        try {
            $notifications = Invoke-RestMethod `
                -Uri "$BaseUrl/notifications" `
                -Method Get `
                -Headers $Headers `
                -TimeoutSec 20

            foreach ($notification in @($notifications)) {
                $payload = $notification.payload |
                    ConvertTo-Json -Compress -Depth 10

                if ($payload -match [regex]::Escape(
                    $TransferId.ToString())) {
                    return $notification
                }
            }
        }
        catch {
            Write-Host "Esperando Notifications..."
        }
    }

    throw "No apareció la notificación para $TransferId."
}

function Invoke-Sql {
    param(
        [string]$Service,
        [string]$Sql
    )

    $Sql |
    docker compose exec -T $Service `
        sh -lc 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'

    if ($LASTEXITCODE -ne 0) {
        throw "Falló la consulta SQL en $Service."
    }
}

$baseUrl = "http://localhost:8080"
$email = "saga.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())@finbank.local"
$password = "FinBank123!"

$notificationsStopped = $false
$monolithStopped = $false

try {
    Write-Step "Verificando plataforma"
    docker compose ps -a

    Write-Step "Registrando usuario"
    $authBody = @{
        email = $email
        password = $password
        name = "Usuario Saga ADR002"
    } | ConvertTo-Json

    $auth = Invoke-RestMethod `
        -Uri "$baseUrl/auth/register" `
        -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -Body $authBody `
        -TimeoutSec 30

    $token = Get-Token $auth
    $headers = @{ Authorization = "Bearer $token" }

    Write-Step "Creando cuentas"
    $source = Invoke-RestMethod `
        -Uri "$baseUrl/accounts" `
        -Method Post `
        -Headers $headers `
        -TimeoutSec 30

    $target = Invoke-RestMethod `
        -Uri "$baseUrl/accounts" `
        -Method Post `
        -Headers $headers `
        -TimeoutSec 30

    $sourceId = Get-AccountId $source
    $targetId = Get-AccountId $target

    Invoke-Sql `
        -Service "postgres-monolith" `
        -Sql @"
UPDATE accounts.accounts
SET "Balance" = 1000.00
WHERE "Id" = '$sourceId';
"@

    # ESCENARIO 1
    Write-Step "Escenario 1: Notifications fuera de servicio"
    docker compose stop notifications-service
    $notificationsStopped = $true

    $body1 = @{
        sourceAccountId = $sourceId
        targetAccountId = $targetId
        amount = $Amount
        reference = "Saga - Notifications detenido"
    } | ConvertTo-Json

    $transfer1 = Invoke-RestMethod `
        -Uri "$baseUrl/transfers" `
        -Method Post `
        -Headers $headers `
        -ContentType "application/json; charset=utf-8" `
        -Body $body1 `
        -TimeoutSec 30

    $transfer1Id = [Guid]$transfer1.id
    Write-Host "Aceptada: $transfer1Id - estado inicial $($transfer1.status)"

    $completed1 = Wait-TransferStatus `
        -BaseUrl $baseUrl `
        -Headers $headers `
        -TransferId $transfer1Id `
        -ExpectedStatuses @("Completed", "2") `
        -Timeout $TimeoutSeconds

    Write-Host "Transferencia completada sin Notifications." -ForegroundColor Green

    Write-Step "Reactivando Notifications y esperando consistencia eventual"
    docker compose start notifications-service
    $notificationsStopped = $false

    Start-Sleep -Seconds 8

    $notification = Wait-Notification `
        -BaseUrl $baseUrl `
        -Headers $headers `
        -TransferId $transfer1Id `
        -Timeout $TimeoutSeconds

    Write-Host "Notificación recibida después de la recuperación." -ForegroundColor Green

    # ESCENARIO 2
    Write-Step "Escenario 2: Accounts/Audit del monolito fuera de servicio"
    docker compose stop monolith
    $monolithStopped = $true

    $body2 = @{
        sourceAccountId = $sourceId
        targetAccountId = $targetId
        amount = $Amount
        reference = "Saga - Monolito detenido"
    } | ConvertTo-Json

    $transfer2 = Invoke-RestMethod `
        -Uri "$baseUrl/transfers" `
        -Method Post `
        -Headers $headers `
        -ContentType "application/json; charset=utf-8" `
        -Body $body2 `
        -TimeoutSec 30

    $transfer2Id = [Guid]$transfer2.id
    Write-Host "Aceptada con monolito detenido: $transfer2Id"

    Start-Sleep -Seconds 6

    $pending2 = Get-Transfer `
        -BaseUrl $baseUrl `
        -Headers $headers `
        -TransferId $transfer2Id

    if ($null -eq $pending2) {
        throw "No se encontró la transferencia creada con monolito detenido."
    }

    $pendingStatus = [string]$pending2.status

    if ($pendingStatus -notin @("Pending", "1")) {
        throw "Se esperaba Pending mientras Accounts estaba detenido; estado: $pendingStatus"
    }

    Write-Host "La solicitud quedó Pending en Transfers y durable en RabbitMQ." `
        -ForegroundColor Green

    docker compose exec -T rabbitmq `
        rabbitmqctl list_queues name messages_ready messages_unacknowledged |
        Select-String `
            "accounts.transfer-requested.v1|transfers.account-results.v1|audit.transfer-results.v1"

    Write-Step "Reactivando monolito"
    docker compose start monolith
    $monolithStopped = $false

    Start-Sleep -Seconds 10

    $completed2 = Wait-TransferStatus `
        -BaseUrl $baseUrl `
        -Headers $headers `
        -TransferId $transfer2Id `
        -ExpectedStatuses @("Completed", "2") `
        -Timeout $TimeoutSeconds

    Write-Host "Accounts consumió TransferRequested y completó la Saga." `
        -ForegroundColor Green

    Write-Step "Validando Outbox, idempotencia y Audit"

    Invoke-Sql `
        -Service "postgres-transfers" `
        -Sql @"
SELECT id, status, amount, created_at, completed_at, failure_reason
FROM transfers.transfers
WHERE id IN ('$transfer1Id', '$transfer2Id')
ORDER BY created_at;

SELECT id, event_type, routing_key, processed_at, attempts, last_error
FROM transfers.outbox_messages
WHERE payload::text LIKE '%$transfer1Id%'
   OR payload::text LIKE '%$transfer2Id%'
ORDER BY occurred_at;

SELECT consumer_name, event_id, processed_at
FROM transfers.processed_integration_events
ORDER BY processed_at DESC
LIMIT 10;
"@

    Invoke-Sql `
        -Service "postgres-monolith" `
        -Sql @"
SELECT transfer_id, request_event_id, result, failure_reason, processed_at
FROM accounts.processed_transfer_commands
WHERE transfer_id IN ('$transfer1Id', '$transfer2Id')
ORDER BY processed_at;

SELECT id, event_type, routing_key, processed_at, attempts, last_error
FROM accounts.account_outbox_messages
WHERE payload::text LIKE '%$transfer1Id%'
   OR payload::text LIKE '%$transfer2Id%'
ORDER BY occurred_at;

SELECT consumer_name, event_id, processed_at
FROM integration.inbox_messages
ORDER BY processed_at DESC
LIMIT 10;

SELECT row_to_json(a)::text AS audit_evidence
FROM audit.audit_entries a
WHERE row_to_json(a)::text LIKE '%$transfer1Id%'
   OR row_to_json(a)::text LIKE '%$transfer2Id%';
"@

    Write-Step "Evidencia de logs"
    docker compose logs `
        --since=10m `
        --no-color `
        transfers-service `
        monolith `
        notifications-service `
        rabbitmq |
    Select-String `
        -Pattern `
            "TransferRequested",
            "AccountTransferApplied",
            "AccountTransferRejected",
            "TransferCompleted",
            "TransferFailed",
            "Audit registró",
            "notificación creada" `
        -Context 1,2

    Write-Host ""
    Write-Host "Saga ADR-002 validada satisfactoriamente." -ForegroundColor Green
    Write-Host "Transfers, Accounts, Notifications y Audit se coordinaron por RabbitMQ."
}
finally {
    if ($notificationsStopped) {
        docker compose start notifications-service |
            Out-Null
    }

    if ($monolithStopped) {
        docker compose start monolith |
            Out-Null
    }
}
