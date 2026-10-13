# 项目检查

## 当前范围决定（2026-10-12）

OCR 图片下载、附件存储、OCR 队列和重分析已调整为后期独立项目，本阶段不以 OCR 完成为门槛。额外的本机数据安全增强同样后置；测试仍必须保持 QQ/NapCat 只读，软件不得向 QQ 群聊或私聊发送消息。普通通知转任务、六字段完整编辑及可读变更记录/卡片变化标记已由工作区、桥协议、正式页面和 Native Smoke 专项覆盖；下一阶段完成手动新增、合并拆分、备份恢复和桌面卡片恢复。

## 本轮完整编辑实现与验证（2026-10-13）

完整编辑覆盖标题、课程、正式日期、钟点、时间类型和优先级。测试覆盖版本化 `edit-task`、操作幂等、原始证据与前后审计、手动时间保护、旧 JSON 优先级兼容、版本冲突保留全部草稿、刷新响应按起始账号/任务绑定，以及编辑或清空课程后同账号同群原证据引用仍可关联（仅标题匹配仍要求课程一致，跨群/跨账号/歧义拒绝）。最新 Release 回归：Core **306/306**、Infrastructure **463/463**、Windows **206/206**；Node 全量 **95/95**（含正式页面 **39/39**），均退出 0。自包含发布 `.tools/published/app-change-history-final/` 无警告错误；`MessagePurify.exe --smoke-test --report .tools/desktop-validation/change-history-native-final.json` 退出 0，报告 `success=true`、`synthetic=true`，21 项检查通过，覆盖编辑、变更历史、待确认变更、卡片标签、重开及版本冲突。真实 QQ 编辑场景统一后置；默认正式入口及正在运行版本未替换，测试未发送 QQ 或 Windows 通知。

## 本轮可读变更记录与卡片标记（2026-10-13）

测试覆盖从 `operations` 审计还原中文字段前后值、北京时间、原消息证据和顺序；延期/取消/新要求/编辑产生重要变化标签，催交补充仅追加证据；待确认变更保留正式日期并可打开建议；`mark-read` 按任务版本工作，旧版本不能清除后来变化。正式页面专项覆盖详情转义、历史显示、日期精度、待确认建议定位和新变化保留；Native Smoke 覆盖重开、卡片标签、变化读取及滚动条存在时仍精确触发缩放手柄。报告为 `.tools/desktop-validation/change-history-native-final.json`，success=true、synthetic=true、21 项通过。

## 本轮 Windows 登录自动启动实现（2026-10-12）

自动启动使用当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值为带引号的正式 `MessagePurify.exe --background`；默认开启，设置页可关闭并重新开启。`--background` 隐藏主窗口，保留托盘和桌面卡片；普通手动入口仍显示主窗口。验证模式不会写入当前用户的 Run 项，设置文件使用原子替换并在注册表/文件写入失败时回滚注册表状态。

自动回归：`StartupRegistrationTests` 与 `AppBridgeProtocolTests` 53/53，正式页面 `node --test tests/application-ui.test.mjs` 29/29，应用 Release build 退出 0 且无警告；`git diff --check` 退出 0。直接启动发布版 PID 35380 后，实际查询到 Run 项为 `"D:\ai制作的软件\消息提纯\.tools\published\app-launcher\MessagePurify.exe" --background`；唯一实例先以 `--background` 启动时主窗口句柄为 0，随后从普通入口启动同一 PID 后主窗口正确显示，托盘检查通过；`--smoke-test` 不写入 Run 项。登录后的真实 Windows 自启动和关闭/重新开启的实机验收按用户要求后置到全部确定功能完成后。

## 本轮消息提纯便捷启动入口（2026-10-12）

新增 `Install-MessagePurifyShortcuts.ps1`：桌面和开始菜单“消息提纯”快捷方式直接指向正式 `MessagePurify.exe`，空参数、正常窗口、发布目录为 `.tools/published/app-launcher/`；同名冲突保留原入口，重复安装保持一致，QQ 快捷方式不改。应用直接启动时优先使用环境凭据，缺失时只读唯一启用的本机 3001 NapCat 配置，配置错误不猜选且不输出凭据。

已执行 `node --test tests/application-shortcuts.test.mjs tests/application-launcher.test.mjs tests/application-ui.test.mjs`，快捷方式/入口 7/7、正式页面 35/35；`NapCatConnectionConfigurationTests` 11/11，Windows Release 164/164，Node 全量 80/80。覆盖真实 Windows COM 属性、中文和空格路径、指定发布目录、缺发布/同名冲突、重复安装、环境凭据优先、本机地址过滤、跨文件/同文件歧义、损坏配置脱敏及三种页面错误提示。新产物已发布至 `.tools/published/app-launcher/`；`app-launcher-native.json` success=true、13 项 Native 合成通过，默认入口 `-CheckOnly` Ready/AuthenticationReady=true。当前桌面和开始菜单入口实际安装并启动 PID 13596，两个卡片的桌面父窗/style 检查通过；开始菜单重复启动保持单实例，隐藏主窗口后再次启动唤回；真实只读 `app-launcher-read-only.json` connected/online、历史 complete、失败为空，UIA 时间报告显示 18:29 成功时间和 20:00 计划时间。自然固定时刻、实际游戏暂停/补同步、长期稳定性仍未验收。

## 本轮同步状态与时间显示验收（2026-10-12）

同步状态显示已实现并验证：正常等待、正在同步、游戏延后、名单未就绪、未补全和连接故障分别显示；“上次成功同步（本次运行）”只记录实际完成且没有历史失败的批次，状态刷新、游戏暂停、失败和空闲重连不伪造成功时间。该时间暂不跨进程持久化；“下次计划同步”复用北京时间 08:00/12:00/20:00，游戏期间继续延后。Chrome 用 America/New_York 时区验证北京时间显示、无记录空状态、草稿/焦点/选择范围和迟到旧状态。

