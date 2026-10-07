"""Standalone Windows creator board. Run app.py or the packaged XhsBoard.exe."""
from __future__ import annotations

import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import logging
import mimetypes
import threading
import time
from urllib.parse import urlparse

from storage import Storage, app_dir, resource_dir
from xhs_adapter import CREATOR_URL

LOG = logging.getLogger("xhsboard")


class AssetServer:
    def __init__(self):
        root = resource_dir() / "web"
        assets = {"/": "index.html", "/index.html": "index.html", "/style.css": "style.css", "/app.js": "app.js"}

        class Handler(BaseHTTPRequestHandler):
            def do_GET(self):
                name = assets.get(urlparse(self.path).path)
                if not name:
                    self.send_error(404)
                    return
                content = (root / name).read_bytes()
                self.send_response(200)
                self.send_header("Content-Type", (mimetypes.guess_type(name)[0] or "text/plain") + "; charset=utf-8")
                self.send_header("Content-Length", str(len(content)))
                self.send_header("Cache-Control", "no-store")
                self.send_header("X-Content-Type-Options", "nosniff")
                self.end_headers()
                self.wfile.write(content)

            def log_message(self, *args):
                pass

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.url = f"http://127.0.0.1:{self.server.server_port}/"
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=1)


class CardAPI:
    """Only these methods are exposed to the local card. The remote window has no API."""
    def __init__(self, owner):
        self._owner = owner

    def _check(self):
        if self._owner.card.get_current_url() != self._owner.server.url:
            raise PermissionError("仅允许本地看板调用")

    def snapshot(self):
        self._check()
        return {"data": self._owner.sync.snapshot(), "settings": self._owner.store.get_settings()}

    def refresh(self):
        self._check()
        return {"accepted": self._owner.sync.refresh()}

    def login(self):
        self._check()
        self._owner.show_login()
        return {"ok": True}

    def hide(self):
        self._check()
        self._owner.set_visible(False)
        return {"ok": True}

    def pin(self, enabled):
        self._check()
        self._owner.set_option("on_top", enabled)
        return {"ok": True}

    def options(self):
        self._check()
        from native import get_autostart
        return {**self._owner.store.get_settings(), "autostart": get_autostart()}

    def set_option(self, key, enabled):
        self._check()
        self._owner.set_option(key, enabled)
        return {"ok": True}

    def set_theme(self, theme):
        self._check()
        self._owner.set_theme(theme)
        return {"ok": True}


