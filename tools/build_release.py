"""Create a clean Windows download ZIP from a freshly published application."""
import argparse
from email.parser import Parser
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]
OCR_MODELS = ("PP-OCRv6_det_small.onnx", "ch_ppocr_mobile_v2.0_cls_mobile.onnx", "PP-OCRv6_rec_small.onnx")
OCR_REQUIRED_FILES = (
    "worker.py", "requirements.lock.txt", "runtime-manifest.json",
    "python/python.exe", "python/python312.dll", "python/python312.zip", "python/python312._pth", "python/LICENSE.txt",
    "python/Lib/site-packages/rapidocr/__init__.py", "python/Lib/site-packages/rapidocr/config.yaml",
    "python/Lib/site-packages/rapidocr/default_models.yaml",
    "python/Lib/site-packages/onnxruntime/capi/onnxruntime.dll",
    "python/Lib/site-packages/onnxruntime/capi/onnxruntime_pybind11_state.pyd",
    "python/Lib/site-packages/onnxruntime/capi/onnxruntime_providers_shared.dll",
    "python/Lib/site-packages/numpy/_core/_multiarray_umath.cp312-win_amd64.pyd",
    "python/Lib/site-packages/cv2/cv2.pyd", "python/Lib/site-packages/PIL/_imaging.cp312-win_amd64.pyd",
    "licenses/RapidOCR-LICENSE", "licenses/RapidOCR-MODEL_LICENSES.md", "licenses/ONNXRuntime-LICENSE",
    "licenses/FlatBuffers-LICENSE.txt", "licenses/FlatBuffers-NOTICE.txt",
)


def validate_ocr_runtime(runtime, root=ROOT):
    runtime = Path(runtime).resolve(strict=True)
    for name in OCR_REQUIRED_FILES:
        path = runtime / name
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"Incomplete OCR runtime: {name}")
    for name in ("worker.py", "requirements.lock.txt"):
        if (runtime / name).read_bytes() != (root / "ocr" / name).read_bytes():
            raise ValueError(f"OCR runtime does not match source: {name}")
    if (runtime / "python/python312._pth").read_text(encoding="utf-8").splitlines() != ["python312.zip", ".", "Lib/site-packages"]:
        raise ValueError("OCR runtime must use only bundled Python paths")
    expected = {}
    for line in (runtime / "requirements.lock.txt").read_text(encoding="utf-8").splitlines():
        if line.strip() and not line.startswith("#"):
            name, version = line.strip().split("==")
            expected[name.lower().replace("_", "-")] = version
    packages = runtime / "python/Lib/site-packages"
    actual = {}
    for metadata in packages.glob("*.dist-info/METADATA"):
        fields = Parser().parsestr(metadata.read_text(encoding="utf-8"))
        if not fields["Name"] or not fields["Version"]:
            raise ValueError("OCR dependency metadata is incomplete")
        actual[fields["Name"].lower().replace("_", "-")] = fields["Version"]
    manifest = json.loads((runtime / "runtime-manifest.json").read_text(encoding="utf-8"))
    if (not isinstance(manifest, dict) or manifest.get("pythonVersion") != "3.12.10" or manifest.get("dependencies") != expected
            or any(actual.get(name) != version for name, version in expected.items())
            or not isinstance(manifest.get("models"), dict) or set(manifest["models"]) != set(OCR_MODELS)):
        raise ValueError("OCR runtime versions or models do not match the locked dependencies")
    for name in OCR_MODELS:
        model = packages / "rapidocr/models" / name
        if not model.is_file():
            raise ValueError(f"Incomplete OCR runtime model: {name}")
        with model.open("rb") as stream:
            if hashlib.file_digest(stream, "sha256").hexdigest() != manifest["models"][name]:
                raise ValueError(f"OCR runtime model checksum mismatch: {name}")
    return runtime


def private_file(relative):
    name = relative.name.lower()
    return (relative.suffix.lower() in {".sqlite3", ".db", ".log", ".lnk"}
            or name in {"qq-sync.json", "active-account.txt", "app-settings.json",
                        "desktop-layout.json", "interface-preferences.json", "validation.json"}
            or name.startswith(("onebot11", "webui"))
            or any(part.lower() in {"config", "logs", "webview2", "accounts"} for part in relative.parts))


