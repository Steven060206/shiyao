[CmdletBinding()]
param(
    [string]$QqExecutablePath = 'D:/QQ/QQ.exe',
    [string]$PackageDirectory,
    [switch]$CheckOnly,
    [switch]$OpenExisting
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $PSScriptRoot '../../.tools/napcat/framework/v4.18.37/package'
}
$napPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$napQq = (Resolve-Path -LiteralPath $QqExecutablePath).Path
if (-not (Test-Path -LiteralPath $napQq -PathType Leaf)) { throw 'QQ executable is missing.' }
$napLauncher = Join-Path $napPackage 'napimain.exe'
$napDll = Join-Path $napPackage 'napiloader.dll'
$napScript = Join-Path $napPackage 'nativeLoader.cjs'
$napRuntime = Join-Path $napPackage 'napcat.mjs'
foreach ($napPath in @($napLauncher, $napDll, $napScript, $napRuntime)) {
    if (-not (Test-Path -LiteralPath $napPath -PathType Leaf)) {
        throw "NapCat Framework dependency is missing: $napPath"
    }
}
if ($CheckOnly) {
    [pscustomobject]@{
        Ready = $true
        Mode = 'Framework'
        QqExecutable = $napQq
        PackageDirectory = $napPackage
        Launcher = $napLauncher
        RegistryRequired = $false
    } | ConvertTo-Json
    return
}
if (Get-Process -Name 'QQ' -ErrorAction SilentlyContinue) {
    if ($OpenExisting) {
        Start-Process -FilePath $napQq -WorkingDirectory (Split-Path -Parent $napQq)
        return
    }
    throw 'QQ.exe is already running. Exit the current QQ before starting NapCat Framework.'
}
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
Push-Location -LiteralPath $napPackage
try {
    & (Join-Path $PSScriptRoot 'Prepare-PinnedGroups.ps1') -PackageDirectory $napPackage
    $napPinnedScript = Join-Path $napPackage 'nativeLoaderPinned.cjs'
    & $napLauncher $napQq $napDll $napPinnedScript
    $napExit = $LASTEXITCODE
} finally { Pop-Location }
exit $napExit
