$ErrorActionPreference = "Stop"

Write-Host "=== Verificación de PLATAFORMA_BASE_FINBANK ===" -ForegroundColor Cyan

$commands = @("git", "docker")
foreach ($command in $commands) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "No se encontró '$command' en PATH. Instálelo y abra una nueva terminal."
    }
}

Write-Host "`nGit:" -ForegroundColor Yellow
git --version
if ($LASTEXITCODE -ne 0) { throw "Git no respondió correctamente." }

Write-Host "`nDocker CLI:" -ForegroundColor Yellow
docker --version
if ($LASTEXITCODE -ne 0) { throw "Docker CLI no respondió correctamente." }

Write-Host "`nDocker Compose:" -ForegroundColor Yellow
docker compose version
if ($LASTEXITCODE -ne 0) { throw "Docker Compose no respondió correctamente." }

Write-Host "`nDocker Engine:" -ForegroundColor Yellow
$osType = docker info --format '{{.OSType}}'
if ($LASTEXITCODE -ne 0) { throw "Docker Engine no está disponible. Inicie Docker Desktop." }
if ($osType.Trim() -ne "linux") {
    throw "Docker Desktop no está usando contenedores Linux. Cambie a Linux containers."
}
Write-Host "OSType: $osType"

Write-Host "`nWSL:" -ForegroundColor Yellow
wsl --status
if ($LASTEXITCODE -ne 0) { throw "WSL no está disponible o no está configurado. Ejecute wsl --install y reinicie Windows." }

Write-Host "`nEntorno correcto para continuar." -ForegroundColor Green
