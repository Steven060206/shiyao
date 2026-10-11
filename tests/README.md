# 项目检查

当前置顶群规则回归（2026-10-11）：Core Release 296/296、Infrastructure Release 78/78、Windows Release 57/57、OCR 25/25 且 `pip check` 通过、Node 五文件 29/29（文档 20、桥 4）、demo 108 项，均退出码 0。新增 `PinnedGroupProbeTests.cs` 覆盖混合群/无置顶、未知或错误名单不回退、账号错配、实时/分页取消置顶与重连更换范围；桥使用真实 `{get, call}` 结构和数字 `topFlag`，检查其他读取接口不变、未知标记/失败/不完整快照停止。真实 9 个置顶群两轮 18/18 到达日期边界、去重 341、重连 1/1、退出码 0；未观察到本次真实实时群事件，不替代动态置顶或长期稳定性验收。下文旧全群结果为规则变化前的历史记录。

在项目根目录运行全部现有测试：

```powershell
& ./.tools/dotnet/dotnet.exe restore tests/MessagePurify.Core.Tests --locked-mode
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --no-restore
& ./.tools/dotnet/dotnet.exe restore tools/MessagePurify.RuleBaseline --locked-mode
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.RuleBaseline --no-restore -- --output docs/verification/rule-baseline.md
node --test tests/project-rules.test.mjs
node tests/prototype-qa.mjs
```

核心测试使用 .NET 10 SDK，已在 `.tools/dotnet` 安装 Windows x64 10.0.401；`global.json` 锁定版本，Core、Core.Tests 和基线 CLI 各有 `packages.lock.json`。如另一本机已有对应 SDK，可把命令中的可执行文件改为 `dotnet`。M1 的 87 项 C# 测试包含 10 项时间、33 项提醒和 44 项状态检查，覆盖日期当天与次日、精确到时、Deadline／Start、时区和夏令时异常、默认 24／2 小时及前日中午、自定义小时／日历、延期重算、精度失效回退、关闭保留及非法提前节点；状态检查覆盖手动值保护、版本变化、幂等、取消优先、归档／恢复、新行动恢复、完成 1 小时／忽略 24 小时撤销边界，以及延期后撤销保留最新时间。测试用显式时间输入，不依赖执行机器的当前时刻。

M2 消息与日期部分现有 2 项消息标准化和 102 项日期解析检查。覆盖原发送时刻、保存时区、跨年、日期精度、冲突／范围、非法数值、夏令时、未覆盖表达，以及图片、引用及消息证据保留。

M2 Task 2 新增 85 项行动提取与 13 项优先级检查，核心合计 289 项通过。覆盖短行动、多交付物、报名／开始时间、疑问／否定／转述／条件、限定对象、可信变更来源及唯一目标、OCR 建议、仅引用、账号／群隔离、混合来源引用和排除目标、完成／归档／取消的新要求，以及 P0～P3 日历边界和明确后果提升。

M2 Task 3 新增 65 条合成样本、6 项 fixture/指标回归测试和 RuleBaseline CLI。实际 Release 核心全量为 295 项（含 6 项 fixture）；CLI 报告完整匹配 65/65、正式 Task 准确率 23/23、需行动召回 47/47、直接自动覆盖 27/47、待确认占比 23/57、高影响错误 0。无效 CLI 参数退出码 2。合成样本不替代真实 QQ 群消息验收。

Node 文档测试包含 19 项检查：协作规则同步、小任务规则优先级、交付文件和文档链接、整体设计及核心开发状态、M2 计划与阶段、全部设计验收场景的计划覆盖、新版演示的保存方式和真实集成边界、QQ 登录与 OneBot 连接分别记录，以及 Shell/Framework 接入边界；真实 QQ 只读结果、用户窗口反馈、启动异常、实时群事件的限定验证和未验收的长期稳定性/补漏范围须同步。新增只读政策检查：不向 QQ 群或私聊发送消息，测试和任务提醒采用本机 Windows 通知。

浏览器测试包含 108 项检查，覆盖原有主题／动画、排序、桌面卡片、图片确认及通知转任务，并新增：首次引导、已读行为、完成 1 小时／忽略 24 小时撤销及刷新、归档恢复、搜索与筛选、手动任务与开始时间、自定义提醒、延期及精度失效、人工字段冲突、新行动要求、取消、图片放大及 OCR 折叠、静音通知内容预览、设置与桌面布局保存。图片日期不再单独勾选，检查没有二次勾选框、未提交建议不确立正式时间、按钮直接提交、修改后保存及刷新、仅日期和图片通知提交。详情栏的回归检查在 1100×800 窗口中确认长内容不被裁切，使用 Chrome 原生鼠标滚轮上下滚动及原生点击固定确认按钮，核对任务／通知的去向和错误提示可见性。课程相关 16 项检查覆盖共享选项、两处自定义入口、刷新和重置、重复／空白名称、取消恢复选择、未提交表单保留、待确认／手动任务使用自定义课程及筛选。测试在截止边界显式验证撤销资格，并通过过期动作检查防止越界恢复；未实际等待 1 小时或 24 小时。动画与系统主题测试显式模拟媒体偏好，不依赖本机 Windows 设置。

M3 已创建 OneBot、Windows 探针和 OCR 测试项目。实际验证命令为：

