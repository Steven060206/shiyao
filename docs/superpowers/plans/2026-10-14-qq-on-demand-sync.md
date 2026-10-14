# 按次同步与普通 QQ 恢复实施记录

**目标：** 点击拾要或“自动同步”执行一轮置顶群七天同步，然后恢复普通 QQ。

**已确认：** 普通 QQ 运行时允许软件自动退出它，完成同步后重新打开普通 QQ。沿用 NapCat、本地存储、游戏名单和只读边界。用户已取消三个固定时刻，只保留点击触发。

**实现：** 使用已有请求队列合并重复点击；每个请求拥有一个 `QqSyncSession`。先检查配置与文件、准备项目置顶桥，再退出指定路径 QQ，启动 Framework，执行 `RunOnceAsync`，最后恢复普通 QQ。正常结束不重连；失败及五分钟超时停止本轮，保留本地数据并尝试恢复。游戏开始取消在途操作、恢复 QQ，退出游戏后补一次。

## 1. 单次只读宿主

- [x] 为 loopback OneBot 增加连接断开观察和游戏中断测试。
- [x] `NapCatMessageHost.RunOnceAsync(CancellationToken)` 完成或 partial 后返回，协议/连接错误传播，不消费调度队列。
- [x] 执行 Infrastructure 宿主专项测试，43/43 通过；旧常驻接口仍用于历史验证。

## 2. QQ 会话

- [x] 新增 `QqSyncSession` 与可注入进程适配器：`StartAsync`、`RestoreAsync`、`DisposeAsync`。
- [x] 准备失败不关闭 QQ；进程操作核对路径、PID、启动时刻与本次启动父链；恢复幂等。
- [x] 执行 Windows 切换器专项测试，10/10 通过。

## 3. 软件入口和按钮

- [x] `AppRuntime.RequestSync()` 合并忙碌期间点击；成功/失败恢复后等待下一次请求，游戏延后保留一个请求。
- [x] `Program` 首次启动、单实例唤回及托盘打开接入；`sync-now` 桥仅接受可信本地页和空参数，不要求已有账号。
- [x] 页面新增“自动同步”，保留本地“刷新”；页面和卡片展示等待/切换/同步/恢复/失败/延后，正常断开不显示重连。
- [x] 编写并实际执行流程、桥和页面相关测试；专项通过。

## 4. 交付验证

- [x] 更新有效需求、交接及接入说明，依据用户定时选择同步最终规则。
- [x] 依次运行 Core、Infrastructure、Windows 全量，以及 `node --test tests/*.test.mjs`、`git diff --check`。
- [x] 自包含 Release publish，执行 `python tests/app_desktop_test.py --executable .tools/published/app-sync-session-check/MessagePurify.exe`。
- [x] 保护现有四库和设置，检查真实 QQ 启动/单轮/恢复；不确认、完成或编辑真实事项。
- [x] 发布到现有 `app-card-controls` 目录，保留入口和自启动开关，记录真实验证结果并创建 Git commit。

实际结果：Core306/306、Infrastructure690/690、Windows446/446、Node153/153，均退出0；隔离及正式发布版108项Native和真实父窗/styles检查通过。 两种QQ初始状态和正式软件启动/按钮真实同步均通过；PID43240保持运行，普通QQ已恢复。报告和数据保护见HANDOFF当前接续。实际游戏与长期稳定性仍未验收。
