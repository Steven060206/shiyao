"""Create a clean Windows download ZIP from a freshly published application."""
import argparse
import json
from pathlib import Path, PurePosixPath
import re
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def private_file(relative):
    name = relative.name.lower()
    return (relative.suffix.lower() in {".sqlite3", ".db", ".log", ".lnk"}
            or name in {"qq-sync.json", "active-account.txt", "app-settings.json",
                        "desktop-layout.json", "interface-preferences.json", "validation.json"}
            or name.startswith(("onebot11", "webui"))
            or any(part.lower() in {"config", "logs", "webview2", "accounts"} for part in relative.parts))


def build_release(published, napcat_archive, output, version, root=ROOT):
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?", version):
        raise ValueError("Invalid release version")
    published = Path(published).resolve(strict=True)
    napcat_archive = Path(napcat_archive).resolve(strict=True)
    for required in ("MessagePurify.exe", "MessagePurify.dll", "ui/index.html"):
        if not (published / required).is_file():
            raise ValueError(f"Incomplete publish directory: {required}")
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
    args = parser.parse_args()
    result = build_release(args.publish_directory, args.napcat_archive, args.output_directory, args.version)
    print(json.dumps({"archive": str(result), "bytes": result.stat().st_size}))


if __name__ == "__main__":
    main()
