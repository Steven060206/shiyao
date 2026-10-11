# M3 真实集成探针验证记录

更新：2026-10-11（北京时间）。本记录只写实际运行结果；账号号、群名、消息正文、Token 和用户截图原文不写入。

## 结果总览

| 项目 | 实际结果 | 证据与边界 |
| --- | --- | --- |
| OneBot 协议与客户端 | 通过受控 loopback：46 项 | `tests/MessagePurify.Infrastructure.Tests` Release 46/46；含真实 ClientWebSocket、乱序 echo、事件交错、账号隔离、历史字段、并发释放回归。受控服务不代表真实 QQ 已连接。 |
| QQ 真实只读连接 | 接口和实时群事件限定范围通过 | 前四次探针接口通过、实时群消息 0；用户反馈刚收到新群消息后，13:42:06（北京时间）开始的第五次 180 秒探针实际观察到 1 条。QQ 在线、账号核对、NapCat 4.18.37 / v11、群 52 个、历史群成功 1/1、历史消息 7 条、去重后 8 条、无失败，退出码 0。实时群事件限定范围通过（观察到 1 条），不作为七天补漏或长期稳定性验收。最新脱敏报告 `.tools/m3-results/qq-framework-user-feedback.md`。 |
| Framework 桌面模式 | 已加载，用户确认窗口正常 | 官方 v4.18.37 在 QQ `9.9.36-53644` 中实际启动，已加载 `napiloader.dll` 并自动登录；6099 与 localhost 3001 属于同一 QQ 主进程。用户从托盘打开后确认“聊天列表和窗口正常显示”。启动日志曾有 `updateWindowSizeProperties` 的 TypeError，保留问题记录；长期稳定性尚未验收。 |
| OCR 协议与合成图 | 通过：25 项 Python | `./.tools/ocr/venv/Scripts/python.exe -m pip check` 和 `./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py` 均通过。真实 CPU 模型识别合成中文通知、长图日期、空白图和损坏图后继续；有限数值、代理码点 UTF-8 和离线模型回归已覆盖。 |
| 用户 QQ 截图 OCR | 真实截图文字通过，通知日期不验收 | 本机 worker 处理用户群列表截图：55 行、6 个红框群名均识别、原图字节未变。截图没有完整学校通知正文和日期，不能证明日期提取。全文只在 `.tools/m3-results/`。 |
| WPF/WebView2 隐藏桥 | 通过 | 发布后运行 `Run-Probe.ps1 -Mode Bridge`；报告 `C:\Users\Administrator\AppData\Local\MessagePurify\IntegrationProbe\results\Bridge-20261010-225528.json`。本轮重新 publish 并安装到含空格路径 `.tools/m3-install space`，运行 `Run-Probe.ps1 -Mode Bridge -WaitSeconds 20` 退出码 0，报告 `.tools/m3-install space/results/Bridge-20261011-013731.json`。WebView2 `155.0.4283.45`，echo→reply→ack、secure context、特殊文字保留均通过，PNG 已核对。 |
| Windows 通知 | 资源发布、提交及用户实际点击通过；声音未验收 | 专用 URI 入口的普通测试提醒 ID 36649，用户确认“出现了事项窗口”；点击记录与发送 Probe ID 一致，`activationObserved=true`、`activationKind=protocol`、账号匹配、`error=null`。受控 COM 冷启动和 Shell URI 分发分别通过。声音、静音提醒呈现及长期通知稳定性仍未验收。 |
| 桌面卡片 | 未完成人工操作 | WPF 产物、父窗/DPI/样式及拖动几何测试通过；未执行 Win+D、遮挡、鼠标拖动/缩放、多屏、Explorer 重启或睡眠恢复人工验收。 |

## 代码和自动化验证

- Core Release：295/295。
- Infrastructure Release：46/46。
- Windows 参数/桥/通知路由/桌面几何及真实令牌完整性：本次 Release 57/57，退出码 0；IntegrationProbe 自包含 `win-x64` publish 退出码 0。此前权限修正阶段为 45/45。
- OCR：25/25，使用项目隔离解释器运行；`./.tools/ocr/venv/Scripts/python.exe -m pip check` 无坏依赖。
- Node 文档、NapCat 启动路径和当前用户探针打包：本轮运行四文件命令，21/21、退出码 0；其中打包测试覆盖官方通知资源提取、缺资源拒绝安装、重复安装和路径空格。
- demo 浏览器：107 项。
- `tests/notification_cold_activation_test.py`：正式自包含发布产物受控 COM 冷启动回归退出码 0，输出 `PASS: cold COM callback located the synthetic account and item.`；受控回调不是用户点击验收。
- `git diff --check`：退出码 0（仅显示工作区换行提示）。

