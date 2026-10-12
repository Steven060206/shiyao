[CmdletBinding()]
param([Parameter(Mandatory)][int]$ProcessId, [int]$WaitSeconds = 10, [switch]$ExitApplication)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System.Runtime.InteropServices;
public static class TrayTestNative {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, System.UIntPtr extra);
}
'@
$appProcess = Get-Process -Id $ProcessId
if ($appProcess.ProcessName -ne 'MessagePurify') { throw 'Expected a running MessagePurify application.' }
$root = [System.Windows.Automation.AutomationElement]::RootElement
$containerCondition = [System.Windows.Automation.OrCondition]::new(
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Shell_TrayWnd'),
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TopLevelWindowForOverflowXamlIsland'))
$iconCondition = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '消息提纯')
$openedOverflow = $false
$cursorMoved = $false
$cursor = [TrayTestNative+Point]::new()
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    $icon = $null
    do {
        $appProcess.Refresh()
        if ($appProcess.HasExited) { throw 'Application exited before tray verification.' }
        $containers = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $containerCondition)
        foreach ($container in $containers) {
            if (-not [TrayTestNative]::IsWindowVisible([IntPtr]$container.Current.NativeWindowHandle)) { continue }
            $icon = $container.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $iconCondition)
            if ($null -ne $icon -and -not $icon.Current.IsOffscreen -and -not $icon.Current.BoundingRectangle.IsEmpty) { break }
            $icon = $null
        }
        if ($null -eq $icon -and -not $openedOverflow) {
            $toggle = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '显示隐藏的图标'))
            if ($null -ne $toggle) {
                $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
                $openedOverflow = $true
                Start-Sleep -Milliseconds 300
            }
        }
        if ($null -ne $icon) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $icon) { throw 'MessagePurify tray icon missing after application startup.' }
    $iconName = $icon.Current.Name
    if ($ExitApplication) {
        # Keep an open process handle so its exit code remains readable after exit.
        [void]$appProcess.Handle
        $bounds = $icon.Current.BoundingRectangle
        Write-Verbose "Opening tray menu for process $ProcessId at $bounds."
        [void][TrayTestNative]::GetCursorPos([ref]$cursor)
        $cursorMoved = $true
        [void][TrayTestNative]::SetCursorPos([int]($bounds.X + $bounds.Width / 2), [int]($bounds.Y + $bounds.Height / 2))
        [TrayTestNative]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
        [TrayTestNative]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
        $exitCondition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $ProcessId),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, '退出'))
        $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
        do {
            $exitItem = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $exitCondition)
            if ($null -ne $exitItem) { break }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($null -eq $exitItem) { throw 'MessagePurify tray exit menu missing.' }
        Write-Verbose 'Invoking the tray exit menu.'
        $exitItem.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        if (-not $appProcess.WaitForExit(15000)) { throw 'Tray exit did not stop the application.' }
        if ($appProcess.ExitCode -ne 0) { throw "Tray exit failed with code $($appProcess.ExitCode)." }
    }
    [pscustomobject]@{ Success = $true; ProcessId = $ProcessId; TrayIcon = $iconName; Offscreen = $false; ExitVerified = [bool]$ExitApplication } | ConvertTo-Json
} finally {
    if ($cursorMoved) { [void][TrayTestNative]::SetCursorPos($cursor.X, $cursor.Y) }
    if ($openedOverflow) {
        $overflow = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty, 'TopLevelWindowForOverflowXamlIsland'))
        if ($null -ne $overflow -and [TrayTestNative]::IsWindowVisible([IntPtr]$overflow.Current.NativeWindowHandle)) {
            $toggle.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        }
    }
}
