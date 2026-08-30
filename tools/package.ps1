<#
.SYNOPSIS
  Builds Ossuary and assembles the Steam Workshop workspace.

.DESCRIPTION
  Produces build\workshop\, laid out the way Mega Crit's uploader expects:

      content\        the files that go to the Workshop (DLL + manifest)
      workshop.json   metadata, with the change note substituted in
      image.png       the preview image
      mod_id.txt      copied back from a previous upload, if there was one

  Assembling is destroy-and-recreate, for the same reason the mod staging is: a
  file left behind from a previous layout uploads as if it belonged to the
  current one.

  This does not talk to Steam. See publish.ps1, which does, and docs\RELEASING.md
  for why the two are separate.

.PARAMETER ChangeNote
  What changed in this release, shown to subscribers. Defaults to the subject of
  the most recent commit, which is usually what you meant anyway.

.PARAMETER GameDir
  Slay the Spire 2 install root. Defaults to the repo's usual resolution order.
#>
[CmdletBinding()]
param(
    [string]$ChangeNote,
    [string]$GameDir,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$workspace = Join-Path $repo 'build\workshop'
$source = Join-Path $repo 'workshop'

# ── build and stage the mod itself ───────────────────────────────────────────
$buildArgs = @{ Configuration = $Configuration }
if ($GameDir) { $buildArgs['GameDir'] = $GameDir }
& (Join-Path $PSScriptRoot 'build.ps1') @buildArgs

$staged = Join-Path $repo 'build\mods\Ossuary'
if (-not (Test-Path $staged)) { throw "Expected staged mod at $staged" }

# ── version, from the one place it lives ─────────────────────────────────────
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = ($props.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1

if (-not $ChangeNote) {
    $ChangeNote = (& git -C $repo log -1 --format=%s 2>$null)
    if (-not $ChangeNote) { $ChangeNote = "Version $version" }
}

Write-Host "Ossuary $version -> Workshop workspace" -ForegroundColor Cyan
Write-Host "  change note: $ChangeNote" -ForegroundColor DarkGray

# ── assemble ─────────────────────────────────────────────────────────────────
if (Test-Path $workspace) { Remove-Item -Recurse -Force $workspace }
New-Item -ItemType Directory -Force -Path (Join-Path $workspace 'content') | Out-Null

Copy-Item (Join-Path $staged '*') (Join-Path $workspace 'content') -Recurse

$image = Join-Path $source 'image.png'
if (-not (Test-Path $image)) { throw "Preview image not found: $image. Run: python tools\make-preview.py" }
if ((Get-Item $image).Length -ge 1MB) { throw 'Preview image must be under 1 MB (Steam backend limit).' }
Copy-Item $image (Join-Path $workspace 'image.png')

# The change note is the only field that differs per release, so it is
# substituted rather than kept in the committed file where it would go stale.
$manifest = Get-Content (Join-Path $source 'workshop.json') -Raw | ConvertFrom-Json
$manifest.changeNote = $ChangeNote
$manifest | ConvertTo-Json -Depth 10 | Set-Content (Join-Path $workspace 'workshop.json') -Encoding utf8

# Carry the Workshop item id forward. Without it the uploader creates a second
# item rather than updating the one people are subscribed to.
$modId = Join-Path $source 'mod_id.txt'
if (Test-Path $modId) {
    Copy-Item $modId (Join-Path $workspace 'mod_id.txt')
    Write-Host "  updating Workshop item $(Get-Content $modId -Raw)" -ForegroundColor DarkGray
}
else {
    Write-Host '  no mod_id.txt: this will CREATE a new Workshop item' -ForegroundColor Yellow
}

# ── verify what we are about to publish ──────────────────────────────────────
$dll = Join-Path $workspace 'content\Ossuary.dll'
$json = Join-Path $workspace 'content\Ossuary.json'
foreach ($required in @($dll, $json)) {
    if (-not (Test-Path $required)) { throw "Workspace is missing $required" }
}

$modManifest = Get-Content $json -Raw | ConvertFrom-Json
if ($modManifest.version -ne $version) {
    throw "Mod manifest says $($modManifest.version) but the build is $version."
}
if ($modManifest.affects_gameplay) {
    throw 'Mod manifest declares affects_gameplay: true. Ossuary reads and displays only.'
}

Write-Host "`nWorkspace ready at $workspace" -ForegroundColor Green
Get-ChildItem $workspace -Recurse -File | ForEach-Object {
    '{0,-34} {1,8:N0} bytes' -f $_.FullName.Substring($workspace.Length + 1), $_.Length
}
Write-Host "`nVisibility is '$($manifest.visibility)'. Publish with: .\tools\publish.ps1" -ForegroundColor Cyan