QQ 接入检查完成后继续通知验证，先修正令牌完整性检测，再定位并修复自包含发布缺少官方通知资源的问题。本轮已重跑 Core 295、Infrastructure 46、Windows 45、OCR 25、Node 21、demo 107，全部通过；该阶段受控冷启动回调通过；本次专用 URI 修复后的用户实际点击通过，声音和所有未人工操作的 M3 项仍保持未验收。

本轮回归实际执行：三个测试项目的 `.tools/dotnet/dotnet.exe test tests/<项目> --no-restore --configuration Release`，OCR 的 pip check / unittest discover，四文件 Node 测试及 `node tests/prototype-qa.mjs`。Infrastructure 初次构建因正在监听的 QQ 探针锁住运行目录 DLL 而退出码 1；先用同项目 `--no-build --no-restore --configuration Release` 验证 46 项，退出码 0。300 秒监听正常结束后，复跑 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`，构建与 46 项均通过、退出码 0；没有关闭 QQ。

后续实时事件和只读政策文档更新未改产品代码。四文件 Node 检查初跑 19/20，缺少明确的长期稳定性未验收状态；补齐后实际重跑同一命令，20/20、退出码 0。实际运行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-build --no-restore --configuration Release --filter FullyQualifiedName~OneBotTransportTests`，5/5、退出码 0，含精确读取 action 列表断言。本次没有重跑上述完整套件。

## Windows 权限检测与通知尝试

