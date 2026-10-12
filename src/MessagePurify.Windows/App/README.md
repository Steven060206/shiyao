# 消息提纯：正式应用首段

当前为可运行的 Windows 应用。AccountWorkspace 使用现有账号独立 SQLite 保存事项；NapCatMessageHost 只读置顶群。正式页面没有样例事项，刷新后仍读取本地数据。

## 打开

发布后双击 [启动消息提纯](../../../packaging/app/Start-MessagePurify.cmd)。关闭主窗口会隐藏到托盘；双击托盘图标重新打开，托盘菜单可退出。再次启动会唤起现有窗口。

如果 QQ 消息接口未就绪，窗口显示接收暂停并自动重试，已有本地事项仍可查看。QQ 应从已准备好的 [Framework 入口](../../../packaging/napcat/Start-NapCat-Framework.cmd) 启动，该入口要求先正常退出已有 QQ。首次成功连接从登录接口确定账号，后续账号变化暂停接收，不自动切换。

## 当前可操作

- 浏览任务、待确认、通知和已完成记录；按标题、课程、来源群搜索，按课程和状态筛选。
- 查看原群消息正文；打开详情保存已读版本。
- 完成、1 小时内撤销、归档/恢复；忽略待确认、24 小时内撤销、拒绝建议。
- 填写或修改正式日期及可选钟点；接受关联建议同时核对原任务版本。图片建议“填入建议”只更新草稿，点击确认才保存。
- 在设置中确认置顶群的课程建议、修改名称或排除。课程确认影响后续消息，已有普通待确认仍需核对；完整历史关系修复在 M6。
- 浅色/纯黑深色主题、跟随系统、减少动画。界面偏好保存在本应用 WebView2 配置中，事项不使用浏览器存储。

接收先保存原消息；历史分页期间暂缓路由，读取结束后按原发送时间处理。首次和重连尝试最近七天；空页、接口错误、缺游标或页数上限均显示未补全，已有内容保留。实时与历史按账号/群/消息 ID 去重。

## 后续功能

提醒调度、变更 Windows 通知、正式桌面卡片、自启动、图片下载/OCR 队列、手动新增/完整编辑、通知转任务、完整 M6 修复、合并拆分及备份恢复未完成。应用不向 QQ 发消息。

## 开发和验证

在项目根目录执行：

```powershell
& ./.tools/dotnet/dotnet.exe restore src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --locked-mode
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app
node --test tests/application-ui.test.mjs tests/application-launcher.test.mjs
```

真实隐藏 WebView2 合成闭环检查使用发布后的程序 `--smoke-test --report <报告路径>`，临时合成账号验证快照、完成/撤销、显式日期确认及接受待确认。`--read-only-check --seconds 20 --report <报告路径>` 使用正式数据目录，只记录状态、数量和安全错误码；真实连接不可用时退出 1。PowerShell 启动 WinExe 后须用 `Start-Process -Wait` 或 `WaitForExit` 等待程序结束，再核对退出码及报告。

## 本地数据

正式数据位于 `%LOCALAPPDATA%/MessagePurify/App/`；账号目录下保存三个 SQLite 库和 `workspace-settings.json`，`active-account.txt` 保存当前账号。工作区设置原子替换，失败保留旧数据。未知账号或损坏数据不以清空重建解决。

应用进程只从 `MESSAGEPURIFY_NAPCAT_TOKEN` 环境变量读取凭据。启动脚本在环境变量缺失时读取本项目唯一启用的本机 3001 配置，并仅传入子进程环境；不发送到页面，不写入报告、文档或仓库。配置有歧义时停止，不猜选账号。此应用只连接 `ws://127.0.0.1:3001/`；WebView2 禁止外部导航、外部资源及页面权限。
