# 账号备份恢复实施计划

> **For agentic workers:** 使用 subagent-driven-development 分工实施；本项目采用一次有限复核、根代理串行构建验证，沿用已授权分支，不重复询问执行模式。

**Goal:** 设置页可以导出当前账号快照、预览并确认整份恢复，失败保留原数据。

**Architecture:** 工作区原实例在 gate 内备份及切换，宿主已接收的消息等锁释放后核对账号继续。四库同一时点通过附加连接 BEGIN IMMEDIATE 和独立只读 SQLite Backup API 保证；恢复使用暂存目录、原目录和持久化切换记录。工作区 generation 使旧界面及卡片操作失效。

**Tech Stack:** .NET 10、Microsoft.Data.Sqlite、ZipArchive、SHA256、WPF 文件选择器、WebView2。

**Spec:** `docs/superpowers/specs/2026-10-10-windows-app-design.md` 第 5.3 节。

## 约束及实施裁定

- QQ/NapCat 只读，不发送群聊/私聊或系统通知。
- 仅验证隔离合成账号；不覆盖真实数据、切换默认入口或运行版本。
- 原图片当前仅有 AssetId。包记录 not-downloaded、空路径及校验值；有未下载图片时明确显示“不完整：未包含原图”，仍允许用户预览并确认恢复已有本地数据。图片下载和完整图片验收仍随后期 OCR 项目实施。
- 不包含 Token、登录材料、其他账号、本机游戏扫描缓存或 active-account.txt。
- 保存账号课程关系、已读版本、四库全部历史及原撤销期限；应用共用自启动、布局和界面偏好在恢复预览中明确列出。
- 失败或中断恢复原目录和原共用设置；启动须先恢复未完成切换，再构造工作区。
- 不构造工作区校验 ZIP；校验连接只读。恢复后重载工作区设置，并请求受游戏限制的七天历史补漏。

## 1. 存储包及校验

文件：新增 `src/MessagePurify.Infrastructure/Backup/AccountBackupService.cs`、`BackupModels.cs`、`tests/MessagePurify.Infrastructure.Tests/AccountBackupTests.cs`。

接口：`AccountBackupService(dataRoot, accountId)`；`Export(destination, workspaceSettingsJson, BackupApplicationSettings, now)` 返回 `BackupResult`；`Inspect(source)` 返回可释放的 `PreparedAccountBackup`；`Restore(prepared, workspaceSettingsJson, currentSettings, now, applySettings)` 返回 `BackupResult`；`RecoverInterrupted(dataRoot)` 在启动时回滚未完成切换。

`BackupApplicationSettings(bool AutoStartEnabled, string Theme, string Motion, string? DesktopLayoutJson)`；结果包含账号、文件位置、UTC时刻、完整性、未下载图片数、任务/审核/通知数和恢复前备份位置。预览有宿主持有的 Id，不接受网页任意目标路径。

- [x] 编写失败测试：所有状态/人工覆盖/关系/历史/撤销期限往返；缺失、校验损坏、账号、版本、无效JSON拒绝；不改原目录；恢复前备份；设置及目录切换失败回滚；启动中断回滚。
- [x] 根代理运行 `dotnet test tests/MessagePurify.Infrastructure.Tests --configuration Release --no-restore --filter FullyQualifiedName~AccountBackupTests`，确认未实现失败。
- [x] 固定 ZIP 路径：manifest.json、account 四库与 workspace-settings.json、settings/app-settings.json、settings/interface-preferences.json、可选 desktop-layout.json。文件清单严格核对长度/校验和；四库 integrity/schema/account/table/JSON/证据归属只读校验。
- [x] gate 内取得四库保留锁，独立只读源调用 BackupDatabase；校验暂存产物后临时 ZIP 改名。结果持久化到账号外 backup-history.json，不导入旧位置结果。
- [x] 恢复前导出同格式自动备份；持久化 rollback 记录，整体切目录和共用设置；失败及重启中断回滚，成功后再提交记录。

## 2. 工作区及宿主协调

文件：AccountWorkspace、ApplicationModels、AppRuntime、DesktopCardHost；新增 BackupWorkspaceTests；新增 BackupRuntimeTests。

- [x] 测试旧 generation 请求恢复后拒绝、收件等锁释放后继续、设置重载、恢复前备份失败不切换。
- [x] 工作区 gate 包装服务，generation 初值 0；恢复成功递增且写入快照。命令携带 generation；旧请求不能写入恢复数据。
- [x] 运行时恢复前设置 IsRestoring，页面/卡片暂停；原宿主保留引用，收件锁内继续。成功复用现有 Startup 请求触发补漏，受现有 CanSync 限制。
- [x] 桌面卡片捕获渲染时 generation，拒绝旧操作；成功重新载入布局及当前快照。

## 3. 正式设置页和桥

文件：AppBridgeProtocol、AppWindow、新增 BackupFileDialogs；ui/app.js、app.css；BackupBridgeTests、application-ui.test.mjs。

- [x] 编写并运行失败测试：导出、选择预览、取消、确认、账号及预览 ID 隔离；无路径参数；恢复刷新清除旧草稿和迟到响应；不完整图片明确呈现。
- [x] 操作：export-backup(accountId,generation,theme,motion)、inspect-backup(accountId,generation)、restore-backup(accountId,generation,previewId,theme,motion)、cancel-backup(accountId,previewId)、open-backup-location(accountId)。文件选择使用注入的 IBackupFileDialogs。
- [x] 设置显示最近结果、打开本地位置；预览显示账号/时刻/全部状态数量/共用设置/图片范围，显式“确认覆盖并恢复”；取消释放暂存目录。
- [x] 界面备份请求独立状态，较长操作不丢弃未知结果；generation 变化后丢弃旧响应与草稿、应用恢复偏好并刷新卡片。

## 4. 验证、有限复核和提交

文件：SmokeCheck、docs/HANDOFF.md、docs/requirements.md、App/README.md、project-rules.test.mjs。

- [x] 发布版 Native 使用注入文件选择器、隔离账号执行真实页面导出/预览/取消/确认恢复/损坏包拒绝/原数据保留/重开/卡片与布局刷新；核对报告新写入并查看截图。
- [x] 一次有限代码复核，修复有证据的具体问题及回归。
- [x] 根代理串行执行三个 .NET Release 全量、Node 全量、App build/publish、发布版 Native Smoke。
- [x] 更新真实进度与命令结果，人工核对上下文，git diff --check；全部通过后对应 Git commit，确认工作区干净。
