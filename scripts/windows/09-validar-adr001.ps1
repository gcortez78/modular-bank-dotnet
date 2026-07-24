$ErrorActionPreference = "Stop"

function Get-DotEnvValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [string]$DefaultValue = ""
    )

    if (-not (Test-Path ".env")) {
        return $DefaultValue
    }

    $line = Get-Content ".env" |
        Where-Object { $_ -match "^\s*$([regex]::Escape($Name))\s*=" } |
        Select-Object -Last 1

    if (-not $line) {
        return $DefaultValue
    }

    return (($line -split "=", 2)[1]).Trim().Trim('"')
}

function Decode-JwtPayload {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    $parts = $Token.Split(".")
    if ($parts.Count -ne 3) {
        throw "El accessToken no tiene formato JWT."
    }

    $payload = $parts[1].Replace("-", "+").Replace("_", "/")

    switch ($payload.Length % 4) {
        0 { }
        2 { $payload += "==" }
        3 { $payload += "=" }
        default { throw "Payload JWT Base64Url inválido." }
    }

    $json = [Text.Encoding]::UTF8.GetString(
        [Convert]::FromBase64String($payload))

    return $json | ConvertFrom-Json
}

Write-Host "=== Validación funcional ADR-001 ===" -ForegroundColor Cyan

$gatewayPort = Get-DotEnvValue -Name "GATEWAY_PORT" -DefaultValue "8080"
$gatewayUrl = "http://localhost:$gatewayPort"
$internalKey = Get-DotEnvValue -Name "NOTIFICATIONS_INTERNAL_API_KEY"

if ([string]::IsNullOrWhiteSpace($internalKey)) {
    throw "NOTIFICATIONS_INTERNAL_API_KEY no está definida en .env."
}

Write-Host "`n1. Estado de contenedores" -ForegroundColor Yellow
docker compose ps
if ($LASTEXITCODE -ne 0) {
    throw "No fue posible consultar Docker Compose."
}

$requiredServices = @(
    "postgres-monolith",
    "postgres-notifications",
    "rabbitmq",
    "notifications-service",
    "monolith",
    "gateway"
)

foreach ($service in $requiredServices) {
    $containerId = (docker compose ps -q $service).Trim()
    if (-not $containerId) {
        throw "El servicio $service no está iniciado."
    }

    $status = (docker inspect `
        --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' `
        $containerId).Trim()

    if ($status -ne "healthy" -and $status -ne "running") {
        throw "El servicio $service tiene estado $status."
    }
}

Write-Host "`n2. Health del Gateway" -ForegroundColor Yellow
$gatewayHealth = Invoke-RestMethod `
    -Method Get `
    -Uri "$gatewayUrl/health" `
    -TimeoutSec 15

Write-Host "Gateway OK: $gatewayHealth" -ForegroundColor Green

Write-Host "`n3. Health interno de Notifications Service" -ForegroundColor Yellow
$notificationHealth = docker compose exec -T notifications-service `
    curl -fsS http://localhost:8080/health

if ($LASTEXITCODE -ne 0) {
    throw "Notifications Service no respondió correctamente."
}

Write-Host "Notifications Service OK: $notificationHealth" -ForegroundColor Green

Write-Host "`n4. Registro de usuario por Gateway -> Monolito" -ForegroundColor Yellow
$email = "adr001.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())@example.com"

$registerBody = @{
    email = $email
    password = "FinBank123!"
    name = "Usuario ADR-001"
} | ConvertTo-Json

$registration = Invoke-RestMethod `
    -Method Post `
    -Uri "$gatewayUrl/auth/register" `
    -ContentType "application/json" `
    -Body $registerBody `
    -TimeoutSec 30

$accessToken = $registration.accessToken
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "El registro no devolvió accessToken."
}

Write-Host "Usuario registrado: $email" -ForegroundColor Green

$claims = Decode-JwtPayload -Token $accessToken
$userId = [string]$claims.sub
$parsedUserId = [Guid]::Empty

if (-not [Guid]::TryParse($userId, [ref]$parsedUserId)) {
    throw "No se pudo extraer un sub GUID desde el JWT."
}

$authHeaders = @{
    Authorization = "Bearer $accessToken"
}

Write-Host "`n5. Ruta /notifications por Gateway -> Microservicio" -ForegroundColor Yellow
$initialNotifications = Invoke-RestMethod `
    -Method Get `
    -Uri "$gatewayUrl/notifications" `
    -Headers $authHeaders `
    -TimeoutSec 15

Write-Host "Consulta inicial correcta. Registros: $(@($initialNotifications).Count)" `
    -ForegroundColor Green

Write-Host "`n6. Alta interna en la DB exclusiva del microservicio" -ForegroundColor Yellow
$idempotencyKey = "adr001-smoke-$([Guid]::NewGuid().ToString('N'))"

$createBody = @{
    userId = $userId
    type = 0
    payload = @{
        source = "ADR-001 smoke test"
        message = "Notificación creada en la base exclusiva"
    }
    idempotencyKey = $idempotencyKey
} | ConvertTo-Json -Depth 5 -Compress

