"""Verify Windows shell URI dispatch and the visible probe window; sends no toast."""
import argparse
import ctypes
from ctypes import wintypes as w
import json
import os
from pathlib import Path
import time
import uuid
import winreg


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--executable', type=Path, required=True)
    options = parser.parse_args()
    executable = options.executable.resolve(strict=True)
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                        r'Software\Classes\messagepurify-probe\shell\open\command') as key:
        command = winreg.QueryValueEx(key, '')[0]
    expected = f'"{executable}" --open-notification "%1" --wait-seconds 600'
    assert command == expected, 'Protocol is not registered to the tested executable.'
    probe_id = 'controlled-shell-' + uuid.uuid4().hex
    results = Path(os.environ['LOCALAPPDATA']) / 'MessagePurify/IntegrationProbe/results'
    started = time.time()
    os.startfile('messagepurify-probe://view/?action=view&account=demo-account&item=demo-item&probe=' + probe_id)
    report = None
    for _ in range(100):
        for candidate in results.glob('protocol-*.json'):
            if candidate.stat().st_mtime < started:
                continue
            try:
                value = json.loads(candidate.read_text(encoding='utf-8'))
            except (OSError, ValueError):
                continue
            if value.get('notificationProbeId') == probe_id and value.get('activationObserved'):
                report = value
                break
        if report:
            break
        time.sleep(0.1)
    assert report is not None, 'Windows did not dispatch the protocol URI.'
    assert report['error'] is None
    assert report['activationKind'] == 'protocol'
    assert report['activationAccountMatched'] is True
    user = ctypes.WinDLL('user32', use_last_error=True)
    user.FindWindowW.argtypes = [w.LPCWSTR, w.LPCWSTR]
    user.FindWindowW.restype = w.HWND
    user.IsWindowVisible.argtypes = [w.HWND]
    user.PostMessageW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
    window = user.FindWindowW(None, '消息提纯：Windows 集成验证')
    assert window and user.IsWindowVisible(window), 'Details window is not visible.'
    user.PostMessageW(window, 0x0010, 0, 0)
    print('PASS: Windows shell protocol opened the visible synthetic item window.')


if __name__ == '__main__':
    main()
