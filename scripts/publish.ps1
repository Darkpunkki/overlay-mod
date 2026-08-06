<#
.SYNOPSIS
    Build a standalone OverlayMod.exe.

.DESCRIPTION
    Produces a single self-contained executable that runs on a machine with no
    .NET installed. The result is windowed rather than a console application and
    lives in the notification area.

    Self-contained costs roughly 100 MB because the whole runtime is bundled.
    That is the trade for "download one file and run it", which is the right
    default for people who want to track a run rather than install a toolchain.

.PARAMETER Output
    Where to put the build. Defaults to ./publish.

.PARAMETER Slim
    Build against an installed .NET 8 runtime instead of bundling one. A few MB
    rather than ~180, but it will not start on a machine without .NET 8.

.PARAMETER Trimmed
    Trim unused framework code. Much smaller, but trimming can remove types that
    are only ever resolved by reflection, so test the result before relying on it.

.EXAMPLE
    ./scripts/publish.ps1

.EXAMPLE
    ./scripts/publish.ps1 -Slim -Output publish-slim
#>
[CmdletBinding()]
param(
    [string]$Output = "publish",
    [switch]$Slim,
    [switch]$Trimmed
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Host/OverlayMod.Host.csproj"
$target = Join-Path $root $Output

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    $local = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
    if (Test-Path (Join-Path $local "dotnet.exe")) {
        $env:DOTNET_ROOT = $local
        $env:PATH = "$local;$env:PATH"
    }
    else {
        throw "dotnet was not found on PATH or at $local."
    }
}

Write-Host "Publishing OverlayMod to $target" -ForegroundColor Cyan

$arguments = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $(if ($Slim) { "false" } else { "true" }),
    "-p:PublishSingleFile=true",
    # Keeps the native bits inside the one file instead of scattering DLLs beside it.
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none",
    "-o", $target
)

if ($Trimmed) {
    $arguments += "-p:PublishTrimmed=true"
    Write-Warning "Trimming is enabled. Verify the published build actually runs before shipping it."
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $target "OverlayMod.exe"
if (-not (Test-Path $exe)) { throw "Publish reported success but $exe is missing." }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ""
Write-Host "Built $exe ($size MB)" -ForegroundColor Green
if ($Slim) { Write-Host "Slim build: the target machine needs .NET 8 installed." -ForegroundColor Yellow }
Write-Host ""
Write-Host "Run it and OverlayMod appears in the notification area."
Write-Host "Right-click the icon for the overlay and control-panel links."
Write-Host "Data and logs are written next to the executable, in .\appdata\."
Write-Host ""
Write-Host "To publish a release:" -ForegroundColor Cyan
Write-Host "  gh release create v0.1.0 `"$exe`" --title `"OverlayMod v0.1.0`" --notes `"...`""
