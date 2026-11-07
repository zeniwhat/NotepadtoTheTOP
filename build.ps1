param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$outputName = ([char]0x8bb0).ToString() + [char]0x4e8b + [char]0x672c + [char]0x7f6e + [char]0x9876 + '.exe'
$output = Join-Path $PSScriptRoot $outputName
if ($OutputPath) {
    $output = [System.IO.Path]::GetFullPath($OutputPath)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
}
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /warnaserror+ /optimize+ /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll "/win32manifest:$PSScriptRoot\app.manifest" "/out:$output" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Get-Item -LiteralPath $output | Select-Object FullName,Length,LastWriteTime
