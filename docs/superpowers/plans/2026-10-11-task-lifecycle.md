# M4 第二段：正式事项生命周期 Implementation Plan

> **For agentic workers:** 使用 executing-plans 执行本计划；先写真实 SQLite 生命周期测试，再实现最小正式事项存储。用户已确认继续实施。

**Goal:** 将已持久化的规则候选转成可操作的正式事项或待确认记录，并让完成、忽略、取消、归档、恢复与撤销在重启后保持一致。

**Architecture:** 保留现有 `messages.sqlite3` 收件箱不变，新增同一账号目录下的 `tasks.sqlite3` 作为事项生命周期存储，避免未经备份直接修改已发布的消息库结构。`AccountTaskStore` 用事务保存事项、待确认记录、证据和操作幂等键；它调用 Core 的 `TaskTransitions`，所有写操作带账号、操作 ID 和期望版本。提醒计划和 Windows 投递仍由后续阶段接入。

**Tech Stack:** .NET 10.0.401、Microsoft.Data.Sqlite 10.0.9、SQLitePCLRaw.bundle_e_sqlite3 2.1.13、System.Text.Json、xUnit。

**Spec:** [已确认设计](../specs/2026-10-10-windows-app-design.md)第 5、7.3 节；[有效需求](../../requirements.md)第 3、5 节。

## Global Constraints

- 事项账号与证据账号必须一致；所有 QQ、群和消息 ID 使用字符串。
- `RuleDisposition.Task` 且有正式 `Time` 才能自动建正式事项；`SuggestedTime` 永远不能直接成为正式时间。
- 课程、对象或时间不确定的候选进入待确认；接受待确认时由用户提供或确认正式时间。
- 原始证据只追加不覆盖；同一候选使用稳定候选键、事项/待确认 ID 和操作 ID 幂等，不重复创建事项。关联待确认被接受时追加去重后的全部证据。
- 初始事项 Version/TimeVersion 均为 1，待确认 Version 为 1；同版本的无效状态操作返回当前记录且不升版本，但登记操作 ID。关联建议一次事务将事项 Version 增加 1，只有正式时间改变才增加 TimeVersion。所有状态编辑带期望版本；旧版本不能覆盖新状态。完成撤销期限 1 小时，忽略撤销期限 24 小时，逾期或被后续操作替代时不恢复旧快照。操作幂等记录保存命令、操作顺序、请求参数与操作前后快照；相同操作 ID 重放先返回当前记录，不能因为重放时携带旧版本而再次执行。
- 取消保留证据并停止后续提醒入口；归档移出待办，恢复采用当前正式字段；本段不实现通知投递。
- 本段不接真实 QQ，不发送 QQ 或 Windows 通知，不实现 OCR 队列、提醒节点、合并拆分或备份恢复。

本段只实现正式任务和待确认候选的本地生命周期。普通通知转换、手动新增/编辑，以及标题、课程、优先级等非时间字段的逐字段手动保护不在本段；本段只持久化并执行 `TimeProtected`，不能据此宣称完整的字段保护已完成。与已有任务关联的延期、取消或新行动建议保留在待确认记录，接受时原子更新原任务，不创建第二个任务。

## Task 1：事项与待确认存储

**Files:** 新建 `src/MessagePurify.Infrastructure/Tasks/TaskStoreModels.cs`、`TaskLifecycleService.cs`、`tests/MessagePurify.Infrastructure.Tests/TaskLifecycleTests.cs`。

**Interfaces:**

```csharp
public enum ReviewState { Pending, Ignored, Accepted, Rejected }
public sealed record TaskItem(string Id, string AccountId, string Title,
    string? Course, RuleAction Action, TaskTime Time, TaskState State,
    bool TimeProtected, int? Priority, long Version, long TimeVersion,
    IReadOnlyList<MessageKey> Evidence, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, DateTimeOffset? CancelledAt,
    DateTimeOffset? ArchivedAt, DateTimeOffset? UndoExpiresAt = null,
    string? UndoOperationId = null);
public sealed record PendingReview(string Id, string AccountId,
    RuleCandidate Candidate, ReviewState State, DateTimeOffset CreatedAt,
    DateTimeOffset? IgnoredAt, DateTimeOffset? UndoExpiresAt, long Version,
    string? AcceptedTaskId = null, string? UndoOperationId = null);
```

`AccountTaskStore(string dataRoot, string accountId)` exposes:

```csharp
TaskItem CreateTask(string id, RuleCandidate candidate, string operationId,
    DateTimeOffset now);
PendingReview SavePending(string id, RuleCandidate candidate, string operationId,
    DateTimeOffset now);
TaskItem? FindTask(string id);
PendingReview? FindReview(string id);
IReadOnlyList<TaskItem> ListTasks(TaskState? state = null);
IReadOnlyList<PendingReview> ListReviews(ReviewState? state = null);
TaskItem Complete(string id, string operationId, long expectedVersion,
    DateTimeOffset now);
TaskItem UndoComplete(string id, string operationId, long expectedVersion,
    DateTimeOffset now, string? completedOperationId = null);
TaskItem Cancel(string id, string operationId, long expectedVersion,
    DateTimeOffset now);
TaskItem Archive(string id, string operationId, long expectedVersion,
    DateTimeOffset now);
TaskItem RestoreArchived(string id, string operationId, long expectedVersion,
    DateTimeOffset now);
TaskItem AcceptTime(string id, string operationId, long expectedVersion,
    TaskTime time, DateTimeOffset now);
PendingReview IgnoreReview(string id, string operationId, long expectedVersion,
    DateTimeOffset now);
PendingReview UndoIgnored(string id, string operationId, long expectedVersion,
    DateTimeOffset now, string? ignoredOperationId = null);
TaskItem AcceptReview(string reviewId, string taskId, string operationId,
    long expectedReviewVersion, TaskTime? confirmedTime, DateTimeOffset now,
    long? expectedTaskVersion = null);
PendingReview RejectReview(string reviewId, string operationId,
    long expectedVersion, DateTimeOffset now);
```

