"""Exercise the actual release builder with clean and contaminated fixtures."""
import importlib.util
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("build_release", ROOT / "tools/build_release.py")
builder = importlib.util.module_from_spec(spec)
spec.loader.exec_module(builder)


class ReleasePackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="拾要 发布 ")
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.published = self.folder / "publish"
        (self.published / "ui").mkdir(parents=True)
        for name in ("MessagePurify.exe", "MessagePurify.dll", "ui/index.html", "MessagePurify.pdb"):
            (self.published / name).write_text("synthetic", encoding="utf-8")
        self.runtime = self.published / "ocr"
        self.write_runtime(self.runtime)
        self.vendor = self.folder / "original.zip"
        self.write_vendor()

    def write_runtime(self, runtime):
        for name in builder.OCR_REQUIRED_FILES:
            target = runtime / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("synthetic", encoding="utf-8")
        for name in ("worker.py", "requirements.lock.txt"):
            shutil.copyfile(ROOT / "ocr" / name, runtime / name)
        (runtime / "python/python312._pth").write_text("python312.zip\n.\nLib/site-packages\n", encoding="utf-8")
        dependencies = {}
        for line in (ROOT / "ocr/requirements.lock.txt").read_text(encoding="utf-8").splitlines():
            if not line.strip() or line.startswith("#"):
                continue
            name, version = line.split("==")
            normalized = name.lower().replace("_", "-")
            dependencies[normalized] = version
            metadata = runtime / "python/Lib/site-packages" / f"{name}-{version}.dist-info/METADATA"
            metadata.parent.mkdir(parents=True, exist_ok=True)
            metadata.write_text(f"Name: {name}\nVersion: {version}\n", encoding="utf-8")
        models = {}
        for name in builder.OCR_MODELS:
            model = runtime / "python/Lib/site-packages/rapidocr/models" / name
            model.parent.mkdir(parents=True, exist_ok=True)
            model.write_bytes(b"synthetic-model")
            models[name] = hashlib.sha256(model.read_bytes()).hexdigest()
        (runtime / "runtime-manifest.json").write_text(json.dumps({
            "pythonVersion": "3.12.10", "dependencies": dependencies, "models": models,
        }), encoding="utf-8")

    def write_vendor(self, extra=()):
        with zipfile.ZipFile(self.vendor, "w") as archive:
            for name in ("napimain.exe", "napiloader.dll", "napcat.mjs", "nativeLoader.cjs", *extra):
                entry = zipfile.ZipInfo(name)
                # ZipInfo normalizes backslashes on Windows; preserve the hostile fixture.
                entry.filename = name
                archive.writestr(entry, "synthetic")

    def build(self, ocr_runtime=None):
        return builder.build_release(self.published, self.vendor, self.folder / "out", "0.1.0-beta.1",
                                     ocr_runtime=ocr_runtime)

    def test_clean_package_contains_app_setup_original_vendor_and_no_user_config(self):
        result = self.build()
        with zipfile.ZipFile(result) as archive:
            names = archive.namelist()
            for name in ("app/MessagePurify.exe", "app/ui/index.html", "app/qq-sync-required",
                         "Setup.cmd", "Setup.ps1", "Start.cmd", "bridge/nativeLoaderPinned.cjs",
                         "licenses/NapCat-LICENSE.txt", "vendor/NapCat.Framework-v4.18.37.zip"):
                self.assertIn("Shiyao/" + name, names)
            self.assertEqual(archive.read("Shiyao/vendor/NapCat.Framework-v4.18.37.zip"), self.vendor.read_bytes())
            self.assertFalse(any(name.endswith(".pdb") for name in names))
            self.assertFalse(any(builder.private_file(Path(name)) for name in names))
            for name in (*builder.OCR_REQUIRED_FILES,
                         "python/Lib/site-packages/rapidocr/models/PP-OCRv6_rec_small.onnx"):
                self.assertEqual((self.runtime / name).read_bytes(), archive.read("Shiyao/app/ocr/" + name))

    def test_publish_local_data_is_rejected_before_zip_is_written(self):
        for name in ("messages.sqlite3", "qq-sync.json", "onebot11_12345.json", "active-account.txt", "WebView2/Cookies"):
            with self.subTest(name=name):
                target = self.published / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text("private-sentinel")
                with self.assertRaisesRegex(ValueError, "local data"):
                    self.build()
                self.assertFalse((self.folder / "out").exists())
                target.unlink()
                if target.parent != self.published:
                    target.parent.rmdir()

    def test_configured_or_unsafe_vendor_archive_is_rejected(self):
        for name in ("config/onebot11_12345.json", "logs/record.txt", "../outside.exe", "C:/outside", "bad\\entry"):
            with self.subTest(name=name):
                self.write_vendor([name])
                with self.assertRaises(ValueError):
                    self.build()

    def test_incomplete_publish_is_rejected(self):
        (self.published / "ui/index.html").unlink()
        with self.assertRaisesRegex(ValueError, "Incomplete"):
            self.build()

    def test_missing_ocr_runtime_is_rejected_before_zip_is_written(self):
        shutil.rmtree(self.runtime)
        with self.assertRaisesRegex(ValueError, "missing OCR"):
            self.build()
        self.assertFalse((self.folder / "out").exists())

    def test_missing_ocr_interpreter_worker_model_dependency_or_license_is_rejected(self):
        for name in ("python/python.exe", "worker.py", "python/python312.zip",
                     "python/Lib/site-packages/rapidocr/models/PP-OCRv6_rec_small.onnx",
                     "python/Lib/site-packages/onnxruntime/capi/onnxruntime_pybind11_state.pyd",
                     "python/Lib/site-packages/PIL/_imaging.cp312-win_amd64.pyd", "licenses/RapidOCR-LICENSE"):
            with self.subTest(name=name):
                target = self.runtime / name
                original = target.read_bytes()
                target.unlink()
                with self.assertRaisesRegex(ValueError, "OCR"):
                    self.build()
                self.assertFalse((self.folder / "out").exists())
                target.write_bytes(original)

    def test_stale_worker_modified_model_and_external_python_paths_are_rejected(self):
        for name in ("worker.py", "python/Lib/site-packages/rapidocr/models/PP-OCRv6_rec_small.onnx",
                     "python/python312._pth", "python/Lib/site-packages/rapidocr-3.10.0.dist-info/METADATA"):
            with self.subTest(name=name):
                target = self.runtime / name
                original = target.read_bytes()
                target.write_text("synthetic-modified", encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.build()
                self.assertFalse((self.folder / "out").exists())
                target.write_bytes(original)

    def test_explicit_portable_runtime_is_copied_without_replacing_existing_publish_ocr(self):
        source = self.folder / "portable"
        shutil.move(self.runtime, source)
        result = self.build(ocr_runtime=source)
        self.assertTrue(result.is_file())
        self.assertEqual((source / "worker.py").read_bytes(), (self.runtime / "worker.py").read_bytes())
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.build(ocr_runtime=source)

    def test_runtime_copy_rejects_destination_inside_its_source(self):
        self.published = self.runtime / "nested-publish"
        (self.published / "ui").mkdir(parents=True)
        for name in ("MessagePurify.exe", "MessagePurify.dll", "ui/index.html"):
            (self.published / name).write_text("synthetic", encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "outside the source OCR"):
            self.build(ocr_runtime=self.runtime)
        self.assertFalse((self.published / "ocr").exists())


if __name__ == "__main__":
    unittest.main()
