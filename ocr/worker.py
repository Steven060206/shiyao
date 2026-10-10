"""Sequential, local-only RapidOCR worker using UTF-8 JSON Lines."""

import argparse
import contextlib
import importlib.util
import json
import math
from pathlib import Path
import sys

MODEL_FILES = {
    "Det": "PP-OCRv6_det_small.onnx",
    "Cls": "ch_ppocr_mobile_v2.0_cls_mobile.onnx",
    "Rec": "PP-OCRv6_rec_small.onnx",
}


class WorkerError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code


def deny_model_download(*_args, **_kwargs):
    raise WorkerError("MISSING_MODEL", "Local OCR model or character dictionary is unavailable")


def load_engine(models_dir=None):
    """Load bundled models explicitly. Missing files never initiate a download."""
    if models_dir is None:
        spec = importlib.util.find_spec("rapidocr")
        if spec is None:
            raise WorkerError("MISSING_DEPENDENCY", "RapidOCR is not installed")
        models_dir = Path(spec.origin).parent / "models"
    models_dir = Path(models_dir)
    for name in MODEL_FILES.values():
        if not (models_dir / name).is_file():
            raise WorkerError("MISSING_MODEL", f"Missing local model: {name}")
    try:
        from rapidocr import EngineType, OCRVersion, RapidOCR
        from rapidocr.utils.download_file import DownloadFile
        # The library has a dictionary download fallback for missing metadata.
        # Keep this dedicated OCR process offline without changing package files.
        DownloadFile.run = staticmethod(deny_model_download)
        params = {
            "Global.log_level": "warning",
            "EngineConfig.onnxruntime.use_cuda": False,
            "EngineConfig.onnxruntime.use_dml": False,
            "EngineConfig.onnxruntime.intra_op_num_threads": 2,
            "EngineConfig.onnxruntime.inter_op_num_threads": 1,
        }
        for stage, name in MODEL_FILES.items():
            params[f"{stage}.model_path"] = str((models_dir / name).resolve())
            params[f"{stage}.engine_type"] = EngineType.ONNXRUNTIME
            params[f"{stage}.ocr_version"] = (
                OCRVersion.PPOCRV4 if stage == "Cls" else OCRVersion.PPOCRV6)
        return RapidOCR(params=params)
    except ImportError as error:
        raise WorkerError("MISSING_DEPENDENCY", "RapidOCR CPU dependencies are unavailable") from error


def result_data(raw):
    if raw.txts is None:
        return {"text": "", "lines": []}
    lines = []
    for text, box, score in zip(raw.txts, raw.boxes, raw.scores, strict=True):
        if (not isinstance(text, str)
                or any(0xD800 <= ord(character) <= 0xDFFF for character in text)):
            raise WorkerError("OCR_FAILED", "OCR returned invalid Unicode text")
        coordinates = []
        for point in box:
            if len(point) != 2:
                raise WorkerError("OCR_FAILED", "OCR returned an invalid box")
            numeric_point = [float(value) for value in point]
            if not all(math.isfinite(value) for value in numeric_point):
                raise WorkerError("OCR_FAILED", "OCR returned a non-finite box")
            coordinates.append(numeric_point)
        numeric_score = float(score)
        if not math.isfinite(numeric_score):
            raise WorkerError("OCR_FAILED", "OCR returned a non-finite confidence")
        lines.append({"text": text, "box": coordinates, "confidence": numeric_score})
    return {"text": "\n".join(line["text"] for line in lines), "lines": lines}


class Worker:
    def __init__(self, engine_factory=load_engine, log=None):
        self.engine_factory = engine_factory
        self.engine = None
        self.log = log if log is not None else sys.stderr

    def process(self, line):
        request_id = None
        try:
            try:
                request = json.loads(line)
            except (json.JSONDecodeError, ValueError) as error:
                raise WorkerError("INVALID_REQUEST", "Request must be a JSON object") from error
            if not isinstance(request, dict):
                raise WorkerError("INVALID_REQUEST", "Request must be a JSON object")
            request_id = request.get("id")
            if (not isinstance(request_id, str) or not request_id.strip()
                    or any(0xD800 <= ord(character) <= 0xDFFF for character in request_id)):
                request_id = None
                raise WorkerError("INVALID_REQUEST", "id must be a nonempty string")
            image_path = request.get("imagePath")
            if (not isinstance(image_path, str) or "://" in image_path
                    or image_path.startswith(("\\\\", "//"))
                    or not Path(image_path).is_absolute()):
                raise WorkerError("INVALID_REQUEST", "imagePath must be an absolute local file path")
            image = Path(image_path)
            if not image.is_file():
                raise WorkerError("IMAGE_NOT_FOUND", "Local image file does not exist")
            try:
                image_bytes = image.read_bytes()
            except OSError as error:
                raise WorkerError("IMAGE_UNREADABLE", "Local image file could not be read") from error
            # Redirect library prints during import, lazy model loading and inference.
            with contextlib.redirect_stdout(self.log):
                if self.engine is None:
                    self.engine = self.engine_factory()
                result = result_data(self.engine(image_bytes))
            return {"id": request_id, "success": True, "result": result}
        except WorkerError as error:
            code, message = error.code, str(error)
        except Exception as error:
            code, message = "OCR_FAILED", f"{type(error).__name__}: image recognition failed"
        print(f"OCR request failed: {code}", file=self.log, flush=True)
        return {"id": request_id, "success": False,
                "error": {"code": code, "message": message}}


def run(input_stream, output_stream, log=None, engine_factory=load_engine):
    processor = Worker(engine_factory, log)
    for line in input_stream:
        result = processor.process(line)
        output_stream.write(json.dumps(result, ensure_ascii=False, allow_nan=False) + "\n")
        output_stream.flush()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--models-dir", type=Path, help="Override bundled local model directory")
    args = parser.parse_args()
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        stream.reconfigure(encoding="utf-8")
    run(sys.stdin, sys.stdout, sys.stderr, lambda: load_engine(args.models_dir))


if __name__ == "__main__":
    main()
