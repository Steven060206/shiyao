[CmdletBinding()]
param(
    [string]$QqExecutable,
    [string]$AccountId,
    [switch]$SkipShortcuts,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

function Find-QqExecutable {
    $candidates = @()
    $savedSettings = Join-Path $PSScriptRoot 'app/qq-sync.json'
    if (Test-Path -LiteralPath $savedSettings -PathType Leaf) {
        try { $candidates += [string]((Get-Content -LiteralPath $savedSettings -Raw -Encoding UTF8 | ConvertFrom-Json).QqExecutable) }
        catch { throw '已有 QQ 路径配置无法读取，请使用 -QqExecutable 指定完整路径。' }
    }
    if ($candidates.Count -eq 0) {
        $candidates += @(Get-Process -Name 'QQ' -ErrorAction SilentlyContinue | ForEach-Object { $_.Path })
        foreach ($key in @(
            'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\QQ.exe',
            'HKLM:\Software\Microsoft\Windows\CurrentVersion\App Paths\QQ.exe',
            'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\QQ.exe'
        )) {
            if (Test-Path -LiteralPath $key) { $candidates += [string](Get-Item -LiteralPath $key).GetValue('') }
        }
        foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
            if (-not [string]::IsNullOrWhiteSpace($base)) {
                $candidates += Join-Path $base 'Tencent/QQ/QQ.exe'
                $candidates += Join-Path $base 'QQ/QQ.exe'
            }
        }
    }
    $found = @($candidates | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_) -and [IO.Path]::GetFileName($_) -ieq 'QQ.exe' -and
        (Test-Path -LiteralPath $_ -PathType Leaf)
    } | ForEach-Object { (Resolve-Path -LiteralPath $_).Path } | Select-Object -Unique)
    if ($found.Count -eq 1) { return $found[0] }
    if ($CheckOnly) { throw '未找到唯一 QQ.exe，请使用 -QqExecutable 指定完整路径。' }
    Add-Type -AssemblyName System.Windows.Forms
    $picker = New-Object Windows.Forms.OpenFileDialog
    try {
        $picker.Title = '选择电脑上已安装的 QQ.exe'
        $picker.Filter = 'QQ.exe|QQ.exe'
        $picker.CheckFileExists = $true
        if ($picker.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { throw '已取消配置，未写入文件。' }
        return $picker.FileName
    } finally { $picker.Dispose() }
}

function Assert-NapCatSource([string]$Source) {
    if ($Source -notmatch 'class OneBotAction' -or $Source -notmatch 'const WebUiDataRuntime') {
        throw 'NapCat 包不支持当前只读置顶群桥，请重新下载完整分发包。'
    }
}

function Write-AtomicJson([string]$Destination, $Value) {
    $temporaryFile = $Destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($temporaryFile, ($Value | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $Destination) { [IO.File]::Replace($temporaryFile, $Destination, [NullString]::Value) }
        else { [IO.File]::Move($temporaryFile, $Destination) }
    } finally {
        if (Test-Path -LiteralPath $temporaryFile) { Remove-Item -LiteralPath $temporaryFile }
    }
}

# Validate both user inputs before extracting or writing anything.
if ([string]::IsNullOrWhiteSpace($QqExecutable)) { $QqExecutable = Find-QqExecutable }
$QqExecutable = $QqExecutable.Trim()
if ($QqExecutable -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+\\[^\\/]+\\)' -or
    [IO.Path]::GetFileName($QqExecutable) -ine 'QQ.exe' -or
    -not (Test-Path -LiteralPath $QqExecutable -PathType Leaf)) {
    throw 'QQ 路径必须是已存在的 QQ.exe 绝对路径。'
}
$QqExecutable = (Resolve-Path -LiteralPath $QqExecutable).Path
if ([string]::IsNullOrWhiteSpace($AccountId)) {
    if ($CheckOnly) { throw '检查模式需要使用 -AccountId 提供自己的 QQ 号。' }
    $AccountId = Read-Host '请输入你自己的 QQ 号（5 至 15 位数字）'
}
if ($AccountId -notmatch '\A[0-9]{5,15}\z') { throw 'QQ 号必须是 5 至 15 位数字。' }

$appDirectory = Join-Path $PSScriptRoot 'app'
$packageDirectory = Join-Path $PSScriptRoot 'napcat/package'
$archivePath = Join-Path $PSScriptRoot 'vendor/NapCat.Framework-v4.18.37.zip'
$bridgeDirectory = Join-Path $PSScriptRoot 'bridge'
$installer = Join-Path $PSScriptRoot 'app-tools/Install-MessagePurifyShortcuts.ps1'
foreach ($required in @('MessagePurify.exe', 'ui/index.html')) {
    if (-not (Test-Path -LiteralPath (Join-Path $appDirectory $required) -PathType Leaf)) { throw "分发包缺少应用文件：$required" }
}
foreach ($required in @('Prepare-PinnedGroups.ps1', 'nativeLoaderPinned.cjs', 'pinned-groups.mjs')) {
    if (-not (Test-Path -LiteralPath (Join-Path $bridgeDirectory $required) -PathType Leaf)) { throw "分发包缺少只读桥文件：$required" }
}
if (-not $SkipShortcuts -and -not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw '分发包缺少快捷方式安装脚本。' }

