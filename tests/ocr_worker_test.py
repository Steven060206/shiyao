"""OCR protocol tests. Images in fixtures/ocr are synthetic, never QQ evidence."""

import contextlib
import importlib.util
import io
import json
import math
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("ocr_worker", ROOT / "ocr" / "worker.py")
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


def recognized(text="数学作业"):
    return SimpleNamespace(txts=(text,), scores=(0.98,),
                           boxes=(((1, 2), (30, 2), (30, 12), (1, 12)),))


class ProtocolTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.image = Path(self.directory.name) / "原图.png"
        self.image.write_bytes(b"unchanged image evidence")
        self.log = io.StringIO()
        self.processor = worker.Worker(lambda: lambda _: recognized(), self.log)

    def request(self, **changes):
        data = {"id": "request-1", "imagePath": str(self.image)}
        data.update(changes)
        return json.dumps(data, ensure_ascii=False)

    def test_recognized_text_positions_and_confidence(self):
        actual = self.processor.process(self.request())
        self.assertTrue(actual["success"])
        self.assertEqual("request-1", actual["id"])
        self.assertEqual("数学作业", actual["result"]["text"])
        line = actual["result"]["lines"][0]
        self.assertEqual([[1, 2], [30, 2], [30, 12], [1, 12]], line["box"])
        self.assertEqual(0.98, line["confidence"])

    def test_invalid_json_then_next_request(self):
        failure = self.processor.process("{broken")
        self.assertIsNone(failure["id"])
        self.assertEqual("INVALID_REQUEST", failure["error"]["code"])
        self.assertTrue(self.processor.process(self.request())["success"])

    def test_invalid_request_fields(self):
        invalid = [None, [], {}, {"id": 123, "imagePath": str(self.image)},
                   {"id": "", "imagePath": str(self.image)},
                   {"id": "p", "imagePath": None}]
        for data in invalid:
            with self.subTest(data=data):
                result = self.processor.process(json.dumps(data))
                self.assertFalse(result["success"])
                self.assertEqual("INVALID_REQUEST", result["error"]["code"])

    def test_surrogate_id_is_rejected_and_following_utf8_request_survives(self):
        output_bytes = io.BytesIO()
        output = io.TextIOWrapper(output_bytes, encoding="utf-8")
        invalid_request = json.dumps({"id": "\ud800", "imagePath": str(self.image)}, ensure_ascii=True)
        requests = invalid_request + "\n" + self.request(id="valid") + "\n"
        worker.run(io.StringIO(requests), output, self.log,
                   lambda: lambda _: recognized())
        output.flush()
        results = [json.loads(line) for line in output_bytes.getvalue().decode("utf-8").splitlines()]
        self.assertEqual(2, len(results))
        self.assertFalse(results[0]["success"])
        self.assertIsNone(results[0]["id"])
        self.assertEqual("INVALID_REQUEST", results[0]["error"]["code"])
        self.assertTrue(results[1]["success"])

    def test_bad_numeric_result_is_failed_and_next_request_survives(self):
        for field in ("confidence", "box"):
            for value in (math.nan, math.inf, -math.inf):
                with self.subTest(field=field, value=value):
                    bad = recognized()
                    if field == "confidence":
                        bad.scores = (value,)
                    else:
                        bad.boxes = (((1, 2), (value, 2), (30, 12), (1, 12)),)
                    responses = [bad, recognized()]
                    output_bytes = io.BytesIO()
                    output = io.TextIOWrapper(output_bytes, encoding="utf-8")
                    worker.run(io.StringIO(self.request(id="bad") + "\n" + self.request(id="valid") + "\n"),
                               output, self.log, lambda: lambda _: responses.pop(0))
                    output.flush()
                    results = [json.loads(line) for line in output_bytes.getvalue().decode("utf-8").splitlines()]
                    self.assertEqual(2, len(results))
                    self.assertFalse(results[0]["success"])
                    self.assertEqual("OCR_FAILED", results[0]["error"]["code"])
                    self.assertTrue(results[1]["success"])

    def test_surrogate_result_text_is_failed_and_next_utf8_request_survives(self):
        responses = [recognized("日期\ud800"), recognized()]
        output_bytes = io.BytesIO()
        output = io.TextIOWrapper(output_bytes, encoding="utf-8")
        worker.run(io.StringIO(self.request(id="bad-text") + "\n" + self.request(id="valid") + "\n"),
                   output, self.log, lambda: lambda _: responses.pop(0))
        output.flush()
        results = [json.loads(line) for line in output_bytes.getvalue().decode("utf-8").splitlines()]
        self.assertEqual(2, len(results))
        self.assertFalse(results[0]["success"])
        self.assertEqual("bad-text", results[0]["id"])
        self.assertEqual("OCR_FAILED", results[0]["error"]["code"])
        self.assertTrue(results[1]["success"])
        self.assertEqual("数学作业", results[1]["result"]["text"])

    def test_only_absolute_local_paths(self):
        for image_path in ("relative.png", "https://example.com/image.png",
                           "file:///image.png", "\\\\server\\share\\image.png"):
            with self.subTest(image_path=image_path):
                failure = self.processor.process(self.request(imagePath=image_path))
                self.assertEqual("INVALID_REQUEST", failure["error"]["code"])

    def test_missing_image_preserves_request_id(self):
        result = self.processor.process(self.request(imagePath=str(self.image.with_name("missing.png"))))
        self.assertEqual("request-1", result["id"])
        self.assertEqual("IMAGE_NOT_FOUND", result["error"]["code"])

    def test_directory_is_not_an_image(self):
        result = self.processor.process(self.request(imagePath=self.directory.name))
        self.assertEqual("IMAGE_NOT_FOUND", result["error"]["code"])

    def test_unreadable_image_reports_failure_without_modifying_it(self):
        with mock.patch.object(Path, "read_bytes", side_effect=PermissionError):
            result = self.processor.process(self.request())
        self.assertEqual("IMAGE_UNREADABLE", result["error"]["code"])
        self.assertEqual(b"unchanged image evidence", self.image.read_bytes())

    def test_engine_is_reused_for_sequential_requests(self):
        calls = []
        def factory():
            calls.append("initialize")
            return lambda _: recognized()
        processor = worker.Worker(factory, self.log)
        processor.process(self.request())
        processor.process(self.request(id="request-2"))
        self.assertEqual(["initialize"], calls)

    def test_single_image_failure_then_next_request(self):
        calls = []
        def engine(_):
            calls.append("recognize")
            if len(calls) == 1:
                raise ValueError("invalid image")
            return recognized()
        processor = worker.Worker(lambda: engine, self.log)
        self.assertEqual("OCR_FAILED", processor.process(self.request())["error"]["code"])
        self.assertTrue(processor.process(self.request(id="request-2"))["success"])
        self.assertEqual(b"unchanged image evidence", self.image.read_bytes())

    def test_missing_model_returns_failure_and_preserves_image(self):
        processor = worker.Worker(lambda: worker.load_engine(self.image.parent / "missing-models"), self.log)
        result = processor.process(self.request())
        self.assertEqual("MISSING_MODEL", result["error"]["code"])
        self.assertEqual(b"unchanged image evidence", self.image.read_bytes())

    def test_initialization_failure_allows_a_later_request_to_retry(self):
        calls = []
        def factory():
            calls.append("initialize")
            if len(calls) == 1:
                raise worker.WorkerError("MISSING_MODEL", "Model unavailable")
            return lambda _: recognized()
        processor = worker.Worker(factory, self.log)
        self.assertEqual("MISSING_MODEL", processor.process(self.request())["error"]["code"])
        self.assertTrue(processor.process(self.request(id="request-2"))["success"])

    def test_empty_recognition_is_success_with_no_lines(self):
        processor = worker.Worker(lambda: lambda _: SimpleNamespace(txts=None, boxes=None, scores=None), self.log)
        self.assertEqual({"text": "", "lines": []}, processor.process(self.request())["result"])

    def test_stdout_is_json_lines_and_library_logs_go_to_stderr(self):
        def factory():
            print("library initialized")
            def engine(_):
                print("library inference")
                return recognized()
            return engine
        output = io.StringIO()
        worker.run(io.StringIO("{broken\n" + self.request() + "\n"), output, self.log, factory)
        results = [json.loads(line) for line in output.getvalue().splitlines()]
        self.assertEqual(2, len(results))
        self.assertFalse(results[0]["success"])
        self.assertTrue(results[1]["success"])
        self.assertIn("library initialized", self.log.getvalue())
        self.assertIn("library inference", self.log.getvalue())

    def test_eof_exits_without_initializing_engine(self):
        def factory():
            self.fail("EOF must not initialize models")
        output = io.StringIO()
        worker.run(io.StringIO(""), output, self.log, factory)
        self.assertEqual("", output.getvalue())


class LocalEngineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.directory.cleanup)
        directory = Path(cls.directory.name)
        marker = directory / "network-guard-active.txt"
        attempts = directory / "network-attempts.txt"
        # sitecustomize executes before the worker imports any third-party package.
        (directory / "sitecustomize.py").write_text(
            "import os, socket\n"
            "from pathlib import Path\n"
            "Path(os.environ['OCR_GUARD_MARKER']).write_text('active')\n"
            "def deny_network(*args, **kwargs):\n"
            "    Path(os.environ['OCR_GUARD_ATTEMPTS']).write_text('attempted')\n"
            "    raise RuntimeError('Network disabled in OCR integration test')\n"
            "socket.socket.connect = deny_network\n"
            "socket.socket.connect_ex = deny_network\n"
            "socket.create_connection = deny_network\n"
            "socket.getaddrinfo = deny_network\n", encoding="utf-8")
        fixture = ROOT / "tests" / "fixtures" / "ocr"
        cls.images = [fixture / name for name in (
            "synthetic-notice.png", "synthetic-long-notice.png", "synthetic-blank.png")]
        cls.originals = [image.read_bytes() for image in cls.images]
        corrupt = directory / "synthetic-corrupt.png"
        corrupt.write_bytes(b"this is intentionally not a PNG")
        requests = [{"id": "corrupt", "imagePath": str(corrupt)}]
        requests += [{"id": str(index), "imagePath": str(image)}
                     for index, image in enumerate(cls.images)]
        env = os.environ.copy()
        env.update(PYTHONPATH=str(directory), PYTHONIOENCODING="utf-8",
                   OCR_GUARD_MARKER=str(marker), OCR_GUARD_ATTEMPTS=str(attempts))
        cls.completed = subprocess.run(
            [sys.executable, str(ROOT / "ocr" / "worker.py")],
            input="\n".join(json.dumps(request, ensure_ascii=False) for request in requests) + "\n",
            capture_output=True, text=True, encoding="utf-8", env=env, timeout=90)
        cls.guard_active = marker.is_file()
        cls.network_attempted = attempts.is_file()
        cls.results = [json.loads(line) for line in cls.completed.stdout.splitlines()]

    def test_local_cpu_models_recognize_chinese_notice_and_date(self):
        self.assertEqual(0, self.completed.returncode, self.completed.stderr)
        self.assertEqual(4, len(self.results), self.completed.stderr)
        result = self.results[1]
        self.assertEqual("0", result["id"])
        self.assertTrue(result["success"], result)
        for expected in ("数学作业通知", "2026年10月12日18:00", "课程平台"):
            self.assertIn(expected, result["result"]["text"])
        for line in result["result"]["lines"]:
            self.assertEqual(4, len(line["box"]))
            self.assertTrue(all(len(point) == 2 for point in line["box"]))
            self.assertTrue(0 <= line["confidence"] <= 1)

    def test_synthetic_long_image_retains_bottom_deadline(self):
        result = self.results[2]
        self.assertTrue(result["success"], result)
        self.assertIn("2026年10月15日12:00", result["result"]["text"])

    def test_corrupt_image_does_not_stop_next_request(self):
        self.assertFalse(self.results[0]["success"])
        self.assertEqual("OCR_FAILED", self.results[0]["error"]["code"])
        self.assertTrue(self.results[1]["success"], self.results[1])

    def test_blank_image_returns_empty_result(self):
        result = self.results[3]
        self.assertTrue(result["success"], result)
        self.assertEqual({"text": "", "lines": []}, result["result"])

    def test_network_guard_is_active_and_no_network_was_attempted(self):
        self.assertTrue(self.guard_active)
        self.assertFalse(self.network_attempted)

    def test_synthetic_images_unchanged_after_inference(self):
        self.assertEqual(self.originals, [image.read_bytes() for image in self.images])

    def test_cpu_sessions_and_local_embedded_character_dictionary(self):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            engine = worker.load_engine()
            engine(self.originals[0])
        for stage in (engine.text_det, engine.text_cls, engine.text_rec):
            self.assertEqual(["CPUExecutionProvider"], stage.session.session.get_providers())
        self.assertIn("学", engine.text_rec.session.get_character_list())

    def test_missing_dictionary_download_fallback_is_disabled(self):
        from rapidocr.inference_engine.onnxruntime.main import OrtInferSession
        processor = worker.Worker(log=io.StringIO())
        with mock.patch.object(OrtInferSession, "have_key", return_value=False), \
                mock.patch.object(OrtInferSession, "get_dict_key_url", return_value=None), \
                mock.patch("requests.get", side_effect=RuntimeError("Unexpected network attempt")) as download:
            result = processor.process(json.dumps({"id": "missing-dictionary", "imagePath": str(self.images[0])}))
        self.assertFalse(result["success"])
        self.assertEqual("MISSING_MODEL", result["error"]["code"])
        download.assert_not_called()


if __name__ == "__main__":
    unittest.main()
