[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'MessagePurify/IntegrationProbe')
)
$ErrorActionPreference = 'Stop'
$probeSource = (Resolve-Path -LiteralPath $PublishDirectory).Path
$probeTarget = [IO.Path]::GetFullPath($InstallDirectory)
foreach ($probeRequired in @('MessagePurify.Windows.IntegrationProbe.exe', 'ui/index.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $probeSource $probeRequired) -PathType Leaf)) {
        throw "Published probe is incomplete: $probeRequired"
    }
}
if ($probeSource.TrimEnd('\', '/') -eq $probeTarget.TrimEnd('\', '/')) {
    throw 'Publish source and install directory must differ.'
}
if ($PSCmdlet.ShouldProcess($probeTarget, 'Install the integration probe for the current user')) {
    New-Item -ItemType Directory -Path $probeTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $probeSource -Force | Copy-Item -Destination $probeTarget -Recurse -Force
    Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object {
        $_.Name -eq 'Run-Probe.ps1' -or $_.Extension -eq '.cmd'
    } | Copy-Item -Destination $probeTarget -Force
    Write-Output $probeTarget
}
