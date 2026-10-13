# 任务变更记录与卡片标记实施计划

> **For agentic workers:** 使用 subagent-driven-development 或 executing-plans 逐项执行；本轮已授权接续开发，UI 与后端按下述合同并行，统一验证后保存提交。

**Goal:** 将已有操作审计转为可读历史，并显示未查看的重要变更及待确认关联建议。

**Architecture:** 读取当前账号 tasks.sqlite3 的 operations，按 sequence 解析直接任务快照及 accept-review 包裹的 Task 快照；不新建历史库或修改数据库格式。AccountWorkspace 投影字段前后值及新增消息证据，用已有 ReadVersions 区分重要变更与纯证据补充。正式页面和桌面卡片共用可选投影字段。

**Tech Stack:** C#、SQLite、WPF/WebView2、现有 JavaScript 页面和 Node/C# 测试。

**Spec:** docs/requirements.md 第 5～7 节及 docs/superpowers/specs/2026-10-10-windows-app-design.md 的重要变更未读规则。

## 实际交付（2026-10-13）

审计投影、正式页面及桌面标记已实现。最终 Core 306/306、Infrastructure 463/463、Windows 206/206、Node 全量 95/95（正式页面 39/39、文档 37/37）通过；发布目录为 `.tools/published/app-change-history-final/`，Native Smoke 报告 `.tools/desktop-validation/change-history-native-final.json` 为 success=true、synthetic=true、21 项通过。新增场景使卡片出现滚动条 Thumb，旧检查取第一个 Thumb 误选；检查已改为按缩放手柄提示精确定位。未替换默认入口、未操作真实 QQ 事项；统一验收后置。

## 约束与接口

- QQ/NapCat 只读，不发送群聊或私聊消息；Windows 系统通知和声音取消，OCR 后置。
- 当前分支 codex/m3-integration-probes，基线 e56b1c7；不替换默认入口或正在运行的正式版本，不使用真实 QQ 事项测试。
- ApplicationItem 末尾新增可选 ChangeLabel、Changes、PendingChanges，旧页面数据仍可加载。
- ApplicationChange(Label, At, Version, Fields, Evidence)；ApplicationFieldChange(Field, Before, After)，Field 是中文字段名。
- ApplicationPendingChange(Id, Label, Due, Time, TimeKind, Reasons)，用于打开已有待确认项。
- 历史最新在前；重要字段或状态改变、新要求使事项重新未读；无变化操作及纯证据补充不制造新的变更标记。
- mark-read 仍携带实际展示的 item.version，旧依赖版本拒绝清除后续变更。未处理关联建议持续显示“待确认变更”。

## Task 1：审计读取和工作区投影

文件：TaskLifecycleService.cs、新增 TaskHistoryService.cs / TaskChangeProjection.cs、ApplicationModels.cs、AccountWorkspace.cs、AccountWorkspaceTests.cs。

- [ ] 先增加真实 SQLite 回归：编辑六字段后历史前后值、原证据保留、重开/账号隔离、无变化/重复操作不重复显示、accept-review 包裹快照、新要求和催交的未读差异、待确认建议处理及旧版本读操作拒绝。
- [ ] 运行 Infrastructure 专项，确认缺少 Changes/PendingChanges 等接口导致失败。
- [ ] 读取 operations 中 result_kind='task' 的行，按 sequence 排序并跳过相同版本操作；解析 before/after 的 Task 包裹及 action，保留新增证据键。
- [ ] 投影中文标签、字段前后值、北京时间所需时间戳及原消息；纯证据补充不提升重要未读版本，保留所有真实变更历史。
- [ ] 运行专项与完整 Infrastructure Release 回归。

## Task 2：正式页面与桌面卡片

文件：ui/app.js、app.css、DesktopCardProjection.cs、DesktopCardHost.cs、SmokeCheck.cs、application-ui.test.mjs、DesktopCardTests.cs。

- [ ] Node 先增加历史字段/消息转义、北京时间、旧数据兼容、卡片标记、待确认建议定位和实际查看读操作测试并确认失败。
- [ ] 列表和详情显示 ChangeLabel；原文前展示历史；待确认建议显示拟定日期及原因并打开原 review。
- [ ] 桌面签名纳入 ChangeLabel，卡片显示标记，取消事项沿既有规则移出。
- [ ] 增加 Native 合成历史/标记/查看清除闭环；运行页面回归和 Windows Release。

## Task 3：合并验证及保存

- [ ] 一次代码复核，修复确实影响本轮功能的问题。
- [ ] 串行运行 Core、Infrastructure、Windows Release；运行 node --test tests/*.test.mjs。
- [ ] 自包含发布 .tools/published/app-change-history/，运行 --smoke-test 并检查进程退出码及 JSON 报告。
- [ ] 同步 requirements、HANDOFF、应用及测试 README，更新项目进度回归；git diff --check 通过后创建 feat Git commit。

后续为手动新增及其余确定功能、合并拆分、备份恢复和桌面恢复；统一真实验收仍后置。
