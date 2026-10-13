[CmdletBinding()]
param(
    [string[]]$ShortcutDirectories = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')),
    [string]$PublishDirectory,
    [string]$PreviousPublishDirectory
)
$ErrorActionPreference = 'Stop'
$shortcutEntry = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'Start-MessagePurify.ps1')).Path
$shortcutReady = & $shortcutEntry -PublishDirectory $PublishDirectory -CheckOnly | ConvertFrom-Json
if (-not $shortcutReady.Ready) { throw 'Application launcher is not ready.' }
$shortcutShell = New-Object -ComObject WScript.Shell
$shortcutTarget = $shortcutReady.Executable
$shortcutAllowedTargets = @($shortcutTarget)
if (-not [string]::IsNullOrWhiteSpace($PreviousPublishDirectory)) {
    $shortcutAllowedTargets += (Resolve-Path -LiteralPath (Join-Path $PreviousPublishDirectory 'MessagePurify.exe')).Path
}
$shortcutPaths = @($ShortcutDirectories | ForEach-Object { Join-Path ([IO.Path]::GetFullPath($_)) '消息提纯.lnk' } | Select-Object -Unique)
# Check existing entries before creating either shortcut; preserve unrelated files.
foreach ($shortcutPath in $shortcutPaths) {
    if (Test-Path -LiteralPath $shortcutPath) {
        if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) { throw 'Shortcut destination is not a file. Existing entry was preserved.' }
        $shortcutExisting = $shortcutShell.CreateShortcut($shortcutPath)
        if ($shortcutExisting.TargetPath -notin $shortcutAllowedTargets -or $shortcutExisting.Arguments -ne '') {
            throw 'Shortcut destination belongs to another entry. Existing shortcut was preserved.'
        }
    }
}
foreach ($shortcutPath in $shortcutPaths) {
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $shortcutPath) -Force)
    $shortcutLink = $shortcutShell.CreateShortcut($shortcutPath)
    $shortcutLink.TargetPath = $shortcutTarget
    $shortcutLink.Arguments = ''
    $shortcutLink.WorkingDirectory = Split-Path -Parent $shortcutTarget
    $shortcutLink.IconLocation = $shortcutReady.Executable + ',0'
    $shortcutLink.Description = '消息提纯'
    $shortcutLink.WindowStyle = 1
    $shortcutLink.Save()
}
[pscustomobject]@{ Shortcuts = $shortcutPaths; Executable = $shortcutTarget } | ConvertTo-Json
