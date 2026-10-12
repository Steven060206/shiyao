# 正式桌面卡片 Implementation Plan

> **For agentic workers:** 按 subagent-driven-development 与 test-driven-development 执行独立组件；root 集成并验证发布产物。用户已选择 A，取消全部 Windows 通知弹窗和声音，并授权按该方向继续；沿用现有 `codex/m3-integration-probes` 分支。

**Goal:** 用正式账号任务与待确认数据驱动两张桌面卡片，支持查看/完成、自动更新、独立拖动缩放与重开布局，移除系统提醒设置界面。

**Architecture:** WPF 创建两张独立窗口，复用已验证的 SHELLDLL_DefView 附着和 DesktopGeometry，保持 WS_CHILD/NOACTIVATE，禁止 Topmost。AccountWorkspace 快照投影卡片，操作仍用原带账号及版本的事务入口；主界面 WebView2 桥同步主题与详情定位。位置以 DIP 原子保存，恢复时按当前 DPI/边界调整。

**Tech Stack:** 现有 .NET 10、WPF/WebView2、SQLite、Win32 API、xUnit、Node/Chrome；无新增外部依赖。

**Spec:** `docs/requirements.md` 第 6/7 节与 `docs/superpowers/specs/2026-10-10-windows-app-design.md` 最新桌面卡片规则。

**本轮结果（2026-10-12）：** 正式两张桌面卡片首段已实现并验证，发布至 `.tools/published/app-desktop/` 且默认入口已更新。Windows Release 141/141、Infrastructure Release 375/375、Node 七文件 63/63；App/Windows.Tests locked restore、最终 publish、Native 合成 13 项、独立父窗/style 检查及入口检查均退出 0。多屏、Explorer 重启和睡眠恢复尚未验收，M3 与 M8 总体仍未完成。

## Global Constraints

- 仅用两张桌面卡片提醒；Windows 弹窗、声音、自定义定时/恢复弹窗及安静时段入口取消，App 普通群通知列表保留。
- 已实现旧提醒计算、库、台账和探针保留为历史产物；不删除用户数据，不接入系统 sink/调度。
- 只读取 QQ 置顶群，不发送 QQ；不代用户确认真实课程或日期。
- 卡片只在桌面显示，不遮挡软件；提醒卡片无任务显示“暂无待办”，待确认为空则隐藏。
- 完成/取消/归档移出卡片；逾期仍待办高亮，开始已过显示对应标签，只有日期时当天有效。
- 用户操作使用当前账号与事项版本，陈旧操作拒绝并刷新；打开卡片项后进入对应详情，已读仍走正式入口。
- 旧程序和 QQ 保持运行，发布到新 app-desktop 目录，重开加载新版。

## Task 1：有效需求与历史范围同步

**Files:** requirements、HANDOFF、当前完整设计/总计划、`tests/project-rules.test.mjs`。

- [x] 先同步用户 A 成为有效决定，替换待确认与 Windows 下一步。
- [x] 更新系统通知相关行为/验收为卡片和主界面行为，保留历史验证事实。
- [x] 运行文档测试，避免原规则与新渠道冲突。

## Task 2：卡片数据投影与布局保存

**Files:** `Windows/App/DesktopCardProjection.cs`、`DesktopLayoutStore.cs`、对应 Windows.Tests 和项目引用。

**Interfaces:** `DesktopCardProjection.Build(ApplicationSnapshot?, MessageHostStatus)` 返回 `DesktopCardsSnapshot(AccountId,Tasks,Pending,ConnectionText,Error)`；`DesktopLayoutStore.Load(DesktopRect,double)` / `Save(DesktopCardLayout,double)`，layout 为 Tasks/Pending 两个 DesktopRect。

- [x] 写红测：仅 active 正式任务和 pending review，按优先级/日期/时刻稳定排序，账号未知不假装暂无待办。
- [x] 写真实临时 JSON 红测：左右默认、保存重开、DPI/屏幕变化 Clamp、损坏/未知 schema 保留旧文件。
- [x] 实现纯投影与原子保存，运行专项 Release 绿测。

## Task 3：主界面简化及卡片协议

**Files:** `App/ui/app.js`、app.css、README、application-ui.test.mjs；root 修改 AppBridgeProtocol/AppWindow。

**Interfaces:** 本地桥 `set-appearance` 参数恰好 `{theme:'light'|'dark',reducedMotion:boolean}`，无需账号；native 消息 `{type:'open-item',accountId,id}` 仅匹配当前快照后定位。state 可带 `desktop:{state:'attached'|'waiting'|'unavailable',error}`。

