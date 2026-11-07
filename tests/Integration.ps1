param([switch]$ExternalInput, [switch]$NativeOnly, [string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$outputDir = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sources = @(Join-Path $PSScriptRoot 'Integration.cs')
$sources += @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /warnaserror+ /target:exe /main:Integration /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$outputDir\Integration.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Integration compilation failed.' }
$release = Get-ChildItem -LiteralPath $root -Filter '*.exe' | Select-Object -First 1
if ($ExecutablePath) { $release = Get-Item -LiteralPath $ExecutablePath }
if (-not $release) { throw 'Build release EXE first.' }
if ($NativeOnly) {
    & (Join-Path $outputDir 'Integration.exe') $release.FullName 'native-only'
} elseif ($ExternalInput) {
    & (Join-Path $outputDir 'Integration.exe') $release.FullName 'external-input'
} else {
    & (Join-Path $outputDir 'Integration.exe') $release.FullName
}
if ($LASTEXITCODE -ne 0) { throw 'Integration checks failed.' }
