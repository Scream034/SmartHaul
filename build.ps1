param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ModName = "SmartHaul"
$ProjectRoot = $PSScriptRoot
$OutputDir = Join-Path (Join-Path $ProjectRoot "dist") $ModName

if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "Building $Configuration..." -ForegroundColor Cyan
dotnet build "$ProjectRoot\SmartHaul.sln" -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$folders = @("1.6", "About", "Defs", "Languages", "Patches")
foreach ($folder in $folders) {
    $src = Join-Path $ProjectRoot $folder
    if (Test-Path $src) {
        $dst = Join-Path $OutputDir $folder
        Copy-Item $src $dst -Recurse -Force
        Write-Host "  Copied $folder" -ForegroundColor Green
    }
}

if ($Configuration -eq "Release") {
    Get-ChildItem $OutputDir -Recurse -Filter "*.pdb" | Remove-Item -Force
    Write-Host "  Removed PDB files" -ForegroundColor Yellow
}

Write-Host "`nMod packed to: $OutputDir" -ForegroundColor Green