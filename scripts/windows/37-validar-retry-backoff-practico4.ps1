param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$RabbitApi = "http://localhost:15672",
    [string]$RabbitUser = "finbank",
    [string]$RabbitPassword = "finbank-local",
    [int]$TimeoutSeconds = 220
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-RabbitAuthHeader {
    $pair = "{0}:{1}" -f $RabbitUser, $RabbitPassword
    $bytes = [Text.Encoding]::ASCII.GetBytes($pair)
    $encoded = [Convert]::ToBase64String($bytes)

    return @{
        Authorization = "Basic {0}" -f $encoded
    }
}

function Get-Queue {
    param([string]$QueueName)

    $encodedQueue = [uri]::EscapeDataString($QueueName)
    $uri = "{0}/api/queues/%2F/{1}" -f $RabbitApi, $encodedQueue

    return Invoke-RestMethod `
        -Uri $uri `
        -Headers (Get-RabbitAuthHeader) `
        -Method Get `
        -TimeoutSec 20
}

function Wait-ComposeServiceHealthy {
    param(
        [string]$ServiceName,
        [int]$Timeout = 90
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $containerId = (
            docker compose `
                -f compose.yaml `
                -f compose.practico4.yaml `
                ps -q $ServiceName
        ).Trim()

        if (-not [string]::IsNullOrWhiteSpace($containerId)) {
            $state = (
                docker inspect `
                    --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' `
                    $containerId
            ).Trim()

            Write-Host (
                "{0}: {1}" -f
                $ServiceName,
                $state
            )

            if ($state -in @("healthy", "running")) {
                return
            }
        }

        Start-Sleep -Seconds 2
    }

    throw (
        "{0} no quedó saludable en {1} segundos." -f
        $ServiceName,
        $Timeout
    )
}

