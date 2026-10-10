# QQ 只读探针

此工具连接已经启动并登录的本机 NapCat OneBot 11 正向 WebSocket。默认根地址 `ws://127.0.0.1:3001/`，NapCat 须配置 `messagePostFormat=array`。不连接 `/api`，该路径不会推送事件。

只提供 `get_status`、`get_login_info`、`get_version_info`、`get_group_list` 和 `get_group_msg_history`。默认读取群列表中第一个群的一页历史（最多 20 条），随后监听 30 秒。选择顺序来自接口，不按群名判断是否学校群；本探针不建立课程或任务。

在当前运行进程环境中提供 `MESSAGEPURIFY_NAPCAT_TOKEN`，工具使用 Authorization Bearer 发送它。不要把 Token 放在命令、URL、项目文档或报告里。

```powershell
& ./.tools/dotnet/dotnet.exe run --project tools/MessagePurify.QqProbe --configuration Release -- --output .tools/m3-results/qq-probe.md
```

支持 `--url`、`--listen-seconds`（0～300）、`--history-groups`（0～20）、`--history-count`（1～100）、`--output`。每个选择的群只读取一页，不声称已回溯七天或完成分页。历史接口另外保留读取游标参数，但不对游标作任何算术。

报告只记录版本、状态、去重消息及段类型计数和安全错误类别；不输出账号号、群名、昵称、正文、图片地址或 Token。私聊事件排除，发现事件账号不同立即停止此次读取。引用只有明确 `id` 才建立原消息证据；仅 `seq` 时保留协议字段。

退出码：`0` 为本次限定范围通过，`1` 为服务、QQ 状态、接口或协议失败，`2` 为参数或工具执行错误。连接存在但没有观察到群事件，不能据此认定真实群接收已通过。

协议测试使用合成数据和本机受控 WebSocket 服务，不替代真实 NapCat、QQ 或学校群消息验收。
