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

Windows测试首轮与Infrastructure编译同时写共享中间产物而失败，按顺序重跑522/522通过；不改产品代码或放宽断言。日志及本机数据保护报告留`.tools/nav-labels/`。已保存原程序回滚副本及四库/5设置快照，尚未替换正式程序或发布新包。
