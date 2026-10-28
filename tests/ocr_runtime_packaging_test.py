"""Portable OCR runtime assembly uses only explicit, local inputs."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("prepare_ocr_runtime", ROOT / "tools/prepare_ocr_runtime.py")
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)


class RuntimePackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="OCR portable synthetic ")
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.archive = self.folder / "python.zip"
        with zipfile.ZipFile(self.archive, "w") as archive:
            for name in ("python.exe", "python312.dll", "python312.zip", "python312._pth", "LICENSE.txt"):
                archive.writestr(name, "synthetic")
        self.packages = self.folder / "site-packages"
        for name, content in {
            "rapidocr/__init__.py": "synthetic",
            "rapidocr/models/PP-OCRv6_det_small.onnx": "det",
            "rapidocr/models/ch_ppocr_mobile_v2.0_cls_mobile.onnx": "cls",
            "rapidocr/models/PP-OCRv6_rec_small.onnx": "rec",
            "rapidocr-3.10.0.dist-info/licenses/LICENSE": "license",
            "flatbuffers-25.12.19.dist-info/METADATA": "Name: flatbuffers\nVersion: 25.12.19\n",
            "pip/__init__.py": "omit",
            "pip-25.0.1.dist-info/METADATA": "omit",
            "rapidocr/__pycache__/old.pyc": "omit",
        }.items():
            target = self.packages / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8")
        self.output = self.folder / "portable"

    def build(self):
        return prepare.prepare_runtime(self.archive, self.packages, self.output,
            python_sha256=hashlib.sha256(self.archive.read_bytes()).hexdigest(),
            verify_dependencies=False)

    def test_copies_standalone_interpreter_worker_models_and_licenses(self):
        self.build()
        self.assertTrue((self.output / "python/python.exe").is_file())
        self.assertEqual((ROOT / "ocr/worker.py").read_bytes(), (self.output / "worker.py").read_bytes())
        paths = (self.output / "python/python312._pth").read_text(encoding="utf-8").splitlines()
        self.assertEqual(["python312.zip", ".", "Lib/site-packages"], paths)
        self.assertFalse((self.output / "python/Lib/site-packages/pip").exists())
        self.assertFalse((self.output / "python/Lib/site-packages/pip-25.0.1.dist-info").exists())
        self.assertFalse(list(self.output.rglob("*.pyc")))
        self.assertTrue((self.output / "python/Lib/site-packages/rapidocr-3.10.0.dist-info/licenses/LICENSE").is_file())
        self.assertIn("Apache License", (self.output / "licenses/FlatBuffers-LICENSE.txt").read_text())
        manifest = json.loads((self.output / "runtime-manifest.json").read_text())
        self.assertEqual("3.12.10", manifest["pythonVersion"])
        self.assertEqual(3, len(manifest["models"]))
        self.assertEqual(len([p for p in self.output.rglob("*") if p.is_file()]), manifest["files"])

    def test_existing_destination_is_rejected_without_overwrite(self):
        self.output.mkdir()
        sentinel = self.output / "keep.txt"
        sentinel.write_text("untouched")
        with self.assertRaisesRegex(ValueError, "exists"):
            self.build()
        self.assertEqual("untouched", sentinel.read_text())

    def test_bad_python_hash_is_rejected_before_destination_creation(self):
        with self.assertRaisesRegex(ValueError, "checksum"):
            prepare.prepare_runtime(self.archive, self.packages, self.output,
                python_sha256="0" * 64, verify_dependencies=False)
        self.assertFalse(self.output.exists())

    def test_unsafe_archive_path_is_rejected(self):
        with zipfile.ZipFile(self.archive, "a") as archive:
            archive.writestr("../outside.txt", "bad")
        with self.assertRaisesRegex(ValueError, "archive"):
            self.build()
        self.assertFalse(self.output.exists())

    def test_missing_model_is_rejected(self):
        (self.packages / "rapidocr/models/PP-OCRv6_rec_small.onnx").unlink()
        with self.assertRaisesRegex(ValueError, "model"):
            self.build()
        self.assertFalse(self.output.exists())


PORTABLE = ROOT / ".tools/ocr/portable"


@unittest.skipUnless((PORTABLE / "python/python.exe").is_file(), "Prepare the portable OCR runtime for integration tests")
class PortableEngineTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory(prefix="拾要 OCR 独立验包 ")
        cls.addClassCleanup(cls.temporary.cleanup)
        folder = Path(cls.temporary.name)
        cls.runtime = folder / "中文 空格程序" / "ocr"
        shutil.copytree(PORTABLE, cls.runtime)
        worker = cls.runtime / "worker.py"
        guard = folder / "guard-active"
        attempts = folder / "network-attempts"
        fixture = ROOT / "tests/fixtures/ocr"
        cls.images = [fixture / name for name in ("synthetic-notice.png", "synthetic-long-notice.png", "synthetic-blank.png")]
        cls.originals = [image.read_bytes() for image in cls.images]
        corrupt = folder / "synthetic-corrupt.png"
        corrupt.write_bytes(b"not an image")
        requests = [{"id": "corrupt", "imagePath": str(corrupt)}]
        requests += [{"id": str(index), "imagePath": str(image)} for index, image in enumerate(cls.images)]
        cls.requests = "\n".join(json.dumps(request, ensure_ascii=False) for request in requests) + "\n"
        launcher = """
