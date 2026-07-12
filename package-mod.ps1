# KMH Patch - build the official GitHub release assets.
#
# Usage:
#   .\package-mod.ps1
#   .\package-mod.ps1 -Stage "E:\RWT Related\KMH-Patch\Releases\KMHPatch"
param(
    [string]$Stage = (Join-Path $PSScriptRoot "Releases\KMHPatch")
)

$ErrorActionPreference = "Stop"

function Get-ModVersion {
    $about = Join-Path $PSScriptRoot "About\About.xml"
    if (Test-Path $about) {
        $m = Select-String -Path $about -Pattern "<modVersion>([^<]+)</modVersion>" -List
        if ($m -and $m.Matches.Count -gt 0) {
            return $m.Matches[0].Groups[1].Value
        }
    }

    $project = Join-Path $PSScriptRoot "Source\KMHPatch.csproj"
    if (Test-Path $project) {
        $m = Select-String -Path $project -Pattern "<Version>([^<]+)</Version>" -List
        if ($m -and $m.Matches.Count -gt 0) {
            return $m.Matches[0].Groups[1].Value
        }
    }

    return "0.0.0"
}

function Assert-OnlyExpectedBinaries {
    param([string]$Path)

    $allowedDll = @(
        "KMHPatch.dll",
        "KMH.Sdk.Client.dll",
        "KMHPatch.GameClient.dll",
        "KMHPatch.RTClient.dll"
    )

    $stray = Get-ChildItem $Path -Recurse -File -Include *.dll,*.exe -ErrorAction SilentlyContinue |
        Where-Object { $allowedDll -notcontains $_.Name }

    if ($stray) {
        Write-Host ""
        Write-Host "[mod] Binary check failed - unexpected binaries were staged:" -ForegroundColor Red
        $stray | ForEach-Object { Write-Host "       $($_.FullName)" -ForegroundColor Red }
        throw "Release blocked: unexpected binary in staged mod folder."
    }

    Write-Host "[mod] Binary check OK - only expected KMH assemblies staged."
}

function Remove-IfExists {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item $Path -Recurse -Force
    }
}

# Guards against the recurring failure mode where a stale flavor payload (e.g. an old RTClient) is left in the output
# dir and ships silently inside the release. Every built KMH assembly must carry the current mod version.
function Assert-AssemblyVersions {
    param([string[]]$Paths, [string]$Expected)

    foreach ($p in $Paths) {
        try {
            $v = [System.Reflection.AssemblyName]::GetAssemblyName($p).Version
        } catch {
            throw "Could not read assembly version of $p : $($_.Exception.Message)"
        }
        $short = "$($v.Major).$($v.Minor).$($v.Build)"
        if ($short -ne $Expected) {
            throw "Version mismatch: $([System.IO.Path]::GetFileName($p)) is $v but the mod version is $Expected (stale build? rebuild both flavors)."
        }
    }
    Write-Host "[mod] Version check OK - all KMH assemblies are v$Expected."
}

$project = Join-Path $PSScriptRoot "Source\KMHPatch.csproj"
$releases = Join-Path $PSScriptRoot "Releases"
$version = Get-ModVersion

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

if (-not (Test-Path $releases)) {
    New-Item -ItemType Directory -Path $releases -Force | Out-Null
}

Write-Host "[mod] Building KMH Patch v${version}:"
Write-Host "      $project"

Write-Host "[mod] Building loader + SDK..."
& dotnet build (Join-Path $PSScriptRoot "Source\Loader\KMHPatchLoader.csproj") -c Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Loader build failed with exit code $LASTEXITCODE."
}

& dotnet build (Join-Path $PSScriptRoot "Source\KMH.Sdk.Client\KMH.Sdk.Client.csproj") -c Release | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Client SDK build failed with exit code $LASTEXITCODE."
}

Write-Host "[mod] Building KMH Patch payload: Old/GameClient..."
& dotnet build $project -c Release -p:RwtFlavor=Old | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Old/GameClient build failed with exit code $LASTEXITCODE."
}

Write-Host "[mod] Building KMH Patch payload: New/RTClient..."
& dotnet build $project -c Release -p:RwtFlavor=New | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "New/RTClient build failed with exit code $LASTEXITCODE."
}

