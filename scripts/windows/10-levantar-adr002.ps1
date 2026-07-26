param(
    [int]$TimeoutSeconds = 240,
    [bool]$RepairRuntimeState = $true,
    [string]$ProjectName = "plataforma-base-finbank"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

function Assert-Command {
    param([string]$Name)

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontró el comando requerido: $Name"
    }
}

function Show-Diagnostics {
    param([string]$Service = "")

    Write-Host ""
    Write-Host "Estado de Docker Compose:" -ForegroundColor Yellow
    & docker compose ps -a

    Write-Host ""
    if ([string]::IsNullOrWhiteSpace($Service)) {
        Write-Host "Últimos logs integrados:" -ForegroundColor Yellow
        & docker compose logs --tail=150
    }
    else {
        Write-Host "Últimos logs de '$Service':" -ForegroundColor Yellow
        & docker compose logs --tail=150 $Service
    }
}

function Test-ContainerExists {
    param([string]$Container)

    & docker inspect $Container --format "{{.Id}}" 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

function Remove-ProjectRuntimeState {
    param([string]$ComposeProjectName)

    Write-Step "Preparando el estado de ejecución de Docker Compose"

    Write-Host "Se retirarán únicamente contenedores y redes del proyecto."
    Write-Host "Los volúmenes y datos se conservarán." -ForegroundColor Green

    & docker compose down --remove-orphans

    if ($LASTEXITCODE -eq 0) {
        Write-Host "Estado anterior retirado correctamente." -ForegroundColor Green
        return
    }

    Write-Warning "Compose no pudo retirar completamente el estado anterior."
    Write-Host "Aplicando limpieza controlada por etiquetas del proyecto."

    $containers = @(
        & docker ps -a `
            --filter "label=com.docker.compose.project=$ComposeProjectName" `
            --format "{{.Names}}"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($container in $containers) {
        Write-Host "Eliminando contenedor: $container"
        & docker rm -f $container

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "No se pudo eliminar '$container'."
        }
    }

    $networks = @(
        & docker network ls `
            --filter "label=com.docker.compose.project=$ComposeProjectName" `
            --format "{{.Name}}"
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($network in $networks) {
        Write-Host "Eliminando red: $network"
        & docker network rm $network

        if ($LASTEXITCODE -ne 0) {
            Write-Warning "No se pudo eliminar la red '$network'."
        }
    }

    & docker compose down --remove-orphans

    if ($LASTEXITCODE -ne 0) {
        Show-Diagnostics
        throw "No fue posible reparar el estado de ejecución de Docker Compose."
    }

    Write-Host "Estado de ejecución reparado correctamente." -ForegroundColor Green
}

function Wait-ContainerHealthy {
    param(
        [string]$Service,
        [string]$Container,
        [int]$Timeout
    )

    $elapsed = 0

    while ($elapsed -lt $Timeout) {
        if (-not (Test-ContainerExists -Container $Container)) {
            Write-Host "$Container -> todavía no fue creado; esperando..."
            Start-Sleep -Seconds 3
            $elapsed += 3
            continue
        }

        $status = (
            & docker inspect $Container `
                --format "{{.State.Status}}" 2>$null
        ).Trim()

        if ($LASTEXITCODE -ne 0) {
            Show-Diagnostics -Service $Service
            throw "No se pudo consultar el estado de '$Container'."
        }

        $health = (
            & docker inspect $Container `
                --format "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}" `
                2>$null
        ).Trim()

        if ($LASTEXITCODE -ne 0) {
            Show-Diagnostics -Service $Service
            throw "No se pudo consultar el healthcheck de '$Container'."
        }

        Write-Host (
            "{0,-40} status={1,-10} health={2,-10} tiempo={3}s" -f `
                $Container,
                $status,
                $health,
                $elapsed
        )

        if ($status -eq "running" -and $health -in @("healthy", "none")) {
            Write-Host "$Container está disponible." -ForegroundColor Green
            return
        }

        if ($status -in @("exited", "dead", "removing") -or $health -eq "unhealthy") {
            Show-Diagnostics -Service $Service
            throw "El contenedor '$Container' no pudo iniciar correctamente."
        }

        Start-Sleep -Seconds 3
        $elapsed += 3
    }

    Show-Diagnostics -Service $Service
    throw "Timeout esperando '$Container' después de $Timeout segundos."
}

function Start-ComposeService {
    param(
        [string]$Service,
        [string]$Container,
        [int]$Timeout
    )

    Write-Step "Iniciando servicio: $Service"

    & docker compose up -d $Service

    if ($LASTEXITCODE -ne 0) {
        Show-Diagnostics -Service $Service
        throw "Docker Compose no pudo crear o iniciar '$Service'."
    }

    Wait-ContainerHealthy `
        -Service $Service `
        -Container $Container `
        -Timeout $Timeout
}

Assert-Command -Name "docker"

Write-Step "Validando Docker y Docker Compose"

& docker info 1>$null

if ($LASTEXITCODE -ne 0) {
    throw "Docker Desktop no está disponible."
}

& docker compose version

if ($LASTEXITCODE -ne 0) {
    throw "Docker Compose no está disponible."
}

Write-Step "Validando compose.yaml"

& docker compose config --quiet

if ($LASTEXITCODE -ne 0) {
    throw "compose.yaml contiene errores."
}

$requiredServices = @(
    "postgres-monolith",
    "postgres-notifications",
    "postgres-transfers",
    "rabbitmq",
    "notifications-service",
    "monolith",
    "transfers-service",
    "gateway"
)

$configuredServices = @(& docker compose config --services)

foreach ($service in $requiredServices) {
    if ($configuredServices -notcontains $service) {
        throw "El servicio requerido '$service' no existe en compose.yaml."
    }
}

if ($RepairRuntimeState) {
    Remove-ProjectRuntimeState -ComposeProjectName $ProjectName
}
else {
    Write-Warning "Se omitió la reparación del estado anterior."
}

$services = @(
    @{ Number = "1/8"; Service = "postgres-monolith";       Container = "finbank-postgres-monolith" },
    @{ Number = "2/8"; Service = "postgres-notifications";  Container = "finbank-postgres-notifications" },
    @{ Number = "3/8"; Service = "postgres-transfers";      Container = "finbank-postgres-transfers" },
    @{ Number = "4/8"; Service = "rabbitmq";                Container = "finbank-rabbitmq" },
    @{ Number = "5/8"; Service = "notifications-service";   Container = "finbank-notifications-service" },
    @{ Number = "6/8"; Service = "monolith";                Container = "finbank-monolith" },
    @{ Number = "7/8"; Service = "transfers-service";       Container = "finbank-transfers-service" },
    @{ Number = "8/8"; Service = "gateway";                 Container = "finbank-gateway" }
)

foreach ($item in $services) {
    Write-Host ""
    Write-Host "$($item.Number) $($item.Service)" -ForegroundColor Magenta

    Start-ComposeService `
        -Service $item.Service `
        -Container $item.Container `
        -Timeout $TimeoutSeconds
}

Write-Step "Estado final de la plataforma"

& docker compose ps

if ($LASTEXITCODE -ne 0) {
    throw "No fue posible consultar el estado final."
}

Write-Host ""
Write-Host "ADR-002 levantado correctamente." -ForegroundColor Green
Write-Host "Gateway:     http://localhost:8080"
Write-Host "RabbitMQ UI: http://localhost:15672"
Write-Host ""
Write-Host "Siguiente paso:"
Write-Host "  .\scripts\windows\11-validar-adr002.ps1" -ForegroundColor Cyan