- [x] 实际 Chrome 红测后移除定时提醒表单/预览和安静时段入口。
- [x] 页面将有效主题/运动偏好发送原生卡片；卡片详情请求先等匹配快照，错误账号不定位，保留其他草稿。
- [x] 桥只接受受信来源、两个有效外观字段，旧未知字段拒绝；通过新旧浏览器与桥回归。

## Task 4：正式 WPF 桌面宿主

**Files:** `App/DesktopCardHost.cs`、Program/AppWindow/AppRuntime、App csproj、SmokeCheck、启动入口测试。

- [x] 将实际桌面检查加入合成原生入口，先验证旧 app-reminders 缺正式卡片失败。
- [x] 复用既有 Win32 挂载方式，创建两张独立卡片，捕获标题拖动/右下角缩放并保存布局。
- [x] 使用真实快照投影、滚动内容、查看详情/完成按钮与连接状态，近截止/逾期高亮；变化/时钟刷新时保持原布局。
- [x] 启动/隐藏主窗口仍保留卡片，退出释放卡片；挂载失败只在主界面显示原因，后台检测 Explorer 重建后重挂载的代码已写入，真实重启/恢复验收留待后续。
- [x] 原生合成检查真实父窗/style、数据、完成移出、待确认空时隐藏、主题、详情桥及保存重开；截图实际查看。

## Task 5：最终验证与保存

- [x] 运行受影响 Infrastructure/Windows Release、Node 七文件；Core/OCR/demo 未改动，沿用上段 301/301、25/25、108 项实际结果，具体 gate 按实际变更范围。
- [x] App/Windows.Tests locked restore、发布 `.tools/published/app-desktop`、默认启动入口检查、合成实际 WPF/WebView2/桌面闭环。
- [x] 同步 HANDOFF/测试README/计划结果，区分合成检查与真实用户桌面/多屏/长期验收。
- [x] 只读审查本轮差异及具体要求；必要修复完成后停止重复审查。
- [x] `git diff --check`、staged check、Git commit、干净工作区。

## 验证与修复记录

- 单次审查发现 P2 时钟标签不刷新：render signature 未含 `IsPast`，截止/开始到时或日期跨午夜时原任务版本与 P0 均可保持不变。三项专项先红 0/3；同一 `now` 的 `IsPast` 加入 signature 与 Render 后完整 Windows 141/141。DPI 重挂载改为只 Load/Clamp，不自动 Save；扩展既有五项损坏/未知格式测试，重复改变边界/scale 读取仍保留原文件，未新增计数。
- Node 七文件初轮 61/63：测试只模拟主题，未固定系统减少动画（本机 motion=true）；只修 CDP `no-preference` 测试设置后 63/63。最终重新 publish、Native 13 项及 Python 桌面检查均退出 0。
- 报告 `.tools/desktop-validation/native-smoke-final.json` success=true，两个卡片均 parentClass=`SHELLDLL_DefView`、child/noActivate=true、topmost=false；任务可见、待确认清空隐藏。`native-smoke-final.png` 和 `native-smoke-final-cards.png` 已查看。入口报告 `.tools/desktop-validation/launcher-check.json` Ready/AuthenticationReady=true，独立 PowerShell 进程 ExitCode=0。
- 本轮未做物理鼠标拖动、真实多屏/DPI/睡眠/Explorer 重启或长期稳定性验收；重挂载代码和模拟布局回归不替代人工验收。未重测真实 QQ 数量，沿用上段 9 个置顶群/40 项待确认两次结果，没有代用户确认课程/日期。完整变化标记仍待实施。
- 最终 Node 七文件 63/63、退出 0；文档同步时曾有一项接续顺序检查 26/27，补齐明确的 HANDOFF → requirements 规则后通过。diff/staged check 退出 0，本轮已用对应 Git commit 保存并核对工作区干净，最终提交标识以 Git 日志为准。实际命令和产物详见 HANDOFF 与 tests/README。

## 实施决定

- 继续使用已有分支和已确认桌面布局；用户已授权开发，普通实现细节不再请求选择。
- 两张卡片分别拖动/缩放，布局以 DIP 保存，不增加通知依赖；旧提醒库和内部 API 保留兼容，不继续作为当前产品入口。
- 本轮交付正式卡片首段；完整多屏、睡眠/Explorer长期稳定性、首次引导、自启动、OCR队列及课程修复仍须分别验收。
