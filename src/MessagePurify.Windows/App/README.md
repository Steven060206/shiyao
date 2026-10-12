# 消息提纯：正式应用

当前为可运行的 Windows 应用。AccountWorkspace 使用现有账号独立 SQLite 保存事项；NapCatMessageHost 只读置顶群。正式页面没有样例事项，刷新后仍读取本地数据。

## 打开

发布后双击 [启动消息提纯](../../../packaging/app/Start-MessagePurify.cmd)。关闭主窗口会隐藏到托盘；双击托盘图标重新打开，托盘菜单可退出。再次启动会唤起现有窗口。

正式桌面卡片首段已实现并验证，当前自包含产物已发布到 `.tools/published/app-tray-fix/`，默认入口已指向托盘修复版。旧 app、app-reminders 和 app-desktop 目录保留；最新检查 QQ 仍运行，消息提纯进程未运行，未自动重开。已修复启动回调提前释放托盘图标的问题，图标可能位于任务栏右侧“显示隐藏的图标”中，名称为“消息提纯”。关闭隐藏、再次启动唤回同一窗口及托盘菜单退出均已实际通过。

当前产品方向为固定时间自动接入、读取置顶群并保存结果、结束后恢复普通 QQ，定时自动切换尚未实现。用户已停止 QQ 快捷方式改造，不要求从定制图标打开 QQ；旧安装脚本和备份保留历史，桌面 QQ.lnk 当前实际缺失。现有应用仍使用原持续连接实现：接口未就绪时显示接收暂停并重试，已有本地事项仍可查看。原 [Framework 直接入口](../../../packaging/napcat/Start-NapCat-Framework.cmd) 仅作为已有手动验证入口，要求先正常退出已有 QQ。首次成功连接确定账号，账号变化暂停接收，不自动切换账号。

## 当前可操作

- 浏览任务、待确认、通知和已完成记录；按标题、课程、来源群搜索，按课程和状态筛选。
- 查看原群消息正文；打开详情保存已读版本。
- 完成、1 小时内撤销、归档/恢复；忽略待确认、24 小时内撤销、拒绝建议。
- 填写或修改正式日期及可选钟点；接受关联建议同时核对原任务版本。图片建议“填入建议”只更新草稿，点击确认才保存。
- 在设置中确认置顶群的课程建议、修改名称或排除。课程确认影响后续消息，已有普通待确认仍需核对；完整历史关系修复在 M6。
- 浅色/纯黑深色主题、跟随系统、减少动画；当前有效外观同步给原生桌面卡片。界面偏好保存在本应用 WebView2 配置中，事项不使用浏览器存储。
- 第一版只通过任务、待确认两张桌面卡片提醒，不播放声音或发送 Windows 系统通知。正式详情保留日期编辑，删除定时提醒设置和节点预览；设置不再提供安静时段入口，显示桌面卡片实际挂载状态。
- 卡片打开事项后，主界面清除阻碍定位的筛选、打开对应详情并保存已读；快照尚未到达时等待账号快照。错误账号只显示提示，不查询其他账号；已有日期草稿保留。

## 桌面卡片

“提醒事项”和“待确认”分别显示当前账号的待办任务与未处理待确认项，首次左右排列。点击事项标题打开主界面对应详情；点击任务“完成”使用正式账号/版本操作，成功后移出卡片，主界面提供撤销。任务为空显示“暂无待办”，待确认全部处理后隐藏；普通通知继续留在主界面列表。

拖动卡片标题调整位置，用右下角“↘”手柄调整大小。位置和尺寸以 DIP 保存到 `%LOCALAPPDATA%/MessagePurify/App/desktop-layout.json`（schema 1），重新打开恢复；读取时按当前屏幕边界调整，坏文件或未知格式保留原文件并在界面提示。布局重载不自动覆盖原文件，用户拖动/缩放后保存。

