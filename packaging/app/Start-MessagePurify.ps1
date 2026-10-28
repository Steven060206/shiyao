[CmdletBinding()]
param([string]$PublishDirectory, [string]$NapCatConfigurationDirectory, [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($PublishDirectory)) {
    $PublishDirectory = Join-Path $PSScriptRoot '../../.tools/published/app-card-controls'
}
$appDirectory = (Resolve-Path -LiteralPath $PublishDirectory).Path
$appExecutable = Join-Path $appDirectory 'MessagePurify.exe'
if (-not (Test-Path -LiteralPath $appExecutable -PathType Leaf)) { throw 'Application executable is missing. Publish the app first.' }
if (-not (Test-Path -LiteralPath (Join-Path $appDirectory 'ui/index.html') -PathType Leaf)) { throw 'Application UI is missing.' }
$appToken = [Environment]::GetEnvironmentVariable('MESSAGEPURIFY_NAPCAT_TOKEN', 'Process')
if ([string]::IsNullOrWhiteSpace($NapCatConfigurationDirectory)) {
    $NapCatConfigurationDirectory = Join-Path $PSScriptRoot '../../.tools/napcat/framework/v4.18.37/package/config'
}
if ([string]::IsNullOrEmpty($appToken) -and (Test-Path -LiteralPath $NapCatConfigurationDirectory -PathType Container)) {
    $appConfigurations = @(Get-ChildItem -LiteralPath $NapCatConfigurationDirectory -Filter 'onebot11_*.json' -File)
    $appEndpoints = @()
    foreach ($appConfigurationFile in $appConfigurations) {
        try { $appConfiguration = Get-Content -LiteralPath $appConfigurationFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json }
        catch { throw 'Local NapCat configuration cannot be read. Original configuration was preserved.' }
        $appEndpoints += @($appConfiguration.network.websocketServers | Where-Object {
            $_.enable -eq $true -and $_.port -ge 1 -and $_.port -le 65535 -and $_.host -in @('127.0.0.1', 'localhost', '::1')
        })
    }
    if ($appEndpoints.Count -gt 1) { throw 'Local NapCat configuration is ambiguous. Keep exactly one enabled loopback WebSocket endpoint.' }
    if ($appEndpoints.Count -eq 1) { $appToken = [string]$appEndpoints[0].token }
}
if ($CheckOnly) {
    [pscustomobject]@{ Ready = $true; Executable = $appExecutable; AuthenticationReady = -not [string]::IsNullOrEmpty($appToken) } | ConvertTo-Json
    return
}
$appPreviousToken = [Environment]::GetEnvironmentVariable('MESSAGEPURIFY_NAPCAT_TOKEN', 'Process')
try {
    if (-not [string]::IsNullOrEmpty($appToken)) { [Environment]::SetEnvironmentVariable('MESSAGEPURIFY_NAPCAT_TOKEN', $appToken, 'Process') }
    Start-Process -FilePath $appExecutable -WorkingDirectory $appDirectory
} finally { [Environment]::SetEnvironmentVariable('MESSAGEPURIFY_NAPCAT_TOKEN', $appPreviousToken, 'Process') }
