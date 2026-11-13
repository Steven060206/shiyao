# 导航分组标题可读性调整

用户反馈左侧“处理”“记录”不显眼。仅修改`.nav-group`：12px普通辅助文字改为16px、700字重、主文字色，下方留白4px改为8px。浅深色跟随已有变量，导航顺序、计数、点击、设置位置及业务逻辑不变。

| 浅色 | 深色 |
|---|---|
| ![浅色导航](nav-groups-light.png) | ![深色导航](nav-groups-dark.png) |

截图来自正式HTML与隔离合成数据。检查1000×581、1360×841、1920×1041内容区及浏览器1/1.25/1.5像素比例，无横向溢出或按钮裁切；不改变Windows缩放。

实际检查：

- `node --test tests/*.test.mjs`：217/217；新增浏览器回归验证两个标题的实际字号/字重/颜色、浅深主题和最小窗口全部六个导航入口可见。
- `.tools/dotnet/dotnet.exe test tests/MessagePurify.Core.Tests --configuration Release --verbosity quiet`：428/428；同参数Infrastructure865/865、Windows522/522。
- `.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p '*_test.py'`：41/41，包含OCR worker、运行库与发布打包。
- `git diff --check`：退出0。

Windows测试首轮与Infrastructure编译同时写共享中间产物而失败，按顺序重跑522/522通过；不改产品代码或放宽断言。日志及本机数据保护报告留`.tools/nav-labels/`。已保存原程序回滚副本及四库/5设置快照，本机正式程序与公开下载包已更新，结果如下。


发布源码：d6c4547508f35526e63c0bbd4932b4af1e15de07。从该提交git archive导出干净源码，自包含publish退出0，干净Node217/217；下载包240621079字节、2696条目，2673个非PDB文件、2177个OCR文件、官方NapCat原包与隐私排除检查通过。中文空格解压版Native151、合成Setup与随包离线OCR四图通过。

[GitHub beta.15](https://github.com/Steven060206/shiyao/releases/tag/v0.1.0-beta.15)已公开，draft=false、prerelease=true、uploaded；匿名下载HTTP200，摘要与本地一致。代码已推main。

本机更新：原进程53148通过自己的托盘菜单正常退出，exitCode=0；复制2676个干净发布文件，Explorer以`--background`启动PID58356，无包身份、不在工具Job且响应。主窗口隐藏、非最小化；两卡SHELLDLL_DefView父窗、非置顶/不激活且空列表隐藏。四库16表全行及5设置与即时保护快照一致，程序文件逐字节匹配发布版；未触发QQ同步或代用户处理事项。

回滚程序`.tools/nav-labels/rollback/program`；即时四库/设置保护快照`.tools/nav-labels/beta15-before-restart`。回滚时正常退出应用，仅恢复程序文件，不覆盖后续账号数据。旧辅助托盘脚本两次未找到菜单，未强制结束应用；通过实际UI及同进程InvokePattern正常退出完成更新。

关键交付命令实际执行成功：`beta15-verify-archive.py`、`beta15-verify-package.py`、`beta15-run-physical-data.ps1 -Mode verify -Snapshot beta15-before-restart -FormalProcessId 58356`、`beta15-check-process-isolation.ps1 -FormalProcessId 58356`、`beta15-check-main.py 58356 hidden`、`beta15-verify-github.py`，均位于`.tools/nav-labels/`。收尾文档另行提交，发布tag继续对应d6c4547。
