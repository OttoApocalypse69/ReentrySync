<#
.SYNOPSIS
    Automated 1-click installer for BepInEx 5 (x64) and ReentrySync (Smart Copilot, Autopilot, Burn Planner & Crew Co-op).
.EXAMPLE
    irm https://raw.githubusercontent.com/OttoApocalypse69/ReentrySync/main/scripts/install.ps1 | iex
.EXAMPLE
    .\install.ps1 -GameDir "E:\Games\Reentry - A Space Flight Simulator"
.EXAMPLE
    .\install.ps1 -Uninstall
#>
param(
    [string]$GameDir = "",
    [string]$Repo = "OttoApocalypse69/ReentrySync",
    [string]$Version = "latest",
    [string]$BepInExVersion = "5.4.23.6",
    [switch]$ForceBepInEx,
    [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Warn($msg) { Write-Host "  [!]  $msg" -ForegroundColor Yellow }
function Write-Err($msg)  { Write-Host "  [X]  $msg" -ForegroundColor Red }

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  REENTRY SYNC & COPILOT — AUTOMATED INSTALLER" -ForegroundColor White
Write-Host "  Installs BepInEx $BepInExVersion (x64) + ReentrySync Mod" -ForegroundColor Gray
Write-Host "============================================================" -ForegroundColor Cyan

# ------------------------------------------------------------------
# 1. Auto-Detect Reentry Installation Folder
# ------------------------------------------------------------------
function Test-ReentryDir([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    if (-not (Test-Path $path)) { return $false }
    return (Test-Path (Join-Path $path "ReEntry_Data")) -or (Test-Path (Join-Path $path "ReEntry.exe"))
}

function Find-ReentryDir {
    # Check current directory & script parent
    if (Test-ReentryDir (Get-Location).Path) { return (Get-Location).Path }
    if ($PSScriptRoot) {
        $parent1 = Split-Path $PSScriptRoot -Parent
        if (Test-ReentryDir $parent1) { return $parent1 }
        $parent2 = Split-Path $parent1 -Parent
        if (Test-ReentryDir $parent2) { return $parent2 }
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    $folderNames = @(
        "Reentry - An Orbital Simulator",
        "Reentry - A Space Flight Simulator",
        "Reentry"
    )

    # Scan Steam registry & libraryfolders.vdf
    $steamPaths = @()
    foreach ($reg in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $p = (Get-ItemProperty -Path $reg -ErrorAction SilentlyContinue).SteamPath
            if (-not $p) { $p = (Get-ItemProperty -Path $reg -ErrorAction SilentlyContinue).InstallPath }
            if ($p -and (Test-Path $p)) { $steamPaths += $p }
        } catch {}
    }

    $steamLibraries = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
    foreach ($sp in $steamPaths) {
        [void]$steamLibraries.Add($sp)
        $vdf = Join-Path $sp "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $content = Get-Content $vdf -Raw
            $matches = [regex]::Matches($content, '"path"\s*"([^"]+)"')
            foreach ($m in $matches) {
                $libPath = $m.Groups[1].Value -replace '\\\\', '\'
                if (Test-Path $libPath) { [void]$steamLibraries.Add($libPath) }
            }
        }
    }

    foreach ($lib in $steamLibraries) {
        foreach ($fn in $folderNames) {
            $candidates.Add((Join-Path $lib "steamapps\common\$fn"))
        }
    }

    # Scan common drive roots
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem | Select-Object -ExpandProperty Root)) {
        foreach ($fn in $folderNames) {
            $candidates.Add((Join-Path $drive "Games\$fn"))
            $candidates.Add((Join-Path $drive "SteamLibrary\steamapps\common\$fn"))
            $candidates.Add((Join-Path $drive "Program Files (x86)\Steam\steamapps\common\$fn"))
        }
    }

    foreach ($c in $candidates) {
        if (Test-ReentryDir $c) { return (Resolve-Path $c).Path }
    }
    return $null
}

Write-Step "Locating Reentry installation..."
if (-not (Test-ReentryDir $GameDir)) {
    $detected = Find-ReentryDir
    if ($detected) {
        Write-Ok "Auto-detected Reentry at: $detected"
        $GameDir = $detected
    } else {
        Write-Warn "Could not auto-detect your Reentry folder."
        try {
            Add-Type -AssemblyName System.Windows.Forms
            $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
            $dlg.Description = "Select your Reentry game folder (containing ReEntry.exe & ReEntry_Data)"
            if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                $GameDir = $dlg.SelectedPath
            }
        } catch {}

        if (-not (Test-ReentryDir $GameDir)) {
            $GameDir = Read-Host "Paste full path to your Reentry folder (containing ReEntry.exe)"
        }
    }
}

if (-not (Test-ReentryDir $GameDir)) {
    Write-Err "Invalid Reentry directory: '$GameDir' (missing ReEntry_Data / ReEntry.exe)."
    exit 1
}
Write-Ok "Target game folder: $GameDir"

