# 持久化提醒第一段 Implementation Plan

> **For agentic workers:** 使用测试先行和并行组件实现；当前已确认设计与持续开发授权有效，在现有 `codex/m3-integration-probes` 分支继续。

**Goal:** 在正式任务中保存默认、自定义及关闭提醒的选择，展示真实计划，并以可控投递端验证时间节点、恢复汇总、去重及事项变更与提交的串行协调。

**Architecture:** 复用 `ReminderPlanner`，新增账号独立 `reminders.sqlite3` schema 1，保留原三库格式。提醒服务读取最新正式任务，保存计划和投递台账；`AccountWorkspace` 的同一锁协调用户操作、消息路由及提交。Windows 实际投递端和自动运行循环在下一段接入，本段页面明确显示尚未开启系统提醒。

**Tech Stack:** .NET 10、Microsoft.Data.Sqlite、现有 WPF/WebView2、xUnit、Node/Chrome；不增加依赖。

**Spec:** `docs/requirements.md` 第 6 节及 `docs/superpowers/specs/2026-10-10-windows-app-design.md` 第 2.2、2.3、10.3 节。

## Global Constraints

- 只读取 QQ 置顶群，不向 QQ 发送消息；本段验证不提交 Windows 测试通知。
- 图片建议不能自动成为正式时间；仅正式任务生成提醒计划。
- 所有通知统一静音；台账的“已提交”不代表用户已看到。
- 已完成、取消、归档或时间已过不提交时间节点；日期当天仍有效。
- 内容版本变化不创建新的节点；延期按时间版本重算，关闭选择保持。
- 自定义小时规则降为日期精度后保留选择，临时使用默认计划并显示待重新设置提示。
- 投递前持久化发送中；未知结果不逐条重放，仍有效事项可进入一次恢复汇总。
- 每次实现同步文档，运行现有全部测试并创建对应 Git commit。

## 接口约定

新增 `MessagePurify.Infrastructure.Reminders` 命名空间：

```csharp
public sealed record ReminderPreference(string Mode = "default", double? Hours = null,
    int? Days = null, string? Clock = null);
// Mode: default / hours / calendar / off；Clock 为 HH:mm。
public interface IReminderSink
{
    ReminderSubmission Submit(ReminderDelivery delivery);
    void Remove(string notificationId);
}
// ReminderSubmission: State（submitted/blocked/failed/unknown）与 NotificationId、Error。
// TaskReminderPlan: TaskId、TimeVersion、PlanVersion、Preference、NeedsReview、Active、Nodes。
// ReminderNode: Id、ScheduledAt、State。
// ReminderDelivery: Id、AccountId、Kind（time/recovery）、Members、State、NotificationId、Error。
// ReminderMember: TaskId、Title、TimeVersion、PlanVersion、NodeIds。
public sealed class AccountReminderService
{
    public AccountReminderService(string dataRoot, string accountId, AccountTaskStore tasks);
    public IReadOnlyList<TaskReminderPlan> Synchronize(DateTimeOffset now);
    public TaskReminderPlan Configure(string taskId, long expectedTaskVersion,
        long expectedPlanVersion, ReminderPreference preference, string operationId, DateTimeOffset now);
    public IReadOnlyList<ReminderDelivery> DispatchDue(DateTimeOffset now, IReminderSink sink,
        bool recovering = false);
    public void ReconcileNotifications(IReminderSink sink, DateTimeOffset now);
    public IReadOnlyList<ReminderDelivery> Deliveries();
}
```

前端快照 `ApplicationSnapshot.Reminders` 为计划列表（camelCase）。`WorkspaceCommand` 增加 `ReminderMode`、`ReminderHours`、`ReminderDays`、`ReminderClock`、`ReminderVersion`，桥操作 `set-reminder`；必须同时核对事项版本和提醒计划版本，操作 ID 重试幂等。`AccountWorkspace.DispatchReminders` 在原锁内调用服务并记住投递端，使后续正式状态变化能清理旧通知。

## Task 1：提醒服务和真实 SQLite 回归

**Files:** `src/MessagePurify.Infrastructure/Reminders/ReminderModels.cs`、`AccountReminderService.cs`；`tests/MessagePurify.Infrastructure.Tests/PersistedReminderTests.cs`。