本轮实际 Infrastructure 专项 61/61、完整 Release 428/428，Windows Release 153/153，Node 全量 75/75（文档 33），均退出 0。App build 无警告/错误；自包含发布 `.tools/published/app-sync-status/`、默认入口 `-CheckOnly` 通过。Native 合成 13 项通过，`.tools/desktop-validation/sync-status-native.json` success=true、synthetic=true、进程退出 0。

真实只读新版 `run-resident-check.ps1 -PublishDirectory '.tools/published/app-sync-status' -ReportName sync-status-read-only.json -Seconds 20` 退出 0，complete/connected/online、失败为空，记录实际成功时间及下一计划时间。默认入口重开 PID 40840，UIA `inspect-sync-status.ps1 -ProcessId 40840` 显示等待下次同步、17:51 成功时间和今晚 20:00，北京时间标签与本次运行说明通过；独立卡片窗口、托盘及 Established 连接通过。只读 SQLite 核对 1 个任务、46 项 pending、1 项 ignored，原 2026-10-09/无钟点保留。安全报告为 `.tools/desktop-validation/sync-status-*.json`，主窗口停在设置页，QQ 保持运行，未操作真实事项或发送 QQ/Windows 通知。

托盘脚本原根桌面的跨提供程序搜索两次漏掉隐藏按钮；限定 Shell_TrayWnd 后实际找到按钮/图标，脚本改为托盘及该进程窗口范围搜索，旧版菜单退出和新版托盘检查均通过。临时 UIA 脚本首次由 Windows PowerShell 5 误读 UTF-8 中文，改用 PowerShell 7 执行通过；这两项是验证工具修正，不记为软件托盘或设置故障。自然固定时刻、实际游戏暂停/补同步、桌面恢复/多屏及长期稳定性仍未验收；Core/OCR/demo 未改，本轮未重跑。

## 上一段单条真实事项到卡片验收（2026-10-12）

用户选定材料完成通知，核对原消息 10 月 8 日发送，“明天”对应 10 月 9 日；用户回复“就按日期保存”，保存 2026-10-09、无钟点。真实确认、卡片 P0/已逾期/仅有日期、从卡片完成、主界面撤销、退出重开及卡片打开正确原文通过，最终保留待办。当前 1 个待办任务、46 项未处理待确认、1 项已忽略；此前 48 项待确认是 review 类总数，包含 1 项已忽略。未批量确认真实课程关系，不把一个真实例子记为完整端到端或识别准确率完成，实际游戏实机验收继续延后。

实际 UI Automation 执行确认按钮、原生桌面完成、已完成页撤销；只读 SQLite 验证任务 active → completed → active、正式时间 2026-10-09/Time=null/TimeProtected=true、证据保留 1 条、待确认转为 accepted。托盘脚本首轮未找到退出菜单，原因未确定；同一 `tests/app_tray_test.ps1 -ProcessId 8540 -ExitApplication -Verbose` 重跑退出 0、ExitVerified=true，应用退出码 0，未改生产代码。默认入口重开进程 5128，数据库无重复、两卡片父窗/style 正常，原生卡片点击打开的正式日期和原文通过。首次 UIA 读取因未读点及异步详情时序失败，按实际标签兼容未读点并等待详情就绪后通过，未修改生产逻辑。

本机只读报告位于 .tools/desktop-validation/：real-task-accepted.json、real-task-completed.json、real-task-restored.json、real-task-reopened.json、real-task-desktop-accepted.json、real-task-desktop-reopened.json、real-task-native-detail.json。QQ 未退出，未发送 QQ/Windows 通知；主界面停在真实任务详情，可由用户核对。普通通知转任务已由本地工作区、桥协议和正式页面专项覆盖；下一步进入完整编辑、变更标记及其他真实任务/桌面恢复场景；OCR 队列后置。

新增 `AccountWorkspaceTests.DateOnlyReview_UsesOriginalMessageDayAndSurvivesCompleteUndoAndReopen`，采用通用材料文本与合成账号，覆盖以原发送日解析“明天”、日期精度、显式确认、P0、完成/撤销及重开证据保留；单项 1/1、Infrastructure 完整 Release 419/419 通过。文档回归更新当前数量与真实日期确认边界，Node 全量 72/72。生产代码未改，不重新发布，也未重跑未改动的 Core/Windows/OCR/demo。

## 上一段真实首次后台同步与常驻验收（2026-10-12）

真实首次后台同步与批次后保持连接已通过，三个自然固定时刻尚未验收。

用户要求先测试真实 QQ 接入、首次后台同步与两张卡片，实际游戏暂停/补同步实机验收延后，原保护规则保持有效。用户正常退出后启动现有 Framework，新版真实检查两次均 ExitCode=0、success=true、synthetic=false；52 个已加入群、9 个置顶群、48 项待确认、0 个任务/通知，重开未增加重复项。两次均 transport=connected、qq=online、receiving=paused、history=complete、error=null、failures=[]；paused 为正常批次结束，连接保持。只读 SQLite 数量为 messages=359、route_receipts=359、reviews=48。

实际命令及报告：

