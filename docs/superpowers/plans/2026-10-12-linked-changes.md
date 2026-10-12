# M4 第四段：可靠关联变更与消息顺序 Implementation Plan

> **For agentic workers:** 沿用已确认完整设计和用户本轮继续授权，在当前开发分支执行；由主代理实施生产、独立代理编写整合测试，并做最终只读核对，无需再次选择技术路线。

**Goal:** 本地首轮处理可靠取消、延期、新行动和催交，原子更新同一事项及证据；冲突保留待确认，重启不重放。

**Architecture:** 在 AccountCandidateRouter 内按原发送时间选择最早未回执消息，从当前 tasks 投影 KnownTasks，事务内提取及路由。自动变更共用 AccountTaskStore 的 AcceptLinked 和操作历史核心。三库 schema 1 不变，用已关联证据的原消息时间保守核对迟到变更。

**Tech Stack:** .NET 10、Microsoft.Data.Sqlite、xUnit、System.Text.Json；不新增依赖。

**Spec:** [有效需求](../../requirements.md)第 2～5 节及 [完整设计](../specs/2026-10-10-windows-app-design.md)第 7.1/7.3 节。

## Global Constraints

- 仅当前账号/群及当前置顶群；课程/发布者排除、账号归属和三库 rollback journal 校验继续有效。
- 时间来自原消息发送时刻；SuggestedTime 不自动确立正式时间；取消不受手动时间保护阻挡。
- 信任仅来自当前群上下文 TrustedSenders；投影 ConfirmedPublisherId=null，不把历史发送者或课程确认当作发布者授权。
- 已有普通候选不升级；只有尚未路由的 Pending/Change 关联行动在原课程确认仍匹配时重核对。已路由消息重新分析不重放，完整 M6 修复另行实施。
- 已归档/已取消任务的新行动仍待确认；已完成任务的可靠新行动及有效正式时间可恢复，历史完成记录保留。普通催交不恢复、不覆盖时间。
- 不执行真实 QQ 操作，不发系统通知；本段仍为本地 API。

## Task 1：真实 SQLite 红测

**Files:** 新建 `tests/MessagePurify.Infrastructure.Tests/LinkedChangeRoutingTests.cs`；更新 `CandidateRoutingTests.cs` 的可靠取消预期。

**Interfaces:**

```csharp
int AccountCandidateRouter.ProcessAndRoutePending(
    RuleContext context, IReadOnlySet<string> pinnedGroupIds, int limit = 100);
// RoutePending 保留已有提取入口，遇到更早未提取消息先等待，不越序。
```

- [x] 可靠取消最小红测：旧预期改为 `TaskState.Cancelled`，原事项 Version 推进，Evidence 含取消来源，Reviews 为空。
- [x] 反向入队、批量预提取及未提取早消息：原任务先落库，后续关联使用持久化最新快照；调用者 KnownTasks 不参与目标授权。
- [x] 时间保护冲突、取消例外、完成延期后撤销、新行动恢复、归档/取消仍待确认；催交仅补证据。
- [x] 未可信来源/疑问/OCR/跨群/课程改变、不完整来源时间、迟到旧变更及同秒顺序冲突均不得自动覆盖。
- [x] 回执触发器注入失败，提取/正式更新/证据/操作全部回滚，重试和重启不重放。
- [x] 实际运行新入口最小测试确认缺方法编译红测，取消行为红测确认仍 Active；随后统一运行三个测试类专项，命令及结果见 HANDOFF 第 1 节。

## Task 2：最小事务实现

**Files:** 修改 `Processing/AccountCandidateRouter.cs` 和 `Tasks/TaskLifecycleService.cs`。

**Interfaces:** 内部 `ApplyAutomaticChangeInTransaction` 返回 `(TaskItem? Task, string? ReviewReason)`，不能自动更新的候选由 router 通过既有 SavePendingInTransaction 保存。

- [x] 两个入口共用一个路由循环；SQL 选最早未回执消息，不先按 extraction 非空过滤。新入口提取 raw，保存候选/处理版本与事项/回执一并提交。
- [x] 从 main.tasks 读取本账号当前事项，不复用调用者快照；每个关联候选重新读取最新任务再核对。旧已保存普通候选和原始 OCR 建议保留。
- [x] 使用 LEFT JOIN 检查除本消息外的全部既有 task_evidence：有缺失时间、 incoming 不晚于最大既有原时间或无法确认基线时保存关联待确认，不能按 messageId/rowid 猜同秒先后。
- [x] 可靠 Change 自动共用 AcceptLinked 写正式事项与操作前后快照；TimeProtected 冲突转 Pending，Cancel 例外，Archived/Cancelled 新行动仍 Pending。
- [x] 自动 NewRequirement 保持原 Title，完整新要求记录在操作和证据；共用 AcceptLinked 在 Remind 未显式 confirmedTime 时忽略候选旧时间，避免恢复或复制旧精度。
- [x] 将刷新后的关联候选、规则版本与处理版本保存在同一事务，重试不留下半次提取；本地通知元数据使用最终处理版本。

## Task 3：验证与保存

- [x] 路由/关联/生命周期专项及 Core/Infrastructure/Windows Release、OCR、Node、demo 实际验证；不把本地通过记作真实宿主联调。
- [x] 同步 requirements、HANDOFF、tests/README、文档状态回归与本计划的实际结果。
- [x] 核对实际文件与有效需求，diff 检查通过后由本提交保存；Git 标识及干净工作区以提交后核对为准。

## 实际结果（2026-10-12）

专项 128/128（候选 36、关联 30、生命周期 62）、Infrastructure Release 251/251、Core 296/296、Windows 57/57、OCR 25/25 与 pip check、Node 五文件 32/32、demo 108 项，均退出码 0。通知 raw/预提取版本错误两项及旧 Remind 接受回退期限一项均先实际运行失败后修复。完整命令、源码路径和剩余范围见 HANDOFF 第 1 节；没有真实 QQ/系统通知操作。
