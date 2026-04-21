#!/usr/bin/env pwsh
# GymForge — build + test script
# Usage: .\scripts\build.ps1 [-Configuration Release] [-SkipTests]
param(
    [string]$Configuration = "Debug",
    [switch]$SkipTests,
    [switch]$Publish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent

Write-Host "=== GymForge Build ===" -ForegroundColor Cyan
Write-Host "Configuration : $Configuration"
Write-Host "Root          : $root"

# Restore
Write-Host "`n[1/4] Restoring packages..." -ForegroundColor Yellow
dotnet restore "$root\GymForge.sln"

# Build
Write-Host "`n[2/4] Building solution..." -ForegroundColor Yellow
dotnet build "$root\GymForge.sln" -c $Configuration --no-restore

if (-not $SkipTests) {
    # Test
    Write-Host "`n[3/4] Running tests..." -ForegroundColor Yellow
    dotnet test "$root\GymForge.sln" -c $Configuration --no-build `
        --logger "console;verbosity=normal" `
        --collect:"XPlat Code Coverage"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Tests FAILED" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ All tests passed" -ForegroundColor Green
}

if ($Publish) {
    Write-Host "`n[4/4] Publishing Desktop app..." -ForegroundColor Yellow
    dotnet publish "$root\src\GymForge.Desktop\GymForge.Desktop.csproj" `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -o "$root\dist\GymForge"
    Write-Host "✅ Published to $root\dist\GymForge" -ForegroundColor Green
}

Write-Host "`n✅ Build complete!" -ForegroundColor Green