def build_release(published, napcat_archive, output, version, root=ROOT, ocr_runtime=None):
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?", version):
        raise ValueError("Invalid release version")
    published = Path(published).resolve(strict=True)
    napcat_archive = Path(napcat_archive).resolve(strict=True)
    for required in ("MessagePurify.exe", "MessagePurify.dll", "ui/index.html"):
        if not (published / required).is_file():
            raise ValueError(f"Incomplete publish directory: {required}")
    if ocr_runtime is not None:
        source_runtime = validate_ocr_runtime(ocr_runtime, root)
        if source_runtime in (published / "ocr").parents:
            raise ValueError("Publish destination must be outside the source OCR runtime")
        if (published / "ocr").exists():
            raise ValueError("Publish OCR directory already exists; use a clean publish directory")
        if any(path.is_symlink() or private_file(path.relative_to(source_runtime)) for path in source_runtime.rglob("*")):
            raise ValueError("OCR runtime contains local data or symbolic links")
        shutil.copytree(source_runtime, published / "ocr")
    if not (published / "ocr").is_dir():
        raise ValueError("Incomplete publish directory: missing OCR runtime")
    validate_ocr_runtime(published / "ocr", root)
    application_files = sorted(p for p in published.rglob("*") if p.is_file())
    if any(p.is_symlink() or private_file(p.relative_to(published)) for p in application_files):
        raise ValueError("Publish directory contains local data or configuration; use a clean publish directory")
    with zipfile.ZipFile(napcat_archive) as original:
        names = original.namelist()
        if not {"napimain.exe", "napiloader.dll", "napcat.mjs", "nativeLoader.cjs"}.issubset(names):
            raise ValueError("Not an original NapCat Framework archive")
        for entry in original.infolist():
            name = entry.orig_filename
            parts = PurePosixPath(name).parts
            if name.startswith(("/", "\\")) or ".." in parts or "\\" in name or ":" in name:
                raise ValueError("Unsafe archive entry")
            if name.lower().startswith(("logs/", "config/onebot11", "config/webui")):
                raise ValueError("NapCat archive contains local configuration")
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f"Shiyao-{version}-win-x64.zip"
    sources = {f"app/{p.relative_to(published).as_posix()}": p
               for p in application_files if p.suffix.lower() != ".pdb"}
    for name in ("Setup.ps1", "Setup.cmd", "Start.cmd", "THIRD-PARTY-NOTICES.md"):
        sources[name] = root / "packaging/release" / name
    for name in ("Prepare-PinnedGroups.ps1", "nativeLoaderPinned.cjs", "pinned-groups.mjs"):
        sources[f"bridge/{name}"] = root / "packaging/napcat" / name
    for name in ("Start-MessagePurify.ps1", "Install-MessagePurifyShortcuts.ps1", "ShortcutIdentity.cs"):
        sources[f"app-tools/{name}"] = root / "packaging/app" / name
    for license_file in (root / "packaging/release/licenses").glob("*"):
        sources[f"licenses/{license_file.name}"] = license_file
    sources["README.md"] = root / "README.md"
    sources["LICENSE"] = root / "LICENSE"
    sources["vendor/NapCat.Framework-v4.18.37.zip"] = napcat_archive
    for source in sources.values():
        if not source.is_file():
            raise ValueError(f"Missing distribution file: {source.name}")
    with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
        for name, source in sources.items():
            archive.write(source, "Shiyao/" + name)
        archive.writestr("Shiyao/app/qq-sync-required", "Run Setup.cmd before synchronizing QQ.\n")
        archive.writestr("Shiyao/VERSION.txt", version + "\n")
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish-directory", type=Path, required=True)
    parser.add_argument("--napcat-archive", type=Path, required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--ocr-runtime-directory", type=Path,
                        help="Copy an already prepared portable runtime into a clean publish/ocr directory")
    args = parser.parse_args()
    result = build_release(args.publish_directory, args.napcat_archive, args.output_directory, args.version,
                           ocr_runtime=args.ocr_runtime_directory)
    print(json.dumps({"archive": str(result), "bytes": result.stat().st_size}))


if __name__ == "__main__":
    main()
