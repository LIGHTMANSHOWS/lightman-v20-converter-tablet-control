$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot 'src\LightmanV20\ZapravkaStage3D.csproj'
$Manifest = Join-Path $RepoRoot 'src\LightmanV20\Patch\SHOW-V20-MINIMAL.json'
$PortableManifest = Join-Path $RepoRoot 'integration\Data\SHOW-V20-MINIMAL-V4-PORTABLE.json'

$sourceHash = (Get-FileHash -LiteralPath $Manifest -Algorithm SHA256).Hash
$portableHash = (Get-FileHash -LiteralPath $PortableManifest -Algorithm SHA256).Hash
if ($sourceHash -ne $portableHash) {
    throw 'Las copias del manifiesto V4 no coinciden.'
}

if (Get-Command py -ErrorAction SilentlyContinue) {
    py -3 (Join-Path $RepoRoot 'integration\Tests\verify_package.py')
} elseif (Get-Command python -ErrorAction SilentlyContinue) {
    python (Join-Path $RepoRoot 'integration\Tests\verify_package.py')
} else {
    throw 'Se requiere Python 3 para validar el paquete de integración.'
}
if ($LASTEXITCODE -ne 0) {
    throw 'Falló la validación Python del paquete de integración.'
}
node (Join-Path $RepoRoot 'src\LightmanV20\tests\manifest-v4-orientation.test.mjs')
if ($LASTEXITCODE -ne 0) { throw 'Falló la validación JavaScript del mapeo.' }
dotnet build $Project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de V20.' }
dotnet run --project $Project -c Release -- --self-test-minimal
if ($LASTEXITCODE -ne 0) { throw 'Falló el self-test de V20.' }

Write-Host "VALIDACION COMPLETA · SHA256 $sourceHash"
