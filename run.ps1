# PowerShell Launch Script for NDI Video Dockers Manager Suite
$PSScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
Set-Location $PSScriptRoot

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host " Starting NDI Video Dockers Manager (.NET 10)" -ForegroundColor Green
Write-Host "==================================================" -ForegroundColor Cyan

if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Error "[Error] .NET SDK ('dotnet') is not installed or not in PATH."
    exit 1
}

Write-Host "[1/2] Building workspace solution..." -ForegroundColor Yellow
dotnet build NdiVideoDockers.slnx -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "[Error] Build failed."
    exit $LASTEXITCODE
}

Write-Host "[2/2] Launching NdiManager web service on http://localhost:8000..." -ForegroundColor Green
dotnet run --project NdiManager/NdiManager.csproj -c Release
