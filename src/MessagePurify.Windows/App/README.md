# 消息提纯：正式应用

当前为可运行的 Windows 应用。AccountWorkspace 使用现有账号独立 SQLite 保存事项；NapCatMessageHost 只读置顶群。正式页面没有样例事项，刷新后仍读取本地数据。

最新真实事项验收（2026-10-12）：用户选定材料完成通知并确认按日期保存，已保存 2026-10-09、无钟点；真实确认、卡片 P0/已逾期/仅有日期、从卡片完成、主界面撤销、退出重开及卡片打开正确原文通过，最终恢复为待办。当前 1 个待办任务、46 项未处理待确认、1 项已忽略，主窗口停在新版设置页，显示同步状态，两张卡片和 QQ/接口保持运行。群/课程关系未批量确认；一个真实例子不替代全部课程关系、识别准确率及完整端到端验收。此前 48 项待确认是 review 类总数，包含 1 项已忽略。

## 打开

发布后双击桌面或开始菜单中的“消息提纯”即可打开；入口直接指向正式 `MessagePurify.exe`，参数为空。关闭主窗口会隐藏到托盘；双击托盘图标重新打开，托盘菜单可退出。再次启动会唤起现有窗口。脚本入口 [启动消息提纯](../../../packaging/app/Start-MessagePurify.cmd) 仍可作为辅助入口。

正式桌面卡片首段已实现并验证，上一版自包含产物位于 `.tools/published/app-tray-fix/`。本轮默认入口已指向 `.tools/published/app-launcher/`，新版本已自包含发布并通过 13 项合成 Native 验证；旧 app、app-reminders、app-desktop、app-tray-fix、app-scheduled-sync 和 app-sync-status 目录保留。上一段用户正常退出 QQ 后重开 Framework，新版真实首次后台同步与重开去重通过；当时主窗口隐藏，两张卡片及托盘可见。当前已完成一条真实任务验收，QQ/正式应用保持运行，主窗口展示新版设置页，3001/6099 监听且应用连接保持。便捷入口实际从桌面和开始菜单启动并保持单实例，隐藏主窗口后再次启动可唤回，真实只读报告和 UIA 时间检查通过。已修复启动回调提前释放托盘图标的问题，图标可能位于任务栏右侧“显示隐藏的图标”中，名称为“消息提纯”；上一版关闭隐藏、再次启动唤回同一窗口及托盘菜单退出均已实际通过。

Windows 登录自动启动已接入：默认在当前用户 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 注册 `MessagePurify.exe --background`，设置页可关闭或重新开启；后台模式隐藏主窗口但保留托盘和桌面卡片，手动入口仍会显示主窗口。设置文件保存在 `%LOCALAPPDATA%/MessagePurify/App/app-settings.json`。自动启动代码、桥接参数、异常回滚和偏好文件均已完成自动/合成验证；登录后的真实系统启动验收统一后置。

当前产品方向为 QQ/NapCat Framework 常驻，固定时间后台同步。2026-10-12 用户选择 A，已确认常驻方案，替代每次同步切换接入环境及结束后恢复普通 QQ 的旧安排；每天 08:00、12:00、20:00（北京时间）各同步一次，共三次，打游戏时禁止同步。游戏导致错过同步时，结束游戏后补同步一次，同一游戏期间错过多个时刻合并为一次补同步；中途开始游戏时暂停，切回桌面不算游戏结束。后台调度与常驻宿主已接入代码，首次启动的七天历史回溯在非游戏时执行，不吞掉下一个固定时刻；批次结束后保留连接，停止消息处理，空闲重连不触发新读取批次。关机/睡眠漏同步规则待确认。

用户要求将电脑现有游戏全部加入检测，并定期扫描电脑游戏、同步检测名单；默认应用启动及每日扫描一次，游戏期间延后目录扫描。`InstalledGameCatalog` 已接入自动发现、进程匹配和本地缓存 `%LOCALAPPDATA%/MessagePurify/App/game-catalog.json`；每分钟检查是否到期，实际目录扫描每 24 小时一次，进程状态约每秒采样。读取 Steam 库、游戏安装登记、WeGame/常见游戏目录、Epic 清单与 Minecraft 版本目录；只遍历游戏安装位置，不递归扫描整个硬盘。扫描失败保留已有名单并重试，未就绪时暂停同步。已扫描到本机 19 个游戏安装目录，Minecraft 整合包合为一项；报告 `.tools/game-validation/installed-games.json`。Minecraft 只在内存识别 Java 客户端启动标记，不保存启动命令或认证参数。启动器常驻与全屏状态不能直接算作正在玩游戏；真实 QQ 冷启动、首次同步与批次后连接保持已通过；各游戏实际启动/退出、资源占用与长期稳定性尚未验收。

QQ 快捷方式改造仍停止，不要求从定制图标打开 QQ；旧安装脚本和备份保留历史，已恢复的桌面 QQ.lnk 仍为普通 QQ.exe、参数为空。原 [Framework 直接入口](../../../packaging/napcat/Start-NapCat-Framework.cmd) 用于首次手动加载与真实验证，要求先正常退出已有 QQ，不能对运行中的 QQ 重复加载。接口未就绪时显示接收暂停并重试，已有本地事项仍可查看；首次连接确定账号，账号变化暂停接收，不自动切换账号。

## 当前可操作

