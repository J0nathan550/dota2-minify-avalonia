import json
import threading
import time
from typing import Any, Callable, Dict, List, Optional

import patch
from core import output


class PatchService:
    def __init__(self) -> None:
        self._window: Any = None
        self._event_sink: Optional[Callable[[str, Any], None]] = None
        self._is_patching: bool = False
        self._logs: List[Dict[str, Any]] = []
        self._downloads: Dict[str, Dict[str, Any]] = {}
        self._lock = threading.Lock()

        output.register_listener(self._on_output_log)
        output.register_download_listener(self._on_download_progress)

    def set_window(self, window: Any) -> None:
        self._window = window

    def set_event_sink(self, sink: Optional[Callable[[str, Any], None]]) -> None:
        self._event_sink = sink

    def _emit(self, callback: str, payload: Any) -> None:
        if self._event_sink:
            try:
                self._event_sink(callback, payload)
            except Exception:
                pass
        if self._window:
            try:
                self._window.evaluate_js(f"window.{callback} && window.{callback}({json.dumps(payload)});")
            except Exception:
                pass

    def _on_download_progress(self, data: Dict[str, Any]) -> None:
        with self._lock:
            self._downloads[data["id"]] = dict(data)
        self._emit("onDownloadProgress", data)

    def get_downloads(self) -> List[Dict[str, Any]]:
        with self._lock:
            return list(self._downloads.values())

    def _on_output_log(self, text: str, msg_type: str | None) -> None:
        if msg_type == "clear":
            with self._lock:
                self._logs.clear()
            self._emit("onLogReceived", {"text": "", "type": "clear"})
            return

        log_entry = {
            "text": text,
            "type": msg_type or "info",
            "timestamp": time.strftime("%H:%M:%S"),
        }
        with self._lock:
            self._logs.append(log_entry)
        self._emit("onLogReceived", log_entry)

    def start_patch(self) -> Dict[str, Any]:
        if self._is_patching:
            return {"status": "already_running"}

        self._is_patching = True
        self._emit("onPatchStatusChange", True)

        def run_patch_thread() -> None:
            try:
                patch.patcher()
            finally:
                self._is_patching = False
                self._emit("onPatchStatusChange", False)

        threading.Thread(target=run_patch_thread, daemon=True).start()
        return {"status": "started"}

    def start_uninstall(self, remove_everything: bool = False) -> Dict[str, Any]:
        if self._is_patching:
            return {"status": "already_running"}

        self._is_patching = True
        self._emit("onPatchStatusChange", True)

        def run_uninstall_thread() -> None:
            try:
                if remove_everything:
                    patch.unins.wipe()
                else:
                    patch.unins.uninstall()
            finally:
                self._is_patching = False
                self._emit("onPatchStatusChange", False)

        threading.Thread(target=run_uninstall_thread, daemon=True).start()
        return {"status": "started"}

    def is_patching(self) -> bool:
        return self._is_patching

    def get_logs(self) -> List[Dict[str, Any]]:
        with self._lock:
            return list(self._logs)

    def clear_logs(self) -> bool:
        with self._lock:
            self._logs.clear()
        return True
