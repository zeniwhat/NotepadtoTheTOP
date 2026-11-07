param([ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$outputName = ([char]0x8bb0).ToString() + [char]0x4e8b + [char]0x672c + [char]0x7f6e + [char]0x9876 + '.exe'
$source = Join-Path $PSScriptRoot $outputName
if (-not (Test-Path -LiteralPath $source)) { throw 'Build the EXE before packaging.' }
$releaseDir = Join-Path $PSScriptRoot "dist\v$Version"
$portableDir = Join-Path $releaseDir 'portable'
New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
$exe = Join-Path $releaseDir 'NotepadPin.exe'
Copy-Item -LiteralPath $source -Destination $exe -Force
Copy-Item -LiteralPath $source -Destination (Join-Path $portableDir $outputName) -Force
$instructions = @"
NotepadPin v$Version

Windows 10 / Windows 11. Portable: no installation required.
Requires .NET Framework 4.x (normally included with Windows 10/11).

1. Double-click the EXE. The program runs silently in the system tray.
2. Select a Windows Notepad window and press Ctrl + Alt + T to pin it.
3. Press the same shortcut again to unpin.

Look for a blue pin icon in the system tray (or its hidden-icons menu).
Right-click it to select a window, unpin all windows, or exit.
On supported Windows, a cyan native border marks a pinned window.
Older systems fall back to a separate border overlay.

If Notepad runs as administrator, run this tool as administrator too.
If the shortcut is occupied, release it and restart the tool, or use its tray menu.
Normal exit restores pins created by this tool. Forced termination may leave pins set.
No auto-start, text changes, network access or external downloads are performed by the tool.
Status and errors may be written beside the EXE, so use a writable local folder.

Project: https://github.com/zeniwhat/NotepadtoTheTOP
"@
Set-Content -Encoding UTF8 -LiteralPath (Join-Path $portableDir 'README.txt') -Value $instructions
$zip = Join-Path $releaseDir "NotepadPin-v$Version-portable.zip"
Compress-Archive -LiteralPath (Join-Path $portableDir $outputName),(Join-Path $portableDir 'README.txt') -DestinationPath $zip -Force
$hashes = foreach ($file in @($exe, $zip)) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $file
    $hash.Hash.ToLowerInvariant() + '  ' + [System.IO.Path]::GetFileName($file)
}
Set-Content -Encoding ASCII -LiteralPath (Join-Path $releaseDir 'SHA256SUMS.txt') -Value $hashes
Get-ChildItem -LiteralPath $releaseDir -File | Select-Object Name,Length
