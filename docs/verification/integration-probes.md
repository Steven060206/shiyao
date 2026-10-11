# M3 真实集成探针验证记录

更新：2026-10-11（北京时间）。本记录只写实际运行结果；账号号、群名、消息正文、Token 和用户截图原文不写入。

## 结果总览

| 项目 | 实际结果 | 证据与边界 |
| --- | --- | --- |
| OneBot 协议与客户端 | 通过受控 loopback：46 项 | `tests/MessagePurify.Infrastructure.Tests` Release 46/46；含真实 ClientWebSocket、乱序 echo、事件交错、账号隔离、历史字段、并发释放回归。受控服务不代表真实 QQ 已连接。 |
| QQ 真实只读连接 | 限定范围通过 | Framework 启动后运行 DLL，30 秒、60 秒、窗口打开后 60 秒探针均退出码 0；QQ 在线、账号核对、NapCat 4.18.37 / v11、群 52 个、历史群成功 1/1、历史消息 7 条。三次实时群消息均为 0，实时群事件尚未验收，不作为七天补漏验收。报告 `.tools/m3-results/qq-framework-current.md`、`qq-framework-events.md` 和 `qq-framework-desktop-open.md`。 |
| Framework 桌面模式 | 已加载，用户确认窗口正常 | 官方 v4.18.37 在 QQ `9.9.36-53644` 中实际启动，已加载 `napiloader.dll` 并自动登录；6099 与 localhost 3001 属于同一 QQ 主进程。用户从托盘打开后确认“聊天列表和窗口正常显示”。启动日志曾有 `updateWindowSizeProperties` 的 TypeError，保留问题记录，不能据此承诺长期稳定性。 |
| OCR 协议与合成图 | 通过：25 项 Python | `./.tools/ocr/venv/Scripts/python.exe -m pip check` 和 `./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py` 均通过。真实 CPU 模型识别合成中文通知、长图日期、空白图和损坏图后继续；有限数值、代理码点 UTF-8 和离线模型回归已覆盖。 |
| 用户 QQ 截图 OCR | 真实截图文字通过，通知日期不验收 | 本机 worker 处理用户群列表截图：55 行、6 个红框群名均识别、原图字节未变。截图没有完整学校通知正文和日期，不能证明日期提取。全文只在 `.tools/m3-results/`。 |
| WPF/WebView2 隐藏桥 | 通过 | 发布后运行 `Run-Probe.ps1 -Mode Bridge`；报告 `C:\Users\Administrator\AppData\Local\MessagePurify\IntegrationProbe\results\Bridge-20261010-225528.json`。本轮重新 publish 并安装到含空格路径 `.tools/m3-install space`，运行 `Run-Probe.ps1 -Mode Bridge -WaitSeconds 20` 退出码 0，报告 `.tools/m3-install space/results/Bridge-20261011-013731.json`。WebView2 `155.0.4283.45`，echo→reply→ack、secure context、特殊文字保留均通过，PNG 已核对。 |
| Windows 通知 | 提权环境阻止 | 运行环境为高完整性会话；`NotificationProbe` 返回 `blockedElevated`，通知提交、系统呈现、热点击和冷启动点击不能标为通过。未修改 UAC。 |
| 桌面卡片 | 未完成人工操作 | WPF 产物、父窗/DPI/样式及拖动几何测试通过；未执行 Win+D、遮挡、鼠标拖动/缩放、多屏、Explorer 重启或睡眠恢复人工验收。 |

## 代码和自动化验证

- Core Release：295/295。
- Infrastructure Release：46/46。
- Windows 参数/桥/通知路由/桌面几何：44/44；IntegrationProbe 自包含 `win-x64` publish 退出码 0。
- OCR：25/25，使用项目隔离解释器运行；`./.tools/ocr/venv/Scripts/python.exe -m pip check` 无坏依赖。
- Node 文档、NapCat 启动路径和当前用户探针打包：本轮运行 `node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs`，19/19、退出码 0（新增一项真实 QQ 报告范围回归，更新模式状态检查）。
- demo 浏览器：107 项。
- `git diff --check`：退出码 0（仅显示工作区换行提示）。

本轮为接入验证及文档同步；除 Node 与真实只读探针外，上述 Core、Infrastructure、Windows、OCR 和 demo 为此前已执行结果，没有在本轮重跑。

## 当前 QQ 接入说明

官方 Shell 已核对并下载校验 v4.18.37。QQ 实际安装路径为 `D:\QQ\QQ.exe`，官方脚本依赖注册表而失败；项目提供 `packaging/napcat/Start-NapCat.cmd`，直接传入 QQ 路径。Shell 是无界面接管路线，同一账号不能在普通 QQ 进程中并行登录。此前 Shell 探针因 3001 未就绪退出码 1；该历史结果已由本次 Framework 的真实只读通过结果更新。

用户确认退出普通 QQ 后，实际通过隐藏 PowerShell 启动 `packaging/napcat/Start-NapCat-Framework.ps1`。日志确认 Framework 初始化完成，进程加载 `napiloader.dll`、`major.node`、`wrapper.node`；WebUI 返回在线。正向 WebSocket 配置仅监听 `127.0.0.1:3001`，array 格式、debug 关闭，随机 Token 仅保存在本机忽略目录及探针子进程环境。原配置备份为 `.tools/m3-results/framework-onebot-before.json`，启动日志在同目录，不提交原日志或私人内容。

实际运行命令：`.tools/dotnet/dotnet.exe tools/MessagePurify.QqProbe/bin/Release/net10.0/MessagePurify.QqProbe.dll --listen-seconds 30 --output .tools/m3-results/qq-framework-current.md`，以及将监听改为 60 秒、输出分别改为 `qq-framework-events.md`、`qq-framework-desktop-open.md` 的同一命令；三次退出码 0。每次仅查询一群一页历史，未进行七天历史分页、断线补漏、正式持久化或任务生成。

用户随后确认从托盘打开 QQ 后聊天列表和窗口正常显示；QQ 主窗口句柄存在，主进程同时监听 6099/3001。启动时曾出现两次 `TypeError: undefined is not a function`，调用点为 `updateWindowSizeProperties`。该错误对长期使用的影响尚未验证；目前没有用户确认的窗口显示故障，不盲目替换加载器。未发送消息，也没有成功生成 QQ 窗口截图。

M3 仍为“正在实施”。下一步继续真实群事件、历史补漏、桌面人工操作和非提权通知验收；不重复要求已经完成的退出、扫码或登录准备。
