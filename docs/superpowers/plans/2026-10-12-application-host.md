# 正式应用首段：持久化界面与消息宿主 Implementation Plan

> 沿用用户已确认设计及继续授权，在当前 `codex/m3-integration-probes` 分支实施。三个独立组件并行开发，主代理组合、验证及提交，不重新询问选型或执行模式。

**Goal:** 提供可运行的正式 WPF 窗口，从账号 SQLite 显示事项并保存操作，接收置顶群消息及尝试七天历史读取。

**Architecture:** AccountWorkspace 串行组合现有三库和本账号关系/已读设置；NapCatMessageHost 使用已有只读 OneBotClient；WPF 只将可信本地页面的已校验命令交给 workspace，WebView2 界面沿用原型视觉。

**Tech Stack:** .NET 10、WPF、WebView2 1.0.4258.31、现有 SQLite、普通 HTML/CSS/JS；Windows x64。

**Spec:** [有效需求](../../requirements.md)、[已确认设计](../specs/2026-10-10-windows-app-design.md)。

## 边界

- 本段交付正式窗口、托盘隐藏/退出、任务/待确认/通知列表及来源正文、已有生命周期操作、日期确认、未读、群课程建议确认、本地消息宿主与连接状态。
- 仅置顶群，非置顶/私聊在正文解析前排除；账号不符暂停，首个账号可从登录接口确定，后续不自动切换。
- 关系按账号保存，建议采用已取得群名称供确认，不要求用户先建立课程名单。课程确认不授予发布者可信身份。
- 已处理普通候选不因课程确认重放；完整历史关系修复属于 M6，界面明确提示已有待确认仍需核对。
- SQLite 是事项来源；无样例任务、浏览器事项存储或模拟连接。时间以本机真实时刻计算，图片建议不自动成为正式时间。
- Token 仅读取 MESSAGEPURIFY_NAPCAT_TOKEN 环境变量；不写入页面、报告、文档或提交。实际报告只记录数量及错误码。
- 首次/重连尝试最近七天历史，分页有边界/无进展/错误状态；失败保留已有记录。尚不完成持久化同步游标/批次关闭及通知调度。
- 本段不发送 QQ 或 Windows 测试通知；提醒调度、OCR 队列、完整编辑/手动新增、通知转任务、正式桌面卡片、自启动、合并拆分与备份恢复留待后续。

## Task 1：AccountWorkspace 与真实存储测试

**Files:** 新建 `Infrastructure/Application/ApplicationModels.cs`、`AccountWorkspace.cs` 与 `Infrastructure.Tests/AccountWorkspaceTests.cs`。

**Interfaces:**

```csharp
AccountWorkspace(string dataRoot, string accountId);
string AccountId { get; }
void SetAvailableGroups(IReadOnlyList<OneBotGroup> groups, IReadOnlySet<string> pinnedGroupIds);
bool Ingest(SchoolMessage message, IReadOnlySet<string> pinnedGroupIds, MessageOrigin origin, string batchId, DateTimeOffset now);
ApplicationSnapshot Snapshot(DateTimeOffset now);
void Execute(WorkspaceCommand command, DateTimeOffset now);
// WorkspaceCommand: AccountId, Operation, Id, OperationId, Version,
// optional Date, Time, TimeKind, UndoOperationId, TargetTaskVersion, Course, Excluded.
```

- [x] 先运行缺入口红测；使用临时真实 SQLite 检查无样例、来源文本、同账号完成/撤销及旧版本冲突。
- [x] 保存账号群课程关系及已读版本，重启保留；确认课程不自动接受已有普通待确认，也不授权消息发送者。
- [x] 操作复用现有 AccountTaskStore，接受关联建议带 review/task 两个版本和显式确认日期；SuggestTime 仅展示。
- [x] 配置写入采用同目录临时文件替换，错误保留旧数据；workspace 内各入口使用同一锁协调。

## Task 2：只读 NapCatMessageHost

**Files:** 新建 `Infrastructure/Application/NapCatMessageHost.cs`、`Infrastructure.Tests/ApplicationMessageHostTests.cs`。

**Interfaces:**

```csharp
NapCatMessageHost(AccountWorkspace workspace, Uri endpoint, string? accessToken = null);
event Action<MessageHostStatus>? StatusChanged;
event Action? DataChanged;
MessageHostStatus Status { get; }
Task RunAsync(CancellationToken cancellationToken);
// Status: Transport, Qq, Receiving, History, Error, UpdatedAt, HistoryFailures.
```

- [x] 先运行缺入口红测；模拟 OneBot 验证真实存储、正文前置顶过滤、状态失败暂停及账号错配。
- [x] 每个事件/历史页复核置顶范围，历史按原发送时间入库并处理，跨连接去重。
- [x] 定期核对 QQ 状态/账号/置顶变化，有限延迟自动重连；历史逐群失败可显示，不误报完整补漏。

## Task 3：正式 WebView2 页面与 WPF 组合

**Files:** 新建 `Windows/App/` 项目及 `ui/`；新建桥测试、浏览器回归、启动入口和应用说明。

**Bridge:** 页面发送 `{id, operation, parameters}`，回复 `{id, success, error, snapshot, status}`；宿主更新 `{type:"state", snapshot, status}`。参数使用 camelCase，所有 ID 均为字符串。只接受 `https://app.messagepurify.local/index.html`。

- [x] 页面先写浏览器行为红测；沿用原型浅色/纯黑主题、三列布局、滚动详情及固定操作区，不加载 demo 事项。
- [x] 任务操作和待确认表单通过桥写入正式存储，错误显示且不丢草稿；每次刷新读取宿主。
- [x] 桥先写参数/来源红测，拒绝其他来源、重复 JSON 字段、未知操作和错误账号；不向页面暴露令牌。
- [x] WPF 组合 workspace/host，后台接收及托盘生命周期，默认关闭隐藏，托盘可打开/退出。
- [x] 运行 Release build/publish、真实隐藏 WebView2 合成数据闭环与浏览器回归；允许检查当前真实只读连接，但不发群消息或通知。

## 最终验证与保存

- [x] Core/Infrastructure/Windows Release、OCR、现有 Node/demo及新应用检查全部通过。
- [x] 同步 requirements、HANDOFF、tests/README；记录真实宿主与本地合成验证的区别。
- [x] 核对需求/文件/验证，执行 diff 检查，Git commit 后确认工作区。

## 实际结果与后续边界（2026-10-12）

代码、专项、完整 C# 回归及自包含发布通过：Infrastructure 305/305、Core 301/301、Windows 77/77；OCR 25/25/pip check、demo 108、新应用浏览器 18/18、启动入口 2/2、完整 Node 七文件 53/53。正式启动入口已打开真实账号窗口。正式产物合成真实 WebView2 七项闭环通过。用户退出 QQ 后已重开 Framework，真实正式宿主读取 9 个置顶群，40 项待确认、0 个正式任务/通知，重启无重复；本次历史 complete，但持续实时和真实课程确认后的任务验收仍未完成。

审查的 P0 饱和后果加级问题先红后绿修复；具体命令、故障记录及剩余范围见 HANDOFF。提醒/OCR/正式桌面/完整编辑/M6 等不属于本段完成声明。保存步骤在本次提交后核对 Git，提交标识以日志为准。
