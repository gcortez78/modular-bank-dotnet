$ErrorActionPreference = "Stop"

$images = @(
    "postgres:16-alpine",
    "rabbitmq:4.3.2-management-alpine",
    "mcr.microsoft.com/dotnet/sdk:10.0",
    "mcr.microsoft.com/dotnet/aspnet:10.0"
)

Write-Host "=== Descarga individual de imágenes base ===" -ForegroundColor Cyan

foreach ($image in $images) {
    Write-Host "`nDescargando: $image" -ForegroundColor Yellow
    docker pull $image
    if ($LASTEXITCODE -ne 0) {
        throw "Falló la descarga de $image"
    }
    Write-Host "Descarga correcta: $image" -ForegroundColor Green
}

Write-Host "`nImágenes disponibles:" -ForegroundColor Cyan
docker images --format "table {{.Repository}}`t{{.Tag}}`t{{.ID}}`t{{.Size}}"
