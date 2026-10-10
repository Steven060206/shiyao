# 消息提纯：M2 本地规则与样本基线 Implementation Plan

> **For agentic workers:** Use executing-plans task-by-task, with independent date, extraction and fixture work delegated in this session. Steps use checkbox syntax. 用户已授权继续开发，不重新询问执行模式或已确认选型。

**Goal:** 在已验证的 M1 核心上实现可重复运行的本地学校消息提取，分别输出行动、对象、课程、时间、来源及关联依据，建立合成样本基线。

**Architecture:** 纯 C# 模块消费账号／群／消息标识、原发送时刻、文字／图片 OCR／引用段和明确的已确认关系，返回候选决定；不修改正式任务。时间解析、消息规则及样本评估分开，后续持久化层负责去重、版本、用户修正和真正提交。

**Tech Stack:** .NET 10.0.401、C#、标准库 Regex／JSON、已有 xUnit；不新增 NuGet 依赖、网络或 AI 调用。

**Spec:** [已确认设计](../specs/2026-10-10-windows-app-design.md)第 2、7、8 节及[总计划](2026-10-10-windows-app-implementation.md) M2。

## Global Constraints

- .NET 命令使用项目 `.tools/dotnet/dotnet.exe`，现有 M1 的 87 项核心、107 项浏览器及文档检查继续通过。
- 所有 QQ／群／消息标识为字符串，已确认课程和发布者关系限于相同账号及群，未确认机器人不能排除。
- 相对日期以原发送时刻及保存时区解释；仅日期不造正式钟点，非法、冲突或未覆盖表达返回待确认原因。
- 图片／引用文字保留证据；OCR 日期只作建议，引用旧消息不能自行确立新的行动要求。
- 正式任务要求明确行动、可靠相关性和有效时间；自愿活动为通知，关键条件不明确为待确认，闲聊／明确无关消息可排除。
- 高影响延期／取消／新要求必须有可靠来源与明确目标关联。来源须为本群确认发布者，或已确认原通知发布者的明确引用续发；昵称和无出处转述不构成确认。
- 本阶段只生成候选，跨群相似文字不合并，正式去重、纠错保存、状态改变与通知在 M4 以后落地。
- 合成样本与真实独立验收分开，评估报告给出分子／分母、高影响错误和失败例子；不能用全部待确认掩盖自动覆盖率。
- 每个可独立交付任务验证、更新交接并创建对应 commit。

## 文件与公共契约

| 文件 | 职责 |
| --- | --- |
| `src/MessagePurify.Core/Rules/RuleModels.cs` | 消息证据、已确认上下文、现有任务及六类判定契约 |
| `src/MessagePurify.Core/Rules/MessageNormalizer.cs` | Unicode 宽度、空白与换行标准化，保留段类型、引用标识及时刻 |
| `src/MessagePurify.Core/Rules/MessageDateParser.cs` | 本地发送日期、绝对／相对日期与钟点，返回 Missing／Invalid／Ambiguous |
| `src/MessagePurify.Core/Rules/MessageRuleExtractor.cs` | 行动分段、疑问／否定／转述、相关性及安全变更候选 |
| `src/MessagePurify.Core/Rules/PriorityCalculator.cs` | P0～P3 日历边界与明确后果提升 |
| `tests/MessagePurify.Core.Tests/MessageNormalizerTests.cs` | 标准化与证据保留 |
| `tests/MessagePurify.Core.Tests/MessageDateParserTests.cs` | 原发送日期、年界、日期精度、歧义和非法表达 |
| `tests/MessagePurify.Core.Tests/RuleExtractionTests.cs` | 合成语义样本与六维／目标／时间回归 |
| `tests/MessagePurify.Core.Tests/PriorityCalculatorTests.cs` | 日历优先级与后果提升 |
| `tests/MessagePurify.Core.Tests/RuleFixtureRunner.cs` | JSON 样本加载、期待值比较与指标统计，测试调用同一逻辑 |
| `tests/fixtures/rules/synthetic.json` | 明确标记合成数据，覆盖基础及对抗场景 |
| `tools/MessagePurify.RuleBaseline/` | 无新依赖的 CLI，运行同一评估逻辑并生成 Markdown 报告 |
| `docs/verification/rule-baseline.md` | 实际运行结果、适用表达与尚未真实验收边界 |

契约使用 `MessagePurify.Core` 命名空间。消息键为 `MessageKey(AccountId, GroupId, MessageId)`；`SchoolMessage` 保留 SenderId、SentAt、Segments 和 IsPrivate。`RuleContext` 带相同账号／群、TimeZoneId、Now、ConfirmedCourse、TrustedSenders、ExcludedSenders、CourseExcluded 和 KnownTasks。每个 `RuleCandidate` 带行动、去向、课程、正式／建议时间、目标、优先级、六类依据、待确认原因及证据键。

