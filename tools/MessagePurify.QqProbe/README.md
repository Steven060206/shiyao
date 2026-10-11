# QQ 只读探针

此工具连接已经启动并登录的本机 NapCat OneBot 11 正向 WebSocket。默认根地址 `ws://127.0.0.1:3001/`，NapCat 须配置 `messagePostFormat=array`。不连接 `/api`，该路径不会推送事件。

只提供 `get_status`、`get_login_info`、`get_version_info`、`get_group_list`、`get_group_msg_history` 和项目的 `messagepurify_get_pinned_groups` 读取桥。只读取置顶的群聊消息：默认读取第一个置顶群的一页历史（最多 20 条），随后监听 30 秒；`--history-groups all` 只表示所有置顶群。每页及重试前、每个实时群事件解析正文前重新核对置顶状态，重连重新筛群；名单缺失、格式错误或接口失败立即停止，不回退到全群。非置顶事件直接排除，不统计消息段或保存正文；没有置顶群时不请求历史。取消置顶不清理既有事项或证据。本探针不建立课程或任务。

置顶桥源码位于 `packaging/napcat/pinned-groups.mjs`，由项目 Framework 入口生成独立运行副本并注册，保留官方原文件。真实 QQ 已加载，置顶使用原生 `topFlag` 数字 0/1，校验快照成功、排序与变更列表长度一致及不足 10000 上限；缺标记或未知取值必须失败，不能根据最近会话顺序推断置顶。2026-10-11 当前规则下 52 个已加入群中只读取 9 个置顶群，两轮历史 18/18 到达日期边界、去重 341、重连 1/1、退出码 0；未观察到真实实时群事件，不替代动态置顶人工或长期验收。旧全群报告仅代表规则修改前的执行结果。

在当前运行进程环境中提供 `MESSAGEPURIFY_NAPCAT_TOKEN`，工具使用 Authorization Bearer 发送它。不要把 Token 放在命令、URL、项目文档或报告里。

```powershell
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.QqProbe --configuration Release -- --output .tools/m3-results/qq-probe.md
```

支持 `--url`、`--listen-seconds`（0～300）、`--history-groups`（0～20 或 `all`）、`--history-count`（1～100）、`--history-days`（0～7）、`--history-pages`（1～50）及 `--output`。默认仍读取一群一页；指定正数天数而未指定页数时，每群最多 20 页。分页每页至少 2 条，天数窗口以本次启动时间为基准。

`--reconnect-rounds`（0～2，默认 0）用于受控重连验证。每轮读取历史并监听 `--listen-seconds` 秒，然后只断开探针自己的 WebSocket；等待 2 秒后重新连接，同一个 QQ 客户端继续运行。每轮重新核对账号与群列表，从最新页回读同一个历史窗口，不复用旧游标；账号不符立即停止，取消后不再连接。消息键去重在本次进程内跨连接保留，失败记录不会因后续成功消失。

```powershell
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.QqProbe --configuration Release -- --history-groups all --history-count 100 --history-days 7 --history-pages 20 --listen-seconds 60 --output .tools/m3-results/qq-paged-history.md
```

在上述命令中加入 `--reconnect-rounds 1` 可执行两轮读取。报告按连接序号区分群结果，并统计连接核对、完成的重连、重复消息及重连后首次发现的消息键。首次发现不证明消息恰好在断开期间发送；受控重连不替代非预期断网的自动恢复、QQ/NapCat 重启、跨进程持久化或正式任务补漏验收。

分页按发送时间选择最早消息，同秒多条用数值 `real_seq` 消歧，下一请求使用该消息的原样 `message_seq`；不对游标加减或按短 ID 数值排序。带游标请求使用 `reverse_order=true` 向旧消息读取；首次读取最新页不变。游标仅在本次连接中使用，不跨 NapCat 重启持久化。分页包含边界消息，按账号、群、消息 ID 去重；短页继续读取。实时事件在历史读取期间也继续接收。

每页接口失败或超时最多重试一次。报告逐群列出本次列表序号、实际读取页数、窗口内去重消息数及停止原因：`date-boundary` 是返回消息越过窗口，`empty-page` 是成功返回空页，默认单页为 `sampled`。`history-page-limit`、`history-no-progress`、游标缺失/歧义及接口失败表示未补全，不伪装为成功；1200 不等同于空页。任何结果均只证明本次 QQ 实际返回范围，不保证完整七天记录，也不代表断线补漏、正式存储或任务生成已实现。

报告只记录版本、状态、消息及段类型计数、分页范围和安全错误类别；不输出账号号、群名、昵称、正文、历史游标、图片地址或 Token。历史消息总计包含页面重叠，去重数另列。私聊事件排除，发现账号不同立即停止此次读取。引用只有明确 `id` 才建立原消息证据；仅 `seq` 时保留协议字段。

退出码：`0` 为本次限定范围通过，`1` 为服务、QQ 状态、接口或协议失败，`2` 为参数或工具执行错误。连接存在但没有观察到群事件，不能据此认定真实群接收已通过。

协议测试使用合成数据和本机受控 WebSocket 服务，不替代真实 NapCat、QQ 或学校群消息验收。
