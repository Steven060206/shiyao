# 项目检查

在项目根目录运行全部现有测试：

```powershell
node --test tests/project-rules.test.mjs
node tests/prototype-qa.mjs
```

第一条包含 7 项检查，检查协作规则在需求和交接中的同步、小任务规则的优先级、文档链接、原型文件、整体设计确认状态及实施计划对全部设计验收场景的覆盖。第二条保留原有 26 项浏览器交互检查，并新增减少动画偏好的检查，共 27 项，包括纯黑主题、过渡动画、提醒时刻展示、桌面卡片拖动和缩放、图片日期确认及完成记录。动画测试显式模拟普通动画及减少动画两种偏好，不依赖本机 Windows 的动画设置。

真实应用尚未创建 .NET 或 OCR 测试项目。实施时按 [总计划](../docs/superpowers/plans/2026-10-10-windows-app-implementation.md) 将新增测试命令补入本文；计划中的 C# 示例和验收目标不计为本轮已运行测试。

需要 Node.js 22 或更新版本和 Chrome。默认使用 `C:/Program Files/Google/Chrome/Application/chrome.exe`；其他安装位置可通过环境变量 `CHROME_PATH` 指定。浏览器检查使用独立临时配置，不访问个人浏览器会话。普通测试不会覆盖预览图；仅在主动更新预览时运行 `node tests/prototype-qa.mjs --screenshots`。

这些检查验证文档和浏览器原型，不代表真实 QQ、OCR、Windows 系统通知或桌面驻留已通过测试。
