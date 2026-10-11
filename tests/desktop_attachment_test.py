"""Check the published desktop probe against the real Explorer hierarchy."""
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
    parser.add_argument("--mouse", action="store_true", help="Drag the resize handle; requires an uncovered desktop")
    args = parser.parse_args()
    executable = args.executable.resolve(strict=True)
    user32 = ctypes.WinDLL("user32", use_last_error=True)
    callback_type = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
    user32.EnumWindows.argtypes = [callback_type, w.LPARAM]
    user32.EnumChildWindows.argtypes = [w.HWND, callback_type, w.LPARAM]
    user32.GetWindowThreadProcessId.argtypes = [w.HWND, ctypes.POINTER(w.DWORD)]
    user32.GetWindowTextW.argtypes = [w.HWND, w.LPWSTR, ctypes.c_int]
    user32.GetClassNameW.argtypes = [w.HWND, w.LPWSTR, ctypes.c_int]
    user32.GetParent.argtypes = [w.HWND]
    user32.GetParent.restype = w.HWND
    user32.GetWindowLongPtrW.argtypes = [w.HWND, ctypes.c_int]
    user32.GetWindowLongPtrW.restype = ctypes.c_ssize_t
    user32.IsWindowVisible.argtypes = [w.HWND]
    user32.GetWindowRect.argtypes = [w.HWND, ctypes.POINTER(w.RECT)]
    user32.GetClientRect.argtypes = [w.HWND, ctypes.POINTER(w.RECT)]
    user32.ScreenToClient.argtypes = [w.HWND, ctypes.POINTER(w.POINT)]
    user32.PostMessageW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
    user32.SetThreadDpiAwarenessContext.argtypes = [w.HANDLE]
    user32.SetThreadDpiAwarenessContext.restype = w.HANDLE
    user32.SetThreadDpiAwarenessContext(ctypes.c_void_p(-4))
    user32.GetCursorPos.argtypes = [ctypes.POINTER(w.POINT)]
    user32.SetCursorPos.argtypes = [ctypes.c_int, ctypes.c_int]
    user32.WindowFromPoint.argtypes = [w.POINT]
    user32.WindowFromPoint.restype = w.HWND
    user32.mouse_event.argtypes = [w.DWORD, w.DWORD, w.DWORD, w.DWORD, ctypes.c_size_t]

    def windows(pid, include_children):
        found = []

        @callback_type
        def inspect(hwnd, _):
            owner = w.DWORD()
            user32.GetWindowThreadProcessId(hwnd, ctypes.byref(owner))
            if owner.value == pid:
                title = ctypes.create_unicode_buffer(256)
                user32.GetWindowTextW(hwnd, title, len(title))
                found.append((hwnd, title.value))
            return True

        @callback_type
        def root(hwnd, _):
            inspect(hwnd, 0)
            if include_children:
                user32.EnumChildWindows(hwnd, inspect, 0)
            return True

        user32.EnumWindows(root, 0)
        return found

    with tempfile.TemporaryDirectory(prefix="messagepurify-desktop-") as folder:
        report_path = Path(folder) / "report.json"
        process = subprocess.Popen([
            str(executable), "--interactive-desktop", "--wait-seconds", "5",
            "--report", str(report_path),
        ], cwd=executable.parent)
        try:
            deadline = time.monotonic() + 15
            report = {}
            while time.monotonic() < deadline and process.poll() is None:
                try:
                    report = json.loads(report_path.read_text(encoding="utf-8"))
                    if report.get("error") or report["desktop"]["status"] != "notRequested":
                        break
                except (FileNotFoundError, json.JSONDecodeError):
                    pass
                time.sleep(0.05)
            assert report.get("error") is None, report.get("error")
            desktop = report.get("desktop", {})
            assert desktop.get("status") == "attachedStructureOnly", desktop
            assert desktop.get("parentClass") == "SHELLDLL_DefView", desktop
            cards = [hwnd for hwnd, title in windows(process.pid, True)
                     if title == "消息提纯桌面验证"]
            assert len(cards) == 1, cards
            hwnd = cards[0]
            parent = user32.GetParent(hwnd)
            class_name = ctypes.create_unicode_buffer(256)
            user32.GetClassNameW(parent, class_name, len(class_name))
            assert class_name.value == "SHELLDLL_DefView", class_name.value
            assert user32.IsWindowVisible(hwnd), "Card window is hidden"
            assert user32.GetWindowLongPtrW(hwnd, -16) & 0x40000000, "Missing WS_CHILD"
            extended = user32.GetWindowLongPtrW(hwnd, -20)
            assert extended & 0x08000000, "Missing WS_EX_NOACTIVATE"
            assert not extended & 8, "Card must not be topmost"
            rectangle, client = w.RECT(), w.RECT()
            assert user32.GetWindowRect(hwnd, ctypes.byref(rectangle))
            assert user32.GetClientRect(parent, ctypes.byref(client))
            origin = w.POINT(rectangle.left, rectangle.top)
            assert user32.ScreenToClient(parent, ctypes.byref(origin))
            width, height = rectangle.right - rectangle.left, rectangle.bottom - rectangle.top
            assert 0 <= origin.x <= client.right - width and width > 0
            assert 0 <= origin.y <= client.bottom - height and height > 0
            visible_controls = [title for handle, title in windows(process.pid, False)
                                if user32.IsWindowVisible(handle)]
            assert visible_controls == [], visible_controls
            if args.mouse:
                # Test a point inside the enlarged handle but outside the old tiny grip.
                x, y = rectangle.right - 30, rectangle.bottom - 30
                previous = w.POINT()
                assert user32.GetCursorPos(ctypes.byref(previous))
                down = False
                try:
                    assert user32.WindowFromPoint(w.POINT(x, y)) == hwnd, "Resize handle is covered"
                    assert user32.SetCursorPos(x, y)
                    time.sleep(0.05)
                    assert user32.WindowFromPoint(w.POINT(x, y)) == hwnd, "Resize handle is covered"
                    user32.mouse_event(2, 0, 0, 0, 0)
                    down = True
                    for step in range(1, 7):
                        assert user32.SetCursorPos(x + step * 10, y + step * 5)
                        time.sleep(0.05)
                    user32.mouse_event(4, 0, 0, 0, 0)
                    down = False
                    time.sleep(0.05)
                    resized = w.RECT()
                    assert user32.GetWindowRect(hwnd, ctypes.byref(resized))
                    assert (resized.left, resized.top) == (rectangle.left, rectangle.top), "Resize moved the cards"
                    assert resized.right - resized.left == width + 60, "Width did not follow the pointer"
                    assert resized.bottom - resized.top == height + 30, "Height did not follow the pointer"
                finally:
                    if down:
                        user32.mouse_event(4, 0, 0, 0, 0)
                    user32.SetCursorPos(previous.x, previous.y)
        finally:
            # Only close the top-level control window owned by the process started here.
            for hwnd, _ in windows(process.pid, False):
                user32.PostMessageW(hwnd, 0x0010, 0, 0)
            process.wait(timeout=10)
        assert process.returncode == 0, process.returncode
        print("PASS: published cards use the desktop icon view, have visible/non-topmost "
              "styles and valid bounds, with no warning window. Manual Win+D and mouse checks remain.")


if __name__ == "__main__":
    main()
