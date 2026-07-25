param(
    [string]$RepoRoot = "C:\FinBank\PLATAFORMA_BASE_FINBANK",
    [string]$KitRoot = "C:\FinBank\ADR002_SAGA_CHOREOGRAPHY_KIT"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptVersion = "ADR002-SAGA-CONTINUE-V1-20260725"

function Write-Step {
    param([string]$Message)

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor DarkGray
    Write-Host $Message -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor DarkGray
}

Write-Host "Versión del script: $ScriptVersion" -ForegroundColor Green
Write-Host "Repositorio: $RepoRoot"
Write-Host "Kit: $KitRoot"

if (-not (Test-Path $RepoRoot)) {
    throw "No existe el repositorio: $RepoRoot"
}

if (-not (Test-Path (Join-Path $RepoRoot ".git"))) {
    throw "La ruta no corresponde a un repositorio Git: $RepoRoot"
}

if (-not (Test-Path $KitRoot)) {
    throw "No existe la carpeta del kit: $KitRoot"
}

$kitFilesRoot = Join-Path $KitRoot "archivos_para_repositorio"

if (-not (Test-Path $kitFilesRoot)) {
    throw "No se encontró archivos_para_repositorio dentro del kit."
}

Set-Location $RepoRoot

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $RepoRoot ".adr002-saga-backup\continuacion-$timestamp"

Write-Step "Respaldando archivos que serán modificados"

$filesToBackup = @(
    "src\ModularBank\Program.cs",
    "src\ModularBank\ModularBank.csproj"
)

foreach ($relativePath in $filesToBackup) {
    $sourcePath = Join-Path $RepoRoot $relativePath

    if (Test-Path $sourcePath) {
        $destinationPath = Join-Path $backupRoot $relativePath
        $destinationDirectory = Split-Path $destinationPath -Parent

        New-Item `
            -ItemType Directory `
            -Force `
            -Path $destinationDirectory |
        Out-Null

        Copy-Item `
            -LiteralPath $sourcePath `
            -Destination $destinationPath `
            -Force
    }
}

Write-Host "Respaldo creado en:" -ForegroundColor Green
Write-Host "  $backupRoot"

Write-Step "Completando el registro de Saga Messaging en Program.cs"

$programPath = Join-Path $RepoRoot "src\ModularBank\Program.cs"

if (-not (Test-Path $programPath)) {
    throw "No se encontró Program.cs: $programPath"
}

$program = Get-Content $programPath -Raw

# Agregar namespace una sola vez.
if ($program -notmatch '(?m)^[ \t]*using[ \t]+ModularBank\.Messaging[ \t]*;') {
    $program = "using ModularBank.Messaging;`r`n" + $program
}

# Insertar el registro antes de builder.Build(), sin depender de una firma
# específica de AddAuditModule.
if ($program -notmatch 'builder\.Services\.AddSagaMessaging\s*\(') {
    $buildPattern = '(?m)^([ \t]*var[ \t]+app[ \t]*=[ \t]*builder\.Build\s*\(\s*\)\s*;[ \t]*)$'

    if ($program -notmatch $buildPattern) {
        throw @"
No se encontró la línea:
var app = builder.Build();

No se modificará Program.cs automáticamente.
"@
    }

    $program = [regex]::Replace(
        $program,
        $buildPattern,
        'builder.Services.AddSagaMessaging(builder.Configuration);' +
        "`r`n`r`n" +
        '$1',
        1
    )
}

# La Saga reemplaza la API HTTP interna utilizada en la versión híbrida.
$program = [regex]::Replace(
    $program,
    '(?m)^[ \t]*app\.MapInternalTransfersEndpoints\s*\(\s*\)\s*;[ \t]*\r?\n',
    ''
)

Set-Content `
    -Path $programPath `
    -Value $program `
    -Encoding UTF8

Write-Host "Program.cs actualizado." -ForegroundColor Green

Write-Step "Agregando RabbitMQ.Client al proyecto del monolito"

$projectPath = Join-Path $RepoRoot "src\ModularBank\ModularBank.csproj"

if (-not (Test-Path $projectPath)) {
    throw "No se encontró ModularBank.csproj: $projectPath"
}

$project = Get-Content $projectPath -Raw

if ($project -notmatch 'PackageReference\s+Include\s*=\s*"RabbitMQ\.Client"') {
    $newItemGroup = @'
  <ItemGroup>
    <PackageReference Include="RabbitMQ.Client" Version="7.2.1" />
  </ItemGroup>
'@

    if ($project -notmatch '</Project>') {
        throw "ModularBank.csproj no contiene la etiqueta </Project>."
    }

    $project = $project.Replace(
        '</Project>',
        $newItemGroup + "`r`n</Project>"
    )

    Set-Content `
        -Path $projectPath `
        -Value $project `
        -Encoding UTF8

    Write-Host "RabbitMQ.Client 7.2.1 agregado." -ForegroundColor Green
}
else {
    Write-Host "RabbitMQ.Client ya estaba registrado." -ForegroundColor Yellow
}

