$ErrorActionPreference = "Stop"

function Wait-ServiceHealthy {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Service,
        [int]$TimeoutSeconds = 240
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)

    while ((Get-Date) -lt $deadline) {
        $containerId = (docker compose ps -q $Service).Trim()

        if (-not $containerId) {
            Start-Sleep -Seconds 2
            continue
        }

        $status = (docker inspect `
            --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' `
            $containerId).Trim()

        Write-Host "$Service -> $status"

        if ($status -eq "healthy" -or $status -eq "running") {
            return
        }

        if ($status -in @("unhealthy", "exited", "dead")) {
            docker compose logs --tail 150 $Service
            throw "El servicio $Service terminó con estado $status."
        }

        Start-Sleep -Seconds 3
    }

    docker compose logs --tail 150 $Service
    throw "Tiempo agotado esperando a $Service."
}

if (-not (Test-Path ".env")) {
    throw "No existe .env. Ejecute: Copy-Item .env.example .env"
}

Write-Host "=== Inicio controlado ADR-001 ===" -ForegroundColor Cyan

Write-Host "`n1/6 PostgreSQL monolito" -ForegroundColor Yellow
docker compose up -d postgres-monolith
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar postgres-monolith." }
Wait-ServiceHealthy "postgres-monolith"

Write-Host "`n2/6 PostgreSQL Notifications" -ForegroundColor Yellow
docker compose up -d postgres-notifications
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar postgres-notifications." }
Wait-ServiceHealthy "postgres-notifications"

Write-Host "`n3/6 RabbitMQ" -ForegroundColor Yellow
docker compose up -d rabbitmq
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar rabbitmq." }
Wait-ServiceHealthy "rabbitmq"

Write-Host "`n4/6 Notifications Service" -ForegroundColor Yellow
docker compose up -d notifications-service
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar notifications-service." }
Wait-ServiceHealthy "notifications-service"

Write-Host "`n5/6 Monolito remanente" -ForegroundColor Yellow
docker compose up -d monolith
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar monolith." }
Wait-ServiceHealthy "monolith"

Write-Host "`n6/6 Gateway YARP" -ForegroundColor Yellow
docker compose up -d gateway
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar gateway." }
Wait-ServiceHealthy "gateway"

Write-Host "`nArquitectura ADR-001 iniciada." -ForegroundColor Green
docker compose ps
