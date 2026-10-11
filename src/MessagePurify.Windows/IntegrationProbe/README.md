# Windows 集成探针

本目录是 M3 最小宿主，只有合成内容，不接正式数据库或创建 QQ 任务。
Windows x64、.NET 10；`packages.lock.json` 锁定 WebView2 1.0.4258.31、Windows App SDK 2.5.1 及传递依赖。
本地 UI 复制到发布目录的 `ui/`；WebView2 Evergreen 使用电脑已有运行时。

## 执行

```powershell
& ./.tools/dotnet/dotnet.exe restore src/MessagePurify.Windows/IntegrationProbe --locked-mode
& ./.tools/dotnet/dotnet.exe build src/MessagePurify.Windows/IntegrationProbe --no-restore --configuration Release
```

发布后的 `MessagePurify.Windows.IntegrationProbe.exe` 支持：

- `--bridge --report result.json`：隐藏加载虚拟 HTTPS 页面，校验 echo 与页面收到回复后的 ack，捕获页面自身 PNG 后退出。
- `--interactive-desktop --wait-seconds 120 --report result.json`：显示验证主窗口；仅在 Explorer 桌面父窗存在且 DPI context 相同时尝试附着合成卡片，不置顶，不重启 Explorer。
- `--notification normal --wait-seconds 60 --report result.json`：请求测试通知；`normal` 和 `silent` 均静音，保留旧入口兼容。点击通过专用 URI 打开合成事项。
- `--notification silent --exit-after-send --report result.json`：提交后退出；从通知中心点击测试冷启动。

不传参数时执行隐藏桥验证。`--wait-seconds` 为 1～600 秒，默认 20。
默认报告为 `%LOCALAPPDATA%/MessagePurify/IntegrationProbe/results/windows-probe.json`；
SDK COM 冷启动另写同目录的 `cold-activation.json`，不依赖启动工作目录。
报告不保存真实账号、消息、登录材料或原始异常堆栈。

## 结果含义

- 桥 `passed` 要求真实 WebView2 页面发送、WPF 回复、页面 ack 完成，保留合成文字且页面处于 secure context。
- 桌面 `attachedStructureOnly` 只说明父窗和样式检查。Win+D、其他窗口遮挡、拖动／缩放、多屏 DPI、Explorer 重启及睡眠恢复仍须操作实测；报告的 `manualChecks` 不自动变为通过。
- `submitted` 只说明 Windows 返回通知标识，不能证明屏幕呈现或用户已查看；第一版取消声音提醒，所有载荷统一静音。`activationObserved` 单独记录 SDK 回调，账号不匹配时不读取当前账号数据；受控冷启动回调通过也不替代用户实际点击验收。
- 当前会话若提权，通知返回 `blockedElevated`，退出码 3。微软 AppNotification 不支持提权应用；不修改 UAC 或绕过系统通知设置。
- 正常退出调用 `Unregister()` 释放本进程接收，保留冷启动注册；卸载的完整注册清理在后续安装阶段实现。

退出码：0 命令成功／未要求人工点击；1 运行失败；2 参数错误；3 请求通知受到限制或提交结果不明。
合成卡片不保存布局；正式托盘、应用生命周期和持久化在后续阶段实现。

## 官方依据

- [WebView2 本地资源映射](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/working-with-local-content)
- [WPF 通知注册](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-wpf)
- [通知运行与 COM 冷启动](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-quickstart)
- [SDK 自包含部署](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)
- [SetParent 样式与 DPI 限制](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent)

WorkerW／Progman 层级是 Explorer 内部行为，这些公开 Win32 函数不构成微软对桌面组件附着的支持承诺。
