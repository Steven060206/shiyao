# 消息提纯：项目交接文档

最后更新：2026-10-12（北京时间）。后续每次需求或实现发生变化，在同一轮工作中更新本文。

**最新有效决定（2026-10-12）：用户选择 A，第一版只使用“提醒事项”和“待确认”两张桌面卡片，取消全部 Windows 系统通知弹窗和声音。** 时间节点、新发现、变更、恢复/历史/迟到批次汇总、断线超过 5 分钟通知、通知点击路由及权限恢复全部取消；自定义时间提醒和安静时段入口移出当前产品范围。普通群通知仍保留在 App“通知”列表，原消息、事项、证据和旧提醒计划/台账不删除。正式两张桌面卡片首段已实现并验证：自动刷新真实 AccountWorkspace 快照、点击查看/完成、左右初始布局、拖动缩放、DIP 布局存盘，仅附着 Explorer 桌面、不设 Topmost。默认入口已指向 `.tools/published/app-tray-fix/`，托盘生命周期已修复；最新检查 QQ 仍运行，消息提纯进程未运行，未自动重开。多屏、Explorer 重启和睡眠恢复尚未验收，M3 真实接收/长期稳定性仍在实施。

本轮方向调整：用户报告桌面 QQ 消失、火绒发出提醒，并明确要求停止快捷方式方向，回到固定时间自动切换与消息同步。当前有效需求为到点接入、同步置顶群并落盘、结束后恢复普通 QQ；具体时刻、错过同步和正在使用 QQ 时的处理待确认，定时自动切换尚未实现。本轮仅同步文档和对应回归，不再修复或重建快捷方式，不关闭/重开当前 QQ，不更改火绒设置。原消息存储、提取、工作区和两卡片继续复用；旧快捷方式代码及备份保留为历史。

上一段托盘修复：用户找不到托盘图标，已定位为启动回调返回时提前 Dispose NotifyIcon。正式入口改为等待应用监听结束，修复版已发布并打开；真实托盘显示、关闭隐藏、再次启动唤回单实例及菜单退出（ExitCode=0）通过。Windows Release 141/141、Node 七文件 63/63、Native 合成 13 项、独立桌面检查及默认入口检查均退出 0。真实只读复查为 52 个群、9 个置顶群、48 项待确认、0 个正式任务/通知，当次历史 complete；未确认真实课程或日期。Infrastructure/Core/OCR/demo 未改，沿用上一段 375/301/25/108 的实际验证结果。

上一段：已完成[正式桌面卡片首段计划](superpowers/plans/2026-10-12-desktop-only-cards.md)的实现与发布验证。Windows Release 141/141、Infrastructure Release 375/375、Node 七文件 63/63，实际 WPF/WebView2/桌面合成 13 项与独立父窗/style 检查通过；App 及 Windows.Tests locked restore、自包含发布和默认入口检查均退出 0。当时默认 app-desktop，旧 app 窗口仍在运行；本轮已替换为托盘修复版。

上段正式应用：2026-10-12 用户要求继续，已实施[正式应用首段计划](superpowers/plans/2026-10-12-application-host.md)：持久化界面、已有事项操作和只读消息宿主。当时基线为 `6ee0735`；Infrastructure Release 305/305、Core 301/301、Windows 77/77、OCR 25/25、demo 108 项及合成真实 WebView2 闭环通过。用户已退出 QQ 并授权继续，Framework 已重开，正式宿主真实读取 9 个置顶群并保存 40 项待确认，重启仍为 40 项。

历史接续（旧通知方案，现已取消渠道）：用户再次要求继续，基线 `473367e`；[持久化提醒第一段](superpowers/plans/2026-10-12-persisted-reminders.md)已实现并验证：正式任务提醒设置、计划与投递台账、恢复和串行协调。审查复现的失败汇总成员变更漏重试已修复，新增四项真实 SQLite 回归；最终 Infrastructure Release 375/375（服务 46、工作区 24），本段 Windows 96/96、Core 301/301、OCR 25/25、Node 七文件 59/59、demo 108 通过。最终产物再次自包含发布并通过真实合成 WebView2 八项，入口检查退出 0。新产物位于 `.tools/published/app-reminders/`，默认入口已更新；旧正式窗口及 QQ 仍运行，原产物保留。当时 Windows 实际投递及后台循环尚未接入，本段页面明确显示“系统提醒尚未开启”，没有新一轮真实 QQ 到任务/提醒验收，也没有代用户确认课程或日期。

正式应用首段已写入并验证：AccountWorkspace 串行组合消息、任务、路由及新增提醒四库、课程关系及已读，NapCatMessageHost 先落原消息再按原发送时间路由；WPF + WebView2 页面操作写入本地 SQLite。来源正文、完成/撤销、归档恢复、待确认显式日期、忽略/撤销、课程建议确认、搜索筛选及基础托盘可运行。旧方案的持久化计划和可控投递协调作为历史产物保留；当前页面已移除定时提醒表单、节点预览和安静时段入口，正式两卡片已接入，Windows 自动提醒调度与实际通知已取消。OCR 队列、完整变化标记及真实 QQ 任务端到端仍未完成。

## 1. 接手先看

- 项目目录：`D:\ai制作的软件\消息提纯`。
- 先读项目根目录的 `AGENTS.md`；恢复工作时先读 `docs/HANDOFF.md`，再读 `docs/requirements.md`，然后核对实际文件。
- 普通 QQ 登录不作为 OneBot 连接成功；本轮正式宿主使用只读接口并持久化真实群消息，合成页面操作和真实接收分别记录。
- 最新规则及结果：只读取 QQ 置顶群。本轮真实只读复查读取 52 个已加入群名单、9 个置顶群历史，当次读取 complete、失败为空，保存 48 项待确认，0 个正式任务/通知；此前两次为 40 项。未自动确认真实课程或日期，没有 QQ 发送或 Windows 通知。Infrastructure 最新完整回归 375/375；动态置顶人工验收、持续实时和长期稳定性未完成，M3 不改为完成。
- 当前阶段：**整体设计已确认，用户反馈 demo 基本满足要求，实施计划已写入并授权持续开发。M1 已完成，M2 已完成，M3 正在实施并保留真实接收/长期稳定性验收。M4 四段本地基础及正式 WPF/WebView2 主界面、AccountWorkspace 和只读宿主已接入；真实 QQ 历史接收/重启去重、合成 WebView2 已通过。旧方案持久化提醒第一段已验证并保留，Windows 全部系统通知与声音已取消；正式两张桌面卡片首段已实现并验证，当前快照、查看/完成、主题、布局存盘和实际桌面层级通过。多屏、Explorer 重启和睡眠恢复尚未验收；OCR 队列、自启动、M6 修复、完整变化标记/编辑/合并拆分、持续真实使用和完整端到端验收仍未完成，M8 不标为完成。**
- 当前可交付产物：文档、原型及六张预览继续保留；新增 `global.json`、`.gitignore`、`src/MessagePurify.Core/` 下的 Time、Reminders、State 和 Rules 核心，`tests/MessagePurify.Core.Tests/` 测试项目，`tests/fixtures/rules/synthetic.json`、`tools/MessagePurify.RuleBaseline/` 及 `docs/verification/rule-baseline.md`。Core、Core.Tests 和 CLI 三个项目各有 `packages.lock.json`；NapCat Shell 与 Framework 的显式路径启动准备位于 `packaging/napcat/`，对应路径回归在 `tests/napcat-launcher.test.mjs` 和 `tests/napcat-framework-launcher.test.mjs`。可重复运行的检查及说明见 `tests/project-rules.test.mjs`、`tests/prototype-qa.mjs`、`tests/README.md`。
- 历史 demo/probe 概述（本轮正式结果见下节）：demo 已写入并运行浏览器验证，双击 `prototype/index.html` 可打开。事项、偏好、自定义课程／分类、撤销时间戳及桌面模拟布局保存在此浏览器的本地存储，刷新保留进度；“重置示例”恢复事项和卡片布局并保留设置及自定义课程／分类。事项时间固定为 2026-10-09 10:00（北京时间），撤销期限使用真实经过时间。用户已确认普通 QQ 在电脑登录；NapCat Shell 与 Framework 官方包均已下载校验。Shell 是无界面路线，同一账号与普通 QQ 并存时会冲突；当前改由 Framework 在 QQ 主进程中加载并自动登录，6099/3001 同时监听。真实探针取得 52 个群及一群一页的 7 条历史消息；当时分页读取 41/52 到达边界、窗口内去重 1098 条，11 群未补全，详见下节。用户确认聊天列表和窗口正常显示。OCR、隐藏 WebView2 桥和自包含 Windows 探针已有实际结果；已收到 1 条实时群文本事件；通知发布会自动从匹配的官方 Windows App SDK 运行时包提取资源，受控 COM 冷启动回调已通过；专用 URI 入口的用户实际点击通过。没有注册正式自启动；桌面探针基本操作与当时静音提醒呈现/点击通过，长期稳定性及桌面恢复/多屏仍未验收；声音提醒已取消。
- 用户要求持续维护交接文档；这是已授权的工作，以后有变化直接同步，不再反复询问是否记录。

### 本轮停止快捷方式方向，回到定时同步（2026-10-12）

- 基线 `2377443`。用户说明桌面 QQ 不见，随后看到火绒提醒，怀疑其删除了快捷方式；用户明确表示不再沿快捷方式方向开发，接入应在固定时间自动切换与同步。最新明确指示替代原“未决定：定时自动切换”及日常桌面 QQ 改造建议，具体时刻与使用冲突规则仍待确认。
- 用户打断前已核对实际：桌面路径为 `C:/Users/Administrator/Desktop`，隐藏图标设置为 0，QQ.lnk 不存在；`D:/QQ/QQ.exe` 及 `%LOCALAPPDATA%/MessagePurify/Shortcuts/QQ.original.lnk` 仍在。重建后的文件曾短暂存在，8 秒检查时再次缺失，Explorer 桌面项未找到；复制原备份回桌面报“无法加密指定的文件”，恢复未成功。没有拿到火绒具体拦截日志，不写成已独立确定删除机制或已恢复图标。
- 用户停止指示后没有再写桌面、关闭或启动 QQ、修改安全软件设置；只撤去本轮未提交的快捷方式重建测试扩展，原已提交脚本和测试保留历史。新的生产调度代码未实现、没有运行定时切换。
- 后续集中定时同步：沿用已确认 NapCat 与本地读取/存储，先确定具体时刻及 QQ 正在使用时的规则，再实现同步窗口和结束恢复。两张卡片展示本地已有事项，任务/证据/人工操作保护继续有效；不因停止快捷方式方向而删除这些产物。
- 本轮实际文档回归命令 `node --test tests/project-rules.test.mjs`，27/27、退出 0；交接前人工核对有效需求、交接、接入/应用说明和实现状态。固定同步方向与已实现功能分开记录，不使用旧快捷方式测试证明自动切换。

### 历史 QQ 桌面入口简化（2026-10-12，当前方向已停止）

本节记录当时实现和瞬时验证，QQ.lnk 当前实际缺失，不能作为当前可用入口或后续要求；用户已停止此方向。

- 基线 `6f1b648`。用户认为到项目目录找脚本重开 QQ 麻烦；采用原桌面“QQ”图标直接启动带消息接口的 QQ，使用方式仍是双击，未修改 QQ 安装文件、任务栏或开始菜单入口。
- 产物：`packaging/napcat/Install-QQShortcut.ps1`、`tests/qq-shortcut.test.mjs`，Framework 脚本新增 `-OpenExisting`。已有 QQ 时该开关调用原 QQ.exe 激活窗口；未传开关的直接 Framework 启动仍拒绝在已有 QQ 上重复加载。QQ 未运行时仍使用既有已验证 Framework 路线及置顶桥，账号、读取范围和只读边界不变。
- 实际执行 `packaging/napcat/Install-QQShortcut.ps1` 退出 0；更新 `C:/Users/Administrator/Desktop/QQ.lnk`，目标为 Windows PowerShell、参数包含 Hidden 和 OpenExisting，名称为 QQ、图标来自 `D:/QQ/QQ.exe,0`。原入口已备份到 `%LOCALAPPDATA%/MessagePurify/Shortcuts/QQ.original.lnk`；重复安装保留首次备份。测试使用临时中文路径、合成依赖和未执行的 QQ 文件，不依赖本机 NapCat 包。
- 实际用 `Start-Process` 打开桌面快捷方式并等待结束，ExitCode=0；3001 监听所属 QQ PID 前后相同、接口仍监听，报告 `.tools/desktop-validation/qq-shortcut-check.json`。没有退出 QQ、发送消息或重新确认课程；未重测完全退出后的冷启动，既有真实 Framework 冷启动记录继续保留。
- `node --test tests/napcat-framework-launcher.test.mjs tests/qq-shortcut.test.mjs` 3/3、退出 0；最终八文件 Node 命令 65/65、退出 0。新两项覆盖中文/空格路径引用、QQ 图标、隐藏窗口、备份保留、重复安装及已运行激活/普通入口拒绝重复加载。测试起初因临时目录 8.3 短路径与 PowerShell 展开的长路径不同而误报，改为核对 realpath 后通过。正式应用、Core/Infrastructure/OCR/demo 未改，不重复记为本轮重跑。
- 需求、交接、接入说明、应用说明和测试说明已同步，并按实际快捷方式/备份/报告人工核对上下文。后续用户日常直接双击桌面 QQ；登录 Windows 自启动、QQ 内部完全退出后重新启动、真实课程/任务、OCR 和恢复/多屏的剩余验收不变。

### 上一段托盘生命周期修复（2026-10-12）

- 基线 `a2faf28`，分支 `codex/m3-integration-probes`。用户反馈托盘中找不到“消息提纯”；实际旧进程确实没有图标，不是用户未找到隐藏图标。`Program.cs` 的启动异步回调使用 `using var tray`，但正常启动以 `_ = ListenAsync(...)` 立即返回并释放图标及取消令牌；改为 `await ListenAsync(...)`，资源存活到应用退出。
- 新增 `tests/app_tray_test.ps1`（UTF-8 BOM，兼容 Windows PowerShell 5.1），限定 Explorer 托盘容器并检查可见图标，必要时展开隐藏区；`-ExitApplication` 实际右击菜单、调用“退出”并核对 ExitCode=0。旧 app 进程 43172 的启动后图标检查实际失败、退出 1；修复版图标检查通过。测试保留进程句柄以读取退出码，并等待隐藏区展开动画、忽略不可见窗口，避免测试自身误报。
- 实际发布 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-tray-fix` 退出 0。默认 `packaging/app/Start-MessagePurify.ps1` 已改为 app-tray-fix，入口回归仍保留三个旧目录；旧进程仅在核对 PID/完整路径后结束，QQ 未退出。
- 实际 `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId 24696 -ExitApplication` 退出 0，图标可见、菜单退出成功、应用 ExitCode=0；报告 `.tools/desktop-validation/tray-exit.json`。默认入口已重新打开修复版，当前 PID 26000（后续以实际进程为准）。向正常主窗口发送 WM_CLOSE 后进程和托盘保留；再启动默认入口唤回同一窗口且只剩一个 MessagePurify 进程，五项全部 true，报告 `tray-lifecycle.json` 和 `tray-hidden.json`。
- 实际 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 141/141、Node 七文件命令 63/63，均退出 0。新产物 `--smoke-test --report .tools/desktop-validation/tray-fix-native-smoke.json` 进程退出 0、success=true，原生 13 项通过；`.tools/ocr/venv/Scripts/python.exe tests/app_desktop_test.py --executable .tools/published/app-tray-fix/MessagePurify.exe` 退出 0，确认两卡片父窗/style。独立 PowerShell 默认入口 `-CheckOnly` 进程退出 0，Ready/AuthenticationReady=true、发布路径匹配，报告 `tray-fix-launcher-check.json`。Infrastructure/Core/OCR/demo 生产代码未改，沿用上段实际结果，不记为重跑。
- 真实只读复查执行已发布 app-desktop 的 `--read-only-check --seconds 20 --report .tools/desktop-validation/read-only-current.json`，进程退出 0、success=true、synthetic=false；transport=connected、qq=online、receiving=active、history=complete、failures=[]，52 个群、9 个置顶群、48 项待确认、0 个任务/通知。凭据只传子进程环境，没有写入报告、文档或仓库；未自动确认真实课程或日期。
- OCR 下一段仅做只读调查，未实现：图片 AssetId 可由本机 NapCat `get_image` 获取本地路径，现有 worker 支持 UTF-8 JSONL，但附件持久库、队列和 C# worker 宿主仍缺。已有路由回执不会因 ProcessingVersion 提高而重新路由；不得删除回执重跑首轮，以免覆盖完成、忽略、拒绝及人工修正。图片独立消息形成待确认需接续 M6 受保护再分析；附件证据及既有详情可先推进。
- 下一步真实课程/日期由用户在界面核对，验收消息到任务、卡片查看/完成；OCR 队列、M6、自启动及多屏/睡眠/Explorer 恢复仍未完成。本机仍找不到 `maintain-context-hygiene/SKILL.md`，按项目规则人工核对需求、交接、实现、发布路径及实际报告；不删除文件或修改无关文档。

### 上一段正式桌面卡片首段（已实现、发布及限定范围验证）

- 开发基线 `dc3eced`，分支 `codex/m3-integration-probes`。需求、第 3/6 节有效结论、设计正文及总计划已改为两卡片；取消全部 Windows 系统通知弹窗和声音及相关设置、点击/权限/定时/汇总验收，普通 App 通知、原文/证据、取消记录和旧计划/台账继续保留。
- 产物：`src/MessagePurify.Windows/App/DesktopCardHost.cs`、`DesktopCardProjection.cs`、`DesktopLayoutStore.cs`；回归位于 `tests/MessagePurify.Windows.Tests/DesktopCardTests.cs`、`DesktopLayoutTests.cs`、`tests/app_desktop_test.py`，桥、正式页面、原生 SmokeCheck、启动入口及相关测试同步更新。两张独立 WPF 卡片只显示当前账号 active task/pending review，按优先级和有效日期/时刻稳定排序；查看与完成复用核对账号/依赖版本的正式入口。当前主题及减少动画偏好同步，内容刷新保持原布局；提醒为空显示“暂无待办”，待确认为空隐藏。
- 布局保存于正式数据根 `%LOCALAPPDATA%/MessagePurify/App/desktop-layout.json`，schema 1，两个卡片位置/尺寸以 DIP 原子保存；首次左右摆放，读取时按当前 DPI/边界约束，坏文件或未知 schema 保留原文件并提示。修正 DPI 重挂载分支为只 Load/Clamp，不自动 Save，防止默认回退覆盖坏文件；扩展既有五项布局测试，重复改变边界/scale 读取仍保留原内容。标题拖动复用已验证几何，右下角缩放使用实际 WPF Thumb 事件；本轮未做物理鼠标拖动，多屏、Explorer 重启和睡眠恢复尚未验收。重挂载检测代码已写入，未把长期恢复记为通过；完整变化标记仍待实施。
- 单次生产差异审查发现 P2：时钟跨过截止/开始或只有日期的午夜时，日期优先级不变仍为 P0，旧 render signature 未包括 past，标签不刷新。同一 `now` 的 `IsPast` 已纳入 signature 与 Render；新增三项专项先红 0/3、退出 1，修复后 Windows 完整 Release 141/141（原 138 加 3）退出 0。没有重复整体审查循环。
- 实际执行 `.tools/dotnet/dotnet.exe restore src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --locked-mode`、`restore tests/MessagePurify.Windows.Tests --locked-mode` 均退出 0；`test tests/MessagePurify.Windows.Tests --no-restore --configuration Release` 141/141、`test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 375/375，均退出 0。Node 七文件命令 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs` 最终 63/63（文档 27、Chrome 24、入口 3、其余 9）退出 0。初轮 61/63 因测试只模拟系统主题而未固定减少动画；当前系统 motion=true，测试 CDP 固定 `no-preference` 后通过。Core/OCR/demo 未改，本轮沿用上段 Core 301/301、OCR 25/25、demo 108 的实际结果，不记为本轮重跑。
- 最终 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-desktop` 退出 0，无警告/错误。新产物 `--smoke-test --report .tools/desktop-validation/native-smoke-final.json` 进程退出 0、success=true：snapshot、complete、undo-complete、explicit-date、accept-review、native-bridge、reopen、desktop-data、desktop-theme、desktop-open-item、desktop-complete、desktop-layout、pending-hide 共 13 项通过；`.tools/desktop-validation/native-smoke-final.png` 和 `native-smoke-final-cards.png` 已实际查看。此为临时合成账号，不替代真实 QQ 课程和任务验收。
- 实际 `.tools/ocr/venv/Scripts/python.exe tests/app_desktop_test.py --executable .tools/published/app-desktop/MessagePurify.exe` 退出 0，枚举本次进程两个卡片子窗：parentClass=`SHELLDLL_DefView`，child/noActivate=true、topmost=false，任务可见、空待确认隐藏。对旧 `.tools/published/app-reminders/` 的同项检查曾真实失败，证明旧版无正式卡片。默认入口已改为 app-desktop；独立 PowerShell 执行 `packaging/app/Start-MessagePurify.ps1 -CheckOnly` 后核对实际 ExitCode=0、Ready/AuthenticationReady=true，报告 `.tools/desktop-validation/launcher-check.json`；UTF-8 输出再次核对发布路径完全匹配。旧正式窗口实际仍运行 `.tools/published/app/MessagePurify.exe`，QQ 保持运行；用户从旧版托盘退出后再双击入口加载新版，两个旧发布目录均保留。
- 设计第 12 节仍有 31 项验收，总计划逐项映射；旧通知竞争、投递和点击场景已替换为卡片刷新、当前状态、账号隔离和渠道边界。本轮未重测真实 QQ 数量，沿用上一段 9 个置顶群、40 项待确认两次结果；未自动确认课程或日期。下一步真实课程确认及消息到正式任务/卡片操作验收，再推进 OCR 队列、M6 修复、自启动和剩余完整功能；M3 和 M8 总体不标为完成。
- 本机两个技能目录仍未找到 `maintain-context-hygiene/SKILL.md`，已按项目规则人工对照需求、交接、设计、总计划、实际源文件、发布产物和报告，未删除数据或修改无关文件。七份交付文档同步完成；文档接续順序措辞检查曾为 26/27，补齐有效 HANDOFF → requirements 规则后最终 Node 七文件 63/63、退出 0；diff/staged check 退出 0，已用对应 Git commit 保存并核对工作区干净，最终提交标识以日志为准。

