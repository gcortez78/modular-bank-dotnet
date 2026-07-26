param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$CommitMessage = "feat: completar extracción de Notifications Service",
    [string]$Remote = "origin",
    [string]$Branch = "",
    [switch]$IncludeDatabaseData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Git {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$GitArgs
    )

    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Falló el comando: git $($GitArgs -join ' ')"
    }
}

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no parece ser la raíz del repositorio Git."
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git no está instalado o no está disponible en PATH."
}

$currentBranch = (& git branch --show-current).Trim()
if ([string]::IsNullOrWhiteSpace($currentBranch)) {
    throw "No se pudo determinar la rama actual."
}

if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = $currentBranch
}

if ($Branch -ne $currentBranch) {
    throw "La rama actual es '$currentBranch', pero se solicitó publicar '$Branch'."
}

$conflicts = & git diff --name-only --diff-filter=U
if ($conflicts) {
    throw "Existen conflictos de merge sin resolver:`n$($conflicts -join "`n")"
}

Write-Step "Estado inicial del repositorio"
git status --short

Write-Step "Agregando cambios al área de preparación"
Invoke-Git -GitArgs @("add", "-A")

$stagedFiles = @(& git diff --cached --name-only)

if (-not $stagedFiles) {
    Write-Host "No existen cambios para registrar." -ForegroundColor Yellow
    exit 0
}

$blockedPatterns = @(
    '(^|/)\.env($|\.)',
    '(^|/).*secret.*',
    '\.pfx$',
    '\.pem$',
    '\.key$',
    'notifications-data-backup\.sql$'
)

if (-not $IncludeDatabaseData) {
    $blockedPatterns += '(^|/)database/notifications/data\.sql$'
}

$blockedFiles = foreach ($file in $stagedFiles) {
    foreach ($pattern in $blockedPatterns) {
        if ($file -match $pattern) {
            $file
            break
        }
    }
}

if ($blockedFiles) {
    Write-Warning "Se detectaron archivos que no deben publicarse automáticamente:"
    $blockedFiles | Sort-Object -Unique | ForEach-Object {
        Write-Host "  - $_" -ForegroundColor Yellow
        & git restore --staged -- "$_"
        if ($LASTEXITCODE -ne 0) {
            & git reset HEAD -- "$_" | Out-Null
        }
    }

    throw "Revise los archivos excluidos. No se realizó el commit."
}

Write-Step "Archivos que serán incluidos"
git diff --cached --stat
git diff --cached --name-status

Write-Step "Creando commit"
Invoke-Git -GitArgs @("commit", "-m", $CommitMessage)

Write-Step "Publicando rama '$Branch' en '$Remote'"
Invoke-Git -GitArgs @("push", "-u", $Remote, $Branch)

Write-Host ""
Write-Host "Cambios publicados correctamente." -ForegroundColor Green
Write-Host "Rama: $Branch"
Write-Host "Commit: $((& git rev-parse --short HEAD).Trim())"
