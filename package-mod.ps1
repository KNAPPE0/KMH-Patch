# KMH: Colony Nexus - build the official GitHub release assets.
# Usage: .\package-mod.ps1 [-Stage <folder>] [-Directory official|preview]   (preview = staging directory, ...-playtest.zip)
param(
    # The folder players unzip; the loader carries settings over from the old KMHPatch folder.
    [string]$Stage = (Join-Path $PSScriptRoot "Releases\ColonyNexus"),
    [ValidateSet("official", "preview")][string]$Directory = "official"
)
$dirArg = "-p:KmhDirectory=$Directory"
$zipSuffix = if ($Directory -eq "preview") { "-playtest" } else { "" }

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
        "KMHPatch.RTClient.dll",
        "KMHPatch.RTClientV2.dll"
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

# A stale flavour payload left in the output dir would otherwise ship silently inside the release.
function Assert-AssemblyVersions {
    param([string[]]$Paths, [string]$Expected)

    # Unset components count as 0, so 1.2.1 matches its 1.2.1.0 assembly while 1.2.1.1 stays exact.
    $w = [version]$Expected
    $want = [version]::new($w.Major, $w.Minor, [Math]::Max($w.Build, 0), [Math]::Max($w.Revision, 0))

    foreach ($p in $Paths) {
        try {
            $v = [System.Reflection.AssemblyName]::GetAssemblyName($p).Version
        } catch {
            throw "Could not read assembly version of $p : $($_.Exception.Message)"
        }
        $got = [version]::new($v.Major, $v.Minor, [Math]::Max($v.Build, 0), [Math]::Max($v.Revision, 0))
        if ($got -ne $want) {
            throw "Version mismatch: $([System.IO.Path]::GetFileName($p)) is $v but the mod version is $Expected (stale build? rebuild both flavors)."
        }
    }
    Write-Host "[mod] Version check OK - all KMH assemblies are v$Expected."

    # Dev builds share one AssemblyVersion; only the release tag read from the BINARY tells fresh from stale.
    $protoPath = Join-Path $PSScriptRoot 'Source\SubProtocol\KmhProtocol.cs'
    if (-not (Test-Path -LiteralPath $protoPath)) { throw "KmhProtocol.cs not found - cannot verify the release tag." }
    $tag = [regex]::Match([System.IO.File]::ReadAllText($protoPath), 'UiBuildTag\s*=\s*"([^"]*)"')
    if (-not $tag.Success) { throw "UiBuildTag not found in KmhProtocol.cs - cannot verify the release tag." }
    $wantTag = $tag.Groups[1].Value
    $wantInfo = "$Expected+$wantTag"

    foreach ($p in $Paths) {
        $got = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($p).ProductVersion
        if ($got -ne $wantInfo) {
            throw ("Release tag mismatch: $([System.IO.Path]::GetFileName($p)) reports '$got' but this source tree is " +
                   "'$wantInfo'. Both are AssemblyVersion $Expected, so this is a stale build - rebuild every flavour.")
        }
    }
    Write-Host "[mod] Release tag OK - every assembly reports $wantInfo." -ForegroundColor Cyan
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

Write-Host "[mod] Building KMH: Colony Nexus v${version}:"
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

Write-Host "[mod] Building Colony Nexus payload: Old/GameClient..."
& dotnet build $project -c Release -p:RwtFlavor=Old $dirArg | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Old/GameClient build failed with exit code $LASTEXITCODE."
}

Write-Host "[mod] Building Colony Nexus payload: New/RTClient..."
& dotnet build $project -c Release -p:RwtFlavor=New $dirArg | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "New/RTClient build failed with exit code $LASTEXITCODE."
}

Write-Host "[mod] Building Colony Nexus payload: RT/RTClientV2..."
& dotnet build $project -c Release -p:RwtFlavor=RT $dirArg | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "RT/RTClientV2 build failed with exit code $LASTEXITCODE."
}

