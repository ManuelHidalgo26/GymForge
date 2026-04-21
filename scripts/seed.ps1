#!/usr/bin/env pwsh
# Re-runs the DatabaseSeeder against a fresh SQLite DB
# Useful for dev resets: .\scripts\seed.ps1
param([string]$DbPath = "$env:LOCALAPPDATA\GymForge\gymforge.db")

Write-Host "Seeding DB at: $DbPath" -ForegroundColor Cyan

if (Test-Path $DbPath) {
    Remove-Item $DbPath -Force
    Write-Host "Removed existing DB" -ForegroundColor Yellow
}

dotnet run --project "$PSScriptRoot\..\src\GymForge.Api" -- --seed-only
Write-Host "✅ Seed complete" -ForegroundColor Green
