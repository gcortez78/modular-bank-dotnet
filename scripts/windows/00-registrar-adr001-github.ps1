param(
    [string]$RepoRoot = (Get-Location).Path,
    [string]$CommitMessage = "feat: completar extracción de Notifications Service",
    [string]$Token = "",
    [switch]$IncludeData
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptsDir = Split-Path -Parent $MyInvocation.MyCommand.Path

& (Join-Path $scriptsDir "10-exportar-evidencias-adr001.ps1") `
    -RepoRoot $RepoRoot `
    -Token $Token `
    -IncludeData:$IncludeData

& (Join-Path $scriptsDir "11-guardar-cambios-github.ps1") `
    -RepoRoot $RepoRoot `
    -CommitMessage $CommitMessage `
    -IncludeDatabaseData:$IncludeData

& (Join-Path $scriptsDir "12-validar-publicacion-github.ps1") `
    -RepoRoot $RepoRoot
