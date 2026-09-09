$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot 'src\LightmanV20\ZapravkaStage3D.csproj'
$Destination = Join-Path $RepoRoot 'publish\win-x64'

dotnet publish $Project -c Release -r win-x64 --self-contained true -o $Destination
Write-Host "Portable generado en $Destination"

