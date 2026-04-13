param(
    [int]$Clients = 1
)

if (-not $env:RIMWORLD_PATH) {
    Write-Host "RIMWORLD_PATH not set! Run setup_dev.ps1 first." -ForegroundColor Red
    exit 1
}

$exePath = "$env:RIMWORLD_PATH\RimWorldWin64.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "RimWorld not found: $exePath" -ForegroundColor Red
    exit 1
}

$processes = @()

Write-Host "Starting RimWorld..." -ForegroundColor Cyan

for ($i = 1; $i -le $Clients; $i++) {
    $proc = Start-Process -FilePath $exePath -PassThru
    $processes += $proc
    Write-Host "  Client $i started (PID: $($proc.Id))" -ForegroundColor Green
    
    if ($i -lt $Clients) {
        Start-Sleep -Seconds 6
    }
}

Write-Host ""
Write-Host "Press Ctrl+C to close all RimWorld instances..." -ForegroundColor Yellow
Write-Host ""

try {
    # Wait for any process to exit
    while ($true) {
        $running = $processes | Where-Object { -not $_.HasExited }
        if ($running.Count -eq 0) {
            Write-Host "All RimWorld instances closed." -ForegroundColor Gray
            break
        }
        Start-Sleep -Milliseconds 500
    }
}
finally {
    # Cleanup on Ctrl+C or exit
    foreach ($proc in $processes) {
        if (-not $proc.HasExited) {
            Write-Host "Closing PID $($proc.Id)..." -ForegroundColor Yellow
            $proc.CloseMainWindow() | Out-Null
            Start-Sleep -Milliseconds 500
            if (-not $proc.HasExited) {
                $proc.Kill()
            }
        }
    }
    Write-Host "Done." -ForegroundColor Green
}