- 原 `NotificationProbe.IsElevated` 根据管理员组成员身份判断权限。`runas /trustlevel:0x20000` 运行诊断时报告 `elevated=false`，但 SDK 的 `IsSupported` 返回 false。核对 [Windows App SDK 的完整性判断源码](https://github.com/microsoft/WindowsAppSDK/blob/main/dev/Common/Security.IntegrityLevel.h) 与 [AppNotificationManager 实现](https://github.com/microsoft/WindowsAppSDK/blob/main/dev/AppNotifications/AppNotificationManager.cpp)：通知按令牌完整性是否达到 High 判断，不按管理员组是否启用。
- 新增 `ProcessIntegrity.cs`，通过 `GetTokenInformation(TokenIntegrityLevel)` 获取当前令牌的完整性 RID；权限报告和阻止规则与 SDK 一致。新增 Windows 回归独立执行 `whoami /groups /fo csv /nh`，核对原生查询值；实际 Windows 45/45、编译无警告。
- 修复后实际执行 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/IntegrationProbe --configuration Release --runtime win-x64 --self-contained true --no-restore --output ".tools/windows-probe-publish fixed"`，再执行 `packaging/probe/Install-Probe.ps1 -PublishDirectory ".tools/windows-probe-publish fixed"`，均退出码 0。发布后由 `Copy-NotificationResource.ps1` 从匹配的官方 Windows App SDK 2.5.1 MSIX 提取 `Microsoft.WindowsAppRuntime.Insights.Resource.dll`；安装脚本会拒绝缺少该资源的产物，Node 打包回归覆盖提取、覆盖旧文件和缺失资源失败。
- 本机一次性测试通过 Windows Basic User 运行独立 Python 辅助进程，再只降低该辅助进程令牌的完整性；没有改变系统策略或已有应用。`results/medium-launch-20261011.json` 记录 RID 12288→8192。资源修复后，中等完整性测试进程的普通通知报告为 `notificationStatus=submitted`；最新用户重发报告为 `.tools/m3-notification-diagnostic-publish/results/normal-medium-user-resend-20261011.json`，仍未观察到用户激活回调。
- 只读核查 WpnService 与当前 WpnUserService 均 Running。错误分为两层：Codex 子进程继承包身份（状态 122），SDK 注册返回 `0x80070490`；经真实 Explorer 启动后无包身份（状态 15700），才暴露缺少通知资源的 `0x8007007E`。补齐匹配资源后注册与提交成功；仅降低完整性不能消除包身份。用户已确认普通提醒呈现；本次修复后的用户实际点击通过，静音呈现和声音仍需验收。

## 通知资源与冷启动回归

- Windows App SDK 2.5.1 的自包含发布目录不会自动携带 `Microsoft.WindowsAppRuntime.Insights.Resource.dll`。`packaging/probe/Copy-NotificationResource.ps1` 在 Publish 后从匹配的官方 `Microsoft.WindowsAppRuntime.2.msix` 提取该资源，`Install-Probe.ps1` 在安装前检查资源存在；资源提取和安装检查已由四文件 Node 回归覆盖。
- `NotificationProbe` 在 `Register()` 后读取 SDK 首次激活参数，修复冷启动点击参数没有被消费的问题。受控回归 `.tools/m3-results/cold-notification-regression-summary.json` 显示修复前 `activationObserved=false`，修复后 `activationObserved=true`、`activationKind=cold`、账号匹配；`tests/notification_cold_activation_test.py` 对正式发布产物退出码 0。
- 2026-10-11 14:28:38 通过真实 Explorer 打开本机 `.lnk`（目标为 `pythonw.exe` 辅助脚本）重发，报告 `.tools/m3-notification-diagnostic-publish/results/normal-medium-user-resend-20261011.json` 为 `submitted`、通知 ID 36646、`error=null`、`activationObserved=false`。辅助进程无包身份，完整性 RID 8192。此运行副本包含资源修复，尚未包含冷启动修复；用户此前确认看到过提醒并点击无反应；这是当时运行副本的历史结果，当前专用 URI 入口的点击通过，声音未验收。
- 冷启动回归对正式产物 `.tools/windows-probe-publish fixed/MessagePurify.Windows.IntegrationProbe.exe` 实际执行 `python tests/notification_cold_activation_test.py --executable <该产物绝对路径>`。经真实 Explorer 启动的辅助进程运行，最新 `.tools/m3-results/cold-test-result.json` 为退出码 0、PASS；直接从 Codex 运行包装脚本会继承包身份，本轮曾得到 COM `0x80080005`，随后改用实际 Explorer 重跑通过。该测试不发送通知，不能作为用户点击结果。
- 保存修复后，14:40:56 通过真实 Explorer 使用包含冷启动修复的正式发布产物重发。最新报告 `.tools/windows-probe-publish fixed/results/normal-medium-user-resend-fixed-20261011.json` 为 `submitted`、通知 ID 36648、`error=null`、`activationObserved=false`；无包身份、完整性 RID 8192，等待点击 600 秒。用户随后反馈该条提醒多次点击无反应，COM 激活超时；当前专用 URI 入口的点击通过，声音仍未验收。

## 专用 URI 点击入口与实际用户验收

- 旧通知 ID 36648 的 COM 激活出现 DistributedCOM 10010 超时。Explorer 完整性 RID 12288、发送进程 RID 8192，但真实 Explorer 辅助进程在不同完整性下均出现 COM 超时，不能将完整性差异记为唯一根因。确定的代码缺口是 `ProbeOptions.Parse` 拒绝 Windows COM 追加的 `-Embedding`；现已修复，受控冷启动脚本带该参数。
- 当前普通及静音通知使用 `activationType=protocol`，由当前用户的 `messagepurify-probe` URI 打开合成事项；命令直接执行探针 EXE，字段严格校验，URI 模式只显示详情，不注册或发送通知、不读取 QQ。详情窗口保持至手动关闭或最多 600 秒；发送报告与独立点击报告通过 Probe ID 关联。
- 2026-10-11 14:52:34 普通测试提醒 ID 36649 已提交，报告 `.tools/windows-probe-protocol/results/normal-protocol.json`。14:54:38 的 `%LOCALAPPDATA%/MessagePurify/IntegrationProbe/results/protocol-*.json` 点击记录与发送 Probe ID 一致，`activationObserved=true`、`activationKind=protocol`、`activationAccountMatched=true`、`error=null`；用户随后确认“出现了事项窗口”。用户实际点击通过。
- 实际执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`，57/57、退出码 0；执行 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/IntegrationProbe --configuration Release --runtime win-x64 --self-contained true --no-restore --output .tools/windows-probe-protocol-final`，退出码 0，官方通知资源自动提取。
- `python tests/notification_protocol_activation_test.py --executable .tools/windows-probe-protocol/MessagePurify.Windows.IntegrationProbe.exe` 输出 `PASS: Windows shell protocol opened the visible synthetic item window.`，退出码 0；该检查不发送通知，受控报告 Probe ID 为 `controlled-shell-` 前缀，与实际通知点击分开。
- 经真实 Explorer 辅助进程运行最终发布产物的 `tests/notification_cold_activation_test.py`，`.tools/m3-results/cold-protocol-final-test-result.json` 为退出码 0、`PASS: cold COM callback located the synthetic account and item.`；该受控检查验证兼容的 COM 回调，不替代用户实际通知点击验收。
- 最终产物通过 `packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-protocol-final` 安装到 `%LOCALAPPDATA%/MessagePurify/IntegrationProbe`，退出码 0；安装 DLL 的 SHA256 与最终发布产物一致，没有覆盖仍打开的 `.tools/windows-probe-protocol/` 事项窗口。协议注册仍指向实际点击验证的运行副本，下次从安装入口发送会更新注册。
- 本次实际重跑四文件 Node 文档/打包/启动回归，21/21、退出码 0；初跑 20/21 仅为受控检查边界说明的文档断言不匹配，补齐说明后通过。本次通知修复没有重跑未改动的 Core、Infrastructure、OCR 和 demo 套件，先前完整回归与本次范围分开记录。
- 声音、静音提醒呈现和长期通知稳定性仍未验收。正式应用尚未接入通知探针能力，M3 不因一次点击通过而完成。

## 当前 QQ 接入说明

官方 Shell 已核对并下载校验 v4.18.37。QQ 实际安装路径为 `D:\QQ\QQ.exe`，官方脚本依赖注册表而失败；项目提供 `packaging/napcat/Start-NapCat.cmd`，直接传入 QQ 路径。Shell 是无界面接管路线，同一账号不能在普通 QQ 进程中并行登录。此前 Shell 探针因 3001 未就绪退出码 1；该历史结果已由本次 Framework 的真实只读通过结果更新。

用户确认退出普通 QQ 后，实际通过隐藏 PowerShell 启动 `packaging/napcat/Start-NapCat-Framework.ps1`。日志确认 Framework 初始化完成，进程加载 `napiloader.dll`、`major.node`、`wrapper.node`；WebUI 返回在线。正向 WebSocket 配置仅监听 `127.0.0.1:3001`，array 格式、debug 关闭，随机 Token 仅保存在本机忽略目录及探针子进程环境。原配置备份为 `.tools/m3-results/framework-onebot-before.json`，启动日志在同目录，不提交原日志或私人内容。

实际运行命令：`.tools/dotnet/dotnet.exe tools/MessagePurify.QqProbe/bin/Release/net10.0/MessagePurify.QqProbe.dll --listen-seconds 30 --output .tools/m3-results/qq-framework-current.md`，以及将监听改为 60 秒、输出分别改为 `qq-framework-events.md`、`qq-framework-desktop-open.md` 的同一命令；三次退出码 0。随后以 `--listen-seconds 300 --output .tools/m3-results/qq-framework-live-observation.md` 运行同一 DLL，退出码 0，历史 7 条、实时群消息 0、其他事件 11、无失败。每次仅查询一群一页历史，未进行七天历史分页、断线补漏、正式持久化或任务生成。

用户回答“刚收到新的群消息”后，把已有探针运行文件复制到独立忽略目录以避免构建 DLL 锁，实际运行 `.tools/dotnet/dotnet.exe .tools/m3-results/qq-live-run-20261011-feedback/MessagePurify.QqProbe.dll --listen-seconds 180 --output .tools/m3-results/qq-framework-user-feedback.md`，退出码 0。报告起始 2026-10-11 13:42:06（北京时间）、实时群消息 1、历史 7、去重后 8、排除私聊 0、其他事件 7、无失败。只验证收到一条群文本事件，没有验证所有群、图片事件或长期接收。

随后运行 `python .tools/m3-results/check-recent-group-traffic.py`，只调用群列表及历史读取，每群最多读一页 5 条：52 群尝试、41 成功、11 接口失败、历史合计 178 条，其中近十分钟 2 条分属 2 群，最新原始时间为 13:42:10。脚本退出码 0表示诊断已完成，不表示 52 群历史全部通过。统计保存在 `.tools/m3-results/qq-recent-group-traffic.json`，不保存账号、群名、正文或 Token，不把这些历史作为实时事件验收。

实际执行 `python .tools/m3-results/diagnose-group-history-errors-20261011.py`，只读查询状态一次、群列表一次、历史两次；退出码 0。报告 `.tools/m3-results/qq-history-errors-diagnosis-20261011.json` 中一个失败样本为 `failed/1200/data=null`，空历史错误成立；一个成功对照为 `ok/0`、5 条。结合本机 NapCat 实现，定位失败样本为底层返回空历史后转换成接口错误；未逐群复测全部 11 个失败，不修改客户端或把错误伪装成成功。七天分页和断线补漏仍未验收。

用户询问是否会向学校群发送消息，已核对项目 QQ 客户端、探针和启动脚本没有 QQ 发送接口调用。QQ 接入只读，不向 QQ 群或私聊发送消息；现有受控 loopback 回归精确断言探针仅调用五个读取 action，提醒使用本机 Windows 通知。

用户随后确认从托盘打开 QQ 后聊天列表和窗口正常显示；QQ 主窗口句柄存在，主进程同时监听 6099/3001。启动时曾出现两次 `TypeError: undefined is not a function`，调用点为 `updateWindowSizeProperties`。该错误对长期使用的影响尚未验证；目前没有用户确认的窗口显示故障，不盲目替换加载器。未发送消息，也没有成功生成 QQ 窗口截图。

M3 仍为“正在实施”。下一步继续历史补漏、实时接收稳定性、桌面人工操作及通知声音、静音呈现验收；不重复要求已经完成的退出、扫码或登录准备。
