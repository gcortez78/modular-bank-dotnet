param(
    [switch]$EliminarDatos
)

$ErrorActionPreference = "Stop"

if ($EliminarDatos) {
    Write-Warning "Se eliminarán contenedores, redes y volúmenes. Se perderán los datos locales."
    docker compose down --volumes --remove-orphans
}
else {
    docker compose down --remove-orphans
}

if ($LASTEXITCODE -ne 0) { throw "No fue posible detener la plataforma." }
Write-Host "Plataforma detenida." -ForegroundColor Green
