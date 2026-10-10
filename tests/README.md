# 项目检查

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

Node 文档测试包含 10 项检查：协作规则同步、小任务规则优先级、交付文件和文档链接、整体设计及核心开发状态、M2 计划与阶段、全部设计验收场景的计划覆盖、新版演示的保存方式和真实集成边界，以及 QQ 登录与 OneBot 连接分别记录。

浏览器测试包含 107 项检查，覆盖原有主题／动画、排序、桌面卡片、图片确认及通知转任务，并新增：首次引导、已读行为、完成 1 小时／忽略 24 小时撤销及刷新、归档恢复、搜索与筛选、手动任务与开始时间、自定义提醒、延期及精度失效、人工字段冲突、新行动要求、取消、图片放大及 OCR 折叠、通知内容／声音意图预览、设置与桌面布局保存。图片日期不再单独勾选，检查没有二次勾选框、未提交建议不确立正式时间、按钮直接提交、修改后保存及刷新、仅日期和图片通知提交。详情栏的回归检查在 1100×800 窗口中确认长内容不被裁切，使用 Chrome 原生鼠标滚轮上下滚动及原生点击固定确认按钮，核对任务／通知的去向和错误提示可见性。课程相关 16 项检查覆盖共享选项、两处自定义入口、刷新和重置、重复／空白名称、取消恢复选择、未提交表单保留、待确认／手动任务使用自定义课程及筛选。测试在截止边界显式验证撤销资格，并通过过期动作检查防止越界恢复；未实际等待 1 小时或 24 小时。动画与系统主题测试显式模拟媒体偏好，不依赖本机 Windows 设置。

真实应用已创建 .NET 核心测试项目，OCR 测试项目尚未创建。后续按 [总计划](../docs/superpowers/plans/2026-10-10-windows-app-implementation.md) 将新增测试命令补入本文；计划中的示例和验收目标不计为已运行测试。

需要 Node.js 22 或更新版本和 Chrome。默认使用 `C:/Program Files/Google/Chrome/Application/chrome.exe`；其他安装位置可通过环境变量 `CHROME_PATH` 指定。浏览器检查使用独立临时配置，不访问个人浏览器会话。普通测试不会覆盖预览图；仅在主动更新预览时运行 `node tests/prototype-qa.mjs --screenshots`，生成课程选择、待确认、主界面及桌面卡片的六张预览。

这些检查验证 C# 核心、文档和浏览器原型，不代表真实 QQ、OCR、Windows 系统通知或桌面驻留已通过测试。
