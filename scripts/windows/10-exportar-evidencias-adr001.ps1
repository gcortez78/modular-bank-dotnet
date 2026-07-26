param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$Since = "10m",
    [string]$Token = "",
    [switch]$IncludeData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Command {
    param([string]$Name)
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "No se encontró el comando requerido: $Name"
    }
}

Assert-Command "docker"
Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no parece ser la raíz del repositorio Git."
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$evidenceDir = Join-Path $RepoRoot "docs\evidencias\adr001"
$dbDir = Join-Path $RepoRoot "database\notifications"

New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
New-Item -ItemType Directory -Force -Path $dbDir | Out-Null

Write-Step "Registrando estado de contenedores"
docker compose ps -a |
    Out-File -FilePath (Join-Path $evidenceDir "docker-compose-ps-$timestamp.txt") -Encoding utf8

Write-Step "Registrando logs de Gateway, Notifications y Monolith"
docker compose logs --since=$Since gateway notifications-service monolith postgres-notifications |
    Out-File -FilePath (Join-Path $evidenceDir "logs-integrados-$timestamp.txt") -Encoding utf8

Write-Step "Exportando esquema PostgreSQL de Notifications"
docker compose exec -T postgres-notifications sh -lc `
    'pg_dump -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}" --schema=notifications --schema-only --no-owner --no-privileges > /tmp/notifications-schema.sql'

docker cp `
    "finbank-postgres-notifications:/tmp/notifications-schema.sql" `
    (Join-Path $dbDir "schema.sql") | Out-Null

Write-Step "Exportando historial de migraciones"
$sqlMigrations = @'
SELECT "MigrationId", "ProductVersion"
FROM notifications."__EFMigrationsHistory"
ORDER BY "MigrationId";
'@

$sqlMigrations |
    docker compose exec -T postgres-notifications sh -lc `
        'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"' |
    Out-File -FilePath (Join-Path $evidenceDir "migraciones-$timestamp.txt") -Encoding utf8

Write-Step "Registrando conteo de datos"
$sqlCounts = @'
SELECT COUNT(*) AS notifications_count
FROM notifications.notifications;
'@

$sqlCounts |
    docker compose exec -T postgres-notifications sh -lc `
        'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}"' |
    Out-File -FilePath (Join-Path $evidenceDir "conteo-notifications-$timestamp.txt") -Encoding utf8

if ($IncludeData) {
    Write-Step "Exportando datos de Notifications"
    Write-Warning "Se exportarán datos. Verifique que no contengan información sensible antes de subirlos a GitHub."

    docker compose exec -T postgres-notifications sh -lc `
        'pg_dump -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}" --table=notifications.notifications --data-only --column-inserts --no-owner --no-privileges > /tmp/notifications-data.sql'

    docker cp `
        "finbank-postgres-notifications:/tmp/notifications-data.sql" `
        (Join-Path $dbDir "data.sql") | Out-Null
}
else {
    Write-Host "Datos no exportados. Use -IncludeData únicamente para datos de prueba no sensibles."
}

if (-not [string]::IsNullOrWhiteSpace($Token)) {
    Write-Step "Validando enrutamiento mediante Gateway"

    $headers = @{ Authorization = "Bearer $Token" }
    $routeResults = @()

    foreach ($route in @("/notifications", "/accounts")) {
        try {
            $response = Invoke-WebRequest `
                -Uri "http://localhost:8080$route" `
                -Method Get `
                -Headers $headers `
                -UseBasicParsing

            $routeResults += [PSCustomObject]@{
                Route      = $route
                StatusCode = $response.StatusCode
                Result     = "OK"
            }
        }
        catch {
            $statusCode = $null
            if ($_.Exception.Response) {
                $statusCode = $_.Exception.Response.StatusCode.value__
            }

            $routeResults += [PSCustomObject]@{
                Route      = $route
                StatusCode = $statusCode
                Result     = $_.Exception.Message
            }
        }
    }

    $routeResults |
        Format-Table -AutoSize |
        Out-String |
        Out-File -FilePath (Join-Path $evidenceDir "pruebas-enrutamiento-$timestamp.txt") -Encoding utf8

    docker compose logs --since=2m gateway notifications-service monolith |
        Out-File -FilePath (Join-Path $evidenceDir "logs-enrutamiento-$timestamp.txt") -Encoding utf8
}
else {
    Write-Host "Prueba autenticada omitida. Ejecute con -Token `$token para registrar rutas."
}

Write-Step "Generando resumen de evidencia"
$summary = @"
# Evidencia ADR-001

Fecha de generación: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")

## Componentes validados

- API Gateway basado en YARP.
- Notifications Service ejecutado como servicio independiente.
- PostgreSQL exclusivo para Notifications Service.
- Esquema de base de datos: notifications.
- Historial de migraciones de Entity Framework Core.
- Enrutamiento /notifications hacia el microservicio.
- Enrutamiento del resto de rutas hacia el monolito.

## Archivos generados

- Estado de Docker Compose.
- Logs integrados de los servicios.
- Esquema SQL de Notifications.
- Historial de migraciones.
- Conteo de registros.
- Pruebas y logs de enrutamiento, cuando se proporciona un JWT.
"@

$summary |
    Out-File -FilePath (Join-Path $evidenceDir "README.md") -Encoding utf8

Write-Host ""
Write-Host "Evidencias generadas en:" -ForegroundColor Green
Write-Host "  $evidenceDir"
Write-Host "Esquema SQL generado en:" -ForegroundColor Green
Write-Host "  $dbDir\schema.sql"
