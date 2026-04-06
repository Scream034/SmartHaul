<#
.SYNOPSIS
    Setup development environment for SmartHaul RimWorld mod.
#>

param(
    [string]$RimWorldPath
)

$ErrorActionPreference = "Stop"

Write-Host ""
Write-Host "=== SmartHaul Development Environment Setup ===" -ForegroundColor Cyan
Write-Host ""

# Check existing
$existingPath = [Environment]::GetEnvironmentVariable("RIMWORLD_PATH", "User")
if ($existingPath -and (Test-Path "$existingPath\RimWorldWin64.exe")) {
    Write-Host "RIMWORLD_PATH already set to:" -ForegroundColor Green
    Write-Host "  $existingPath" -ForegroundColor White
    Write-Host ""
    
    $confirm = Read-Host "Keep this path? (Y/n)"
    if ($confirm -ne "n" -and $confirm -ne "N") {
        Write-Host ""
        Write-Host "Setup complete! Restart VS Code if needed." -ForegroundColor Green
        Write-Host ""
        Read-Host "Press Enter to close"
        exit 0
    }
}

# Common paths
$commonPaths = @(
    "C:\Program Files (x86)\Steam\steamapps\common\RimWorld",
    "D:\Steam\steamapps\common\RimWorld",
    "D:\SteamLibrary\steamapps\common\RimWorld",
    "E:\Steam\steamapps\common\RimWorld",
    "D:\Games\RimWorld",
    "C:\Games\RimWorld"
)

# Auto-detect
$detectedPath = $null
foreach ($path in $commonPaths) {
    if (Test-Path "$path\RimWorldWin64.exe") {
        $detectedPath = $path
        break
    }
}

if ($detectedPath -and -not $RimWorldPath) {
    Write-Host "Found RimWorld at:" -ForegroundColor Green
    Write-Host "  $detectedPath" -ForegroundColor Cyan
    Write-Host ""
    $useDetected = Read-Host "Use this path? (Y/n)"
    if ($useDetected -ne "n" -and $useDetected -ne "N") {
        $RimWorldPath = $detectedPath
    }
}

# Manual input if needed
if (-not $RimWorldPath) {
    Write-Host "Enter RimWorld installation path:" -ForegroundColor Yellow
    Write-Host "(folder containing RimWorldWin64.exe)" -ForegroundColor Gray
    Write-Host ""
    $RimWorldPath = Read-Host "Path"
}

# Validate
$RimWorldPath = $RimWorldPath.Trim('"', "'", " ")

if (-not (Test-Path $RimWorldPath)) {
    Write-Host "ERROR: Path does not exist: $RimWorldPath" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

$exePath = Join-Path $RimWorldPath "RimWorldWin64.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: RimWorldWin64.exe not found in: $RimWorldPath" -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

# Set environment variable
[Environment]::SetEnvironmentVariable("RIMWORLD_PATH", $RimWorldPath, "User")

# Also set for current session
$env:RIMWORLD_PATH = $RimWorldPath

Write-Host ""
Write-Host "SUCCESS!" -ForegroundColor Green
Write-Host ""
Write-Host "RIMWORLD_PATH = $RimWorldPath" -ForegroundColor Cyan
Write-Host ""

# Create junction
$modsPath = Join-Path $RimWorldPath "Mods"
$projectRoot = $PSScriptRoot
$modName = "SmartHaul"
$targetLink = Join-Path $modsPath $modName

Write-Host "---" -ForegroundColor Gray
$createLink = Read-Host "Create symlink in Mods folder? (Y/n)"

if ($createLink -ne "n" -and $createLink -ne "N") {
    try {
        if (Test-Path $targetLink) {
            $item = Get-Item $targetLink -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                cmd /c rmdir "$targetLink" 2>$null
            } else {
                Remove-Item $targetLink -Recurse -Force
            }
        }
        
        New-Item -ItemType Junction -Path $targetLink -Target $projectRoot -Force | Out-Null
        Write-Host "Created: $targetLink" -ForegroundColor Green
    }
    catch {
        Write-Host "Could not create symlink: $_" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host " IMPORTANT: Restart VS Code now!" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "After restart:" -ForegroundColor White
Write-Host "  F5        = Build & Run RimWorld" -ForegroundColor Gray
Write-Host "  Ctrl+F5   = Build only" -ForegroundColor Gray
Write-Host "  Shift+F5  = Build & Run 2 clients" -ForegroundColor Gray
Write-Host ""

Read-Host "Press Enter to close"