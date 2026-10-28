# 2026-10-28 本机备份与后台运行验收

本轮从 beta.10 开始，发现并修复真实备份导出错误，修复版本为 beta.11。实际游戏由用户明确延期，继续其他验收。M3 仍正在实施。

## 备份错误及修复

正式页面导出失败，未产生 ZIP。使用已保护的真实数据副本及单条纯图片合成消息，对已发布 beta.10 DLL 均复现 `InvalidDataException`：备份候选内容无效。

根因：没有文本的图片、空消息和空白文字可产生合法的空标题 Ignored 分析结果，备份校验却无条件要求标题非空。修复只允许原始提取记录中的 Ignored 空标题；任务、待确认、通知及其他必要字段继续校验，不改写已有原文或处理历史。

新增 8 项回归，覆盖图片、引用、空文本、空白文本的导出/检查/恢复，以及四种业务候选空标题继续被拒绝。修前专项实际 4 失败、4 通过；修后 AccountBackupTests 34/34，真实副本和纯图片合成导出均退出 0。

实际执行命令：

```powershell
.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --configuration Release --verbosity quiet --filter 'FullyQualifiedName~ExportRestore_PreservesIgnoredExtractionWithoutText|FullyQualifiedName~Export_RejectsBusinessExtractionWithoutTitle'
.tools/dotnet/dotnet.exe test tests/MessagePurify.Infrastructure.Tests --configuration Release --verbosity quiet --filter 'FullyQualifiedName~AccountBackupTests'
```

临时复现和修后真实副本报告留在 `.tools/runtime-acceptance-beta10/`，不提交真实备份、数据库、账号、消息或异常原始详情。

## 回归与干净发布产物

| 实际执行的命令 | 结果 |
| --- | --- |
| `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --configuration Release --verbosity quiet` | 428/428 |
| 相同参数运行 `tests/MessagePurify.Infrastructure.Tests` | 786/786 |
| 相同参数运行 `tests/MessagePurify.Windows.Tests` | 477/477 |
| `node --test tests/*.test.mjs` | 发布前 191/191 |
| `python -m unittest discover -s tests -p release_packaging_test.py` | 4/4 |
| `.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py` | 25/25 |
| `node --test .tools/release-stage-beta11/tests/*.test.mjs` | 干净源码 191/191 |

收尾新增验收文档边界回归后，实际运行 `node --test tests/project-rules.test.mjs tests/message-review-requirements.test.mjs` 为 54/54、`node --test tests/*.test.mjs` 为 192/192，均退出 0；已验证的发布包仍对应功能提交及其 191 项 Node 测试。

从提交 `41e02bd` 用 `git archive` 导出干净源码，再执行：

```powershell
.tools/dotnet/dotnet.exe publish .tools/release-stage-beta11/src/MessagePurify.Windows/App/MessagePurify.Windows.App.csproj --configuration Release --runtime win-x64 --self-contained true --output .tools/published/github-release-0.1.0-beta.11 --verbosity quiet
python tests/app_desktop_test.py --executable .tools/published/github-release-0.1.0-beta.11/MessagePurify.exe
python .tools/release-stage-beta11/tools/build_release.py --publish-directory .tools/published/github-release-0.1.0-beta.11 --napcat-archive .tools/napcat/framework/v4.18.37/NapCat.Framework.zip --output-directory .tools/releases --version 0.1.0-beta.11
python .tools/runtime-acceptance-beta10/beta11-verify-archive.py
python .tools/runtime-acceptance-beta10/beta11-verify-package.py
python tests/app_desktop_test.py --executable '.tools/releases/验包 中文空格 0.1.0-beta.11/Shiyao/app/MessagePurify.exe'
```

均退出 0。publish 无警告错误；发布版与中文空格解压版各 139 项 Native/真实父窗检查通过，`synthetic=true`。ZIP 为 119942255 字节、518 条目，495 个非 PDB 文件与干净发布逐字节一致；官方 NapCat 原包、隐私文件缺失及合成 Setup/CheckOnly 通过。已有三个视觉文件的未提交修改没有进入功能提交或下载包。

## 正式页面与隔离恢复

正式进程只通过托盘打开设置，导出当前账号 ZIP，打开恢复预览后取消。没有向正式账号提交覆盖恢复。

```powershell
& .tools/runtime-acceptance-beta10/check-backup-ui.ps1 -ProcessId 49012 -Mode Export
& .tools/runtime-acceptance-beta10/check-backup-ui.ps1 -ProcessId 49012 -Mode PreviewCancel
.tools/dotnet/dotnet.exe .tools/runtime-acceptance-beta10/restore-check/beta11-output/RestoreCheck.dll --package 'D:/ai制作的软件/消息提纯/.tools/runtime-acceptance-beta10/private-real-account-backup.zip' --work 'D:/ai制作的软件/消息提纯/.tools/runtime-acceptance-beta10/isolated-restore-beta11-1' --report 'D:/ai制作的软件/消息提纯/.tools/runtime-acceptance-beta10/restore-report-beta11-1.json'
```

均退出 0：正式页面导出 585345 字节 ZIP；预览显示恢复范围和确认覆盖按钮，随后取消，`RestoreSubmitted=false`。

隔离助手引用的 7 个二进制与 beta.11 发布目录逐字节一致。恢复真实备份副本后，四库 14 张业务表、2319 行及表结构完全相同，共用设置、工作区设置和布局一致。自动恢复前备份保留原合成任务及原设置；重开隔离工作区后任务、审核、证据和操作历史保留。缺失必要任务数据库的包被检查阶段拒绝，隔离数据逐文件不变。

