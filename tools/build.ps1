<#
.SYNOPSIS
  Builds Ossuary and stages an install-ready mod directory.

.DESCRIPTION
  Staging is destroy-and-recreate, never an incremental copy. A resource left
  behind by a previous build is one of the hardest mod failures to diagnose,
  and recreating the directory makes it impossible rather than unlikely.

  The build fails if the manifest declares a payload that was not produced.

.PARAMETER GameDir
  Slay the Spire 2 install root. Defaults to the repo's usual resolution order
  (GameDir.props, then OSSUARY_GAME_DIR, then the standard Steam path).

.PARAMETER Configuration
  Release (default) or Debug.
#>
[CmdletBinding()]
param(
    [string]$GameDir,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$staging = Join-Path $repo 'build\mods\Ossuary'

# The version lives in exactly one place.
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = ($props.Project.PropertyGroup.Version | Where-Object { $_ }) | Select-Object -First 1
if (-not $version) { throw 'Could not read <Version> from Directory.Build.props' }

Write-Host "Ossuary $version ($Configuration)" -ForegroundColor Cyan

# ── locate a dotnet that actually has an SDK ─────────────────────────────────
# A machine can carry a runtime-only install (typically C:\Program Files\dotnet)
# ahead of the SDK on PATH, in which case a bare `dotnet build` fails with "No
# .NET SDKs were found" even though an SDK is installed. Probe candidates and
# take the first that reports one, rather than trusting PATH order.
function Resolve-DotNet {
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
    $candidates += (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe')
    $candidates += (Get-Command dotnet -All -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })

    foreach ($c in ($candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -Unique)) {
        try {
            $sdks = & $c --list-sdks
            if ($LASTEXITCODE -eq 0 -and $sdks) { return $c }
        }
        catch { }
    }
    throw 'No .NET SDK found. Install the .NET 9 SDK, or point DOTNET_ROOT at an install that has one.'
}

$dotnet = Resolve-DotNet
Write-Host "using $dotnet" -ForegroundColor DarkGray

$buildArgs = @(
    'build', (Join-Path $repo 'src\Ossuary\Ossuary.csproj'),
    '-c', $Configuration, '--nologo'
)
if ($GameDir) { $buildArgs += "-p:GameDir=$GameDir" }

& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

# ── stage ────────────────────────────────────────────────────────────────────
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

$dll = Join-Path $repo "src\Ossuary\bin\$Configuration\Ossuary.dll"
if (-not (Test-Path $dll)) { throw "Expected build output not found: $dll" }
Copy-Item $dll (Join-Path $staging 'Ossuary.dll')

# The manifest ships with the real version substituted in, so the loader UI and
# the assembly can never disagree about what is installed.
$manifestPath = Join-Path $staging 'Ossuary.json'
(Get-Content (Join-Path $repo 'src\Ossuary\Ossuary.json') -Raw).Replace('__VERSION__', $version) |
    Set-Content -Path $manifestPath -Encoding utf8 -NoNewline

# ── verify the manifest's promises ───────────────────────────────────────────
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
foreach ($p in @(@{ Flag = $manifest.has_dll; File = 'Ossuary.dll' },
                 @{ Flag = $manifest.has_pck; File = 'Ossuary.pck' })) {
    $present = Test-Path (Join-Path $staging $p.File)
    if ($p.Flag -and -not $present) { throw "Manifest declares $($p.File) but it was not staged." }
    if (-not $p.Flag -and $present) { throw "$($p.File) was staged but the manifest does not declare it." }
}
if ($manifest.version -ne $version) { throw "Manifest version '$($manifest.version)' != build version '$version'." }

Write-Host "`nStaged to $staging" -ForegroundColor Green
Get-ChildItem $staging | ForEach-Object {
    $sha = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16)
    '{0,-16} {1,8:N0} bytes  sha256:{2}…' -f $_.Name, $_.Length, $sha
}
