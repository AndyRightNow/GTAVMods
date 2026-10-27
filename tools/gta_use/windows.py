"""Windows readers compatible with atomic file replacement."""
import ctypes as c
from ctypes import wintypes as w
from functools import cache
import json
import os
import time


@cache
def api():
    dll = c.WinDLL("kernel32", use_last_error=True)
    signatures = {
        "CreateFileW": (w.HANDLE, [w.LPCWSTR, w.DWORD, w.DWORD, c.c_void_p, w.DWORD, w.DWORD, w.HANDLE]),
        "CloseHandle": (w.BOOL, [w.HANDLE]),
    }
    for name, (result, args) in signatures.items():
        fn = getattr(dll, name)
        fn.restype, fn.argtypes = result, args
    return dll


def read_json(path):
    deadline = time.monotonic() + .2
    while True:
        try:
            return _read_json_once(path)
        except (FileNotFoundError, PermissionError):
            # ReplaceFile/scanners can briefly hide or lock the directory entry.
            # Retry this read only; never replay a command to recover its result.
            if time.monotonic() >= deadline:
                raise
            time.sleep(.005)


def _read_json_once(path):
    if os.name != "nt":
        with open(path, encoding="utf-8-sig") as stream:
            return json.load(stream)
    import msvcrt
    dll = api()
    handle = dll.CreateFileW(str(path), 0x80000000, 7, None, 3, 0, None)
    if handle == c.c_void_p(-1).value:
        raise c.WinError(c.get_last_error())
    try:
        fd = msvcrt.open_osfhandle(handle, os.O_RDONLY | os.O_BINARY)
    except BaseException:
        dll.CloseHandle(handle)
        raise
    with os.fdopen(fd, "r", encoding="utf-8-sig") as stream:
        return json.load(stream)
