$ErrorActionPreference = "Stop"

if (-not (Test-Path ".env")) {
    throw "No existe .env. Ejecute: Copy-Item .env.example .env"
}

Write-Host "=== Validación de Compose ===" -ForegroundColor Cyan
docker compose config --quiet
if ($LASTEXITCODE -ne 0) { throw "compose.yaml o .env no son válidos." }

Write-Host "`n=== Construcción del monolito ===" -ForegroundColor Cyan
docker compose build monolith
if ($LASTEXITCODE -ne 0) { throw "Falló la construcción de monolith." }

Write-Host "`n=== Construcción del Gateway ===" -ForegroundColor Cyan
docker compose build gateway
if ($LASTEXITCODE -ne 0) { throw "Falló la construcción de gateway." }

Write-Host "`nImágenes FinBank construidas:" -ForegroundColor Green
docker images --filter "reference=plataforma-base-finbank-*" --format "table {{.Repository}}`t{{.Tag}}`t{{.ID}}`t{{.Size}}"
