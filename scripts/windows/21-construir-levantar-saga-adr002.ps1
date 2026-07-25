param(
    [string]$RepoRoot = (Get-Location).Path,
    [switch]$NoCache
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-Docker {
    param(
        [string]$Description,
        [string[]]$DockerArguments
    )

    $previousPreference = $ErrorActionPreference

    try {
        $ErrorActionPreference = "Continue"

        & docker @DockerArguments 2>&1 |
            ForEach-Object { Write-Host $_ }

        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }

    if ($exitCode -ne 0) {
        throw "Falló: $Description. ExitCode=$exitCode"
    }
}

Set-Location $RepoRoot

$buildArguments = @(
    "compose",
    "--progress",
    "plain",
    "build"
)

if ($NoCache) {
    $buildArguments += "--no-cache"
}

$buildArguments += @(
    "monolith",
    "transfers-service",
    "notifications-service",
    "gateway"
)

Invoke-Docker `
    -Description "construcción de servicios Saga" `
    -DockerArguments $buildArguments

Invoke-Docker `
    -Description "inicio de la plataforma Saga" `
    -DockerArguments @(
        "compose",
        "up",
        "-d",
        "--force-recreate",
        "postgres-monolith",
        "postgres-notifications",
        "postgres-transfers",
        "rabbitmq",
        "notifications-service",
        "monolith",
        "transfers-service",
        "gateway"
    )

Start-Sleep -Seconds 15

Invoke-Docker `
    -Description "estado de la plataforma" `
    -DockerArguments @(
        "compose",
        "ps",
        "-a"
    )

Write-Host ""
Write-Host "Revisar logs clave:" -ForegroundColor Cyan
Write-Host "  docker compose logs --since=5m --no-color monolith transfers-service rabbitmq"
