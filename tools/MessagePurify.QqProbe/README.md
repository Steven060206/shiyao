# QQ 只读探针

此工具连接已经启动并登录的本机 NapCat OneBot 11 正向 WebSocket。默认根地址 `ws://127.0.0.1:3001/`，NapCat 须配置 `messagePostFormat=array`。不连接 `/api`，该路径不会推送事件。

只提供 `get_status`、`get_login_info`、`get_version_info`、`get_group_list` 和 `get_group_msg_history`。默认读取群列表中第一个群的一页历史（最多 20 条），随后监听 30 秒。选择顺序来自接口，不按群名判断是否学校群；本探针不建立课程或任务。

在当前运行进程环境中提供 `MESSAGEPURIFY_NAPCAT_TOKEN`，工具使用 Authorization Bearer 发送它。不要把 Token 放在命令、URL、项目文档或报告里。

```powershell
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.QqProbe --configuration Release -- --output .tools/m3-results/qq-probe.md
```

支持 `--url`、`--listen-seconds`（0～300）、`--history-groups`（0～20 或 `all`）、`--history-count`（1～100）、`--history-days`（0～7）、`--history-pages`（1～50）及 `--output`。默认仍读取一群一页；指定正数天数而未指定页数时，每群最多 20 页。分页每页至少 2 条，天数窗口以本次启动时间为基准。

```powershell
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.QqProbe --configuration Release -- --history-groups all --history-count 100 --history-days 7 --history-pages 20 --listen-seconds 60 --output .tools/m3-results/qq-paged-history.md
```

分页按发送时间选择最早消息，同秒多条用数值 `real_seq` 消歧，下一请求使用该消息的原样 `message_seq`；不对游标加减或按短 ID 数值排序。带游标请求使用 `reverse_order=true` 向旧消息读取；首次读取最新页不变。游标仅在本次连接中使用，不跨 NapCat 重启持久化。分页包含边界消息，按账号、群、消息 ID 去重；短页继续读取。实时事件在历史读取期间也继续接收。

每页接口失败或超时最多重试一次。报告逐群列出本次列表序号、实际读取页数、窗口内去重消息数及停止原因：`date-boundary` 是返回消息越过窗口，`empty-page` 是成功返回空页，默认单页为 `sampled`。`history-page-limit`、`history-no-progress`、游标缺失/歧义及接口失败表示未补全，不伪装为成功；1200 不等同于空页。任何结果均只证明本次 QQ 实际返回范围，不保证完整七天记录，也不代表断线补漏、正式存储或任务生成已实现。

报告只记录版本、状态、消息及段类型计数、分页范围和安全错误类别；不输出账号号、群名、昵称、正文、历史游标、图片地址或 Token。历史消息总计包含页面重叠，去重数另列。私聊事件排除，发现账号不同立即停止此次读取。引用只有明确 `id` 才建立原消息证据；仅 `seq` 时保留协议字段。

退出码：`0` 为本次限定范围通过，`1` 为服务、QQ 状态、接口或协议失败，`2` 为参数或工具执行错误。连接存在但没有观察到群事件，不能据此认定真实群接收已通过。

协议测试使用合成数据和本机受控 WebSocket 服务，不替代真实 NapCat、QQ 或学校群消息验收。
