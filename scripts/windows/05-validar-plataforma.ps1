$ErrorActionPreference = "Stop"

Write-Host "=== Validación funcional de PLATAFORMA_BASE_FINBANK ===" -ForegroundColor Cyan

Write-Host "`n1. Estado de contenedores" -ForegroundColor Yellow
docker compose ps
if ($LASTEXITCODE -ne 0) { throw "No fue posible consultar Compose." }

Write-Host "`n2. Health del Gateway" -ForegroundColor Yellow
$health = Invoke-RestMethod -Method Get -Uri "http://localhost:8080/health" -TimeoutSec 15
if ($health -ne "Healthy" -and $health -ne "ok") {
    throw "Respuesta inesperada del health: $health"
}
Write-Host "Gateway OK: $health" -ForegroundColor Green

Write-Host "`n3. Consola RabbitMQ" -ForegroundColor Yellow
$rabbit = Invoke-WebRequest -UseBasicParsing -Uri "http://localhost:15672" -TimeoutSec 15
if ($rabbit.StatusCode -ne 200) { throw "RabbitMQ Management respondió $($rabbit.StatusCode)." }
Write-Host "RabbitMQ Management OK: HTTP $($rabbit.StatusCode)" -ForegroundColor Green

Write-Host "`n4. Schemas PostgreSQL creados por EF Core" -ForegroundColor Yellow
$dbUser = (docker compose exec -T postgres-monolith printenv POSTGRES_USER).Trim()
$dbName = (docker compose exec -T postgres-monolith printenv POSTGRES_DB).Trim()
$sql = "SELECT schema_name FROM information_schema.schemata WHERE schema_name IN ('auth','accounts','transfers','notifications','audit') ORDER BY schema_name;"
docker compose exec -T postgres-monolith psql -U $dbUser -d $dbName -c $sql
if ($LASTEXITCODE -ne 0) { throw "No se pudieron consultar los schemas." }

Write-Host "`n5. Prueba del Gateway hacia /auth/register" -ForegroundColor Yellow
$email = "finbank.$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())@example.com"
$body = @{
    email = $email
    password = "FinBank123!"
    name = "Usuario de prueba"
} | ConvertTo-Json

$response = Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:8080/auth/register" `
    -ContentType "application/json" `
    -Body $body `
    -TimeoutSec 30

if ([string]::IsNullOrWhiteSpace($response.accessToken)) {
    throw "El registro no devolvió accessToken."
}
Write-Host "Registro correcto por el Gateway: $email" -ForegroundColor Green

Write-Host "`nPLATAFORMA_BASE_FINBANK validada satisfactoriamente." -ForegroundColor Green
