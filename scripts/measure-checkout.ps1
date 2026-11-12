$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:RETAILCORE_PERFORMANCE_DIR = Join-Path $root 'artifacts/performance'
dotnet build $root -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
dotnet (Join-Path $root 'tests/RetailCore.Foundation.Tests/bin/Release/net10.0/RetailCore.Foundation.Tests.dll') -explicit only -method '*Measure_isolated_ten_thousand*'
if ($LASTEXITCODE -ne 0) { throw 'Measurement failed.' }
Get-Content (Join-Path $env:RETAILCORE_PERFORMANCE_DIR 'checkout-10000.json')
Write-Output 'Temporary database only. Measure WPF cold start, bound-table search and real scanner/printer separately on the intended shop PC.'
