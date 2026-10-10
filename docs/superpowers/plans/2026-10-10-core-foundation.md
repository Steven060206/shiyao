# 消息提纯：第一阶段核心基础 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. 用户的小任务快速通道优先；每次改动仍须更新测试、全部验证通过并提交 Git commit。

**Goal:** 建立可编译、可测试的时间、提醒与状态核心，供后续真实接入和持久化调用。

**Architecture:** 一个无窗口/网络/数据库依赖的 .NET 10 类库，使用明确输入的当前时间完成纯函数计算。日期与时刻分开保存，提醒节点不回写正式时间；状态与字段变更返回新版本，不恢复整个旧快照。

**Tech Stack:** C#、.NET 10 LTS、xUnit、现有 Node 原型/文档检查。

**Spec:** [已确认设计](../specs/2026-10-10-windows-app-design.md)第 2、5、7.3 节；[总计划](2026-10-10-windows-app-implementation.md)的 M1。

## Global Constraints

- 用户已授权准备完成后开始编写代码，本计划正在执行；逐任务记录实际产物、验证与提交。
- Windows x64、.NET 10 LTS；本机默认 dotnet 尚无 SDK，准备项目专用 SDK，安装运行时不能替代安装 SDK。
- 日期当天仍有效，次日才过期；不能把零点或 23:59 写成正式截止。
- 默认具体时刻提前 24 小时、2 小时；仅日期提前一天 12:00。
- 小时自定义遇到日期精度失效时回退默认并标记待确认，日历自定义保持偏移和钟点；关闭提醒保持关闭。
- 完成撤销 1 小时，忽略撤销 24 小时；过期/已被取消或新要求替代的旧撤销不生效。
- 每个任务的测试及全部已有测试通过后，同步交接并创建对应 commit。

## 文件结构与公开契约

| 文件 | 职责 |
| --- | --- |
| `global.json` | 锁定实际安装且验证的 .NET 10 SDK 补丁系列 |
| `.gitignore` | 排除生成的 bin/obj/TestResults，保留源码、测试和锁文件 |
| `src/MessagePurify.Core/MessagePurify.Core.csproj` | net10.0 类库，启用 Nullable |
| `src/MessagePurify.Core/Time/TaskTime.cs` | 时间类型、日期/可选时刻、时区和有效性 |
| `src/MessagePurify.Core/Reminders/ReminderPlanner.cs` | 默认/自定义节点及失效提示决定 |
| `src/MessagePurify.Core/State/TaskTransitions.cs` | 完成/取消/归档/恢复、延期冲突及撤销决定 |
| `tests/MessagePurify.Core.Tests/MessagePurify.Core.Tests.csproj` | 引用 Core 的测试项目，NuGet 依赖锁定 |
| `tests/MessagePurify.Core.Tests/TaskTimeTests.cs` | 时间精度与过期边界 |
| `tests/MessagePurify.Core.Tests/ReminderPlannerTests.cs` | 提醒安排及精度变化 |
| `tests/MessagePurify.Core.Tests/TaskTransitionsTests.cs` | 手动字段、操作顺序及撤销期限 |

Task 1 定义时间契约；Task 2、3 只消费该契约。所有命名空间使用 `MessagePurify.Core`，测试显式 `using MessagePurify.Core; using Xunit;`。

```csharp
public enum TimeKind { Deadline, Start }
public sealed record TaskTime(
    TimeKind Kind, DateOnly Date, TimeOnly? Time, string TimeZoneId)
{
    public bool IsPast(DateTimeOffset now);
    public DateTimeOffset? GetExactInstant();
    internal DateTimeOffset AtLocal(DateOnly date, TimeOnly clock);
}
```

以上方法是待实现的签名，不是可直接编译的完整类；仅有日期时 `GetExactInstant()` 返回 null。`IsPast` 在保存时区判断日期，精确时刻用对应瞬时值判断。表格中的所有源码/测试路径均在实施时创建。

## Task 1：SDK、测试项目及时间模型

**Files:** 创建上述 `global.json`、`.gitignore`、两个 csproj、`TaskTime.cs`、`TaskTimeTests.cs`；更新 `tests/README.md`、`docs/HANDOFF.md`。

**Interfaces:** 输入原发送上下文确定的时区及已确认日期/时刻；产出 `TaskTime.IsPast(now)`、`GetExactInstant()`。

- [x] **Step 1：准备 SDK 并创建两个项目。** 从设计第 14 节的官方 .NET 来源安装 Windows x64 .NET 10 SDK；执行 `dotnet --list-sdks` 和 `dotnet --version`，确认当前选中 10.x，记录精确版本。然后在项目根目录执行：

