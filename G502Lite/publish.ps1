$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'G502Lite.csproj'
$workspacePath = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $workspacePath 'dist\G502Lite'
dotnet publish $projectPath -c Release --no-self-contained -p:UseSharedCompilation=false -p:DebugType=None -o $outputPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar G502 Lite.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $outputPath 'LEEME.md')
Get-ChildItem -LiteralPath $outputPath -File | Select-Object Name, Length
