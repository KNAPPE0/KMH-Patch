# KMH Patch - client mod packaging
#
# Builds the patch in Release and stages a clean RimWorld mod folder (only what
# the game needs: About, 1.6, Textures, LoadFolders.xml, LICENSE) plus a zip.
# Dev folders (Source, .vs, .git, Releases, Templates) are left out.
#
# Usage:  .\package-mod.ps1

param(
    [string]$Stage = (Join-Path $PSScriptRoot "Releases\KMHPatch")
)
$ErrorActionPreference = "Stop"

# --- build Release so 1.6\Assemblies is current ---
$Proj = Join-Path $PSScriptRoot "Source\KMHPatch.csproj"
Write-Host "[mod] Building patch (Release): $Proj"
& dotnet build "$Proj" -c Release | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Patch build failed (exit $LASTEXITCODE)" }

# --- stage a clean mod folder ---
if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Path $Stage -Force | Out-Null

foreach ($item in @("About", "1.6", "Textures", "LoadFolders.xml", "LICENSE")) {
    $src = Join-Path $PSScriptRoot $item
    if (Test-Path $src) {
        Write-Host "[mod] Staging $item"
        Copy-Item -Path $src -Destination $Stage -Recurse -Force
    } else {
        Write-Host "[mod] (skip - not found) $item"
    }
}

# --- read version from About.xml for the zip name ---
$ver = "0.0.0"
$about = Join-Path $PSScriptRoot "About\About.xml"
if (Test-Path $about) {
    $m = Select-String -Path $about -Pattern "<modVersion>([^<]+)</modVersion>" -List
    if ($m -and $m.Matches.Count -gt 0) { $ver = $m.Matches[0].Groups[1].Value }
}

# --- zip it ---
$Releases = Join-Path $PSScriptRoot "Releases"
if (-not (Test-Path $Releases)) { New-Item -ItemType Directory -Path $Releases -Force | Out-Null }
$Zip = Join-Path $Releases "KMHPatch-mod-v$ver.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path $Stage -DestinationPath $Zip -Force   # zip the folder itself so it extracts as a mod dir
$kb = [Math]::Round(((Get-Item $Zip).Length / 1KB), 1)

$count = (Get-ChildItem $Stage -Recurse -File).Count
Write-Host ""
Write-Host "[mod] Done. Clean mod folder: $Stage ($count files)"
Write-Host "[mod] Mod zip:               $Zip (${kb} KB)"
Write-Host "[mod] Drop the folder in RimWorld\Mods\, or upload the zip."
