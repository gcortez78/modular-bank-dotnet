param(
    [string]$RepoRoot = "C:\FinBank\PLATAFORMA_BASE_FINBANK",
    [string]$KitRoot = "C:\FinBank\ADR002_SAGA_CHOREOGRAPHY_KIT"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScriptVersion = "ADR002-SAGA-CONTINUE-V2-20260725"

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

$programPath = Join-Path $RepoRoot "src\ModularBank\Program.cs"
$projectPath = Join-Path $RepoRoot "src\ModularBank\ModularBank.csproj"

if (-not (Test-Path $programPath)) {
    throw "No se encontró Program.cs: $programPath"
}

if (-not (Test-Path $projectPath)) {
    throw "No se encontró ModularBank.csproj: $projectPath"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupRoot = Join-Path $RepoRoot ".adr002-saga-backup\continuacion-v2-$timestamp"

Write-Step "Respaldando Program.cs y ModularBank.csproj"

foreach ($relativePath in @(
    "src\ModularBank\Program.cs",
    "src\ModularBank\ModularBank.csproj"
)) {
    $sourcePath = Join-Path $RepoRoot $relativePath
    $destinationPath = Join-Path $backupRoot $relativePath

    New-Item `
        -ItemType Directory `
        -Force `
        -Path (Split-Path $destinationPath -Parent) |
    Out-Null

    Copy-Item `
        -LiteralPath $sourcePath `
        -Destination $destinationPath `
        -Force
}

Write-Host "Respaldo creado en:" -ForegroundColor Green
Write-Host "  $backupRoot"

Write-Step "Analizando la estructura real de Program.cs"

$lines = [System.Collections.Generic.List[string]]::new()

foreach ($line in Get-Content $programPath) {
    $lines.Add($line)
}

Write-Host "Líneas con builder.Services:" -ForegroundColor Yellow

$serviceIndexes = @()

for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '\bbuilder\.Services\b') {
        $serviceIndexes += $index
        Write-Host ("  {0,4}: {1}" -f ($index + 1), $lines[$index])
    }
}

if ($serviceIndexes.Count -eq 0) {
    throw @"
Program.cs no contiene ningún registro builder.Services.
No es seguro insertar Saga Messaging automáticamente.
"@
}

Write-Host ""
Write-Host "Líneas relacionadas con Build():" -ForegroundColor Yellow

for ($index = 0; $index -lt $lines.Count; $index++) {
    if ($lines[$index] -match '\.Build\s*\(') {
        Write-Host ("  {0,4}: {1}" -f ($index + 1), $lines[$index])
    }
}

Write-Step "Insertando using ModularBank.Messaging"

$usingExists = $false

foreach ($line in $lines) {
    if ($line -match '^\s*using\s+ModularBank\.Messaging\s*;\s*$') {
        $usingExists = $true
        break
    }
}

if (-not $usingExists) {
    $lastUsingIndex = -1

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\s*using\s+[^;]+;\s*$') {
            $lastUsingIndex = $index
        }
    }

    if ($lastUsingIndex -ge 0) {
        $lines.Insert(
            $lastUsingIndex + 1,
            "using ModularBank.Messaging;"
        )
    }
    else {
        $lines.Insert(0, "using ModularBank.Messaging;")
    }

    Write-Host "Namespace agregado." -ForegroundColor Green
}
else {
    Write-Host "El namespace ya estaba registrado." -ForegroundColor Yellow
}

Write-Step "Insertando AddSagaMessaging después del último builder.Services"

$registrationExists = $false

foreach ($line in $lines) {
    if ($line -match 'builder\.Services\.AddSagaMessaging\s*\(') {
        $registrationExists = $true
        break
    }
}

if (-not $registrationExists) {
    # Recalcular índices porque pudo agregarse un using al inicio.
    $lastServiceIndex = -1

    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '\bbuilder\.Services\b') {
            $lastServiceIndex = $index
        }
    }

    if ($lastServiceIndex -lt 0) {
        throw "No se pudo localizar el último registro builder.Services."
    }

    # Si el último registro ocupa varias líneas, avanzar hasta cerrar la
    # instrucción con punto y coma.
    $insertAfterIndex = $lastServiceIndex

    while (
        $insertAfterIndex -lt ($lines.Count - 1) -and
        $lines[$insertAfterIndex] -notmatch ';\s*$'
    ) {
        $insertAfterIndex++
    }

    $lines.Insert(
        $insertAfterIndex + 1,
        "builder.Services.AddSagaMessaging(builder.Configuration);"
    )

    Write-Host (
        "Registro agregado después de la línea {0}." -f ($insertAfterIndex + 1)
    ) -ForegroundColor Green
}
else {
    Write-Host "AddSagaMessaging ya estaba registrado." -ForegroundColor Yellow
}

Write-Step "Retirando la API HTTP interna de Transfers"

$removedInternalEndpoint = $false

