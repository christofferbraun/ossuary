<#
.SYNOPSIS
  Shows what Ossuary and the mod loader reported in the most recent session.

.DESCRIPTION
  The game writes its Godot log to %APPDATA%\SlayTheSpire2\logs\godot.log.
  It does NOT reliably write <STS2>\sts2_stdout.log, so that path is not used
  here even though community guides mention it.

.PARAMETER All
  Show every mod-loader line, not just Ossuary's.

.PARAMETER Follow
  Tail the log as the game writes it.
#>
[CmdletBinding()]
param(
    [switch]$All,
    [switch]$Follow
)

$ErrorActionPreference = 'Stop'
$log = Join-Path $env:APPDATA 'SlayTheSpire2\logs\godot.log'
if (-not (Test-Path $log)) { throw "No log at $log — has the game been run?" }

$pattern = if ($All) { '\[Ossuary\]|Mod |mod manifest|initializer|Loading assembly|Loading Godot PCK' } else { '\[Ossuary\]|Ossuary' }

Write-Host "$log`n" -ForegroundColor DarkGray
if ($Follow) {
    Get-Content $log -Wait -Tail 0 | Where-Object { $_ -match $pattern }
} else {
    $lines = Select-String -Path $log -Pattern $pattern -CaseSensitive:$false
    if (-not $lines) { Write-Host 'No matching lines. Ossuary may not have loaded.' -ForegroundColor Yellow; return }
    $lines | ForEach-Object {
        $color = if ($_.Line -match '\[ERROR\]') { 'Red' } elseif ($_.Line -match '\[WARN\]') { 'Yellow' } else { 'Gray' }
        Write-Host $_.Line -ForegroundColor $color
    }
}