卡片附着 Explorer 桌面，不抢焦点、不设 Topmost；主窗口关闭到托盘时卡片继续运行。临期/逾期和开始时间已过分别显示，只有日期时当天仍有效；主题与主界面同步。当前状态、查看/完成、主题及保存重开已通过 Native 合成和独立窗口检查；多屏、Explorer 重启和睡眠恢复尚未验收，本轮未做物理鼠标拖动。

普通学校群通知继续保存在应用的通知列表，列表名称不表示 Windows 系统通知。历史提醒库和内部配置、计划、台账保留数据兼容，正式应用不运行系统通知投递。桌面挂载暂不可用时，设置提示用户仍可在主界面查看任务和待确认；本机合成验证不替代真实 QQ 事项验收。

接收先保存原消息；历史分页期间暂缓路由，读取结束后按原发送时间处理。首次和重连尝试最近七天；空页、接口错误、缺游标或页数上限均显示未补全，已有内容保留。实时与历史按账号/群/消息 ID 去重。

## 后续功能

自启动、图片下载/OCR 队列、手动新增/完整编辑、通知转任务、完整变化标记、完整 M6 修复、合并拆分及备份恢复未完成。真实 QQ 课程确认后的任务端到端、桌面卡片的恢复、多屏与长期稳定性仍需实际验收；Windows 系统通知及声音已取消。应用不向 QQ 发消息。

## 开发和验证

在项目根目录执行：

```powershell
& ./.tools/dotnet/dotnet.exe restore src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --locked-mode
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-tray-fix
node --test tests/application-ui.test.mjs tests/application-launcher.test.mjs
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId <运行的消息提纯进程ID>
```

托盘脚本默认只检查图标，添加 `-ExitApplication` 会实际通过托盘菜单退出该进程，验证后可用默认入口重开。当前修复版 Windows Release 141/141、Node 七文件 63/63、原生合成 13 项、独立桌面检查和默认入口检查均退出 0；Infrastructure 未改，沿用上一段 375/375。修复版报告 `.tools/desktop-validation/tray-fix-native-smoke.json` 的 success=true、进程退出 0，覆盖快照、完成/撤销、显式日期确认、接受待确认、桥、数据重开、卡片数据/主题/详情/完成/布局及空待确认隐藏。`python tests/app_desktop_test.py --executable .tools/published/app-tray-fix/MessagePurify.exe` 确认父窗/style。这些检查使用临时合成账号；本轮另用正式数据只读复查真实 QQ，9 个置顶群、48 项待确认、0 个任务/通知，当次历史 complete，未代用户确认课程或日期。

Chrome 页面测试用独立临时配置，不控制正式窗口或 QQ，截图位于 `.tools/desktop-validation/`。`--read-only-check --seconds 20 --report <报告路径>` 使用正式数据目录，只记录状态、数量和安全错误码；真实连接不可用时退出 1。PowerShell 启动 WinExe 后须用 `Start-Process -PassThru -WindowStyle Hidden` 或配合 `-Wait`，等待结束后核对该进程的 `ExitCode` 和报告。

## 本地数据

正式数据位于 `%LOCALAPPDATA%/MessagePurify/App/`；账号目录下保存 messages/tasks/routing/reminders 四个 SQLite 库和 `workspace-settings.json`，`active-account.txt` 保存当前账号，数据根的 `desktop-layout.json` 保存两卡片布局。原三库 schema 1 不变，历史提醒库及新布局文件均为 schema 1。工作区设置/布局原子替换，历史提醒配置和投递台账保留，失败保留旧数据。未知账号或损坏数据不以清空重建解决。

应用进程只从 `MESSAGEPURIFY_NAPCAT_TOKEN` 环境变量读取凭据。启动脚本在环境变量缺失时读取本项目唯一启用的本机 3001 配置，并仅传入子进程环境；不发送到页面，不写入报告、文档或仓库。配置有歧义时停止，不猜选账号。此应用只连接 `ws://127.0.0.1:3001/`；WebView2 禁止外部导航、外部资源及页面权限。
