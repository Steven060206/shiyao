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

无包身份只读助手使用项目原有 QQ 同步会话流程启动自己的 NapCat，已在 13001 建立连接，`get_login_info` 成功且与正式账号一致。首轮尝试 3 个已有图片引用未取得原图，真实QQ图片下载/OCR闭环仍未验收；不能继续归因为原 3001 端口冲突，也不能视为图片功能实机通过。追加探针未产生新报告，未将首轮旧报告当作新结果；没有新的错误细节可以确认，下一步仍须定位图片下载失败。普通 QQ 已经通过 Explorer 重新打开，最终进程检查无 `napimain` 遗留。

正式账号数据库未交给探针写入；首轮后通过 WMI 无包身份 Python 核对四库全部 16 表及 4 份设置与保护快照原样，0 待办/0 待确认，卡片附着且隐藏。没有代用户确认、忽略、恢复账号或发送消息；未调用 QQ 服务端 OCR，未上传图片。真实资料、Token、诊断助手和快照仅位于被忽略的 `.tools/image-ocr-validation/`。

## 发布与部署

功能提交 `6f7fd4e453170a3a56508499d70b59748d422a9f` 已推送 main，`git archive` 导出 `.tools/release-stage-beta13/`。从该干净源码实际执行：

- `.tools/dotnet/dotnet.exe publish .tools/release-stage-beta13/src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --configuration Release --runtime win-x64 --self-contained true --output .tools/published/github-release-0.1.0-beta.13 --verbosity quiet`：退出 0，零警告错误。
- 在干净源码目录运行 `node --test tests/*.test.mjs`：204 通过。
- `python .tools/release-stage-beta13/tools/build_release.py --publish-directory .tools/published/github-release-0.1.0-beta.13 --napcat-archive .tools/napcat/framework/v4.18.37/NapCat.Framework.zip --ocr-runtime-directory .tools/ocr/portable-beta12 --output-directory .tools/releases --version 0.1.0-beta.13`：退出 0；OCR 源码未变，复用已严格核对的随包运行库。
- `python .tools/image-ocr-validation/beta13-verify-archive.py`：2696 ZIP 条目，2673 个非 PDB 发布文件、2177 个 OCR 文件、官方 NapCat 原包逐字节匹配；路径、隐私和解压后字节核对通过。
- 设置 `DOTNET_ROOT` 后运行 `python .tools/release-stage-beta13/tests/app_desktop_test.py --executable .tools/published/github-release-0.1.0-beta.13/MessagePurify.exe`：147 项 Native，success=true/synthetic=true。
- `python .tools/image-ocr-validation/beta13-verify-package.py`：中文空格解压版同样 147 项 Native；合成 Setup CheckOnly/实际配置、默认13001、随包 Python 空 PATH/禁 socket 四图 OCR 通过；QQ 进程前后原样。

下载包 `.tools/releases/Shiyao-0.1.0-beta.13-win-x64.zip` 为 **240617086 字节**。Release：[v0.1.0-beta.13](https://github.com/Steven060206/shiyao/releases/tag/v0.1.0-beta.13)。`gh release create` 后经 `python .tools/image-ocr-validation/beta13-verify-github.py` 核对附件 uploaded、draft=false、prerelease=true、匿名下载 HTTP200，tag 对应功能提交；GitHub digest 与本地 SHA256 均为 `9478dd460233e32d3c432846521ac4c2ba502601968c9d16048cc6df80bd297d`。初次 push 被 Git URL 专用代理 127.0.0.1:10808 失联阻断，仅在本次 Git 命令内禁用代理后推送成功，未修改用户代理配置。

## 正式部署与数据保留

`beta13-run-physical-data.ps1 -Mode capture -Snapshot beta13-before-deploy` 保护真实四库及4设置。旧 `tests/app_tray_test.ps1` 未找到退出菜单，随后私有 `beta13-exit-formal.ps1` 枚举同名托盘图标并点击本进程的“退出”，正式 PID84364 正常退出码0；没有更改产品托盘。`beta13-deploy.ps1` 复制2676文件到原正式目录，经 Explorer `--background` 启动 **PID54604**。

`beta13-check-process-isolation.ps1 -FormalProcessId 54604` 确认无包身份、不在工具Job且响应。分别以 `beta13-before-deploy` 和 `beta13-before-port-change` 快照执行 `beta13-run-physical-data.ps1 -Mode verify -Snapshot <快照> -FormalProcessId 54604`：四库全部16表全行及4设置原样，2676正式文件与干净发布一致；0待办/0待确认，两卡父窗SHELLDLL_DefView、child/noActivate=true、topmost=false且隐藏。`python .tools/image-ocr-validation/beta13-check-main.py 54604 hidden`：visible=false/iconic=false。

三个既有视觉改动 `app.css`、`index.html`、`application-ui.test.mjs` 保持未提交，不混入本次提交与干净包。maintain-context-hygiene 无 SKILL.md，已人工核对当前有效需求、交接、代码、正式文件和实际结果，不为压缩删除文件。M3仍正在实施；真实图片下载、实际游戏、Windows登录、多屏/恢复、跨电脑及长期稳定性继续保留验收范围。收尾文档及对应回归单独提交，beta.13 包保持功能提交。

交付收尾新增端口边界文档回归，`node --test tests/*.test.mjs` 最终205/205，退出0；`git diff --check` 对本轮文件通过。有效需求、交接、真实数据及公开下载结果已核对，收尾文档及回归单独 commit 推送 main，不重建已经验证的功能包。
