# 本机 OneBot 端口调整（2026-10-28）

用户确认换端口。此前 3001 由其他 Node 程序占用；本轮先检查 13001 空闲，私有备份 NapCat 配置后仅将对应 WebSocket 端口改为 13001。未直接停止占用 3001 的服务，未输出或公开凭据。

## 实现与回归

拾要从同一份 NapCat 配置读取唯一启用的 loopback WebSocket 端点和 Token；新安装默认 127.0.0.1:13001，已有 3001 配置保持兼容。环境 Token 仅覆盖凭据，不改变端点。多端点拒绝猜测，非本机绑定不选取，端口必须在 1–65535。Setup 重跑保留已有账号配置及 Token；旧启动脚本同样支持非默认端口。

实际执行：

- `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --configuration Release --verbosity quiet`：428 通过。
- 同参数 Infrastructure：865 通过；Windows：522 通过，包括配置端口上的真实 loopback WebSocket 鉴权连接。
- `node --test tests/*.test.mjs`：204 通过（工作区含三个既有视觉改动）；首次因交接新标题放在总览之前导致两项文档断言失败，移至总览后重新全量通过。
- `node --test tests/application-launcher.test.mjs tests/distribution-setup.test.mjs`：13 通过，覆盖旧端口、新端口和重复 Setup 原样保留。
- `.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py`：25 通过。
- `python -m unittest discover -s tests -p '*ocr*packag*test.py'`：7 通过。
- `python -m unittest discover -s tests -p release_packaging_test.py`：9 通过。

## 真实链路边界

无包身份只读助手使用项目原有 QQ 同步会话流程启动自己的 NapCat，已在 13001 建立连接，`get_login_info` 成功且与正式账号一致。首轮尝试 3 个已有图片引用未取得原图，真实QQ图片下载/OCR闭环仍未验收；不能继续归因为原 3001 端口冲突，也不能视为图片功能实机通过。后续只读诊断结果待核对，不将未完成结果写成通过。

正式账号数据库未交给探针写入；首轮后通过 WMI 无包身份 Python 核对四库全部 16 表及 4 份设置与保护快照原样，0 待办/0 待确认，卡片附着且隐藏。没有代用户确认、忽略、恢复账号或发送消息；未调用 QQ 服务端 OCR，未上传图片。真实资料、Token、诊断助手和快照仅位于被忽略的 `.tools/image-ocr-validation/`。

## 发布与部署

源代码测试已通过；beta.13 的干净发布、验包、正式部署及 GitHub 同步正在进行，完成后更新本节。