```powershell
# 本地辅助仅存放于忽略目录，凭据仅传入进程环境
& ./.tools/desktop-validation/run-resident-check.ps1 -ReportName resident-read-only-first.json -Seconds 35
& ./.tools/desktop-validation/run-resident-check.ps1 -ReportName resident-read-only-repeat.json -Seconds 35
& ./packaging/app/Start-MessagePurify.ps1
& ./.tools/ocr/venv/Scripts/python.exe .tools/desktop-validation/inspect-resident-desktop.py --pid 34036 --close-main --report .tools/desktop-validation/resident-desktop-hidden.json
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId 34036
# 再次默认启动唤回同一实例，再正常隐藏
& ./packaging/app/Start-MessagePurify.ps1
& ./.tools/ocr/venv/Scripts/python.exe .tools/desktop-validation/inspect-resident-desktop.py --pid 34036 --close-main --report .tools/desktop-validation/resident-desktop-reopen.json
```

正式两张卡片实际均可见、parentClass=SHELLDLL_DefView、child/noActivate=true、topmost=false；关闭主窗口后进程保留、托盘图标存在，重复启动仍为同一 PID 34036、仅一实例。实际 TCP 检查确认该进程到 3001 的 Established 连接保持。QQ/Framework 与正式应用继续运行，主窗口隐藏；没有发送 QQ 或 Windows 通知，没有代用户确认课程/日期或完成真实事项。真实首次同步不能替代自然 08:00/12:00/20:00 到点验收，实际游戏暂停、桌面恢复/多屏和长期稳定性未验收。

本轮新增验收顺序及真实结果的文档回归；实际 `node --test tests/*.test.mjs` 71/71，Infrastructure Release 调度/名单专项 38/38、实际宿主专项 29/29 均退出 0。前一个 filter 使用了未匹配的 NapCatMessageHostTests 类名，已补跑实际 ApplicationMessageHostTests，合计 67 项。生产代码未改，未重跑未改动的 Core/Windows/OCR/demo，不重新发布。两个入口 `-CheckOnly` Ready=true，正式入口 AuthenticationReady=true，发布路径 app-scheduled-sync；本地安全报告均位于 `.tools/desktop-validation/`。

## 上一段常驻后台同步与自动游戏名单（2026-10-12）

用户选择 A，确认 QQ/NapCat Framework 常驻，按北京时间 08:00、12:00、20:00 后台读取置顶群；游戏期间暂停，退出后把错过多个时刻合并补一次。后台调度和常驻宿主已接入，首次非游戏时历史回溯不吞掉下一固定时刻，批次外重连不开启新读取；请求、响应及落盘/路由事务前核对游戏，中途游戏暂停未提交的处理，退出后合并恢复。自动发现和缓存已接入，默认应用启动及每日扫描一次，游戏期间延后目录扫描。本机扫描报告 `.tools/game-validation/installed-games.json`：19 个游戏安装目录，Minecraft 整合包合为一项，最终只读状态 GameRunning=false；不把已打开的 Steam 当作游戏。

新增 `ScheduledSyncTests.cs`（23 项）、`InstalledGameCatalogTests.cs`（15 项）及常驻宿主/逐消息路由回归。实际 Infrastructure Release 418/418、Windows 141/141、Core 301/301、OCR 25/25、demo 108 项、Node 八文件 69/69（文档 29、正式页面 25）通过；自包含发布、默认入口检查和 Native 合成 13 项退出 0。Native 报告 `.tools/desktop-validation/scheduled-sync-native.json`；入口为 `.tools/published/app-scheduled-sync/`。本轮没有启动或关闭真实 QQ；真实常驻、实际游戏中途暂停/退出补同步、每款游戏的实际识别和长期稳定性尚未验收，不把测试/合成结果写为这些真实项目已通过。

```powershell
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release
& ./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py
node --test tests/*.test.mjs
node tests/prototype-qa.mjs
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-scheduled-sync
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File packaging/app/Start-MessagePurify.ps1 -CheckOnly
```

## 上一段核对后台同步可行性（2026-10-12，待确认建议现已确认）

新增文档回归：后台读取可行性与接口常驻待确认建议分别记录，未确认前保留按次切换、游戏限制及补同步规则。核对现有 AppRuntime 后台宿主和 Framework 重复启动检查，未改动生产代码或实际切换 QQ；尚未实现定时后台读取，未实测资源占用和游戏影响。

实际 `node --test tests/project-rules.test.mjs` 29/29、退出 0；验证范围为文档一致性，设计稿首段的同步时间及游戏补同步状态已同步。

## 上一段确认游戏结束后补同步（2026-10-12）

每天 08:00、12:00、20:00（北京时间）各同步一次，共三次；打游戏时禁止同步。用户选择 A：游戏导致错过同步时，结束游戏后补同步一次，同一游戏期间错过多个时刻合并为一次补同步。更新文档回归，核对四份当前说明的时刻、游戏限制、补同步规则和未实现边界，检查有效条款及交接下一步不再要求确认游戏漏同步处理。现有持续连接宿主未改，定时自动切换及游戏检测尚未实现，本轮没有实际切换 QQ。

实际 `node --test tests/project-rules.test.mjs` 28/28、退出 0；本轮验证仅覆盖文档一致性，不作为定时同步或游戏检测已实现的证据。

## 上一段恢复普通 QQ 图标（2026-10-12）

新增 `Restore-QQShortcut.ps1` 及 `qq-shortcut.test.mjs` 第三项：首次创建、直接指向 QQ.exe、QQ 图标、空参数、正常窗口及重复恢复清除旧配置。实际 `node --test tests/qq-shortcut.test.mjs` 3/3，组合验证 `node --test tests/project-rules.test.mjs tests/qq-shortcut.test.mjs` 30/30，均退出 0；合成文件未执行。真实恢复脚本退出 0，桌面 QQ.lnk 持续存在，Explorer 桌面命名空间含 QQ、Target 为原 QQ.exe、Arguments 为空、Encrypted=false，报告 `.tools/desktop-validation/qq-original-restored.json`。原加密备份未改，当前 QQ 未退出或重开，火绒设置未改；集成快捷方式改造仍停止，定时同步尚未实现。

