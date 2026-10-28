# 本地 OCR 工作进程

使用 RapidOCR 3.10.0、ONNX Runtime 1.31.0 和随包 CPU 模型。请求顺序处理，不上传图片，不修改输入图片；结果只作证据和建议，不建立正式日期或提醒。

## 项目环境

已验证 Windows x64、Python 3.12.10。所有包安装在项目隔离环境，完整版本见 `requirements.lock.txt`：

```powershell
python -m venv .tools/ocr/venv
& ./.tools/ocr/venv/Scripts/python.exe -m pip install --only-binary=:all: -r ocr/requirements.lock.txt
& ./.tools/ocr/venv/Scripts/python.exe -m pip check
& ./.tools/ocr/venv/Scripts/python.exe -m unittest discover -s tests -p ocr_worker_test.py
```

开发 venv 仍依赖本机 Python，不直接分发。便携运行库使用官方 CPython 3.12.10 x64 embeddable ZIP 和相同锁定依赖，不要求用户安装 Python。

## 便携运行库

从[官方发行页](https://www.python.org/downloads/release/python-31210/)取得 `python-3.12.10-embed-amd64.zip`。其 SHA-256 为 `4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3`，已与该发行页链接的官方 SPDX 文件核对；发行页 MD5 也一致。准备命令：

```powershell
python tools/prepare_ocr_runtime.py --python-archive .tools/ocr/python-3.12.10-embed-amd64.zip --site-packages .tools/ocr/venv/Lib/site-packages --output-directory .tools/ocr/portable
python -m unittest discover -s tests -p ocr_runtime_packaging_test.py
```

工具拒绝已有目标目录，核对官方解释器和三个模型的固定校验值，以及锁定依赖版本。输出 `worker.py`、`requirements.lock.txt`、`runtime-manifest.json`、`licenses/` 和 `python/`；后者保留解释器、标准库和全部运行依赖及对应 dist-info 许可，排除 pip 与字节码缓存。`python312._pth` 只列包内标准库和 site-packages，不启用系统 site，也不依赖系统 PATH/PYTHONHOME/PYTHONPATH。完整目录在应用发布目录中放为 `ocr/`。

```text
ocr/
  worker.py
  runtime-manifest.json
  licenses/
  python/
    python.exe
    python312.dll
    python312.zip
    python312._pth
    Lib/site-packages/
```

`MessagePurify.Infrastructure.Images.LocalOcrProcess` 固定启动指定解释器和 worker，隐藏控制台，通过 UTF-8 JSON Lines 顺序复用一个进程。单请求默认超时 90 秒，取消、超时、进程退出或无效响应后终止旧进程，下个请求重新启动；Dispose 也会终止正在识别的进程。限制响应大小、请求 ID、文字/行数、四角坐标及有限置信度；只返回固定错误码，不返回子进程原始错误、图片路径或识别原文。宿主不调用 QQ、网络或系统注册表。

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

上述值已与安装文件和[官方模型声明](https://rapidai.github.io/RapidOCRDocs/main/model_licenses/)核对。RapidOCR 代码为 Apache-2.0；模型权重归百度及 PaddleOCR 相关权利人，按 Apache-2.0 分发；ONNX Runtime 为 MIT。`licenses/` 保留安装包内的 RapidOCR 原许可证、模型归属及 ONNX Runtime 许可证。便携准备工具保留各依赖原有许可证，包括 ONNX Runtime 的 `ThirdPartyNotices.txt`、OpenCV 第三方许可、NumPy/Shapely/Pillow 等许可，以及 tqdm 的 `LICENCE`。FlatBuffers wheel 缺少完整许可证，准备工具补入 Apache-2.0 全文及 Google 版权归属；Python 官方 ZIP 的 LICENSE.txt 原样保留。

版本和本地配置来源：[RapidOCR PyPI](https://pypi.org/project/rapidocr/3.10.0/)、[ONNX Runtime PyPI](https://pypi.org/project/onnxruntime/1.31.0/)、[官方本地模型使用](https://rapidai.github.io/RapidOCRDocs/main/install_usage/rapidocr/how_to_use_offline_model/)。

## 验证范围

测试用 `tests/fixtures/ocr/generate_synthetic.py` 生成明确标为 synthetic 的中文通知、3300 像素长图和空白图。真实引擎测试检查通知日期文字、长图底部日期、四角位置、置信度、损坏图片后继续及原图不变；另外检查三个实际会话均只用 `CPUExecutionProvider`。

开发 venv 测试通过启动前的 `sitecustomize` 禁止 Python socket 连接／DNS，并检查没有联网尝试。便携解释器的隔离路径忽略 PYTHONPATH，因此便携测试使用包装入口先禁 socket 再运行 worker，避免禁网钩子未实际加载。实际在中文空格解压路径、空 PATH 和无效 PYTHONHOME/PYTHONPATH 下识别中文通知、长图、空白图及坏图后续请求，原图不变；该测试仍不替代整机断网或真实学校图片验收。用户截图只按本次明确授权处理；原图及识别全文不进入 Git 或公开验证文档。

2026-10-28：便携准备工具 5 项、便携引擎 2 项验证通过；宿主 15 项真实子进程/引擎测试通过，包含顺序复用、并发串行、超时、取消、退出/无效响应后重建和 Dispose 无残留。应用集成及最终公开下载包验收由项目交接单独记录，不以本工具测试替代。
