"""One scheduler and one sync transaction, shared by manual and scheduled refresh."""
from __future__ import annotations

import copy
from datetime import datetime, timezone
import logging
import math
import threading
import time

from xhs_adapter import LoginRequired, VerificationRequired, SyncError


class SyncService:
    def __init__(self, storage, transport, interval=900, on_change=None):
        self.storage, self.transport, self.interval = storage, transport, interval
        self.on_change = on_change or (lambda: None)
        self.lock = threading.RLock()
        saved = storage.read("snapshot.json", {})
        if not self.valid_snapshot(saved):
            saved = {}
        self.state = {"status": "initializing", "message": "正在连接创作后台", "notes": saved.get("notes", []),
                      "last_success": saved.get("last_success"), "next_sync": None, "cached": bool(saved)}
        self.stop = threading.Event()
        self.wake = threading.Event()
        self.paused = False
        self.busy = False
        self.next_due = 0.0
        self.thread = threading.Thread(target=self.run, name="xhs-sync", daemon=True)

    @staticmethod
    def valid_snapshot(saved):
        if not isinstance(saved, dict) or not isinstance(saved.get("notes"), list) or len(saved["notes"]) > 50:
            return False
        try:
            date = datetime.fromisoformat(saved["last_success"])
            if date.tzinfo is None:
                return False
            for note in saved["notes"]:
                if not isinstance(note, dict):
                    return False
                if not all(isinstance(note.get(k), str) for k in ("id", "title", "published_at", "read_label")):
                    return False
                if not note["id"]:
                    return False
                number = note.get("published_timestamp")
                if type(number) not in (int, float) or not math.isfinite(number):
                    return False
                for key in ("read_count", "comment_count", "collect_count"):
                    value = note.get(key)
                    if value is not None and (type(value) is not int or value < 0):
                        return False
            return True
        except (ValueError, KeyError, TypeError):
            return False

    def snapshot(self):
        with self.lock:
            return copy.deepcopy(self.state)

    def update(self, **changes):
        with self.lock:
            self.state.update(changes)
        self.on_change()

    def refresh(self, resume=False):
        with self.lock:
            if self.busy:
                return False
            if self.paused and not resume:
                return False
            if resume:
                self.paused = False
            self.next_due = 0
            self.wake.set()
        return True

    def once(self):
        with self.lock:
            if self.busy or self.paused or self.stop.is_set():
                return False
            self.busy = True
        self.update(status="syncing", message="正在同步最近文章", next_sync=None)
        logging.getLogger("xhsboard").info("Sync started")
        try:
            notes = self.transport.collect(self.stop)
            if self.stop.is_set():
                return False
            succeeded = datetime.now(timezone.utc).isoformat(timespec="seconds")
            snapshot = {"notes": [n.to_dict() for n in notes], "last_success": succeeded}
            self.storage.write("snapshot.json", snapshot)
            self.update(**snapshot, status="ready", message="已同步 · 每 15 分钟更新", cached=False)
            logging.getLogger("xhsboard").info("Sync succeeded: %d notes", len(notes))
        except (LoginRequired, VerificationRequired) as error:
            with self.lock:
                self.paused = True
            self.update(status="login_required" if isinstance(error, LoginRequired) else "verification_required",
                        message=str(error), cached=bool(self.state["last_success"]))
            logging.getLogger("xhsboard").info("Sync paused: %s", type(error).__name__)
        except (SyncError, OSError) as error:
            self.update(status="error", message=str(error), cached=bool(self.state["last_success"]))
            logging.getLogger("xhsboard").warning("Sync failed: %s", type(error).__name__)
        except Exception:
            logging.getLogger("xhsboard").exception("Unexpected sync failure")
            self.update(status="error", message="同步失败，请打开创作后台检查", cached=bool(self.state["last_success"]))
        finally:
            with self.lock:
                self.busy = False
                self.next_due = time.monotonic() + self.interval
                if not self.paused:
                    self.state["next_sync"] = time.time() + self.interval
        return True

    def run(self):
        while not self.stop.is_set():
            with self.lock:
                self.wake.clear()
                paused, due = self.paused, self.next_due
            if not paused and time.monotonic() >= due:
                self.once()
            with self.lock:
                wait = None if self.paused else max(0, self.next_due - time.monotonic())
            if self.stop.is_set():
                break
            self.wake.wait(wait)

    def close(self):
        self.stop.set()
        self.wake.set()
        if self.thread.is_alive():
            self.thread.join(timeout=5)
