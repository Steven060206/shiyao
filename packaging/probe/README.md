# Windows 集成探针安装入口

仅用于 M3 集成验证，不创建正式任务或开机启动项。

在项目根目录发布并安装到当前用户的固定目录：

```powershell
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/IntegrationProbe --configuration Release --runtime win-x64 --self-contained true --output .tools/windows-probe-publish
& ./packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-publish
```

发布步骤会从项目锁定的 Windows App SDK 2.5.1 官方 MSIX 中提取 `Microsoft.WindowsAppRuntime.Insights.Resource.dll`；安装脚本会拒绝缺少该资源的发布目录。默认安装目录为 `%LOCALAPPDATA%\MessagePurify\IntegrationProbe`，无需管理员权限。安装后双击对应入口：

| 入口 | 操作 |
| --- | --- |
| Bridge.cmd | 本地 WebView2 隐藏桥往返并退出 |
| Desktop.cmd | 显示桌面探针，检查 Win+D、拖动、缩放，关闭后退出 |
| Notification.cmd | 发普通测试提醒，点击通过专用 URI 打开合成事项 |
| SilentNotification.cmd | 发静音测试提醒，点击通过专用 URI 打开合成事项 |
| ColdNotification.cmd | 发测试提醒后退出，点击通过专用 URI 启动合成事项窗口 |

发送报告保存在安装目录的 `results/`；专用 URI 点击报告独立保存在 `%LOCALAPPDATA%\MessagePurify\IntegrationProbe\results\protocol-*.json`，通过 Probe ID 与原提醒关联。发送时在当前用户注册 `messagepurify-probe` URI，点击仅显示合成事项，不发送通知或读取 QQ。事项窗口可手动关闭，最多保持 600 秒。

报告中的提交、系统呈现和点击分别判断；自动命令不能证明用户看见或听见。高完整性发送进程会明确报告受阻，请从普通桌面双击入口，不选择“以管理员身份运行”。受控冷启动回调可用 `python tests/notification_cold_activation_test.py --executable <发布目录>\MessagePurify.Windows.IntegrationProbe.exe` 回归；受控 Shell 分发可用 `python tests/notification_protocol_activation_test.py --executable <已注册的探针绝对路径>` 回归，两者均不替代用户实际点击验收。2026-10-11 用户实际点击通过并确认出现事项窗口，声音提醒已取消，静音提醒呈现仍待验收。普通与静音入口保留兼容名称，实际提醒均静音。

桌面探针用于核对实际附着、非置顶与操作行为。Explorer 重启、睡眠和多屏 DPI 项需要各自实际验证；本脚本不主动执行这些操作。
