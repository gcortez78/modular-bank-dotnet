param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$BaseUrl = "http://localhost:8080",
    [decimal]$Amount = 25.50,
    [int]$TimeoutSeconds = 120,
    [string]$NotificationQueue = "notifications.transfer-completed.v1",
    [string]$CompletedRoutingKey = "transfers.completed.v1",
    [string]$EvidenceDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptVersion = "ADR002-VALIDATE-ASYNC-MS1-MS2-V1-20260725"

function Write-Step {
    param([string]$Text)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

function Write-Pass {
    param([string]$Text)
    Write-Host "[PASS] $Text" -ForegroundColor Green
}

function Write-Info {
    param([string]$Text)
    Write-Host "[INFO] $Text" -ForegroundColor Yellow
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
        [string]$Url,
        [hashtable]$Headers,
        [Guid]$TransferId
    )

    $items = Invoke-RestMethod `
        -Uri "$Url/transfers" `
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
        [string]$Url,
        [hashtable]$Headers,
        [Guid]$TransferId,
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 3

        $transfer = Get-Transfer `
            -Url $Url `
            -Headers $Headers `
            -TransferId $TransferId

        if ($null -ne $transfer) {
            $status = [string]$transfer.status
            Write-Info "Transferencia ${TransferId}: estado $status"

            if ($status -in @("Completed", "2")) {
                return $transfer
            }

            if ($status -in @("Failed", "3")) {
                $reason = if (
                    $transfer.PSObject.Properties.Name -contains "failureReason"
                ) {
                    [string]$transfer.failureReason
                }
                else {
                    "sin detalle"
                }

                throw "La transferencia terminó Failed: $reason"
            }
        }
    }

    throw "La transferencia $TransferId no alcanzó el estado Completed en $Timeout segundos."
}

function Wait-Notification {
    param(
        [string]$Url,
        [hashtable]$Headers,
        [Guid]$TransferId,
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 3

        try {
            $notifications = Invoke-RestMethod `
                -Uri "$Url/notifications" `
                -Method Get `
                -Headers $Headers `
                -TimeoutSec 20

            foreach ($notification in @($notifications)) {
                $json = $notification |
                    ConvertTo-Json -Compress -Depth 20

                if ($json -match [regex]::Escape($TransferId.ToString())) {
                    return $notification
                }
            }
        }
        catch {
            Write-Info "Esperando que Notifications responda y procese el evento..."
        }
    }

    throw "No apareció una notificación asociada a $TransferId en $Timeout segundos."
}

function Invoke-Sql {
    param(
        [string]$Service,
        [string]$Sql,
        [switch]$ReturnText
    )

    $output = @(
        $Sql |
        docker compose exec -T $Service `
            sh -lc 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"' `
            2>&1
    )

    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "Falló la consulta SQL en $Service."
    }

    $text = $output -join [Environment]::NewLine
    Write-Host $text

    if ($ReturnText) {
        return $text
    }
}

function Get-QueueState {
    param([string]$QueueName)

    $output = @(
        docker compose exec -T rabbitmq `
            rabbitmqctl -q list_queues `
                name `
                durable `
                messages_ready `
                messages_unacknowledged `
                consumers `
            2>&1
    )

    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ -ForegroundColor Red }
        throw "No fue posible consultar las colas de RabbitMQ."
    }

    $escaped = [regex]::Escape($QueueName)

    $line = $output |
        Where-Object {
            [string]$_ -match "^$escaped(?:\s+|`t)"
        } |
        Select-Object -First 1

    if ($null -eq $line) {
        Write-Host "Colas encontradas:" -ForegroundColor Yellow
        $output | ForEach-Object { Write-Host $_ }
        throw "No existe la cola '$QueueName'."
    }

    $parts = ([string]$line).Trim() -split '\s+'

    if ($parts.Count -lt 5) {
        throw "No se pudo interpretar el estado de la cola: $line"
    }

    return [pscustomobject]@{
        Name                   = $parts[0]
        Durable                = [bool]::Parse($parts[1])
        MessagesReady          = [int]$parts[2]
        MessagesUnacknowledged = [int]$parts[3]
        Consumers              = [int]$parts[4]
        Raw                    = [string]$line
    }
}

function Show-QueueState {
    param(
        [string]$Label,
        [object]$State
    )

    Write-Host (
        "{0}: queue={1}; durable={2}; ready={3}; unacked={4}; consumers={5}" -f
        $Label,
        $State.Name,
        $State.Durable,
        $State.MessagesReady,
        $State.MessagesUnacknowledged,
        $State.Consumers
    )
}