Write-Step "Copiando scripts operativos 20, 21 y 22"

$scriptsSource = Join-Path $kitFilesRoot "scripts\windows"
$scriptsTarget = Join-Path $RepoRoot "scripts\windows"

if (-not (Test-Path $scriptsSource)) {
    throw "No se encontró la carpeta de scripts del kit: $scriptsSource"
}

New-Item `
    -ItemType Directory `
    -Force `
    -Path $scriptsTarget |
Out-Null

$requiredScripts = @(
    "20-generar-migracion-saga-accounts.ps1",
    "21-construir-levantar-saga-adr002.ps1",
    "22-validar-saga-adr002.ps1"
)

foreach ($scriptName in $requiredScripts) {
    $sourceScript = Join-Path $scriptsSource $scriptName
    $targetScript = Join-Path $scriptsTarget $scriptName

    if (-not (Test-Path $sourceScript)) {
        throw "No se encontró el script requerido en el kit: $sourceScript"
    }

    Copy-Item `
        -LiteralPath $sourceScript `
        -Destination $targetScript `
        -Force

    Write-Host "Copiado: $scriptName"
}

Write-Step "Validando la instalación parcial completada"

$validationErrors = New-Object System.Collections.Generic.List[string]

$requiredFiles = @(
    "src\TransfersService\Application\TransfersApplicationService.cs",
    "src\TransfersService\Messaging\AccountTransferResultConsumer.cs",
    "src\ModularBank\Messaging\AccountsTransferRequestedConsumer.cs",
    "src\ModularBank\Messaging\AccountsOutboxPublisher.cs",
    "src\ModularBank\Messaging\AuditTransferResultConsumer.cs",
    "src\ModularBank\Messaging\SagaMessagingExtensions.cs",
    "src\ModularBank\Modules\Accounts\Domain\AccountOutboxMessage.cs",
    "scripts\windows\20-generar-migracion-saga-accounts.ps1",
    "scripts\windows\21-construir-levantar-saga-adr002.ps1",
    "scripts\windows\22-validar-saga-adr002.ps1"
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path (Join-Path $RepoRoot $relativePath))) {
        $validationErrors.Add("Falta el archivo: $relativePath")
    }
}

$programAfter = Get-Content $programPath -Raw
$projectAfter = Get-Content $projectPath -Raw

if ($programAfter -notmatch '(?m)^[ \t]*using[ \t]+ModularBank\.Messaging[ \t]*;') {
    $validationErrors.Add("Program.cs no contiene using ModularBank.Messaging;")
}

if ($programAfter -notmatch 'builder\.Services\.AddSagaMessaging\s*\(\s*builder\.Configuration\s*\)') {
    $validationErrors.Add(
        "Program.cs no contiene builder.Services.AddSagaMessaging(builder.Configuration);"
    )
}

if ($programAfter -match 'app\.MapInternalTransfersEndpoints\s*\(') {
    $validationErrors.Add(
        "Program.cs todavía contiene app.MapInternalTransfersEndpoints();"
    )
}

if ($projectAfter -notmatch 'PackageReference\s+Include\s*=\s*"RabbitMQ\.Client"') {
    $validationErrors.Add(
        "ModularBank.csproj no contiene RabbitMQ.Client."
    )
}

$registrationCount = (
    [regex]::Matches(
        $programAfter,
        'builder\.Services\.AddSagaMessaging\s*\('
    )
).Count

if ($registrationCount -ne 1) {
    $validationErrors.Add(
        "AddSagaMessaging debe aparecer una sola vez; se encontraron $registrationCount."
    )
}

if ($validationErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "Se encontraron problemas:" -ForegroundColor Red

    foreach ($validationError in $validationErrors) {
        Write-Host "  - $validationError" -ForegroundColor Red
    }

    throw "La continuación de instalación no superó la validación."
}

Write-Host ""
Write-Host "Instalación Saga completada correctamente." -ForegroundColor Green
Write-Host ""
Write-Host "Siguiente paso:" -ForegroundColor Cyan
Write-Host "  .\scripts\windows\20-generar-migracion-saga-accounts.ps1"
