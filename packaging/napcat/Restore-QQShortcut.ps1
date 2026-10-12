[CmdletBinding()]
param(
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'QQ.lnk'),
    [string]$QqExecutablePath = 'D:/QQ/QQ.exe'
)
$ErrorActionPreference = 'Stop'
$qqPath = (Resolve-Path -LiteralPath $QqExecutablePath).Path
if (-not (Test-Path -LiteralPath $qqPath -PathType Leaf)) { throw 'QQ executable is missing.' }
$linkPath = [IO.Path]::GetFullPath($ShortcutPath)
[void][IO.Directory]::CreateDirectory((Split-Path -Parent $linkPath))
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($linkPath)
$shortcut.TargetPath = $qqPath
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = Split-Path -Parent $qqPath
$shortcut.IconLocation = $qqPath + ',0'
$shortcut.Description = 'QQ'
$shortcut.WindowStyle = 1
$shortcut.Save()
if (-not (Test-Path -LiteralPath $linkPath -PathType Leaf)) { throw 'QQ shortcut was not saved.' }
[pscustomobject]@{ Shortcut = $linkPath; Target = $shortcut.TargetPath; Arguments = $shortcut.Arguments } | ConvertTo-Json
