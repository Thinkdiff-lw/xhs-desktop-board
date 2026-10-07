"""Create the creator WebView only while syncing or while the user needs it."""
from __future__ import annotations

import logging
import threading

from collector import Collector, PAGE_PROBE
from xhs_adapter import CREATOR_URL, SyncError

LOG = logging.getLogger("xhsboard")


class RemoteSession:
    def __init__(self, webview):
        self.webview = webview
        self.lock = threading.RLock()
        self.window = None
        self.collector = None
        self.busy = False
        self.closed = False

    def _ensure(self):
        # Called from background workers after webview.start has begun.
        if self.closed:
            raise SyncError("同步已停止")
        if self.window is None:
            from System import Action
            window = self.webview.create_window(
                "小红书创作后台 · 登录后自动同步", CREATOR_URL,
                width=1100, height=780, min_size=(850, 620), hidden=True,
                background_color="#FFFFFF", text_select=True)
            collector = Collector(window)
            self.window, self.collector = window, collector
            window.events.closing += self._user_closing
            # Dynamic windows may already have fired before_show.
            window.native.Invoke(Action(collector.attach))
            LOG.info("Creator window created on demand")
        return self.collector

    def show_login(self):
        with self.lock:
            self._ensure().show_login()

    def _user_closing(self):
        # Keep the UI callback nonblocking: destroy runs after this close is canceled.
        collector = self.collector
        if collector:
            collector.has_user_login_window = False
            collector.window.hide()
            threading.Thread(target=self.release_idle, daemon=True).start()
        return False

    def collect(self, stop):
        with self.lock:
            if self.busy:
                raise SyncError("同步正在进行")
            collector = self._ensure()
            self.busy = True
        try:
            return collector.collect(stop)
        finally:
            with self.lock:
                self.busy = False
            self.release_idle()

    def probe_login(self):
        with self.lock:
            collector = self.collector
            if self.busy or not collector or not collector.has_user_login_window:
                return None
        try:
            probe = collector.window.evaluate_js(PAGE_PROBE)
            with self.lock:
                return probe if self.collector is collector else None
        except Exception:
            return None

    def release_idle(self, force=False):
        with self.lock:
            collector, window = self.collector, self.window
            if not window or (not force and (self.busy or collector.has_user_login_window)):
                return
            self.collector = self.window = None
        # Don't hold the session lock while marshaling to the UI thread.
        window.events.closing -= self._user_closing
        try:
            collector.detach()
        except Exception:
            LOG.exception("Could not detach creator response observer")
        finally:
            window.destroy()
        LOG.info("Creator window released; login profile retained")

    def diagnostics(self):
        with self.lock:
            window, collector = self.window, self.collector
            return {"remote_allocated": window is not None,
                    "remote_visible": bool(collector and collector.has_user_login_window),
                    "observer_attached": bool(collector and collector.attached)}

    def close(self):
        with self.lock:
            self.closed = True
        self.release_idle(force=True)
