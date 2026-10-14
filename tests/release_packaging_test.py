"""Exercise the actual release builder with clean and contaminated fixtures."""
import importlib.util
from pathlib import Path
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
        self.vendor = self.folder / "original.zip"
        self.write_vendor()

    def write_vendor(self, extra=()):
        with zipfile.ZipFile(self.vendor, "w") as archive:
            for name in ("napimain.exe", "napiloader.dll", "napcat.mjs", "nativeLoader.cjs", *extra):
                entry = zipfile.ZipInfo(name)
                # ZipInfo normalizes backslashes on Windows; preserve the hostile fixture.
                entry.filename = name
                archive.writestr(entry, "synthetic")

    def build(self):
        return builder.build_release(self.published, self.vendor, self.folder / "out", "0.1.0-beta.1")

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


if __name__ == "__main__":
    unittest.main()