### 暂停接续记录（2026-10-12）

以下至“已确认的真实应用路线”前的接续、实现与探针记录均为当时范围的历史；旧“下一步”不覆盖本轮结果及第 6 节。

- 用户曾要求“先把目前的工作保存好，明天继续”；已保存并暂停。2026-10-12 用户明确要求“继续我们的工作”，暂停已结束，恢复下一段候选路由。
- 实现基线提交：`80274aa`（正式事项与待确认生命周期）；分支：`codex/m3-integration-probes`。保存前已核对工作区干净，验证结果见下方，真实 QQ 和正式界面接入状态不变。
- 当时恢复工作先读 `docs/HANDOFF.md`，再读 `docs/requirements.md`，核对 Git 和实际文件；随后已推进正式卡片首段，当前下一步以第 6 节为准，不重复询问已确认方案。

### 历史：持久化提醒第一段（旧通知方案的设置、计划、可控投递与最终发布通过）

- 产物：`src/MessagePurify.Infrastructure/Reminders/ReminderModels.cs`、`AccountReminderService.cs`；`tests/MessagePurify.Infrastructure.Tests/PersistedReminderTests.cs`、`WorkspaceReminderTests.cs`；工作区、正式桥及页面提醒设置、原生 `SmokeCheck.cs`，实施计划见上方链接。账号目录新增 `reminders.sqlite3` schema 1，原 messages/tasks/routing 三库 schema 1 不变，损坏/未知结构/账号错配不清空重建。
- 正式任务详情保存默认、提前小时、前若干天某时刻和关闭提醒，配置同时核对任务和计划版本，操作 ID 重试幂等。精确时间默认提前 24/2 小时，日期默认前日 12:00；提醒设置不修改正式时间或精度。延期按时间版本重算，内容变化不重建节点；小时规则降为日期时保留原选择、临时默认和 NeedsReview；关闭选择不重新打开。图片建议仍须用户明确确认才能产生正式计划。
- SQLite 保存节点和投递台账，外部调用前事务落盘 `sending`。可控 sink 验证时间节点、错过节点一次恢复汇总、未知结果不逐条重放、明确失败最多三次跨重启重试、权限阻挡不循环提交。未进入外部提交的 `time pending` 恢复时合入一次 recovery；`recovery pending` 接续原 ID 并吸收本次其他到期有效成员。已截止/开始、完成、取消、归档或关闭不投递，只有日期时当天仍有效。
- AccountWorkspace 同一锁跨用户操作、路由、提醒配置及外部提交；变更先发生时不提交旧计划，提交先发生时后续变更清理旧条目。已提交汇总任一成员失效可整条移除，其他成员已发台账保持；Remove 失败保留 ID 和错误供重试，不额外重发摘要。
- 唯一整体审查实际复现 P1：失败 recovery 中 A 完成/延期，使有效 B 永久停在 failed。新增四项真实 SQLite 测试先红 42/46、退出 1；持久化原始未成功成员，按当前有效未成功节点重试，保留原 ID 与三次预算后绿 46/46。完成撤销可恢复原未成功成员，已成功成员不重放；成员变化及重启不重置预算。root 再运行审查窄复现 `.tools/dotnet/dotnet.exe run --project .tools/reminder-validation/review-repro/review-repro.csproj --configuration Release` 退出 0，complete/postpone 两路均只提交有效 B 一次，B 已到期节点为 submitted；不再开启整体审查循环。
- 实际最终回归 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`：375/375（提醒服务 46、工作区提醒 24），退出 0。本段修复前已执行 Windows Release 96/96（桥新增 19）、Core Release 301/301；OCR `pip check` 通过、unittest 25/25；`node tests/prototype-qa.mjs` 108 项；Node 七文件命令 59/59（文档 24、正式 Chrome 23、入口 3、其余桥/打包 9），均退出 0。除提醒服务外的代码未因 P1 修复变化，不重复无关测试。
- 最终 `.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-reminders` 退出 0、零警告/错误；新产物 `--smoke-test --report .tools/reminder-validation/native-smoke-final.json` 通过真实隐藏 WebView2 的 snapshot、complete、undo-complete、explicit-date、accept-review、reminder-settings、native-bridge、reopen 八项，success=true、进程退出 0。截图 `.tools/reminder-validation/native-smoke-final.png` 已查看；这是临时合成账号，不替代真实 QQ 任务验收。
- 默认双击入口已指向 `.tools/published/app-reminders/`，上一版 `.tools/published/app/` 保留。最终独立 PowerShell 进程运行 `packaging/app/Start-MessagePurify.ps1 -CheckOnly`，WaitForExit 后读取 ExitCode=0，Ready/AuthenticationReady=true；原正式应用进程仍运行旧版，QQ 仍运行。要加载新版，用户从旧版托盘退出后再双击入口。初次验证包装错误地读取旧 `$LASTEXITCODE` 曾退出 1，原生检查本身退出 0；以后 WinExe/脚本进程均核对其真实退出码。
- 此历史版本页面明确显示“系统提醒尚未开启”；当时 Windows 实际投递及后台循环尚未接入，未发送 QQ/Windows 测试通知。当前已取消 Windows 时间节点、批次汇总、新发现/变更、权限恢复、五分钟断线和通知点击；旧 ReminderPlanner、SQLite plans/ledger 保留。后续推进正式桌面、自启动、OCR 队列、账号隔离和真实验收。
- 需求、交接、测试及应用说明已同步；实际 `node --test tests/project-rules.test.mjs` 为 24/24、退出 0，新增提醒进度/存储格式/系统未接入边界检查；`git diff --check` 退出 0。maintain-context-hygiene 两个本机目录仍缺 SKILL.md，已人工对照有效需求、计划、实际文件和报告；没有删除文件或修改无关文档。本段由对应 Git commit 保存，提交标识以日志为准。

### 上段正式应用首段（代码、合成闭环及真实只读宿主通过）

- 产物：`src/MessagePurify.Infrastructure/Application/ApplicationModels.cs`、`AccountWorkspace.cs`、`NapCatMessageHost.cs`；`src/MessagePurify.Windows/App/` 的正式 WPF 项目、桥、窗口、运行时和 `ui/`；双击入口 `packaging/app/Start-MessagePurify.cmd`，自包含产物 `.tools/published/app/MessagePurify.exe`。入口没有样例任务，关闭隐藏托盘，托盘可打开/退出，重复启动唤起已有窗口。
- AccountWorkspace 在同一锁内完成消息、路由、用户操作和快照，来源正文读原消息，课程/群关系与已读版本保存在账号目录 `workspace-settings.json`。原子替换失败保留旧文件；原有三库 schema 1 不变。正式事项操作复用生命周期，带账号、操作 ID、依赖版本、撤销来源 ID及关联事项版本；图片建议只填草稿，显式确认才保存。
- 页面沿用 demo 视觉；任务/待确认/通知/完成记录、搜索/课程/状态筛选、来源正文、日期确认、完成/撤销、归档恢复、忽略/撤销、拒绝建议可操作。初始有待确认则优先显示；群课程建议由取得的名单提出，确认影响新消息，旧普通待确认不重放。完整课程修复仍是 M6，可信发送者和机器人设置未接入，课程确认不授予发送者可信身份。主题/减少动画使用浏览器偏好，事项全部来自 SQLite。
- WebView2 只允许固定本地 HTTPS 页面及资源，桥拒绝外部来源、未知命令、重复字段、数字化 ID、错误版本/日期等；拒绝外部导航、新窗及权限，不把 Token 给页面。AppRuntime 从登录接口确定首个账号，保存后不自动切换，重连账号不符暂停。
- NapCatMessageHost 每个事件和历史页解析正文前核对当前置顶，历史页返回后再核对。实时原消息立即入库，历史期间暂缓路由，读取结束后按原发送时间处理；定期复核 QQ/账号/群名单/置顶，无事件也同步范围。自动重连及跨连接去重有模拟检查；分页按原返回游标、七天边界、100 页上限，无进展/空页/游标错误保留部分失败。同步游标/批次关闭未持久化，首次/重连重新尽力读取，不声称完整 M5。
- 首轮真实检查：当前普通 QQ 在运行但 3001 未监听，`--read-only-check --seconds 5` 退出 1、`connection-unavailable`；用户明确回复“我已退出 QQ，可以继续”后启动已准备 Framework，3001/6099 就绪。新进程未继承 Token 导致 `api-1403`，通过当前本机配置只向进程环境传入凭据后成功。正式启动入口已支持环境变量优先、唯一启用 loopback:3001 配置兜底；配置歧义停止，凭据不输出、不入 Git。
- 真实只读正式宿主：发布程序 `--read-only-check --seconds 20 --report .tools/app-validation/read-only.json` 退出 0，transport connected、QQ online、receiving active、history complete、失败列表为空，群名单 52、事项 40。第二个进程 `--seconds 10 --report .tools/app-validation/read-only-restart.json` 退出 0，9 个置顶群、40 项待确认、0 个正式任务/通知，与上轮相同。课程和图片日期均未擅自确认；没有发送 QQ 或 Windows 通知。本次有边界的历史读取通过，不代表持续实时新消息或独立准确率/长期稳定性验收。
- 合成真实 WPF/WebView2：自包含正式产物 `--smoke-test --report .tools/app-validation/native-smoke.json` 最终退出 0，snapshot、complete、undo-complete、explicit-date、accept-review、native-bridge、reopen 七项通过；截图 `.tools/app-validation/native-smoke.png` 已查看。最初接受测试调用 `review-form.requestSubmit`，页面没有该表单提交入口而超时；页面已补 Enter 同入口，检查也使用固定确认按钮，最终通过。不以合成账号代替真实用户课程和任务验收。
- 审查实际复现：原文“逾期不收”任务当天已为 P0，用户修改为更晚日期后优先级降为 P3，根因是从已饱和的等级反推后果。共享 `PriorityCalculator.HasClearConsequence` 保留匹配行动正文中的明确后果，不改变原提取规则或库格式；先红后绿确认远期改为 P2，否定后果/同消息其他行动不误加级。
- 实际 Release 命令 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 为 305/305（工作区 30、宿主 24）；Core 同参数 301/301、Windows 77/77。`.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app` 退出 0，零警告/错误；新项目 lock file 已写入。
- OCR `pip check` 通过、`-m unittest discover -s tests -p ocr_worker_test.py` 25/25；`node tests/prototype-qa.mjs` 108 项通过。新正式页面 `node --test tests/application-ui.test.mjs` 18/18，启动入口 `node --test tests/application-launcher.test.mjs` 2/2，包括中文/空格路径、缺产物拒绝、配置唯一性及不输出凭据。完整命令 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs tests/application-ui.test.mjs tests/application-launcher.test.mjs` 最终 53/53，退出码 0。
- 已通过正式启动入口打开“消息提纯”真实账号窗口，收尾时核对窗口仍运行、OneBot 3001 和 WebUI 6099 仍监听；QQ 保持运行，用户可查看待确认和群课程建议。尚未代用户确认课程或真实任务。
- 收尾同步完整 Node 结果、窗口状态及历史记录范围后，实际执行 `node --test tests/project-rules.test.mjs`，24/24、退出码 0；`git diff --check` 退出码 0。此次收尾仅改文档，不重复运行已通过的 C#/OCR/浏览器全量检查。
- 剩余与下一步：真实课程建议人工确认、连续实时及消息到任务验收；持久化提醒调度/在途协调/Windows 变更通知；正式桌面卡片、自启动、OCR 队列、手动新增/完整编辑、通知转任务、M6 修复、机器人/可信发送者设置、合并拆分/备份恢复和完整账号切换仍未完成。
- 上下文核对：maintain-context-hygiene 两个本机目录仍缺 SKILL.md，按项目协作规则人工对照当前需求、计划、产物与真实运行报告；未删除项目文件或修改无关文档。上述产物由本次 Git 提交保存，标识以提交后日志为准。

### 上段可靠关联变更与保守消息顺序（`6ee0735` 本地 API 与持久化验证通过）

- 产物：更新 `src/MessagePurify.Infrastructure/Processing/AccountCandidateRouter.cs`、`Tasks/TaskLifecycleService.cs`，新增 `tests/MessagePurify.Infrastructure.Tests/LinkedChangeRoutingTests.cs` 并扩充 `CandidateRoutingTests.cs`。新入口 `ProcessAndRoutePending` 从原消息顺序逐条提取与路由；旧 `RoutePending` 可处理预提取候选，但不越过更早未提取消息。
- 每条消息在 tasks 主库附加 inbox/routing 的同一事务中读取最新 KnownTasks、保存提取/处理版本、写正式变更/证据/操作/本地通知及回执；失败全部回滚。KnownTasks 完全替换调用者旧快照，只使用当前群 TrustedSenders 授权高影响变更，ConfirmedPublisherId 不由历史发送者猜测；真实关系获取与持久化仍待正式宿主。
- 可靠 Cancel/Postpone/NewRequirement/Remind 自动更新同一事项，时间手动保护冲突转待确认，取消不受时间保护阻挡。已完成事项的可靠新行动恢复待办并保留完成历史；已归档/已取消的新行动仍待确认。自动新要求保留稳定原标题，完整新要求留在原消息及操作历史。Remind 仅补证据，自动和手动接受均不使用旧候选期限；用户显式传入 confirmedTime 才修改正式时间。
- 仅尚未路由的 Pending/Change 关联行动在课程仍匹配时重新核对；已有普通 Pending/Task/Notice 不升级，课程撤销/变更使旧可靠候选转待确认。已有消息回执阻止更高提取版本重放；完整 M6 关联修复仍未实现。
- 三库 schema 1 不变，rollback journal 限制继续有效。使用 task_evidence LEFT JOIN inbox.messages 的最大原时间保守协调；除本消息外任何证据缺少时间、迟到旧变更或同秒顺序不明都保留关联待确认，不凭 messageId/rowid 判断同秒先后。尚未完整支持 real_seq 或跨群原始转发顺序，最大证据时间也可能使旧有效变更需要人工核对。
- 实际专项命令：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CandidateRoutingTests|FullyQualifiedName~LinkedChangeRoutingTests|FullyQualifiedName~TaskLifecycleTests'` 为 128/128（候选 36、关联 30、生命周期 62），退出码 0。最小取消行为红测先确认仍 Active，新入口红测确认 CS1061 缺方法；实现后通过。
- 本轮先红后绿的额外修复：通知先前记录提取前版本，raw 与预提取分别实际复现 0/1 而应为 1/2；改为候选处理结束后按最终提取版本保存通知及回执。审查发现并实际复现旧催交建议在新延期后接受会将 10/20 回退 10/13；共用 AcceptLinked 现只在明确确认时间时改变催交正式时间，完成/撤销字段不受影响。
- 完整回归：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 最终 251/251；同参数 `tests/MessagePurify.Core.Tests` 296/296、`tests/MessagePurify.Windows.Tests` 57/57。`.tools/ocr/venv/Scripts/python.exe -m pip check` 通过，`-m unittest discover -s tests -p ocr_worker_test.py` 25/25；`node tests/prototype-qa.mjs` 108 项，均退出码 0。
- 文档/打包实际命令：`node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs` 32/32（文档 23、桥 4），退出码 0。首轮旧 Change 仅待确认的正文及断言已更新为当前自动应用，上一段结果只作为历史保留；新增来源授权、顺序及正式宿主边界回归。
- 第四段保存时的剩余项：基础界面和正式只读消息宿主已在本轮接入（见上方）。提醒计划与串行投递及变更一次 Windows 通知、OCR 队列、普通通知转任务、手动新增/完整编辑与非时间字段保护、M6 修复、合并拆分和备份恢复仍在后续；第四段没有 QQ 发送或 Windows 测试提醒。
- 上下文核对：maintain-context-hygiene 两个本机目录此前均缺 SKILL.md；已按项目规则人工对照有效需求、计划、生产文件、测试与真实命令，不删除项目文件或修改无关文档。

### 上段消息候选首轮自动路由（本地 API 与持久化验证通过）