function Wait-QueueState {
    param(
        [string]$QueueName,
        [scriptblock]$Condition,
        [string]$Description,
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)
    $lastState = $null

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $lastState = Get-QueueState -QueueName $QueueName
        Show-QueueState -Label "RabbitMQ" -State $lastState

        if (& $Condition $lastState) {
            return $lastState
        }

        Start-Sleep -Seconds 2
    }

    throw "La cola '$QueueName' no alcanzó la condición: $Description. Último estado: $($lastState.Raw)"
}

function Get-ServiceContainerId {
    param([string]$Service)

    $id = (
        docker compose ps -a -q $Service 2>$null |
        Select-Object -First 1
    )

    if ([string]::IsNullOrWhiteSpace([string]$id)) {
        throw "No existe un contenedor de Docker Compose para '$Service'."
    }

    return ([string]$id).Trim()
}

function Get-ServiceState {
    param([string]$Service)

    $containerId = Get-ServiceContainerId -Service $Service

    $status = (
        docker inspect `
            --format '{{.State.Status}}' `
            $containerId
    ).Trim()

    $health = (
        docker inspect `
            --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' `
            $containerId
    ).Trim()

    return [pscustomobject]@{
        Service = $Service
        Status  = $status
        Health  = $health
    }
}

function Wait-ServiceHealthy {
    param(
        [string]$Service,
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $state = Get-ServiceState -Service $Service
        Write-Info "${Service}: status=$($state.Status), health=$($state.Health)"

        if (
            $state.Status -eq "running" -and
            $state.Health -in @("healthy", "none")
        ) {
            return $state
        }

        Start-Sleep -Seconds 3
    }

    throw "$Service no quedó saludable en $Timeout segundos."
}

function Wait-ServiceStopped {
    param(
        [string]$Service,
        [int]$Timeout
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)

    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $state = Get-ServiceState -Service $Service
        Write-Info "${Service}: status=$($state.Status)"

        if ($state.Status -ne "running") {
            return $state
        }

        Start-Sleep -Seconds 2
    }

    throw "$Service no se detuvo en $Timeout segundos."
}

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde a la raíz del repositorio."
}

