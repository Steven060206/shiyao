[CmdletBinding()]
param(
    [ValidateSet('Bridge', 'Desktop', 'NormalNotification', 'SilentNotification', 'ColdNotification')]
    [string]$Mode = 'Bridge',
    [ValidateRange(1,600)][int]$WaitSeconds = 60
)
$ErrorActionPreference = 'Stop'
$probeExecutable = Join-Path $PSScriptRoot 'MessagePurify.Windows.IntegrationProbe.exe'
if (-not (Test-Path -LiteralPath $probeExecutable -PathType Leaf)) {
    throw 'Install the published probe before running this entry.'
}
$probeResults = Join-Path $PSScriptRoot 'results'
New-Item -ItemType Directory -Path $probeResults -Force | Out-Null
$probeReport = Join-Path $probeResults ($Mode + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.json')
[string[]]$probeArguments = switch ($Mode) {
    'Bridge' { @('--bridge') }
    'Desktop' { @('--interactive-desktop') }
    'NormalNotification' { @('--notification', 'normal') }
    'SilentNotification' { @('--notification', 'silent') }
    'ColdNotification' { @('--notification', 'normal', '--exit-after-send') }
}
$probeArguments += @('--wait-seconds', "$WaitSeconds", '--report', ('"' + $probeReport + '"'))
$probeProcess = Start-Process -FilePath $probeExecutable -ArgumentList $probeArguments -WindowStyle Hidden -Wait -PassThru
Write-Output "Report: $probeReport"
exit $probeProcess.ExitCode
