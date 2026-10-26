import json
import queue
import threading

from ui import bridge, pickers


class FakeApi:
    def __init__(self):
        self.patch_service = None

    def add(self, a, b=0):
        return a + b

    def boom(self):
        raise ValueError("boom")

    def ask_file(self):
        return pickers.pick_file("Pick", ["*.ttf"])

    def _secret(self):
        return "nope"


class Frontend:
    "Plays the native frontend: writes requests into the bridge's stdin and reads its stdout."

    def __init__(self, api):
        self._stdin = queue.Queue()
        self._messages = queue.Queue()
        self.bridge = bridge.Bridge(api, self)
        self._thread = threading.Thread(target=self.bridge.serve, args=(self._lines(),), daemon=True)

    def _lines(self):
        while (line := self._stdin.get()) is not None:
            yield line

    def write(self, text):
        for line in text.splitlines():
            self._messages.put(json.loads(line))

    def flush(self):
        pass

    def __enter__(self):
        bridge.active = self.bridge
        self._thread.start()
        return self

    def __exit__(self, *exc):
        self._stdin.put(None)
        self._thread.join(timeout=5)
        bridge.active = None

    def send(self, message):
        self._stdin.put(json.dumps(message))

    def receive(self):
        return self._messages.get(timeout=5)


def test_calls_results_and_errors():
    with Frontend(FakeApi()) as frontend:
        assert frontend.receive()["event"] == "ready"

        frontend.send({"id": 1, "method": "add", "args": [2], "kwargs": {"b": 3}})
        assert frontend.receive() == {"id": 1, "result": 5}

        frontend.send({"id": 2, "method": "boom"})
        response = frontend.receive()
        while "event" in response:  # failures are also logged to the terminal
            response = frontend.receive()
        assert response == {"id": 2, "error": "boom"}

        frontend.send({"id": 3, "method": "_secret"})
        response = frontend.receive()
        while "event" in response:
            response = frontend.receive()
        assert response["id"] == 3 and "Invalid API method" in response["error"]


def test_backend_asks_frontend_for_dialog():
    with Frontend(FakeApi()) as frontend:
        frontend.receive()
        frontend.send({"id": 1, "method": "ask_file"})

        dialog = frontend.receive()
        assert dialog["method"] == "pick_file"
        assert dialog["params"]["title"] == "Pick"
        assert dialog["params"]["file_types"] == ["*.ttf"]

        frontend.send({"id": dialog["id"], "result": "/fonts/a.ttf"})
        assert frontend.receive() == {"id": 1, "result": "/fonts/a.ttf"}


def test_events_are_forwarded():
    with Frontend(FakeApi()) as frontend:
        frontend.receive()
        frontend.bridge.emit("onPatchStatusChange", True)
        assert frontend.receive() == {"event": "onPatchStatusChange", "data": True}
