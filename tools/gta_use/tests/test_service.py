"""Agent-facing scenarios against real FastAPI handlers; only the game boundary is observed."""
import asyncio
import json
import logging
from pathlib import Path
import tempfile
import threading
import time
import unittest
import uuid
from fastapi.testclient import TestClient
from gta_use.service import make_app, MAX_REQUEST


class ObservedGame:
    def __init__(self, root):
        self.root = self.directory = Path(root)
        self.process = 123
        self.session = uuid.uuid4().hex
        self.requests = []
        self.failure = None

    def discovery(self):
        return {"process": self.process, "bridge": {"session": self.session, "directory": str(self.directory), "state": "ready"}}

    async def request(self, envelope):
        self.requests.append(envelope)
        if self.failure:
            raise self.failure
        return dict(id=envelope["id"], session=self.session, ok=True,
                    result={"gameTime": 150, "sections": {"player": {"entity": {"health": 175, "dead": False}}}})


class ServiceScenarios(unittest.TestCase):
    def setUp(self):
        self.storage = tempfile.TemporaryDirectory(prefix="GTAVMods-http-test-")
        self.game = ObservedGame(self.storage.name)
        self.app = make_app(self.game, "test-secret", logging.getLogger("gta-use-test"))
        self.client = TestClient(self.app, client=("127.0.0.1", 40000), headers={"Authorization": "Bearer test-secret"})
        self.client.__enter__()

    def tearDown(self):
        self.client.__exit__(None, None, None)
        self.storage.cleanup()

    def envelope(self, op="state"):
        return {"version": 1, "id": uuid.uuid4().hex, "session": self.game.session, "op": op}

    def test_local_agents_require_authentication_and_cross_origin_pages_cannot_control_game(self):
        self.assertEqual(401, self.client.get("/v1/session", headers={"Authorization": "wrong"}).status_code)
        self.assertEqual(401, self.client.post("/v1/request", json=self.envelope(), headers={"Origin": "https://example.com"}).status_code)
        self.assertEqual([], self.game.requests)
        self.assertEqual(self.game.session, self.client.get("/v1/session").json()["bridge"]["session"])
        self.assertEqual("GTA use", self.client.get("/openapi.json").json()["info"]["title"])

    def test_filtered_state_and_command_receipts_keep_the_callers_identity(self):
        message = self.envelope()
        message.update(sections=["player"], radius=20)
        response = self.client.post("/v1/request", json=message).json()
        self.assertEqual(message["id"], response["id"])
        self.assertEqual(175, response["result"]["sections"]["player"]["entity"]["health"])
        self.assertEqual(message, self.game.requests[0])

    def test_game_disconnection_reports_uncertainty_and_does_not_replay(self):
        self.game.failure = TimeoutError("game frame stalled")
        response = self.client.post("/v1/request", json=self.envelope("batch")).json()
        self.assertTrue(response["uncertain"])
        self.assertIn("inspect game state", response["error"])
        self.assertEqual(1, len(self.game.requests))

    def test_invalid_or_oversized_requests_never_reach_game_thread(self):
        self.assertEqual(400, self.client.post("/v1/request", json=[1]).status_code)
        self.assertEqual(400, self.client.post("/v1/request", json={"id": "wrong"}).status_code)
        self.assertEqual(413, self.client.post("/v1/request", content=b"x" * (MAX_REQUEST + 1)).status_code)
        self.assertEqual([], self.game.requests)

    def test_artifact_downloads_are_confined_to_session_and_preserve_contents(self):
        (self.game.directory / "frame.png").write_bytes(b"observed screenshot pixels")
        (self.game.directory / "state.json").write_text('{"health":175}')
        self.assertEqual(b"observed screenshot pixels", self.client.get("/v1/files/frame.png").content)
        self.assertEqual({"health": 175}, self.client.get("/v1/files/state.json").json())
        self.assertEqual(403, self.client.get("/v1/files/..%5Coutside.json").status_code)
        self.assertEqual(404, self.client.get("/v1/files/missing.png").status_code)

    def test_completed_batch_reads_published_results_and_never_mistakes_a_later_batch_for_it(self):
        identifier = uuid.uuid4().hex
        (self.game.directory / f"batch-{identifier}-status.json").write_text(json.dumps(dict(batch=identifier, state="completed", total=1)))
        (self.game.directory / f"result-{identifier}-0.json").write_text('{"result":{"paused":true}}')
        result = self.client.get("/v1/batches/" + identifier).json()
        self.assertEqual([{"paused": True}], result["results"])
        # A later batch may publish before this caller reads its completed result.
        (self.game.directory / "batch-status.json").write_text(json.dumps(dict(batch=uuid.uuid4().hex, state="running")))
        self.assertEqual([{"paused": True}], self.client.get("/v1/batches/" + identifier).json()["results"])
        self.assertEqual(400, self.client.get("/v1/batches/" + uuid.uuid4().hex).status_code)
        self.assertEqual([], self.game.requests)

    def test_inspection_recovers_from_a_briefly_unavailable_replaced_status_without_replaying_actions(self):
        identifier = uuid.uuid4().hex
        (self.game.directory / f"result-{identifier}-0.json").write_text('{"result":{"paused":true}}')
        def publish():
            time.sleep(.04)
            (self.game.directory / f"batch-{identifier}-status.json").write_text(json.dumps(dict(batch=identifier, state="completed", total=1)))
        writer = threading.Thread(target=publish)
        writer.start()
        try:
            response = self.client.get("/v1/batches/" + identifier)
            self.assertEqual(200, response.status_code, response.text)
            self.assertEqual([{"paused": True}], response.json()["results"])
            self.assertEqual([], self.game.requests)
        finally:
            writer.join()

    def test_websocket_streams_filtered_observations_while_receiving_independent_commands(self):
        with self.client.websocket_connect("/v1/ws") as ws:
            ws.send_json({"type": "subscribe", "session": self.game.session, "hz": 5, "query": {"sections": ["player"], "limit": 5}})
            self.assertEqual("subscribed", ws.receive_json()["type"])
            snapshot = ws.receive_json()
            self.assertEqual("state", snapshot["type"])
            self.assertEqual(1, snapshot["sequence"])
            message = dict(self.envelope("status"), type="request")
            ws.send_json(message)
            while True:
                response = ws.receive_json()
                if response["type"] == "response":
                    break
            self.assertEqual(message["id"], response["id"])
            self.assertEqual(["player"], self.game.requests[0]["sections"])
            self.assertEqual(5, self.game.requests[0]["limit"])

    def test_invalid_subscription_is_rejected_and_socket_can_still_inspect(self):
        with self.client.websocket_connect("/v1/ws") as ws:
            ws.send_json({"type": "subscribe", "session": self.game.session, "hz": 100})
            self.assertEqual("error", ws.receive_json()["type"])
            ws.send_json({"type": "unknown"})
            self.assertEqual("error", ws.receive_json()["type"])
            ws.send_json(dict(self.envelope("status"), type="request"))
            self.assertTrue(ws.receive_json()["ok"])


if __name__ == "__main__":
    unittest.main()
