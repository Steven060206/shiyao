[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RuntimePackageDirectory,
    [Parameter(Mandatory)][string]$PublishDirectory
)
$ErrorActionPreference = 'Stop'
$probeArchive = Join-Path $RuntimePackageDirectory 'tools/MSIX/win10-x64/Microsoft.WindowsAppRuntime.2.msix'
$probePublish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$probeResourceName = 'Microsoft.WindowsAppRuntime.Insights.Resource.dll'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$probeZip = [IO.Compression.ZipFile]::OpenRead($probeArchive)
try {
    $probeResource = $probeZip.GetEntry($probeResourceName)
    if ($null -eq $probeResource -or $probeResource.Length -eq 0) {
        throw "The matching runtime package is missing $probeResourceName."
    }
    [IO.Compression.ZipFileExtensions]::ExtractToFile(
        $probeResource, (Join-Path $probePublish $probeResourceName), $true)
    Write-Output "Published notification resource: $probeResourceName"
} finally {
    $probeZip.Dispose()
}
