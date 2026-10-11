[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$pinnedPackage = (Resolve-Path -LiteralPath $PackageDirectory).Path
$pinnedOriginal = Join-Path $pinnedPackage 'napcat.mjs'
$pinnedRuntime = Join-Path $pinnedPackage 'messagepurify-napcat.mjs'
$pinnedSource = [IO.File]::ReadAllText($pinnedOriginal, [Text.Encoding]::UTF8)
if ($pinnedSource -notmatch 'class OneBotAction' -or $pinnedSource -notmatch 'const WebUiDataRuntime') {
    throw 'This NapCat build does not support the pinned group bridge.'
}
# Keep the official bundle intact; add exports only in a generated local copy.
[IO.File]::WriteAllText($pinnedRuntime, $pinnedSource + "`nexport { OneBotAction, WebUiDataRuntime };`n", [Text.UTF8Encoding]::new($false))
foreach ($pinnedFile in @('nativeLoaderPinned.cjs', 'pinned-groups.mjs')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $pinnedFile) -Destination (Join-Path $pinnedPackage $pinnedFile) -Force
}
