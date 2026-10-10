# M3 真实集成探针验证记录

更新：2026-10-11（北京时间）。本记录只写实际运行结果；账号号、群名、消息正文、Token 和用户截图原文不写入。

## 结果总览

| 项目 | 实际结果 | 证据与边界 |
| --- | --- | --- |
| OneBot 协议与客户端 | 通过受控 loopback：46 项 | `tests/MessagePurify.Infrastructure.Tests` Release 46/46；含真实 ClientWebSocket、乱序 echo、事件交错、账号隔离、历史字段、并发释放回归。受控服务不代表真实 QQ 已连接。 |
| QQ 真实只读连接 | 未通过，依赖未就绪 | `.tools/m3-results/qq-not-ready-current.md`：运行 DLL，退出码 1，`connection-failed`；当时未监听 3001。NapCat WebUI 曾实际监听 6099，但登录状态返回同一账号已在普通 QQ 登录，随后 NapCat 进程退出。 |
| OCR 协议与合成图 | 通过：25 项 Python | `python -m unittest discover -s tests -p ocr_worker_test.py`；`pip check` 通过。真实 CPU 模型识别合成中文通知、长图日期、空白图和损坏图后继续；有限数值、代理码点 UTF-8 和离线模型回归已覆盖。 |
| 用户 QQ 截图 OCR | 真实截图文字通过，通知日期不验收 | 本机 worker 处理用户群列表截图：55 行、6 个红框群名均识别、原图字节未变。截图没有完整学校通知正文和日期，不能证明日期提取。全文只在 `.tools/m3-results/`。 |
| WPF/WebView2 隐藏桥 | 通过 | 发布后运行 `Run-Probe.ps1 -Mode Bridge`；报告 `C:\Users\Administrator\AppData\Local\MessagePurify\IntegrationProbe\results\Bridge-20261010-225528.json`。本轮重新 publish 并安装到含空格路径 `.tools/m3-install space`，运行 `Run-Probe.ps1 -Mode Bridge -WaitSeconds 20` 退出码 0，报告 `.tools/m3-install space/results/Bridge-20261011-013731.json`。WebView2 `155.0.4283.45`，echo→reply→ack、secure context、特殊文字保留均通过，PNG 已核对。 |
| Windows 通知 | 提权环境阻止 | 运行环境为高完整性会话；`NotificationProbe` 返回 `blockedElevated`，通知提交、系统呈现、热点击和冷启动点击不能标为通过。未修改 UAC。 |
| 桌面卡片 | 未完成人工操作 | WPF 产物、父窗/DPI/样式及拖动几何测试通过；未执行 Win+D、遮挡、鼠标拖动/缩放、多屏、Explorer 重启或睡眠恢复人工验收。 |

## 代码和自动化验证

- Core Release：295/295。
- Infrastructure Release：46/46。
- Windows 参数/桥/通知路由/桌面几何：44/44；IntegrationProbe 自包含 `win-x64` publish 退出码 0。
- OCR：25/25，`pip check` 无坏依赖。
- Node 文档、NapCat 启动路径和当前用户探针打包：`node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs`，15/15。
- demo 浏览器：107 项。
- `git diff --check`：退出码 0（仅显示工作区换行提示）。

## 当前 QQ 接入说明

官方 Shell 已核对并下载校验 v4.18.37。QQ 实际安装路径为 `D:\QQ\QQ.exe`，官方脚本依赖注册表而失败；项目提供 `packaging/napcat/Start-NapCat.cmd`，直接传入 QQ 路径。Shell 方式仍要求同一账号不要在普通 QQ 进程中同时登录。NapCat WebUI 6099 曾返回 HTTP 200；OneBot 3001 尚未启用，真实 QQ 只读探针因此保持未通过。

M3 仍为“正在实施”。要完成 QQ 真实项，需要用户在使用 NapCat 的 QQ 窗口时关闭普通 QQ，登录后在 WebUI 启用正向 WebSocket（`127.0.0.1:3001`、array 格式），再运行只读探针。该步骤不发送消息，也不把 NapCat Token 写入报告或 Git。