$dependencies = @('napimain.exe', 'napiloader.dll', 'nativeLoader.cjs', 'napcat.mjs')
$packageExists = Test-Path -LiteralPath $packageDirectory
if ($packageExists) {
    foreach ($required in $dependencies) {
        if (-not (Test-Path -LiteralPath (Join-Path $packageDirectory $required) -PathType Leaf)) {
            throw '已有 NapCat 目录不完整，已保留原文件。请重新解压软件到新文件夹后配置。'
        }
    }
    Assert-NapCatSource ([IO.File]::ReadAllText((Join-Path $packageDirectory 'napcat.mjs'), [Text.Encoding]::UTF8))
} else {
    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { throw '分发包缺少官方 NapCat 压缩包。' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($required in $dependencies) {
            if ($null -eq $archive.GetEntry($required)) { throw '官方 NapCat 压缩包不完整，请重新下载分发包。' }
        }
        if (@($archive.Entries | Where-Object { $_.FullName -match '(?:^|/)onebot11_[^/]+\.json$' }).Count -gt 0) {
            throw 'NapCat 压缩包包含账号配置，请重新下载未经配置的官方分发包。'
        }
        $reader = New-Object IO.StreamReader($archive.GetEntry('napcat.mjs').Open(), [Text.Encoding]::UTF8)
        try { Assert-NapCatSource ($reader.ReadToEnd()) } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}

$configurationDirectory = Join-Path $packageDirectory 'config'
$accountFileName = 'onebot11_' + $AccountId + '.json'
$accountConfiguration = Join-Path $configurationDirectory $accountFileName
if (Test-Path -LiteralPath $configurationDirectory) {
    if (-not (Test-Path -LiteralPath $configurationDirectory -PathType Container)) { throw 'NapCat config 路径不是目录，已保留原文件。' }
    $otherAccounts = @(Get-ChildItem -LiteralPath $configurationDirectory -Filter 'onebot11_*.json' -File | Where-Object { $_.Name -ine $accountFileName })
    if ($otherAccounts.Count -gt 0) { throw '此文件夹已配置其他 QQ 账号。请重新解压软件到新文件夹后配置自己的账号。' }
}
$configurationExists = Test-Path -LiteralPath $accountConfiguration
if ($configurationExists) {
    try { $null = Get-Content -LiteralPath $accountConfiguration -Raw -Encoding UTF8 | ConvertFrom-Json }
    catch { throw '已有账号配置无法读取，已保留原文件。请使用新的软件文件夹。' }
}
if ($CheckOnly) {
    [pscustomobject]@{ Ready = $true; QqExecutable = $QqExecutable; AccountId = $AccountId; PackageAlreadyExists = $packageExists; ConfigurationExists = $configurationExists } | ConvertTo-Json
    return
}

if (-not $packageExists) { Expand-Archive -LiteralPath $archivePath -DestinationPath $packageDirectory }
& (Join-Path $bridgeDirectory 'Prepare-PinnedGroups.ps1') -PackageDirectory $packageDirectory
if (-not $configurationExists) {
    [void](New-Item -ItemType Directory -Path $configurationDirectory -Force)
    $tokenBytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($tokenBytes) } finally { $random.Dispose() }
    $configuration = [ordered]@{
        network = [ordered]@{
            httpServers = @(); httpSseServers = @(); httpClients = @(); websocketClients = @(); plugins = @()
            websocketServers = @([ordered]@{
                name = 'shiyao-local'; enable = $true; host = '127.0.0.1'; port = 13001
                messagePostFormat = 'array'; reportSelfMessage = $false
                token = [Convert]::ToBase64String($tokenBytes); enableForcePushEvent = $true; debug = $false; heartInterval = 30000
            })
        }
        musicSignUrl = ''; enableLocalFile2Url = $false; parseMultMsg = $false; imageDownloadProxy = ''
    }
    Write-AtomicJson $accountConfiguration $configuration
}
Write-AtomicJson (Join-Path $appDirectory 'qq-sync.json') ([ordered]@{ QqExecutable = $QqExecutable })
if (-not $SkipShortcuts) { $null = & $installer -PublishDirectory $appDirectory }
Write-Host '配置已完成。请点击 Start.cmd 或“拾要”快捷方式启动；首次同步时使用你自己的 QQ 账号登录。'
