# 拾要 Shiyao

从纷杂的 QQ 群消息中拾取要事。拾要在本机读取自己的置顶群，将需要处理的通知整理为任务和待确认事项，并在桌面显示两张卡片。

## 下载与使用

[下载最新测试版](https://github.com/Steven060206/shiyao/releases)。下载 **Shiyao-…-win-x64.zip**，完整解压到自己可写的固定文件夹；不要直接在压缩包里运行。

1. 使用 Windows 11 的 64 位电脑，安装并登录自己的桌面 QQ。
2. 双击 `Setup.cmd`，选择本机 `QQ.exe`，输入当前登录的 QQ 号。配置过程会安装拾要的桌面和开始菜单快捷方式。
3. 双击 `Start.cmd` 或桌面“拾要”。首次连接可能需要在 QQ 中重新登录；将想分析的群设为置顶。
4. 在设置里核对群与课程关系；到“待确认”核对消息、日期，支持逐条或批量加入任务、忽略。任务也可以手动添加。

程序已包含 .NET 运行时，不需要安装开发工具。界面需要 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)；Windows 11 通常已安装，若界面不能加载，请安装官方 Evergreen Runtime 后重试。

点击桌面入口或“自动同步”会临时退出普通 QQ，启动 NapCat Framework 读取消息，完成后恢复普通 QQ。托盘打开只显示窗口。关闭主窗口会留在托盘，托盘菜单“退出”才结束程序。游戏期间延后同步。

## 当前测试版

- 支持任务、待确认、普通通知、日期与优先级编辑、完成/撤销、合并/拆分、批量审核、搜索筛选、桌面卡片和本地备份恢复。
- 一条 QQ 原消息只生成一个完整待确认项；多个日期由你核对，加入任务后可按需手动拆分。升级会整理同一原消息尚未处理的旧拆分项，保留已确认、已忽略和手动拆分的结果。
- 同步保留已忽略和已确认的处理结果；所有消息按最新原消息时间倒序。已忽略消息位于独立“回收站”，无待办时隐藏提醒卡片，有新待办时自动显示。
- 只读取置顶群；应用不向 QQ 群或私聊发送消息。消息和事项保存在当前 Windows 用户的 `%LOCALAPPDATA%\MessagePurify\App`。
- 使用 NapCat Framework **v4.18.37**。现有真实 QQ 验证使用 **9.9.36-53644**；其他 QQ 版本需自行试用反馈。
- 图片下载与 OCR 尚未接入；消息中的图片标识不能代替原图。真实多屏/睡眠恢复、实际游戏和长期稳定性仍在验收中。
- 下载包不含开发者的聊天记录或登录资料。每位同学配置自己的 QQ；重新配置其他账号请使用新的解压文件夹。
- 配置后请保留文件夹位置；QQ 安装路径变化时重新运行 `Setup.cmd`。不要将配置后的 `napcat` 目录、账号数据库或导出备份上传到公开仓库。

## 从源码构建

安装 .NET SDK **10.0.401**（或兼容的 10.0 补丁版本），在 Windows 上执行：

```powershell
dotnet publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj -c Release --self-contained true -o .tools/published/release
```

从 [NapCat 官方 v4.18.37 Release](https://github.com/NapNeko/NapCatQQ/releases/tag/v4.18.37) 下载未配置的 `NapCat.Framework.zip`，再用 Python 3 创建同学下载包：

```powershell
python tools/build_release.py --publish-directory .tools/published/release --napcat-archive PATH_TO/NapCat.Framework.zip --output-directory .tools/releases --version 0.1.0-beta.2
```

测试命令：

```powershell
dotnet test tests/MessagePurify.Core.Tests -c Release
dotnet test tests/MessagePurify.Infrastructure.Tests -c Release
dotnet test tests/MessagePurify.Windows.Tests -c Release
node --test tests/*.test.mjs
python -m unittest discover -s tests -p "release_packaging_test.py"
```

## 许可证与依赖

本项目自己的代码采用 [MIT License](LICENSE)。NapCat 使用独立的 Limited Redistribution License（含非商业限制），不适用本项目的 MIT；下载包保留原始发行包及完整许可，在用户本地生成置顶群桥副本。[第三方说明](packaging/release/THIRD-PARTY-NOTICES.md)。

需求和实现进度见 [需求](docs/requirements.md) 与 [交接](docs/HANDOFF.md)。
