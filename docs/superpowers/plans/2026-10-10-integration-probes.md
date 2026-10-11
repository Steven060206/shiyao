# 消息提纯：M3 真实集成探针 Implementation Plan

> **For agentic workers:** 按已确认设计实施，Windows、OneBot、OCR 独立委派。先写接口／协议回归再实现；各可交付任务验证后提交。用户已授权继续，不重新询问执行模式或已确认选型。

**Goal:** 在本机验证最小 WPF/WebView2、桌面附着、OneBot 只读连接、本地 OCR 和 Windows 通知，明确实际通过及需要登录／人工操作的未测试项。

**Architecture:** 三个独立探针复用既有 Core；OneBot 客户端只读，本地 OCR 通过 JSON Lines，Windows 宿主仅加载本地验证页面。探针不创建正式任务，不安装自启动，不把协议模拟或合成图像当作真实群消息验收。

**Tech Stack:** 项目 .NET 10.0.401、Windows x64、WPF、官方稳定 WebView2/Windows App SDK NuGet、ClientWebSocket、Python 3.12、RapidOCR/ONNX Runtime CPU；包与完整依赖写锁文件。

**Spec:** [已确认设计](../specs/2026-10-10-windows-app-design.md)第 6、8、9、10、11、12 节及[总计划](2026-10-10-windows-app-implementation.md) M3。

## Global Constraints

- 本轮从 `0c0893a` 进入 `codex/m3-integration-probes`；M1/M2 不回退，295 项核心、10 项文档、107 项 demo 检查作为基线。
- 已检测到 `D:/QQ/QQ.exe`，版本目录 `9.9.36-53644`。用户确认退出普通 QQ 后，Framework 已实际加载并自动登录，WebUI 6099 和 OneBot 3001 已监听；真实只读接口限定范围通过，不能替代实时群事件或完整历史补漏验收。
- NapCat 安装、启动和对应 QQ 登录沿用设计第 6.1 节；本应用连接用户就绪的服务，不改已有 QQ 程序或代填密码。可准备独立依赖启动包和配置指引。
- Shell 是无界面接管路线，同账号不能与普通 QQ 并行登录；官方 Framework v4.18.37 已在当前 QQ 中加载。用户确认聊天列表和窗口正常显示，窗口打开期间同进程的 OneBot 只读接口复测通过；实时群事件及长期稳定性尚未验证，不重复要求退出或扫码。
- 探针只调用 get_status、get_login_info、get_group_list、get_group_msg_history、get_version_info；不提供发送消息等写操作。群历史默认一群一页，实时仅统计消息段，不在 Git、控制台或报告记录账号号、群名、正文或 Token。
- QQ/群/消息 ID 全部按字符串消费，原发送时刻保留；引用只有明确 id 才构造证据，不能把 seq 猜成消息 id。
- OCR 只接收本地图片；模型显式指定本地路径，运行时不联网下载。保留位置、文字及置信度，输出不能确立正式日期。
- 原始本机运行报告放 `.tools/m3-results/`，交付报告经核对后放 `docs/verification/integration-probes.md`。秘密仅本地环境或忽略目录保存。
- 当前执行会话为高完整性；微软文档不支持提权应用的 AppNotification。通知注册、系统呈现、热/冷点击分别记录，提权时返回明确未测试原因，不改 UAC 或伪报成功。
- 桌面只在真实附着成功时显示；不置顶。Win+D、鼠标、多显示器/DPI、Explorer 重启和睡眠恢复逐项记录；本轮不主动重启 Explorer 或让电脑休眠。
- 可独立编译、协议测试、合成 OCR 和桥往返先完成；需要 NapCat 登录、真实学校截图或实际点击时只请求这项必要输入。M3 所有真实验收项完成前保持“正在实施”。

## Task 1：OneBot 读取客户端与 QQ 探针

**Files:** `src/MessagePurify.Infrastructure/MessagePurify.Infrastructure.csproj`、`NapCat/OneBotClient.cs`、`NapCat/OneBotMessageParser.cs`、`NapCat/OneBotModels.cs`；`tests/MessagePurify.Infrastructure.Tests/`；`tools/MessagePurify.QqProbe/`。

**Contract:** 客户端连 loopback WebSocket 根路径，以 Authorization Bearer 注入本地环境中的 Token；单接收循环将 echo 响应交给对应请求、群事件交给独立队列。解析器消费 JSON 和已核对账号，返回包含 Core `SchoolMessage` 的群事件或明确忽略／账号不匹配结果。CLI 消费 `--url`、`--listen-seconds`、`--history-groups`、`--history-count`、`--output`，输出脱敏汇总和非零失败码。

