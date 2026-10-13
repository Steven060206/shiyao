# 桌面卡片恢复实施计划

> **For agentic workers:** 使用现有已授权分支分工实施，根代理串行 .NET 构建验证，一次有限复核，不重复询问选型或执行方式。

**Goal:** 桌面宿主暂时不可用、显示变化和恢复运行后，两卡片重新挂载并显示当前事项，布局回到真实可见工作区域。

**Architecture:** 原生桌面环境适配器捕获父窗、DPI和各屏工作区域；宿主定时及系统事件检查真实父窗/styles，失效时释放并重建卡片。布局schema1保持，自动调整只作用显示位置，用户只修改单卡片时保留另一张原存储位置。

**Tech Stack:** .NET10 WPF、Win32、既有SQLite工作区和schema1布局JSON。

**Spec:** `docs/superpowers/specs/2026-10-10-windows-app-design.md` 第10.1节及当前 requirements 两卡片规则。

## 已确认约束

- QQ/NapCat只读；无Windows系统通知、声音或Topmost替代卡片。
- Explorer内部桌面附着需真实验收；合成宿主丢失、重建和显示变化不宣称真实Explorer重启、实际睡眠或多屏硬件验收完成。
- 本轮不重启用户Explorer、不让机器睡眠、不改变真实屏幕配置、默认入口或真实账号数据；使用隔离合成账号和可注入环境适配器进行Native验证。
- 只刷新桌面当前状态，不新增睡眠漏同步政策，不恢复已完成/忽略状态或延长撤销期限。
- 继续schema1布局及备份兼容；损坏布局文件和临时屏幕缺失时保留原文件，只有用户明确拖动/缩放才保存对应卡片。

## 1. 多屏工作区域与布局

文件：新增 `DesktopPlacement.cs` 和 `DesktopPlacementTests.cs`；修改 `DesktopLayoutStore.cs`、`DesktopLayoutTests.cs`，Windows.Tests.csproj链接新纯算法。

接口：`DesktopPlacement.Fit(DesktopRect rectangle,IReadOnlyList<DesktopRect> workAreas)`；`DesktopLayoutStore.Load(IReadOnlyList<DesktopRect> workAreas,double scale)` 首项为主屏；`SaveCard(DesktopCardLayout current,double scale,bool pending)` 保存用户变动的一张，另一张保留旧DIP。

- [x] 失败回归：屏幕间空隙、任务栏、负坐标、显示器消失/回来、尺度变化、任一卡片手改不覆盖另一张自动回退位置、损坏布局不自动写。
- [x] 根代理运行Windows专项并核对失败，再实现：最大相交面积屏幕优先，无相交选最近区域；保持尺寸或夹入单屏工作区域，首次两卡片左右摆在主屏。
- [x] 旧单矩形Load/Save接口保持兼容，schema1不改，备份与旧Native保持通过。

## 2. 宿主恢复及系统变化

文件：修改 `DesktopCardHost.cs`、`Program.cs`，新增 `DesktopEnvironment.cs`、`DesktopSystemMessages.cs`及测试。

接口：`IDesktopEnvironment.Capture()` 返回可空 `DesktopEnvironmentState(Parent,Scale,Bounds,WorkAreas,Compatible)`；`IsAttached(child,parent)` 核对实际父窗、WS_CHILD、NOACTIVATE和非TOPMOST；默认Win32实现。`DesktopCardHost` 可选注入适配器；`RequestRecovery()` 只使当前显示/布局失效并刷新；`WatchSystemWindow(Window)` 在主窗口SourceInitialized上监听显示/工作区域/DPI/电源恢复及TaskbarCreated消息，释放时解绑。

- [x] 宿主真实Native失败回归：卡片脱挂但HWND仍存在；正确等待无父窗，恢复后新HWND与当前任务/空状态/主题/布局；原生操作失败不把错误坐标写入偏好。
- [x] 固定每两秒检查真实父窗和style、环境指纹（区域/DPI），只在变化时重读布局并强制当前快照刷新；捕获跨await变化，丢弃失效窗口旧结果。
- [x] SetWindowPos失败明确失败；拖动/缩放时宿主消失由本地异常处理取消交互并请求恢复，不让异常越过WPF事件。
- [x] 显示/电源恢复事件只触发当前数据刷新；主窗口与托盘仍可用，重挂载成功清除宿主错误；处理/保存权限继续核对账号、事项版本和工作区generation。

## 3. Native验证和收尾

文件：新增 `SmokeCheck.DesktopRecovery.cs`，修改 SmokeCheck报告；docs/HANDOFF、requirements、App/README及文档回归。

- [x] 可注入适配器包装真实父窗，合成暂不可用/返回、区域缩小恢复、scale变化、脱挂实际卡片；不重启真实Explorer。Native使用真实WPF窗口/桥、隔离账号保存重开和数据刷新，明确报告synthetic。
- [x] 根代理串行三个.NET Release全量、Node全量、Appbuild/publish和发布版Native；核对报告新写入与截图。
- [x] 一次有限复核，修具体证据问题；同步当前正文与真实未验收范围，人工上下文核对，diff检查，通过后对应Git commit及干净工作区。

## 实际结果

纯算法/布局/消息专项68/68；Core306/306、Infrastructure675/675、Windows390/390，Node139/139（页面77、文档43），均退出0。App build 0警告/0错误；发布 `.tools/published/app-desktop-recovery/`。Native报告 `.tools/desktop-validation/desktop-recovery-native.json` 新写入、success=true、synthetic=true、83项通过（原68+桌面15），三张截图已查看。旧宿主脱挂、旧缩放保存和旧读取错误提示有实际失败回归；几何夹具期望、native脱挂根父窗和WPF按钮模板时机已按证据修正。真实Explorer重启、多屏、睡眠与长期稳定性未验收。

命令与失败记录详见 docs/HANDOFF.md 当前接续；一次有限复核后只处理具体问题。未找到 maintain-context-hygiene 技能，已人工核对需求、交接、源码和实际报告。对应提交说明 feat: recover desktop cards after host and display changes，标识以Git日志为准。
