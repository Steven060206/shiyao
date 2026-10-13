# 消息缓存清理实施计划

> **For agentic workers:** 沿用当前分支，由主代理串行集成、运行 .NET 验证；独立文件可并行实施。用户已授权继续，不再询问执行模式。

**Goal:** 安全清理超过最近 7 天分析窗口、已提取并路由且无证据或待处理引用的临时消息。

**Architecture:** 复用 AccountCandidateRouter 三库事务及 AccountWorkspace 串行入口。消息原发送时刻严格早于 now.AddDays(-7) 才进入候选，先全量收集长期证据和待处理消息，再递归保护保留消息的引用链。删除与成功结果在路由库同事务提交，路由回执继续保存；工作区收件先检查回执，已清理消息重复读取不重新入库。

**Tech Stack:** C#、Microsoft.Data.Sqlite、WPF/WebView2、现有 JavaScript 页面。

**Spec:** docs/superpowers/specs/2026-10-10-windows-app-design.md 第 5.2 节；docs/requirements.md 当前顺序。

## 约束

- 全部 task_evidence、全部状态 reviews、notices.Source 和候选 Evidence 长期保护；历史、设置、撤销、合并及拆分关系不删除。
- 无提取、无回执、处理版本与回执不同、边界及窗口内消息均保留，不按当前群排除状态跳过保护扫描。
- 从所有保留消息递归保护 Quote.Reference 和提取候选 Evidence，完整账号/群/消息键去重。
- 保留路由回执；AccountWorkspace.EnqueueCore 对已有回执直接去重，避免旧消息入库后永远待处理。
- 清理前、删除中和提交前检查暂停回调；异常及暂停回滚全部删除，不覆盖上次成功结果。
- 不接入尚不存在的图片文件缓存，不宣称物理磁盘释放量，不增加 VACUUM。
- 本轮只用隔离合成数据验证，不清理真实 QQ 数据，不更换默认入口；QQ/NapCat 只读。

## Task 1：存储事务

Files: AccountCandidateRouter.cs、AccountCacheCleanup.cs、新建 CacheCleanupTests.cs。

Interfaces: Processing.CacheCleanupResult(AccountId, CompletedAt, Cutoff, DeletedMessages, RetainedOldMessages, RemainingMessages)；router.CleanMessageCache(now, canContinue = null)、router.ReadCacheCleanupResult()、router.HasRouted(MessageKey)。

- [x] 先编写失败回归，主代理运行专项。
- [x] 实现三库保护扫描、引用闭包、删除和成功结果持久化。
- [x] 验证 7 天精确边界、任务及所有 review 状态、通知、引用链、待处理/旧处理版本、账号隔离、回滚及重开。

## Task 2：工作区、自动入口和设置页

Files: ApplicationModels.cs、AccountWorkspace.cs、NapCatMessageHost.cs、AppBridgeProtocol.cs、ui/app.js；工作区/宿主/桥/页面相关测试。

Interfaces: ApplicationSnapshot 新增可选 CacheCleanup、CacheCleanupError；workspace.CleanMessageCache(now, canContinue = null) 返回成功结果。clean-cache 使用 accountId/id/operationId/version=0，其 id 固定为 cache，不接受其他业务字段。

- [x] 先编写收件去重、工作区结果、桥及页面失败回归。
- [x] 快照读取持久化成功结果，清理失败保留该结果并显示独立错误。
- [x] 历史同步批次处理结束后自动清理；暂停阻止提交，普通清理失败仍记录同步成功并显示清理错误。
- [x] 设置页增加清理入口、7 天及证据保留说明、时间及删除条数；请求沿用账号隔离和忙碌状态，不丢失编辑草稿。

## Task 3：集成与交付

Files: SmokeCheck.cs、项目文档回归、docs/HANDOFF.md、docs/requirements.md。

- [x] Native 合成验证设置页清理、旧临时消息删除、正式证据保留、重读去重、结果重开。
- [x] 一次有限代码复核，修复有证据的问题。
- [x] 串行运行三个 .NET Release 测试、App build/publish；运行 Node 全量、发布版 Native Smoke，查看截图。
- [x] 核对需求、交接及实际产物；更新执行结果，git diff --check，通过后创建对应 Git commit。

下一步：备份恢复、桌面恢复，最后统一真实 QQ 验收。
