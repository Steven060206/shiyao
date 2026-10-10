# NapCat 本机接入准备

当前已准备官方 **v4.18.37 Shell** 包。Shell 会启动一个带 NapCat 的 QQ 窗口；同一账号不能同时登录普通 QQ 和这个窗口，但这不影响使用 QQ，只是接入期间要在 NapCat 窗口中聊天。

## 在这台电脑启动

1. 本机官方入口实际报“QQ installation was not found in the registry”。QQ 文件存在于 `D:/QQ/QQ.exe`，已提供不依赖注册表的入口：双击 `D:\ai制作的软件\消息提纯\packaging\napcat\Start-NapCat.cmd`。它沿用官方包的五项启动环境及启动器，直接指定实际 QQ 路径，不申请提权。不要运行 `KillQQ.bat`。
2. 如果普通 QQ 正在使用同一个账号，先自行退出普通 QQ，再启动入口。按控制台提示完成对应 QQ 登录；探针不填密码、不代替用户操作。NapCat 窗口就是可正常使用的 QQ 窗口。
3. 打开控制台显示的本机 WebUI 地址，在网络配置添加“WebSocket 服务端”：启用，Host `127.0.0.1`，Port `3001`，消息格式 `array`，设置 Token，关闭 debug。保存后保持 NapCat 运行。
4. 本项目只读探针连接 `ws://127.0.0.1:3001/`，Token 通过 `MESSAGEPURIFY_NAPCAT_TOKEN` 环境变量传入。不把 Token 放入 URL、聊天、Git 或验证报告。

本轮未改动 `D:/QQ` 文件。官方入口因注册表缺失失败；替代入口已通过 `-CheckOnly` 路径与依赖验证。用户曾扫码，但 NapCat 返回同一账号已在普通 QQ 登录；之后进程已退出，当前不能记录为 OneBot 已接入。下次启动前先关闭普通 QQ，接入完成后日常可使用 NapCat QQ 窗口。

## 可复现来源

- [官方 Shell 启动说明](https://doc.napneko.icu/guide/boot/Shell)
- [官方 v4.18.37 发布](https://github.com/NapNeko/NapCatQQ/releases/tag/v4.18.37)，发布于 2026-10-10。
- 资产 `NapCat.Shell.zip`，35,880,104 字节。已下载到 `.tools/napcat/NapCat.Shell-v4.18.37.zip` 并解压。
- SHA256 已实查相符：`db39b28192714f8f54b3dcdccb84f9898be409faebb05da6627995b998517859`。
- 包内 `launcher-user.bat` 从系统 QQ 安装项确定程序路径，调用 `NapCatWinBootMain.exe` 与 `NapCatWinBootHook.dll`；本项目不修改包内启动脚本。
- WebSocket 配置字段已核对[官方配置源码](https://github.com/NapNeko/NapCatQQ/blob/6797143b0446c00e34111930b22e6aa08deb053e/packages/napcat-onebot/config/config.ts)。根路径提供 API 与事件；`/api` 仅接口，不适合作为本探针事件连接。

QQ 探针调用 get_status、get_login_info、get_version_info、get_group_list 和 get_group_msg_history；默认一群一页、每页 20 条，并限时监听。没有发送消息功能，不声称补全七天。