if ([string]::IsNullOrWhiteSpace($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path `
        $RepoRoot `
        ("evidencias\ms1-ms2-{0}" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
elseif (-not [System.IO.Path]::IsPathRooted($EvidenceDirectory)) {
    $EvidenceDirectory = Join-Path $RepoRoot $EvidenceDirectory
}

New-Item `
    -ItemType Directory `
    -Force `
    -Path $EvidenceDirectory |
Out-Null

$transcriptPath = Join-Path $EvidenceDirectory "validacion-ms1-ms2.log"
$summaryPath = Join-Path $EvidenceDirectory "resumen-validacion.json"
$bindingsPath = Join-Path $EvidenceDirectory "rabbitmq-bindings.txt"
$logsPath = Join-Path $EvidenceDirectory "logs-publicacion-consumo.txt"
$transferEvidencePath = Join-Path $EvidenceDirectory "transfer-outbox.txt"
$notificationEvidencePath = Join-Path $EvidenceDirectory "notification-db.txt"

$notificationsStoppedByScript = $false
$transcriptStarted = $false
$startedAt = [DateTimeOffset]::Now
$transferId = [Guid]::Empty
$sourceId = [Guid]::Empty
$targetId = [Guid]::Empty
$queueInitial = $null
$queueStopped = $null
$queuePending = $null
$queueFinal = $null

try {
    Start-Transcript `
        -Path $transcriptPath `
        -Force |
    Out-Null

    $transcriptStarted = $true

    Write-Host "Versión: $ScriptVersion" -ForegroundColor Green
    Write-Host "Inicio: $startedAt"
    Write-Host "Repositorio: $RepoRoot"
    Write-Host "Evidencias: $EvidenceDirectory"
    Write-Host "MS2 productor: transfers-service"
    Write-Host "Broker: rabbitmq"
    Write-Host "MS1 consumidor: notifications-service"
    Write-Host "Cola: $NotificationQueue"
    Write-Host "Routing key: $CompletedRoutingKey"

    Write-Step "1. Verificando la plataforma"

    foreach ($service in @(
        "gateway",
        "monolith",
        "transfers-service",
        "notifications-service",
        "rabbitmq",
        "postgres-monolith",
        "postgres-transfers",
        "postgres-notifications"
    )) {
        Wait-ServiceHealthy `
            -Service $service `
            -Timeout $TimeoutSeconds |
        Out-Null
    }

    Write-Pass "Todos los componentes requeridos están en ejecución."

    Write-Step "2. Validando topología RabbitMQ"

    $bindings = @(
        docker compose exec -T rabbitmq `
            rabbitmqctl -q list_bindings `
                source_name `
                destination_name `
                routing_key `
            2>&1
    )

    if ($LASTEXITCODE -ne 0) {
        throw "No fue posible consultar los bindings de RabbitMQ."
    }

    $bindingText = $bindings -join [Environment]::NewLine
    $bindingText |
        Set-Content `
            -Path $bindingsPath `
            -Encoding UTF8

    $bindingText |
        Select-String `
            -Pattern `
                [regex]::Escape($NotificationQueue),
                [regex]::Escape($CompletedRoutingKey) |
        ForEach-Object { Write-Host $_.Line }

    if (
        $bindingText -notmatch [regex]::Escape($NotificationQueue) -or
        $bindingText -notmatch [regex]::Escape($CompletedRoutingKey)
    ) {
        throw "No se encontró el binding esperado entre '$CompletedRoutingKey' y '$NotificationQueue'."
    }

    $queueInitial = Get-QueueState -QueueName $NotificationQueue
    Show-QueueState -Label "Estado inicial" -State $queueInitial

    if (-not $queueInitial.Durable) {
        throw "La cola de Notifications no es durable."
    }

    if ($queueInitial.Consumers -lt 1) {
        throw "Notifications no aparece conectado como consumidor de la cola."
    }

    Write-Pass "La cola es durable y MS1 está conectado como consumidor."

    Write-Step "3. Creando usuario y cuentas para la prueba"

    $email = "async.ms1.ms2.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())@finbank.local"
    $password = "FinBank123!"

    $authBody = @{
        email    = $email
        password = $password
        name     = "Validación asíncrona MS1 MS2"
    } | ConvertTo-Json

    $auth = Invoke-RestMethod `
        -Uri "$BaseUrl/auth/register" `
        -Method Post `
        -ContentType "application/json; charset=utf-8" `
        -Body $authBody `
        -TimeoutSec 30

    $token = Get-Token -Response $auth
    $headers = @{ Authorization = "Bearer $token" }

    $source = Invoke-RestMethod `
        -Uri "$BaseUrl/accounts" `
        -Method Post `
        -Headers $headers `
        -TimeoutSec 30

    $target = Invoke-RestMethod `
        -Uri "$BaseUrl/accounts" `
        -Method Post `
        -Headers $headers `
        -TimeoutSec 30

    $sourceId = Get-AccountId -Account $source
    $targetId = Get-AccountId -Account $target

    Invoke-Sql `
        -Service "postgres-monolith" `
        -Sql @"
UPDATE accounts.accounts
SET "Balance" = 1000.00
WHERE "Id" = '$sourceId';

SELECT "Id", "Balance"
FROM accounts.accounts
WHERE "Id" IN ('$sourceId', '$targetId')
ORDER BY "Id";
"@

    Write-Pass "Usuario y cuentas creados; cuenta origen fondeada con 1000.00."

    Write-Step "4. Deteniendo MS1 Notifications"

    docker compose stop notifications-service

    if ($LASTEXITCODE -ne 0) {
        throw "No fue posible detener notifications-service."
    }

    $notificationsStoppedByScript = $true

    Wait-ServiceStopped `
        -Service "notifications-service" `
        -Timeout 30 |
    Out-Null

    $queueStopped = Wait-QueueState `
        -QueueName $NotificationQueue `
        -Condition {
            param($state)
            $state.Consumers -eq 0
        } `
        -Description "consumers = 0" `
        -Timeout 30

    Show-QueueState -Label "MS1 detenido" -State $queueStopped
    Write-Pass "MS1 está detenido y la cola permanece disponible sin consumidores."

    Write-Step "5. Creando una transferencia en MS2"

    $transferBody = @{
        sourceAccountId = $sourceId
        targetAccountId = $targetId
        amount          = $Amount
        reference       = "Validación asíncrona MS2 a MS1"
    } | ConvertTo-Json

    $transfer = Invoke-RestMethod `
        -Uri "$BaseUrl/transfers" `
        -Method Post `
        -Headers $headers `
        -ContentType "application/json; charset=utf-8" `
        -Body $transferBody `
        -TimeoutSec 30

    $transferId = [Guid]$transfer.id

    Write-Host "TransferId: $transferId"
    Write-Host "Estado inicial: $($transfer.status)"

    $completedTransfer = Wait-TransferStatus `
        -Url $BaseUrl `
        -Headers $headers `
        -TransferId $transferId `
        -Timeout $TimeoutSeconds

    Write-Pass "MS2 completó la transferencia aunque MS1 estaba detenido."

    Write-Step "6. Comprobando que RabbitMQ retuvo el evento"

    $minimumReady = $queueStopped.MessagesReady + 1

    $queuePending = Wait-QueueState `
        -QueueName $NotificationQueue `
        -Condition {
            param($state)
            $state.Consumers -eq 0 -and
            $state.MessagesReady -ge $minimumReady
        } `
        -Description "messages_ready >= $minimumReady y consumers = 0" `
        -Timeout $TimeoutSeconds

    Show-QueueState -Label "Evento pendiente" -State $queuePending

    Write-Pass (
        "RabbitMQ retuvo el evento: messages_ready pasó de {0} a {1}." -f
        $queueStopped.MessagesReady,
        $queuePending.MessagesReady
    )

    Write-Step "7. Validando Outbox de MS2"

    $transferEvidence = Invoke-Sql `
        -Service "postgres-transfers" `
        -ReturnText `
        -Sql @"
SELECT
    id,
    status,
    amount,
    created_at,
    completed_at,
    failure_reason
FROM transfers.transfers
WHERE id = '$transferId';

SELECT
    id,
    event_type,
    routing_key,
    processed_at,
    attempts,
    last_error,
    occurred_at
FROM transfers.outbox_messages
WHERE payload::text LIKE '%$transferId%'
ORDER BY occurred_at;
"@

    $transferEvidence |
        Set-Content `
            -Path $transferEvidencePath `
            -Encoding UTF8

    if (
        $transferEvidence -notmatch "TransferCompleted" -or
        $transferEvidence -notmatch [regex]::Escape($CompletedRoutingKey)
    ) {
        throw "No se encontró TransferCompleted con routing key '$CompletedRoutingKey' en el Outbox."
    }

    Write-Pass "MS2 registró y publicó TransferCompleted mediante Transactional Outbox."

    Write-Step "8. Confirmando que MS1 todavía no persistió la notificación"

    $beforeNotification = Invoke-Sql `
        -Service "postgres-notifications" `
        -ReturnText `
        -Sql @"
SELECT COUNT(*) AS notifications_before_start
FROM notifications.notifications n
WHERE row_to_json(n)::text LIKE '%$transferId%';
"@

    if ($beforeNotification -notmatch '(?m)^\s*0\s*$') {
        Write-Info "Revise el resultado anterior; se esperaba cero notificaciones antes de iniciar MS1."
    }
    else {
        Write-Pass "No existe notificación mientras MS1 permanece detenido."
    }

    Write-Step "9. Reactivando MS1 Notifications"

    docker compose start notifications-service

    if ($LASTEXITCODE -ne 0) {
        throw "No fue posible iniciar notifications-service."
    }

    $notificationsStoppedByScript = $false

    Wait-ServiceHealthy `
        -Service "notifications-service" `
        -Timeout $TimeoutSeconds |
    Out-Null

    $notification = Wait-Notification `
        -Url $BaseUrl `
        -Headers $headers `
        -TransferId $transferId `
        -Timeout $TimeoutSeconds

    Write-Pass "MS1 consumió el evento y la notificación apareció en su API."

    Write-Step "10. Comprobando ACK y drenaje de la cola"

    $baselineReady = $queueStopped.MessagesReady

    $queueFinal = Wait-QueueState `
        -QueueName $NotificationQueue `
        -Condition {
            param($state)
            $state.Consumers -ge 1 -and
            $state.MessagesReady -le $baselineReady -and
            $state.MessagesUnacknowledged -eq 0
        } `
        -Description "consumers >= 1, ready <= $baselineReady y unacked = 0" `
        -Timeout $TimeoutSeconds

    Show-QueueState -Label "Estado final" -State $queueFinal

    Write-Pass "El mensaje fue consumido y confirmado por MS1."

    Write-Step "11. Validando persistencia en la base exclusiva de MS1"

    $notificationEvidence = Invoke-Sql `
        -Service "postgres-notifications" `
        -ReturnText `
        -Sql @"
SELECT row_to_json(n)::text AS notification_evidence
FROM notifications.notifications n
WHERE row_to_json(n)::text LIKE '%$transferId%'
LIMIT 5;
"@

    $notificationEvidence |
        Set-Content `
            -Path $notificationEvidencePath `
            -Encoding UTF8

    if ($notificationEvidence -notmatch [regex]::Escape($transferId.ToString())) {
        throw "La notificación no fue encontrada en notifications_db."
    }

    Write-Pass "La notificación quedó persistida en notifications_db."

    Write-Step "12. Extrayendo logs de publicación y consumo"

    $filteredLogs = @(
        docker compose logs `
            --since=20m `
            --timestamps `
            --no-color `
            transfers-service `
            notifications-service `
            2>&1 |
        Select-String `
            -Pattern `
                "TransferCompleted",
                $CompletedRoutingKey,
                "Evento .* publicado",
                "Evento .* consumido",
                "notificación creada",
                $transferId.ToString()
    )

    $filteredLogs |
        ForEach-Object { Write-Host $_.Line }

    $filteredLogs |
        ForEach-Object { $_.Line } |
        Set-Content `
            -Path $logsPath `
            -Encoding UTF8

    $finishedAt = [DateTimeOffset]::Now

    $summary = [ordered]@{
        result                         = "PASS"
        scriptVersion                  = $ScriptVersion
        startedAt                      = $startedAt
        finishedAt                     = $finishedAt
        producer                       = "transfers-service"
        broker                         = "rabbitmq"
        consumer                       = "notifications-service"
        queue                          = $NotificationQueue
        routingKey                     = $CompletedRoutingKey
        transferId                     = $transferId
        sourceAccountId                = $sourceId
        targetAccountId                = $targetId
        amount                         = $Amount
        queueReadyBeforeStop            = $queueInitial.MessagesReady
        queueConsumersBeforeStop        = $queueInitial.Consumers
        queueReadyWithConsumerStopped   = $queueStopped.MessagesReady
        queueConsumersStopped           = $queueStopped.Consumers
        queueReadyWithEventPending      = $queuePending.MessagesReady
        queueConsumersWithEventPending  = $queuePending.Consumers
        queueReadyAfterConsumption      = $queueFinal.MessagesReady
        queueUnackedAfterConsumption    = $queueFinal.MessagesUnacknowledged
        queueConsumersAfterConsumption  = $queueFinal.Consumers
        evidenceDirectory               = $EvidenceDirectory
    }

    $summary |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -Path $summaryPath `
            -Encoding UTF8

    Write-Step "RESULTADO FINAL"

    Write-Host "VALIDACIÓN SATISFACTORIA" -ForegroundColor Green
    Write-Host ""
    Write-Host "Se demostró la secuencia:" -ForegroundColor Green
    Write-Host "  1. MS1 Notifications fue detenido."
    Write-Host "  2. MS2 Transfers completó la operación."
    Write-Host "  3. RabbitMQ retuvo TransferCompleted."
    Write-Host "  4. MS1 fue reactivado."
    Write-Host "  5. MS1 consumió y confirmó el evento."
    Write-Host "  6. La notificación quedó persistida en notifications_db."
    Write-Host ""
    Write-Host "TransferId: $transferId"
    Write-Host "Evidencias: $EvidenceDirectory" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Archivos generados:"
    Write-Host "  $transcriptPath"
    Write-Host "  $summaryPath"
    Write-Host "  $bindingsPath"
    Write-Host "  $logsPath"
    Write-Host "  $transferEvidencePath"
    Write-Host "  $notificationEvidencePath"
}
catch {
    Write-Host ""
    Write-Host "VALIDACIÓN FALLIDA" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red

    $failedSummary = [ordered]@{
        result            = "FAIL"
        scriptVersion     = $ScriptVersion
        startedAt         = $startedAt
        failedAt          = [DateTimeOffset]::Now
        transferId        = $transferId
        error             = $_.Exception.Message
        evidenceDirectory = $EvidenceDirectory
    }

    $failedSummary |
        ConvertTo-Json -Depth 10 |
        Set-Content `
            -Path $summaryPath `
            -Encoding UTF8

    throw
}
finally {
    if ($notificationsStoppedByScript) {
        Write-Info "Restaurando notifications-service en el bloque finally..."

        try {
            docker compose start notifications-service |
                Out-Null
        }
        catch {
            Write-Host "No fue posible restaurar notifications-service automáticamente." `
                -ForegroundColor Red
        }
    }

    if ($transcriptStarted) {
        Stop-Transcript |
            Out-Null
    }
}
