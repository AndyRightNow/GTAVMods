"""External game boundary for scenarios using the shipped Rust server and Python runner.

Only engine observations are supplied here. The real companion owns HTTP, WS,
authentication, framing, correlation and jobs; no server implementation is mocked.
"""
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import threading
import time
import uuid
from gta_use.segment import save_json
from gta_use.windows import read_json


class ObservedGame:
    def __init__(self, root):
        self.root = self.directory = Path(root)
        self.process = os.getpid()
        self.session = uuid.uuid4().hex
        self.requests = []

    def request(self, envelope):
        self.requests.append(envelope)
        return dict(id=envelope['id'], session=self.session, ok=True,
                    result={'gameTime': 150, 'sections': {'player': {'entity': {'health': 175, 'dead': False}}}})


class Overlapped(c.Structure):
    _fields_ = [('Internal', c.c_size_t), ('InternalHigh', c.c_size_t),
                ('Offset', w.DWORD), ('OffsetHigh', w.DWORD), ('hEvent', w.HANDLE)]


class RustScene:
    def __init__(self, game):
        self.game, self.stopped, self.errors = game, threading.Event(), []
        self.pipe = 'GTAVMods-Python-Scenario-' + uuid.uuid4().hex
        self.dll = c.WinDLL('kernel32', use_last_error=True)
        signatures = {
            'CreateNamedPipeW': (w.HANDLE, [w.LPCWSTR, w.DWORD, w.DWORD, w.DWORD, w.DWORD, w.DWORD, w.DWORD, c.c_void_p]),
            'CreateEventW': (w.HANDLE, [c.c_void_p, w.BOOL, w.BOOL, w.LPCWSTR]),
            'ConnectNamedPipe': (w.BOOL, [w.HANDLE, c.POINTER(Overlapped)]),
            'ReadFile': (w.BOOL, [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.POINTER(Overlapped)]),
            'WriteFile': (w.BOOL, [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.POINTER(Overlapped)]),
            'GetOverlappedResult': (w.BOOL, [w.HANDLE, c.POINTER(Overlapped), c.POINTER(w.DWORD), w.BOOL]),
            'CancelIoEx': (w.BOOL, [w.HANDLE, c.POINTER(Overlapped)]),
            'WaitForSingleObject': (w.DWORD, [w.HANDLE, w.DWORD]),
            'CloseHandle': (w.BOOL, [w.HANDLE]),
        }
        for name, (result, args) in signatures.items():
            fn = getattr(self.dll, name)
            fn.restype, fn.argtypes = result, args
        save_json(game.root / f'bridge-{game.process}.json', {
            'process': game.process, 'pipe': self.pipe,
            'bridge': {'session': game.session, 'directory': str(game.directory), 'state': 'ready'}})
        built = Path(__file__).resolve().parents[3] / 'artifacts/rust/release/gta-use-server.exe'
        executable = Path(os.environ.get('GTA_USE_RUST_TEST_EXECUTABLE', built))
        if hashlib.sha256(executable.read_bytes()).digest() != hashlib.sha256(built.read_bytes()).digest():
            raise AssertionError('The staged Rust server must match the current release build.')
        self.thread = threading.Thread(target=self.serve, daemon=True)
        self.thread.start()
        self.process = None
        try:
            self.process = subprocess.Popen([str(executable), str(game.root), str(game.process), sys.executable],
                                            creationflags=subprocess.CREATE_NO_WINDOW)
            deadline = time.monotonic() + 10
            discovery = game.root / f'service-{game.process}.json'
            while not discovery.exists():
                if self.process.poll() is not None or time.monotonic() >= deadline:
                    raise AssertionError('Rust companion failed to start: ' + str(list(game.root.glob('*stdio.log'))))
                time.sleep(.01)
            assert read_json(discovery)['backend'] == 'axum'
        except BaseException:
            self.close()
            raise

    def transfer(self, handle, operation, data=None):
        event = self.dll.CreateEventW(None, True, False, None)
        if not event:
            raise c.WinError(c.get_last_error())
        overlapped, count = Overlapped(hEvent=event), w.DWORD()
        buffer = c.create_string_buffer(data) if data is not None else None
        try:
            if operation == 'connect':
                ok = self.dll.ConnectNamedPipe(handle, c.byref(overlapped))
            else:
                fn = self.dll.ReadFile if operation == 'read' else self.dll.WriteFile
                size = data if isinstance(data, int) else len(data)
                ok = fn(handle, buffer, size, c.byref(count), c.byref(overlapped))
            if not ok:
                error = c.get_last_error()
                if operation == 'connect' and error == 535:
                    return
                if error != 997:
                    raise c.WinError(error)
                deadline = time.monotonic() + 5
                while self.dll.WaitForSingleObject(event, 50) != 0:
                    if self.stopped.is_set() or (operation != 'connect' and time.monotonic() >= deadline):
                        self.dll.CancelIoEx(handle, c.byref(overlapped))
                        self.dll.GetOverlappedResult(handle, c.byref(overlapped), c.byref(count), True)
                        raise TimeoutError('External game observation timed out.')
                if not self.dll.GetOverlappedResult(handle, c.byref(overlapped), c.byref(count), False):
                    raise c.WinError(c.get_last_error())
            return buffer.raw[:count.value] if operation == 'read' else count.value
        finally:
            self.dll.CloseHandle(event)

    def serve(self):
        try:
            while not self.stopped.is_set():
                handle = self.dll.CreateNamedPipeW('\\\\.\\pipe\\' + self.pipe, 3 | 0x40000000, 0, 1, 65536, 65536, 0, None)
                if handle == c.c_void_p(-1).value:
                    raise c.WinError(c.get_last_error())
                def receive(size):
                    result = bytearray()
                    while len(result) < size:
                        chunk = self.transfer(handle, 'read', size - len(result))
                        if not chunk:
                            raise EOFError('External client closed.')
                        result.extend(chunk)
                    return result
                try:
                    self.transfer(handle, 'connect')
                    size, = struct.unpack('<I', receive(4))
                    if not 0 < size <= 1024 * 1024:
                        raise ValueError('Invalid request frame.')
                    reply = json.dumps(self.game.request(json.loads(receive(size)))).encode()
                    frame = struct.pack('<I', len(reply)) + reply
                    while frame:
                        written = self.transfer(handle, 'write', frame)
                        if not written:
                            raise EOFError('External client stopped receiving.')
                        frame = frame[written:]
                    # Closing a named-pipe server discards unread bytes. Wait for
                    # the real client to consume its response and close first.
                    self.transfer(handle, 'read', 1)
                except OSError as error:
                    if getattr(error, 'winerror', None) not in (109, 232, 995) and not self.stopped.is_set():
                        raise
                finally:
                    self.dll.CloseHandle(handle)
        except BaseException as error:
            if not self.stopped.is_set():
                self.errors.append(error)

    def close(self):
        if self.process is not None:
            if self.process.poll() is None:
                self.process.terminate()
            self.process.wait(timeout=5)
        self.stopped.set()
        self.thread.join(timeout=6)
        if self.thread.is_alive() or self.errors:
            raise AssertionError(f'External game boundary failed: {self.errors}')