```powershell
dotnet new globaljson --sdk-version (dotnet --version) --roll-forward latestPatch
dotnet new classlib -n MessagePurify.Core -o src/MessagePurify.Core -f net10.0
dotnet new xunit -n MessagePurify.Core.Tests -o tests/MessagePurify.Core.Tests -f net10.0
dotnet add tests/MessagePurify.Core.Tests reference src/MessagePurify.Core
dotnet restore tests/MessagePurify.Core.Tests --use-lock-file
```

仅删除自己新生成的 `Class1.cs`/默认示例测试，替换为真实测试；`.gitignore` 加入 `**/bin/`、`**/obj/`、`**/TestResults/`。检查模板包的许可和稳定版本，将生成的锁文件纳入提交。SDK 尚未可用时记录事实，不伪报编译成功。

- [x] **Step 2：写时间边界测试，再运行以确认缺少实现而失败。** 在 `TaskTimeTests.cs` 写：

```csharp
[Fact]
public void DateOnly_RemainsValidUntilNextLocalDay()
{
    var time = new TaskTime(TimeKind.Deadline,
        new DateOnly(2026, 10, 12), null, "China Standard Time");
    Assert.Null(time.GetExactInstant());
    Assert.False(time.IsPast(DateTimeOffset.Parse("2026-10-12T23:59:59+08:00")));
    Assert.True(time.IsPast(DateTimeOffset.Parse("2026-10-13T00:00:00+08:00")));
}
```

增加精确 `2026-10-12 18:00 +08:00` 的前一秒/到时边界；`Deadline` 与 `Start` 均检测有效性但保留不同类型。运行 `dotnet test tests/MessagePurify.Core.Tests --no-restore --filter TaskTimeTests`，预期因尚无实现失败，不能把配置或测试框架失败当作目标失败。

- [x] **Step 3：实现时间模型。** 实现下面的方法；无效时区或无法唯一解释的时刻向调用者报告校验错误，由后续待确认路径处理，不猜测正式时间。

```csharp
public DateTimeOffset? GetExactInstant() =>
    Time is { } clock ? AtLocal(Date, clock) : null;

internal DateTimeOffset AtLocal(DateOnly date, TimeOnly clock)
{
    var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    var local = DateTime.SpecifyKind(date.ToDateTime(clock), DateTimeKind.Unspecified);
    if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
        throw new ArgumentException("时间无法唯一解释，需要确认。");
    return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
}

public bool IsPast(DateTimeOffset now)
{
    if (GetExactInstant() is { } instant) return now >= instant;
    var zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
    var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
    return localDate > Date;
}
```
- [x] **Step 4：运行相关与全量测试。** `dotnet test tests/MessagePurify.Core.Tests --no-restore`、`node --test tests/project-rules.test.mjs`、`node tests/prototype-qa.mjs` 全部通过；记录实际版本、命令和结果。
- [x] **Step 5：更新交接并提交。** 写明仅核心时间模型可用；检查源码/测试/锁文件和文档范围，执行 `git diff --check`、暂存本任务文件、`git diff --cached --check`，提交 `feat: add core time model and test project`。

## Task 2：默认提醒、自定义提醒与精度失效

**Files:** 创建 `ReminderPlanner.cs`、`ReminderPlannerTests.cs`；更新交接。

**Interfaces:** 消费 Task 1 的 `TaskTime`；产出下面契约。不加入真实 Windows 通知，也不筛除过去节点，后续调度根据当前时间区分正常与补发。

```csharp
public abstract record ReminderChoice;
public sealed record DefaultReminder : ReminderChoice;
public sealed record DisabledReminder : ReminderChoice;
public sealed record HoursBefore(double Hours) : ReminderChoice;
public sealed record CalendarBefore(int Days, TimeOnly Clock) : ReminderChoice;
public sealed record ReminderPlan(
    IReadOnlyList<DateTimeOffset> Nodes, bool NeedsReview);
public static class ReminderPlanner
{
    public static ReminderPlan Plan(TaskTime time, ReminderChoice choice);
}
```

- [ ] **Step 1：写默认日期、精确时间和关闭提醒测试。** 在 `ReminderPlannerTests.cs` 写：

```csharp
[Fact]
public void DefaultExactTime_Uses24And2Hours()
{
    var time = new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12),
        new TimeOnly(18, 0), "China Standard Time");
    var plan = ReminderPlanner.Plan(time, new DefaultReminder());
    Assert.Equal(new[] {
        DateTimeOffset.Parse("2026-10-11T18:00:00+08:00"),
        DateTimeOffset.Parse("2026-10-12T16:00:00+08:00")
    }, plan.Nodes);
    Assert.False(plan.NeedsReview);
}
```