$requiredBuilt = @(
    (Join-Path $PSScriptRoot "1.6\Assemblies\KMHPatch.dll"),
    (Join-Path $PSScriptRoot "1.6\Assemblies\KMH.Sdk.Client.dll"),
    (Join-Path $PSScriptRoot "1.6\KMHLib\KMHPatch.GameClient.dll"),
    (Join-Path $PSScriptRoot "1.6\KMHLib\KMHPatch.RTClient.dll")
)

foreach ($built in $requiredBuilt) {
    if (-not (Test-Path $built)) {
        throw "Required build output missing: $built"
    }
}

Assert-AssemblyVersions -Paths $requiredBuilt -Expected $version

Write-Host "[mod] Build outputs:" -ForegroundColor Green
Get-Item $requiredBuilt | Select-Object FullName, LastWriteTime, Length | Format-Table -AutoSize

Remove-IfExists $Stage
New-Item -ItemType Directory -Path $Stage -Force | Out-Null

foreach ($item in @("About", "1.6", "Textures", "LoadFolders.xml", "LICENSE")) {
    $src = Join-Path $PSScriptRoot $item
    if (Test-Path $src) {
        Write-Host "[mod] Staging $item"
        Copy-Item $src $Stage -Recurse -Force
    }
}

Assert-OnlyExpectedBinaries -Path $Stage

# Clean only current-version generated assets. Older release zips are left alone.
Get-ChildItem $releases -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -eq "KMH-Patch-v$version.zip" -or
        $_.Name -eq "KMH-Sdk-Client-v$version.zip" -or
        $_.Name -eq "KMHPatch-mod-v$version.zip" -or
        $_.Name -like "KMH-Patch-v$version-source.*" -or
        $_.Name -like "KMHPatch-v$version-source.*"
    } |
    Remove-Item -Force

# Mod zip. This zips the folder itself so it extracts as a normal RimWorld mod folder.
$modZip = Join-Path $releases "KMH-Patch-v$version.zip"
Compress-Archive -Path $Stage -DestinationPath $modZip -Force
$modKb = [Math]::Round((Get-Item $modZip).Length / 1KB, 1)

# Client SDK zip for extension authors.
$sdkStage = Join-Path ([System.IO.Path]::GetTempPath()) ("kmh_sdk_client_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $sdkStage -Force | Out-Null

try {
    $sdkRequired = @(
        @{ Source = (Join-Path $PSScriptRoot "1.6\Assemblies\KMH.Sdk.Client.dll"); Name = "KMH.Sdk.Client.dll" },
        @{ Source = (Join-Path $PSScriptRoot "1.6\Assemblies\KMH.Sdk.Client.xml"); Name = "KMH.Sdk.Client.xml" }
    )

    foreach ($f in $sdkRequired) {
        if (-not (Test-Path $f.Source)) {
            throw "SDK file missing: $($f.Source)"
        }

        Copy-Item $f.Source (Join-Path $sdkStage $f.Name) -Force
    }

    foreach ($optional in @("LICENSE", "EXTENSIONS.md")) {
        $src = Join-Path $PSScriptRoot $optional
        if (Test-Path $src) {
            Copy-Item $src (Join-Path $sdkStage $optional) -Force
        }
    }

    $sdkZip = Join-Path $releases "KMH-Sdk-Client-v$version.zip"
    Compress-Archive -Path (Join-Path $sdkStage "*") -DestinationPath $sdkZip -Force
    $sdkKb = [Math]::Round((Get-Item $sdkZip).Length / 1KB, 1)
}
finally {
    Remove-IfExists $sdkStage
}

$count = (Get-ChildItem $Stage -Recurse -File).Count

Write-Host ""
Write-Host "[mod] Done." -ForegroundColor Green
Write-Host "[mod] Clean mod folder: $Stage ($count files)"
Write-Host "[mod] Release asset:   $modZip ($modKb KB)"
Write-Host "[mod] SDK asset:       $sdkZip ($sdkKb KB)"
Write-Host ""
Write-Host "[mod] Upload these manually to GitHub Releases:"
Write-Host "       KMH-Patch-v$version.zip"
Write-Host "       KMH-Sdk-Client-v$version.zip"