- 已写入 `src/MessagePurify.Infrastructure/Processing/AccountCandidateRouter.cs`、`CandidateRoutingModels.cs` 和 `tests/MessagePurify.Infrastructure.Tests/CandidateRoutingTests.cs`。新增每账号 `routing.sqlite3` schema 1；messages/tasks 结构不变。Task 新建事项，Pending/Change 待确认，Notice 保存完整本地记录，Ignored 仅写处理回执；当前置顶群、账号/群、机器人排除和课程排除再次检查，暂时排除不写回执。普通清楚新任务无需发布者在可信名单，与本地提取器契约一致；高影响变更仍保留来源检查。本段不代表正式 QQ 宿主或系统通知投递已接入。
- 单条消息跨库事务保存全部候选、操作和回执，失败整条回滚；拒绝 WAL/内存/关闭日志模式。消息级回执与完整字符串消息键/候选序号生成的确定性记录身份防止重启、重复消息和更高提取版本重放；通知保留原候选、证据、消息键、处理版本、来源、批次及原发送时刻。生命周期共用原事务内部逻辑，不复制状态变更。
- 最终只读审查发现并修复：提取后课程确认撤销或变更，旧 Task/Notice 均降级为待确认，保留原字段与证据，将课程及对象判断标记为不可靠；不升级原 Pending/Change。四项新增测试先实际运行失败，修正后路由及生命周期专项 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release --filter 'FullyQualifiedName~CandidateRoutingTests|FullyQualifiedName~TaskLifecycleTests'` 为 94/94（路由 32、生命周期 62），退出码 0。
- 实际完整命令：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 为 217/217；同参数 `tests/MessagePurify.Core.Tests` 为 296/296，`tests/MessagePurify.Windows.Tests` 为 57/57。`.tools/ocr/venv/Scripts/python.exe -m pip check` 通过，`-m unittest discover -s tests -p ocr_worker_test.py` 为 25/25；`node tests/prototype-qa.mjs` 为 108 项，均退出码 0。
- 文档/打包实际命令：`node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs` 为 31/31（文档 22、桥 4），退出码 0。新增路由交付与当前状态回归，区分本地通知入库、可靠变更待确认及 M6/正式宿主剩余范围。
- 第三段保存时的剩余项：可靠关联变更与保守消息顺序后来已在第四段完成（见上方）；M6 重新分析修复协调、普通通知转任务、手动新增/完整编辑、非时间字段保护、提醒计划与系统投递、OCR 队列、合并拆分、备份恢复和正式应用宿主仍未实现。已完成/忽略等人工处理不会因首轮路由重播而丢失。
- 上下文核对：本机两个 maintain-context-hygiene 目录仍无 SKILL.md；按项目规则人工对照有效需求、计划、源码、测试与实际命令结果，没有删除项目文件或改无关文档。

### 上段正式事项生命周期（本地 API 与持久化验证通过）

- 产物：`src/MessagePurify.Infrastructure/Tasks/TaskStoreModels.cs`、`TaskLifecycleService.cs` 及 `tests/MessagePurify.Infrastructure.Tests/TaskLifecycleTests.cs`。同账号目录新增 `tasks.sqlite3`，既有 `messages.sqlite3` schema 1 不变；账号归属、未知版本及无版本非空库均检查，失败保留数据。
- 已实现：可靠 Task + 正式 Time 新建事项，Pending/Change 保存待确认；接受/拒绝、完成/取消/归档/恢复、完成 1 小时及忽略 24 小时撤销。关联取消/延期/新行动接受时检查 review 和 task 两个版本、更新原事项并追加去重证据；拒绝建议保留原任务，Ignored 须先撤销才能接受。仅保护手动确认时间，不声称完整逐字段编辑已完成。
- 操作 ID 保存命令、依赖版本及参数，同请求重放返回最新记录，不重新执行；同 ID 不同请求冲突。规范化完整候选内容唯一键防止同一候选换 ID 重复建事项，证据重排/重复不改变键；不同内容候选的重新分析协调仍待后续。撤销凭据包含原操作 ID 与截止时间，旧入口不能撤销新一轮操作；当前完成/忽略/归档字段在撤销或恢复时清除，历史由操作前后快照长期保留。
- 图片 SuggestedTime 留在候选，只有显式传入 confirmedTime 才成为受保护正式时间；直接有正式 Time 的候选使用正式值。仅日期不虚构钟点。关联取消无须另填时间，取消不受时间保护阻挡，不把已取消任务恢复为待办。
- 实际命令：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release` 为 184/184（新增生命周期 61）；同配置 Core 296/296、Windows 57/57。`.tools/ocr/venv/Scripts/python.exe -m pip check` 通过，`-m unittest discover -s tests -p ocr_worker_test.py` 25/25；`node tests/prototype-qa.mjs` 108 项，均退出码 0。最初红测仅缺生命周期实现；首次生产编译的泛型推断错误已按 CS0411 修正，随后两项参数错误类型与约定对齐；审查后补齐完成后取消/归档的当前时间戳清除，新增三项检查确认操作历史仍保留。
- 文档/打包验证：实际运行 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs tests/pinned-groups-bridge.test.mjs`，30/30、退出码 0。首轮两项失败分别因测试说明未明确宿主未联调及精简总览丢失 QQ 登录与 OneBot 区分，补齐真实边界后通过。
- 本段保存时的剩余项：候选路由及可靠关联变更后来已在第三、四段完成（见上方）；普通通知转任务、手动新增/编辑及非时间字段保护、提醒计划与系统投递、OCR 队列、合并拆分、备份恢复和正式应用宿主仍未实现。
- Git 收尾：本段 11 日开始，提交时间为 12 日凌晨（北京时间）；验证后保存到当前分支，提交标识以 Git 日志为准。暂存 diff 检查退出码 0，提交后工作区为空。
- 上下文核对：两个技能目录的 maintain-context-hygiene 均只有 agents 目录、没有 SKILL.md；按项目规则人工对照需求、计划、源码、测试与实际命令结果，没有删除项目文件或改无关文档。

### 本次置顶群范围调整（代码及真实名单/历史限定验证通过）

- 用户新增明确规则：只读取置顶的群聊消息，替代此前所有群聊范围；实时、历史和补漏统一受限。需求已确认，过滤代码及只读桥已写入并加载真实 QQ；旧全群报告仅是规则调整前的历史结果。当前规则下真实名单与分页/重连限定范围通过，正式自动补漏和长期实时验收仍待完成。
- 本机 NapCat v4.18.37 的 `get_recent_contact` 会读取最近消息但不返回置顶标志，不能据此判断置顶；自定义插件被该版本的官方插件白名单拒绝，未绕过白名单或冒充官方插件。诊断插件未加载，已删除本轮临时文件及启用标记；曾注册空插件管理器，未发送 QQ 消息。改为项目的 Framework 加载入口注册唯一读取 action `messagepurify_get_pinned_groups`，从原生快照只选取群类型/置顶标记/群 ID，不请求最近消息的消息读取接口。
- 产物：`packaging/napcat/pinned-groups.mjs`、`nativeLoaderPinned.cjs`、`Prepare-PinnedGroups.ps1`；Framework 入口在启动时生成带额外导出的 `messagepurify-napcat.mjs` 副本，保留官方 `napcat.mjs` 和 `nativeLoader.cjs`。真实快照 139 项会话、排序列表也为 139，群类型 62 项（包括非当前已加入群的最近会话）；群的 `topFlag` 为数字，实际 1 的 9 项、0 的 53 项。代码只接受 0/1，校验根结果和内部结果、排序/变更列表长度一致及不足 10000 上限；未知标记或快照不完整即失败，不凭顺序/群名推断。首次 `isTop` 假设已由实际 `topFlag` 替代。
- `OneBotClient` 读取桥返回的账号和群 ID 集合；`ProbeRunner` 初始筛群、每页及重试前重新核对，每个实时群事件解析正文前重新核对，重连重建范围。非置顶事件只计排除数量，不解析正文/消息段或存盘；NapCat 通道本身仍可能推送其他群事件。`--history-groups all` 也仅表示所有置顶群。状态读取或格式失败停止本次连接，不回退到全群；不自动清理已有事项或证据。
- 模拟回归：Infrastructure Release 最终 78/78；覆盖混合群/空名单、非置顶坏正文提前排除、首屏/分页/实时处理时取消置顶、重连更换范围、未知状态及账号错配。桥测试 4/4，使用真实版本的 `{get, call}` 结构并检查原接口委托不变。首次接入报 `context.actions.set is not a function`，根因为该版本仅暴露读取包装器，不是 Map；已改为只扩展 `get` 的唯一项目 action，保留原调用方法及其他读取 action。
- 用户明确回复“已退出”后启动项目入口；定位注册问题和原生字段期间，核对路径后重启本轮启动的 QQ 进程继续验证，无需再次扫码。当前 3001/6099 同属于 QQ PID 38900，项目读取桥 ready，QQ 在线。旧进程的新探针先返回 1404、历史尝试及消息均为 0、退出码 1，验证接口不可用不回退；报告 `.tools/m3-results/qq-pinned-bridge-unavailable-20261011.md`。
- 真实执行 `python .tools/m3-results/run-current-framework-probe.py .tools/m3-results/qq-pinned-run-20261011/MessagePurify.QqProbe.dll --history-groups all --history-count 100 --history-days 7 --history-pages 20 --reconnect-rounds 1 --listen-seconds 15 --output .tools/m3-results/qq-pinned-history-reconnect-20261011.md`，22:35:24（北京时间）开始，退出码 0。52 个已加入群中的 9 个置顶群各读两轮，18/18 到达 `date-boundary`，历史计数 686、去重 341、重复 345、账号核对 2/2、重连 1/1、重试 0、实时群事件 0、重连后首次发现 0。其余 43 群未请求历史；没有真实实时群样本，不能把模拟实时排除或受控重连当作长期接收/非预期断网/正式补漏验收。到达日期边界仍不保证 QQ 服务器的历史完整。
- 本轮全部自动回归：Core Release 296/296、Infrastructure Release 78/78、Windows Release 57/57、OCR `pip check` 与 unittest 25/25、Node 五文件 29/29（文档 20 项、桥 4 项）、demo 108 项，均退出码 0。实际命令按 `tests/README.md`，新桥准备/语法检查亦通过。私人配置、运行副本、日志和脱敏诊断/报告均留在 `.tools/m3-results` 或 NapCat 忽略目录，不提交账号、群名或正文。
- 本机未找到 `maintain-context-hygiene` 技能，按项目规则人工核对当前需求、交接、设计/计划、源码、测试及真实报告；没有删除项目文件来缩短上下文。该阶段之后已完成 M4 四段本地能力；下一步正式界面与宿主联调，保留真实动态置顶/长期接收及多屏/恢复等未验收项，不重复静音提醒及桌面基本测试。

### 本次受控重连及跨连接历史去重

- 用户要求继续，沿用 M3 范围推进连接恢复验证；当前 OneBot 3001/6099 正常，不要求再次退出或扫码。只断开探针自己的 WebSocket，QQ 客户端继续运行，没有修改 QQ 或系统设置、发送 QQ 消息或重复 Windows 测试提醒。
- `ProbeArguments` 增加 `--reconnect-rounds 0..2`（默认 0）。每轮读取历史并监听指定秒数，正常结束后等待 2 秒重新连接；重新核对第一次读取的账号，账号变化立即停止。每次从最新页开始，分页游标与页内去重集合重新建立，报告的消息键去重跨连接保留。取消后不继续连接，历史失败在后续成功后仍保留。
- `ProbeReport` 新增连接尝试/账号核对、重连完成/请求、重复消息、重连后首次发现消息键的统计；群结果与失败按连接序号区分，群序号只属于该轮列表。首次发现不证明消息恰好在断开期间发送。报告不包含账号、群名、正文、Token 或游标。
- 新增 `tests/MessagePurify.Infrastructure.Tests/ReconnectProbeTests.cs`，loopback helper 支持连续连接与账号变化。模拟回读新增消息、跨历史/实时/连接去重、每轮新游标、变更账号不读新历史、先前失败不被后续成功覆盖，以及取消后不再连接。初跑 1 项失败源于合成事件缺少 `post_type`（被客户端按响应解析），补齐正确协议字段后 Infrastructure Release 67/67、退出码 0，没有修改协议校验去迁就坏数据。
- 真实执行 `python .tools/m3-results/run-current-framework-probe.py .tools/m3-results/qq-reconnect-run-20261011/MessagePurify.QqProbe.dll --history-groups all --history-count 100 --history-days 7 --history-pages 20 --reconnect-rounds 1 --listen-seconds 15 --output .tools/m3-results/qq-controlled-reconnect-20261011.md`。21:40:37（北京时间）开始，两次连接账号核对 2/2、主动重连 1/1；52 群各读两轮，82/104 到达边界（每轮 41/52），同 11 群每轮持续 1200，各重试一次，合计 22 次重试。历史计数 2206、去重 1098、重复 1108、重连后首次发现 0、实时群 0、其他事件 4。退出码 1 如实反映历史未补全；重连及跨连接去重限定范围通过，没有声称真实断开期间的新消息补回。
- 真实报告和独立 Release 运行副本在 `.tools/m3-results/qq-controlled-reconnect-20261011.md` 及 `qq-reconnect-run-20261011/`，均不提交私人配置或原始消息。模拟新消息补回与真实只读重连分别记录；没有进行 QQ/NapCat 重启、非预期断网自动恢复或跨进程持久化验收。
- 本轮完整自动回归：Core Release 296/296、Infrastructure Release 67/67、Windows Release 57/57、OCR `pip check` 与 unittest 25/25、四文件 Node 24/24（文档 19 项）、demo 108 项，均退出码 0；实际命令沿用 `tests/README.md`，Infrastructure 命令及真实探针命令已列在上方。`git diff --check` 退出码 0。最终只读核对 3001/6099 仍属于 QQ PID 10824。
- M3 继续实施：11 群历史可用范围、长期实时接收/通知稳定性、桌面恢复/多屏及布局持久化仍待验证；正式数据库、自动重连服务、消息转事项与提醒联调尚未完成。下一步准备可独立推进的本地存储和消息处理代码，继续保留未验收项目，不重复已经通过的静音和桌面基本操作。
- 按项目规则人工核对交接、需求、计划、源码、测试与报告；本机仍未找到 `maintain-context-hygiene` 技能，没有删除文件或改无关文档来缩短上下文。

### 本次静音提醒验收与真实历史分页

- 本机静音提醒于 20:33:12（北京时间）提交，通知 ID 36667；用户确认“看到了，点击能打开”。20:33:15 点击记录与发送 Probe ID 一致，协议激活、账号匹配、无错误。发送报告 `.tools/m3-results/silent-presentation-20261011.json`，点击报告 `%LOCALAPPDATA%/MessagePurify/IntegrationProbe/results/protocol-0fb7539916ae42e0b5dd7bfb24a350aa.json`。使用 DLL 与安装相同的 `.tools/windows-probe-desktop-grip-fixed/` 发布副本；不重复提醒或声音验收，长期通知稳定性未验收。
- 用户退出普通 QQ 后，已恢复 `packaging/napcat/Start-NapCat-Framework.ps1`；3001/6099 属于 QQ PID 10824。0 群、0 秒只读基线为在线、账号核对、52 群、NapCat 4.18.37 / v11、退出码 0，报告 `.tools/m3-results/qq-framework-resume-20261011.md`。QQ 保持运行，没有要求再次退出或登录。
- 扩展 `tools/MessagePurify.QqProbe/` 参数、读取及报告：7 天窗口、`all` 群、每页/每群上限、一次失败重试；按发送时间及同秒数值 `real_seq` 选择最早消息的原样游标，短页继续、边界去重、账号/群核对、明确未补全原因，不记录账号/群名/正文/游标/Token。默认一群一页保留。
- 首次全群分页发现 3 群重复页。实际方向对照（状态/账号/群列表及 9 次历史读取）显示相同最早游标 `reverse_order=false` 均 0 新增，`true` 分别 99/96/85 条更早消息；据此修正 `src/MessagePurify.Infrastructure/NapCat/OneBotClient.cs` 的带游标请求方向，首次最新页不变。报告 `.tools/m3-results/qq-history-direction-20261011.json`，不是仅凭参数名推断。
- 最终真实命令：`python .tools/m3-results/run-current-framework-probe.py .tools/m3-results/qq-paged-fixed-run-20261011/MessagePurify.QqProbe.dll --history-groups all --history-count 100 --history-days 7 --history-pages 20 --listen-seconds 60 --output .tools/m3-results/qq-paged-history-fixed-20261011.md`。20:50:00（北京时间）开始，41/52 到达边界（39 日期边界、2 成功空页）、11 群持续 1200、11 次重试；历史计数 1103 含重叠，窗口内去重 1098、实时群 0、排除私聊 1、其他事件 6。退出码 1 如实反映 11 群未补全；原 3 群方向问题解决，无页上限/游标/无进展失败。到达边界不保证 QQ 历史完整，1200 不冒充成功空页。
- 新增 `HistoryPagingTests.cs`，扩展 loopback helper、参数测试及历史方向测试。方向断言先失败、修复后通过；实际重新运行 Core Release 296/296、Infrastructure Release 60/60、Windows Release 57/57、OCR `pip check` 与 unittest 25/25、demo 浏览器 108 项，均退出码 0。最终运行 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`，23/23、退出码 0；静音验收和分页边界文档检查通过。
- M3 仍在实施：完整历史补漏、断线恢复、长期实时接收/通知稳定性、桌面恢复/多屏和布局持久化仍未验收，正式持久化/任务生成/提醒联调尚未完成。下一步处理真实历史可用范围及剩余集成项，不重复静音提醒和已通过的桌面基本操作。
- 本机仍未找到 `maintain-context-hygiene` 技能，按项目规则人工核对需求、交接、计划、代码、测试及真实报告一致，没有删除文件或为缩短上下文改无关文档。
- `git diff --check` 退出码 0；工作区只包含本轮分页代码/测试和相关文档，私人运行报告与配置仍在忽略目录。

### 本次桌面卡片不可见修复

- 用户反馈按 Win+D 未看到卡片，并提供截图：白色窗口是验证状态控制窗，不能作为卡片显示通过。原版报告 `.tools/m3-results/desktop-current-20261011.json` 和 `desktop-reproduce-20261011.json` 为 `attachedStructureOnly`、父窗 `Progman`、DPI 120，但窗口被桌面图标层遮住。
- 受控实验只重新挂载探针自己的卡片窗口；挂到外层 `Progman` 时捕获壁纸，改挂其 `SHELLDLL_DefView` 子窗后绘制两张卡片。`DesktopHost.cs` 已按此证据修正父窗选择，未修改 Explorer 或 QQ；`attachedStructureOnly` 仍只代表结构检查。
- 新增 `tests/desktop_attachment_test.py`，对真实发布产物按启动 PID 核对父窗、窗口可见标志、子窗/不激活/非置顶样式及物理边界，并正常关闭自己启动的测试窗口。实际运行原版 `.tools/windows-probe-silent`，退出码 1，复现父窗 `Progman`；发布 `.tools/windows-probe-desktop-fixed` 后用同一命令回归，退出码 0。Windows Release 57/57、退出码 0。
- 修复版已通过 `packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-desktop-fixed` 更新当前用户安装，退出码 0，安装 DLL SHA256 与发布一致；没有覆盖正在运行的旧产物。运行 `--interactive-desktop --wait-seconds 600 --report .tools/m3-results/desktop-fixed-20261011.json`，父窗 `SHELLDLL_DefView`、DPI 120、`error=null`，窗口捕获 `.tools/m3-results/desktop-fixed-render-20261011.png` 显示左右两张合成卡片。用户要求“再来一遍我看看”，已重新启动同一修复版，最新报告 `desktop-fixed-resend-20261011.json`。
- 用户确认父窗修复版“看到了，拖动正常”，随后反馈“无法缩放，只能拖动位置”，且白色状态窗伴随卡片显示。实际鼠标诊断发现：在原 18 DIP 小灰块内拖动，尺寸从 850×375 变为 910×405，位置不变；离右下角 30 像素的区域拖动，大小没有变化。扩大手柄至 32 DIP，增加“↘”箭头及缩放提示；成功挂载时不再显示状态窗，失败处理入口保留。
- 扩展真实发布产物回归：成功挂载不能有可见的顶层状态窗；`--mouse` 在扩大后的手柄区域实际拖动，验证宽高分别增加 60/30 物理像素且左上角不变，并恢复指针。原版成功状态窗回归退出码 1；最终 `.tools/windows-probe-desktop-grip-fixed` 的 `python tests/desktop_attachment_test.py --executable .tools/windows-probe-desktop-grip-fixed/MessagePurify.Windows.IntegrationProbe.exe --mouse` 退出码 0。鼠标回归前短暂 `Shell.Application.MinimizeAll()`，结束后 `UndoMinimizeALL()`，未操作 QQ 消息。
- 最终 Release publish 与当前用户安装退出码 0，安装 DLL SHA256 与发布一致。最新运行报告 `.tools/m3-results/desktop-grip-fixed-20261011.json`，捕获 `desktop-grip-fixed-render-20261011.png` 显示箭头。用户确认“缩放正常，也没有白色窗口”，随后确认“正常，不会盖住其他软件”。桌面基本人工项（Win+D 可见、拖动、缩放、普通窗口遮挡及无多余状态窗）通过；程序报告的 `manualChecks` 是自动模板，用户验收以本段反馈为准。
- 桌面修复阶段实际完整回归：`.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release` 295/295、同参数 Infrastructure 46/46、Windows 57/57，OCR `pip check` 与 unittest 25/25、四文件 Node 22/22、`node tests/prototype-qa.mjs` 108 项，均退出码 0。后续手柄与状态窗只改 Windows 探针及说明，Windows 57/57 和真实鼠标回归再次通过。多屏、Explorer 重启、睡眠恢复、布局持久化和长期稳定性未验收，M3 继续实施；下一步静音呈现、学校通知日期角色确认及剩余稳定性/补漏验证。
- 已按项目规则人工核对需求、交接、源码、报告和剩余验收范围；本机仍未找到 `maintain-context-hygiene` 技能，没有为缩短上下文删除文件或改无关文档。

### 本次学校通知截图 OCR 实测与“交齐”规则补充

- 用户提供一张真实学校群通知截图（原图只保存在本机临时目录，不写 Git）。使用项目本地 RapidOCR worker 离线处理，图片 1828×674，识别 15 行，退出码 0，stderr 为空；原图未改写，网络 guard 未发现连接尝试。关键日期行最低置信度 0.98118，截图消息时间识别为 `2026/09/26 12:43`。
- OCR 正确识别出 2025-2026 学年、填写日期范围 9 月 26 日至 10 月 16 日，以及“10 月 12 日之前交齐”的截止表达。用户明确说明后者是班长要求同学交齐材料，提取为用户待办事项的截止建议；截图正文是学校通知内容，不是用户对编码助手或软件的操作指令。消息时间是元数据，9 月 26 日至 10 月 16 日是表格落款范围，不应直接作为提醒。
- 真实 OCR 的完整日期角色仍需确认：日期范围按 `MessageDateParser` 返回 `Ambiguous`；OCR 图片中的日期只能作为 `SuggestedTime`，不能直接建立正式提醒。原图及完整 OCR 文本仅在 `.tools/m3-results/user-school-notice-ocr*.{jsonl,txt,json}` 留存，交付文档不写姓名、QQ 号或全文。
- 这次实测暴露规则缺口：`GetAction` 原先不识别“交齐”，导致整图不能形成待确认候选。已补充“同学交齐材料”的事项识别；`RuleAction.Submit` 表示通知中要求用户完成的行动，只用于分类，不执行提交或回复。脱敏回归现名为 `ClassMonitorImageSubmissionRequirementKeepsDeadlineAsSuggestion`，发布者使用 `class-monitor`：单独 OCR 行得到 `Submit/Pending`，建议日期为 2026-10-12、正式时间为空，仍要求查看原图确认。没有把该截图直接写入测试。
- 实际回归：OCR `pip check` 与 `unittest` 25/25，新增后 Core Release 296/296，四文件 Node 22/22；原图 OCR 运行报告为 `.tools/m3-results/user-school-notice-ocr-summary.json`。这证明本地 OCR 和动作/建议日期边界，不证明正式应用已完成学校通知任务生成、日期人工确认或提醒投递。