日期 `2026-10-12` 的唯一默认节点断言为 `2026-10-11T12:00:00+08:00`，且原 `time.Time` 仍为 null；`Start` 使用同样倒推但保留时间类型；`DisabledReminder` 始终无节点、无需失效提示。执行 `dotnet test tests/MessagePurify.Core.Tests --no-restore --filter ReminderPlannerTests`，确认缺少实现而失败。

- [ ] **Step 2：实现默认计算和校验。** 在 `ReminderPlanner` 内实现，节点不回写正式时间：

```csharp
private static IReadOnlyList<DateTimeOffset> DefaultNodes(TaskTime time) =>
    time.GetExactInstant() is { } instant
        ? new[] { instant.AddHours(-24), instant.AddHours(-2) }
        : new[] { time.AtLocal(time.Date.AddDays(-1), new TimeOnly(12, 0)) };

public static ReminderPlan Plan(TaskTime time, ReminderChoice choice)
{
    if (choice is DisabledReminder) return new(Array.Empty<DateTimeOffset>(), false);
    if (choice is DefaultReminder) return new(DefaultNodes(time), false);
    if (choice is HoursBefore hours)
    {
        if (!double.IsFinite(hours.Hours) || hours.Hours <= 0)
            throw new ArgumentOutOfRangeException(nameof(choice));
        return time.GetExactInstant() is { } instant
            ? new(new[] { instant.AddHours(-hours.Hours) }, false)
            : new(DefaultNodes(time), true);
    }
    if (choice is CalendarBefore calendar)
    {
        if (calendar.Days < 0) throw new ArgumentOutOfRangeException(nameof(choice));
        var node = time.AtLocal(time.Date.AddDays(-calendar.Days), calendar.Clock);
        if (time.GetExactInstant() is { } instant && node > instant)
            throw new ArgumentException("提前提醒不能晚于正式时间。", nameof(choice));
        return new(new[] { node }, false);
    }
    throw new ArgumentException("不支持的提醒规则。", nameof(choice));
}
```
- [ ] **Step 3：补充自定义变更测试并实现。** `HoursBefore(1)` 在 `2026-10-14 18:00` 得到 `17:00`；新时间只有 `2026-10-14` 时回退 `10-13 12:00` 且 `NeedsReview=true`。`CalendarBefore(1, 20:00)` 在日期或精确时间下均保持 `10-13 20:00`；关闭提醒在延期或精度变化后仍为空。小时须有限且正数，日历天数须非负；精确基准下不能配置基准之后的节点，日期精度允许当天钟点。用不合法输入测试检查明确校验失败。
- [ ] **Step 4：运行核心与全部已有测试。** 记录实际结果；精度失效仅生成计划决定，正式状态持久化和设置待确认 UI 在 M4/M7 实现。

```powershell
dotnet test tests/MessagePurify.Core.Tests --no-restore
node --test tests/project-rules.test.mjs
node tests/prototype-qa.mjs
```
- [ ] **Step 5：同步交接并提交。** 只暂存本任务源码、测试及交接，检查 diff，提交 `feat: add default and custom reminder planning`。

## Task 3：状态操作、字段保护和撤销适用性

**Files:** 创建 `TaskTransitions.cs`、`TaskTransitionsTests.cs`；更新交接。

**Interfaces:** 消费 `TaskTime`；产出不可变状态决定，之后由 M4 在数据库事务中保存。完成/归档/取消不删证据；小时/日历提醒由 Task 2 计算。

```csharp
public enum TaskState { Active, Completed, Cancelled, Archived }
public enum UndoKind { Complete, Ignore }
public sealed record TaskSnapshot(string Id, TaskState State, TaskTime Time,
    bool TimeProtected, long Version, long TimeVersion);
public sealed record UndoTicket(UndoKind Kind, DateTimeOffset ExpiresAt,
    long OperationSequence);
public sealed record TransitionResult(TaskSnapshot Current,
    TaskTime? SuggestedTime, bool Changed);
public static class TaskTransitions
{
    public static TaskSnapshot Complete(TaskSnapshot task);
    public static TaskSnapshot Cancel(TaskSnapshot task);
    public static TaskSnapshot Archive(TaskSnapshot task);
    public static TaskSnapshot RestoreArchived(TaskSnapshot task);
    public static TransitionResult ProposeTime(TaskSnapshot task, TaskTime proposed);
    public static TaskSnapshot AcceptTime(TaskSnapshot task, TaskTime proposed);
    public static TaskSnapshot RestoreForNewAction(TaskSnapshot task);
    public static UndoTicket CreateUndo(UndoKind kind, DateTimeOffset processedAt,
        long operationSequence);
    public static bool CanUndo(UndoTicket ticket, DateTimeOffset now,
        bool superseded);
    public static TransitionResult UndoComplete(TaskSnapshot current,
        UndoTicket ticket, DateTimeOffset now, bool superseded);
}
```

