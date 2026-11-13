# 拾要温润界面改版验证

基准为正式版 `codex/m3-integration-probes` 的 `f60afbe`，实施分支 `codex/ui-redesign-warm`。用户已确认[实施计划](../superpowers/plans/2026-11-13-shiyao-ui-redesign.md)。本记录区分界面实现、合成验证、本机更新与公开发布。

## 已实现

- 主窗口铺满原生内容区，208/168px 导航、420/360px 详情，独立列表和详情滚动；设置使用完整内容区。
- 米白/墨绿浅色、纯黑主背景/暖黑面板深色；统一字体、控件、状态、焦点和减少动画。
- 原消息先于编辑字段；待确认默认展开，其他原文按实际六行高度折叠；原文展开与OCR分别保存会话状态。
- 待确认、新增、合并拆分固定底部提交，表单外按钮仍走单一 submit 链路；普通编辑保存紧邻字段。
- 批量选项在选择后展开，不清空统一日期，处理结果独立保留；原队列、超时、版本、账号和代次保护保留。
- 设置按钮定位四个分区、可见焦点、URL不变；图片关闭恢复打开入口焦点。
- 桌面卡片暖色、辅助文字至少12 DIP、原生标题栏配色；透明度、16 DIP圆角、吸附、布局和不抢焦点机制保留。
- 无公共API、桥接命令、数据库或备份格式改动。QQ仍只读；未触发真实QQ同步或处理真实事项。

## 自动验证

从项目根运行。日志及合成截图在 `.tools/ui-redesign/`，不会进入下载包。

| 实际命令 | 结果 |
|---|---|
| `node --check src/MessagePurify.Windows/App/ui/app.js` | 退出0 |
| `node --test tests/*.test.mjs` | 214/214，通过 |
| `node tests/prototype-qa.mjs` | 108项，通过；仅作为旧原型兼容检查 |
| `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --configuration Release --verbosity quiet` | 428/428 |
| 同参数 `tests/MessagePurify.Infrastructure.Tests` | 865/865 |
| 同参数 `tests/MessagePurify.Windows.Tests` | 522/522 |
| `.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py` | 25/25 |
| 同参数 `ocr_runtime_packaging_test.py` | 7/7 |
| 同参数 `release_packaging_test.py` | 9/9 |
| `git diff --check` | 退出0 |

正式页面新增回归先在旧实现上失败，再由新实现通过。覆盖实际几何尺寸、独立滚动、原文顺序/展开、草稿光标、固定按钮单次提交、批量折叠、设置地址与桥接、清除筛选以及图片关闭焦点。既有待确认矩阵、关联变更、课程冲突、并发、超时、合并拆分及备份测试继续通过。

验证版本命令：

```powershell
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --configuration Release --runtime win-x64 --self-contained true --output .tools/published/ui-redesign-validation --verbosity quiet
$env:DOTNET_ROOT = (Resolve-Path './.tools/dotnet').Path
& ./.tools/ocr/venv/Scripts/python.exe .tools/ui-redesign/run_native.py .tools/ui-redesign/native .tools/published/ui-redesign-validation/MessagePurify.exe
```

私有 `run_native.py` 原样执行 `tests/app_desktop_test.py --executable ...`，只在临时目录清理前保留合成报告和PNG。Native首轮因两处已删除breadcrumb节点的测试引用超时，改为page-title后149项通过；随后扩充最小原生宿主和最大化/还原检查，最终151项通过，success=true、synthetic=true，真实父窗SHELLDLL_DefView、两窗样式与显隐检查通过。新增DWM Get属性探测返回E_INVALIDARG，未将不支持的查询作为产品问题；标题栏由现有实际截图像素检查验证。

## 视觉与兼容范围

集中检查任务、待确认、设置的浅深主题，以及1000×581、1360×841、1920×1041内容区。Chrome模拟设备像素比1/1.25/1.5；常规、最小和大屏无横向溢出或按钮裁切。原文按实际六行高度判断；固定底部不遮盖末尾字段。Native合成检查真实Explorer父窗、标题栏像素、非置顶/不激活、透明度、圆角、吸附、显隐、布局、滚动和DPI恢复路径。

浏览器像素比例测试不等于实际修改Windows缩放或多屏切换。真实QQ图片下载、实际游戏、Windows登录、多屏恢复及长期运行仍是原有独立验收项。

Impeccable检测仅两项：米白配色是用户明确指定；原图对话框的img在校验同源URL后、打开前赋值，关闭后移除src。图片交互及来源校验回归通过，未绕过CSP或受信任页面限制。

独立设计复核确认视觉符合计划，提出原图打开期间快照替换入口会丢失返回焦点。新增测试先失败；按账号/代次/页面/事项/图片身份恢复替换后的入口，关闭按钮及Escape回归10/10通过，复核结论为ship（只针对这一修复的复核通过）。设计tokens由DESIGN.md和.impeccable/design.json保存。

[浅深主题预览](ui-redesign/README.md)。

## 交付记录

- 实现提交 `869ed00`。第一份干净自包含包240620847字节、2696条目；2673个非PDB文件、2177个OCR文件、官方NapCat原包和隐私文件排除核对通过。
- 中文空格解压路径中，151项Native、Setup合成CheckOnly/实际配置、空PATH与禁网络的4张OCR图片检查通过，未启动QQ。
- 干净Node检查暴露Escape的close事件异步时序：对话框open已变false时，焦点恢复事件尚在队列。回归改为等待真实焦点恢复（保留超时失败），不再以open=false当作焦点已恢复。产品代码未改；修正后再运行完整检查。
- 本机原程序回滚副本`.tools/ui-redesign/rollback/program`，四库与5份设置快照`.tools/ui-redesign/beta14-before-deploy`。实际更新与GitHub公开状态仍待完成后记录。