### 本次“交齐”含义澄清

- 用户纠正：“交齐”是班长要求同学按期提交材料，不是对编码助手的指令。核对 `MessageRuleExtractor` 后确认现有规则仅返回事项候选，无提交材料或 QQ 回复逻辑；修正之前“提交动作”的表述，保留学生待办及截止日期识别。
- `RuleAction` 增加分类语义说明；脱敏图片测试改用班长发布者，并核对候选保留原消息证据、没有关联到已有事项、正式时间仍为空。需求与上方 OCR 结论已同步；正式应用联调验收范围不变。
- 实际运行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`，296/296；`node --test tests/project-rules.test.mjs`，17/17；`git diff --check`，均退出码 0。此次只改说明与回归测试，没有重跑 OCR、Windows 或 QQ 探针，也未发送通知或 QQ 消息。
- 本机未找到 `maintain-context-hygiene` 技能，按项目规则人工核对需求、交接、实际规则和剩余验收一致，没有删除文件。

### 本次取消声音提醒

- 2026-10-11 用户表示不需要声音提醒，原因是不一直坐在电脑前；第一版改为统一静音，删除声音开关及声音播放验收。此前按任务类别播放声音和总开关的规则由本条替代。视觉通知、通知中心记录、桌面卡片高亮及恢复运行时的一次汇总仍保留；安静时段的“暂未启用”入口沿用已确认规则。
- demo 已删除声音开关，提醒预览统一静音；加载旧演示存储时丢弃原 `sound` 字段，其他设置及事项保留。Windows 通知载荷固定 `<audio silent="true"/>`，不再提供声音参数或 `notificationSound` 人工验收项；`normal`/`silent` 入口名称兼容保留，实际均静音。
- 实际执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`（57/57）、`node tests/prototype-qa.mjs`（108 项）、`node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`（22/22），均退出码 0。浏览器回归覆盖考试/作业/活动静音、移除开关、旧声音偏好不恢复、其他设置与事项保留。
- 实际发布 `.tools/windows-probe-silent` 并通过 `packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-silent` 更新当前用户安装，均退出码 0；安装 DLL 的 SHA256 与发布一致。本轮没有发送新提醒、读取 QQ 或重跑未改动的 Core/Infrastructure/OCR；静音通知呈现仍未进行新的人工验收。
- 需求及设计、计划、源码、测试及剩余验收已人工核对一致，未找到的 `maintain-context-hygiene` 技能继续按项目规则人工核对。正式应用仍尚未接入探针能力，取消声音不代表静音通知呈现或 M3 已验收。

### 本轮接续 M3 真实集成探针

- 用户要求继续，已从 `0c0893a` 进入 `codex/m3-integration-probes`，沿用已确认设计；[M3 详细计划](superpowers/plans/2026-10-10-integration-probes.md)已写入。M1／M2 基线实际重跑 Release 核心 295 项通过。
- 已只读检测普通 QQ 安装及进程（`D:/QQ`、版本目录 `9.9.36-53644`）。官方 Shell 入口依赖注册表而失败，已提供 `packaging/napcat/Start-NapCat.cmd` 直接指定 QQ 路径。用户确认退出普通 QQ 后，本轮启动 Framework 并自动登录；QQ 主进程加载 `napiloader.dll`，WebUI 6099 与 OneBot 3001 已监听。普通 QQ 登录不等于 OneBot 连接，实际只读连接结果单独记录。当前执行会话提权，通知须在支持的非提权环境实测；尚无 M3 完成结论。
- 用户已提供真实 QQ 截图并确认六处红框中的群均为学校相关群；名称通常正规，也有非正式名称，群名只辅助候选，不能据此排除。截图是群列表和私聊界面，没有完整学校通知正文及日期；可用于真实截图 OCR，不作为通知日期提取验收。原图及识别全文仅用于本机，不写 Git。
- 用户反馈担心接入后无法继续使用 QQ。已纠正此前关于 Shell 桌面聊天能力的说明：Shell 是无界面接管路线，同一账号不能与普通 QQ 并行登录。Framework 本轮已实际加载，用户从托盘打开后确认“聊天列表和窗口正常显示”；窗口打开期间同一进程的只读接口复测通过。启动日志曾出现 `updateWindowSizeProperties` 的 TypeError，长期稳定性尚未验证；没有确认的窗口显示故障，不盲目替换加载器或重新登录。
- M3 当前实际结果见[集成探针验证记录](verification/integration-probes.md)：本轮自动回归 Core 296、Infrastructure 46、Windows 45、OCR 25、Node 21、demo 107 均通过。前四次真实只读探针分别监听 30、60、60、300 秒，四次均退出码 0；群 52 个、历史成功 1/1、历史消息 7 条、实时群消息 0 条。第五次 180 秒探针实际收到 1 条实时群消息，退出码 0。不把接口通过替代实时接收、七天补漏、正式任务生成、日期人工确认或 M3 完成。高完整性进程按预期阻止通知；发布时补齐官方资源后，独立中等完整性进程注册并提交测试提醒成功。当时用户点击未观察到激活回调；本次专用 URI 修复后的用户实际点击通过，桌面卡片人工项已基本通过，仍未验收多屏、恢复和长期稳定性。本阶段已有提交 `fbef082`、`42c939a`、`3d87f4f`、`1fd1b1c`、`f980c51` 和 `4128526`。
- 本轮启动入口为 `packaging/napcat/Start-NapCat-Framework.ps1`，实际探针运行 `.tools/dotnet/dotnet.exe tools/MessagePurify.QqProbe/bin/Release/net10.0/MessagePurify.QqProbe.dll --listen-seconds 30 --output .tools/m3-results/qq-framework-current.md`，随后以 60 秒输出 `qq-framework-events.md`、`qq-framework-desktop-open.md`，均退出码 0。原始日志、配置备份和脱敏报告只在 `.tools/m3-results/`；Token 只保存在本机配置和子进程环境，不写入文档或 Git。
- 本轮更新文档回归：初次运行四文件 Node 测试 18/19，通过结果报告缺少明确“实时群事件尚未验收”正文而失败；补齐范围后重跑 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`，19/19、退出码 0，`git diff --check` 退出码 0。本轮未改产品代码，也未重跑此前 Core/Infrastructure/Windows/OCR/demo 套件。已人工核对需求、设计、交接、报告和本机实际状态；下一步继续实时群事件与 M3 剩余人工验收。
- 上述接入检查已保存为 `4128526`。随后继续 Windows 通知验证，发现原权限检测只判断管理员组，不能识别 Basic User 进程仍保持高完整性；新增 `ProcessIntegrity.cs` 改为查询真实令牌完整性，并新增独立 `whoami` 回归，Windows 45/45、Release publish 和当前用户安装退出码 0。Basic User 复跑正确返回 `blockedElevated`；当时独立进程降到中等完整性后仍返回 `0x80070490`、没有通知 ID。后续已分别定位包身份与资源缺失并成功提交，见下文；未改系统策略或 QQ 进程。
- 通知检查后实际重跑 Core Release 295、Windows Release 45、OCR pip check 与 unittest 25、四文件 Node 19、demo 107，全部退出码 0。Infrastructure 初次构建因本轮仍在运行的 300 秒只读探针锁住 DLL 而退出码 1，代码未改；先执行同项目 `--no-build --no-restore --configuration Release`，46 项通过、退出码 0。监听正常结束后再执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release`，构建与 46 项均通过、退出码 0；没有关闭 QQ。四文件 Node 回归随后扩展为 21/21，新增通知资源提取和打包安装检查。
- 300 秒只读探针报告为 `.tools/m3-results/qq-framework-live-observation.md`，退出码 0、历史 7 条、实时群消息 0、其他事件 11、无失败。用户现已回答“刚收到新的群消息”；第五次探针复制运行产物到独立忽略目录，再实际运行 `.tools/dotnet/dotnet.exe .tools/m3-results/qq-live-run-20261011-feedback/MessagePurify.QqProbe.dll --listen-seconds 180 --output .tools/m3-results/qq-framework-user-feedback.md`，退出码 0，13:42:06（北京时间）开始、实时群消息 1、历史 7、去重后 8、无失败。实时群事件限定范围通过（观察到 1 条），不代表所有群、图片事件或长期稳定性通过。保持 Framework QQ 运行；当前用户实际点击通过，下一步继续静音提醒呈现、桌面人工操作、实时稳定性和历史补漏验收。
- 运行 `python .tools/m3-results/check-recent-group-traffic.py`，每群仅一页 5 条：52 群尝试、41 成功、11 接口失败、历史合计 178 条，近十分钟 2 条分属 2 群，最新时间 13:42:10；退出码 0仅表示诊断完成，不作为全部群历史通过。原统计在 `.tools/m3-results/qq-recent-group-traffic.json`，当时未定位失败原因；后续限定样本诊断见下文。群名、账号、正文及 Token 不输出、不提交；历史诊断不代替实时探针。
- 用户询问是否会往学校群发送消息，已核对源代码及既有 loopback 读取 action 断言。QQ 接入只读，不向 QQ 群或私聊发送消息；学校群通知及测试提醒不通过 QQ 发送，提醒采用本机 Windows 通知。需求及设计同步明确此边界，不修改 NapCat 本身提供的接口能力。
- 本次文档检查初跑 19/20，报告缺少明确的长期稳定性未验收状态；补齐正文并纠正设计、M3 计划及测试说明中的旧实时状态。随后加入通知资源提取和冷启动回归，实际重跑四文件 Node 测试为 21/21、退出码 0；实际运行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-build --no-restore --configuration Release --filter FullyQualifiedName~OneBotTransportTests`，5/5、退出码 0，精确断言探针只调用五个读取 action。本次未改产品源码，未重跑完整 Core/Infrastructure/Windows/OCR/demo 套件；已人工核对需求、交接、设计、计划及本机脱敏报告，不把一条实时事件作为长期或七天补漏验收。

- 本轮修正 Windows App SDK 2.5.1 自包含发布缺少 `Microsoft.WindowsAppRuntime.Insights.Resource.dll` 的问题：`MessagePurify.Windows.IntegrationProbe.csproj` 在 Publish 后从匹配官方运行时 MSIX 提取资源，安装脚本也会拒绝缺少该文件的产物；资源字节来自本机 NuGet 缓存，不写入仓库。打包资源提取、覆盖旧文件、缺资源拒绝及空格/中文路径安装回归均通过。
- `NotificationProbe` 在 `Register()` 后读取 `AppInstance.GetCurrent().GetActivatedEventArgs()`，补上 SDK 首次启动参数消费。受控 COM 冷启动回归报告 `.tools/m3-results/cold-notification-regression-summary.json`：修复前未定位，修复后 `activationObserved=true`、`activationKind=cold`、账号匹配；独立 `tests/notification_cold_activation_test.py` 使用正式发布产物退出码 0。该结果只证明回调路由，不能替代用户实际点击验收。
- 14:28:38 已按用户要求通过真实 Explorer `.lnk` 重发普通测试提醒，报告 `.tools/m3-notification-diagnostic-publish/results/normal-medium-user-resend-20261011.json` 为 `submitted`、ID 36646、`error=null`、`activationObserved=false`；无包身份 15700、完整性 RID 8192。用户此前已看到一条提醒但点击没有反应；当时运行副本尚未包含冷启动修复，点击问题现已通过专用 URI 入口修复，旧声音验收现已取消。直接从 Codex 运行辅助脚本继承包身份 122，会再次出现 `0x80070490`；无包身份后缺资源错误为 `0x8007007E`，两者分别定位。
- 群历史错误诊断实际运行 `python .tools/m3-results/diagnose-group-history-errors-20261011.py`，退出码 0；状态一次、群列表一次、历史两次。报告 `.tools/m3-results/qq-history-errors-diagnosis-20261011.json` 复现一个失败样本 `retcode 1200/data=null`；成功对照返回 5 条消息。失败样本为 NapCat 对底层空历史返回接口错误，未逐群复测全部 11 个失败；仍需在正式同步中逐群展示失败并完成七天补漏策略，不修改 QQ/NapCat 客户端。
- 保存前重新执行 Windows Release 测试，45/45、退出码 0；通过真实 Explorer 辅助进程运行正式发布产物的冷启动回归，`.tools/m3-results/cold-test-result.json` 为退出码 0、PASS。直接从 Codex 运行包装脚本的包身份环境错误已与产品回归区分；无需再次退出或登录 QQ。未找到 `maintain-context-hygiene` 技能，已人工核对交接、需求、实际源码及验证边界。
- 通知修复、测试和文档已保存为 `bdffc7b`。14:28 测试等待结束后，14:40:56 使用包含冷启动修复的正式发布产物再次发送本机提醒；报告 `.tools/windows-probe-publish fixed/results/normal-medium-user-resend-fixed-20261011.json` 为 `submitted`、ID 36648、`error=null`，等待点击 600 秒。用户随后反馈该条提醒多次点击无反应；现已改用专用 URI 入口并取得实际点击通过结果，旧声音验收现已取消，不再重复登录 QQ 或无必要地发送提醒。最新文档边界断言与交付文件检查已更新，实际运行 `node --test tests/project-rules.test.mjs` 为 16/16，`git diff --check` 退出码 0。

### 本次通知点击修复与真实反馈

- 用户反馈多次点击无反应。旧通知 36648 对应 COM 激活出现 DistributedCOM 10010 超时；真实 Explorer 辅助进程的不同完整性测试均出现超时，完整性差异不记为唯一根因。确定的参数缺口是 Windows 会追加 `-Embedding`，旧解析器拒绝该参数；已修复并补回归。
- 新增 `src/MessagePurify.Windows/IntegrationProbe/NotificationProtocol.cs`，以当前用户注册的 `messagepurify-probe` URI 打开合成事项；严格校验字段，点击只显示详情，不注册或发送通知、不读取 QQ。发送与点击报告使用同一 Probe ID，声音意图与静音载荷保留。新增 `tests/MessagePurify.Windows.Tests/NotificationProtocolTests.cs`、`tests/notification_protocol_activation_test.py`；受控 Shell 分发与实际通知点击分开记录。
- 14:52:34 发送的普通测试提醒为 ID 36649，发送报告 `.tools/windows-probe-protocol/results/normal-protocol.json`；用户回答“出现了事项窗口”。14:54:38 的 `%LOCALAPPDATA%/MessagePurify/IntegrationProbe/results/protocol-*.json` 点击记录与发送 Probe ID 一致，`activationObserved=true`、`activationKind=protocol`、账号匹配、`error=null`。用户实际点击通过，不再等待同一项反馈。
- 实际运行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release`，57/57、退出码 0；发布 `.tools/windows-probe-protocol-final` 退出码 0。`python tests/notification_protocol_activation_test.py --executable .tools/windows-probe-protocol/MessagePurify.Windows.IntegrationProbe.exe` 退出码 0，受控报告使用 `controlled-shell-` Probe ID；带 `-Embedding` 的最终产物受控 COM 回归 `.tools/m3-results/cold-protocol-final-test-result.json` 为退出码 0、PASS。
- 实际通知运行副本为 `.tools/windows-probe-protocol/`，最终发布产物为 `.tools/windows-probe-protocol-final/`；执行 `packaging/probe/Install-Probe.ps1 -PublishDirectory .tools/windows-probe-protocol-final` 更新当前用户安装，退出码 0，安装 DLL 的 SHA256 与发布一致，没有覆盖仍打开的事项窗口。当前用户协议注册仍指向实际通知运行副本，后续从新的安装入口发送会重新注册到对应可执行文件。声音验收已取消；静音提醒呈现、桌面人工项、学校通知日期角色人工确认、正式任务生成、长期稳定性及七天补漏仍未验收；M3 继续实施。
- 本次四文件 Node 回归初跑 20/21：文档断言要求同一行明确“受控检查不替代实际点击”，更新后的说明分散在两行；补齐说明后实际重跑 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`，21/21、退出码 0。本次没有重跑未改动的 Core、Infrastructure、OCR 和 demo 套件；已人工核对当前需求、设计、交接、源码、报告及剩余验收一致，未找到的 `maintain-context-hygiene` 技能继续以项目规定的人工核对替代。

### 上轮 M2 本地规则与样本

- 用户要求继续工作，沿用整体设计及已确认技术路线推进 M2。已核对 M1 源码、锁文件、87 项核心测试及 demo 的二次确认改动，工作从 `8f9acdc` 创建 `codex/m2-local-rules` 分支。
- [M2 详细计划](superpowers/plans/2026-10-10-local-rules.md)已写入，消息／日期、提取／优先级、样本／评估三个任务均已完成；不把合成样本作为真实 QQ 验收。
- Task 1 新增 `src/MessagePurify.Core/Rules/RuleModels.cs`、`MessageNormalizer.cs`、`MessageDateParser.cs`，以及 `tests/MessagePurify.Core.Tests/MessageNormalizerTests.cs`、`MessageDateParserTests.cs`。保留账号／群／消息证据、图片与引用段；日期按原发送时刻及保存时区解释，不制造钟点。支持完整年月日、月日、相对日期、明确星期、下月日期和数字钟点；日期范围、冲突、非法和未覆盖表达返回具体确认原因。
- 实现前过滤测试因缺少类型退出 1；Task 1 提交前实际执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`，退出码 0、183 项通过（原 87 项、标准化 2 项、日期 94 项）。执行 `node --test tests/project-rules.test.mjs`（10 项）、`node tests/prototype-qa.mjs`（107 项）、`git diff --check`，均退出码 0。人工核对需求、阶段和实际产物；未找到的 `maintain-context-hygiene` 技能继续以项目规定的人工核对替代。Task 1 已提交 `998eb0f`。
- Task 2 已写入 `Rules/MessageRuleExtractor.cs`、`PriorityCalculator.cs`，及 `RuleExtractionTests.cs`、`PriorityCalculatorTests.cs`（在既有 Core／测试目录）。普通明确行动按已确认课程和有效时间提取；疑问、否定、转述、限定对象、缺时间分别保留原因。延期／取消／新要求分别检查可信发布者和唯一目标，已归档／已取消新要求为待确认；引用仅提供关联证据，OCR 只提供建议，不实际改变事项或发通知。
- Task 2 两类过滤测试实现前均退出 1；实现后提取过滤初始 67 项通过、优先级初始 12 项通过。补保存时区跨 UTC 日期、条件句、排除目标、混合证据引用、格式要求及自愿表达回归后，root 实际运行 Release 核心全量，退出码 0、289 项通过（提取 85 项、优先级 13 项，含 Task 1 标准化 2 项和日期 102 项）。后果只按明确说明提升一级。
- 独立只读复核发现日期部分匹配、条件拆分后丢失作用域、取消格式要求误取消任务、自愿报名表误建任务等具体问题，已修复并补回归。日期 8 项新增回归实际先失败 6 项（退出 1），修复后过滤 102 项通过（退出 0）；核心全量 289 项包括这些修复，Task 2 已提交 `39fa505`。
- Task 3 新增 `tests/fixtures/rules/synthetic.json`（65 个独立样本、27 类、57 个 gold 候选、47 个需本人行动）以及共享 `RuleFixtureRunner.cs`、6 项 fixture 测试和 `tools/MessagePurify.RuleBaseline/` CLI。测试项目复制 fixture 到输出目录，CLI 只引用 Core 并链接同一 runner。实际执行 fixture 过滤测试通过 6 项；CLI 生成 [rule-baseline.md](verification/rule-baseline.md)：65/65 完整匹配、正式 Task 准确率 23/23、需行动召回 47/47、直接自动覆盖 27/47、待确认占比 23/57、高影响错误 0。CLI 无效参数实际退出码 2；合成结果不替代真实独立 QQ 样本验收。
- CLI 错误 gold 出口通过独立复制运行产物实测：执行 `.tools/dotnet/dotnet.exe .tools/m2-cli-negative/MessagePurify.RuleBaseline.dll --output .tools/m2-cli-negative/failure-report.md`，退出码 1；复制样本只修改一个期待日期，结果为 64/65 匹配、失败 1、高影响错误 1。正式 JSON 已核对未改。共享评估器的全部 Pending、错误日期／目标、零分母及失败出口经独立只读复核无未解决问题。
- 配置 fixture 时首次过滤构建因 SDK 默认包含与显式 `Compile` 重复而退出 1；定位后移除重复项，过滤 6 项及 Release 全量 295 项通过。Core.Tests 和 CLI 的 `restore --locked-mode` 均退出 0；文档阶段措辞检查曾失败，同步有效阶段及断言后 10 项通过。验证范围始终是 C# 核心、合成样本、文档和 demo，不含真实 Windows／QQ／OCR 集成。
- M2 交付前实际执行 `.tools/dotnet/dotnet.exe restore tests/MessagePurify.Core.Tests --locked-mode`、`restore tools/MessagePurify.RuleBaseline --locked-mode`、`test tests/MessagePurify.Core.Tests --no-restore --configuration Release`、`run --project tools/MessagePurify.RuleBaseline --configuration Release --no-restore -- --output docs/verification/rule-baseline.md`，全部退出 0，核心 295 项、CLI 65 条样本通过。执行 `node --test tests/project-rules.test.mjs`（10 项）、`node tests/prototype-qa.mjs`（107 项）、`node --check` 三个现有 JS 文件及 `git diff --check`，均退出 0。人工里程碑核对：M1／M2 已勾选，M3～M9 未勾选，需求、交接、实际源码、基线和验证范围一致；不删除文件来缩短上下文。Task 3 提交说明为 `test: add synthetic rule corpus and baseline report`，标识以 Git 日志为准。