`RestoreForNewAction` 仅在调用者已验证可靠来源、关联和必要时间后调用；完成状态恢复待办，已归档状态不自动恢复，形成建议由 M4 处理。`OperationSequence` 将供持久化检查后续替代关系；纯函数不假装完成数据库的并发保障。

- [ ] **Step 1：写手动时间保护测试，运行确认缺少实现而失败。** 在 `TaskTransitionsTests.cs` 写：

```csharp
[Fact]
public void ProposedTime_DoesNotOverrideProtectedTime()
{
    var oldTime = new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12),
        new TimeOnly(18, 0), "China Standard Time");
    var later = oldTime with { Date = new DateOnly(2026, 10, 14) };
    var task = new TaskSnapshot("t1", TaskState.Active, oldTime, true, 3, 2);
    var result = TaskTransitions.ProposeTime(task, later);
    Assert.Equal(oldTime, result.Current.Time);
    Assert.Equal(2, result.Current.TimeVersion);
    Assert.Equal(later, result.SuggestedTime);
    Assert.False(result.Changed);
}
```

运行 `dotnet test tests/MessagePurify.Core.Tests --no-restore --filter TaskTransitionsTests`，确认缺少实现失败。

- [ ] **Step 2：实现状态转换与版本决定。** 只有实际状态/字段改变才增加事项版本；正式时间改变才增加时间版本。重复完成/取消/归档为幂等；`AcceptTime` 表示用户接受新值，更新正式时间及保护标记，`ProposeTime` 遇保护只输出建议，未保护时自动更新但不新增手动保护。取消不被手动时间保护阻挡，取消状态不被旧完成或旧恢复调用覆盖。先实现以下保护分支，再为各状态转换加入测试：

```csharp
public static TransitionResult ProposeTime(TaskSnapshot task, TaskTime proposed)
{
    if (task.Time == proposed) return new(task, null, false);
    if (task.TimeProtected) return new(task, proposed, false);
    var updated = task with {
        Time = proposed, Version = task.Version + 1, TimeVersion = task.TimeVersion + 1
    };
    return new(updated, null, true);
}
```
- [ ] **Step 3：补充撤销与后续变更测试。** `CreateUndo(Complete)` 为处理时刻+1小时，`Ignore` 为+24小时；`now < ExpiresAt && !superseded` 才适用，到期边界不适用。覆盖完成后延期再撤销采用最新时间、完成后取消不能撤销、已被新行动恢复后旧撤销不适用、归档新行动不自动恢复。纯函数只判断和生成决定，持久化操作 ID/旧版本冲突及实际撤销调用在 M4 测试。
- [ ] **Step 4：实现仅作用于完成状态的撤销并运行全部测试。** 当前快照的时间不回退；`superseded` 由后续持久化层根据操作顺序检查，核心只消费判定。

```csharp
public static UndoTicket CreateUndo(UndoKind kind, DateTimeOffset processedAt,
    long operationSequence) => new(kind,
        processedAt.AddHours(kind == UndoKind.Complete ? 1 : 24), operationSequence);

public static bool CanUndo(UndoTicket ticket, DateTimeOffset now, bool superseded) =>
    !superseded && now < ticket.ExpiresAt;

public static TransitionResult UndoComplete(TaskSnapshot current, UndoTicket ticket,
    DateTimeOffset now, bool superseded)
{
    if (ticket.Kind != UndoKind.Complete || current.State != TaskState.Completed ||
        !CanUndo(ticket, now, superseded)) return new(current, null, false);
    return new(current with { State = TaskState.Active, Version = current.Version + 1 },
        null, true);
}
```

```powershell
dotnet test tests/MessagePurify.Core.Tests --no-restore
node --test tests/project-rules.test.mjs
node tests/prototype-qa.mjs
```

明确记录状态决定已测试、数据库/通知/真实窗口未实现。
- [ ] **Step 5：同步交接并提交。** 检查范围及 diff，提交 `feat: add lifecycle and protected time decisions`。

## 第一阶段交付与接续

M1 完成后，交接文档记录实际 SDK 和依赖锁版本、源码/测试路径、执行命令、结果和剩余范围。用户可查看时间与状态测试结果，但此时还没有真实 QQ 监听或系统通知功能。下一步按总计划进入 M2，先制定本地规则与样本模块的详细计划，再实施；技术接口由开发者确定，无需重新让用户选择后端机制。
