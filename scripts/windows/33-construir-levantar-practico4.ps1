param(
    [string]$RepoRoot = (Get-Location).Path,
    [int]$TimeoutSeconds = 240,
    [switch]$NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Text -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

function Invoke-Compose([string[]]$Arguments, [string]$Description) {
    & docker compose -f compose.yaml -f compose.practico4.yaml @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw ("Falló {0}. ExitCode={1}." -f $Description, $LASTEXITCODE)
    }
}

function Wait-Healthy([string]$Service, [int]$Timeout) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Timeout)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $id = (& docker compose -f compose.yaml -f compose.practico4.yaml ps -q $Service).Trim()
        if (-not [string]::IsNullOrWhiteSpace($id)) {
            $status = (& docker inspect --format '{{.State.Status}}' $id).Trim()
            $health = (& docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' $id).Trim()
            Write-Host ("{0}: status={1}; health={2}" -f $Service, $status, $health)
            if ($status -eq "running" -and $health -in @("healthy", "none")) { return }
        }
        Start-Sleep -Seconds 4
    }
    throw ("{0} no quedó saludable en {1} segundos." -f $Service, $Timeout)
}

Set-Location $RepoRoot

& ".\scripts\windows\32-validar-estatico-practico4.ps1" -RepoRoot $RepoRoot

Write-Step "Validando Compose combinado"
Invoke-Compose -Arguments @("config", "--quiet") -Description "docker compose config"

Write-Step "Construyendo servicios"
$buildArgs = @("build")
if ($NoCache) { $buildArgs += "--no-cache" }
$buildArgs += @("monolith", "transfers-service", "notifications-service")
Invoke-Compose -Arguments $buildArgs -Description "build de servicios"

Write-Step "Levantando plataforma"
Invoke-Compose -Arguments @("up", "-d", "--force-recreate") -Description "levantamiento"

Write-Step "Esperando servicios saludables"
foreach ($service in @(
    "postgres-monolith",
    "postgres-transfers",
    "postgres-notifications",
    "rabbitmq",
    "monolith",
    "transfers-service",
    "notifications-service",
    "gateway"
)) {
    Wait-Healthy -Service $service -Timeout $TimeoutSeconds
}

Write-Step "Estado final"
Invoke-Compose -Arguments @("ps", "-a") -Description "consulta de estado"
Write-Host "Plataforma lista para las pruebas 34 a 38." -ForegroundColor Green
