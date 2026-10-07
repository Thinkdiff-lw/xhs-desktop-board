"""Short-lived browser process for the native card; no local Python API on the site."""
from __future__ import annotations

import argparse
import ctypes
import hashlib
import logging
import os
from pathlib import Path
import threading
import time

from collector import Collector, PAGE_PROBE
from storage import Storage
from xhs_adapter import CREATOR_URL, LoginRequired, VerificationRequired


class WorkerSignals:
    def __init__(self, directory):
        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        self.api.CreateEventW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_wchar_p]
        self.api.CreateEventW.restype = ctypes.c_void_p
        self.api.CreateMutexW.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_wchar_p]
        self.api.CreateMutexW.restype = ctypes.c_void_p
        self.api.WaitForSingleObject.argtypes = [ctypes.c_void_p, ctypes.c_uint]
        self.api.CloseHandle.argtypes = [ctypes.c_void_p]
        identity = hashlib.sha256(str(directory.resolve()).lower().encode()).hexdigest()[:16]
        self.mutex = self.api.CreateMutexW(None, False, "Local\\XhsCollector-" + identity)
        if not self.mutex:
            raise ctypes.WinError(ctypes.get_last_error())
        self.existing = ctypes.get_last_error() == 183
        self.events = {key: self.api.CreateEventW(None, False, False, "Local\\XhsCollector-" + key + "-" + identity)
                       for key in ("Show", "Refresh", "Stop")}
        if not all(self.events.values()):
            raise ctypes.WinError(ctypes.get_last_error())

    def take(self, key):
        return self.api.WaitForSingleObject(self.events[key], 0) == 0

    def close(self):
        for handle in self.events.values():
            self.api.CloseHandle(handle)
        self.api.CloseHandle(self.mutex)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--data-dir", type=Path, required=True)
    parser.add_argument("--count", type=int, default=5, choices=range(1, 51))
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--login", action="store_true")
    parser.add_argument("--parent-pid", type=int, default=0)
    args = parser.parse_args()
    store = Storage(args.data_dir)
    logging.basicConfig(filename=store.directory / "collector.log", level=logging.INFO, encoding="utf-8",
                        format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    logging.getLogger("pywebview").setLevel(logging.WARNING)
    signals = WorkerSignals(store.directory)
    if signals.existing:
        signals.close()
        return
    stop = threading.Event()
    parent_handle = None
    if args.parent_pid:
        signals.api.OpenProcess.argtypes = [ctypes.c_uint, ctypes.c_int, ctypes.c_uint]
        signals.api.OpenProcess.restype = ctypes.c_void_p
        parent_handle = signals.api.OpenProcess(0x100000, False, args.parent_pid)
        if not parent_handle:
            signals.close()
            return
    show_requested = threading.Event()
    refresh_requested = threading.Event()
    interactive = args.login
    collector = None
    window = None

    def status(state, message):
        store.write("worker-status.json", {"run_id": args.run_id, "status": state, "message": message,
                     "note_count": args.count, "updated_at": time.time()})

    def signals_loop():
        while not stop.wait(.4):
            if parent_handle and signals.api.WaitForSingleObject(parent_handle, 0) == 0:
                stop.set()
            if signals.take("Stop"):
                stop.set()
            if signals.take("Show"):
                show_requested.set()
            if signals.take("Refresh"):
                refresh_requested.set()

    try:
        import webview
        webview.settings["ALLOW_FILE_URLS"] = False
        webview.settings["ALLOW_DOWNLOADS"] = False
        window = webview.create_window("小红书创作后台 · 登录与同步", CREATOR_URL,
            width=1100, height=780, min_size=(850, 620), hidden=not interactive,
            background_color="#FFFFFF", text_select=True)
        collector = Collector(window)

        def closed_by_user():
            stop.set()

        window.events.closing += closed_by_user
        threading.Thread(target=signals_loop, daemon=True).start()

        def run():
            nonlocal interactive
            due, paused = 0.0, False
            try:
                while not stop.is_set():
                    if show_requested.is_set():
                        show_requested.clear()
                        interactive = True
                        window.show()
                        window.restore()
                    retry = refresh_requested.is_set()
                    refresh_requested.clear()
                    if paused and interactive:
                        probe = window.evaluate_js(PAGE_PROBE)
                        retry |= isinstance(probe, dict) and probe.get("state") in ("manager", "home")
                    if retry or (not paused and time.monotonic() >= due):
                        status("syncing", "正在同步最近文章")
                        try:
                            notes = collector.collect(stop, limit=args.count)
                            if stop.is_set():
                                break
                            from datetime import datetime, timezone
                            store.write("snapshot.json", {"notes": [n.to_dict() for n in notes],
                                        "last_success": datetime.now(timezone.utc).isoformat(timespec="seconds")})
                            status("ready", "已同步 · 每 15 分钟更新")
                            logging.getLogger("xhsboard").info("Sync succeeded: %d notes; target=%d", len(notes), args.count)
                            paused, due = False, time.monotonic() + 900
                            if not interactive and not show_requested.is_set():
                                break
                            interactive = True
                            window.show()
                        except (LoginRequired, VerificationRequired) as error:
                            paused = True
                            status("login_required" if isinstance(error, LoginRequired) else "verification_required", str(error))
                            if not interactive and not show_requested.is_set():
                                break
                            interactive = True
                            window.show()
                        except Exception:
                            logging.getLogger("xhsboard").exception("Collector sync failed")
                            status("error", "同步失败，已保留上次数据；请检查网络或打开后台")
                            if not interactive:
                                break
                            due = time.monotonic() + 900
                    stop.wait(2)
            except Exception:
                logging.getLogger("xhsboard").exception("Collector failed")
                if not stop.is_set():
                    status("error", "同步失败，请打开创作后台检查")
            finally:
                stop.set()
                try:
                    collector.detach()
                except Exception:
                    logging.getLogger("xhsboard").exception("Could not release collector")
                window.destroy()

        webview.start(run, gui="edgechromium", private_mode=False, storage_path=str(store.directory / "webview"))
    except Exception:
        logging.getLogger("xhsboard").exception("Browser worker could not start")
        status("error", "浏览器同步进程启动失败，请检查 WebView2 运行时")
    finally:
        stop.set()
        if parent_handle:
            signals.api.CloseHandle(parent_handle)
        signals.close()


if __name__ == "__main__":
    main()
