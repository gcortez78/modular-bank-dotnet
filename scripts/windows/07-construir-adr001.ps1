$ErrorActionPreference = "Stop"

Write-Host "=== Build ADR-001: extracción de Notifications ===" -ForegroundColor Cyan

if (-not (Test-Path ".env")) {
    throw "No existe .env. Ejecute: Copy-Item .env.example .env"
}

Write-Host "`n1. Validando compose.yaml" -ForegroundColor Yellow
docker compose config --quiet
if ($LASTEXITCODE -ne 0) {
    throw "compose.yaml no es válido."
}

Write-Host "`n2. Construyendo Notifications Service" -ForegroundColor Yellow
docker compose build notifications-service
if ($LASTEXITCODE -ne 0) {
    throw "Falló el build de notifications-service."
}

Write-Host "`n3. Reconstruyendo monolito remanente" -ForegroundColor Yellow
docker compose build monolith
if ($LASTEXITCODE -ne 0) {
    throw "Falló el build del monolito."
}

Write-Host "`n4. Reconstruyendo Gateway YARP" -ForegroundColor Yellow
docker compose build gateway
if ($LASTEXITCODE -ne 0) {
    throw "Falló el build del gateway."
}

Write-Host "`nImágenes generadas:" -ForegroundColor Green
docker images --format "table {{.Repository}}`t{{.Tag}}`t{{.ID}}`t{{.Size}}" |
    Select-String "plataforma-base-finbank"
