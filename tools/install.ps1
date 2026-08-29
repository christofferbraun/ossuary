<#
.SYNOPSIS
  Builds Ossuary and installs it into the game's mods directory.

.DESCRIPTION
  Replaces the whole mod directory rather than merging into it, for the same
  reason staging is recreated: a leftover file from an older layout loads as if
  it belonged to the current one.

  Refuses to run while Slay the Spire 2 is open, because the loaded DLL is
  locked and a partial copy is worse than no copy.
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue) {
    throw 'Slay the Spire 2 is running. Close it before installing — the loaded DLL is locked.'
}

if (-not $GameDir) {
    $propsPath = Join-Path $repo 'GameDir.props'
    if (Test-Path $propsPath) {
        [xml]$p = Get-Content $propsPath
        $GameDir = ($p.Project.PropertyGroup.GameDir | Where-Object { $_ }) | Select-Object -First 1
    }
}
if (-not $GameDir) { $GameDir = $env:OSSUARY_GAME_DIR }
if (-not $GameDir) { $GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2' }

if (-not (Test-Path (Join-Path $GameDir 'data_sts2_windows_x86_64\sts2.dll'))) {
    throw "Slay the Spire 2 not found at '$GameDir'."
}

& (Join-Path $PSScriptRoot 'build.ps1') -GameDir $GameDir -Configuration $Configuration

$staging = Join-Path $repo 'build\mods\Ossuary'
$target = Join-Path $GameDir 'mods\Ossuary'

New-Item -ItemType Directory -Force -Path (Join-Path $GameDir 'mods') | Out-Null
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
Copy-Item $staging $target -Recurse

Write-Host "`nInstalled to $target" -ForegroundColor Green
Get-ChildItem $target | Select-Object Name, Length | Format-Table -AutoSize
Write-Host 'Launch the game, then run tools\logs.ps1 to see what Ossuary reported.' -ForegroundColor Cyan
