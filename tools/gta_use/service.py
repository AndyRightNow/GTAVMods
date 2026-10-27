"""FastAPI loopback gateway; all GTA operations stay on SHVDNE's script thread."""
import asyncio
import contextlib
import hmac
import json
import logging
from logging.handlers import RotatingFileHandler
import os
from pathlib import Path
import secrets
import socket
import subprocess
import sys
import time
import uuid
from fastapi import FastAPI, HTTPException, Request, WebSocket, WebSocketDisconnect
from fastapi.responses import FileResponse, JSONResponse
import uvicorn
from .segment import save_json
from .windows import exchange, read_json

MAX_REQUEST = 1024 * 1024


class PipeLink:
    def __init__(self, root, process):
        self.root, self.process = Path(root), process
        self.lock = asyncio.Lock()

    def discovery(self):
        return read_json(self.root / f"bridge-{self.process}.json")

    @property
    def directory(self):
        return Path(self.discovery()["bridge"]["directory"])

    async def request(self, envelope):
        data = json.dumps(envelope, ensure_ascii=False, allow_nan=False).encode("utf-8")
        if len(data) > MAX_REQUEST:
            raise ValueError("Request exceeds 1 MiB. Keep Python programs in files and send action arguments.")
        async with self.lock:
            task = asyncio.create_task(asyncio.to_thread(exchange, self.discovery()["pipe"], data, 4))
            try:
                return json.loads(await asyncio.shield(task))
            except asyncio.CancelledError:
                # Cancellation must not release the single-pipe lock while OS I/O still owns it.
                with contextlib.suppress(Exception):
                    await task
                raise