```powershell
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --configuration Release
& ./.tools/dotnet/dotnet.exe test tests/MessagePurify.Windows.Tests --no-restore --configuration Release
& ./.tools/ocr/venv/Scripts/python.exe -m pip check
& ./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py
node --test tests/project-rules.test.mjs tests/probe-packaging.test.mjs tests/napcat-launcher.test.mjs tests/napcat-framework-launcher.test.mjs
node --test tests/pinned-groups-bridge.test.mjs
```

通知权限修正阶段的回归计数：Core 295、Infrastructure 46、Windows 45、OCR 25、打包/启动 Node 四个测试文件共 21、demo 107，均通过，见[集成探针记录](../docs/verification/integration-probes.md)。本次点击入口修复后 Windows Release 为 57/57，新增 URI 载荷、参数限制、字段校验、静音载荷和 `-Embedding` 回归。Node 覆盖文档 16 项、通知资源提取/安装/入口打包 3 项以及两个 NapCat 启动入口各 1 项。受控未就绪 QQ CLI 退出码 1；Framework 前四次真实只读探针退出码 0、实时群消息 0，第五次 180 秒探针收到 1 条真实群事件、退出码 0。用户确认聊天列表和窗口正常显示，窗口打开期间接口复测通过。Windows 通知探针的用户实际点击通过，用户确认出现事项窗口；后续静音提醒显示/点击和桌面基本操作已由用户确认通过；完整历史补漏、长期稳定性和桌面恢复/多屏仍单独验收。

`tests/notification_protocol_activation_test.py --executable <已注册的探针绝对路径>` 检查 Windows Shell 的 URI 分发和合成事项窗口，不发送提醒；报告中的 `controlled-shell-` Probe ID 与用户实际通知点击区分。`tests/notification_cold_activation_test.py` 保留受控 COM 回调检查，并模拟 Windows 追加的 `-Embedding` 参数。这些受控检查均不替代用户实际点击验收。

2026-10-11 取消声音提醒后的实际回归为 Windows 57/57、四文件 Node 22/22（文档 17 项）及浏览器 108 项；所有通知载荷固定静音，浏览器检查覆盖移除声音开关、旧 `sound=true` 偏好不恢复、其他设置保留及考试/作业/活动的静音预览。

本轮静音提醒的显示和实际点击已通过。历史分页新增 `HistoryPagingTests.cs`、参数和方向回归后，Infrastructure Release 为 60/60；覆盖乱序页、原样游标、超 Int64 数值 `real_seq`、窗口过滤、重叠、短页继续、空页、无进展、页上限、游标不足、整页解析原子性及失败重试后继续下一群。带游标请求的 `reverse_order=true` 先用失败测试定位，再由真实 QQ 对照确认方向；默认单页和实时/历史去重仍有回归。

上一轮历史分页扩展的完整自动回归：Core 296/296、Infrastructure 60/60、Windows 57/57、OCR `pip check` 和 unittest 25/25、四文件 Node 23/23（文档 18 项）、demo 108 项，均退出码 0。真实全群分页与测试分开：41/52 到达边界、窗口内去重 1098，11 群持续 1200，CLI 正确退出 1；不把接口失败当作空页或完整补漏。报告及实际命令见[集成验证记录](../docs/verification/integration-probes.md)。

本轮受控重连新增 `ReconnectProbeTests.cs`，Infrastructure Release 67/67；验证重新读取最新页、重建游标、跨连接/历史/实时去重、模拟新消息补回、账号变化停止、取消后不连接及历史失败保留。参数支持 0～2 次主动重连、默认 0；只断开测试连接，QQ 保持运行。真实两轮账号核对 2/2、重连 1/1，历史 2206、去重 1098、重复 1108；未观察到重连后新消息，11 群每轮持续 1200，CLI 退出 1，不能替代正式自动恢复或跨进程补漏。

本轮全部自动回归：Core 296/296、Infrastructure 67/67、Windows 57/57、OCR 25/25 且 `pip check` 通过、四文件 Node 24/24（文档 19 项）、demo 108 项及 `git diff --check`，均退出码 0。

`python tests/desktop_attachment_test.py --executable <已发布探针的绝对路径>` 在真实 Explorer 中启动合成桌面测试，核对实际父窗为 `SHELLDLL_DefView`、窗口可见标志、子窗/不激活/非置顶样式、物理边界及没有成功状态窗。原版 `Progman` 父窗和多余状态窗均会使回归失败。可增加 `--mouse`，在桌面未被覆盖时实际拖动扩大后的右下角手柄，验证宽高增加 60/30 像素、左上角不移动；结束后释放按钮并恢复指针，测试进程在 5 秒后正常退出。运行鼠标回归时先用 `Shell.Application.MinimizeAll()` 切到桌面，最后 `UndoMinimizeALL()` 恢复窗口。该测试只使用合成卡片，不操作 QQ，不替代用户人工验收。

需要 Node.js 22 或更新版本和 Chrome。默认使用 `C:/Program Files/Google/Chrome/Application/chrome.exe`；其他安装位置可通过环境变量 `CHROME_PATH` 指定。浏览器检查使用独立临时配置，不访问个人浏览器会话。普通测试不会覆盖预览图；仅在主动更新预览时运行 `node tests/prototype-qa.mjs --screenshots`，生成课程选择、待确认、主界面及桌面卡片的六张预览。

这些检查验证 C# 核心、协议、OCR 工作进程、Windows 探针参数和浏览器原型；真实 QQ、Windows 系统通知呈现/点击和桌面人工驻留仍不由自动测试替代。
