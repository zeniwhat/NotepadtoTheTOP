$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
$outputDir = Join-Path $PSScriptRoot 'tests\bin'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sources = @(Join-Path $PSScriptRoot 'tests\Tests.cs')
$sources += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -ErrorAction SilentlyContinue | ForEach-Object FullName)
& $compiler /nologo /warnaserror+ /target:exe /main:Tests /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$outputDir\Tests.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $outputDir 'Tests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