import json,runpy,socket,sys
from pathlib import Path
worker,marker,attempts=sys.argv[1:]
Path(marker).write_text(json.dumps({'prefix':sys.prefix,'base':sys.base_prefix,'paths':sys.path}))
def deny(*args,**kwargs):
    Path(attempts).write_text('attempted')
    raise RuntimeError('OCR network disabled')
socket.socket.connect=deny
socket.socket.connect_ex=deny
socket.create_connection=deny
socket.getaddrinfo=deny
sys.argv=[worker]
runpy.run_path(worker,run_name='__main__')
"""
        environment = os.environ.copy()
        environment.update(PATH="", PYTHONHOME=str(folder / "no-system-python"),
                           PYTHONPATH=str(folder / "no-system-packages"), PYTHONDONTWRITEBYTECODE="1")
        cls.completed = subprocess.run([str(cls.runtime / "python/python.exe"), "-c", launcher,
            str(worker), str(guard), str(attempts)], input=cls.requests, capture_output=True,
            encoding="utf-8", env=environment, timeout=90)
        cls.results = [json.loads(line) for line in cls.completed.stdout.splitlines()]
        cls.guard = json.loads(guard.read_text()) if guard.exists() else None
        cls.network_attempted = attempts.exists()

    def test_embedded_python_runs_from_chinese_space_path_without_system_python(self):
        self.assertEqual(0, self.completed.returncode, self.completed.stderr)
        self.assertIsNotNone(self.guard)
        self.assertEqual((self.runtime / "python").resolve(), Path(self.guard["base"]).resolve())
        for path in self.guard["paths"]:
            self.assertTrue(Path(path).resolve().is_relative_to(self.runtime.resolve()), path)
        self.assertFalse(self.network_attempted)

    def test_portable_cpu_recognizes_chinese_long_image_and_blank(self):
        self.assertEqual(4, len(self.results), self.completed.stderr)
        self.assertFalse(self.results[0]["success"])
        self.assertTrue(self.results[1]["success"], self.results[1])
        self.assertIn("2026年10月12日18:00", self.results[1]["result"]["text"])
        self.assertTrue(self.results[2]["success"], self.results[2])
        self.assertIn("2026年10月15日12:00", self.results[2]["result"]["text"])
        self.assertEqual({"text": "", "lines": []}, self.results[3]["result"])
        self.assertEqual(self.originals, [image.read_bytes() for image in self.images])


if __name__ == "__main__":
    unittest.main()
