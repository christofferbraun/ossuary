<#
.SYNOPSIS
  Uploads the packaged workspace to the Steam Workshop.

.DESCRIPTION
  Uses Mega Crit's own uploader (github.com/megacrit/sts2-mod-uploader), fetched
  and cached on first use, pinned to a known version.

  This has to run on your machine rather than in CI. The uploader calls
  SteamAPI.InitEx(), which needs a running Steam client logged into an account
  that owns Slay the Spire 2 — a GitHub-hosted runner has neither. See
  docs\RELEASING.md.

  After the first successful upload the tool writes mod_id.txt into the
  workspace. It is copied back into workshop\ so that later releases update the
  same Workshop item instead of creating a second one.

.PARAMETER Version
  Uploader release to use. Pinned by default so a new upstream release cannot
  change publishing behaviour without someone choosing it.

.PARAMETER Force
  Skip the confirmation prompt. For when you have already read the summary.
#>
[CmdletBinding()]
param(
    [string]$Version = 'v0.2.0',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$workspace = Join-Path $repo 'build\workshop'
$cache = Join-Path $repo "build\tools\ModUploader-$Version"
$exe = Join-Path $cache 'ModUploader.exe'

if (-not (Test-Path $workspace)) {
    throw "No workspace at $workspace. Run .\tools\package.ps1 first."
}

# ── the uploader needs Steam ─────────────────────────────────────────────────
if (-not (Get-Process -Name 'steam' -ErrorAction SilentlyContinue)) {
    throw 'Steam is not running. The uploader talks to the Steam client, so start Steam and sign in first.'
}

# ── fetch the uploader once ──────────────────────────────────────────────────
if (-not (Test-Path $exe)) {
    $url = "https://github.com/megacrit/sts2-mod-uploader/releases/download/$Version/ModUploader-win-x64.zip"
    Write-Host "Fetching the uploader $Version" -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    $zip = Join-Path $cache 'ModUploader.zip'
    Invoke-WebRequest -Uri $url -OutFile $zip
    Expand-Archive -Path $zip -DestinationPath $cache -Force
    Remove-Item $zip

    if (-not (Test-Path $exe)) {
        $found = Get-ChildItem $cache -Recurse -Filter 'ModUploader.exe' | Select-Object -First 1
        if (-not $found) { throw "ModUploader.exe not found in the $Version archive." }
        $exe = $found.FullName
    }
}

# ── say what is about to happen ──────────────────────────────────────────────
$manifest = Get-Content (Join-Path $workspace 'workshop.json') -Raw | ConvertFrom-Json
$modIdPath = Join-Path $workspace 'mod_id.txt'
$action = if (Test-Path $modIdPath) { "UPDATE item $(Get-Content $modIdPath -Raw)" } else { 'CREATE a new item' }

Write-Host ''
Write-Host "  action     : $action" -ForegroundColor Yellow
Write-Host "  title      : $($manifest.title)"
Write-Host "  visibility : $($manifest.visibility)" -ForegroundColor Yellow
Write-Host "  change note: $($manifest.changeNote)"
Write-Host ''

if (-not $Force) {
    $answer = Read-Host 'Publish to the Steam Workshop? (yes/no)'
    if ($answer -ne 'yes') { Write-Host 'Cancelled.' -ForegroundColor DarkGray; return }
}

& $exe upload -w $workspace
if ($LASTEXITCODE -ne 0) {
    $log = Join-Path $workspace 'mod-uploader.log'
    if (Test-Path $log) { Write-Host "`n--- mod-uploader.log ---" -ForegroundColor DarkGray; Get-Content $log -Tail 30 }
    throw "Upload failed with exit code $LASTEXITCODE"
}

# Keep the item id under version control, so the next release updates rather
# than creating a duplicate.
if (Test-Path $modIdPath) {
    $kept = Join-Path $repo 'workshop\mod_id.txt'
    Copy-Item $modIdPath $kept -Force
    Write-Host "`nWorkshop item id saved to workshop\mod_id.txt — commit this." -ForegroundColor Green
}

Write-Host 'Published.' -ForegroundColor Green
