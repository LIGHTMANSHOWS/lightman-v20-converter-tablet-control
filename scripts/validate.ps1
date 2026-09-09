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

$PythonExecutable = $null
$PythonUsesLauncher = $false
if (Get-Command py -ErrorAction SilentlyContinue) {
    try {
        py -3 --version *> $null
        if ($LASTEXITCODE -eq 0) { $PythonUsesLauncher = $true }
    } catch {
        # Windows puede conservar el lanzador `py.exe` aunque ya no tenga un
        # runtime registrado. En ese caso continuamos con la búsqueda directa.
        $PythonUsesLauncher = $false
    }
}
if (-not $PythonUsesLauncher) {
    $PythonExecutable = (Get-Command python -ErrorAction SilentlyContinue).Source
}
if (-not $PythonUsesLauncher -and -not $PythonExecutable) {
    $PythonInstallRoot = Join-Path $env:LOCALAPPDATA 'Programs\Python'
    foreach ($PythonVersion in 314..38) {
        $PythonCandidate = Join-Path $PythonInstallRoot "Python$PythonVersion\python.exe"
        if (Test-Path -LiteralPath $PythonCandidate) {
            $PythonExecutable = $PythonCandidate
            break
        }
    }
}
if ($PythonUsesLauncher) {
    py -3 (Join-Path $RepoRoot 'integration\Tests\verify_package.py')
} elseif ($PythonExecutable) {
    & $PythonExecutable (Join-Path $RepoRoot 'integration\Tests\verify_package.py')
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
dotnet run --project $Project -c Release --no-build -- --self-test-xschedule-sync
if ($LASTEXITCODE -ne 0) { throw 'Falló el self-test aislado del sincronizador xSchedule.' }

Write-Host "VALIDACION COMPLETA · SHA256 $sourceHash"
