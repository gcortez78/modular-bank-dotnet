param(
    [string]$RepoRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Set-Location $RepoRoot

$scripts = @(
    "32-validar-estatico-practico4.ps1",
    "34-validar-topologia-practico4.ps1",
    "35-validar-dlq-practico4.ps1",
    "36-validar-idempotencia-practico4.ps1",
    "37-validar-retry-backoff-practico4.ps1",
    "38-validar-outbox-broker-caido-practico4.ps1"
)

foreach ($script in $scripts) {
    Write-Host ""
    Write-Host ("EJECUTANDO {0}" -f $script) -ForegroundColor Cyan
    & (Join-Path $RepoRoot ("scripts\windows\{0}" -f $script)) -RepoRoot $RepoRoot
}

Write-Host ""
Write-Host "TODAS LAS VALIDACIONES DEL PRÁCTICO 4 TERMINARON SATISFACTORIAMENTE" -ForegroundColor Green
