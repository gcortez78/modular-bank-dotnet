param(
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no corresponde a la raíz del repositorio."
}

$repoForDocker = (Resolve-Path $RepoRoot).Path.Replace("\", "/")
$logPath = Join-Path $RepoRoot "adr002-saga-monolith-build.log"

Write-Step "Ejecutando compilación diagnóstica del monolito"

$command = @'
set -o pipefail
dotnet restore src/ModularBank/ModularBank.csproj --disable-parallel
dotnet build src/ModularBank/ModularBank.csproj \
  --configuration Release \
  --no-restore \
  --verbosity minimal \
  /p:UseAppHost=false 2>&1
'@

$previousPreference = $ErrorActionPreference

try {
    $ErrorActionPreference = "Continue"

    & docker run --rm `
        -e NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT=10 `
        -e NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS=2000 `
        -v "${repoForDocker}:/workspace" `
        -v "finbank_nuget_cache:/root/.nuget/packages" `
        -w /workspace `
        mcr.microsoft.com/dotnet/sdk:10.0 `
        bash -lc $command 2>&1 |
    Tee-Object -FilePath $logPath |
    ForEach-Object {
        Write-Host $_
    }

    $exitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $previousPreference
}

Write-Step "Errores de compilación encontrados"

$errors = @(
    Get-Content $logPath |
    Select-String `
        -Pattern `
            'error CS[0-9]+:',
            'error NU[0-9]+:',
            'error :'
)

if ($errors.Count -eq 0) {
    Write-Host "No se encontraron líneas de error CS/NU." -ForegroundColor Yellow
}
else {
    $errors |
        ForEach-Object {
            Write-Host $_.Line -ForegroundColor Red
        }
}

Write-Step "Verificaciones de instalación Saga"

$checks = @(
    @{
        Name = "Registro AddSagaMessaging"
        Path = "src\ModularBank\Program.cs"
        Pattern = "AddSagaMessaging"
    },
    @{
        Name = "Contratos del monolito"
        Path = "src\ModularBank\Messaging\Contracts\TransferRequestedIntegrationEvent.cs"
        Pattern = "TransferRequestedIntegrationEvent"
    },
    @{
        Name = "Consumidor Accounts"
        Path = "src\ModularBank\Messaging\AccountsTransferRequestedConsumer.cs"
        Pattern = "AccountsTransferRequestedConsumer"
    },
    @{
        Name = "Consumidor Audit"
        Path = "src\ModularBank\Messaging\AuditTransferResultConsumer.cs"
        Pattern = "AuditTransferResultConsumer"
    },
    @{
        Name = "RabbitMQ.Client"
        Path = "src\ModularBank\ModularBank.csproj"
        Pattern = "RabbitMQ.Client"
    }
)

foreach ($check in $checks) {
    $path = Join-Path $RepoRoot $check.Path

    $ok = (
        (Test-Path $path) -and
        (Select-String -Path $path -Pattern $check.Pattern -Quiet)
    )

    $status = if ($ok) { "OK" } else { "FALTA" }
    $color = if ($ok) { "Green" } else { "Red" }

    Write-Host ("{0,-32} {1}" -f $check.Name, $status) -ForegroundColor $color
}

Write-Step "Ubicación del diagnóstico"

Write-Host $logPath -ForegroundColor Green

if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "La compilación falló. Use las líneas CSxxxx mostradas arriba para corregir el código." `
        -ForegroundColor Yellow
    exit $exitCode
}

Write-Host ""
Write-Host "La compilación terminó correctamente." -ForegroundColor Green