备份明确标为不完整，保留 70 个未下载原图的引用。这次不是原图恢复、跨电脑恢复或正式账号覆盖恢复；缺失数据库测试也不代表覆盖了所有损坏类型。隔离恢复未应用系统自启动设置回调，未接管 QQ 或写注册表。

脚本初期的系统文件对话框识别、UTF-8/BOM、导航文本与按钮区分，以及打开/保存对话框 Edit ID 差异已在本地验收助手修正；不计为产品通过证据。上述最终命令才是正式结果。

## 正式部署与后台重开

以 WMI 启动无包身份 Python 保护真实物理 profile，避免 Codex MSIX 虚拟目录干扰。正常托盘退出 beta.10 后，部署 beta.11 的 498 个文件；原入口、MessagePurify.Shiyao、黑色标题栏、约 2 DIP 间距与个人数据保留。

部署后 PID49012、重开后 PID44892 均通过 Explorer 以 `--background` 独立启动，无包身份、不在工具 Job，主窗口真正隐藏。

```powershell
& .tools/runtime-acceptance-beta10/beta11-run-physical-data.ps1 -Mode capture -Snapshot beta11-before-deploy
& .tools/runtime-acceptance-beta10/beta11-deploy.ps1
& .tools/runtime-acceptance-beta10/beta11-start-explorer-background.ps1 -ExecutablePath 'D:/ai制作的软件/消息提纯/.tools/published/app-card-controls/MessagePurify.exe'
& .tools/runtime-acceptance-beta10/beta11-check-process-isolation.ps1 -FormalProcessId 44892
& .tools/runtime-acceptance-beta10/beta11-run-physical-data.ps1 -Mode verify -Snapshot beta11-after-export -FormalProcessId 44892
python .tools/runtime-acceptance-beta10/beta11-check-main.py 44892 hidden
```

部署前后四库 15 表（含 SQLite 内部序列表）全行、4 份设置严格相同。验收期间用户自行忽略了 5 条待确认，并明确确认操作来源；这些操作及其正常已读变化保留，没有回滚。随后建立 `beta11-after-export` 保护快照，导出/预览取消之后及重开后四库全行和 4 设置均与该快照严格相同，498 个程序文件匹配发布版。当前 0 个待办、0 个待确认，两卡仍附着 `SHELLDLL_DefView`，child/noActivate=true、topmost=false，均正确隐藏。

`python .tools/runtime-acceptance-beta10/audit-beta11-five-ignores.py` 实际退出 0：原 8 个任务、8 条任务证据、184 条审核候选和 371 笔旧操作原样；仅 5 条审核正常忽略，版本各增 1，撤销期限恰为 24 小时，新增审计及序号连续。被更新旧记录正常补写兼容缺省 `IsCourseConflict=false`。3 份设置字节原样，工作区设置仅已读版本新增 4 条、前进 2 条，无删除或回退，课程关系保留。该脚本只比较两份快照，未读写正式 profile。

QQ 进程身份与既有接口监听 owner 在部署/重开前后不变；没有触发实际 QQ 同步。Run 项只读核对指向正式 exe 的 `--background`，真实 Windows 登录自启动仍未验收。

## 观察结果与剩余范围

beta.10 在本轮完成 300.01 秒观察，40 个样本主进程均响应、两卡均附着；此后已替换为 beta.11。

beta.11 实际执行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/watch_application.ps1 -ProcessId 44892 -Report 'D:/ai制作的软件/消息提纯/.tools/runtime-acceptance-beta10/watch-beta11-5m.json' -Seconds 300 -IntervalSeconds 10`，退出 0。报告 300.08 秒、30 个唯一样本，全部响应、两卡全部附着且保持隐藏，采样间隔 10.02～10.07 秒。主进程私有内存 107.473→100.055 MiB、工作集 175.707→170.484 MiB，累计 CPU 增 4.578 秒。结束后物理四库全行和 4 设置再次验证通过。本节仅为五分钟空闲观察，不能作为长期稳定性验收。

源码提交 `41e02bd5406309df634c312789cfb29c79cfc153` 已推送 main。[beta.11 Release](https://github.com/Steven060206/shiyao/releases/tag/v0.1.0-beta.11) 已公开；通过 `gh release view` 核对完整目标提交与附件后发布草稿，`gh api repos/Steven060206/shiyao/releases/tags/v0.1.0-beta.11` 确认 `draft=false`、`prerelease=true`、附件 `uploaded`，119942255 字节与本地一致。`curl.exe --noproxy '*' --head --location --silent --show-error --max-time 30 --output NUL --write-out 'HTTP %{http_code}' https://github.com/Steven060206/shiyao/releases/download/v0.1.0-beta.11/Shiyao-0.1.0-beta.11-win-x64.zip` 实际退出 0，匿名下载 HTTP200。

历史 `.tools/unattended-validation/continuous-20261013-163828/watch-2h.json` 已逐一审计全部 238 样本：2026-10-13 16:40:00.415 至 18:40:00.548，共 7200.13 秒；238/238 响应、两卡附着且可见。该报告对象是旧 `app-desktop-recovery`，不是 beta.10/beta.11。主进程工作集增长 33.859 MiB、私有内存增长 64.836 MiB，四个半小时私有内存均值持续上升、未显示平台；不据此认定资源稳定或内存泄漏。连接标签全程“等待下次同步”，不能作为 QQ 协议健康或断线恢复证明。

仍未验收：实际游戏暂停/补同步（用户延期）、Windows 真实登录、多屏/Explorer 重启/睡眠、跨电脑恢复、实际课程关系修改和当前版本长期稳定性。图片下载、OCR 和完整原图备份仍是后期独立项目。
