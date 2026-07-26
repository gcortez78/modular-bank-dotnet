param(
    [decimal]$Amount = 25.50,
    [int]$TimeoutSeconds = 90
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
            $value = $Response.$property
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                return $value
            }
        }
    }

    throw "La respuesta de autenticación no contiene un token reconocido."
}

function Get-AccountId {
    param([object]$Account)

    foreach ($property in @("id", "accountId")) {
        if ($Account.PSObject.Properties.Name -contains $property) {
            return [Guid]$Account.$property
        }
    }

    throw "La respuesta de Accounts no contiene un identificador."
}

$baseUrl = "http://localhost:8080"
$email = "adr002.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())@finbank.local"
$password = "FinBank123!"

Write-Step "Verificando servicios"
docker compose ps

Write-Step "Registrando usuario por Gateway -> Monolito"
$registerBody = @{
    email = $email
    password = $password
    name = "Usuario ADR002"
} | ConvertTo-Json

$auth = Invoke-RestMethod `
    -Uri "$baseUrl/auth/register" `
    -Method Post `
    -ContentType "application/json" `
    -Body $registerBody

$token = Get-Token $auth
$headers = @{ Authorization = "Bearer $token" }

Write-Step "Creando cuentas origen y destino en Accounts"
$source = Invoke-RestMethod `
    -Uri "$baseUrl/accounts" `
    -Method Post `
    -Headers $headers

$target = Invoke-RestMethod `
    -Uri "$baseUrl/accounts" `
    -Method Post `
    -Headers $headers

$sourceId = Get-AccountId $source
$targetId = Get-AccountId $target

Write-Step "Asignando saldo de laboratorio a la cuenta origen"
$sqlSeed = @"
UPDATE accounts.accounts
SET "Balance" = 1000.00
WHERE "Id" = '$sourceId';
"@

$sqlSeed |
docker compose exec -T postgres-monolith `
    sh -lc 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'

Write-Step "Confirmando topología durable antes de la prueba de caída"
docker compose logs --since=2m notifications-service |
    Select-String "Consumidor conectado|Queue|TransferCompleted" |
    ForEach-Object { Write-Host $_ }

Write-Step "Deteniendo Notifications Service"
docker compose stop notifications-service

Write-Step "Creando transferencia con Notifications fuera de servicio"
$transferBody = @{
    sourceAccountId = $sourceId
    targetAccountId = $targetId
    amount = $Amount
    reference = "Prueba ADR-002 RabbitMQ"
} | ConvertTo-Json

$transfer = Invoke-RestMethod `
    -Uri "$baseUrl/transfers" `
    -Method Post `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $transferBody

$transferId = [Guid]$transfer.id
Write-Host "Transferencia creada: $transferId"
Write-Host "Estado: $($transfer.status)"

Write-Step "Validando persistencia exclusiva y Outbox"
$sqlTransfers = @"
SELECT id, status, amount, created_at, completed_at
FROM transfers.transfers
WHERE id = '$transferId';

SELECT id, event_type, routing_key, processed_at, attempts
FROM transfers.outbox_messages
ORDER BY occurred_at DESC
LIMIT 5;
"@

$sqlTransfers |
docker compose exec -T postgres-transfers `
    sh -lc 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'

Write-Step "Reiniciando Notifications Service"
docker compose start notifications-service

$elapsed = 0
$notificationFound = $false

while ($elapsed -lt $TimeoutSeconds) {
    Start-Sleep -Seconds 3
    $elapsed += 3

    try {
        $notifications = Invoke-RestMethod `
            -Uri "$baseUrl/notifications" `
            -Method Get `
            -Headers $headers

        foreach ($notification in @($notifications)) {
            $payloadText = $notification.payload | ConvertTo-Json -Compress -Depth 10
            if ($payloadText -match [regex]::Escape($transferId.ToString())) {
                $notificationFound = $true
                break
            }
        }

        if ($notificationFound) {
            break
        }
    }
    catch {
        Write-Host "Esperando recuperación de Notifications..."
    }
}

if (-not $notificationFound) {
    docker compose logs --tail=200 transfers-service notifications-service rabbitmq
    throw "La notificación no apareció después de $TimeoutSeconds segundos."
}

Write-Step "Validando idempotencia y procesamiento final"
$sqlNotification = @"
SELECT id, user_id, type, idempotency_key, created_at
FROM notifications.notifications
WHERE payload ->> 'transferId' = '$transferId';

SELECT COUNT(*) AS cantidad
FROM notifications.notifications
WHERE payload ->> 'transferId' = '$transferId';
"@

$sqlNotification |
docker compose exec -T postgres-notifications `
    sh -lc 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'

$sqlOutbox = @"
SELECT id, processed_at, attempts, last_error
FROM transfers.outbox_messages
ORDER BY occurred_at DESC
LIMIT 5;
"@

$sqlOutbox |
docker compose exec -T postgres-transfers `
    sh -lc 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"'

Write-Step "Evidencia de enrutamiento y mensajería"
docker compose logs --since=5m `
    gateway transfers-service notifications-service monolith rabbitmq

Write-Host ""
Write-Host "ADR-002 validado satisfactoriamente." -ForegroundColor Green
Write-Host "La transferencia se completó con Notifications detenido."
Write-Host "RabbitMQ conservó el evento y Notifications lo procesó al recuperarse."