同步状态显示已实现并验证：批次结束显示“等待下次同步”，正在同步、游戏延后、名单未就绪、未补全及连接故障分别显示。设置显示“上次成功同步（本次运行）”和“下次计划同步”，均按北京时间格式化。成功时间只在实际批次完整结束后更新，状态刷新、失败批次、游戏暂停和空闲重连不覆盖；该时间暂不跨进程持久化，无成功记录时明确显示空状态。下次计划复用每天 08:00、12:00、20:00 的调度规则，游戏期间仍延后。真实只读同步与正式 UIA 时间显示、两卡片和托盘已通过；自然固定时刻及实际游戏验收仍未完成。

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

同步接收先保存原消息；历史分页期间暂缓路由，读取结束后按原发送时间处理。首次非游戏时尝试最近七天，同步批次内重连尝试补漏，空闲重连只恢复连接；空页、接口错误、缺游标或页数上限均显示未补全，已有内容保留。实时与历史按账号/群/消息 ID 去重。发请求、收响应及落盘/路由事务前核对游戏，中途游戏暂停未提交的处理，退出后合并恢复读取，已保存内容不删除；真实游戏环境仍需验收。

## 后续功能

图片下载/OCR 队列、手动新增/完整编辑、通知转任务、完整变化标记、完整 M6 修复、合并拆分及备份恢复未完成。Windows 登录自动启动代码已完成，登录后的真实系统启动仍待统一验收。常驻接口真实冷启动和首次同步已通过；三个自然固定时间同步、实际游戏暂停/补同步和自动名单覆盖需验收，用户要求实际游戏验收延后；真实 QQ 课程确认后的任务端到端、桌面卡片的恢复、多屏与长期稳定性仍需实际验收。Windows 系统通知及声音已取消，应用不向 QQ 发消息。

## 开发和验证

在项目根目录执行：

```powershell
& ./.tools/dotnet/dotnet.exe restore src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --locked-mode
& ./.tools/dotnet/dotnet.exe publish src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --no-restore --configuration Release --self-contained true --output .tools/published/app-sync-status
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --no-restore --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release
node --test tests/project-rules.test.mjs
node --test tests/application-ui.test.mjs tests/application-launcher.test.mjs
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File tests/app_tray_test.ps1 -ProcessId <运行的消息提纯进程ID>
```

托盘脚本默认只检查图标，添加 `-ExitApplication` 会实际通过托盘菜单退出该进程。上一段 Infrastructure 418/418、Windows 141/141、Core 301/301、Node 八文件 69/69、OCR 25/25、demo 108 项通过；新版 publish、默认入口 `-CheckOnly` 与 Native 合成 13 项退出 0。本机游戏扫描报告列出 19 个安装目录。本轮真实检查两次退出 0，均为 52 个群、9 个置顶群、48 项待确认、0 个任务/通知、历史 complete、失败为空；接收 paused 表示批次结束，连接仍 connected 且 QQ online。正式两卡片父窗/style、关闭隐藏、托盘及单实例唤回实际通过，QQ/正式应用继续运行。报告在 `.tools/desktop-validation/resident-*.json`；自然固定时刻和游戏实机验收未完成。本轮 Node 71/71、Infrastructure 调度/名单 38/38 和实际宿主 29/29 退出 0，无生产代码修改或重新发布。

上一版托盘修复验证：Windows Release 141/141、Node 七文件 63/63、原生合成 13 项、独立桌面检查和默认入口检查均退出 0；当时 Infrastructure 未改，沿用 375/375。`.tools/desktop-validation/tray-fix-native-smoke.json` success=true、进程退出 0，覆盖工作区/桥/卡片合成操作；`python tests/app_desktop_test.py --executable .tools/published/app-tray-fix/MessagePurify.exe` 确认父窗/style。该段另用正式数据只读复查真实 QQ，9 个置顶群、48 项待确认、0 个任务/通知、当次历史 complete，未代用户确认课程或日期；这些历史结果不替代本轮真实常驻和游戏验收。

Chrome 页面测试用独立临时配置，不控制正式窗口或 QQ，截图位于 `.tools/desktop-validation/`。`--read-only-check --seconds 20 --report <报告路径>` 使用正式数据目录，只记录状态、数量和安全错误码；真实连接不可用时退出 1。PowerShell 启动 WinExe 后须用 `Start-Process -PassThru -WindowStyle Hidden` 或配合 `-Wait`，等待结束后核对该进程的 `ExitCode` 和报告。

## 本地数据

正式数据位于 `%LOCALAPPDATA%/MessagePurify/App/`；账号目录下保存 messages/tasks/routing/reminders 四个 SQLite 库和 `workspace-settings.json`，`active-account.txt` 保存当前账号，数据根的 `desktop-layout.json` 保存两卡片布局。原三库 schema 1 不变，历史提醒库及新布局文件均为 schema 1。工作区设置/布局原子替换，历史提醒配置和投递台账保留，失败保留旧数据。未知账号或损坏数据不以清空重建解决。

应用优先使用 `MESSAGEPURIFY_NAPCAT_TOKEN`；缺失时，启动脚本和应用只读项目内唯一启用的本机 3001 NapCat 配置，并仅向子进程传入凭据。配置损坏、无法读取或歧义时停止接入并显示安全错误码，不猜选账号。凭据不进入页面、报告、文档或仓库。此应用只连接 `ws://127.0.0.1:3001/`；WebView2 禁止外部导航、外部资源及页面权限。