- [x] 编写乱序 echo／事件交错、失败 retcode、未知 echo、请求超时、连接关闭、数值大 ID、群/私聊、账号不匹配、text/image/reply 回归。例：数值 `9007199254740993` 必须得到同一十进制字符串；群事件插在响应之间不能丢失。
- [x] 执行缺实现失败，再实现只读客户端和解析器；保留原始协议字段但不把未知段当作可执行行动。
- [x] 实现 CLI：核对 status/login/version、读取群列表、一群一页历史并监听限定时间；历史使用返回游标，不对 message_seq 作算术。空群、未登录、无接口、认证或连接失败分别记录。
- [x] 用本机受控 WebSocket 服务运行真实传输回归（服务不接 QQ）；运行依赖未就绪路径，验证安全失败报告和退出码。
- [ ] NapCat 服务就绪后执行真实只读探针；未就绪保留“未接入”和必要启动步骤。全量验证、同步文档并提交。

## Task 2：离线 OCR 工作进程

**Files:** `ocr/worker.py`、`ocr/requirements.lock.txt`、`ocr/README.md`、许可说明；`tests/ocr_worker_test.py`、合成中文通知图及生成源；项目隔离环境 `.tools/ocr/`。

**Contract:** 每行请求 `{"id":"p1","imagePath":"本地绝对路径"}`；每行结果带同一 id、success、result 或 error；result 包含 text 和 lines（每行 text、box 四角位置、confidence）。初始化和运行日志只到 stderr。EOF 正常退出，坏 JSON、缺图、不可读图或缺模型结构化失败，后续请求仍可处理。

- [x] 写 stdlib 协议测试：坏 JSON、id／路径校验、缺文件、单图失败后继续、stdout 每行都是 JSON、原图不改动。先确认缺实现失败。
- [x] 创建项目 venv，安装官方 CPU 依赖并锁完整版本；实查随包模型，记录许可及版本。不得修改全局 Python 包。
- [x] 写 worker：初始化只读本地模型，顺序处理，用明确的错误类别报告，不将异常堆栈或日志混入 stdout。
- [x] 生成明确标注为 synthetic 的中文学校通知图，实际调用 ONNX CPU 引擎，验证文字、日期、位置和置信度。运行时阻止网络下载以验证本地模型使用。
- [ ] 真实学校群截图、长图／模糊图实测未提供时如实保留待验证；更新文档并提交已验证产物。

## Task 3：Windows 宿主、桌面与通知探针

**Files:** `src/MessagePurify.Windows/IntegrationProbe/`（WPF csproj、宿主、桌面／通知适配、本地 probe 页面）；`tests/MessagePurify.Windows.Tests/`；`packaging/probe/`；`docs/verification/integration-probes.md`。

**Contract:** `--bridge --report` 隐藏启动本地 WebView2，虚拟 HTTPS 主机 `probe.messagepurify.local`，页面发带 id 的 echo 请求，宿主只接受该来源／允许操作，报告往返结果后退出。桌面可视测试和通知发送由专用参数显式触发；通知提交和 activationObserved 独立，冷启动等候 NotificationInvoked。报告不包含真实用户数据。

- [x] 写桥来源、JSON 参数、白名单操作、通知载荷路由和桌面几何回归，先确认缺实现失败；锁稳定包与完整依赖。
- [x] 编译最小宿主并运行隐藏桥往返；输出真实 WebView2 版本与结果，截取 probe 自身可见 PNG 核对页面，禁止用静态 HTML 测试代替宿主运行。
- [ ] 实现可视桌面附着，检查父窗、窗口样式、焦点和任务栏状态；用户可操作 Win+D／拖动／缩放。未操作的 Explorer／睡眠／多屏项保持未测试。
- [x] 发布到独立当前用户固定目录，验证 AppNotificationManager 提权约束检查。已修正管理员组身份与令牌完整性混淆，独立中等完整性测试仍返回系统接口错误；普通／静音呈现和热／冷点击未通过，不能把 blocked 或失败当 passed。
- [x] 运行现有核心、协议、OCR、Windows 参数测试、Node、探针实际命令及 diff 检查；同步五项实测清单和剩余步骤。独立复核发现并修正 OneBot Dispose 竞态和 OCR 严格 JSONL 边界。

## 验证与完成条件

```powershell
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --configuration Release
& ./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py
node --test tests/project-rules.test.mjs
node tests/prototype-qa.mjs
git diff --check
```

三个代码任务可分别验证提交；M3 完成须五项真实验证都有具体环境、操作和结果。QQ 服务未就绪、真实截图未提供、用户操作未执行或通知受提权阻挡均不能勾为通过，独立工作继续推进。