# ------------------------------------------------------------------
# Optional Uninstall Mode
# ------------------------------------------------------------------
if ($Uninstall) {
    Write-Step "Uninstalling ReentrySync..."
    $pluginsDir = Join-Path $GameDir "BepInEx\plugins"
    foreach ($f in @("ReentrySync.dll", "ReentrySync.pdb", "Fleck.dll")) {
        $p = Join-Path $pluginsDir $f
        if (Test-Path $p) {
            Remove-Item $p -Force
            Write-Ok "Removed $f"
        }
    }
    Write-Ok "ReentrySync uninstalled cleanly (BepInEx left intact)."
    exit 0
}

$tempDir = Join-Path $env:TEMP ("ReentrySyncInstall_" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

try {
    $headers = @{ "User-Agent" = "ReentrySync-Installer" }

    # ------------------------------------------------------------------
    # 2. Install BepInEx 5 (x64) if missing
    # ------------------------------------------------------------------
    Write-Step "Checking BepInEx $BepInExVersion (win_x64)..."
    $bepDll = Join-Path $GameDir "BepInEx\core\BepInEx.dll"
    $winhttp = Join-Path $GameDir "winhttp.dll"

    if ((Test-Path $bepDll) -and (Test-Path $winhttp) -and (-not $ForceBepInEx)) {
        Write-Ok "BepInEx is already installed."
    } else {
        $bepUrl = "https://github.com/BepInEx/BepInEx/releases/download/v$BepInExVersion/BepInEx_win_x64_$BepInExVersion.zip"
        $bepZip = Join-Path $tempDir "BepInEx.zip"
        Write-Host "  Downloading BepInEx v$BepInExVersion from GitHub..."
        Invoke-WebRequest -Uri $bepUrl -OutFile $bepZip -Headers $headers -UseBasicParsing
        Write-Host "  Extracting BepInEx into $GameDir ..."
        Expand-Archive -Path $bepZip -DestinationPath $GameDir -Force
        Write-Ok "BepInEx v$BepInExVersion installed!"
    }

    $pluginsDir = Join-Path $GameDir "BepInEx\plugins"
    New-Item -ItemType Directory -Force -Path $pluginsDir | Out-Null

    # ------------------------------------------------------------------
    # 3. Download & Install ReentrySync Mod from GitHub Releases
    # ------------------------------------------------------------------
    Write-Step "Fetching ReentrySync release ($Version) from github.com/$Repo ..."
    $apiUrl = if ($Version -eq "latest") {
        "https://api.github.com/repos/$Repo/releases/latest"
    } else {
        "https://api.github.com/repos/$Repo/releases/tags/$Version"
    }

    $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing
    $asset = $release.assets | Where-Object { $_.name -like "ReentrySync-Mod*.zip" } | Select-Object -First 1

    if (-not $asset) {
        throw "Could not find a ReentrySync-Mod*.zip asset in release $($release.tag_name)."
    }

    Write-Ok "Found release $($release.tag_name): $($asset.name)"
    $modZip = Join-Path $tempDir $asset.name
    $modExtract = Join-Path $tempDir "mod"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $modZip -Headers $headers -UseBasicParsing
    Expand-Archive -Path $modZip -DestinationPath $modExtract -Force

    # Copy DLLs into BepInEx\plugins
    $copied = 0
    Get-ChildItem -Path $modExtract -Recurse -Filter "*.dll" | ForEach-Object {
        Copy-Item $_.FullName -Destination (Join-Path $pluginsDir $_.Name) -Force
        Write-Ok "Installed $($_.Name) -> BepInEx\plugins\"
        $copied++
    }

    if ($copied -eq 0) {
        throw "No DLL files were found inside $($asset.name)."
    }

    # ------------------------------------------------------------------
    # 4. Final Verification
    # ------------------------------------------------------------------
    Write-Step "Verifying installation..."
    $required = @(
        (Join-Path $GameDir "winhttp.dll"),
        (Join-Path $GameDir "BepInEx\core\BepInEx.dll"),
        (Join-Path $pluginsDir "ReentrySync.dll"),
        (Join-Path $pluginsDir "Fleck.dll")
    )
    foreach ($req in $required) {
        if (-not (Test-Path $req)) {
            throw "Verification failed: missing $req"
        }
    }

    Write-Host "`n============================================================" -ForegroundColor Green
    Write-Host "  INSTALLATION COMPLETE! ($($release.tag_name))" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "  1. Launch Reentry normally (via Steam or ReEntry.exe)." -ForegroundColor White
    Write-Host "  2. Open your browser to:  http://localhost:8080/" -ForegroundColor Yellow
    Write-Host "  3. Use Smart Checklists, Autopilot, Burn Planner, Lunar Map & Crew Co-op!" -ForegroundColor White
    Write-Host ""
}
finally {
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