def make_app(link, token, log):
    children = set()
    sockets = set()

    @contextlib.asynccontextmanager
    async def lifespan(app):
        yield
        for ws in set(sockets):
            with contextlib.suppress(Exception):
                await ws.close(code=1001)
        # Supervisors stop on HTTP failure; the in-game lease is the final fallback.

    app = FastAPI(title="GTA use", version="1", lifespan=lifespan)

    def authenticated(connection):
        return (connection.client.host in ("127.0.0.1", "::1")
                and not connection.headers.get("origin")
                and hmac.compare_digest(connection.headers.get("authorization", ""), "Bearer " + token))

    @app.middleware("http")
    async def security(request, call_next):
        if not authenticated(request):
            return JSONResponse({"error": "Local bearer authentication required."}, status_code=401)
        try:
            return await call_next(request)
        except (OSError, ValueError, KeyError, TypeError, TimeoutError) as error:
            log.warning("request.failure %s %s", request.url.path, error)
            return JSONResponse({"ok": False, "error": str(error)}, status_code=400)

    async def body(request):
        data = bytearray()
        async for chunk in request.stream():
            data.extend(chunk)
            if len(data) > MAX_REQUEST:
                raise HTTPException(413, "Request exceeds 1 MiB.")
        return json.loads(data)

    async def dispatch(envelope):
        if not isinstance(envelope, dict):
            raise ValueError("Expected a JSON object.")
        identifier = envelope.get("id")
        if not isinstance(identifier, str) or uuid.UUID(identifier).hex != identifier:
            raise ValueError("Expected a 32-character request ID.")
        log.info("request %s %s %s", identifier, envelope.get("session"), envelope.get("op"))
        try:
            response = await link.request(envelope)
            log.info("response %s ok=%s", identifier, response.get("ok"))
            return response
        except (OSError, TimeoutError, EOFError, ValueError) as error:
            log.exception("uncertain %s %s", identifier, error)
            return dict(id=identifier, session=envelope.get("session"), ok=False, uncertain=True,
                        error=f"{error}; inspect game state before acting; request {identifier} was not replayed.")

    @app.get("/v1/session")
    async def session():
        return link.discovery()

    @app.post("/v1/request")
    async def rpc(request: Request):
        # The pipe response is already decoded JSON. Avoid FastAPI recursively
        # adapting every scalar in large world packs a second time.
        return JSONResponse(await dispatch(await body(request)))

    def local_path(relative):
        root = link.directory.resolve()
        path = (root / relative).resolve()
        if not path.is_relative_to(root) or path == root or path.suffix not in (".json", ".jsonl", ".png", ".log", ".py"):
            raise HTTPException(403, "Artifact outside session.")
        return path

    @app.get("/v1/files/{path:path}")
    async def artifact(path: str):
        file = local_path(path)
        if not file.is_file():
            raise HTTPException(404)
        if file.suffix == ".json":
            return read_json(file)
        return FileResponse(file)

    @app.get("/v1/batches/{identifier}")
    async def batch(identifier: str):
        if uuid.UUID(identifier).hex != identifier:
            raise ValueError("Invalid batch ID.")
        directory = link.directory
        status = read_json(directory / f"batch-{identifier}-status.json")
        if status["batch"] != identifier:
            return {"batch": identifier, "state": "superseded", "error": "Inspect historical result files; do not replay."}
        if status["state"] == "completed":
            status["results"] = [read_json(directory / f"result-{identifier}-{i}.json")["result"] for i in range(status["total"])]
        return status

    @app.post("/v1/jobs", status_code=202)
    async def start_job(request: Request):
        data = await body(request)
        identifier = uuid.uuid4().hex
        directory = link.directory / ("run-" + identifier)
        source = Path(data["script"]).resolve(strict=True)
        arguments = [sys.executable, "-u", "-m", "gta_use", "run", str(source), "--discovery", str(link.root),
                     "--process", str(link.process), "--output", str(directory)]
        for field, option, default in (("timeout", "--timeout", 180), ("stuckTimeout", "--stuck-timeout", 30), ("checkTimeout", "--check-timeout", 10)):
            value = float(data.get(field, default))
            if not 0 < value <= 86400:
                raise ValueError("Job limits must be positive seconds, at most 86400.")
            arguments.extend([option, str(value)])
        children.difference_update([p for p in children if p.poll() is not None])
        if children:
            raise HTTPException(409, "A segment is already running.")
        directory.mkdir()
        with (link.directory / f"job-{identifier}.log").open("wb") as output:
            process = subprocess.Popen(arguments, stdout=output, stderr=subprocess.STDOUT,
                                       creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        children.add(process)
        save_json(link.directory / f"job-{identifier}.json", {"id": identifier, "pid": process.pid, "directory": str(directory), "script": str(source)})
        log.info("job.started %s pid=%s", identifier, process.pid)
        return {"id": identifier, "directory": str(directory)}

    @app.get("/v1/jobs/{identifier}")
    async def job(identifier: str):
        if uuid.UUID(identifier).hex != identifier:
            raise ValueError("Invalid job ID.")
        directory = link.directory / ("run-" + identifier)
        for name in ("result.json", "progress.json", "run.json"):
            try:
                return read_json(directory / name)
            except FileNotFoundError:
                pass
        raise HTTPException(404)

    @app.delete("/v1/jobs/{identifier}")
    async def cancel_job(identifier: str):
        if uuid.UUID(identifier).hex != identifier:
            raise ValueError("Invalid job ID.")
        receipt = read_json(link.directory / f"job-{identifier}.json")
        (link.directory / ("run-" + identifier) / "cancel").touch()
        return {"id": receipt["id"], "cancelRequested": True}

    @app.websocket("/v1/ws")
    async def websocket(ws: WebSocket):
        if not authenticated(ws):
            await ws.close(code=1008)
            return
        await ws.accept()
        sockets.add(ws)
        sender = asyncio.Lock()
        subscription = None
        pending = set()

        async def send(message):
            async with sender:
                async with asyncio.timeout(2):
                    await ws.send_json(message)

        async def sample(query, hz, session_id):
            sequence = 0
            previous_batch = None
            try:
                while True:
                    started = time.monotonic()
                    response = await dispatch(dict(query, op="state", version=1, session=session_id, id=uuid.uuid4().hex))
                    sequence += 1
                    await send(dict(response, type="state", sequence=sequence))
                    if not response["ok"]:
                        return
                    try:
                        status = read_json(link.directory / "batch-status.json")
                        if status != previous_batch:
                            await send({"type": "batch", "session": session_id, "result": status})
                            previous_batch = status
                    except FileNotFoundError:
                        pass
                    await asyncio.sleep(max(0, 1 / hz - (time.monotonic() - started)))
            except (WebSocketDisconnect, ConnectionError, TimeoutError, RuntimeError):
                with contextlib.suppress(Exception):
                    await ws.close()
            except Exception as error:
                log.exception("subscription.failed")
                with contextlib.suppress(Exception):
                    await send({"type": "error", "error": str(error)})

        async def respond(message):
            identifier = message.get("id")
            try:
                message.pop("type", None)
                await send(dict(await dispatch(message), type="response"))
            except Exception as error:
                log.warning("websocket.request.failed %s", error)
                with contextlib.suppress(Exception):
                    await send({"type": "response", "id": identifier, "ok": False, "error": str(error)})

        try:
            while True:
                message = await ws.receive_json()
                if message.get("type") == "subscribe":
                    hz = float(message.get("hz", 5))
                    if not 0 < hz <= 10 or not isinstance(message.get("query", {}), dict):
                        await send({"type": "error", "error": "Subscription requires query object and 0 < hz <= 10."})
                        continue
                    if subscription:
                        subscription.cancel()
                        with contextlib.suppress(asyncio.CancelledError):
                            await subscription
                    await send({"type": "subscribed", "session": message["session"], "hz": hz})
                    subscription = asyncio.create_task(sample(message.get("query", {}), hz, message["session"]))
                elif message.get("type") == "request":
                    if len(pending) >= 16:
                        await send({"type": "response", "id": message.get("id"), "session": message.get("session"),
                                    "ok": False, "error": "Too many outstanding requests."})
                        continue
                    task = asyncio.create_task(respond(message))
                    pending.add(task)
                    task.add_done_callback(pending.discard)
                else:
                    await send({"type": "error", "error": "Expected request or subscribe."})
        except WebSocketDisconnect:
            pass
        except (ValueError, KeyError, TypeError, AttributeError) as error:
            with contextlib.suppress(Exception):
                await send({"type": "error", "error": str(error)})
                await ws.close(code=1003)
        finally:
            sockets.discard(ws)
            if subscription:
                subscription.cancel()
                with contextlib.suppress(asyncio.CancelledError):
                    await subscription
            if pending:
                await asyncio.gather(*pending, return_exceptions=True)

    return app


class JsonFormatter(logging.Formatter):
    def format(self, record):
        return json.dumps({"utc": time.time(), "level": record.levelname, "message": record.getMessage(),
                           "exception": self.formatException(record.exc_info) if record.exc_info else None})


def serve(root, process, port):
    import ctypes
    from ctypes import wintypes
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
    kernel.CreateMutexW.restype = wintypes.HANDLE
    kernel.CloseHandle.argtypes = [wintypes.HANDLE]
    mutex = kernel.CreateMutexW(None, False, f"Local\\GTAVMods-Service-{process}")
    if not mutex:
        raise ctypes.WinError(ctypes.get_last_error())
    if ctypes.get_last_error() == 183:
        kernel.CloseHandle(mutex)
        return  # Another service already owns this game process, including after reload.
    root = Path(root)
    token = secrets.token_urlsafe(32)
    link = PipeLink(root, process)
    log = logging.getLogger("gta_use.service")
    log.setLevel(logging.INFO)
    handler = RotatingFileHandler(root / f"service-{process}.jsonl", maxBytes=5 * 1024 * 1024, backupCount=9, encoding="utf-8")
    handler.setFormatter(JsonFormatter())
    log.addHandler(handler)
    path = root / f"service-{process}.json"
    listener = socket.socket()
    try:
        listener.bind(("127.0.0.1", port))
        app = make_app(link, token, log)
        save_json(path, {"url": f"http://127.0.0.1:{listener.getsockname()[1]}", "token": token,
                         "process": process, "serviceProcess": os.getpid()})
        config = uvicorn.Config(app, log_config=None, access_log=False, ws_max_size=MAX_REQUEST, ws_max_queue=8,
                                ws_ping_interval=5, ws_ping_timeout=5)
        server = uvicorn.Server(config)
        kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel.OpenProcess.restype = wintypes.HANDLE
        kernel.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        game_handle = kernel.OpenProcess(0x00100000, False, process)
        if not game_handle:
            raise ctypes.WinError(ctypes.get_last_error())

        async def watch_game():
            while kernel.WaitForSingleObject(game_handle, 0) == 258:
                await asyncio.sleep(1)
            server.should_exit = True

        async def main():
            watcher = asyncio.create_task(watch_game())
            try:
                await server.serve(sockets=[listener])
            finally:
                watcher.cancel()
                with contextlib.suppress(asyncio.CancelledError):
                    await watcher
        try:
            log.info("service.started game=%s", process)
            asyncio.run(main())
        finally:
            kernel.CloseHandle(game_handle)
    finally:
        listener.close()
        path.unlink(missing_ok=True)
        handler.close()
        kernel.CloseHandle(mutex)
