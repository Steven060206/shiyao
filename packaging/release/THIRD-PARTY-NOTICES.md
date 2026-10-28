# 第三方组件

本项目自己的 MIT 许可证不改变以下第三方组件的独立许可证。

- **NapCat Framework v4.18.37**：Copyright © 2024 Mlikowa。[原始发行包](https://github.com/NapNeko/NapCatQQ/releases/tag/v4.18.37)，[完整许可](licenses/NapCat-LICENSE.txt)。Limited Redistribution License 包含非商业使用限制。下载包只包含官方未配置的原始 ZIP；置顶桥在用户电脑上从官方文件生成副本，不公开分发修改后的 NapCat bundle。
- **.NET / WPF Runtime**：[dotnet/runtime](https://github.com/dotnet/runtime)、[dotnet/wpf](https://github.com/dotnet/wpf)，MIT 及其第三方许可。[完整许可](licenses/dotnet-LICENSE.txt)与[第三方许可](licenses/dotnet-ThirdPartyNotices.txt)一并保留。
- **Microsoft Edge WebView2 SDK / Loader**：Copyright Microsoft Corporation，按包内 [WebView2 LICENSE](licenses/WebView2-LICENSE.txt) 和 [NOTICE](licenses/WebView2-NOTICE.txt) 使用。Edge WebView2 Runtime 由用户通过微软官方渠道安装，不包含在本下载包中。
- **Microsoft.Data.Sqlite / SQLitePCLRaw**：Copyright Microsoft Corporation / Copyright 2014–2024 SourceGear, LLC；[EF Core MIT](licenses/EFCore-LICENSE.txt)、[SQLitePCL.raw Apache-2.0](licenses/SQLitePCLRaw-LICENSE.txt)和[NOTICE](licenses/SQLitePCLRaw-NOTICE.txt)。[SQLite](https://www.sqlite.org/copyright.html) 为公有领域，发布目录内组件随程序使用。
- **本地 OCR**：下载包 `app/ocr/` 包含独立 Python 3.12.10、RapidOCR CPU worker、三个 ONNX 模型及锁定推理依赖，不要求系统 Python。Python 完整许可位于 `app/ocr/python/LICENSE.txt`；RapidOCR、ONNX Runtime、FlatBuffers 与模型许可说明位于 `app/ocr/licenses/`，其他依赖的许可保留在 `app/ocr/python/Lib/site-packages/` 对应发行包的元数据目录中。版本及模型哈希见 `app/ocr/runtime-manifest.json`，完整依赖版本见 `app/ocr/requirements.lock.txt`。
