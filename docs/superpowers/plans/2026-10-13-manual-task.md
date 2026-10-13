# 手动新增任务实施计划

**目标：** 补录口头布置或漏识别的事项，保存标题、课程和正式日期；钟点可选，来源为“手动添加”，沿用正式任务的完成、优先级、编辑、历史与桌面规则。用户已确认需求并授权持续开发，统一真实验收后置。

**设计：** 复用账号工作区和任务 SQLite，不构造 QQ 原消息。任务 JSON 增加可选来源字段，旧任务默认为 QQ 来源。新增命令 `create-task` 使用版本 0、稳定的任务 ID 和操作 ID；重复相同请求只建立一个任务，冲突请求拒绝覆盖。

## 1. 存储和工作区

- [x] 在 `TaskStoreModels.cs` 增加尾部 `Source = "qq"`；新增 `TaskManualCreation.cs`，提供 `CreateManualTask(id, title, course, time, priority, operationId, now)`。
- [x] 标题、课程必填且不超过 256 字，正式日期必填，钟点可空；`priority=null` 使用现有自动分级，0–3 为手动覆盖。手动任务为 Active，时间受用户保护，证据为空。
- [x] `AccountWorkspace` 接入 `create-task`；`ApplicationItem` 投影 `source="manual"`、`group="手动添加"`；历史显示“手动添加任务”。
- [x] Infrastructure 回归：日期精度、来源重开兼容、幂等、冲突拒绝、账号隔离、优先级、完成/撤销/编辑、没有伪造证据。

## 2. 桥与正式页面

- [x] `AppBridgeProtocol` 注册命令，版本须为 0；验证标题、课程、日期、可选钟点、时间类型和可选优先级。现有 `AppWindow` 参数映射沿用。
- [x] 任务页提供新增入口，新增表单复用课程选项和自定义课程；自动分级为默认。创建成功打开新任务，列表与详情显示手动来源。
- [x] 草稿绑定起始账号，后台快照和课程更新保留内容；失败保留草稿和重试标识，保存中阻止重复提交。
- [x] Windows 桥及 Node 页面回归覆盖校验、来源、失败重试、课程、快照及账号切换。

## 3. 集成与交付

- [x] 扩展 `SmokeCheck`，用正式 WebView2 表单创建合成手动任务，检查重开、来源、日期精度、桌面刷新和完成。
- [x] 串行执行 Core、Infrastructure、Windows Release 测试；运行 Node 全量，发布自包含产物到 `.tools/published/app-manual-task/`，运行 Native Smoke。
- [x] 同步需求、交接、应用及测试说明和文档回归，核对实际报告；执行 `git diff --check` 并创建本项 Git commit。

本项不替换默认发布入口，不修改正在运行的正式事项。QQ 只读边界和两张桌面卡片规则保持，OCR 与统一真实验收后置。
