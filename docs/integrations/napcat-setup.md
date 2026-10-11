# NapCat 本机接入准备

当前已准备官方 **v4.18.37 Shell** 和 **Framework** 包。Shell 是无界面接管路线；同一账号不能同时登录普通桌面 QQ 和 Shell，不能据此承诺仍有可聊天的 QQ 窗口。本机已通过 Framework 在桌面 QQ 进程中加载 NapCat 并自动登录，真实 OneBot 只读接口限定范围通过；用户确认聊天列表和窗口正常显示，窗口打开期间接口复测通过。实时群事件及长期稳定性尚未验证。

## 在这台电脑启动

1. 本机官方入口实际报“QQ installation was not found in the registry”。QQ 文件存在于 `D:/QQ/QQ.exe`，已提供不依赖注册表的入口：双击 `D:\ai制作的软件\消息提纯\packaging\napcat\Start-NapCat.cmd`。它沿用官方包的五项启动环境及启动器，直接指定实际 QQ 路径，不申请提权。不要运行 `KillQQ.bat`。
2. 这是 Shell 入口。若要执行 Shell 真实探针，同一账号的普通 QQ 必须先退出；按控制台提示完成对应 QQ 登录。探针不填密码、不代替用户操作，也不把 Shell 当作桌面聊天窗口验收。
3. 打开控制台显示的本机 WebUI 地址，在网络配置添加“WebSocket 服务端”：启用，Host `127.0.0.1`，Port `3001`，消息格式 `array`，设置 Token，关闭 debug。保存后保持 NapCat 运行。
4. 本项目只读探针连接 `ws://127.0.0.1:3001/`，Token 通过 `MESSAGEPURIFY_NAPCAT_TOKEN` 环境变量传入。不把 Token 放入 URL、聊天、Git 或验证报告。

未改动 `D:/QQ` 文件。官方 Shell 入口因注册表缺失失败；替代 Shell 入口已通过 `-CheckOnly` 路径与依赖验证。此前 Shell 因同账号已在普通 QQ 登录而未完成接入，随后退出；本轮用户确认退出普通 QQ 后，通过 Framework 自动登录并完成只读接口探针，不需要重复扫码。

## Framework 桌面模式（已启动，继续验收）

`packaging/napcat/Start-NapCat-Framework.cmd` 是显式路径入口，会调用 Framework 包中的 `napimain.exe`、`napiloader.dll` 和 `nativeLoader.cjs`，不依赖 QQ 注册表。对应 PowerShell 脚本已实际启动当前 QQ `9.9.36-53644` 并自动登录，没有绕过登录或发送消息。当前 QQ 已由该入口运行，不要在已有进程上重复启动。

WebUI 6099 与 OneBot `127.0.0.1:3001` 均属于同一 QQ 主进程。只读探针取得 52 个群及一群一页的 7 条历史消息；用户确认从托盘打开后聊天列表和窗口正常显示。三次限时监听未观察到实时群事件，七天补漏尚未验收；启动日志有 `updateWindowSizeProperties` 的 TypeError，长期稳定性尚未验证。后续启动仍先正常退出当前 QQ，再用 Framework 入口重开；这与在两个进程中双开同账号不同。

## 可复现来源

- [官方 Shell 启动说明](https://doc.napneko.icu/guide/boot/Shell)
- [官方 Framework 启动说明](https://doc.napneko.icu/guide/boot/Framework)
- [官方 v4.18.37 发布](https://github.com/NapNeko/NapCatQQ/releases/tag/v4.18.37)，发布于 2026-10-10。
- 资产 `NapCat.Shell.zip`，35,880,104 字节。已下载到 `.tools/napcat/NapCat.Shell-v4.18.37.zip` 并解压。
- SHA256 已实查相符：`db39b28192714f8f54b3dcdccb84f9898be409faebb05da6627995b998517859`。
- 资产 `NapCat.Framework.zip`，36,723,625 字节。已下载到 `.tools/napcat/framework/v4.18.37/NapCat.Framework.zip` 并解压到 `package/`。
- Framework SHA256 已实查相符：`59d9538c1f1b66ff1269c362069fcd7d1b1d6793c35255ae80e699b91e37401c`。
- 包内 `launcher-user.bat` 从系统 QQ 安装项确定程序路径，调用 `NapCatWinBootMain.exe` 与 `NapCatWinBootHook.dll`；本项目不修改包内启动脚本。
- Framework 包内 `napiLoader.bat` 同样默认读取注册表；本项目的 Framework 入口改为直接调用 `napimain.exe` 并传入 `D:/QQ/QQ.exe`，避免把注册表缺失误判为 QQ 不存在。
- WebSocket 配置字段已核对[官方配置源码](https://github.com/NapNeko/NapCatQQ/blob/6797143b0446c00e34111930b22e6aa08deb053e/packages/napcat-onebot/config/config.ts)。根路径提供 API 与事件；`/api` 仅接口，不适合作为本探针事件连接。

QQ 探针调用 get_status、get_login_info、get_version_info、get_group_list 和 get_group_msg_history；默认一群一页、每页 20 条，并限时监听。没有发送消息功能，不声称补全七天。
