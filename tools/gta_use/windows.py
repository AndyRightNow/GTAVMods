"""Bounded overlapped pipe I/O and readers compatible with atomic file replacement."""
import ctypes as c
from ctypes import wintypes as w
import json
import os
import struct
import time


class Overlapped(c.Structure):
    _fields_ = [("Internal", c.c_size_t), ("InternalHigh", c.c_size_t),
                ("Offset", w.DWORD), ("OffsetHigh", w.DWORD), ("hEvent", w.HANDLE)]


def api():
    dll = c.WinDLL("kernel32", use_last_error=True)
    signatures = {
        "CreateFileW": (w.HANDLE, [w.LPCWSTR, w.DWORD, w.DWORD, c.c_void_p, w.DWORD, w.DWORD, w.HANDLE]),
        "CreateEventW": (w.HANDLE, [c.c_void_p, w.BOOL, w.BOOL, w.LPCWSTR]),
        "ReadFile": (w.BOOL, [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.POINTER(Overlapped)]),
        "WriteFile": (w.BOOL, [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.POINTER(Overlapped)]),
        "GetOverlappedResult": (w.BOOL, [w.HANDLE, c.POINTER(Overlapped), c.POINTER(w.DWORD), w.BOOL]),
        "CancelIoEx": (w.BOOL, [w.HANDLE, c.POINTER(Overlapped)]),
        "WaitForSingleObject": (w.DWORD, [w.HANDLE, w.DWORD]),
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


def exchange(pipe, payload, timeout):
    """Retry only connection acquisition. Once bytes are sent, never replay."""
    dll = api()
    deadline = time.monotonic() + timeout
    while True:
        # Request data I/O only. GENERIC_WRITE also grants FILE_CREATE_PIPE_INSTANCE.
        handle = dll.CreateFileW("\\\\.\\pipe\\" + pipe, 0x00100003, 0, None, 3, 0x40110000, None)
        if handle != c.c_void_p(-1).value:
            break
        error = c.get_last_error()
        if error not in (2, 231) or time.monotonic() >= deadline:
            raise c.WinError(error)
        time.sleep(.01)

    def transfer(data, reading):
        buffer = c.create_string_buffer(data if not reading else data)
        size = data if reading else len(data)
        event = dll.CreateEventW(None, True, False, None)
        if not event:
            raise c.WinError(c.get_last_error())
        overlapped = Overlapped(hEvent=event)
        count = w.DWORD()
        try:
            fn = dll.ReadFile if reading else dll.WriteFile
            if not fn(handle, buffer, size, c.byref(count), c.byref(overlapped)):
                error = c.get_last_error()
                if error != 997:
                    raise c.WinError(error)
                remaining = max(0, int((deadline - time.monotonic()) * 1000))
                if dll.WaitForSingleObject(event, remaining) != 0:
                    dll.CancelIoEx(handle, c.byref(overlapped))
                    dll.GetOverlappedResult(handle, c.byref(overlapped), c.byref(count), True)
                    raise TimeoutError("Bridge I/O timed out; inspect the request receipt before retrying.")
                if not dll.GetOverlappedResult(handle, c.byref(overlapped), c.byref(count), False):
                    raise c.WinError(c.get_last_error())
            if count.value == 0:
                raise EOFError("Bridge disconnected.")
            return buffer.raw[:count.value] if reading else count.value
        finally:
            dll.CloseHandle(event)

    def receive(length):
        data = bytearray()
        while len(data) < length:
            data.extend(transfer(length - len(data), True))
        return data

    try:
        frame = struct.pack("<I", len(payload)) + payload
        while frame:
            frame = frame[transfer(frame, False):]
        size, = struct.unpack("<I", receive(4))
        if not 0 < size <= 4 * 1024 * 1024:
            raise ValueError("Invalid response frame size.")
        return bytes(receive(size))
    finally:
        dll.CloseHandle(handle)
