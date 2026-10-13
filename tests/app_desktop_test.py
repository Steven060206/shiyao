"""Check the published application against the real Explorer desktop using synthetic data."""
import argparse
import ctypes
from ctypes import wintypes as w
import json
from pathlib import Path
import subprocess
import tempfile
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--executable", type=Path, required=True)
    args = parser.parse_args()
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
    user32.EnumWindows.argtypes = [callback_type, w.LPARAM]
    user32.EnumChildWindows.argtypes = [w.HWND, callback_type, w.LPARAM]
    user32.GetWindowThreadProcessId.argtypes = [w.HWND, ctypes.POINTER(w.DWORD)]
    user32.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, ctypes.c_int]
    user32.GetParent.argtypes = [w.HWND]
    user32.GetParent.restype = w.HWND
    user32.GetClassNameW.argtypes = [w.HWND, w.LPWSTR, ctypes.c_int]
    user32.GetWindowLongPtrW.argtypes = [w.HWND, ctypes.c_int]
    user32.GetWindowLongPtrW.restype = ctypes.c_ssize_t
    user32.IsWindowVisible.argtypes = [w.HWND]
    user32.IsWindowVisible.restype = w.BOOL
    with tempfile.TemporaryDirectory(prefix="messagepurify-formal-desktop-") as folder:
        report_path = Path(folder) / "native-smoke.json"
        process = subprocess.Popen([str(args.executable.resolve(strict=True)), "--smoke-test", "--hold-seconds", "3", "--report", str(report_path)])
        try:
            deadline = time.monotonic() + 40
            while not report_path.exists() and process.poll() is None and time.monotonic() < deadline:
                time.sleep(0.05)
            report = json.loads(report_path.read_text(encoding="utf-8"))
            assert report["success"], report
            desktop = report.get("desktop")
            assert desktop and desktop["tasks"]["parentClass"] == "SHELLDLL_DefView", "Formal desktop cards not attached"
            found = {}

            @callback_type
            def inspect(hwnd, _):
                owner = w.DWORD()
                user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
                if owner.value == process.pid:
                    title = ctypes.create_unicode_buffer(256)
                    user32.GetWindowTextW(hwnd, title, len(title))
                    if title.value in ("拾要 · 提醒事项", "拾要 · 待确认", "消息提纯 · 提醒事项", "消息提纯 · 待确认"):
                        found[title.value.split(" · ", 1)[1]] = hwnd
                return True

            @callback_type
            def root(hwnd, _):
                inspect(hwnd, 0)
                user32.EnumChildWindows(hwnd, inspect, 0)
                return True

            user32.EnumWindows(root, 0)
            assert len(found) == 2, found
            for hwnd in found.values():
                name = ctypes.create_unicode_buffer(256)
                user32.GetClassNameW(user32.GetParent(hwnd), name, len(name))
                assert name.value == "SHELLDLL_DefView", name.value
                assert user32.GetWindowLongPtrW(hwnd, -16) & 0x40000000, "Missing WS_CHILD"
                extended = user32.GetWindowLongPtrW(hwnd, -20)
                assert extended & 0x08000000, "Missing WS_EX_NOACTIVATE"
                assert not extended & 8, "Desktop card must not be topmost"
            assert user32.IsWindowVisible(found["提醒事项"])
            expected_pending = report["pendingCount"] + report["mergeSuggestionCount"] > 0
            assert bool(user32.IsWindowVisible(found["待确认"])) == expected_pending, "Pending visibility must match final synthetic data"
            assert process.wait(timeout=15) == 0
            print(json.dumps({"success": True, "synthetic": True, "checks": report["checks"], "parentClass": "SHELLDLL_DefView", "windows": 2}, ensure_ascii=False))
        finally:
            if process.poll() is None:
                process.terminate()
                process.wait(timeout=10)


if __name__ == "__main__":
    main()
