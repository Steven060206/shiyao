# 安静的个人助理视觉改版实施方案

> **For agentic workers:** 本方案只在用户批准具体设计后执行。实现阶段按任务逐项验证并提交；不得在审批前修改界面代码。

**目标：** 把“拾要”从功能完整的后台工具皮肤，统一为温润、安静、可长期使用的个人消息助理界面。

**架构：** 保留现有 WPF + WebView2、数据模型、桥协议、消息状态和桌面卡片行为，只替换界面视觉层与必要的展示结构。主窗口和桌面卡片共用同一组品牌色、状态色和文字层级；导航入口和操作语义保持不变。

**技术栈：** HTML/CSS/JavaScript、WPF/WPF DesktopCardHost、现有 Node 浏览器测试、.NET Windows Smoke、现有发布打包脚本。

**依据：** `docs/requirements.md` 的“视觉方向确认（2026-10-14）”及 `docs/HANDOFF.md` 的“当前视觉方向”。

## 全局约束

- 不修改 SQLite schema、消息同步、状态流转、桥协议、备份格式或 QQ 只读边界。
- 不删除任务、待确认、通知、已完成、回收站、设置中的任何功能入口。
- 浅色使用米白纸张、墨绿和少量暖金；深色保持黑色基底并增加暖黑/深墨绿层次。
- 桌面卡片保持半透明、圆角、可拖动、位置尺寸记忆和无待办隐藏规则。
- 不用新增图标库或字体依赖；优先复用现有图标资产和系统字体。
- 每个实现任务都更新测试并建立独立 Git commit；最终必须同步 GitHub 源码和 Release 包。

---

### Task 1: 建立统一视觉 Token 和应用外壳

**文件：**

- 修改：`src/MessagePurify.Windows/App/ui/app.css`
- 修改：`src/MessagePurify.Windows/App/ui/index.html`
- 测试：`tests/application-ui.test.mjs`

**结果：** 浅色、深色和桌面卡片可引用同一组语义色名；品牌、侧栏、顶部栏、详情背景和主要按钮形成稳定层级。

- [ ] 在 `app.css` 的 `:root` 与 dark theme 中定义 `--paper`、`--paper-raised`、`--ink`、`--muted`、`--moss`、`--brass`、`--line-soft`、`--danger`、`--warning` 等 token。
- [ ] 将硬编码的白色、纯黑色、绿色边框和重复阴影替换为 token；保持深色底为黑色基底，面板使用暖黑层次。
- [ ] 统一品牌图标、名称和副标题的间距与对齐，保留现有 `assets/shiyao.png` 和 `shiyao.ico`。
- [ ] 将 `index.html` 的结构类名整理为品牌区、处理区、记录区、偏好区、主内容区、详情区；不改变现有 `id`、`data-action` 和 WebView 查询入口。
- [ ] 为浅色/深色主题补一组页面静态回归断言，确认主要 token、品牌标题和导航入口存在。
- [ ] 运行 `node --test tests/application-ui.test.mjs`，确认结构和行为测试通过后提交：`feat: establish calm assistant visual tokens`。

### Task 2: 重做列表、详情和导航的信息层级

**文件：**

- 修改：`src/MessagePurify.Windows/App/ui/app.js`
- 修改：`src/MessagePurify.Windows/App/ui/app.css`
- 修改：`src/MessagePurify.Windows/App/ui/index.html`
- 测试：`tests/application-ui.test.mjs`

**结果：** 页面看起来像连续的阅读和处理工作台，减少后台表格感；所有现有操作仍通过原按钮和桥命令执行。

- [ ] 保留任务、待确认、通知、已完成、回收站、设置六个入口，只增加“处理/记录/偏好”分组标记或视觉分隔。
- [ ] 顶部只保留当前页面标题、同步状态和主要同步动作，降低重复面包屑的视觉权重。
- [ ] 列表行改为轻分隔结构：标题和最新消息时间为第一层，课程/来源为第二层，状态和截止信息为第三层；选中项使用纸张色块和墨绿标记。
- [ ] 将状态胶囊限制在真正需要强调的状态，普通元数据不再全部做成标签。
- [ ] 详情区调整为标题、来源/时间、主要动作、正式字段、原文证据的顺序；原文保留引用块视觉，不改变内容和滚动行为。
- [ ] 保留回收站、搜索、课程筛选、时间倒序、批量审核、撤销和设置表单的既有选择器与操作。
- [ ] 增加浅色、深色和空状态的页面断言，覆盖导航分组不改变入口、回收站与待确认分离、原文和撤销仍可用。
- [ ] 运行 `node --test tests/application-ui.test.mjs` 与 `node --test tests/*.test.mjs`，通过后提交：`feat: refine message workspace hierarchy`。

### Task 3: 统一桌面卡片的温润材质

**文件：**

- 修改：`src/MessagePurify.Windows/App/DesktopCardHost.cs`
- 修改：`src/MessagePurify.Windows/App/SmokeCheck.DesktopAppearance.cs`
- 修改：`tests/app_desktop_test.py`

**结果：** 桌面卡片与主窗口属于同一个产品，保留现有桌面行为和无待办隐藏规则。

- [ ] 把蓝灰背景、独立状态色和文字颜色改为与主界面一致的暖黑/深墨绿/米白/暖金语义色。
- [ ] 保留半透明、16 DIP 圆角、细边框、阴影、滚动条、拖动缩放、完成和查看详情功能。
- [ ] 让标题、来源、截止时间和完成按钮使用与主窗口相同的层级和强调色。
- [ ] 更新 Native Smoke，检查浅色/深色材质、空卡片隐藏、新待办恢复、隐藏卡片不参与吸附、父窗样式和布局文件不变。
- [ ] 运行三个 .NET Release 测试项目及 `python tests/app_desktop_test.py --executable <隔离发布目录>/MessagePurify.exe`，通过后提交：`feat: unify desktop card material`。

### Task 4: 整体回归、发布和 GitHub 同步

**文件：**

- 修改：`docs/requirements.md`
- 修改：`docs/HANDOFF.md`
- 产物：`.tools/published/app-visual-redesign/`、`.tools/releases/Shiyao-<version>-win-x64.zip`

- [ ] 运行 Core、Infrastructure、Windows Release 测试，记录实际通过数量。
- [ ] 运行 Node 全量、OCR unittest、`pip check` 和 release packaging 测试。
- [ ] 从干净自包含目录发布并运行 Native Smoke，查看浅色/深色页面与桌面卡片截图，执行 `impeccable detect --json` 一次。
- [ ] 保护真实四库和设置，正式实例通过托盘退出后发布，不强制终止进程；核对任务、忽略、确认、历史、布局和偏好未改变。
- [ ] 更新版本号，构建 Release ZIP，确认不包含聊天数据、账号数据库、Token 或登录材料。
- [ ] 推送 `main`，创建新的 prerelease，核对公开下载链接和附件 SHA256。
- [ ] 将验证命令、截图、发布地址、剩余审美问题和真实验收边界写入两份文档，运行 `node --test tests/project-rules.test.mjs` 与 `git diff --check`，提交：`release: publish calm assistant visual redesign`。