for ($index = $lines.Count - 1; $index -ge 0; $index--) {
    if ($lines[$index] -match 'app\.MapInternalTransfersEndpoints\s*\(') {
        Write-Host ("Eliminando línea {0}: {1}" -f ($index + 1), $lines[$index])
        $lines.RemoveAt($index)
        $removedInternalEndpoint = $true
    }
}

if (-not $removedInternalEndpoint) {
    Write-Host "MapInternalTransfersEndpoints no estaba presente." `
        -ForegroundColor Yellow
}

Set-Content `
    -Path $programPath `
    -Value $lines `
    -Encoding UTF8

Write-Step "Agregando RabbitMQ.Client al monolito"

$project = Get-Content $projectPath -Raw

if ($project -notmatch 'PackageReference\s+Include\s*=\s*"RabbitMQ\.Client"') {
    $itemGroup = @'
  <ItemGroup>
    <PackageReference Include="RabbitMQ.Client" Version="7.2.1" />
  </ItemGroup>
'@

    if ($project -notmatch '</Project>') {
        throw "ModularBank.csproj no contiene </Project>."
    }

    $project = $project.Replace(
        '</Project>',
        $itemGroup + "`r`n</Project>"
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

Write-Step "Copiando scripts operativos del kit"

$scriptsSource = Join-Path $kitFilesRoot "scripts\windows"
$scriptsTarget = Join-Path $RepoRoot "scripts\windows"

$requiredScripts = @(
    "20-generar-migracion-saga-accounts.ps1",
    "21-construir-levantar-saga-adr002.ps1",
    "22-validar-saga-adr002.ps1"
)

New-Item `
    -ItemType Directory `
    -Force `
    -Path $scriptsTarget |
Out-Null

foreach ($scriptName in $requiredScripts) {
    $sourceScript = Join-Path $scriptsSource $scriptName
    $targetScript = Join-Path $scriptsTarget $scriptName

    if (-not (Test-Path $sourceScript)) {
        throw "No se encontró en el kit: $sourceScript"
    }

    Copy-Item `
        -LiteralPath $sourceScript `
        -Destination $targetScript `
        -Force

    Write-Host "Copiado: $scriptName"
}

Write-Step "Validación final"

$programAfter = Get-Content $programPath -Raw
$projectAfter = Get-Content $projectPath -Raw

$errors = [System.Collections.Generic.List[string]]::new()

if ($programAfter -notmatch '(?m)^\s*using\s+ModularBank\.Messaging\s*;\s*$') {
    $errors.Add("Falta using ModularBank.Messaging;")
}

$registrationMatches = [regex]::Matches(
    $programAfter,
    'builder\.Services\.AddSagaMessaging\s*\(\s*builder\.Configuration\s*\)'
)

if ($registrationMatches.Count -ne 1) {
    $errors.Add(
        "AddSagaMessaging debe aparecer una sola vez; aparece $($registrationMatches.Count)."
    )
}

if ($programAfter -match 'app\.MapInternalTransfersEndpoints\s*\(') {
    $errors.Add("Todavía existe app.MapInternalTransfersEndpoints().")
}

if ($projectAfter -notmatch 'PackageReference\s+Include\s*=\s*"RabbitMQ\.Client"') {
    $errors.Add("Falta RabbitMQ.Client en ModularBank.csproj.")
}

$requiredFiles = @(
    "src\TransfersService\Messaging\AccountTransferResultConsumer.cs",
    "src\ModularBank\Messaging\AccountsTransferRequestedConsumer.cs",
    "src\ModularBank\Messaging\AccountsOutboxPublisher.cs",
    "src\ModularBank\Messaging\AuditTransferResultConsumer.cs",
    "src\ModularBank\Messaging\SagaMessagingExtensions.cs",
    "scripts\windows\20-generar-migracion-saga-accounts.ps1",
    "scripts\windows\21-construir-levantar-saga-adr002.ps1",
    "scripts\windows\22-validar-saga-adr002.ps1"
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path (Join-Path $RepoRoot $relativePath))) {
        $errors.Add("Falta: $relativePath")
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Errores encontrados:" -ForegroundColor Red

    foreach ($validationError in $errors) {
        Write-Host "  - $validationError" -ForegroundColor Red
    }

    throw "La instalación no superó la validación."
}

Write-Host ""
Write-Host "Program.cs resultante:" -ForegroundColor Green

Select-String `
    -Path $programPath `
    -Pattern `
        "ModularBank.Messaging",
        "AddSagaMessaging",
        "MapInternalTransfersEndpoints" |
Select-Object LineNumber, Line |
Format-Table -AutoSize

Write-Host ""
Write-Host "Continuación de la instalación completada." -ForegroundColor Green
Write-Host ""
Write-Host "Siguiente comando:" -ForegroundColor Cyan
Write-Host "  .\scripts\windows\20-generar-migracion-saga-accounts.ps1"