class Application:
    def __init__(self, store, instance, background=False, preview=False):
        import webview
        from remote_session import RemoteSession
        from sync_service import SyncService
        from native import CardStyle, initial_position

        self.webview, self.store, self.instance = webview, store, instance
        self.stop = threading.Event()
        self.ui_changed = threading.Event()
        self.quitting = False
        self.preview = preview
        self.position_timer = None
        self.server = AssetServer()
        saved = store.get_settings()
        x, y = initial_position(saved["position"])
        self.card = webview.create_window(
            "小红书 · 创作看板", self.server.url, js_api=CardAPI(self),
            width=380, height=520, min_size=(380, 520), x=x, y=y,
            frameless=True, resizable=False, easy_drag=False, shadow=False,
            on_top=saved["on_top"], hidden=not saved["visible"], transparent=True,
            background_color="#171921", text_select=False)
        self.style = CardStyle(self.card)
        cached = store.read("snapshot.json", {})
        first_login = not (isinstance(cached, dict) and cached.get("last_success"))
        self.first_login = first_login and not background
        self.remote = RemoteSession(webview)
        self.sync = SyncService(store, self.remote, on_change=self.ui_changed.set)
        self.card.events.before_show += self.style_card
        self.card.events.shown += self.after_show
        self.card.events.loaded += self.card_loaded
        self.card.events.closing += self.card_closing
        self.card.events.moved += self.moved
        self.tray = None

    def style_card(self):
        if self.preview:
            return
        self.style.apply(self.store.get_settings()["click_through"])
        # Changing WinForms.ShowInTaskbar here recreates the HWND while WebView2
        # initializes (E_ABORT). Use styles + ITaskbarList, as TimeTrace does.

    def after_show(self):
        if self.preview:
            return
        try:
            self.style_card()
            self.style.remove_taskbar()
        except Exception:
            LOG.exception("Unable to apply desktop card style")

    def card_loaded(self):
        LOG.info("Desktop card loaded")
        if not self.store.get_settings()["visible"]:
            self.card.hide()
        self.after_show()
        self.ui_changed.set()

    def moved(self, x, y):
        if self.position_timer:
            self.position_timer.cancel()
        self.position_timer = threading.Timer(0.4, self.save_position, args=(int(x), int(y)))
        self.position_timer.daemon = True
        self.position_timer.start()

    def save_position(self, x, y):
        try:
            self.store.set("position", [x, y])
        except OSError:
            LOG.exception("Cannot save card position")

    def set_visible(self, visible):
        self.store.set("visible", bool(visible))
        if visible:
            self.card.show()
            self.after_show()
        else:
            self.card.hide()
        self.ui_changed.set()

    def card_closing(self):
        if self.quitting:
            return True
        self.set_visible(False)
        return False

    def show_login(self):
        self.remote.show_login()
        # Refreshing a failed/paused session is done by the watcher after login,
        # so opening the form does not reload a partially entered verification code.

    def set_option(self, key, enabled):
        from native import set_autostart
        if type(enabled) is not bool:
            raise ValueError("设置值必须为开或关")
        if key == "autostart":
            set_autostart(enabled)
        elif key == "on_top":
            before = self.store.get_settings()[key]
            self.style.pin(enabled)
            try:
                self.store.set(key, enabled)
            except OSError:
                self.style.pin(before)
                raise
            self.after_show()
        elif key == "click_through":
            before = self.store.get_settings()[key]
            self.style.apply(enabled)
            try:
                self.store.set(key, enabled)
            except OSError:
                self.style.apply(before)
                raise
        else:
            raise ValueError("未知设置")
        if self.tray:
            self.tray.update_menu()
        self.ui_changed.set()

    def set_theme(self, theme):
        self.store.set("theme", theme)
        if self.tray:
            self.tray.update_menu()
        self.ui_changed.set()

    def login_watcher(self):
        while not self.stop.wait(3):
            if not self.sync.paused:
                continue
            try:
                probe = self.remote.probe_login()
                if isinstance(probe, dict) and probe.get("state") in ("manager", "home"):
                    self.sync.refresh(resume=True)
            except Exception:
                LOG.debug("Login watcher waiting for navigation")

    def start_services(self):
        LOG.info("Starting tray and scheduler")
        import pystray
        from PIL import Image
        from native import get_autostart
        item = pystray.MenuItem

        def checked(key):
            return lambda _: self.store.get_settings()[key]

        def toggle(key):
            return lambda *_: self.set_option(key, not self.store.get_settings()[key])

        menu = pystray.Menu(
            item("显示桌面看板", lambda *_: self.set_visible(True), default=True),
            item("隐藏桌面看板", lambda *_: self.set_visible(False)),
            item("立即刷新", lambda *_: self.sync.refresh(), enabled=lambda _: not self.sync.busy and not self.sync.paused),
            item("打开创作后台 / 重新登录", lambda *_: self.show_login()),
            pystray.Menu.SEPARATOR,
            item("主题", pystray.Menu(
                item("玻璃", lambda *_: self.set_theme("glass"), radio=True,
                     checked=lambda _: self.store.get_settings()["theme"] == "glass"),
                item("透明", lambda *_: self.set_theme("transparent"), radio=True,
                     checked=lambda _: self.store.get_settings()["theme"] == "transparent"))),
            item("始终置顶", toggle("on_top"), checked=checked("on_top")),
            item("鼠标穿透", toggle("click_through"), checked=checked("click_through")),
            item("开机启动", lambda *_: self.set_option("autostart", not get_autostart()), checked=lambda _: get_autostart()),
            pystray.Menu.SEPARATOR,
            item("退出", lambda *_: self.quit()))
        self.tray = pystray.Icon("XhsBoard", Image.open(resource_dir() / "icon.ico"), "小红书创作看板", menu)
        self.tray.run_detached()
        LOG.info("Tray started")
        self.instance.listen(self.stop, lambda: self.set_visible(True))
        if self.first_login:
            self.show_login()
        threading.Thread(target=self.push_card_state, daemon=True, name="card-updates").start()
        self.sync.thread.start()
        self.watcher = threading.Thread(target=self.login_watcher, daemon=True, name="login-watcher")
        self.watcher.start()
        self.diagnostics = threading.Thread(target=self.record_diagnostics, daemon=True, name="runtime-status")
        self.diagnostics.start()

    def record_diagnostics(self):
        import os
        from native import hwnd, user32, GWL_EXSTYLE
        previous = None
        while not self.stop.wait(30):
            try:
                current = {"pid": os.getpid(), "card_visible": bool(self.card.native.Visible),
                    "card_size": [self.card.width, self.card.height],
                    "card_style": int(user32.GetWindowLongPtrW(hwnd(self.card), GWL_EXSTYLE)),
                    **self.remote.diagnostics(), "sync_status": self.sync.snapshot()["status"]}
                if current != previous:
                    self.store.write("runtime.json", current)
                    previous = current
            except Exception:
                LOG.debug("Cannot write runtime diagnostics")

    def push_card_state(self):
        # No browser timers/bridge requests while data is unchanged or hidden.
        while not self.stop.is_set():
            self.ui_changed.wait()
            self.ui_changed.clear()
            if self.stop.is_set():
                return
            if not self.store.get_settings()["visible"]:
                continue
            try:
                payload = json.dumps({"data": self.sync.snapshot(), "settings": self.store.get_settings()}, ensure_ascii=True)
                self.card.evaluate_js("window.xhsBoardRender && window.xhsBoardRender(" + payload + ")")
            except Exception:
                LOG.debug("Card not ready for state update")

    def quit(self):
        if self.quitting:
            return
        self.quitting = True
        self.stop.set()
        self.ui_changed.set()
        self.sync.stop.set()
        if self.position_timer:
            self.position_timer.cancel()
        if self.tray:
            self.tray.stop()
        self.remote.close()
        self.card.destroy()

    def run(self, smoke_seconds=0):
        self.webview.settings["ALLOW_FILE_URLS"] = False
        self.webview.settings["ALLOW_DOWNLOADS"] = False
        if smoke_seconds:
            timer = threading.Timer(smoke_seconds, self.quit)
            timer.daemon = True
            timer.start()
        try:
            self.webview.start(self.start_services, gui="edgechromium", private_mode=False,
                               storage_path=str(self.store.directory / "webview"), icon=str(resource_dir() / "icon.ico"))
        finally:
            self.stop.set()
            self.sync.close()
            if self.tray:
                self.tray.stop()
            if self.position_timer:
                self.position_timer.cancel()
            self.server.close()


def main():
    from pathlib import Path
    from native import SingleInstance
    parser = argparse.ArgumentParser()
    parser.add_argument("--background", action="store_true")
    parser.add_argument("--data-dir", type=Path, default=app_dir() / "data")
    parser.add_argument("--smoke-seconds", type=int, default=0, help=argparse.SUPPRESS)
    parser.add_argument("--preview", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    store = Storage(args.data_dir)
    handler = logging.FileHandler(store.directory / "xhsboard.log", encoding="utf-8")
    logging.basicConfig(level=logging.INFO, handlers=[handler], format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    logging.getLogger("pywebview").setLevel(logging.WARNING)
    instance = SingleInstance(store.directory)
    if instance.existing:
        instance.close()
        return
    try:
        LOG.info("Starting desktop board")
        application = Application(store, instance, args.background, args.preview)
        application.run(args.smoke_seconds)
    except Exception:
        LOG.exception("Desktop board could not start")
        raise
    finally:
        instance.close()


if __name__ == "__main__":
    main()
