# ui.bridge

Newline-delimited JSON bridge over stdio for native frontends.

Frontend -> backend:  {"id": 1, "method": "get_mods", "args": [], "kwargs": {}}
Backend -> frontend:  {"id": 1, "result": ...} | {"id": 1, "error": "..."}
Backend events:       {"event": "onLogReceived", "data": {...}}
Backend requests:     {"id": "b1", "method": "pick_file", "params": {...}}, answered by {"id": "b1", "result": ...}

## `BridgeClosed()`

*No documentation available.*

<details open><summary>Source</summary>

```python
class BridgeClosed(Exception):
    pass
```

</details>

## `Bridge()`

*No documentation available.*

<details open><summary>Source</summary>

```python
class Bridge:
    def __init__(self, api: Any, out: TextIO) -> None:
        self._api = api
        self._out = out
        self._write_lock = threading.Lock()
        self._ids = itertools.count(1)
        self._pending: Dict[str, Dict[str, Any]] = {}
        self._pending_lock = threading.Lock()
        self._closed = threading.Event()
        self._executor = ThreadPoolExecutor(max_workers=8, thread_name_prefix="minify-bridge")

    def send(self, message: Dict[str, Any]) -> None:
        line = json.dumps(message, ensure_ascii=False, default=str)
        with self._write_lock:
            self._out.write(line + "\n")
            self._out.flush()

    def emit(self, event: str, data: Any = None) -> None:
        if not self._closed.is_set():
            self.send({"event": event, "data": data})

    def request(self, method: str, **params: Any) -> Any:
        "Ask the frontend for something (e.g. a file dialog) and block until it answers."
        request_id = f"b{next(self._ids)}"
        waiter: Dict[str, Any] = {"done": threading.Event()}
        with self._pending_lock:
            self._pending[request_id] = waiter
        self.send({"id": request_id, "method": method, "params": params})
        while not waiter["done"].wait(0.5):
            if self._closed.is_set():
                raise BridgeClosed(method)
        if "error" in waiter:
            raise RuntimeError(waiter["error"])
        return waiter.get("result")

    def _resolve(self, message: Dict[str, Any]) -> None:
        with self._pending_lock:
            waiter = self._pending.pop(str(message.get("id")), None)
        if waiter is None:
            return
        if "error" in message:
            waiter["error"] = message["error"]
        else:
            waiter["result"] = message.get("result")
        waiter["done"].set()

    def _call(self, message: Dict[str, Any]) -> None:
        request_id = message.get("id")
        try:
            method_name = message["method"]
            method = getattr(self._api, method_name, None)
            if method_name.startswith("_") or not callable(method):
                raise ValueError(f"Invalid API method: {method_name}")
            result = method(*message.get("args", []), **message.get("kwargs", {}))
            response = {"id": request_id, "result": result}
        except Exception as error:
            log.write_warning(f"Bridge call {message.get('method')} failed")
            response = {"id": request_id, "error": str(error)}
        if request_id is not None:
            self.send(response)

    def serve(self, stdin: TextIO) -> None:
        self.emit("ready", {"version": base.VERSION, "protocol": PROTOCOL_VERSION})
        try:
            for line in stdin:
                line = line.strip()
                if not line:
                    continue
                try:
                    message = json.loads(line)
                except json.JSONDecodeError:
                    log.write_warning(f"Bridge received invalid JSON: {line[:200]}")
                    continue
                if "method" in message:
                    self._executor.submit(self._call, message)
                else:
                    self._resolve(message)
        finally:
            self._closed.set()
            self._executor.shutdown(wait=False, cancel_futures=True)
```

</details>

## `serve(api, out)`

*No documentation available.*

<details open><summary>Source</summary>

```python
def serve(api: Any, out: TextIO) -> None:
    global active
    out.reconfigure(encoding="utf-8")
    sys.stdin.reconfigure(encoding="utf-8")
    bridge = Bridge(api, out)
    api.patch_service.set_event_sink(bridge.emit)
    active = bridge
    try:
        bridge.serve(sys.stdin)
    finally:
        active = None
```

</details>