$requiredBuilt = @(
    (Join-Path $PSScriptRoot "1.6\Assemblies\KMHPatch.dll"),
    (Join-Path $PSScriptRoot "1.6\Assemblies\KMH.Sdk.Client.dll"),
    (Join-Path $PSScriptRoot "1.6\KMHLib\KMHPatch.GameClient.dll"),
    (Join-Path $PSScriptRoot "1.6\KMHLib\KMHPatch.RTClient.dll"),
    (Join-Path $PSScriptRoot "1.6\KMHLib\KMHPatch.RTClientV2.dll")
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
# A stage left under the old folder name would otherwise sit beside the new one.
Remove-IfExists (Join-Path $PSScriptRoot "Releases\KMHPatch")
New-Item -ItemType Directory -Path $Stage -Force | Out-Null

foreach ($item in @("About", "1.6", "1.7", "Textures", "Languages", "LoadFolders.xml", "LICENSE")) {
    $src = Join-Path $PSScriptRoot $item
    if (Test-Path $src) {
        Write-Host "[mod] Staging $item"
        Copy-Item $src $Stage -Recurse -Force
    }
}

Assert-OnlyExpectedBinaries -Path $Stage

Get-ChildItem $releases -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -eq "KMH-ColonyNexus-v$version$zipSuffix.zip" -or
        # The pre-rebrand name, so a stale zip under it cannot be picked up by the bundle.
        $_.Name -eq "KMH-Patch-v$version.zip" -or
        $_.Name -eq "KMH-Sdk-Client-v$version.zip" -or
        $_.Name -eq "KMHPatch-mod-v$version.zip" -or
        $_.Name -like "KMH-Patch-v$version-source.*" -or
        $_.Name -like "KMHPatch-v$version-source.*"
    } |
    Remove-Item -Force

# Not Compress-Archive: PowerShell 5.1 writes '\' entry names, which unpack as one literal filename on Linux/macOS.
Add-Type -AssemblyName System.IO.Compression             # ZipArchive / ZipArchiveMode
Add-Type -AssemblyName System.IO.Compression.FileSystem  # ZipFile / ZipFileExtensions
$modZip = Join-Path $releases "KMH-ColonyNexus-v$version$zipSuffix.zip"
if (Test-Path $modZip) { Remove-Item $modZip -Force }
$stageParent = (Resolve-Path (Split-Path $Stage -Parent)).Path.TrimEnd('\') + '\'
$modWriter = [System.IO.Compression.ZipFile]::Open($modZip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($item in Get-ChildItem $Stage -Recurse -File) {
        $rel = $item.FullName.Substring($stageParent.Length).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($modWriter, $item.FullName, $rel)
    }
}
finally { $modWriter.Dispose() }

# RimWorld finds a mod by these paths, so verify the shipped archive rather than trusting the staged folder.
$modCheck = [System.IO.Compression.ZipFile]::OpenRead($modZip)
try {
    # Languages included deliberately: a missing tree is invisible, because every string falls back to English.
    $top = Split-Path $Stage -Leaf
    foreach ($required in @("$top/About/About.xml", "$top/LoadFolders.xml", "$top/1.6/Assemblies/KMHPatch.dll",
                            "$top/Languages/English/Keyed/KMH.xml", "$top/Languages/German/Keyed/KMH.xml",
                            "$top/Languages/Spanish/Keyed/KMH.xml")) {
        if (-not ($modCheck.Entries | Where-Object { $_.FullName -eq $required })) {
            throw "Mod zip is missing $required - the archive layout is wrong and RimWorld will not load the mod."
        }
    }
    Write-Host "[mod] Zip layout OK - mod folder structure intact inside the archive."
}
finally { $modCheck.Dispose() }
$modKb = [Math]::Round((Get-Item $modZip).Length / 1KB, 1)

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

    # Entries written one by one, as for the mod zip, so the names are portable on every OS.
    $sdkZip = Join-Path $releases "KMH-Sdk-Client-v$version.zip"
    if (Test-Path $sdkZip) { Remove-Item $sdkZip -Force }
    $sdkWriter = [System.IO.Compression.ZipFile]::Open($sdkZip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in (Get-ChildItem $sdkStage -Recurse -File)) {
            $rel = $item.FullName.Substring($sdkStage.Length).TrimStart('\', '/').Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($sdkWriter, $item.FullName, $rel)
        }
    }
    finally { $sdkWriter.Dispose() }
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
Write-Host "       KMH-ColonyNexus-v$version$zipSuffix.zip"
Write-Host "       KMH-Sdk-Client-v$version.zip"