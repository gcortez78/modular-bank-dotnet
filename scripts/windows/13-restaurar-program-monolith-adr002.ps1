param(
    [string]$RepoRoot = (Get-Location).Path,
    [switch]$BuildMonolith
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

$programPath = Join-Path $RepoRoot "src\ModularBank\Program.cs"
$internalEndpointPath = Join-Path $RepoRoot `
    "src\ModularBank\Modules\Accounts\Api\InternalTransfersEndpoints.cs"

if (-not (Test-Path $programPath)) {
    throw "No se encontró $programPath"
}

if (-not (Test-Path $internalEndpointPath)) {
    throw "No se encontró InternalTransfersEndpoints.cs. Reinstale los archivos ADR-002 antes de continuar."
}

Write-Step "Buscando el respaldo original del Paso 1"

$backupPrograms = @(
    Get-ChildItem `
        -Path (Join-Path $RepoRoot ".adr002-backup") `
        -Recurse `
        -Filter "Program.cs" `
        -File `
        -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -match [regex]::Escape(
            "src\ModularBank\Program.cs"
        ) + "$"
    } |
    Sort-Object LastWriteTime -Descending
)

if (-not $backupPrograms) {
    throw @"
No se encontró el respaldo automático de Program.cs en .adr002-backup.

No se modificará el archivo actual para evitar seguir alterando firmas.
Recupere el Program.cs funcional del Paso 1 desde su commit o rama estable.
"@
}

$sourceProgram = $backupPrograms[0]

Write-Host "Respaldo seleccionado:" -ForegroundColor Green
Write-Host "  $($sourceProgram.FullName)"
Write-Host "Fecha:" $sourceProgram.LastWriteTime

Write-Step "Respaldando el Program.cs actualmente dañado"

$manualBackupDir = Join-Path $RepoRoot ".adr002-backup-manual"
New-Item -ItemType Directory -Force -Path $manualBackupDir | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$currentBackup = Join-Path $manualBackupDir "Program-danado-$timestamp.cs"

Copy-Item `
    -LiteralPath $programPath `
    -Destination $currentBackup `
    -Force

Write-Host "Copia del archivo actual:" -ForegroundColor Yellow
Write-Host "  $currentBackup"

Write-Step "Restaurando el Program.cs funcional del Paso 1"

Copy-Item `
    -LiteralPath $sourceProgram.FullName `
    -Destination $programPath `
    -Force

$content = Get-Content $programPath -Raw

Write-Step "Aplicando únicamente los cambios de extracción de Transfers"

# Retirar el registro del módulo Transfers que ejecutaba la lógica dentro
# del monolito. No se modifican las llamadas de Auth, Accounts,
# Notifications ni Audit.
$content = [regex]::Replace(
    $content,
    '(?m)^[ \t]*builder\.Services\.AddTransfersModule\s*\([^;]*\);[ \t]*\r?\n',
    ''
)

# Retirar el endpoint público legacy de Transfers.
$content = [regex]::Replace(
    $content,
    '(?m)^[ \t]*app\.MapTransfersEndpoints\s*\(\s*\);[ \t]*\r?\n',
    ''
)

# Compatibilidad por si el proyecto usa nombre singular.
$content = [regex]::Replace(
    $content,
    '(?m)^[ \t]*app\.MapTransferEndpoints\s*\(\s*\);[ \t]*\r?\n',
    ''
)

# Incorporar solo el endpoint interno usado por Transfers Service.
if ($content -notmatch 'app\.MapInternalTransfersEndpoints\s*\(\s*\);') {
    if ($content -match 'app\.MapAccountsEndpoints\s*\(\s*\);') {
        $content = [regex]::Replace(
            $content,
            '(app\.MapAccountsEndpoints\s*\(\s*\);)',
            '$1' + "`r`n" + 'app.MapInternalTransfersEndpoints();',
            1
        )
    }
    else {
        throw @"
No se encontró app.MapAccountsEndpoints(); en el Program.cs original.
Se restauró el archivo, pero no se aplicó el endpoint interno.
Revise manualmente la sección de mapeo.
"@
    }
}

# Guardar en UTF-8 con BOM para Windows PowerShell 5.1.
Set-Content `
    -Path $programPath `
    -Value $content `
    -Encoding UTF8

Write-Step "Validando que no se hayan alterado los demás módulos"

$requiredPatterns = @(
    "AddAuthModule",
    "AddAccountsModule",
    "AddNotificationsModule",
    "AddAuditModule",
    "MapAuthEndpoints",
    "MapAccountsEndpoints",
    "MapInternalTransfersEndpoints",
    "MapAuditEndpoints"
)

foreach ($pattern in $requiredPatterns) {
    if ($content -notmatch [regex]::Escape($pattern)) {
        throw "El Program.cs restaurado no contiene el elemento requerido: $pattern"
    }
}

$forbiddenPatterns = @(
    "AddTransfersModule",
    "MapTransfersEndpoints",
    "MapTransferEndpoints"
)

foreach ($pattern in $forbiddenPatterns) {
    if ($content -match [regex]::Escape($pattern)) {
        throw "El Program.cs todavía contiene el elemento legacy: $pattern"
    }
}

Write-Host ""
Write-Host "Registros y endpoints resultantes:" -ForegroundColor Green

Select-String `
    -Path $programPath `
    -Pattern `
        "JwtUtil",
        "AddAuthModule",
        "AddAccountsModule",
        "AddNotificationsModule",
        "AddAuditModule",
        "MapAuthEndpoints",
        "MapAccountsEndpoints",
        "MapInternalTransfersEndpoints",
        "MapAuditEndpoints" |
    Select-Object LineNumber, Line |
    Format-Table -AutoSize

Write-Step "Program.cs corregido de fondo"

Write-Host "No se cambiaron argumentos ni firmas de los módulos existentes." `
    -ForegroundColor Green
Write-Host "Solo se retiró Transfers legacy y se agregó su API interna." `
    -ForegroundColor Green

if ($BuildMonolith) {
    Write-Step "Construyendo el monolito"

    & docker compose --progress plain build monolith

    if ($LASTEXITCODE -ne 0) {
        throw "La construcción del monolito falló. Revise el primer error CSxxxx."
    }

    Write-Step "Recreando el contenedor monolith"

    & docker compose up -d --no-deps --force-recreate monolith

    if ($LASTEXITCODE -ne 0) {
        throw "No fue posible recrear el contenedor monolith."
    }

    Start-Sleep -Seconds 8

    & docker compose ps monolith
    & docker compose logs --since=2m --no-color monolith
}
else {
    Write-Host ""
    Write-Host "Ejecute ahora:" -ForegroundColor Cyan
    Write-Host "  docker compose --progress plain build monolith"
    Write-Host "  docker compose up -d --no-deps --force-recreate monolith"
}