function Publish-Event {
    param([string]$Payload)

    $bodyObject = @{
        properties = @{
            delivery_mode = 2
            content_type = "application/cloudevents+json; charset=utf-8"
        }
        routing_key = "transfers.completed.v1"
        payload = $Payload
        payload_encoding = "string"
    }

    $body = $bodyObject | ConvertTo-Json -Depth 20
    $uri = "{0}/api/exchanges/%2F/finbank.events/publish" -f $RabbitApi

    $response = Invoke-RestMethod `
        -Uri $uri `
        -Headers (Get-RabbitAuthHeader) `
        -Method Post `
        -ContentType "application/json" `
        -Body $body `
        -TimeoutSec 20

    if (-not $response.routed) {
        throw "RabbitMQ no enrutó el evento."
    }
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

Set-Location -LiteralPath $RepoRoot

$folderName = "evidencias\practico4-retry-{0}" -f (
    Get-Date -Format "yyyyMMdd-HHmmss"
)
$evidenceDirectory = Join-Path $RepoRoot $folderName
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null

$databaseStopped = $false
$eventId = [Guid]::NewGuid()
$transferId = [Guid]::NewGuid()

try {
    & docker compose `
        -f compose.yaml `
        -f compose.practico4.yaml `
        stop postgres-notifications

    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo detener postgres-notifications."
    }

    $databaseStopped = $true

    $now = [DateTimeOffset]::UtcNow.ToString("O")

    $payloadObject = @{
        specversion = "1.0"
        id = $eventId
        source = "finbank/transfers-service"
        type = "com.finbank.transfers.transfer-completed.v1"
        subject = ("transfer/{0}" -f $transferId.ToString("N"))
        time = $now
        datacontenttype = "application/json"
        dataschema = "urn:finbank:schema:transfer-completed:v1"
        correlationid = $transferId
        causationid = [Guid]::NewGuid()
        data = @{
            transferId = $transferId
            userId = [Guid]::NewGuid()
            sourceAccountId = [Guid]::NewGuid()
            targetAccountId = [Guid]::NewGuid()
            amount = 15.00
            currency = "BOB"
            reference = "Prueba retry"
            completedAtUtc = $now
        }
    }

    $payload = $payloadObject | ConvertTo-Json -Depth 20 -Compress
    Publish-Event -Payload $payload

    $retryObserved = $false
    $retryDeadline = [DateTimeOffset]::UtcNow.AddSeconds(40)

    while ([DateTimeOffset]::UtcNow -lt $retryDeadline) {
        foreach ($attempt in 1..3) {
            $queueName = "notifications.transfer-completed.v1.retry.{0}" -f $attempt
            $queue = Get-Queue -QueueName $queueName

            Write-Host (
                "Retry {0}: messages={1}" -f
                $attempt,
                $queue.messages
            )

            if ([int]$queue.messages -gt 0) {
                $retryObserved = $true
            }
        }

        if ($retryObserved) {
            break
        }

        Start-Sleep -Milliseconds 400
    }

    if (-not $retryObserved) {
        throw "No se observó el mensaje en ninguna cola de retry."
    }

    & docker compose `
        -f compose.yaml `
        -f compose.practico4.yaml `
        start postgres-notifications

    if ($LASTEXITCODE -ne 0) {
        throw "No se pudo iniciar postgres-notifications."
    }

    $databaseStopped = $false

    Wait-ComposeServiceHealthy `
        -ServiceName "postgres-notifications" `
        -Timeout 90

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    $found = $false

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 3

        try {
            $countSql = @"
SELECT COUNT(*)
FROM notifications.notifications n
WHERE row_to_json(n)::text LIKE '%$eventId%';
"@

            $count = Invoke-Sql `
                -Service "postgres-notifications" `
                -Sql $countSql

            if ([int]$count -ge 1) {
                $found = $true
                break
            }
        }
        catch {
            Write-Host "Esperando recuperación de PostgreSQL Notifications..."
        }
    }

    if (-not $found) {
        Write-Host ""
        Write-Host "Estado de colas Notifications:" -ForegroundColor Yellow

        & docker compose `
            -f compose.yaml `
            -f compose.practico4.yaml `
            exec -T rabbitmq `
            rabbitmqctl -q list_queues `
                name `
                messages_ready `
                messages_unacknowledged `
                consumers |
            Select-String -Pattern "notifications.transfer-completed"

        Write-Host ""
        Write-Host "Logs filtrados:" -ForegroundColor Yellow

        $diagnosticPatterns = @(
            [regex]::Escape($eventId.ToString())
            "Routing key no soportada"
            "DeadLettered"
            "Retried"
            "retry"
            "no procesado"
        )

        & docker compose `
            -f compose.yaml `
            -f compose.practico4.yaml `
            logs --since=15m --timestamps --no-color `
                notifications-service |
            Select-String -Pattern $diagnosticPatterns

        throw "La notificación no fue persistida después del retry."
    }

    $eventIdText = $eventId.ToString()
    $logPatterns = @(
        "Retried"
        "retry"
        [regex]::Escape($eventIdText)
    )

    & docker compose `
        -f compose.yaml `
        -f compose.practico4.yaml `
        logs --since=10m --timestamps --no-color notifications-service |
        Select-String -Pattern $logPatterns |
        ForEach-Object { $_.Line } |
        Set-Content `
            -Path (Join-Path $evidenceDirectory "logs-retry.txt") `
            -Encoding UTF8

    $result = @{
        result = "PASS"
        eventId = $eventId
        transferId = $transferId
        retryObserved = $retryObserved
        validatedAt = [DateTimeOffset]::Now
    }

    $result |
        ConvertTo-Json |
        Set-Content `
            -Path (Join-Path $evidenceDirectory "resultado.json") `
            -Encoding UTF8

    Write-Host "VALIDACIÓN RETRY/BACKOFF SATISFACTORIA" -ForegroundColor Green
    Write-Host ("Evidencia: {0}" -f $evidenceDirectory) -ForegroundColor Cyan
}
finally {
    if ($databaseStopped) {
        & docker compose `
            -f compose.yaml `
            -f compose.practico4.yaml `
            start postgres-notifications |
            Out-Null
    }
}
