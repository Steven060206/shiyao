# M4 第三段：消息候选自动路由 Implementation Plan

> **For agentic workers:** 沿用用户已确认的设计和继续实施授权，在当前开发分支执行；先运行真实 SQLite 红测，再实现和验证。产品规则以 requirements 为准。

**Goal:** 把本地收件箱已保存的首轮识别结果自动送入正式任务、待确认或普通通知，重启及重复运行不重复创建记录。

**Architecture:** 新增账号目录中的 `routing.sqlite3` schema 1，保存消息路由回执及通知。既有 messages/tasks schema 1 不修改。在 tasks 连接中 ATTACH routing 和 messages，以单条消息为事务边界，读取当前提取结果、写入全部候选对应记录、登记幂等操作和回执后一次提交；失败不留下半条消息的事项。

**Tech Stack:** .NET 10、Microsoft.Data.Sqlite、System.Text.Json、xUnit；复用现有生命周期 API 和本地提取器。

**Spec:** [有效需求](../../requirements.md)第 2～5 节及 [完整设计](../specs/2026-10-10-windows-app-design.md)第 5、7 节。

## 范围及契约

- 当前置顶群、账号、群上下文和经确认的机器人排除再次检查；群取消置顶或关系暂时排除时保留消息、已有事项和未路由进度。
- 路由前课程确认已撤销或改变时，旧 Task/Notice 转待确认，保留原课程、时间与证据；不升级原 Pending/Change。
- 可靠的新 `Task` 使用正式 `Time` 自动建事项。普通清楚行动无需发布者在可信名单；发布者可靠性仍用于高影响关联变更判断，与现有提取器一致。
- `Pending` 和 `Change` 写入待确认；本段不自动应用延期/取消/新要求，仍可通过既有 AcceptReview 事务接受。可靠关联变更的自动应用与原消息顺序协调继续后续实施，不改变已确认产品规则。
- `Notice` 保存为独立通知；`Ignored` 只完成处理回执，不建立任务或待确认。普通通知转任务和正式 UI 不在本段。
- 图片 `SuggestedTime` 不转成正式时间。一条消息的多个行动可产生多个记录，原始候选和证据保留。
- 路由记录 ID、操作 ID 由完整消息键和候选序号确定性生成，账号和消息标识按字符串处理；每个消息首轮只路由一次，用户已完成/忽略/拒绝/归档后不因重启复播。
- 回执记录实际 ProcessingVersion 和 RuleVersion；已经路由的消息后续重新分析不自动重放，课程修复与版本差异协调仍由后续 M6 处理。
- 三库事务只接受磁盘 rollback journal（delete/truncate/persist），拒绝 WAL/内存/关闭日志并保留原库。
- 不接真实 QQ 或通知，不创建后台定时服务，不实现 OCR 队列、通知投递、合并拆分、完整备份或迁移。

## Task 1：先写路由集成测试

**Files:** `tests/MessagePurify.Infrastructure.Tests/CandidateRoutingTests.cs`。

**Interfaces:**

```csharp
new AccountCandidateRouter(AccountMessageStore messages, AccountTaskStore tasks);
int RoutePending(RuleContext context, IReadOnlySet<string> pinnedGroupIds, int limit = 100);
IReadOnlyList<StoredNotice> ListNotices();
string RoutingDatabasePath { get; }
```

- [x] 用 LocalMessageProcessor 和真实临时 SQLite 生成 Task/Pending/OCR/Notice/Ignored，多行动结果自动分流；普通未确认发布者的新任务也入库。
- [x] 测试重启/重复执行、历史与实时去重、限量及群范围、取消置顶和机器人/课程排除、账号/目录错配、保留用户处理结果。
- [x] 用 SQLite 触发器注入回执写入失败；整条消息的任务/建议/通知/操作/回执全部回滚，删除触发器后同操作 ID 可重试。
- [x] 测试旧库 schema 不变、新库账号归属/未知版本保留、WAL 拒绝、Change 保存待确认、已路由再分析不重放。
- [x] 运行 `dotnet test ... --filter FullyQualifiedName~CandidateRoutingTests`，确认缺实现红测；课程关联变更四项回归另行先红后绿。

## Task 2：实现最小路由

**Files:** `Processing/AccountCandidateRouter.cs`、`Processing/CandidateRoutingModels.cs`；局部修改 `Tasks/TaskLifecycleService.cs` 复用原事务操作核心。

- [x] 将 CreateTask/SavePending 的事务内部操作与事务开启分离；公有生命周期方法保持原契约及回归，路由使用同一核心，不复制状态逻辑。
- [x] 新路由库初始化检查版本和 owner；从已保存的消息按原发送时间及 rowid 选择当前群未路由记录，每条事务重新读取候选。
- [x] 创建稳定 ID，在同一附加数据库事务保存候选结果、完整通知和路由回执；无效候选抛错并保持可重试。
- [x] 提供通知列表查询；API 同步调用，本段由调用者继续 Ingest → ProcessPending → RoutePending，没有正式服务宿主。

## Task 3：验证与交付

- [x] 生命周期与路由专项、Core/Infrastructure/Windows Release、OCR、Node 和 demo 按 tests/README.md 实际验证。
- [x] 更新 requirements、HANDOFF、tests/README 和文档回归，明确本段路由已实现以及真实宿主/自动关联变更等剩余边界。
- [x] 核对实际源码、文档及验证，diff 检查后由本轮 Git commit 保存；标识以 Git 日志为准。

## 实际结果（2026-10-12）

路由与生命周期专项 94/94（32+62）；Infrastructure Release 217/217、Core 296/296、Windows 57/57、OCR unittest 25/25 与 pip check、Node 五文件 31/31、demo 108 项均退出码 0。最初红测确认缺实现；普通新任务发布者判断另行红测，课程确认撤销/改变的 Task/Notice 四项也先确认失败后修正。完整命令与剩余工作见 HANDOFF 第 1 节。
