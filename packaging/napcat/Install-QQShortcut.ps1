[CmdletBinding()]
param(
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'QQ.lnk'),
    [string]$QqExecutablePath = 'D:/QQ/QQ.exe',
    [string]$BackupDirectory = (Join-Path $env:LOCALAPPDATA 'MessagePurify/Shortcuts')
)
$ErrorActionPreference = 'Stop'
$entry = Join-Path $PSScriptRoot 'Start-NapCat-Framework.ps1'
$ready = & $entry -QqExecutablePath $QqExecutablePath -CheckOnly | ConvertFrom-Json
if (-not $ready.Ready) { throw 'QQ launcher is not ready.' }
$qqPath = $ready.QqExecutable
$backupPath = Join-Path $BackupDirectory 'QQ.original.lnk'
if (Test-Path -LiteralPath $ShortcutPath -PathType Leaf) {
    if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
        [void](New-Item -ItemType Directory -Path $BackupDirectory -Force)
        Copy-Item -LiteralPath $ShortcutPath -Destination $backupPath
    }
}
$shortcutFolder = Split-Path -Parent ([IO.Path]::GetFullPath($ShortcutPath))
[void](New-Item -ItemType Directory -Path $shortcutFolder -Force)
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut([IO.Path]::GetFullPath($ShortcutPath))
$shortcut.TargetPath = Join-Path $env:SystemRoot 'System32/WindowsPowerShell/v1.0/powershell.exe'
$shortcut.Arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + $entry + '" -QqExecutablePath "' + $qqPath + '" -OpenExisting'
$shortcut.WorkingDirectory = $PSScriptRoot
$shortcut.IconLocation = $qqPath + ',0'
$shortcut.Description = 'QQ / MessagePurify'
$shortcut.WindowStyle = 7
$shortcut.Save()
[pscustomobject]@{ Shortcut = $ShortcutPath; OriginalBackup = $backupPath; QqExecutable = $qqPath } | ConvertTo-Json
