param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$Remote = "origin",
    [string]$Branch = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Set-Location $RepoRoot

if (-not (Test-Path ".git")) {
    throw "La ruta '$RepoRoot' no parece ser la raíz del repositorio Git."
}

$currentBranch = (& git branch --show-current).Trim()
if ([string]::IsNullOrWhiteSpace($Branch)) {
    $Branch = $currentBranch
}

Write-Host ""
Write-Host "==> Último commit local" -ForegroundColor Cyan
git log -1 --decorate --date=iso --pretty=format:"Commit: %H%nAutor: %an <%ae>%nFecha: %ad%nMensaje: %s%n"

Write-Host ""
Write-Host "==> Remotos configurados" -ForegroundColor Cyan
git remote -v

Write-Host ""
Write-Host "==> Actualizando referencias remotas" -ForegroundColor Cyan
git fetch $Remote
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo actualizar el remoto '$Remote'."
}

$remoteRef = "$Remote/$Branch"
$remoteExists = & git show-ref --verify --quiet "refs/remotes/$remoteRef"
if ($LASTEXITCODE -ne 0) {
    throw "No se encontró la rama remota '$remoteRef'."
}

$comparison = (& git rev-list --left-right --count "$remoteRef...HEAD").Trim() -split "\s+"
$behind = [int]$comparison[0]
$ahead = [int]$comparison[1]

Write-Host ""
Write-Host "==> Sincronización" -ForegroundColor Cyan
Write-Host "Rama local : $Branch"
Write-Host "Rama remota: $remoteRef"
Write-Host "Commits detrás : $behind"
Write-Host "Commits delante: $ahead"

if ($behind -eq 0 -and $ahead -eq 0) {
    Write-Host "La rama local y GitHub están sincronizados." -ForegroundColor Green
}
else {
    Write-Warning "La rama local y la remota no están sincronizadas."
}

Write-Host ""
Write-Host "==> Estado del árbol de trabajo" -ForegroundColor Cyan
$status = & git status --porcelain
if ($status) {
    $status
    Write-Warning "Existen cambios locales sin registrar."
}
else {
    Write-Host "Árbol de trabajo limpio." -ForegroundColor Green
}
