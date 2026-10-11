# M4 第一段：消息收件箱与规则结果 Implementation Plan

> **For agentic workers:** 使用 executing-plans 执行，存储测试与消息处理适配可按下列契约并行。沿用用户已确认方案，不再询问执行模式。

**Goal:** 消息先保存到账号独立 SQLite，重启后继续提取并持久化可审查的规则结果。

**Architecture:** `AccountMessageStore` 用参数化 SQL 保存原消息、处理版本和完整规则输出；`LocalMessageProcessor` 负责置顶/账号/机器人范围检查及调用已有 Core 提取器。每条结果与处理进度在同一事务中提交，重复输入不覆盖原文，旧处理版本不能覆盖新结果。

**Tech Stack:** .NET 10.0.401，Microsoft.Data.Sqlite 10.0.9，SQLitePCLRaw.bundle_e_sqlite3 2.1.13，System.Text.Json，xUnit，实际临时 SQLite 文件。

**Spec:** [已确认设计](../specs/2026-10-10-windows-app-design.md)第 5、6.2、7 节；[总计划](2026-10-10-windows-app-implementation.md) M4、M5。M3 继续实施，本段可独立推进，不替代其剩余真实验收。

## Global Constraints

- 只读取置顶的群聊消息；非置顶、私聊及经用户确认排除的机器人不进入处理入口的数据库。
- 按 QQ 账号独立目录，消息唯一键包含账号、群与消息 ID；所有 ID 用字符串。
- 保存原发送时间与消息段；以原发送时间解释相对日期，不以处理时刻替代。
- OCR `SuggestedTime` 与 `Time` 分开；仅日期不制造钟点；本段保存规则候选，不创建正式事项或发送通知。
- 已保存证据不因取消置顶或重启删除。失败保留待处理消息，不重建损坏库；未知结构版本停止打开。
- 本段是 M4 基础子任务；正式生命周期、证据文件、迁移备份、提醒计划、清理、课程重查和真实接收宿主仍按总计划后续实现。
- 私人运行数据库放忽略目录或当前用户数据目录，不提交正文、账号、群名或 Token。
- 更新测试及文档，运行全部现有自动回归后创建 Git commit。

## Task 1：账号独立消息存储

**Files:** 新建 `src/MessagePurify.Infrastructure/Storage/MessageStorageModels.cs`、`AccountMessageStore.cs`、`tests/MessagePurify.Infrastructure.Tests/MessageStorageTests.cs`；更新 Infrastructure 项目及受影响依赖锁。

**Interfaces:**

```csharp
public enum MessageOrigin { Realtime, History }
public sealed record StoredMessage(SchoolMessage Message, MessageOrigin Origin,
    string BatchId, DateTimeOffset ReceivedAt, long ProcessingVersion,
    RuleExtractionResult? Extraction, DateTimeOffset? ProcessedAt);
// AccountMessageStore(string dataRoot, string accountId)
// AccountId, DatabasePath
// bool TryEnqueue(SchoolMessage message, MessageOrigin origin,
//     string batchId, DateTimeOffset receivedAt)
// StoredMessage? Find(MessageKey key)
// IReadOnlyList<StoredMessage> GetPending(int limit = 100, string? groupId = null)
// bool TrySaveExtraction(MessageKey key, long expectedVersion,
//     RuleExtractionResult extraction, DateTimeOffset processedAt)
```

- [x] 写临时库测试：重开后大 ID/时间/段/来源完整，重复不覆盖，账号隔离，候选精度和建议保留，多候选/跨群引用证据保留，错账号拒绝，旧版本不覆盖。用 SQL trigger 引发提交失败，检查仍 pending 且解除故障后可重试；未知版本和坏库原数据不动。
- [x] 运行 `& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --configuration Release --filter FullyQualifiedName~MessageStorageTests`，确认缺实现失败。
- [x] 实现 `accounts/<ASCII 数字账号>/messages.sqlite3`，结构版本 1、owner 元数据；参数化插入 `ON CONFLICT(account_id, group_id, message_id) DO NOTHING`；以原发送时间排序待处理队列。原消息和规则输出用 JSON 无损保存。
- [x] 保存结果使用事务和版本条件：

```sql
UPDATE messages SET extraction_json = $result, rule_version = $rules,
    processed_at = $processed, processing_version = processing_version + 1
WHERE account_id = $account AND group_id = $group AND message_id = $message
    AND processing_version = $expected;
```

- [x] 运行上述测试通过；每次操作单独连接，SQLite 负责写入串行化，版本校验处理两个工作进程的竞争。

## Task 2：本地接收与恢复处理

**Files:** 新建 `src/MessagePurify.Infrastructure/Processing/LocalMessageProcessor.cs`、`tests/MessagePurify.Infrastructure.Tests/LocalMessageProcessorTests.cs`。

**Interfaces:** `LocalMessageProcessor(AccountMessageStore store)`；`bool Ingest(SchoolMessage message, IReadOnlySet<string> pinnedGroupIds, RuleContext context, MessageOrigin origin, string batchId, DateTimeOffset receivedAt)`；`int ProcessPending(RuleContext context, int limit = 100)`。

- [x] 先写真实提取器测试，运行 `--filter FullyQualifiedName~LocalMessageProcessorTests` 确认缺实现失败；覆盖置顶/私聊/机器人、上下文错配、重开恢复、重复历史实时、未确认课程、正式任务候选及 OCR 建议、不同群范围和提取失败重试。
- [x] `Ingest` 在保存前检查账号、群、置顶及确认排除名单；`ProcessPending` 只取对应群，调用 `MessageRuleExtractor.Extract(message, context)`，提交时携带读取的 `ProcessingVersion`，成功计数，异常不伪记处理成功。
- [x] 运行两组新增测试通过，确认重启后的 `SuggestedTime` 不混入 `Time`，候选不是已创建正式事项。

## Task 3：验证及交付

- [x] 使用 `restore --locked-mode` 验证受影响锁文件；执行 Core/Infrastructure/Windows Release、OCR `pip check`/unittest、五文件 Node 和 demo 全量检查。
- [x] 同步需求的实现状态、交接的阶段/产物/实际命令/结果、总计划及测试说明。按项目要求人工核对上下文一致性；本机未找到 `maintain-context-hygiene` 时不删除项目文件。
- [x] 运行 `git diff --check` 与 `git diff --cached --check`，只暂存本轮文件，`git commit -m "feat: persist account-scoped message inbox and rule results"`，核对提交及工作区。

## 实施记录

- 开始前 Infrastructure Release 基线 78/78、退出码 0；本轮未启动真实 QQ 读取或 Windows 通知。
- 依赖与事务依据：[Microsoft.Data.Sqlite 10.0.9](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.9)、[微软事务文档](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)。仅锁定本次使用的版本，不声称最新。
