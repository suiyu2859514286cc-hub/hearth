# Build Hearth into one self-contained Hearth.exe at the project root
# Usage: right-click -> Run with PowerShell, or  .\build.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"

Write-Host "Stopping running Hearth..." -ForegroundColor DarkGray
Get-Process Hearth -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Publishing (self-contained single file, takes a while)..." -ForegroundColor Cyan
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
Push-Location $src
try {
    dotnet publish Hearth.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $dist --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
} finally {
    Pop-Location
}

Write-Host "Copying exe to project root..." -ForegroundColor Cyan
Copy-Item (Join-Path $dist "Hearth.exe") (Join-Path $root "Hearth.exe") -Force

$exe = Join-Path $root "Hearth.exe"
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host ("Done: " + $exe + "  " + $size + " MB") -ForegroundColor Green
Write-Host "Self-contained: .NET runtime and images are inside. Just copy this one file." -ForegroundColor DarkGray