## 上一段接入方向调整（2026-10-12）

用户停止 QQ 快捷方式改造，回到固定时间自动接入、同步置顶群并落盘、结束恢复普通 QQ。桌面 QQ.lnk 实际缺失，用户报告火绒提醒；恢复未成功，不把历史瞬时验证记为当前入口可用。定时自动切换尚未实现，具体时刻与 QQ 使用冲突规则待确认。本轮仅更新有效文档及 `project-rules.test.mjs`：实际 `node --test tests/project-rules.test.mjs` 为 27/27、退出 0，核对新方向、未实现边界及停止推广快捷方式；不通过快捷方式安装/激活测试证明自动切换。

## 历史 QQ 桌面入口（2026-10-12，当前方向已停止）

以下为当时结果，QQ.lnk 后续缺失，当前不再修复或推广此入口。

`Install-QQShortcut.ps1` 先检查 Framework 依赖，再备份原桌面 QQ 快捷方式并改为隐藏 PowerShell 入口，保留 QQ 图标；重复安装不覆盖首次备份。Framework 新增 `-OpenExisting`，已运行时仅调用 QQ 打开已有窗口，普通启动仍拒绝重复加载。`qq-shortcut.test.mjs` 使用临时中文/空格路径及合成依赖，未执行假的 QQ 文件；两项覆盖实际 COM 快捷方式属性、参数引用、隐藏窗口、原备份和重复安装，以及已有 QQ 激活/普通入口拒绝重复加载。短路径与 PowerShell 展开的长路径使用 realpath 核对，避免字符串比较误报。

实际 `node --test tests/napcat-framework-launcher.test.mjs tests/qq-shortcut.test.mjs` 3/3、退出 0。最终 Node 八文件命令在此前七文件后追加 `tests/qq-shortcut.test.mjs`，65/65、退出 0。实际执行 `packaging/napcat/Install-QQShortcut.ps1` 退出 0，原 `C:/Users/Administrator/Desktop/QQ.lnk` 已备份到 `%LOCALAPPDATA%/MessagePurify/Shortcuts/QQ.original.lnk`；从修改后的桌面快捷方式启动，进程 ExitCode=0、3001 仍由原 QQ PID 监听，报告 `.tools/desktop-validation/qq-shortcut-check.json`。未退出 QQ 重测冷启动，未注册登录 Windows 后自启动；正式应用及其他生产模块未改，沿用上段结果。

## 上一段托盘修复（2026-10-12）