# Windows PowerShell puede alterar la codificación al enviar JSON por stdin a un
# proceso nativo. Se transporta el cuerpo como Base64 ASCII y se reconstruye en
# UTF-8 dentro del contenedor antes de invocar la API interna.
$createBodyBase64 = [Convert]::ToBase64String(
    [Text.Encoding]::UTF8.GetBytes($createBody)
)

# 1) Reconstruir el JSON dentro del contenedor en un archivo temporal.
# Se evita encadenar base64 y curl en una sola instrucción sh -c, porque
# PowerShell/Docker Compose pueden alterar las comillas y separar los argumentos.
docker compose exec -T `
    -e "ADR001_BODY_B64=$createBodyBase64" `
    notifications-service `
    sh -c 'printf "%s" "$ADR001_BODY_B64" | base64 -d > /tmp/adr001-create-body.json'

if ($LASTEXITCODE -ne 0) {
    throw "No fue posible escribir el cuerpo JSON dentro del contenedor."
}

$bodySize = docker compose exec -T notifications-service `
    sh -c 'wc -c < /tmp/adr001-create-body.json'
$bodySize = ($bodySize | Out-String).Trim()

if ([string]::IsNullOrWhiteSpace($bodySize) -or [int]$bodySize -le 2) {
    throw "El cuerpo JSON temporal está vacío dentro del contenedor."
}

# 2) Ejecutar curl directamente, pasando cada argumento por separado.
# Esto conserva Content-Type, X-Internal-Api-Key y --data-binary.
$curlArguments = @(
    "compose",
    "exec",
    "-T",
    "notifications-service",
    "curl",
    "-sS",
    "-o",
    "/tmp/adr001-create-response.json",
    "-w",
    "%{http_code}",
    "-X",
    "POST",
    "http://localhost:8080/internal/notifications",
    "-H",
    "Content-Type: application/json",
    "-H",
    "X-Internal-Api-Key: $internalKey",
    "--data-binary",
    "@/tmp/adr001-create-body.json"
)

$createStatus = & docker @curlArguments
$curlExitCode = $LASTEXITCODE
$createStatus = ($createStatus | Out-String).Trim()

$responseBody = docker compose exec -T notifications-service `
    sh -c 'cat /tmp/adr001-create-response.json 2>/dev/null || true'
$responseBody = ($responseBody | Out-String).Trim()

if ($curlExitCode -ne 0 -or $createStatus -notin @("200", "201")) {
    throw "No fue posible crear la notificación interna. HTTP=$createStatus. Respuesta=$responseBody"
}

Write-Host "Alta interna correcta. HTTP $createStatus" -ForegroundColor Green
if (-not [string]::IsNullOrWhiteSpace($responseBody)) {
    Write-Host $responseBody
}

Write-Host "`n7. Lectura autenticada por el Gateway" -ForegroundColor Yellow
$finalNotifications = Invoke-RestMethod `
    -Method Get `
    -Uri "$gatewayUrl/notifications" `
    -Headers $authHeaders `
    -TimeoutSec 15

if (@($finalNotifications).Count -lt 1) {
    throw "La notificación creada no aparece en /notifications."
}

Write-Host "Notificaciones recuperadas: $(@($finalNotifications).Count)" `
    -ForegroundColor Green

Write-Host "`n8. Verificación de PostgreSQL exclusivo" -ForegroundColor Yellow
$dbUser = (docker compose exec -T postgres-notifications `
    printenv POSTGRES_USER).Trim()

$dbName = (docker compose exec -T postgres-notifications `
    printenv POSTGRES_DB).Trim()

$count = (docker compose exec -T postgres-notifications `
    psql -U $dbUser -d $dbName -tAc `
    "SELECT count(*) FROM notifications.notifications;").Trim()

if ($LASTEXITCODE -ne 0 -or [int]$count -lt 1) {
    throw "No se verificaron registros en PostgreSQL Notifications."
}

Write-Host "Registros en DB exclusiva: $count" -ForegroundColor Green

Write-Host "`n9. Comprobación de Database-per-Service" -ForegroundColor Yellow
$monolithDbUser = (docker compose exec -T postgres-monolith `
    printenv POSTGRES_USER).Trim()

$monolithDbName = (docker compose exec -T postgres-monolith `
    printenv POSTGRES_DB).Trim()

$monolithHasNewRecord = (docker compose exec -T postgres-monolith `
    psql -U $monolithDbUser -d $monolithDbName -tAc `
    "SELECT count(*) FROM notifications.notifications WHERE payload::text LIKE '%ADR-001 smoke test%';").Trim()

if ($LASTEXITCODE -ne 0) {
    throw "No se pudo consultar la tabla legacy del monolito."
}

if ([int]$monolithHasNewRecord -ne 0) {
    throw "La notificación nueva también apareció en la DB del monolito."
}

Write-Host "La nueva notificación existe solo en postgres-notifications." `
    -ForegroundColor Green

Write-Host "`nADR-001 validado satisfactoriamente." -ForegroundColor Green
