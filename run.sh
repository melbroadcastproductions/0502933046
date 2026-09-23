#!/usr/bin/env bash
# Launch script for NDI Video Dockers Manager & Worker Suite
set -e

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
cd "$SCRIPT_DIR"

echo "=================================================="
echo " Starting NDI Video Dockers Manager (.NET 10)..."
echo "=================================================="

# Check if dotnet is installed
if ! command -v dotnet &> /dev/null; then
    echo "[Error] .NET SDK ('dotnet') is not installed or not in PATH."
    exit 1
fi

echo "[1/2] Building workspace solution..."
dotnet build NdiVideoDockers.slnx -c Release

echo "[2/2] Launching NdiManager web service on http://localhost:8000..."
dotnet run --project NdiManager/NdiManager.csproj -c Release
