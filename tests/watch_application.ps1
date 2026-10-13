[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][string]$Report,
    [ValidateRange(1, 7200)][int]$Seconds = 7200,
    [ValidateRange(1, 300)][int]$IntervalSeconds = 30
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ApplicationWatchNative {
    private delegate bool EnumCallback(IntPtr hwnd, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    public static Dictionary<string,IntPtr> Cards(uint process) {
        var cards = new Dictionary<string,IntPtr>();
        EnumCallback inspect = (hwnd, unused) => {
            uint owner; GetWindowThreadProcessId(hwnd, out owner);
            if (owner == process) {
                var title = new StringBuilder(256); GetWindowText(hwnd, title, title.Capacity);
                if (title.ToString() == "消息提纯 · 提醒事项" || title.ToString() == "消息提纯 · 待确认")
                    cards[title.ToString()] = hwnd;
            }
            return true;
        };
        EnumWindows((hwnd, unused) => { inspect(hwnd, IntPtr.Zero); EnumChildWindows(hwnd, inspect, IntPtr.Zero); return true; }, IntPtr.Zero);
        return cards;
    }
    public static string ParentClass(IntPtr hwnd) {
        var text = new StringBuilder(256); GetClassName(GetParent(hwnd), text, text.Capacity); return text.ToString();
    }
}
'@
$reportPath = [IO.Path]::GetFullPath($Report)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath))
$samples = [Collections.Generic.List[object]]::new()
$watch = [Diagnostics.Stopwatch]::StartNew()
$startedAt = [DateTimeOffset]::Now
$identity = $null
$failure = $null
$connectionLabels = @('等待下次同步', '正在同步置顶群消息', '同步未补全', '同步已暂停',
    '同步状态待确认', 'QQ 连接断开，正在重连', 'QQ 未登录，已暂停接收',
    '正在连接 QQ', '游戏中，同步已延后', '检测名单未就绪，同步已暂停',
    'QQ 账号不匹配，已暂停接收')
function Save-Report([bool]$completed) {
    $result = [ordered]@{
        kind = 'read-only-application-observation'; synthetic = $false; processId = $ProcessId
        startedAt = $startedAt.ToString('o'); updatedAt = [DateTimeOffset]::Now.ToString('o')
        requestedSeconds = $Seconds; elapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
        completed = $completed; success = ($completed -and $null -eq $failure)
        failure = $failure; sampleCount = $samples.Count; samples = $samples.ToArray()
    }
    $temporary = $reportPath + '.tmp'
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $temporary -Encoding UTF8
    Move-Item -LiteralPath $temporary -Destination $reportPath -Force
}
try {
    do {
        $process = Get-Process -Id $ProcessId -ErrorAction Stop
        if ($process.ProcessName -ne 'MessagePurify') { throw 'unexpected-process' }
        if ($null -eq $identity) { $identity = $process.StartTime.ToUniversalTime() }
        if ($process.StartTime.ToUniversalTime() -ne $identity) { throw 'process-identity-changed' }
        $cards = @()
        foreach ($entry in [ApplicationWatchNative]::Cards([uint32]$ProcessId).GetEnumerator()) {
            $style = [ApplicationWatchNative]::GetWindowLong($entry.Value, -16).ToInt64()
            $extended = [ApplicationWatchNative]::GetWindowLong($entry.Value, -20).ToInt64()
            $parentClass = [ApplicationWatchNative]::ParentClass($entry.Value)
            $valid = $parentClass -eq 'SHELLDLL_DefView' -and ($style -band 0x40000000) -ne 0 -and
                ($extended -band 0x08000000) -ne 0 -and ($extended -band 8) -eq 0
            $element = [System.Windows.Automation.AutomationElement]::FromHandle($entry.Value)
            $texts = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            $status = $null
            $itemCount = $null
            foreach ($text in $texts) {
                $name = $text.Current.Name
                if ($connectionLabels -contains $name) { $status = $name }
                if ($name -match '^(?:提醒事项|待确认)\s+·\s+(\d+)$') { $itemCount = [int]$Matches[1] }
            }
            $cards += [pscustomobject]@{ name = $entry.Key; attached = $valid
                visible = [ApplicationWatchNative]::IsWindowVisible($entry.Value)
                itemCount = $itemCount; connection = $status }
        }
        if ($cards.Count -ne 2) { throw 'desktop-card-missing' }
        $samples.Add([pscustomobject]@{ at = [DateTimeOffset]::Now.ToString('o')
            elapsedSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
            responding = $process.Responding; cpuSeconds = $process.TotalProcessorTime.TotalSeconds
            workingSetBytes = $process.WorkingSet64; privateBytes = $process.PrivateMemorySize64
            handles = $process.HandleCount; threads = $process.Threads.Count; cards = $cards })
        if ($cards.Where({ -not $_.attached }).Count -ne 0) { throw 'desktop-card-detached' }
        Save-Report $false
        $remaining = $Seconds - $watch.Elapsed.TotalSeconds
        if ($remaining -gt 0) { Start-Sleep -Milliseconds ([int]([Math]::Min($IntervalSeconds, $remaining) * 1000)) }
    } while ($watch.Elapsed.TotalSeconds -lt $Seconds)
} catch {
    # Report a safe reason; never persist UI text, local paths, credentials or raw exceptions.
    $failure = if ($_.Exception.Message -in @('unexpected-process', 'process-identity-changed',
        'desktop-card-missing', 'desktop-card-detached')) { $_.Exception.Message } else { 'observation-unavailable' }
}
Save-Report $true
if ($null -ne $failure) { exit 1 }