`AcceptReview` 的 `expectedTaskVersion` 在新事项上必须为空；关联既有事项时必须提供并匹配当前版本，`taskId` 必须与候选的 `TargetTaskId` 一致。新事项、延期和新行动必须有正式 `Time` 或用户提供的 `confirmedTime`；关联取消不需要新时间。用户提供的 `confirmedTime` 优先并令时间受保护，未经确认的冲突不能覆盖受保护时间。`SuggestedTime` 永远只留在候选 JSON 中。延期、取消或新行动在确认后更新目标事项并追加证据；取消不受时间保护阻挡。`AcceptReview` 只接受 `Pending` 状态，`Ignored`、`Rejected` 或已接受记录返回冲突或幂等结果。

`RejectReview` 表示用户拒绝建议：保留候选和证据，正式事项不变，且没有忽略撤销入口。`IgnoreReview` 用于暂不处理的待确认候选，`UndoIgnored` 在 24 小时内恢复 `Pending`；接受、显式拒绝（也可终止 Ignored 建议）或再次忽略使旧忽略撤销凭据失效，撤销仅改变 review，不恢复任务快照。撤销可传原完成/忽略操作 ID；新界面调用须使用该凭据，避免旧入口影响第二次操作。

- [x] 先写真实 SQLite 测试：自动任务、待确认和 SuggestedTime 隔离，账号/证据校验，重启保留；运行缺实现红测。
- [x] 实现 `tasks.sqlite3` 的版本 1 表、账号 owner、事项/待确认 JSON 字段、证据和操作幂等记录；候选不满足条件时抛出参数错误且不写库。
- [x] 操作表按账号和操作 ID 唯一保存命令、目标、参数、顺序及操作前后快照；同一 ID 的不同目标/命令/有效参数直接冲突。操作指纹不包含重放时间，重放先命中幂等记录。
- [x] 使用规范化完整候选内容作为唯一键（证据去重排序，其他内容相同）；同一候选换 ID 重放返回既有记录，不覆盖用户字段。不同内容的候选及重新分析协调留后续路由阶段；一条消息可支持多个合法事项。关联建议接受时追加证据，所有证据校验账号一致，任务/候选标题和 ID 非空。

## Task 2：状态与并发操作

- [x] 用事务调用 `TaskTransitions` 实现完成、1 小时撤销、取消、归档/恢复、时间确认和待确认忽略/24 小时撤销；`UndoIgnore` 的状态转换和期限检查补齐到 Core 或等价的纯函数。
- [x] 每个 operation ID 只生效一次；相同操作重放返回当前记录，新的 operation ID 携带旧 `expectedVersion` 拒绝。完成后仅有可靠延期/补充时允许撤销并采用最新正式字段；后续取消、新行动恢复或再次完成会使旧撤销失效，操作历史保留替代前后的状态。
- [x] 接受待确认候选时在同一事务中创建新正式事项，或按 `TargetTaskId` 原子更新既有事项、追加全部证据并把 review 标为 Accepted；没有确认时间且候选只有 SuggestedTime 时拒绝，不能把建议日期写入正式时间。
- [x] `CompletedAt`、`CancelledAt`、`ArchivedAt` 是当前记录投影：完成撤销或归档恢复清除对应当前字段；历史完成/忽略/归档由 operation 的命令、前后快照和顺序保留。当前记录投影持久化撤销截止和原操作 ID；被替代后清除凭据，替代操作可从历史定位，历史显示 API 留界面接入阶段。

## Task 3：验证及交付

- [x] 运行新增生命周期测试、Core/Infrastructure/Windows Release、OCR、Node 和 demo 全量回归。
- [x] 更新 `docs/requirements.md`、`docs/HANDOFF.md`、`tests/README.md`，准确区分代码写入、自动验证和真实 QQ/通知未接入。
- [x] 运行 diff 检查并创建对应 Git commit。

## 未纳入本段

普通通知转换、手动新增/编辑及标题、课程、优先级等非时间字段的逐字段保护、合并/拆分、提醒计划持久化、通知投递和清理、跨目录备份恢复、OCR 队列、真实 QQ 连接到正式处理器的宿主联调，按后续 M4～M7 任务继续。

## 本轮实际验证（2026-10-11）

Core Release 296/296、Infrastructure Release 184/184（生命周期 61）、Windows Release 57/57、OCR unittest 25/25 与 pip check、demo 108 项实际通过；Node 五文件 30/30、退出码 0；本轮 commit 随源码和交接同步保存，标识以 Git 日志为准。最终 diff 检查在暂存后执行。本段仅完成本地生命周期 API，自动候选路由和正式宿主联调留下一段。
