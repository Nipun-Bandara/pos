param([string]$Version = '0.1.8', [string]$Compiler)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part numeric version.' }
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts/publish/win-x64'
if (-not $Compiler) { $Compiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
if (-not (Test-Path $Compiler)) { throw 'Install Inno Setup 6 from https://jrsoftware.org/isdl.php or pass -Compiler.' }
dotnet publish (Join-Path $root 'src/RetailCore.Desktop') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishSingleFile=false -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
& $Compiler "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $root 'installer/RetailCore.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Get-FileHash (Join-Path $root "artifacts/installer/RetailCore-$Version-win-x64-setup.exe") -Algorithm SHA256 |
    Format-List | Out-File (Join-Path $root 'artifacts/installer/SHA256.txt') -Encoding utf8