### 上轮 demo 删除日期二次确认

- 用户要求删除截图中的“我已查看原图并确认所填写的日期和时刻”勾选框。demo 改为填写日期后通过现有确认／保存按钮一次提交，不再单独勾选；填入图片建议仍只更新草稿，提交前不写正式时间。
- 已删除勾选框、修改日期后的勾选重置和提交拦截；图片任务通过现有按钮提交后仍记录已确认时间及手动保护。填入建议、修改后保存、刷新、仅日期、图片转通知和自定义课程草稿保留均有浏览器检查；M1 核心和 M2 接续顺序不变。
- 实际执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、107 项检查通过，无浏览器运行异常；执行 `node --test tests/project-rules.test.mjs`（9 项）、`.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release`（87 项）、`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`git diff --check`，均退出码 0。需求、体验说明及测试说明同步更新；对应提交说明为 `fix: remove redundant image date confirmation`，标识以 Git 日志为准。
- 未找到 `maintain-context-hygiene` 技能，已按项目规则人工核对需求、交接、实际代码及验证范围，原图／OCR 建议仍不自动确立正式时间。
- 更新并查看 `prototype/previews/courses.png`，确认日期输入下直接显示优先级，不再出现二次勾选；课程截图在滚动完成后捕获。本轮保留这一处相关预览更新。

### 上轮正式开发启动

- 用户明确授权准备完成后开始编写代码，沿用既有 M1 计划，不重新选择 NapCat、本地运行或课程名单获取方式。
- QQ 已在电脑登录来自用户说明，尚未检查 NapCat 对应 QQ 环境；真实集成仍按 M3 执行，不把普通 QQ 登录写成已接入。
- 当前在 `codex/m1-core-foundation` 分支完成 M1，Task 1 时间、Task 2 提醒和 Task 3 状态操作均已写入并验证。各任务验证后分别提交；真实应用界面、数据库、消息识别和系统集成仍在后续阶段。
- SDK 安装在本项目 `.tools/dotnet`（Git 忽略），没有修改全局 PATH 或已有 dotnet 安装。通过[微软官方发布元数据](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json)获取 Windows x64 ZIP，实际执行 `Invoke-WebRequest`、`Get-FileHash -Algorithm SHA512`、`Expand-Archive`，校验与元数据一致。执行 `.tools/dotnet/dotnet.exe --version`、`--list-sdks`、`--info` 均退出码 0：SDK 10.0.401，RID win-x64，NETCore／ASP.NETCore／WindowsDesktop 运行时 10.0.12；`global.json` 锁定 10.0.401，rollForward 为 latestPatch。
- 已执行 SDK 的 `new globaljson`、`new classlib -f net10.0 --no-restore`、`new xunit -f net10.0 --no-restore`、`add … reference …` 和 `restore tests/MessagePurify.Core.Tests --use-lock-file`，均退出码 0。保留模板稳定依赖 Microsoft.NET.Test.Sdk 17.14.1（MIT）、xunit 2.9.3 与 runner 3.1.4（Apache-2.0），版本与完整传递依赖写入锁文件；未使用覆盖率采集器，不加入 coverlet。许可证来源为对应 NuGet 官方包页，xunit v2 为仅安全更新的旧稳定系列，当前构建未出现警告。
- `TaskTime` 保留 Deadline／Start、日期、可选钟点及时区；仅日期当天仍有效、不生成正式瞬时值；精确时间到时即失效，非法时区及夏令时歧义／不存在钟点返回明确异常。先执行 `test … --no-restore --filter TaskTimeTests`，退出码 1，确认缺少 TimeKind／TaskTime 实现；写入实现后执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore`，退出码 0，10 项通过、0 失败。
- 文档检查首次因顶部需求摘要未记录真实集成边界而失败，读取失败断言后补齐摘要；重跑 `node --test tests/project-rules.test.mjs` 退出码 0、9 项通过。`node tests/prototype-qa.mjs` 退出码 0、102 项通过。`maintain-context-hygiene` 技能在本机技能目录中未找到，按项目规则人工核对当前阶段、有效需求及实际产物。
- Task 1 交付前执行 `.tools/dotnet/dotnet.exe restore tests/MessagePurify.Core.Tests --locked-mode`、`test tests/MessagePurify.Core.Tests --no-restore`、`node --test tests/project-rules.test.mjs`、`node tests/prototype-qa.mjs`、`node --check tests/project-rules.test.mjs`、`git diff --check`，全部退出码 0，分别 10 项核心、9 项文档和 102 项浏览器检查通过。提交说明 `feat: add core time model and test project`，标识以 Git 日志为准。
- Task 2 新增 `src/MessagePurify.Core/Reminders/ReminderPlanner.cs`、`tests/MessagePurify.Core.Tests/ReminderPlannerTests.cs`。默认精确节点提前 24／2 小时，日期节点前一天 12:00；支持自定义小时、日历偏移及关闭。小时规则遇日期精度回退默认并返回 NeedsReview，日历偏移保留，关闭不重启。仅产出节点及待确认决定，不进行系统投递或创建持久化待确认项。
- 先执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --filter ReminderPlannerTests`，退出码 1，确认缺少提醒类型／实现；实现后同命令退出码 0、33 项通过，全部核心测试退出码 0、43 项通过。精确基准的“提前”节点须严格更早，校正计划示例的 `>` 为 `>=`；包括夏令时经过 24 小时、小数小时及日历节点的测试，不改变正式时间。
- Task 2 交付前再次执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore`、`node --test tests/project-rules.test.mjs`、`node tests/prototype-qa.mjs`、`git diff --check`，全部退出码 0，43 项核心、9 项文档、102 项浏览器检查通过。提交说明 `feat: add default and custom reminder planning`，标识以 Git 日志为准。
- Task 3 新增 `src/MessagePurify.Core/State/TaskTransitions.cs`、`tests/MessagePurify.Core.Tests/TaskTransitionsTests.cs`。完成／取消／归档／恢复只对实际变化递增事项版本，时间修改独立递增时间版本；受保护正式时间只输出建议，接受后才更改并保护。取消不被旧完成／恢复／归档覆盖；已完成可信新行动可恢复，已归档及取消不自动恢复。完成撤销 1 小时、忽略资格 24 小时，到期或被后续操作替代时不可撤销；完成后延期再撤销保留最新时间。
- 先执行 `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --filter TaskTransitionsTests`，退出码 1，确认缺少状态类型／实现；实现后同命令退出码 0、44 项通过；全部核心测试退出码 0、87 项通过、0 失败。`OperationSequence` 及 superseded 的实际检查、证据／处理记录保存、忽略的持久化恢复和数据库并发在 M4 落实，核心只消费当前快照和明确输入，不恢复整个旧快照。
- M1 交付前实际执行 `.tools/dotnet/dotnet.exe --version`、`restore tests/MessagePurify.Core.Tests --locked-mode`、`test tests/MessagePurify.Core.Tests --no-restore --configuration Release`，全部退出码 0：SDK 10.0.401、Release 编译成功、87 项测试通过，0 失败／跳过。执行 `node --test tests/project-rules.test.mjs`（9 项）、`node tests/prototype-qa.mjs`（102 项）、`node --check tests/project-rules.test.mjs`、`node --check tests/prototype-qa.mjs`、`node --check prototype/app.js`、`git diff --check`，均退出码 0，无浏览器运行异常；范围为核心、文档及 demo。
- 独立只读代码复核未发现需要修正的问题；人工完成里程碑上下文核对：M1 三任务已勾选且源码／测试／锁文件存在，需求、设计及本文的阶段与实际验证一致，M2～M9 未勾选。`maintain-context-hygiene` 技能仍未找到，沿用人工核对，不删除项目文件。前两次提交为 `f1bdce3`、`0edf77a`；Task 3 提交说明 `feat: add lifecycle and protected time decisions`，标识以 Git 日志为准。

### 上轮 demo 认可与下一步说明

- 用户表示 demo 基本满足要求，并询问下一步。该反馈是对演示交互的基本认可，不代表真实应用验收；有效产品需求和既有技术路线继续保留。
- 下一步沿用已写入的 M1：准备开发环境，完成时间模型、提醒计算、任务状态及撤销规则的真实 C# 核心和测试；随后按 M2、M3 进行本地消息识别与真实 Windows／QQ／OCR／通知的小规模验证，再推进存储、正式界面、后台运行和安装交付。
- 本轮只更新阶段记录和相关文档测试，没有安装 SDK、创建 C# 文件或开始真实集成。技术细节由开发者处理，实际连接时再请用户完成 QQ 登录或提供必要样本。
- 实际执行 `node --test tests/project-rules.test.mjs`、`node tests/prototype-qa.mjs`，均退出码 0，分别 8 项和 102 项通过；`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。已人工核对演示反馈、有效需求、下一步计划及实际文件一致；`maintain-context-hygiene` 技能此前搜索未找到，沿用项目规定的人工核对。对应提交说明为 `docs: record demo feedback and next development step`，标识以 Git 日志为准。

### 上轮课程／分类选项同步

- 用户要求列表课程筛选与详情的“课程或分类”同步下拉内容并支持自定义。右侧原自由文本输入已替换为下拉框，筛选、待确认、编辑和手动新增任务共用已有课程／分类及自定义名称。
- 各课程下拉框提供“＋ 自定义课程或分类…”入口。右侧新增后自动选中名称，提交事项后才更新所属课程；左侧新增只同步选项，保留当前筛选。取消新增恢复原选择，空白名称显示错误，重复名称复用已有选项并去除首尾空格。同步选项不重建表单，保留未提交的标题、日期、时刻及图片确认。
- 自定义名称保存在原有 `message-purify-demo-v2` 浏览器本地存储的 `customCourses` 字段，兼容未含该字段的旧保存数据；刷新及“重置示例”后保留。本轮支持新增名称，未实现重命名／删除管理或真实课程／群关联修复。
- 实际执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、102 项检查通过、无 JavaScript 异常；新增 16 项课程交互检查，覆盖两处入口、同步、取消、重复／空白名称、草稿保留、刷新、重置、待确认／手动任务及筛选。实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、8 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`git diff --check` 均退出码 0。
- 接续收尾时将课程预览纳入交付文件存在检查，实际重跑 `node tests/prototype-qa.mjs`、`node --test tests/project-rules.test.mjs`，均退出码 0，分别 102 项和 8 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。
- 六张预览已生成，新增并查看 `prototype/previews/courses.png`，确认课程下拉框与固定确认按钮布局正常。需求、交接、体验说明、测试说明及实际文件已人工核对一致；本机技能目录仍未找到 `maintain-context-hygiene`，按项目规则完成核对。对应提交说明为 `feat: sync course selectors and support custom courses`，标识以 Git 日志为准。下一步继续收集 demo 体验反馈，真实应用代码尚未开始。

### 上轮 demo 体验修复

- 用户反馈右侧详情被裁切、滚轮不生效，以及无法找到待确认的编辑确认入口。已复现并定位：`.main` 的 Grid 子项默认最小高度被长详情撑大，上层 `.app` 隐藏溢出，详情本身无滚动空间；确认按钮在裁切区域的表单底部。
- 1100×800 复现时详情高 1169px、底部坐标约 1254px、滚动高度等于可视高度。新增高度回归检查首次执行 `node tests/prototype-qa.mjs` 退出码 1，证明原问题；只加入 `.main { min-height:0 }` 后，详情可视高度为 694px、滚动高度 1169px、底部 779px，复跑退出码 0、79 项通过。
- 详情现在由独立滚动内容区与底部固定操作栏组成。待确认的按钮根据事项类型显示“确认并加入任务”或“确认并保留为通知”，文字说明确认后的去向和必填信息。按钮使用原表单提交机制，信息缺失时滚动到错误提示；图片日期的人工确认规则继续有效。
- 最终实际执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、86 项通过；新增原生鼠标滚轮上下滚动、左栏不受影响、按钮固定可见、按钮文案更新、错误可见，以及原生鼠标点击转为任务／通知的检查。执行 `node --test tests/project-rules.test.mjs`，退出码 0、8 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`git diff --check` 均退出码 0。
- 五张预览已生成；新增并查看 `prototype/previews/pending.png`（1100×800），确认右栏滚动条和底部确认按钮可见。需求、体验说明与实际文件已人工核对一致；`maintain-context-hygiene` 技能此前搜索未找到，继续依据项目规则完成核对。真实应用集成边界不变，继续等待用户体验反馈。对应提交说明为 `fix: restore detail scrolling and visible confirmation`，标识以 Git 日志为准。

### 上轮整体确认与实施计划

- 用户回复 A，确认按更新后的完整设计继续制定开发步骤；设计状态由“待整体核对”改为“整体设计已确认”，不重新选择后端机制。
- 已写入[实施总计划](superpowers/plans/2026-10-10-windows-app-implementation.md)及[第一阶段详细计划](superpowers/plans/2026-10-10-core-foundation.md)。总计划分 M1～M9，映射设计第 12 节的全部 31 个验收场景；M1 细分三个可测试并独立提交的任务。计划中的源码路径、代码示例和测试命令是实施说明，不是已经创建或运行的应用功能。
- 独立只读环境核对实际执行 `git --version`、`git status --short`、`node --version`、`Get-Command dotnet -All -ErrorAction SilentlyContinue | Select-Object Name, Source, CommandType | ConvertTo-Json -Compress`、`dotnet --list-sdks`、`dotnet --list-runtimes`，均退出码 0。Git 为 2.54.0.windows.1、Node 为 v24.16.0；默认 dotnet 在 `C:/Program Files/dotnet/dotnet.exe`，SDK 列表为空，NETCore 6/7/8 及 WindowsDesktop 6/7 运行时已安装，不能替代 .NET 10 SDK。
- 执行 PowerShell 浏览器文件及注册表只读查询，使用 `Test-Path`、`Get-Item`、`Get-ChildItem` 和注册表 `GetValue`，两份查询脚本均退出码 0；Chrome 为 154.0.8037.98，WebView2 Runtime 注册版本为 155.0.4283.45。仅检测安装，尚未运行 WPF/WebView2 应用。
- 当轮更新整体确认状态测试、计划链接检查和全部验收场景映射检查；实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、7 项通过；执行 `node tests/prototype-qa.mjs`，退出码 0、27 项通过、无 JavaScript 异常。执行 `node --check tests/project-rules.test.mjs` 与 `git diff --check`，均退出码 0。当轮测试通过，验证范围是文档和早期浏览器原型；M1 所有实施项未执行。其后用户确认 demo 先行，当前顺序以“接手先看”为准。
- 自行核对计划与完整设计：31 项验收均映射到已定义阶段，两份计划中的任务均未勾选；占位词扫描无匹配，未发现 C#/WPF 实现文件。下载副本与上次提交先作正文比对，确认没有额外改动后同步整体确认状态、环境记录及计划绝对链接；再核对与项目设计正文一致。交接、需求、设计和实际文件保持一致，按已有规则人工完成上下文核对。
- 已实际执行 `git diff --cached --check`、`git commit -m 'docs: confirm design and plan Windows implementation'`、`git log -1 --format='%h %s'` 和 `git status --porcelain`，均退出码 0；对应提交已创建，包含本轮 7 个项目文件，工作区状态输出为空。下载副本为项目设计的正文相同副本，正式版本以仓库中的设计为准；最新提交标识见 Git 日志。

### 协作规则、测试与 Git

- 每次改动完成后，都必须创建一个对应的 Git commit，以便后续追踪和回滚。
- 每次改动后，都必须编写或更新相关测试，并在交付给用户前，确保所有测试和验证全部通过。小任务同样适用，不强制测试先行。
- 现有 `AGENTS.md` 已加入“注意事项”，原有接续、文档同步、讨论和阶段规则保留；需求文档同步新增要求。
- 上轮在项目根目录执行 `git init`，建立本地 Git 仓库，并创建首次基线提交 `ff16b4b`，包含现有文档、浏览器原型及规则和测试改动。提交作者仅在本仓库配置为 `Codex <codex@localhost>`，未修改全局作者设置；后续提交见 Git 日志。本轮没有应用实现改动，也未配置远程仓库。
- 已有 `tests/project-rules.test.mjs`、`tests/prototype-qa.mjs` 和 `tests/README.md`。早期 27 项浏览器检查先扩充为 78 项，详情修复后为 86 项，本轮课程功能后为 102 项；文档检查为 8 项。默认测试不覆盖已有预览图，可用 `--screenshots` 主动更新六张预览；测试范围与命令见 `tests/README.md`。
- 上轮实际执行 `node --test tests/project-rules.test.mjs`，退出码 0，6 项通过、0 失败；执行 `node tests/prototype-qa.mjs`，最终退出码 0，27 项通过、无 JavaScript 异常。首次浏览器检查及诊断复跑均在按钮动画断言处退出码 1：诊断显示本机 `prefers-reduced-motion: reduce` 为真、按钮过渡为 `0s`，符合界面现有规则；测试随后显式模拟普通与减少动画两种偏好，不依赖 Windows 设置，未改原型代码。
- 上轮实际执行 `node --check tests/prototype-qa.mjs` 和 `node --check tests/project-rules.test.mjs`，均退出码 0。全部现有测试的命令及环境要求见 `tests/README.md`；验证范围为文档和浏览器原型，真实应用代码尚未开始。本轮更新阶段相关检查并重新验证后记录结果。
- 上轮实际执行 `git diff --cached --check`、`git commit -m 'chore: add project rules and repeatable checks'`、`git log -1 --format='%h %s'` 和 `git status --short`，均退出码 0；首次提交成功、状态输出为空。提交包含 12 个文件，没有个人凭据配置。核对需求、交接与实际产物一致；本机仍未找到 `maintain-context-hygiene` 技能，按项目规则人工完成交接核对。

### 新版 demo 的实现范围与此前验证

- 入口为 `prototype/index.html`，逻辑拆入普通脚本 `prototype/app.js`；双击 HTML 可运行，未增加构建工具或依赖。体验说明见 [demo 说明](../prototype/README.md)。
- 左侧导航、列表与右侧详情沿用早期布局；新增标题／课程／来源群搜索、课程筛选，以及任务页的已归档／已取消筛选、待确认页的最近忽略入口。桌面卡片列表不随主界面搜索而减少。
- 完成后立即移入已完成记录，即时提示和记录详情均提供“撤销”，1 小时内有效；忽略后 24 小时内可撤销。时间戳保存后刷新继续计时，过期保留记录；点击动作再次检查资格。重新收到新行动要求时，先前完成记录仍保留。
- 归档移出待办和桌面模拟卡片，保留原文并停止时间提醒展示；归档筛选内可恢复待办。新增手动任务支持类别、课程、日期、可选时刻和优先级；区分提交截止与考试／活动开始，开始已过不误记为作业逾期，仅日期当天仍有效。
- 单条提醒可选默认、提前小时、前几天某时刻及关闭；仅日期禁用小时模式。延期保留相对间隔，时刻精度丢失时临时采用默认提醒，新增关联待确认项；已关闭提醒不自动开启。上述只计算和展示节点，没有执行系统调度。
- 详情底部可模拟延期、取消和新增行动要求。手动时间冲突保留旧值及有效提醒，接受后更新；取消独立生效并保留记录；已完成新要求恢复待办，已归档新要求先待确认。示例消息及完成／变化记录可查看；没有接入真实消息识别或系统变更通知。
- 图片点击放大、OCR 默认折叠、建议日期可填入或拒绝；填入仅影响表单，勾选确认并提交后才确立正式日期。新内容带未读数量和红点，显示详情后标记已读；在桌面预览操作不会将隐藏的主界面详情误记为已读。
- 保留浅色与纯黑深色、320ms 换肤、按钮按压和卡片悬停动效；设置新增跟随系统和减少动画，以及声音、自动启动、关闭行为的演示偏好。安静时段入口标注“暂未启用”。首次提供三步示例引导；“预览提醒”展示内容及声音意图，点击“打开事项”定位对应详情。没有播放系统声音或修改 Windows 设置。
- 桌面双卡片可拖动／缩放并保存浏览器模拟布局，待确认清空后隐藏其卡片，任务清空后显示“暂无待办”；保留逾期原时间。这里的双卡片仍为一个浏览器预览容器，不是两个独立 Windows 桌面窗口。
- 本轮实际执行 `node --check prototype/app.js`，退出码 0；中途 `node tests/prototype-qa.mjs` 依次通过 29 项、72 项。最终执行 `node tests/prototype-qa.mjs --screenshots`，退出码 0、78 项通过、无 JavaScript 异常；包括刷新、撤销边界、冲突处理、精度变化、纯黑背景和 1100px 布局检查。检查撤销边界时使用显式时间戳及过期动作，不实际等待 1 小时或 24 小时。
- 已重新生成并查看四张 1440×1000 预览图，主界面和桌面卡片的浅色及纯黑深色布局可用。实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、8 项通过；链接、交付脚本、浏览器存储说明与真实集成阶段一致。
- 交付前再次实际执行 `node --test tests/project-rules.test.mjs` 和 `node tests/prototype-qa.mjs`，均退出码 0，分别 8 项及 78 项通过；`node --check prototype/app.js`、`node --check tests/prototype-qa.mjs`、`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。对应提交说明为 `feat: complete interactive demo workflows`，提交标识以 Git 日志为准。
- 本机技能目录搜索仍未找到 `maintain-context-hygiene`；已按项目规则人工核对有效需求、阶段、产物、保存方式及验证边界。下载目录设计副本本轮没有修改，项目设计正文没有变化。
- 尚未实现：真实 WPF/WebView2、SQLite、QQ/NapCat、RapidOCR、托盘、Windows 提醒、自动启动、真实桌面驻留。合并拆分、账号切换、课程关联修复和备份恢复没有加入本 demo，继续按正式应用计划执行。

