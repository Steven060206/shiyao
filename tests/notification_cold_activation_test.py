"""Exercise the published probe's cold COM callback; no notification is sent."""
import argparse
import ctypes
from ctypes import wintypes as w
import json
from pathlib import Path
import subprocess
import tempfile
import time
import uuid
import winreg


class GUID(ctypes.Structure):
    _fields_ = [('data', ctypes.c_ubyte * 16)]


def guid(value):
    return GUID((ctypes.c_ubyte * 16).from_buffer_copy(uuid.UUID(value).bytes_le))


def registration(executable):
    with winreg.OpenKey(winreg.HKEY_CURRENT_USER, r'Software\Classes\AppUserModelId') as root:
        for index in range(winreg.QueryInfoKey(root)[0]):
            app_id = winreg.EnumKey(root, index)
            try:
                with winreg.OpenKey(root, app_id) as key:
                    clsid = winreg.QueryValueEx(key, 'CustomActivator')[0]
                with winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                        r'Software\Classes\CLSID' + chr(92) + clsid + r'\LocalServer32') as key:
                    command = winreg.QueryValueEx(key, '')[0]
                if command.casefold() == ('"' + str(executable) + '" ----AppNotificationActivated:').casefold():
                    return app_id, clsid
            except FileNotFoundError:
                continue
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--executable', type=Path, required=True)
    options = parser.parse_args()
    executable = options.executable.resolve(strict=True)
    if executable.name != 'MessagePurify.Windows.IntegrationProbe.exe':
        parser.error('Use the published integration probe executable.')
    ole = ctypes.WinDLL('ole32')
    ole.CoInitializeEx.argtypes = [w.LPVOID, w.DWORD]
    ole.CoInitializeEx.restype = w.LONG
    ole.CoCreateInstance.argtypes = [ctypes.POINTER(GUID), w.LPVOID, w.DWORD,
                                   ctypes.POINTER(GUID), ctypes.POINTER(w.LPVOID)]
    ole.CoCreateInstance.restype = w.LONG
    assert ole.CoInitializeEx(None, 0) in (0, 1)
    try:
        with tempfile.TemporaryDirectory(prefix='message-purify-cold-') as temporary:
            report_path = Path(temporary) / 'cold.json'
            process = subprocess.Popen([str(executable), '----AppNotificationActivated:', '-Embedding',
                '--wait-seconds', '15', '--report', str(report_path)])
            deadline = time.monotonic() + 10
            registered = None
            while time.monotonic() < deadline and process.poll() is None:
                registered = registration(executable)
                if registered:
                    break
                time.sleep(0.1)
            assert registered is not None, 'Probe COM registration was not created.'
            pointer = w.LPVOID()
            status = ole.CoCreateInstance(ctypes.byref(guid(registered[1].strip('{}'))), None, 4,
                ctypes.byref(guid('53E31837-6600-4A81-9395-75CFFE746F94')), ctypes.byref(pointer))
            assert status == 0, f'COM activation failed: 0x{status & 0xffffffff:08X}'
            try:
                vtable = ctypes.cast(pointer, ctypes.POINTER(ctypes.POINTER(w.LPVOID))).contents
                activate = ctypes.WINFUNCTYPE(w.LONG, w.LPVOID, w.LPCWSTR, w.LPCWSTR,
                                             w.LPVOID, w.ULONG)(vtable[3])
                status = activate(pointer, registered[0],
                    'action=view&account=demo-account&item=demo-item&probe=controlled-cold', None, 0)
                assert status == 0, f'COM callback failed: 0x{status & 0xffffffff:08X}'
            finally:
                ctypes.WINFUNCTYPE(w.ULONG, w.LPVOID)(vtable[2])(pointer)
            assert process.wait(timeout=25) == 0, 'Probe failed.'
            report = json.loads(report_path.read_text(encoding='utf-8'))
            assert report['elevated'] is False, 'Run this test from a non-elevated desktop.'
            assert report['error'] is None, 'Probe reported an error.'
            assert report['activationObserved'] is True, 'First cold callback was not consumed.'
            assert report['activationKind'] == 'cold'
            assert report['activationAccountMatched'] is True
            print('PASS: cold COM callback located the synthetic account and item.')
    finally:
        ole.CoUninitialize()


if __name__ == '__main__':
    main()