```csharp
public static class MessageNormalizer
{
    public static SchoolMessage Normalize(SchoolMessage message);
}
public enum DateIssue { None, Missing, Invalid, Ambiguous }
public sealed record DateParseResult(TaskTime? Time, DateIssue Issue, string? Detail);
public static class MessageDateParser
{
    public static DateParseResult Parse(string text, DateTimeOffset sentAt,
        string timeZoneId, TimeKind kind);
}
public static class MessageRuleExtractor
{
    public static RuleExtractionResult Extract(SchoolMessage message, RuleContext context);
}
public static class PriorityCalculator
{
    public static int Calculate(TaskTime time, DateTimeOffset now, bool clearConsequence = false);
}
```

这些是接口说明；方法签名在实现文件中提供完整方法体。消息与上下文的账号／群不匹配时输出待确认，不复用另一群的可信关系。

## Task 1：消息契约、标准化与发送上下文日期

- [x] 写 `MessageNormalizerTests.cs` 和 `MessageDateParserTests.cs`。例：2026-12-31 23:30+08:00 的“明天18点交报告”应得到 2027-01-01 18:00；“10月12日交作业”保留 Time=null；“10月12日或13日”不自动选一日。
- [x] 运行过滤测试确认缺少实现；写入 RuleModels、Normalizer 和 DateParser。支持 YYYY-MM-DD／YYYY年M月D日／M月D日／M/D、今天／明天／后天、明确“本周／这周／下周”星期与最近未来的裸星期、下月明确日及上午／下午／晚上钟点；模糊“下周”“稍后”“今晚”无钟点不造日期时刻。
- [x] 增加绝对日期冲突、无效日期／钟点、时区及夏令时异常、跨年发送上下文、全角字符和图片／引用不丢失测试；无年份绝对日期按发送年解释，跨年需明确年份或相对表达，不按执行时刻猜。
- [x] 执行核心、文档、浏览器全量检查，更新阶段和真实结果；提交 `feat: add message normalization and contextual date parsing`。

## Task 2：本地行动、关联、变更决定和优先级

- [ ] 先写 `RuleExtractionTests.cs` 与 `PriorityCalculatorTests.cs`，运行确认缺少实现。短文本“明天交作业”不因长度被丢弃；“明天交作业吗”“不需要交作业”不形成自动任务。
- [ ] 实现文字行动分段及六维依据。提交／考试／强制参加为任务，自愿报名／活动为通知；报名截止和活动开始分别解析，不用报名日期代替开始。没有确认课程／对象或时间不明确时给出具体待确认原因。
- [ ] 只有原文可靠时间可正式使用，OCR 填 SuggestedTime；引用仅用于目标证据，引用内容不独立执行。多个交付物拆候选，格式说明保留在原候选；同一段多个冲突日期不择早。
- [ ] 实现明确取消、延期、催交和新要求的目标决定；来源和目标各自判定。可信新要求遇归档仍待确认；取消目标无需虚构新日期。重复／跨群文本只保留消息键，不在纯提取函数里变更状态或合并。
- [ ] 实现本地日历 P0／P1／P2／P3，今天／3 天／7 天边界无重叠，明确影响成绩／资格或逾期不收才提升；否定后果不提升。核心与全部已有检查通过后同步文档并提交 `feat: extract school actions and guarded change candidates`。

## Task 3：合成样本与可重复基线

- [ ] 写 JSON 样本，包含短消息、疑问、否定、转述、多行动、报名／开始、跨群重复、催交、延期、取消、完成新要求、归档新要求及图片建议。每项记录期待去向、行动、正式／建议时间和目标，不仅检查候选数量。
- [ ] 编写共享 fixture runner 与 xUnit 批量检查；评估 CLI 消费同一 runner，不复制分类规则。CLI 失败时非零退出；报告列出样本类别数量、正式任务准确率、含待确认召回、直接自动覆盖率、待确认占比与高影响错误数，分母为 0 时标明不适用。
- [ ] CLI 项目只引用 Core 并链接测试 runner 源文件，不添加新包；依赖锁写入。示例命令：`& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.RuleBaseline -- --output docs/verification/rule-baseline.md`。
- [ ] 执行 Release 核心全量、Node 文档、浏览器、CLI 和 diff 验证；独立只读复核新增规则与具体边界，同步交接、需求、设计及总计划实际状态。提交 `test: add synthetic rule corpus and baseline report`。

## 验证命令

```powershell
& ./.tools/dotnet/dotnet.exe restore tests/MessagePurify.Core.Tests --locked-mode
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release
node --test tests/project-rules.test.mjs
node tests/prototype-qa.mjs
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.RuleBaseline -- --output docs/verification/rule-baseline.md
git diff --check
```

M2 完成后进入 M3，先核对已有 QQ／NapCat 环境，再执行最小 WPF、OneBot、OCR 与通知探针；需要真实截图时只请求该项必要样本。
