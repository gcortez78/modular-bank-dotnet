param(
    [string]$Since = "10m"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$dir = "docs\evidencias\adr002\$timestamp"
New-Item -ItemType Directory -Force -Path $dir | Out-Null

docker compose ps -a |
    Out-File "$dir\01-docker-compose-ps.txt" -Encoding utf8

docker compose logs --since=$Since `
    gateway transfers-service notifications-service monolith rabbitmq |
    Out-File "$dir\02-logs-integrados.txt" -Encoding utf8

'SELECT "MigrationId", "ProductVersion"
 FROM transfers."__EFMigrationsHistory";
 SELECT COUNT(*) AS transfers FROM transfers.transfers;
 SELECT COUNT(*) AS outbox_pending
 FROM transfers.outbox_messages WHERE processed_at IS NULL;' |
docker compose exec -T postgres-transfers `
    sh -lc 'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"' |
    Out-File "$dir\03-transfers-db.txt" -Encoding utf8

'SELECT COUNT(*) AS event_notifications
 FROM notifications.notifications
 WHERE idempotency_key LIKE ''transfer-completed:%'';' |
docker compose exec -T postgres-notifications `
    sh -lc 'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"' |
    Out-File "$dir\04-notifications-db.txt" -Encoding utf8

docker compose exec -T rabbitmq `
    rabbitmqctl list_queues name messages_ready messages_unacknowledged durable |
    Out-File "$dir\05-rabbitmq-queues.txt" -Encoding utf8

docker compose config |
    Out-File "$dir\06-compose-resuelto.yaml" -Encoding utf8

Write-Host "Evidencias generadas en $dir" -ForegroundColor Green
