[CmdletBinding()]
param(
    [string]$QqExecutablePath = 'D:/QQ/QQ.exe',
    [string]$PackageDirectory,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $PSScriptRoot '../../.tools/napcat/v4.18.37'
}
$napPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$napQq = (Resolve-Path -LiteralPath $QqExecutablePath).Path
if (-not (Test-Path -LiteralPath $napQq -PathType Leaf)) { throw 'QQ executable is missing.' }
$napEnvironment = @{
    NAPCAT_PATCH_PACKAGE = 'qqnt.json'
    NAPCAT_LOAD_PATH = 'loadNapCat.js'
    NAPCAT_INJECT_PATH = 'NapCatWinBootHook.dll'
    NAPCAT_LAUNCHER_PATH = 'NapCatWinBootMain.exe'
    NAPCAT_MAIN_PATH = 'napcat.mjs'
}
foreach ($napKey in @($napEnvironment.Keys)) {
    $napEnvironment[$napKey] = Join-Path $napPackage $napEnvironment[$napKey]
    if (-not (Test-Path -LiteralPath $napEnvironment[$napKey] -PathType Leaf)) {
        throw "NapCat dependency is missing: $napKey"
    }
}
if ($CheckOnly) {
    [pscustomobject]@{ Ready = $true; QqExecutable = $napQq; PackageDirectory = $napPackage; RegistryRequired = $false } | ConvertTo-Json
    return
}
[Console]::InputEncoding = [Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Console]::OutputEncoding
$napPrevious = @{}
try {
    foreach ($napKey in $napEnvironment.Keys) {
        $napPrevious[$napKey] = [Environment]::GetEnvironmentVariable($napKey, 'Process')
        [Environment]::SetEnvironmentVariable($napKey, $napEnvironment[$napKey], 'Process')
    }
    Push-Location -LiteralPath $napPackage
    try {
        & $napEnvironment.NAPCAT_LAUNCHER_PATH $napQq $napEnvironment.NAPCAT_INJECT_PATH
        $napExit = $LASTEXITCODE
    } finally { Pop-Location }
} finally {
    foreach ($napKey in $napPrevious.Keys) {
        [Environment]::SetEnvironmentVariable($napKey, $napPrevious[$napKey], 'Process')
    }
}
exit $napExit
