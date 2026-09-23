@echo off
title NDI Video Dockers Suite Manager
cd /d "%~dp0"

echo ==================================================
echo  Starting NDI Video Dockers Manager (.NET 10)
echo ==================================================

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [Error] .NET SDK ('dotnet') is not installed or not in PATH.
    pause
    exit /b 1
)

echo [1/2] Building workspace solution...
dotnet build NdiVideoDockers.slnx -c Release
if %errorlevel% neq 0 (
    echo [Error] Build failed.
    pause
    exit /b 1
)

echo [2/2] Launching NdiManager web service on http://localhost:8000...
dotnet run --project NdiManager\NdiManager.csproj -c Release

pause
