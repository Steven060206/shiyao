# 本地 OCR 工作进程

M3 探针使用 RapidOCR 3.10.0、ONNX Runtime 1.31.0 和随包 CPU 模型。请求顺序处理，不上传图片，不修改输入图片；结果只作证据和建议，不建立正式日期或提醒。

## 项目环境

已验证 Windows x64、Python 3.12.10。所有包安装在项目隔离环境，完整版本见 `requirements.lock.txt`：

```powershell
python -m venv .tools/ocr/venv
& ./.tools/ocr/venv/Scripts/python.exe -m pip install --only-binary=:all: -r ocr/requirements.lock.txt
& ./.tools/ocr/venv/Scripts/python.exe -m pip check
& ./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py
```

当前 venv 依赖开发机已有的 Python；解释器随安装包、无系统 Python 运行及隐藏宿主的进程重启／超时处理在后续集成和发布阶段验证。

## JSON Lines 协议

```powershell
& ./.tools/ocr/venv/Scripts/python.exe ocr/worker.py
```

标准输入／输出为 UTF-8，一行一个 JSON。EOF 正常退出，日志只写 stderr，无主动 stdout 消息。

请求：

```json
{"id":"p1","imagePath":"D:\\local-evidence\\image.png"}
```

成功：

```json
{"id":"p1","success":true,"result":{"text":"数学作业","lines":[{"text":"数学作业","box":[[1,2],[30,2],[30,12],[1,12]],"confidence":0.98}]}}
```

失败：

```json
{"id":"p1","success":false,"error":{"code":"MISSING_MODEL","message":"Missing local model: PP-OCRv6_det_small.onnx"}}
```

`id` 必须是非空字符串，`imagePath` 必须是本地文件绝对路径，不接收 URL 或网络共享路径。坏 JSON／字段为 `INVALID_REQUEST`，缺图为 `IMAGE_NOT_FOUND`，读取失败为 `IMAGE_UNREADABLE`，缺本地模型为 `MISSING_MODEL`，缺依赖为 `MISSING_DEPENDENCY`，解码／推理失败为 `OCR_FAILED`。无法取得有效 id 时返回 null。单图失败后继续读取下一请求；空白图可成功返回空 text／lines。

## 本地模型和许可

工作进程先检查 RapidOCR 安装目录 `models/` 的三份文件，再显式传入路径。`--models-dir` 可指定另一套相同文件名的本地模型；缺文件直接失败，不回退为默认下载。识别字典嵌入识别模型元数据，测试验证含中文字符。专用进程内禁用 RapidOCR 的下载入口；缺字典触发下载退路时返回 `MISSING_MODEL`，不下载字典或改动安装文件；其它模型格式或配置错误按 `OCR_FAILED` 报告。

| 阶段 | 随包文件 | SHA-256 |
|---|---|---|
| 检测 | `PP-OCRv6_det_small.onnx` | `090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f` |
| 方向 | `ch_ppocr_mobile_v2.0_cls_mobile.onnx` | `e47acedf663230f8863ff1ab0e64dd2d82b838fceb5957146dab185a89d6215c` |
| 识别 | `PP-OCRv6_rec_small.onnx` | `6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884` |

上述值已与安装文件和[官方模型声明](https://rapidai.github.io/RapidOCRDocs/main/model_licenses/)核对。RapidOCR 代码为 Apache-2.0；模型权重归百度及 PaddleOCR 相关权利人，按 Apache-2.0 分发；ONNX Runtime 为 MIT。`licenses/` 保留安装包内的 RapidOCR 原许可证、模型归属及 ONNX Runtime 许可证。ONNX Runtime 的 `ThirdPartyNotices.txt` 和其它锁定依赖各自的许可证目前保留在隔离安装目录；后续分发运行库时须一并保留全部相应文件，不以本说明替代。

版本和本地配置来源：[RapidOCR PyPI](https://pypi.org/project/rapidocr/3.10.0/)、[ONNX Runtime PyPI](https://pypi.org/project/onnxruntime/1.31.0/)、[官方本地模型使用](https://rapidai.github.io/RapidOCRDocs/main/install_usage/rapidocr/how_to_use_offline_model/)。

## 验证范围

测试用 `tests/fixtures/ocr/generate_synthetic.py` 生成明确标为 synthetic 的中文通知、3300 像素长图和空白图。真实引擎测试检查通知日期文字、长图底部日期、四角位置、置信度、损坏图片后继续及原图不变；另外检查三个实际会话均只用 `CPUExecutionProvider`。

测试子进程通过启动前的 `sitecustomize` 禁止 Python socket 连接／DNS，并检查没有联网尝试，验证随包本地模型和字典足以运行。这不替代整机断网、安装包独立解释器或完整真实学校通知图片的验收。用户截图只按本次明确授权处理；原图及识别全文不进入 Git 或公开验证文档。
