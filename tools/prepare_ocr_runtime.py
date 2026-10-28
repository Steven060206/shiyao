"""Assemble a portable offline OCR runtime from an official embedded Python ZIP."""
import argparse
from email.parser import Parser
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import stat
import zipfile

ROOT = Path(__file__).resolve().parents[1]
PYTHON_VERSION = "3.12.10"
PYTHON_SHA256 = "4acbed6dd1c744b0376e3b1cf57ce906f9dc9e95e68824584c8099a63025a3c3"
MODEL_HASHES = {
    "PP-OCRv6_det_small.onnx": "090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f",
    "ch_ppocr_mobile_v2.0_cls_mobile.onnx": "e47acedf663230f8863ff1ab0e64dd2d82b838fceb5957146dab185a89d6215c",
    "PP-OCRv6_rec_small.onnx": "6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884",
}


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def dependency_versions(packages):
    actual = {}
    for metadata in packages.glob("*.dist-info/METADATA"):
        fields = Parser().parsestr(metadata.read_text(encoding="utf-8"))
        actual[fields["Name"].lower().replace("_", "-")] = fields["Version"]
    expected = {}
    for line in (ROOT / "ocr/requirements.lock.txt").read_text(encoding="utf-8").splitlines():
        if line.strip() and not line.startswith("#"):
            name, version = line.strip().split("==")
            expected[name.lower().replace("_", "-")] = version
    if any(actual.get(name) != version for name, version in expected.items()):
        raise ValueError("OCR dependencies do not match requirements.lock.txt")
    return expected


def prepare_runtime(python_archive, site_packages, output_directory, *,
                    python_sha256=PYTHON_SHA256, verify_dependencies=True):
    archive_path = Path(python_archive).resolve(strict=True)
    packages = Path(site_packages).resolve(strict=True)
    output = Path(output_directory).absolute()
    if output.exists():
        raise ValueError("Runtime destination already exists")
    if not packages.is_dir():
        raise ValueError("site-packages directory is missing")
    if digest(archive_path) != python_sha256:
        raise ValueError("Embedded Python archive checksum mismatch")
    if packages == output or packages in output.parents:
        raise ValueError("Runtime destination must be outside source packages")
    versions = dependency_versions(packages) if verify_dependencies else {}
    models = {}
    for name, expected in MODEL_HASHES.items():
        path = packages / "rapidocr/models" / name
        if not path.is_file() or (verify_dependencies and digest(path) != expected):
            raise ValueError("Missing or mismatched local OCR model")
        models[name] = digest(path)
    files = [path for path in packages.rglob("*") if path.is_file()
             and "__pycache__" not in path.parts and path.suffix.lower() not in {".pyc", ".pyo"}
             and not path.relative_to(packages).parts[0].lower().startswith(("pip-", "pip."))
             and path.relative_to(packages).parts[0].lower() != "pip"]
    if any(path.is_symlink() or any(parent.is_symlink() for parent in path.parents if parent != packages.parent) for path in files):
        raise ValueError("Runtime package source contains a symbolic link")
    with zipfile.ZipFile(archive_path) as archive:
        names = archive.namelist()
        required = {"python.exe", "python312.dll", "python312.zip", "python312._pth", "LICENSE.txt"}
        if not required.issubset(names) or len(names) != len(set(names)):
            raise ValueError("Incomplete embedded Python archive")
        for entry in archive.infolist():
            name = entry.filename
            if (name.startswith(("/", "\\")) or ".." in PurePosixPath(name).parts
                    or ":" in name or "\\" in name or stat.S_ISLNK(entry.external_attr >> 16)):
                raise ValueError("Unsafe embedded Python archive")
        if archive.testzip() is not None:
            raise ValueError("Corrupt embedded Python archive")
        python = output / "python"
        python.mkdir(parents=True)
        archive.extractall(python)
    (python / "python312._pth").write_text("python312.zip\n.\nLib/site-packages\n", encoding="utf-8")
    for source in files:
        destination = python / "Lib/site-packages" / source.relative_to(packages)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
    shutil.copyfile(ROOT / "ocr/worker.py", output / "worker.py")
    shutil.copyfile(ROOT / "ocr/requirements.lock.txt", output / "requirements.lock.txt")
    licenses = output / "licenses"
    shutil.copytree(ROOT / "ocr/licenses", licenses)
    apache = (ROOT / "ocr/licenses/RapidOCR-LICENSE").read_text(encoding="utf-8")
    apache = apache[apache.index("                                 Apache License"):]
    (licenses / "FlatBuffers-LICENSE.txt").write_text(apache, encoding="utf-8")
    (licenses / "FlatBuffers-NOTICE.txt").write_text(
        "FlatBuffers Python runtime 25.12.19\nCopyright 2014 Google Inc. All rights reserved.\n"
        "Licensed under the Apache License, Version 2.0. See FlatBuffers-LICENSE.txt.\n",
        encoding="utf-8")
    manifest = {"pythonVersion": PYTHON_VERSION, "pythonArchiveSha256": digest(archive_path),
                "pythonSource": "https://www.python.org/downloads/release/python-31210/",
                "dependencies": versions, "models": models,
                "files": len([p for p in output.rglob("*") if p.is_file()]) + 1}
    (output / "runtime-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return output


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--python-archive", type=Path, required=True)
    parser.add_argument("--site-packages", type=Path, required=True)
    parser.add_argument("--output-directory", type=Path, required=True)
    args = parser.parse_args()
    output = prepare_runtime(args.python_archive, args.site_packages, args.output_directory)
    files = [path for path in output.rglob("*") if path.is_file()]
    print(json.dumps({"success": True, "pythonVersion": PYTHON_VERSION,
                      "files": len(files), "bytes": sum(path.stat().st_size for path in files)}))


if __name__ == "__main__":
    main()
