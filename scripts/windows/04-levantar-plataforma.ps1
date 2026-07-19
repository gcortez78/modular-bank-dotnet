$ErrorActionPreference = "Stop"

function Wait-ServiceHealthy {
    param(
        [Parameter(Mandatory = $true)][string]$Service,
        [int]$TimeoutSeconds = 240
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $containerId = (docker compose ps -q $Service).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
            Start-Sleep -Seconds 2
            continue
        }

        $status = (docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $containerId).Trim()
        Write-Host "$Service -> $status"

        if ($status -eq "healthy" -or $status -eq "running") {
            return
        }
        if ($status -eq "unhealthy" -or $status -eq "exited" -or $status -eq "dead") {
            docker compose logs --tail 100 $Service
            throw "El servicio $Service terminó con estado $status."
        }
        Start-Sleep -Seconds 3
    }

    docker compose logs --tail 100 $Service
    throw "Tiempo agotado esperando a $Service."
}

if (-not (Test-Path ".env")) {
    throw "No existe .env. Ejecute: Copy-Item .env.example .env"
}

Write-Host "=== Inicio controlado de PLATAFORMA_BASE_FINBANK ===" -ForegroundColor Cyan

Write-Host "`n1/4 PostgreSQL" -ForegroundColor Yellow
docker compose up -d postgres-monolith
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar postgres-monolith." }
Wait-ServiceHealthy -Service "postgres-monolith"

Write-Host "`n2/4 RabbitMQ" -ForegroundColor Yellow
docker compose up -d rabbitmq
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar rabbitmq." }
Wait-ServiceHealthy -Service "rabbitmq"

Write-Host "`n3/4 Monolito" -ForegroundColor Yellow
docker compose up -d monolith
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar monolith." }
Wait-ServiceHealthy -Service "monolith"

Write-Host "`n4/4 Gateway" -ForegroundColor Yellow
docker compose up -d gateway
if ($LASTEXITCODE -ne 0) { throw "No se pudo iniciar gateway." }
Wait-ServiceHealthy -Service "gateway"

Write-Host "`nPlataforma iniciada." -ForegroundColor Green
docker compose ps