`Program.cs` 正常启动等待 `ListenAsync`，保持托盘和取消令牌到应用退出。新增 `app_tray_test.ps1`，Windows PowerShell 5.1 可直接运行；只查询 Explorer 托盘容器及可见“消息提纯”图标，必要时展开隐藏区，结束后恢复展开状态和鼠标位置。旧 app 进程 43172 的图标测试实际失败，修复版通过；`-ExitApplication` 实际右击菜单并调用“退出”，保留进程句柄核对 ExitCode=0。测试自身已修正不可见溢出窗、展开动画及退出码读取的误报。

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId <运行的消息提纯进程ID>
# 此项实际退出应用，完成后用默认入口重开
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId <运行的消息提纯进程ID> -ExitApplication
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release
node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-tray-fix
& ./.tools/ocr/venv/Scripts/python.exe tests/app_desktop_test.py --executable .tools/published/app-tray-fix/MessagePurify.exe
```

实际修复版 Windows 141/141、Node 七文件 63/63、publish、Native 合成 13 项、Python 父窗/style 及默认入口 `-CheckOnly` 均退出 0；入口 Ready/AuthenticationReady=true、路径匹配 app-tray-fix。托盘菜单退出检查进程 24696 返回成功且应用 ExitCode=0，`tray-exit.json`；重新打开后，对进程 26000 正常 WM_CLOSE 验证关闭隐藏、进程和托盘保留，再启动唤回同一窗口且只有单实例，`tray-lifecycle.json` 五项全部 true。修复版已保持打开，QQ 未退出。原生报告 `tray-fix-native-smoke.json`、入口报告 `tray-fix-launcher-check.json` 位于 `.tools/desktop-validation/`。

本轮真实只读复查另执行已发布 app-desktop 的 `--read-only-check --seconds 20 --report .tools/desktop-validation/read-only-current.json`，进程退出 0、success=true、synthetic=false，52 个群、9 个置顶群、48 项待确认、0 个任务/通知，当次历史 complete、失败为空；未确认课程或日期。Infrastructure/Core/OCR/demo 未改，沿用上一段 375/301/25/108 的已执行结果。完整真实任务、多屏/睡眠/Explorer 恢复及长期稳定性尚未验收；OCR 队列仅调查，未实现。

## 上一段正式桌面卡片首段（2026-10-12）

正式两张桌面卡片首段已实现并验证：当前账号 active 任务/pending 待确认、稳定排序、查看/完成、主题同步、DIP 布局存盘重开、空待确认隐藏。系统通知、声音、定时提醒表单/预览和安静时段入口已取消，App 普通通知列表及旧 plans/ledger 保留。Windows Release 141/141、Infrastructure Release 375/375、Node 七文件 63/63（文档 27、Chrome 24、入口 3、其余 9）退出 0；App 与 Windows.Tests locked restore、最终自包含 publish、入口检查及真实 Native 合成/独立桌面窗口检查均退出 0。

单次差异审查发现时钟跨界标签未刷新：任务版本和 P0 不变时 render signature 未含 past。新增截止/开始/只有日期跨午夜三项专项先红 0/3；同一 `now` 的 `IsPast` 同时用于 signature/Render 后完整 Windows 141/141。DPI 重挂载不自动 Save，仅 Load/Clamp；既有五项损坏/未知格式布局用例增加重复变更边界/scale 读取断言，确认原文件保留。Node 初轮 61/63 因测试只模拟主题而未固定本机减少动画（motion=true），仅将测试 CDP 固定 `no-preference` 后全部通过。

本轮实际验证命令：

```powershell
& ./.tools/dotnet/dotnet.exe restore src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --locked-mode
& ./.tools/dotnet/dotnet.exe restore tests/MessagePurify.Windows.Tests --locked-mode
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release
node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-desktop
& ./.tools/ocr/venv/Scripts/python.exe tests/app_desktop_test.py --executable .tools/published/app-desktop/MessagePurify.exe
```

新产物 `--smoke-test --report .tools/desktop-validation/native-smoke-final.json` 真实运行并核对进程 ExitCode=0、success=true：snapshot、complete、undo-complete、explicit-date、accept-review、native-bridge、reopen、desktop-data、desktop-theme、desktop-open-item、desktop-complete、desktop-layout、pending-hide 共 13 项通过。`native-smoke-final.png` 和 `native-smoke-final-cards.png` 已查看。Python 检查枚举本次进程两个卡片子窗，parentClass=`SHELLDLL_DefView`、child/noActivate=true、topmost=false，任务可见、待确认空隐藏；对旧 app-reminders 的同项检查曾实际失败，旧版无正式卡片。独立 PowerShell 执行 `packaging/app/Start-MessagePurify.ps1 -CheckOnly` 后读取 ExitCode=0、Ready/AuthenticationReady=true，报告 `.tools/desktop-validation/launcher-check.json`。

Core/OCR/demo 本轮未改，OCR/demo 沿用历史结果；本轮 Core 306/306、Infrastructure 463/463、Windows 206/206、Node 全量 95/95 均已实际执行并退出 0。桌面合成使用临时账号，本轮未重测真实 QQ 数量，沿用上段 9 个置顶群/40 项待确认两次结果，未代用户确认课程/日期。缩放使用按提示定位的实际 WPF Thumb 事件；本轮未做物理鼠标拖动，多屏、Explorer 重启和睡眠恢复尚未验收。源码/测试在仓库；本机报告与截图位于忽略目录 `.tools/desktop-validation/`。

## 历史验证记录

下列各段保留当时执行命令和结果；Windows 通知渠道现已取消，旧“尚未接入”或后续提醒工作不作为当前计划。正式卡片当前范围以上节为准。

历史持久化提醒第一段（2026-10-12）：新增账号 `reminders.sqlite3` schema 1，原 messages/tasks/routing 三库 schema 1 不变。正式提醒设置、计划、NeedsReview、节点与投递台账已接入 AccountWorkspace/桥/UI；可控 sink 验证默认/小时/日历/关闭、延期、恢复一次汇总、去重、有限重试及在途提交协调。页面明确显示“系统提醒尚未开启”；当时 Windows 实际投递及后台循环尚未接入，本段没有发送 QQ 或 Windows 通知。

审查发现失败 recovery 汇总一名成员完成/延期后，有效成员漏重试；四项真实 SQLite 回归先红 42/46、退出 1，修复后服务 46/46、退出 0，仍沿原 ID 和跨重启三次预算重试有效未成功成员，完成撤销不重放成功成员。最终完整 Infrastructure Release 375/375（服务 46、工作区提醒 24）；本段 Windows 96/96、Core 301/301、OCR pip check 通过与 unittest 25/25、demo 108、Node 七文件 59/59（文档 24、正式 Chrome 23、入口 3）通过。P1 只修改服务及其测试，最终重跑 Infrastructure 和窄复现：complete/postpone 两路健康 sink 都只提交有效 B 一次，节点为 submitted；不重复无关全套。

最终再次自包含发布至 `.tools/published/app-reminders/`，原产物 `.tools/published/app/` 保留；默认入口已更新。真实隐藏 WebView2 `--smoke-test --report .tools/reminder-validation/native-smoke-final.json` 八项通过、进程退出 0，包含提醒保存和重开，报告 success=true、截图已查看。独立 PowerShell 启动入口 `-CheckOnly` 退出 0、Ready/AuthenticationReady=true。当前旧窗口及 QQ 保持运行；旧版从托盘退出后重新双击入口才加载新版。本段未重新验证真实 QQ 数量或确认真实课程/日期。

```powershell
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~PersistedReminderTests|FullyQualifiedName~WorkspaceReminderTests'
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-reminders
& ./.tools/dotnet/dotnet.exe run --project .tools/reminder-validation/review-repro/review-repro.csproj --configuration Release
node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs
```

源码和相关真实 SQLite/浏览器用例保存在仓库；`.tools/reminder-validation/` 的专项、审查和合成原生报告为本机忽略产物。上述提醒专项合并命令用于重复验证；最终结果以已实际执行的完整 Infrastructure 与服务专项为准。

最终文档同步后实际运行 `node --test tests/project-rules.test.mjs`，24/24、退出 0，核对提醒进度、原格式保留及系统尚未接入；`git diff --check` 退出 0。

历史正式应用首段（2026-10-12）：AccountWorkspace 已接入三库、账号课程关系及已读，NapCatMessageHost 接收置顶群并尽力读取历史；正式 WPF/WebView2 已自包含发布。合成真实 WebView2 闭环通过，完成/撤销、显式日期确认、接受待确认及重开存储均实际执行。真实 QQ 正式宿主两进程检查退出 0，52 个已加入群、9 个置顶群，40 项待确认，重启仍为 40 项，课程尚未确认，0 个正式任务/通知。当次历史 complete/失败为空不替代持续实时或独立准确率验收。当时 Windows 实际提醒调度、OCR 队列、正式桌面和完整编辑等未完成；当前提醒调度已取消，卡片首段结果见上节。应用说明见 [正式应用](../src/MessagePurify.Windows/App/README.md)。

上一段 Infrastructure Release 305/305（工作区 30、宿主 24）、Core 301/301、Windows 77/77，OCR 25/25、demo 108、新应用 Chrome 18/18、应用启动脚本 2/2，完整 Node 七文件 53/53，均退出码 0。正式启动入口已打开真实账号窗口，QQ 保持运行。上一段验证命令：

```powershell
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app
node --test tests/application-ui.test.mjs tests/application-launcher.test.mjs
node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs
```

WinExe 用 `Start-Process -PassThru -WindowStyle Hidden` 启动，`WaitForExit` 等待并读取退出码；合成检查参数 `--smoke-test --report .tools/app-validation/native-smoke.json`，真实检查参数 `--read-only-check --seconds 20 --report .tools/app-validation/read-only.json`。真实检查凭据只从当前进程环境继承，安全报告只包含数量与错误码。新桥来源/参数 20 项先缺入口红测；工作区/宿主入口、正式页面、配置传递、P0 后果加级缺陷均记录实际先红后绿。未确认发送者的高影响变更保留待确认；课程确认不授予可信身份。已确认群改变关系后旧普通待确认不自动升级。普通通知转任务、M6 重新分析协调、变更一次 Windows 通知尚未完成。真实 QQ 到任务/提醒的完整宿主联调未完成；测试不发 QQ 或 Windows 通知。

上一段 M4 第四段（2026-10-12，历史验证）：AccountCandidateRouter 首轮候选自动路由及自动可靠关联变更与保守消息顺序协调已实现，实际 Release 专项 128/128（候选路由 36、关联变更 30、生命周期 62）、Infrastructure Release 251/251、Core Release 296/296、Windows Release 57/57、OCR 25/25 且 `pip check` 通过、Node 五文件 32/32（文档 23、桥 4）、demo 108 项，均退出码 0。Task + 正式 Time 建事项，可靠关联 Change 自动更新同一事项，冲突和 Pending 保存待确认，Notice 保存本地普通通知列表，Ignored 仅保存回执。普通通知列表入库不等于 Windows 系统通知投递，当时普通通知转任务、完整 M6、正式界面和宿主仍未完成；基础正式界面/只读宿主后来已在本轮接入，提醒调度、OCR 队列及完整真实任务/提醒联调仍未完成。

本轮 `LinkedChangeRoutingTests.cs` 使用真实提取器和临时 SQLite，覆盖取消/延期/新行动/催交、反向入队、预提取批次及更早原消息等待、持久化最新事项替换调用者快照、来源授权、保护时间冲突、取消例外、已完成新行动恢复及历史保留、归档/取消不自动恢复、疑问/OCR/跨群/课程变化、晚到旧消息/同秒/缺来源时间、提取与事项变更整条事务回滚、重试及重启不重放。原 `CandidateRoutingTests.cs` 保留普通未确认发布者、多行动分流、OCR 建议隔离、用户已完成/忽略状态、群范围/限量、暂时排除后的继续处理及账号/目录/数据库归属；可靠取消断言改为自动更新，普通原 Pending/Task/Notice 不升级。

当前调用可用 Ingest → ProcessAndRoutePending：按原发送时间，在单条消息事务中提取并路由原消息；原 RoutePending 入口遇到更早未提取消息先等待。KnownTasks 完全由当前持久化事项投影替换调用者旧快照，只有当前群 TrustedSenders 授权高影响变更，原发送者不自动升级为可信发布者。关联 Pending/Change 首轮重核对，课程确认被撤销或变更时，旧 Task/Notice/Change 降级为待确认。LEFT JOIN 核对全部已关联证据的原消息时间，任一来源缺失时间、晚到旧消息或同秒顺序冲突均转待确认；尚未完整支持 real_seq 或跨群原转发顺序。

自动 Cancel/Postpone/NewRequirement/Remind 共用生命周期事务；受保护时间冲突、Archived/Cancelled 新要求保留待确认，Completed 可靠新要求恢复待办并保留完成历史。自动新要求保留稳定标题，完整新正文保存在原消息、证据和操作历史；自动 Remind 只追加证据，不覆盖正式时间或恢复事项。共享接受逻辑中，Remind 未传 confirmedTime 时不使用候选 Time；用户显式确认新时间仍可修改。消息提取、处理版本、证据、操作和回执一起提交，通知元数据使用最终处理版本。已路由消息的更高 ProcessingVersion 不自动重放，M6 重新分析协调仍待完成；回执失败整条消息回滚且同操作可重试。messages/tasks/routing 三库 schema 1 不变，仅接受持久化 rollback journal，WAL 拒绝且不写结果。

上一段 M4 首轮路由回归（2026-10-12）：Release 专项 94/94（路由 32、生命周期 62）、Infrastructure Release 217/217、Core Release 296/296、Windows Release 57/57、OCR 25/25 且 `pip check` 通过、Node 五文件 31/31（文档 22、桥 4）、demo 108 项，均退出码 0。第三段当时 Pending/Change 仅保存待确认，自动可靠关联变更及消息顺序协调尚未实现；本轮第四段已替代该处理方式。首轮去重、证据保留和本地通知列表继续有效，正式 QQ/界面/提醒宿主仍未接入。

上一轮 M4 生命周期回归（2026-10-11 开始、2026-10-12 保存）：Core Release 296/296、Infrastructure Release 184/184（消息存储 23、消息处理 22、新增生命周期 61）、Windows Release 57/57、OCR 25/25 且 `pip check` 通过、Node 五文件 30/30（文档 21、桥 4）、demo 108 项，均退出码 0。生命周期测试使用真实临时 SQLite，覆盖事务失败回滚、账号隔离/归属、未知结构不重建、候选规范化去重、操作参数冲突、关联变更双版本检查、证据追加、时间保护和撤销边界/旧凭据。此为本轮路由开发前的已验证基线。

本轮路由、关联变更与生命周期专项命令：

```powershell
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CandidateRoutingTests|FullyQualifiedName~LinkedChangeRoutingTests|FullyQualifiedName~TaskLifecycleTests'
```

上一段置顶群规则及 M4 收件箱回归（2026-10-11）：Core Release 296/296、Infrastructure Release 123/123（含 SQLite 存储 23、消息处理 22）、Windows Release 57/57、OCR 25/25 且 `pip check` 通过、Node 五文件 29/29（文档 20、桥 4）、demo 108 项，均退出码 0。新增 `PinnedGroupProbeTests.cs` 覆盖混合群/无置顶、未知或错误名单不回退、账号错配、实时/分页取消置顶与重连更换范围；桥使用真实 `{get, call}` 结构和数字 `topFlag`，检查其他读取接口不变、未知标记/失败/不完整快照停止。真实 9 个置顶群两轮 18/18 到达日期边界、去重 341、重连 1/1、退出码 0；未观察到本次真实实时群事件，不替代动态置顶或长期稳定性验收。下文旧全群结果为规则变化前的历史记录。

在项目根目录运行全部现有测试：

```powershell
& ./.tools/dotnet/dotnet.exe restore tests/MessagePurify.Core.Tests --locked-mode
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore
& ./.tools/dotnet/dotnet.exe restore tools/MessagePurify.RuleBaseline --locked-mode
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.RuleBaseline --no-restore -- --output docs/verification/rule-baseline.md
node --test tests/project-rules.test.mjs
node tests/prototype-qa.mjs
```

核心测试使用 .NET 10 SDK，已在 `.tools/dotnet` 安装 Windows x64 10.0.401；`global.json` 锁定版本，Core、Core.Tests 和基线 CLI 各有 `packages.lock.json`。如另一本机已有对应 SDK，可把命令中的可执行文件改为 `dotnet`。M1 的 87 项 C# 测试包含 10 项时间、33 项提醒和 44 项状态检查，覆盖日期当天与次日、精确到时、Deadline／Start、时区和夏令时异常、默认 24／2 小时及前日中午、自定义小时／日历、延期重算、精度失效回退、关闭保留及非法提前节点；状态检查覆盖手动值保护、版本变化、幂等、取消优先、归档／恢复、新行动恢复、完成 1 小时／忽略 24 小时撤销边界，以及延期后撤销保留最新时间。测试用显式时间输入，不依赖执行机器的当前时刻。

M2 消息与日期部分现有 2 项消息标准化和 102 项日期解析检查。覆盖原发送时刻、保存时区、跨年、日期精度、冲突／范围、非法数值、夏令时、未覆盖表达，以及图片、引用及消息证据保留。

M2 Task 2 新增 85 项行动提取与 13 项优先级检查，核心合计 289 项通过。覆盖短行动、多交付物、报名／开始时间、疑问／否定／转述／条件、限定对象、可信变更来源及唯一目标、OCR 建议、仅引用、账号／群隔离、混合来源引用和排除目标、完成／归档／取消的新要求，以及 P0～P3 日历边界和明确后果提升。

M2 Task 3 新增 65 条合成样本、6 项 fixture/指标回归测试和 RuleBaseline CLI。实际 Release 核心全量为 295 项（含 6 项 fixture）；CLI 报告完整匹配 65/65、正式 Task 准确率 23/23、需行动召回 47/47、直接自动覆盖 27/47、待确认占比 23/57、高影响错误 0。无效 CLI 参数退出码 2。合成样本不替代真实 QQ 群消息验收。

Node 文档测试包含 24 项检查：协作规则同步、小任务规则优先级、交付文件和文档链接、整体设计及核心开发状态、M2 计划与阶段、全部设计验收场景的计划覆盖、新版演示的保存方式和真实集成边界、QQ 登录与 OneBot 连接分别记录，以及 Shell/Framework 接入边界；真实 QQ 只读结果、用户窗口反馈、启动异常、实时群事件的限定验证和未验收的长期稳定性/补漏范围须同步。生命周期、首轮候选路由与本轮可靠关联变更状态检查明确本地通知入库、自动应用与冲突待确认、持久化最新目标、当前群来源授权、保守消息顺序和后续宿主/M6 边界；只读政策检查：不向 QQ 群或私聊发送消息，测试和任务提醒采用本机 Windows 通知。

浏览器测试包含 108 项检查，覆盖原有主题／动画、排序、桌面卡片、图片确认及通知转任务，并新增：首次引导、已读行为、完成 1 小时／忽略 24 小时撤销及刷新、归档恢复、搜索与筛选、手动任务与开始时间、自定义提醒、延期及精度失效、人工字段冲突、新行动要求、取消、图片放大及 OCR 折叠、静音通知内容预览、设置与桌面布局保存。图片日期不再单独勾选，检查没有二次勾选框、未提交建议不确立正式时间、按钮直接提交、修改后保存及刷新、仅日期和图片通知提交。详情栏的回归检查在 1100×800 窗口中确认长内容不被裁切，使用 Chrome 原生鼠标滚轮上下滚动及原生点击固定确认按钮，核对任务／通知的去向和错误提示可见性。课程相关 16 项检查覆盖共享选项、两处自定义入口、刷新和重置、重复／空白名称、取消恢复选择、未提交表单保留、待确认／手动任务使用自定义课程及筛选。测试在截止边界显式验证撤销资格，并通过过期动作检查防止越界恢复；未实际等待 1 小时或 24 小时。动画与系统主题测试显式模拟媒体偏好，不依赖本机 Windows 设置。

M3 已创建 OneBot、Windows 探针和 OCR 测试项目。实际验证命令为：

```powershell
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release
& ./.tools/ocr/venv/Scripts/python.exe -m pip check
& ./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py
node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs
node --test tests/pinned-groups-bridge.test.mjs
```

通知权限修正阶段的回归计数：Core 295、Infrastructure 46、Windows 45、OCR 25、打包/启动 Node 四个测试文件共 21、demo 107，均通过，见[集成探针记录](../docs/verification/integration-probes.md)。本次点击入口修复后 Windows Release 为 57/57，新增 URI 载荷、参数限制、字段校验、静音载荷和 `-Embedding` 回归。Node 覆盖文档 16 项、通知资源提取/安装/入口打包 3 项以及两个 NapCat 启动入口各 1 项。受控未就绪 QQ CLI 退出码 1；Framework 前四次真实只读探针退出码 0、实时群消息 0，第五次 180 秒探针收到 1 条真实群事件、退出码 0。用户确认聊天列表和窗口正常显示，窗口打开期间接口复测通过。Windows 通知探针的用户实际点击通过，用户确认出现事项窗口；后续静音提醒显示/点击和桌面基本操作已由用户确认通过；完整历史补漏、长期稳定性和桌面恢复/多屏仍单独验收。

`tests/notification_protocol_activation_test.py --executable <已注册的探针绝对路径>` 检查 Windows Shell 的 URI 分发和合成事项窗口，不发送提醒；报告中的 `controlled-shell-` Probe ID 与用户实际通知点击区分。`tests/notification_cold_activation_test.py` 保留受控 COM 回调检查，并模拟 Windows 追加的 `-Embedding` 参数。这些受控检查均不替代用户实际点击验收。

2026-10-11 取消声音提醒后的实际回归为 Windows 57/57、四文件 Node 22/22（文档 17 项）及浏览器 108 项；所有通知载荷固定静音，浏览器检查覆盖移除声音开关、旧 `sound=true` 偏好不恢复、其他设置保留及考试/作业/活动的静音预览。

本轮静音提醒的显示和实际点击已通过。历史分页新增 `HistoryPagingTests.cs`、参数和方向回归后，Infrastructure Release 为 60/60；覆盖乱序页、原样游标、超 Int64 数值 `real_seq`、窗口过滤、重叠、短页继续、空页、无进展、页上限、游标不足、整页解析原子性及失败重试后继续下一群。带游标请求的 `reverse_order=true` 先用失败测试定位，再由真实 QQ 对照确认方向；默认单页和实时/历史去重仍有回归。

上一轮历史分页扩展的完整自动回归：Core 296/296、Infrastructure 60/60、Windows 57/57、OCR `pip check` 和 unittest 25/25、四文件 Node 23/23（文档 18 项）、demo 108 项，均退出码 0。真实全群分页与测试分开：41/52 到达边界、窗口内去重 1098，11 群持续 1200，CLI 正确退出 1；不把接口失败当作空页或完整补漏。报告及实际命令见[集成验证记录](../docs/verification/integration-probes.md)。

本轮受控重连新增 `ReconnectProbeTests.cs`，Infrastructure Release 67/67；验证重新读取最新页、重建游标、跨连接/历史/实时去重、模拟新消息补回、账号变化停止、取消后不连接及历史失败保留。参数支持 0～2 次主动重连、默认 0；只断开测试连接，QQ 保持运行。真实两轮账号核对 2/2、重连 1/1，历史 2206、去重 1098、重复 1108；未观察到重连后新消息，11 群每轮持续 1200，CLI 退出 1，不能替代正式自动恢复或跨进程补漏。

本轮全部自动回归：Core 296/296、Infrastructure 67/67、Windows 57/57、OCR 25/25 且 `pip check` 通过、四文件 Node 24/24（文档 19 项）、demo 108 项及 `git diff --check`，均退出码 0。

`python tests/desktop_attachment_test.py --executable <已发布探针的绝对路径>` 在真实 Explorer 中启动合成桌面测试，核对实际父窗为 `SHELLDLL_DefView`、窗口可见标志、子窗/不激活/非置顶样式、物理边界及没有成功状态窗。原版 `Progman` 父窗和多余状态窗均会使回归失败。可增加 `--mouse`，在桌面未被覆盖时实际拖动扩大后的右下角手柄，验证宽高增加 60/30 像素、左上角不移动；结束后释放按钮并恢复指针，测试进程在 5 秒后正常退出。运行鼠标回归时先用 `Shell.Application.MinimizeAll()` 切到桌面，最后 `UndoMinimizeALL()` 恢复窗口。该测试只使用合成卡片，不操作 QQ，不替代用户人工验收。

需要 Node.js 22 或更新版本和 Chrome。默认使用 `C:/Program Files/Google/Chrome/Application/chrome.exe`；其他安装位置可通过环境变量 `CHROME_PATH` 指定。浏览器检查使用独立临时配置，不访问个人浏览器会话。普通测试不会覆盖预览图；仅在主动更新预览时运行 `node tests/prototype-qa.mjs --screenshots`，生成课程选择、待确认、主界面及桌面卡片的六张预览。

这些检查验证 C# 核心、协议、OCR 工作进程、Windows 探针参数和浏览器原型；真实 QQ、Windows 系统通知呈现/点击和桌面人工驻留仍不由自动测试替代。