- [x] 先写并运行缺入口失败测试，预期默认精确时间产生 24/2 小时节点、日期产生前日 12:00。
- [x] 保存账号归属/schema；错误版本、损坏及账号错配保留旧文件。
- [x] 实现选择、计划、节点台账；配置操作幂等，改标题/完成撤销不重放已提交节点，延期生成新节点。
- [x] 实现到期提交及多节点/多事项恢复汇总；过期排除、Blocked 不循环提交、明确失败有限重试、Sending 崩溃变 Unknown 并只汇总一次。
- [x] 保存前提交状态，提交后复核最新版本；移除已失效单条/汇总通知，清理失败保留记录重试。
- [x] 尚未提交的 time pending 在恢复时并入一次汇总；recovery pending 接续原 ID 并吸收新到期成员。失效汇总整条清理，剩余成员已提交记录保留，不另发摘要。
- [x] 专项 Release 实际通过；测试用固定时钟、临时 SQLite 和可控外部投递端。

## Task 2：工作区协调与桥

**Files:** `Application/AccountWorkspace.cs`、`ApplicationModels.cs`、`Windows/App/AppBridgeProtocol.cs`；新增 `WorkspaceReminderTests.cs`，扩充桥测试。

- [x] 先写缺入口红测；用户接受待确认后生成默认计划，未确认图片无计划。
- [x] 同锁刷新计划、配置、消息状态变更和投递，快照包含真实计划。
- [x] 两线程暂停投递检查取消/完成/延期的两种先后顺序；汇总不混入失效成员。
- [x] 桥只接受合法模式/有限正数/有效钟点和计划版本；操作失败不丢草稿。

## Task 3：正式页面提醒设置

**Files:** `Windows/App/ui/app.js`、`app.css`；`tests/application-ui.test.mjs`。

- [x] 先运行浏览器失败测试：任务详情可选默认、小时、日历、关闭，使用原事项版本和计划版本提交。
- [x] 显示计划钟点和未开启系统提醒；精度失效提示重新设置，图片待确认不显示正式提醒计划。
- [x] 后端成功后更新快照；失败保留输入，重开保留已保存选择。

## Task 4：验证、发布与保存

- [x] Infrastructure/Core/Windows Release、OCR pip check/unittest、Node 七文件、demo 全部通过。
- [x] 新产物自包含发布及真实 WebView2 合成闭环通过；不关闭用户当前窗口或 QQ。
- [x] 发布至 `.tools/published/app-reminders`，`packaging/app/Start-MessagePurify.ps1` 默认入口改为此目录；`tests/application-launcher.test.mjs` 实际验证新目录选择、原版文件保留及现有路径/鉴权行为。
- [x] 更新 requirements、HANDOFF、tests/README、应用 README；区分已保存计划、可控投递及实际 Windows 提醒尚未接入。
- [x] diff/staged diff 检查、Git commit、确认工作区状态。

## 本段未覆盖

Windows 实际注册/激活和投递循环、历史/迟到批次汇总、实时新发现及独立变更通知、权限恢复、断线五分钟通知、桌面、自启动、OCR 队列、完整账号切换均继续按总计划执行；本段不宣称 M8 完成。

## 实际结果（2026-10-12）

- 最终 Infrastructure Release 375/375；服务 46/46、工作区提醒 24/24。本段 Core 301/301、Windows 96/96、OCR pip check/unittest 25/25、demo 108、Node 七文件 59/59 通过。
- 审查实际复现的失败汇总成员变化漏重试，四项真实 SQLite 回归先红 42/46 再绿 46/46；保持原记录、未成功成员及三次跨重启预算。最终窄复现的完成/延期两路均仅提交有效 B 一次。
- 修复后再次自包含发布到 app-reminders；真实隐藏 WebView2 合成八项通过、进程退出 0，最终启动入口检查退出 0。原窗口、产物及 QQ 保留。
- 需求/交接/测试/应用说明同步后文档检查 24/24、diff 检查通过。系统提醒尚未开启；Windows 实际投递和后台循环为下一段。