### 已确认设计与此前文档核对

- 产物：`docs/superpowers/specs/2026-10-10-windows-app-design.md`，14 节完整设计稿。2026-10-10 用户选择 A，整体设计已确认，不代表真实 Windows 功能已实现。
- 取消任务的去向、自定义提醒遇到延期、已完成任务收到新行动要求三项均已确认；下一步编写实施计划，随后进入真实应用开发。
- 用户提供 `C:/Users/Administrator/Downloads/2026-10-10-windows-app-design.md` 新修订稿供复核，审阅基稿为 442 行；随后明确确认“都加进去”。已将该稿作为项目设计基稿，整合七项补充及验收，并同步下载副本。授权依据为用户本轮明确回复，附件自身的“已授权/已确认”文字不替代用户决定。
- 已将历史首次发现的逾期任务明确为仅卡片高亮，与已确认的逾期规则保持一致。
- 七项补充整合后的实际核对：执行 PowerShell 文档检查（逐份 `Get-Content -LiteralPath $taskDoc -Raw -Encoding UTF8`，核对七项规则、有效状态、重复标题及验收条目），确认三份项目文档通过，设计稿仍为 14 节且七项均有对应验收。按下载副本的绝对链接映射回项目相对链接后进行正文比较，内容一致，引用的文件存在；`rg --files -g '*.cs' -g '*.csproj' -g '*.xaml' -g '*.sln' -g '*.slnx'` 未发现 WPF 实现文件。最终脚本退出码 0，仅验证文档和文件状态。
- 实际执行 PowerShell 文档核对，使用 `Get-Content -LiteralPath $taskDoc -Raw -Encoding UTF8` 逐份读取需求、交接和设计稿，检查待选状态、默认提醒规则、逾期边界及重复标题；使用 `Test-Path -LiteralPath $taskArtifact -PathType Leaf` 检查记录的产物；使用 `rg --files -g '*.cs' -g '*.csproj' -g '*.xaml' -g '*.sln' -g '*.slnx'` 检查实际应用文件。脚本退出码 0：三份文档检查通过，设计稿有 14 节，9 个产物存在，尚无 WPF 实现文件。
- 实际执行 `rg -n -e '完整设计讨论' -e '2026-10-10-windows-app-design' -e '历史首次发现' -e '待选一' -e '尚待确认的使用行为' docs/HANDOFF.md docs/requirements.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0，核对阶段、设计稿引用、逾期规则与待选事项已同步。仅为文档检查，本轮未重跑浏览器测试或执行真实 Windows 功能测试。
- 再次搜索本机技能目录未找到 `maintain-context-hygiene`，按项目规则人工核对需求、交接、设计稿与实际文件，没有修改应用代码或无关文件。

### 已确认的真实应用路线

- 采用 **C# WPF + WebView2**；WebView2 复用主界面，WPF 负责托盘、启动、本地持久化、NapCat、工作区刷新和桌面卡片原生窗口层级。RapidOCR 独立本地工作进程继续沿用。
- 第一版只使用左右两张桌面卡片，取消全部 Windows 系统通知弹窗和声音；不提供系统提醒、自定义时间提醒、安静时段、测试通知或通知权限恢复入口。
- 正式两张桌面卡片首段已实现并验证：自动刷新 AccountWorkspace 当前账号快照，显示标题、来源、优先级和有效时间，提供查看/完成；待确认处理完隐藏，空待办显示“暂无待办”。仅附着 Explorer 桌面、不设 Topmost，首次按屏幕左右摆放，拖动/缩放后以 DIP 持久化布局。Native 合成 13 项及独立父窗/style 检查通过；多屏、Explorer 重启和睡眠恢复尚未验收，完整变化标记仍待实施。
- 主界面继续展示连接状态、自动重连、原文、图片和变更记录；断线超过 5 分钟不弹系统通知。普通群通知只保存在 App 通知列表，不增加第三张卡片。
- 归档/取消/完成移出待办与卡片并保留证据；取消记录仍为“已取消”，不记用户完成。可靠新行动恢复已完成任务，保留此前完成记录；手动字段保护和关联待确认继续有效。
- 完成 1 小时/忽略 24 小时撤销、已读版本、搜索筛选、课程/分类同步、纯黑主题及减少动画规则继续有效。
- 自动启动默认开启及关闭行为设置继续作为后续产品范围；首次引导预览桌面卡片，点击卡片详情打开主界面对应事项。
- 手动新增、完整编辑、通知转任务、图片队列、合并拆分、M6 修复、完整账号切换和备份恢复按原计划继续；开发者自行处理普通实现细节，不重问 NapCat、本地运行或课程名单来源。
- 以下此前实施和验证记录按当时范围保存；涉及 Windows 通知的“下一步”只属于旧方案历史，当前实施以本节及第 6 节为准。

## 2. 项目目标

制作一个 Windows 桌面软件，从学校 QQ 群消息中整理通知与作业，提取待办、判断优先级并提醒用户。消息处理尽量全部在本机完成。

参考项目是 [偷闲（Touxian）](https://github.com/sssssjw11/touxian)，注意仓库所有者为 `sssssjw11`（五个 s）。借鉴其架构，不直接照搬全部筛选或过期处理规则。

## 3. 已确认需求快照

完整定义见 `docs/requirements.md`。以下为接续时不能遗漏的主要结论。

### 消息获取

- **已决定使用 NapCat**，用户计划用于自己的 QQ 大号。该接入选择已经讨论确认，不需要重新开启选型或账号风险讨论。
- 只读取当前 QQ 中置顶的群聊消息，实时、历史与断线补漏统一限制；连接/重连及运行中重新核对置顶状态，无法取得可靠名单时暂停，不回退到全群。取消置顶后停止新消息读取，已有事项与证据保留；排除私人对话和经用户确认的机器人账号。
- QQ 接入只读，不向 QQ 群或私聊发送消息；第一版通过两张桌面卡片提示待办和待确认，不向学校群发测试消息或 Windows 系统通知。
- 软件按本地规则提出疑似机器人账号，用户确认后才排除；同一账号后续自动忽略，支持撤销。确认前不能因重复内容就排除老师或同学。
- 首次启动尝试回溯最近 7 天；后续断线重连时尝试补漏。历史读取能力取决于 QQ 返回结果，失败时展示对应群和原因。
- 主界面显示连接状态与原因并自动重连；取消断线超过 5 分钟的系统通知。
- 按群及消息 ID 去重，避免历史补漏导致重复任务或重复触发相同提醒。
- 登录 Windows 后自动启动本软件，在系统托盘后台运行；自动启动默认开启，设置中提供开关。该启动决定针对本软件；目前尚未配置启动项，也未实现自动启动开关。

### 本地识别

- 第一版**不接云端 AI**：本地规则负责通知、作业、相关性、时间和优先级判断。
- 学校场景，只保留通知与作业，不设日常闲聊板块。
- 需要完成的作业、考试、提交等可成为任务；自愿活动、竞赛等作为通知，用户自行加入任务后才按任务处理。
- 支持手动新增任务，用于补录口头布置或漏识别的事项；来源标注为“手动添加”，使用同样的优先级、提醒和完成规则。
- 自动提出课程和群关联候选，由用户确认、修改或排除；复用确认过的关联。**不要回退为首次手填课程名单。**
- 面向全体或明确与本人课程相关的事项自动处理，相关性不确定的进入待确认。
- 综合行动要求、日期上下文、对象限定及群与课程关系，不只依靠一个关键词；不能机械过滤短消息。
- 相对时间以消息原始发送时间为依据。需要行动但缺少或无法确定截止日期时进入待确认，用户补齐并确认后进入正式任务卡片；仅有日期时保留日期精度，不造时刻。

### 图片

- 已同意以 **RapidOCR** 作为本地图片文字识别方案；本机已安装隔离运行环境并完成 25 项回归、用户群列表截图识别和真实学校通知截图识别。通知正文文字、位置和置信度通过；日期范围与“交齐”截止的角色仍需人工确认，正式任务生成和提醒投递未验收。
- 文字消息完整时，原图及 OCR 文字作为辅助信息加入事项详情。
- 文字消息缺少完整时间或其他关键信息时进入待确认；用户查看原图/OCR 后手动填写。
- **图片中的日期经用户确认后才能用于提醒**；OCR 不能自动覆盖或确立正式截止时间。
- 识别失败仍保留原图。目前没有决定支持 PDF 或其他附件内容解析。

### 任务生命周期

- 明确属于同一事项的内容、截止时间和补充说明合并，保留各条消息证据。
- 催交更新原任务；明确的延期、取消、要求修改按字段保护规则更新任务并记录变化，仍在待办中的任务更新卡片变化标记。新时间与手动正式字段冲突时保留旧值及当前提醒、显示待确认差异；取消不被手动时间保护阻挡。
- 能明确关联的延期、取消和提交要求变更更新任务、主界面变更记录和仍在待办的卡片，不发送系统通知。
- 老师明确取消且能关联到原任务时，停止该任务后续提醒，移出待办和桌面卡片，保留“已取消”记录及原始消息；不记为用户已完成。取消状态、可靠关联自动应用及关联建议接受的本地存储已实现，正式卡片只投影 active 任务，取消事项移出；取消变更系统通知已取消。
- 无法明确对应某个任务的变更消息进入待确认，不猜测后直接覆盖。
- **7 天是消息回溯/分析窗口，不是任务寿命。**长期任务跨周保留，原始证据须能继续查看。
- **逾期不自动完成或归档**，继续保持待处理，并通过卡片高亮提示，不重复弹通知。
- 完成由用户手动勾选；完成后进入已完成记录并停止提醒，保留原始证据。用户也可手动归档。
- 任务详情有归档及恢复入口，任务页可筛选已归档记录；归档移出待办和卡片，不记为完成，证据保留。旧证据重读不恢复；归档后新行动要求先作为关联建议待确认。本地归档/恢复及建议持久化已实现，正式主界面及卡片当前状态刷新已接入，Windows 通知清理已取消。
- 已完成任务收到能明确关联的新行动要求时，自动恢复为待办，标出新要求并继续按规则提醒，保留此前完成记录及新旧消息证据；普通补充说明不恢复，关联或有效截止信息不确定时仍进入待确认。可靠新行动自动恢复及历史保留已本地验证，正式卡片变化显示尚未完成，系统提醒调度已取消。

### 优先级与桌面提示

- 本地计算 P0～P3，依据截止时间和明确的事项后果；用户可手动调整。依次匹配：已逾期/今天 P0、未来 3 天内 P1、未来 7 天内 P2、更晚 P3。明确后果可提高一级，否定语义不误提升。
- 实时、手动、历史、补漏及修复发现的任务统一刷新卡片，按有效截止/开始排序，临期和逾期高亮；开始已过单独标记，不误记作业逾期或自动完成，日期当天仍有效。
- 第一版取消全部 Windows 系统通知弹窗和声音；24/2 小时节点、日期前日 12:00、自定义时间提醒、新发现/变更、恢复/历史/迟到批次汇总、通知点击路由、权限恢复和安静时段入口均移出当前范围。
- 重启/睡眠恢复/重连读取当前状态刷新卡片，不补发旧通知；待确认常驻至处理完。原 ReminderPlanner、SQLite plans/ledger 和 probe 验证保留为历史产物，不作为未来 Windows 接入任务。
- 延期按字段保护接受后更新卡片排序和高亮；取消、完成、归档移出卡片，原文及操作历史保留。

### 界面

- 可拖动、调整大小并固定在桌面的组件，统一展示左右排列的两张卡片；用户已选择 B（左右排列）。
- **桌面卡片仅在桌面显示**：切回桌面时可见，不覆盖其他软件窗口；用户已选择 A（仅在桌面显示）。待确认事项仍保留在卡片中，直到用户处理，窗口遮挡不改变其处理状态。
- **提醒事项卡片**：已确认任务按优先级与截止时间排序，显示标题、课程/来源群、优先级、截止时间及“详情”“完成”入口。临期和逾期高亮；无任务时显示“暂无待办”。
- **待确认卡片**：有未处理事项时一直显示，不自动淡出。显示事项标题、来源和待补信息，可查看原文及图片；补齐确认后转入正式任务/通知，无关或误报可忽略。处理一项减少一项，全部处理完后隐藏整张卡片。
- **普通通知仅在软件主界面的通知列表中展示**，不增加第三张桌面通知卡片，也不混入提醒事项卡片。
- **主界面采用左侧导航**：包含“任务、待确认、通知、已完成、设置”，右侧显示所选页面的内容；用户已选择 A（左侧导航）。
- **事项详情采用主界面右侧详情栏**：点击事项后保留事项列表，在右侧查看原文、图片，并修改截止时间等信息，方便连续处理；用户已选择 A（右侧详情栏）。
- **课程／分类选项同步和自定义**：课程筛选与详情／新增表单共用下拉选项，可从各下拉框新增名称，保存后立即同步并在刷新后保留；同步不丢失未提交表单。筛选选择与事项所属课程独立，编辑课程不自动切换筛选。本轮已在示例数据 demo 实现并验证，真实应用实现仍按正式计划执行。
- **主题切换**：用户已要求支持浅色模式与深色模式自由切换，主界面和桌面卡片统一主题，切换时有柔和的过渡动画。
- **深色主题视觉**：用户进一步明确深色模式使用纯黑背景；面板允许使用接近黑色的层次和边框来保持可读性。

## 4. 已替换的方案

| 旧方案或容易误解的做法 | 当前有效决定 |
| --- | --- |
| 本地规则后由云端 AI 处理不确定消息 | 第一版全部本地处理，不接云端 AI |
| 首次手填课程名单 | 自动提出课程及群关联候选，再由用户确认 |
| 待确认事项每天弹一次汇总通知 | 改为桌面常驻待确认卡片，不设每日汇总通知 |
| 为待确认选择每日提醒时刻 | 该问题已失效，不继续要求用户选择时刻 |
| 普通通知放桌面通知区或增加第三张卡片 | 仅放软件主界面的通知列表 |
| 只保留一周任务，或任务过期后自动归档 | 长期任务跨周保留，过期仍跟踪 |
| OCR 识别出日期后直接用于提醒 | 图片日期必须经用户确认 |
| 时间节点、单条自定义、新发现/变更、恢复/历史/迟到批次、断线等 Windows 通知方案 | 第一版只用两张桌面卡片，取消全部 Windows 系统通知弹窗和声音；保留当前状态、排序及临期/逾期高亮 |
| 逾期后每天中午弹一次通知的建议 | 用户选择仅保留卡片高亮，不重复弹通知 |
| 明确延期一律覆盖手动正式时间 | 与手动字段冲突时保留旧值和当前提醒，接受差异后才替换；取消仍独立生效 |
| 自定义时间提醒和安静时段入口 | 随 Windows 通知渠道取消，旧设置/计划/台账作为历史数据保留 |
| 课程筛选根据事项生成选项，右侧课程用自由文本输入 | 筛选与详情／新增表单共用下拉选项，支持新增自定义名称并立即同步、刷新保留 |

## 5. 调研结论及边界

以下是此前读过官方资料/源码的结论，不代表本项目已经运行验证。

- 偷闲默认基于本地规则、候选聚类、日期解析与优先级计算；它所称的本地 Jev 基线不是本地大语言模型。云端增强是可选项，本项目第一版不采用。
- 已读的偷闲桌面筛选源码会过滤少于 18 个字符的消息，可能漏掉学校群的短作业通知，不能照搬。
- 已读的偷闲桌面优先级逻辑会将逾期事项归档，本项目明确采用继续跟踪的规则。
- RapidOCR 支持中文、Windows、CPU 和本地模型推理，可返回文字、位置与置信度；后续已完成本机群列表及学校通知截图 OCR，日期角色仍需人工确认。离线部署时需提前准备模型等所需文件。
- NapCat 提供 `get_group_msg_history`，参数包含群号、起始消息位置和数量。它使分批补回历史成为可尝试的方案，不能据此保证每个群都有完整 7 天记录。

参考链接：

- [偷闲桌面说明](https://github.com/sssssjw11/touxian/blob/main/desktop/attention-desk/README.md)
- [偷闲本地筛选、日期及优先级源码](https://github.com/sssssjw11/touxian/blob/main/desktop/.agents/skills/attention-announcement-triage/scripts/triage_announcements.py)
- [RapidOCR 输出与使用](https://rapidai.github.io/RapidOCRDocs/main/quickstart/)
- [RapidOCR 本地模型](https://rapidai.github.io/RapidOCRDocs/main/install_usage/rapidocr/how_to_use_offline_model/)
- [NapCat 群历史接口源码](https://github.com/NapNeko/NapCatQQ/blob/main/packages/napcat-onebot/action/go-cqhttp/GetGroupMsgHistory.ts)

## 6. 未决定事项与下一步

已确认方向不重新询问；以下状态均需按真实产物和验证报告更新。

1. **整体设计已确认，demo 基本满足要求，持续开发已授权**。M1/M2 已完成，M3 真实接收和长期稳定性仍在实施；真实 QQ、OCR 和桌面宿主各自验证，不用合成数据代替真实验收。
2. **已确认：明确取消任务移出待办和桌面卡片**，保留“已取消”记录和原文，不记完成；不再发取消系统通知。本地取消与旧操作保护已测试，正式卡片 active/pending 状态过滤已通过。
3. **旧自定义提醒方案已取消**。ReminderPlanner、提醒设置/计划/台账及既有验证保留为历史，不删除数据，不作为后续 Windows 自动投递工作。
4. **已确认：已完成任务的可靠新行动恢复待办**，保留完成历史及新旧证据；普通补充不恢复，不可靠关联/时间先待确认。本地状态和路由已测试，正式卡片当前状态刷新已接入；完整变化标记及真实操作验收仍待完成。
5. **正式两张桌面卡片首段已实现并验证**。自动刷新 AccountWorkspace、点击查看/完成、左右初始布局、拖动缩放与 DIP 存盘、仅附着 Explorer 桌面且不设 Topmost；Native 合成 13 项及独立父窗/style 检查通过，默认入口已发布 app-tray-fix。托盘生命周期已修复，关闭隐藏、单实例唤回及菜单退出已实际通过；最新检查正式应用未运行，未自动重开。下一步优先确定并实施定时自动切换与同步，再继续真实课程/任务及卡片操作验收、OCR 队列、M6 和自启动；多屏、Explorer 重启、睡眠恢复及长期稳定性仍待完成。
6. SQLite 消息/候选/生命周期/路由和旧提醒计划/台账已回归；OCR 队列、自启动、M6 重分析、完整编辑/手动新增/通知转任务、合并拆分、缓存清理、备份迁移和正式安装仍未完成。7 天只限制未关联缓存分析窗口，事项证据长期保留。
7. 本轮真实只读复查为 52 个群、9 个置顶群、48 项待确认、0 个任务/通知，当次历史 complete、失败为空；此前两次为 40 项且重启无重复。不代表持续实时或未来完整性，真实课程/日期仍待用户核对。旧通知呈现/点击及桌面 probe 验证保留为历史，不代替正式两卡片验收。
8. 本机 Windows 11 专业版 x64 10.0.26200；项目专用 .NET SDK 10.0.401，开发使用 `.tools/dotnet/dotnet.exe`。Framework 已加载，用户确认聊天列表和窗口正常显示；实时群事件限定范围通过（观察到 1 条），长期稳定性尚未验证。
9. **安静时段、系统提醒及自定义时间提醒入口已取消**；手机版尚未确定开发，不保留“暂未启用”的入口。
10. 2026-10-10 七项复核补充中，归档、账号隔离、课程关联修复、手动字段保护继续有效；系统在途发送、迟到/修复汇总及自定义精度默认安排随渠道取消。课程修复保留人工决定和历史台账，结果刷新卡片，不删旧任务或逐条通知。
11. **已明确：固定时间自动切换与同步方向**。2026-10-12 用户要求停止 QQ 快捷方式改造，回到定时接入、读取置顶群并保存、结束后恢复普通 QQ。定时自动切换尚未实现；具体同步时刻、错过同步及 QQ 正在使用时的安排待确认。NapCat 和本地读取/存储继续复用，不重新询问已确认技术路线；不自动选择时刻或在本轮实际切换 QQ。
12. **已确认：第一版仅使用两张桌面卡片**。2026-10-12 用户选择 A，取消全部 Windows 系统通知弹窗和声音；时间节点、新发现/变更、恢复/历史/迟到批次汇总及连接异常系统通知均取消，通知点击路由、权限恢复、自定义时间提醒和安静时段入口移出当前范围。主界面保留原文、变更记录和连接状态，普通群通知保留在 App“通知”列表；旧 ReminderPlanner、SQLite plans/ledger 和 probe 验证作为历史产物保留。正式桌面卡片首段已实现并验证，不等于整体端到端或长期稳定性完成；取消任务仍为已取消，不记完成。

## 7. 接续与同步方式

- 需求变化：更新 `docs/requirements.md` 的有效条款，同时更新本文快照、替换记录和变更记录。
- 实现进展：更新本文“接手先看”中的阶段与产物，记录实际完成内容及尚未完成的部分。
- 验证：写实际执行命令、结果和验证范围；文档检查不能写成 OCR、QQ 连接或提醒功能通过测试。
- 改动交付：每次编写或更新相关测试，全部测试和验证通过后创建对应的 Git commit；小任务不免除这些要求。
- 提出但未确认的新方案只记入“未决定事项”，确认后再更新有效需求。
- 交接前读取文件核对一致性；不能让新结论只留在聊天中。
- 用户希望每次只选择一个问题、选项同时写在正文、等待明确答复；不因等待时间过去就默认选项或进入下一题。

## 8. 变更记录

以下记录保留当时的决定和实际命令；2026-10-12 已取消的 Windows 通知条款不再作为当前有效规则。

以下日期是记录整理日期，不推断此前各条聊天的实际发生日期。后续新变更按实际日期追加。

| 记录日期 | 变更 | 依据与状态 |
| --- | --- | --- |
| 2026-10-12 | 用户停止 QQ 快捷方式方向，回到固定时间自动同步 | 用户看到火绒提醒并怀疑其删除快捷方式；实际 QQ.lnk 缺失，重建及原备份复制恢复未成功。停止此方向，更新有效需求为到点接入、同步落盘、结束恢复普通 QQ；具体时间和使用冲突规则待确认，调度尚未实现。仅同步文档和回归，不实际切换 QQ 或调整火绒 |
| 2026-10-12 | 简化 QQ 重开入口，复用原桌面 QQ 图标 | 原快捷方式先备份，再指向隐藏的 Framework 入口，保留 QQ 名称和图标；已运行时打开已有窗口，不退出 QQ。新增两项快捷方式/激活回归，Node 八文件 65/65；真实快捷方式启动 ExitCode=0、3001 监听仍归原 QQ PID。未重测完全退出后的冷启动，不等于 Windows 自启动已完成 |
| 2026-10-12 | 修复用户找不到托盘图标的问题并切换默认入口 | 启动回调等待 ListenAsync，避免提前释放 NotifyIcon；实际旧版图标检查红、修复版绿。Windows 141/141、Node 63/63、Native 13 项、桌面父窗/style、隐藏/唤回/单实例及菜单退出（ExitCode=0）通过。默认 app-tray-fix，新版已打开、QQ 保持运行；真实只读复查 9 个置顶群/48 项待确认，未确认课程或日期。OCR 仅调查，M3/M8 与恢复/多屏不标完成 |
| 2026-10-12 | 完成正式桌面卡片首段、取消提醒设置入口并发布默认桌面版 | 当前账号任务/待确认、查看/完成、主题、DIP 布局和空待确认隐藏已接入；Windows 141/141、Infrastructure 375/375、Node 63/63、Native 合成 13 项、Python 父窗/style 和入口检查退出 0。单次审查修复时钟 signature 的 past 漏项，DPI 重载不自动覆盖坏布局；最终 publish 无警告/错误。默认 app-desktop，旧 app 窗口/QQ 保持运行；真实 QQ 数量沿用上段，未确认课程/日期，多屏/睡眠/Explorer/长期稳定性仍未验收，M3/M8 不标总体完成，Git 提交由 root 统一执行 |
| 2026-10-12 | 用户选择 A，确认第一版仅用两张桌面卡片 | 取消全部 Windows 系统通知弹窗和声音及相关设置/验收，保留普通 App 通知、原消息与旧计划/台账；同步有效正文、设计和总计划，正式两卡片实施中，验证结果待实际运行后记录 |
| 2026-10-12 | 用户提出仅用两张桌面卡片提醒，询问意见（随后已确认 A） | 声音功能继续取消；是否取消全部 Windows 弹窗待确认，建议只记录于第 6 节第 12 项。当前产品代码及有效需求保持原状态，依赖该选择的 Windows 接入等待明确答复；文档相关检查 25/25、diff 检查通过，本轮以对应 Git commit 保存 |
| 2026-10-12 | 完成持久化提醒第一段及失败汇总成员变更修复 | 新增账号提醒库 schema 1、正式详情设置/计划、节点及投递台账、恢复和串行提交协调。最终 Infrastructure 375/375，服务 46/46、工作区提醒 24/24；本段 Windows 96/96、Core 301/301、OCR 25/25、demo 108、Node 七文件 59/59 通过；最终重新发布与真实合成 WebView2 八项通过。P1 四项先红后绿，窄复现两路有效 B 均仅提交一次。默认入口改为 app-reminders，旧产物/窗口与 QQ 保留；实际 Windows 提醒尚未开启，详细命令及边界见第 1 节 |
| 2026-10-12 | 完成正式应用首段并接入真实 QQ 只读消息宿主 | AccountWorkspace、NapCatMessageHost、WPF/WebView2 页面及双击入口已写入；Infrastructure 305/305、Core 301/301、Windows 77/77、OCR 25/25、demo 108、Node 七文件 53/53、合成真实 WebView2 七项通过。用户退出 QQ 后重开 Framework，真实读取 9 个置顶群、40 项待确认，跨进程重启无重复，正式窗口已打开；P0 饱和后的后果加级修复有回归。真实课程确认后的任务验收、持续实时及提醒/OCR/桌面等仍未完成，详见第 1 节 |
| 2026-10-12 | 用户再次继续，完成 M4 第四段可靠关联变更与保守消息顺序 | 新入口同消息事务内提取/更新/回执，持久化目标及当前群来源授权；迟到旧/同秒/缺来源时间转待确认。新增关联 30 项，候选路由 36、生命周期 62；专项 128/128、Infrastructure 251/251、Core 296/296、Windows 57/57、OCR 25/25 与 pip check、Node 五文件 32/32、demo 108 项实际通过。通知最终版本及旧催交接受回退期限问题均先红测后修正；真实宿主/UI/提醒投递与 M6 仍待完成，完整命令见第 1 节 |
| 2026-10-12 | 用户恢复工作，完成 M4 第三段消息候选首轮路由 | 新增账号路由库及 32 项真实 SQLite 检查，生命周期增至 62；专项 94/94、Infrastructure 217/217、Core 296/296、Windows 57/57、OCR 25/25 与 pip check、Node 五文件 31/31、demo 108 项实际通过。课程确认撤销/改变使旧 Task/Notice 转待确认；当时自动关联变更、M6 重分析协调和真实宿主未完成（自动关联变更后续在第四段完成）；完整产物与实际验证命令见第 1 节 |
| 2026-10-12 | 用户要求保存当前工作，明天继续 | 已核对实现基线 `80274aa` 及干净工作区；补充暂停与恢复顺序、下一步自动路由。仅维护交接及相关文档回归，产品需求不变；当时保存并等待恢复，本次已继续 |
| 2026-10-12 | M4 第二段完成本地正式事项生命周期（11 日开始，12 日凌晨提交） | 新增 AccountTaskStore、正式事项/待确认模型和 61 项真实 SQLite 检查；Infrastructure 184/184、Core 296/296、Windows 57/57、OCR 25/25 与 pip check、Node 五文件 30/30、demo 108 项均通过。完成/忽略撤销、关联建议、证据、版本及操作幂等已落盘；候选自动路由、正式界面、提醒调度和真实 QQ 宿主联调尚未完成。详细产物与命令见第 1 节 |
| 2026-10-11 | M4 第一段写入账号独立 SQLite 消息收件箱与本地规则处理器 | 新增 `AccountMessageStore`、`LocalMessageProcessor` 及真实临时 SQLite 回归；Infrastructure 123/123（存储 23、处理器 22），Core 296/296、Windows 57/57、OCR 25/25、Node 五文件 29/29、demo 108 项和 `git diff --check` 均通过。原消息、来源、规则候选和处理版本可在重启后保留；正式事项、提醒调度、OCR 队列与真实 QQ 接入仍未完成 |
| 2026-10-11 | 用户新增只读取 QQ 置顶群的规则，替代所有群聊范围；写入统一过滤及 Framework 读取桥 | 实时、历史、重连同一范围；未知置顶状态停止。Infrastructure 78/78、桥 4/4；真实 9 个置顶群两轮 18/18 到达日期边界、去重 341、重连 1/1、退出码 0；全部自动回归通过。正式应用、真实动态置顶和长期实时稳定性仍未完成，详见第 1 节 |
| 2026-10-09 | 汇总 NapCat、所有群聊、本地规则、课程/机器人确认、图片辅助、任务生命周期、优先级和补漏方案 | 根据既有对话和需求文档整理；需求已确认，软件未实现 |
| 2026-10-09 | 记录待确认常驻卡片取代定时汇总，普通通知仅进入主界面列表 | 根据用户最新明确选择；旧方案已替换 |
| 2026-10-09 | 新建本交接文档及根目录协作规则，并为需求文档加入同步说明 | 用户要求每次新增内容同步写入，避免长上下文导致遗漏；文档维护机制已建立 |
| 2026-10-09 | 读取并核对三份项目文档，修正根目录规则中的固定阶段描述，避免后续进度变化后留下旧状态 | 执行 `Get-Content -LiteralPath <文档绝对路径> -Encoding UTF8`，三份文件均返回退出码 0；核对需求与交接快照一致。验证范围仅为文档，不包含应用功能 |
| 2026-10-09 | 在新对话中接续项目，核对实际文件，并提出桌面两张卡片上下/左右排列这一项待选问题 | 执行 `Get-Content -LiteralPath 'docs/requirements.md' -Raw -Encoding UTF8`、`Get-Content -LiteralPath 'AGENTS.md' -Raw -Encoding UTF8`、`rg --files -g '!node_modules' -g '!.git'` 和 `Get-ChildItem -LiteralPath . -Force`，均退出码 0；实际仍只有三份项目文档，应用未开始。排列方案尚未确认。未找到 `maintain-context-hygiene` 技能，按项目规则手动核对需求、交接与实际文件一致性 |
| 2026-10-09 | 用户回复 B，确认桌面两张卡片左右排列；同步需求与交接正文，下一项讨论主界面导航布局 | 左右排列是已确认需求，界面尚未实现；此前上下排列只是建议。主界面左侧导航/顶部标签仍待用户选择。执行 `rg -n '左右排列\|左右并排\|当前阶段\|主界面导航\|尚未得到用户选择' docs/requirements.md docs/HANDOFF.md`（表格中的反斜杠用于转义竖线），退出码 0，确认两份文档的当前决定及待选问题一致；验证范围仅为文档 |
| 2026-10-09 | 用户回复 A，确认主界面采用左侧导航；同步需求与交接正文，下一项讨论事项详情的展示方式 | 已确认导航包含任务、待确认、通知、已完成和设置，右侧显示所选页面；界面尚未实现。右侧详情栏/独立弹窗仍待选择。执行 `rg -n -e '左侧导航' -e '详情' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对两份文档的已确认布局与待选问题；验证范围仅为文档 |
| 2026-10-09 | 用户回复 A，确认事项详情采用主界面右侧详情栏；同步需求与交接正文，下一项讨论桌面卡片的显示层级 | 保留事项列表，右侧查看原文、图片及编辑信息；界面尚未实现。卡片仅在桌面显示/始终置顶仍待选择。执行 `rg -n -e '右侧详情栏' -e '显示层级' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对两份文档的已确认详情布局与待选问题；验证范围仅为文档 |
| 2026-10-09 | 用户回复 A，确认桌面卡片仅在桌面显示；界面基础布局已确认，下一项讨论截止提醒发送时刻 | 已同步需求与交接正文，应用尚未实现。执行 `rg -n -e '仅在桌面显示' -e '发送时刻' -e '09:00' docs/requirements.md docs/HANDOFF.md` 和 `rg --files -g '!node_modules' -g '!.git'`，均退出码 0；核对有效决定、待选提醒时刻及实际仍只有三份项目文档。完成本轮界面布局里程碑的文档与实际文件一致性检查；验证范围仅为文档与文件清单 |
| 2026-10-09 | 用户明确按时间精度安排提醒：有具体时刻则提前提醒，仅有日期则前一天中午 12:00 提醒 | 已同步需求与交接正文，替换原来统一提前 3 天/1 天/当天的安排及固定提醒时刻的待选建议；有具体时刻的提前间隔仍未确认。执行 `rg -n -e '12:00' -e '提前间隔' -e '提前提醒间隔' -e '替代原先' -e '无法确定截止日期' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对新规则、日期精度及下一待选问题；验证范围仅为文档，提醒功能尚未实现 |
| 2026-10-09 | 用户回复 A，确认有具体时刻时提前 1 天（24 小时）及 2 小时各提醒一次；下一项讨论逾期后的提醒频率 | 已同步需求、交接快照及替换规则，截止前提醒安排已确认，提醒功能尚未实现。执行 `rg -n -e '24 小时' -e '逾期后的提醒频率' -e '每天中午' -e '当前阶段' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对有效提醒间隔、阶段及待选逾期频率；验证范围仅为文档 |
| 2026-10-09 | 用户回复 B，确认逾期后仅保留卡片高亮，不重复弹 Windows 通知；下一步制作界面原型 | 已同步需求与交接正文，基础布局与提醒安排已确认，实际应用尚未实现。执行 `rg -n -e '不重复弹' -e '界面原型' -e '当前阶段' docs/requirements.md docs/HANDOFF.md`，退出码 0，核对逾期规则与下一步一致；验证范围仅为文档 |
| 2026-10-09 | 新增单文件可点击界面原型与主界面、桌面卡片预览图；补齐逾期卡片的原截止时间显示 | 新增 `prototype/index.html`、`prototype/previews/main.png`、`prototype/previews/desktop.png`。执行 `node C:/Users/Administrator/AppData/Local/Temp/msg-purify-prototype-qa.mjs`，最终退出码 0、19 项检查通过；查看两张预览图，完成原型里程碑的有效需求、交接与实际产物一致性核对。原型已验证，真实 QQ、OCR、Windows 驻留及系统提醒尚未实现 |
| 2026-10-10 | 用户认可原型，并新增浅色/深色自由切换及过渡动画需求 | 已同步需求与交接正文；主题功能已实现并完成验证 |
| 2026-10-10 | 用户要求深色模式改为纯黑背景 | 已同步需求与交接正文；原型深色配色已调整，待重新验证 |
| 2026-10-10 | 完成纯黑深色主题调整并重新验证 | 更新 `prototype/index.html` 的深色配色，生成 `prototype/previews/main-dark.png` 和 `prototype/previews/desktop-dark.png`；执行 `node C:/Users/Administrator/AppData/Local/Temp/msg-purify-prototype-qa.mjs`，退出码 0、23 项检查通过，确认主界面和桌面背景计算颜色均为纯黑；验证范围为浏览器原型 |
| 2026-10-10 | 新增按钮按压过渡和卡片悬停动画需求 | 已同步需求与交接正文；`prototype/index.html` 已加入按钮按压缩放、按钮悬停阴影、任务卡片与桌面卡片悬停抬升、桌面卡片内条目平移动效，并尊重减少动画设置；执行 `node C:/Users/Administrator/AppData/Local/Temp/msg-purify-prototype-qa.mjs`，退出码 0、26 项检查通过 |
| 2026-10-10 | 用户选择 A，确认真实 Windows 应用采用 C# WPF 路线 | 将实现路线确定为 C# WPF + WebView2：复用现有 HTML/CSS/JS 原型，WPF 承担托盘、启动、通知、持久化、NapCat、提醒调度和桌面卡片层级；RapidOCR 规划为独立本地工作进程。路线已确认，尚未开始真实应用实现 |
| 2026-10-10 | 用户要求先完成所有主要部分的设计讨论，再开始写代码 | 后续按设计讨论、设计文档确认、实现计划、实现的顺序推进；当前不新增代码 |
| 2026-10-10 | 用户说明自己是非技术用户，希望只被询问前端操作和提醒等可感知的定制内容 | 后端数据、推送机制、通信和打包方案由开发者自行选择；后续问题聚焦日常使用体验 |
| 2026-10-10 | 用户选择 A，确认首次启动显示简短引导 | 引导完成初始连接、课程/群关联确认和提醒预览；以后登录 Windows 后在后台运行 |
| 2026-10-10 | 用户选择 A，确认点击 Windows 提醒后直接打开对应事项详情 | 提醒点击后进入具体事项的处理页面，减少从列表中再次查找的步骤 |
| 2026-10-10 | 用户选择 A，确认事项较多时提供搜索和简单筛选 | 支持按标题、课程、来源群或状态查找 |
| 2026-10-10 | 用户选择 B，确认主界面默认优先打开待确认页 | 有待确认事项时进入“待确认”，否则进入“任务” |
| 2026-10-10 | 用户选择 A，确认详情栏图片以缩略图显示，点击后放大 | 保持主界面整洁，同时保留查看原图的入口 |
| 2026-10-10 | 用户选择 A，确认 OCR 文字默认折叠 | 需要核对时手动展开，图片日期仍须单独人工确认 |
| 2026-10-10 | 用户选择 A，确认 OCR 日期作为建议显示，人工确认后才生效 | 可修改或拒绝 OCR 建议，不能自动覆盖正式截止时间 |
| 2026-10-10 | 用户选择 A，并将完成后的撤销时间调整为 1 小时 | 任务点击“完成”后立即完成，1 小时内可撤销；该行为尚未实现，属于待开发交互 |
| 2026-10-10 | 用户将完成后的撤销按钮文字改为“撤销” | 即时提示和 1 小时内的已完成记录统一使用“撤销” |
| 2026-10-10 | 用户选择 A，并将忽略待确认事项的撤销时间调整为 1 天 | 误忽略后 1 天内可撤销，撤销后恢复到待确认列表 |
| 2026-10-10 | 用户选择 A，确认新内容显示未读数量和小红点 | 左侧导航、桌面卡片和对应列表可感知新内容 |
| 2026-10-10 | 用户选择 A，确认打开并查看后自动标记已读 | 无需额外点击“标记已读”按钮 |
| 2026-10-10 | 用户选择 A，并要求在设置中保留关闭行为自定义选项 | 默认关闭主窗口后隐藏到托盘；用户可改为直接退出 |
| 2026-10-10 | 用户选择 A，确认首次打开时跟随 Windows 系统主题 | 浅色/纯黑深色主题仍可在设置中自由切换 |
| 2026-10-10 | 用户选择 C，确认桌面卡片首次由软件根据屏幕空间自动摆放 | 后续记住用户拖动和调整大小后的布局 |
| 2026-10-10 | 用户明确作业、考试和提交类任务需要系统声音，其他事项不需要 | 声音规则按事项是否需要完成区分 |
| 2026-10-10 | 用户选择 A，确认声音任务在每个提醒节点各播放一次 | 新任务发现、提前 1 天和提前 2 小时等已安排节点各播放一次 |
| 2026-10-10 | 用户选择 A，确认设置中提供任务声音提醒总开关 | 关闭声音时仍保留视觉通知和桌面卡片提醒 |
| 2026-10-10 | 用户选择 A，确认自动启动默认开启、设置中可以关闭 | 已同步需求与交接正文；自动启动和开关尚未实现。实际验证命令：`rg -n -e '自动启动默认开启' -e '自动启动开关' -e '错过截止前提醒' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档。核对当前文件仍为原型和文档，继续设计讨论，不开始写应用代码 |
| 2026-10-10 | 用户选择 A，确认恢复运行后补发一次汇总提醒 | 仅包括错过截止前提醒、尚未截止且仍待处理的任务；已逾期任务仍仅卡片高亮。需求已确认，功能尚未实现。实际验证命令：`rg -n -e '补发一次汇总提醒' -e '单独调整提醒' docs/requirements.md docs/HANDOFF.md`，退出码 0；仅核对文档同步 |
| 2026-10-10 | 用户选择 A，确认允许单条任务自定义提醒时间 | 任务详情可单独调整提醒，其他任务沿用默认规则；不改变截止时间或其精度。需求已确认，功能尚未实现。实际执行 `rg -n -e '单独调整' -e '单条任务自定义提醒' -e '下一项使用体验讨论' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户选择 A，确认任务变更后额外发一次系统通知 | 明确关联的延期、取消和提交要求修改更新原任务、标出变化，并发一次系统通知说明变化；同一变更不重复通知。需求已确认，功能尚未实现。实际执行 `rg -n -e '同一变更不重复通知' -e '无法接收 QQ 新消息' -e '用户选择 A，确认任务变更' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户选择 A，确认 QQ 消息连接持续断开超过 5 分钟时提醒一次 | 界面显示连接状态，自动尝试恢复；同一次持续断线只通知一次，恢复后重新计时。需求已确认，功能尚未实现。实际执行 `rg -n -e '持续断开超过 5 分钟' -e '手动新增任务' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户选择 A，确认支持手动新增任务 | 可补录口头布置或漏识别的事项，填写标题、课程和截止时间，来源标注为“手动添加”，使用统一的优先级、提醒及完成规则。需求已确认，功能尚未实现。实际执行 `rg -n -e '手动新增任务' -e '下一项使用体验讨论' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 用户要求先保留安静时段入口，未来若开发手机版再讨论 | Windows 第一版仅规划保留“暂未启用”的入口，暂不实现按时段自动静音；手机版尚未确定开发。已同步需求和交接，未修改应用代码，入口尚未实现。实际执行 `rg -n -e '安静时段' -e '完整设计供用户核对' docs/requirements.md docs/HANDOFF.md`，退出码 0；验证范围仅为文档 |
| 2026-10-10 | 写入 Windows 第一版完整设计草稿并同步需求和交接，替换“下一步整理设计”的旧进度 | 数据、接入、OCR、提醒、桌面、恢复与安装方案已汇总，三项使用行为及整体设计仍待确认；应用代码未开始。修正新 P0/P1 通知与逾期仅高亮的歧义，历史首次发现的逾期任务也不弹新任务通知。实际文档核对命令与结果见第 1 节；仅验证文档和文件状态，下一项只询问取消任务的去向 |
| 2026-10-10 | 用户选择 A，确认明确取消的任务移出待办和桌面卡片 | 停止该任务后续提醒，保留“已取消”记录及原始消息，不记为用户已完成；取消变更仍按既有规则通知一次。需求、交接及设计稿已同步，尚未实现。实际执行 `rg -n -e '取消任务的处理已确认' -e '保留“已取消”记录' -e '紧接着只问待选二' -e '两处行为' -e '自定义提醒' docs/requirements.md docs/HANDOFF.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0；仅核对文档。下一项讨论自定义提醒遇到延期，两种方案仍待确认 |
| 2026-10-10 | 用户选择 A，确认延期后自定义提醒保持原来的提前间隔 | 自定义提醒随新截止时间调整，未自定义任务重新计算默认节点，旧截止安排失效。已同步需求、交接及设计稿，尚未实现。实际执行 `rg -n -e '保持原来的提前间隔' -e '旧截止安排失效' -e '紧接着只问待选三' -e '最后一项行为' docs/requirements.md docs/HANDOFF.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0；仅核对文档。当前文件清单仍为原型和文档，人工核对实际状态一致；最后一项讨论已完成任务收到明确新行动要求的处理，整体设计尚未确认 |
| 2026-10-10 | 用户选择 A，确认已完成任务收到明确新行动要求后自动恢复待办 | 标出新要求并按规则继续提醒，保留此前完成记录及新旧证据；普通补充不恢复，关联或有效截止信息不确定时仍待确认。三项使用行为均已确认，需求、交接及设计稿已同步；阶段改为等待整体核对，整体设计尚未得到确认，应用代码未开始。实际执行 `rg -n -e '自动恢复为待办' -e '保留此前完成记录' -e '整体设计确认' -e '整体核对' -e '第 13' docs/requirements.md docs/HANDOFF.md docs/superpowers/specs/2026-10-10-windows-app-design.md`，退出码 0；仅核对文档。结合 `rg --files -g '!node_modules/**' -g '!bin/**' -g '!obj/**'` 返回的原型及文档清单，完成本里程碑的人工上下文核对；下一步只请用户确认或修改整体方案 |
| 2026-10-10 | 用户提交下载目录新版设计稿，要求检查是否仍有缺陷；完成文档复核 | 按行号读取 442 行附件并与有效需求及实际文件核对，两项独立只读检查分别覆盖提醒/状态及产品一致性；复核收敛为第 6 节的 5 处流程缺口和 2 处新增提醒行为差异，均未记为新增已确认需求。未修改附件、有效需求、项目内设计稿或应用代码，只按持续维护要求更新交接。实际执行 `rg -n -e '归档' -e '账号.*通知' -e '通知.*账号' -e '重新分析' -e '重新检查' -e '关联.*修改' docs/requirements.md 'C:/Users/Administrator/Downloads/2026-10-10-windows-app-design.md'`，退出码 0；结合带行号读取核对缺口。通过 agent-reach 网页阅读和 web 工具查看微软通知发送/管理及自包含部署官方说明，未运行真实通知。实际执行 `rg -n -e '完整设计复核' -e '442 行' -e '附件复核发现' -e '并发缺口' -e '迟到消息' -e '行为差异一' -e '行为差异二' docs/HANDOFF.md`，退出码 0；交接记录核对通过。人工核对上下文与文件状态一致，整体设计仍等待确认 |
| 2026-10-10 | 用户确认“都加进去”，整合七项复核补充 | 已更新 `docs/requirements.md`、项目设计稿、本文及下载目录设计稿；设计基稿采用用户提供的新版，七项补充落实到流程、数据、调度和验收。两条提醒策略由建议改为有效规则，旧概括由字段保护和精度兼容条件限定；普通技术参数由开发者确定。实际执行 PowerShell 文档核对，命令与最终结果见第 1 节：三份项目文档、14 节设计及七项验收通过，项目与下载副本正文一致，链接有效，最终退出码 0。最初两次检查分别因“不兼容/小时规则失效”和“持久化小批次/持久化迟到小批次”的匹配表述不同退出码 1；读取对应正文确认规则已存在后修正检查条件，未用改产品规则掩盖核对问题。人工完成里程碑上下文核对；应用代码未开始，整体设计仍待核对 |
| 2026-10-10 | 用户要求每次改动更新测试、全部验证通过并创建对应 Git commit | 在现有 `AGENTS.md` 加入“注意事项”，保留原协作规则，并明确新要求优先于小任务测试豁免；同步有效需求及交接。保存可重复运行的浏览器检查，新增文档检查及减少动画偏好覆盖：6 项文档检查、27 项浏览器检查全部通过，两份测试脚本语法检查通过。初始化本地 Git 并保存首个项目基线，实际命令与诊断记录见第 1 节；未改原型应用代码。下一步仍为整体设计确认，之后才编写实现计划和真实应用代码 |
| 2026-10-10 | 用户选择 A，整体设计已确认，制定实施计划 | 更新需求、项目设计、下载副本和交接的有效状态；写入 M1～M9 总路线及 M1 三任务详细计划，31 项设计验收场景均有映射。只读复核未检测到 .NET SDK、已检测到 Chrome/WebView2，未安装依赖或接入 QQ。更新并实际运行 7 项文档检查、27 项浏览器检查，均退出码 0；脚本语法及 diff 检查通过，范围为文档和原型。真实应用代码未开始，下一步执行 M1；不再把整体确认写成待选事项 |
| 2026-10-10 | 用户询问是否应先看 demo | 核对现有原型后建议先补齐后续交互再供体验；只记为待决定建议，整体设计确认不回退，不把旧原型写成新版完整 demo。强化当前阶段检查，要求需求、交接与设计均明确应用实现尚未开始。实际执行 `node --test tests/project-rules.test.mjs`，退出码 0、7 项通过；`node tests/prototype-qa.mjs` 退出码 0、27 项通过；`node --check tests/project-rules.test.mjs`、`git diff --check` 均退出码 0。本轮不修改应用或原型代码，验证仅为文档和既有浏览器原型 |
| 2026-10-10 | 用户选择 A，补齐新版 demo 供体验 | demo 先行由建议转为有效顺序；新增普通脚本、体验说明及撤销／归档／提醒／变更／搜索／新增／图片／设置等示例交互，演示进度改为浏览器本地保存，刷新不重置撤销计时。更新实际测试为 8 项文档与 78 项浏览器检查，全部通过，四张预览已生成并查看；具体命令及范围见第 1 节。真实应用代码尚未开始，下一步先收集体验反馈，再接续 M1 |
| 2026-10-10 | 用户反馈右栏无法滚动、找不到待确认提交入口 | 复现并修复 Grid 最小高度造成的裁切，独立滚动详情内容，固定底部确认按钮并明确任务／通知去向，验证失败时提示可见。最终 86 项浏览器与 8 项文档检查通过，脚本语法及 diff 检查通过；新增待确认预览并查看。普通刷新可加载修复且保留已有演示数据，真实 QQ／Windows 功能尚未接入 |
| 2026-10-10 | 用户要求两处课程下拉内容同步并支持自定义 | 筛选、待确认、编辑及手动新增任务共用课程／分类选项，新增后同步、刷新及重置保留，取消和同步不丢失草稿。新增 16 项相关浏览器检查及课程预览，最终 102 项浏览器与 8 项文档检查通过，脚本语法及 diff 检查通过；需求、交接和说明已同步。真实应用代码尚未开始，继续供用户体验 |
| 2026-10-10 | 用户反馈 demo 基本满足要求，询问下一步 | 同步演示基本认可状态，说明按既有计划先实施 M1 核心基础，再推进识别与真实集成。更新文档状态检查；本轮仅维护文档及测试，真实应用代码尚未开始 |
| 2026-10-10 | 用户确认 QQ 已在电脑登录并授权准备完成后开始编写代码；完成 M1 Task 1 | 在 codex/m1-core-foundation 分支安装项目专用 SDK 10.0.401、锁定依赖并编写日期／时刻／时区模型；10 项核心、9 项文档和 102 项浏览器检查通过，实际命令见第 1 节。提醒／状态核心继续实施，真实 QQ 接入尚未验证 |
| 2026-10-10 | 完成 M1 Task 2 默认及自定义提醒计算 | 新增提醒纯函数与 33 项测试，合计 43 项核心检查通过；日期精度不造时刻，小时失效返回默认及 NeedsReview，关闭状态保留。校正计划示例以保证提前节点严格早于精确基准；系统调度／投递尚未实现 |
| 2026-10-10 | 完成 M1 Task 3 状态与字段保护；M1 核心基础完成 | 新增状态纯函数与 44 项测试，合计 87 项核心检查通过；取消优先、延期后撤销保留最新值、完成 1 小时／忽略 24 小时边界均验证。SDK、三个核心模块及测试已写入，真实 NapCat／OCR／WPF／SQLite／系统通知仍在后续阶段；下一步 M2 |
| 2026-10-10 | 用户要求删除 demo 图片日期的独立二次确认勾选 | 填入／修改日期后直接通过确认或保存按钮提交，建议填入仍仅更新草稿。新增和更新浏览器回归，107 项浏览器、9 项文档及 87 项核心测试通过，需求与交接同步 |
| 2026-10-10 | 接续 M2，完成 Task 1 消息契约、标准化及发送上下文日期 | 原发送日期、跨年、范围／冲突、非法数值及时区边界已验证，183 项核心、10 项文档及 107 项浏览器检查通过；行动与样本尚在实施，真实 QQ／OCR 尚未接入 |
| 2026-10-10 | 完成 M2 Task 2 与 Task 3 本地规则和合成基线 | Release 核心 289 项、fixture 6 项、文档 10 项、浏览器 107 项通过；CLI 生成 65 条样本报告（23/23 正式 Task 准确、47/47 需行动召回、27/47 直接自动覆盖、0 高影响错误），无效参数退出 2。结果不代表真实 QQ 验收；下一步 M3 |
| 2026-10-10 | 用户提供真实 QQ 截图，确认红框均为学校相关群 | 同步正规群名辅助候选及非正式名称例外；截断名称待真实列表核对。仅验证截图 OCR，不把群列表视作完整通知日期样本；M3 正在实施 |
| 2026-10-11 | 用户反馈普通 QQ 与 NapCat 同账号冲突，担心无法继续使用 QQ | 纠正此前关于 Shell 聊天窗口的错误说明：Shell 是无界面接管路线，同账号不能与普通 QQ 并行；官方 Framework 桌面模式仅作为待验证候选。更新需求、设计和接入说明；真实 OneBot 尚未通过 |
| 2026-10-11 | M3 探针实现与自动验证推进 | Infrastructure 46 项、Windows 44 项、OCR 25 项和 Node 18 项通过；隐藏 WebView2 桥及自包含发布实测通过；真实 QQ 3001 未就绪，通知受提权阻止，桌面人工项未测。见 `docs/verification/integration-probes.md` |
| 2026-10-11 | 纠正 NapCat Shell 聊天能力说明并准备 Framework 显式入口 | Shell 明确记录为无界面接管路线；官方 Framework v4.18.37 包与本机 QQ `9.9.36-53644` 版本映射已核对，SHA256 与发布摘要一致，新增 `Start-NapCat-Framework.ps1/.cmd` 及路径回归。未向 QQ 注入或重启，桌面聊天与 OneBot 共存仍未验证。实际验证：Framework `-CheckOnly` 通过，Node 18/18、Core 295/295、Infrastructure 46/46、Windows 44/44、OCR 25/25、demo 107 及 `git diff --check` 通过 |
| 2026-10-11 | 用户询问是否可让普通 QQ 与 NapCat 定时自动切换 | 仅记录为未决定方案：可在空闲时切换并恢复普通 QQ，但仍非并行运行；尚未确认开发，不进入有效需求 |
| 2026-10-11 | Framework 真实只读接入及桌面窗口反馈 | 用户确认已退出普通 QQ 后实际启动 Framework 并自动登录；三个限定范围探针退出码 0，群 52 个、一群一页历史 7 条、实时群消息 0 条。用户确认聊天列表和窗口正常显示，窗口打开期间接口复测通过；启动窗口初始化 TypeError 保留记录，实时群事件、长期稳定性、完整历史补漏及 M3 剩余人工项未验收。同步当前正文与相关回归，Node 19/19、diff 检查通过，不提交私人日志或配置 |
| 2026-10-11 | 修正 Windows 权限检测并继续真实验证 | 新增令牌完整性查询和独立 whoami 回归，Windows 45/45；Basic User 高完整性误判已修正。中等完整性测试实际运行，但通知接口 `0x80070490`、未提交提醒；服务均运行，原因仍待定位。第四次 QQ 探针监听 300 秒退出码 0，未观察到实时群事件。完整回归 Core 295、Infrastructure 46、Windows 45、OCR 25、Node 19、demo 107 及 Release publish/安装通过；Infrastructure 初次构建 DLL 锁问题在探针结束后复跑通过，QQ 保持运行 |
| 2026-10-11 | 收到真实群事件并明确 QQ 只读边界 | 用户反馈新群消息后，第五次 180 秒探针收到 1 条、历史 7 条、退出码 0；历史诊断 52 群中 41 成功、11 失败，原因待查。用户询问是否会发群消息，已核对没有发送接口调用，需求明确只读及本机 Windows 提醒。相关文档和测试同步，Node 20/20、OneBotTransportTests 5/5 通过；长期稳定性、七天补漏及 M3 人工项仍未验收 |
| 2026-10-11 | 通知资源发布、冷启动回调修复及用户要求重发 | 发布补齐匹配 SDK 官方资源并拒绝缺资源安装；冷启动读取 SDK 首次参数，受控 COM 正式产物回归退出 0，Windows 45/45、四文件 Node 21/21。14:28:38 本机提醒提交 ID 36646，实际点击和声音仍待用户反馈；QQ 只读。历史只复测一个失败样本及一个对照，定位空历史错误，不代表七天补漏通过 |
| 2026-10-11 | 修复通知点击入口，用户确认出现事项窗口 | 探针改用专用 URI 打开合成事项，并补齐 Windows COM 的 `-Embedding` 参数处理；14:52:34 提醒 ID 36649 的实际点击记录与发送 Probe ID 一致，用户实际点击通过。Windows 57/57、Node 21/21、Release 发布、当前用户安装及受控 Shell/COM 回归通过；声音、静音呈现、桌面人工项、长期稳定性及七天补漏未验收，M3 继续实施 |
| 2026-10-11 | 完成真实学校通知截图 OCR 实测并补齐“交齐”规则 | 本地 RapidOCR 离线识别真实通知截图 15 行、退出码 0、stderr 为空，正文文字、位置和置信度通过；识别出的填写日期范围进入人工确认，“10月12日之前交齐”作为提交候选，图片日期只保存为 `SuggestedTime`。新增脱敏 Core 回归，Core Release 296/296、OCR 25/25、四文件 Node 22/22 通过；正式任务生成、日期人工确认及提醒投递仍未验收，原图和全文未入 Git |
| 2026-10-11 | 用户取消声音提醒，第一版统一静音 | 替代此前按任务类别播放声音及声音总开关的规则；demo 删除开关并清理旧声音偏好，通知载荷固定静音，声音验收项移除。Windows 57/57、Node 22/22、浏览器 108 项及 Release 发布/当前用户安装通过；静音呈现和 M3 其余人工项仍未验收 |
| 2026-10-11 | 修复桌面卡片不可见、缩放手柄不明显及多余状态窗 | 父窗改为 `SHELLDLL_DefView`，扩大右下角箭头手柄，成功时隐藏状态窗；真实产物/鼠标回归通过。用户确认可见、拖动和缩放正常、无白色窗口、不覆盖其他软件，基本桌面人工项通过；完整自动回归及最终 Windows 57/57、发布/安装通过。多屏、Explorer 重启、睡眠恢复和布局持久化未验收，M3 继续实施 |
| 2026-10-11 | 用户澄清“交齐”是班长对同学的要求 | 纠正“提交动作”的表述，明确截图正文属于学校通知内容；软件识别学生待办，不执行材料提交或 QQ 回复。更新分类说明和班长发布者回归，Core 296/296、文档 17/17、diff 检查通过；正式应用联调仍待验收 |

| 2026-10-11 | 静音提醒呈现与点击通过，恢复 Framework 并扩展真实历史分页 | ID 36667 用户确认，点击 Probe ID 匹配；修复带游标读取方向，Infrastructure 60/60。全群复测 41/52 到达边界、窗口内去重 1098，11 群持续 1200 保留未补全；真实 CLI 退出 1，不宣称完整补漏。详细命令、自动回归和范围见第 1 节及集成验证文档；M3 继续实施 |

| 2026-10-11 | 受控重连与跨连接去重限定范围通过 | 新增主动重连探针及回归，Infrastructure 67/67；真实两次账号核对 2/2、重连 1/1，历史 2206、去重 1098、重复 1108。11 群每轮持续 1200，CLI 退出 1；模拟新消息补回与真实无新消息分开记录，正式自动恢复/持久化仍未完成 |
