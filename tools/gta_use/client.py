"""Public HTTP and WebSocket SDK; game scripts never handle Windows pipes."""
import asyncio
import json
import os
from pathlib import Path
import time
import uuid
import httpx
from websockets.asyncio.client import connect
from .windows import read_json


class BridgeError(RuntimeError):
    pass


class UncertainOutcome(BridgeError):
    pass


class Client:
    def __init__(self, discovery=None, process=None, token=None, timeout=8):
        root = Path(discovery or os.environ.get("GTA_USE_DISCOVERY") or Path(os.environ["LOCALAPPDATA"]) / "GTAVMods" / "Bridge")
        files = list(root.glob(f"service-{process or '*'}.json"))
        if len(files) != 1:
            raise BridgeError(f"Expected one HTTP service, found {len(files)}. Select --process explicitly.")
        self.service = read_json(files[0])
        self.timeout, self.token = timeout, token or os.environ.get("GTA_USE_TOKEN")
        self.connection = httpx.Client(base_url=self.service["url"], trust_env=False, timeout=timeout,
                                      headers={"Authorization": "Bearer " + self.service["token"]})
        self.discovery = self.http("GET", "/v1/session")
        self.session = self.discovery["bridge"]["session"]
        self.directory = Path(self.discovery["bridge"]["directory"])

    def http(self, method, path, body=None):
        response = self.connection.request(method, path, json=body)
        if not response.is_success:
            raise BridgeError(f"HTTP {response.status_code}: {response.text}")
        return response.json()

    def envelope(self, op, arguments):
        envelope = dict(arguments, version=1, id=uuid.uuid4().hex, session=self.session, op=op)
        if self.token and op in ("batch", "reload", "action"):
            envelope["token"] = self.token
        return envelope

    def result(self, response, identifier):
        if response.get("id") != identifier or response.get("session") != self.session:
            raise UncertainOutcome("Response identity mismatch; inspect before acting.")
        if not response["ok"]:
            error = UncertainOutcome if response.get("uncertain") else BridgeError
            raise error(response["error"])
        return response["result"]

    def request(self, op, **arguments):
        envelope = self.envelope(op, arguments)
        try:
            response = self.http("POST", "/v1/request", envelope)
        except httpx.TransportError as error:
            raise UncertainOutcome(f"Request {envelope['id']} lost its response; inspect game state before acting. No automatic replay.") from error
        return self.result(response, envelope["id"])

    def state(self, **filters):
        return self.request("state", **filters)

    def batch(self, steps, timeout=35):
        accepted = self.request("batch", steps=steps)
        return self.wait_batch(accepted, timeout)

    def wait_batch(self, accepted, timeout=35):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            status = self.http("GET", "/v1/batches/" + accepted["batch"])
            if status["state"] == "completed":
                return status["results"]
            if status["state"] != "running":
                raise BridgeError(status["error"] or status["state"])
            time.sleep(.03)
        raise UncertainOutcome(f"Batch {accepted['batch']} timed out; inspect or stop it before acting.")

    def action(self, op, **arguments):
        response = self.request("action", steps=[dict(arguments, op=op)])
        if response["completed"]:
            return response["value"]
        return self.wait_batch(response["batch"])[0]

    def command(self, code):
        return self.action("command", code=code)

    def native(self, name, *arguments, returns="void"):
        return self.action("native", name=name, args=list(arguments), returns=returns)

    def lease(self, action, milliseconds=5000):
        return self.request("lease", action=action, token=self.token, ms=milliseconds)

    def step(self, milliseconds=500, inputs=None, **filters):
        action = {"op": "controls", "ms": milliseconds, "inputs": inputs} if inputs else {"op": "wait", "ms": milliseconds}
        return self.batch([{"op": "resume"}, action, {"op": "pause"}, dict(filters, op="state")])[-1]

    def connect(self):
        return LiveClient(self)

    def close(self):
        self.connection.close()


class LiveClient:
    """One receiver correlates command receipts and retains only the latest state."""
    def __init__(self, client):
        self.client = client
        self.pending = {}
        self.observations = asyncio.Queue(maxsize=1)

    async def __aenter__(self):
        self.http = httpx.AsyncClient(base_url=self.client.service["url"], trust_env=False, timeout=self.client.timeout,
                                     headers={"Authorization": "Bearer " + self.client.service["token"]})
        try:
            self.ws = await connect(self.client.service["url"].replace("http://", "ws://") + "/v1/ws",
                                    additional_headers={"Authorization": "Bearer " + self.client.service["token"]},
                                    proxy=None, ping_interval=5, ping_timeout=5, max_size=4 * 1024 * 1024, max_queue=8)
        except BaseException:
            await self.http.aclose()
            raise
        self.receiver = asyncio.create_task(self.receive())
        return self

    async def receive(self):
        error = UncertainOutcome("WebSocket disconnected. Inspect game state before acting.")
        try:
            async for frame in self.ws:
                message = json.loads(frame)
                if message["type"] == "response":
                    future = self.pending.get(message.get("id"))
                    if future is not None and not future.done():
                        future.set_result(message)
                elif message["type"] in ("state", "error"):
                    if self.observations.full():
                        self.observations.get_nowait()
                    self.observations.put_nowait(message)
        except Exception as caught:
            error = UncertainOutcome(str(caught))
        finally:
            for future in self.pending.values():
                if not future.done():
                    future.set_exception(error)
            if self.observations.full():
                self.observations.get_nowait()
            self.observations.put_nowait({"type": "error", "error": str(error)})

    async def request(self, op, **arguments):
        envelope = self.client.envelope(op, arguments)
        future = asyncio.get_running_loop().create_future()
        self.pending[envelope["id"]] = future
        try:
            await self.ws.send(json.dumps(dict(envelope, type="request"), allow_nan=False))
            response = await asyncio.wait_for(future, self.client.timeout)
            return self.client.result(response, envelope["id"])
        except (TimeoutError, ConnectionError) as error:
            raise UncertainOutcome(f"Request {envelope['id']} lost its response; inspect game state. Command was not replayed.") from error
        finally:
            self.pending.pop(envelope["id"], None)

    async def states(self, hz=5, **query):
        await self.ws.send(json.dumps({"type": "subscribe", "session": self.client.session, "query": query, "hz": hz}, allow_nan=False))
        while True:
            message = await asyncio.wait_for(self.observations.get(), self.client.timeout)
            if message["type"] == "error" or not message["ok"]:
                raise BridgeError(message["error"])
            if message["session"] != self.client.session:
                raise UncertainOutcome("Subscription session changed.")
            yield message["result"]

    async def batch(self, steps, timeout=35):
        accepted = await self.request("batch", steps=steps)
        return await self.wait_batch(accepted, timeout)

    async def wait_batch(self, accepted, timeout=35):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            response = await self.http.get("/v1/batches/" + accepted["batch"])
            response.raise_for_status()
            status = response.json()
            if status["state"] == "completed":
                return status["results"]
            if status["state"] != "running":
                raise BridgeError(status["error"] or status["state"])
            await asyncio.sleep(.03)
        raise UncertainOutcome(f"Batch {accepted['batch']} timed out; inspect before retrying.")

    async def action(self, op, **arguments):
        response = await self.request("action", steps=[dict(arguments, op=op)])
        if response["completed"]:
            return response["value"]
        return (await self.wait_batch(response["batch"]))[0]

    async def native(self, name, *arguments, returns="void"):
        return await self.action("native", name=name, args=list(arguments), returns=returns)

    async def __aexit__(self, *errors):
        await self.ws.close()
        await self.receiver
        await self.http.aclose()
