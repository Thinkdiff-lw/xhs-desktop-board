"""Native WebView2 response observer; only published-note responses enter the queue."""
from __future__ import annotations

import json
import logging
import queue
import threading
import time

from xhs_adapter import CREATOR_URL, LoginRequired, VerificationRequired, SyncError, parse_page, posted_request, recent_notes

LOG = logging.getLogger("xhsboard")

# Select a read-only tab using the official site's observed rendered label.
# Native automation never submits credentials or invokes any editing controls.
PAGE_PROBE = r"""(() => {
  const text = document.body ? document.body.innerText : '';
  if (/安全验证|拖动滑块|完成验证|访问过于频繁/.test(text)) return {state:'verification'};
  const login = /短信登录|验证码登录|扫码登录|手机号登录|发送验证码/.test(text);
  if (login && !document.querySelector('.note-card')) return {state:'login'};
  const tabs = [...document.querySelectorAll('span,div,button,a')].filter(e =>
    e.textContent.trim() === '已发布' && e.children.length === 0);
  const tab = tabs.find(e => e.getBoundingClientRect().height > 0);
  if (tab && !window.__xhsBoardSelectedPublished) {
    window.__xhsBoardSelectedPublished = true; tab.click();
  }
  const noteCard = document.querySelector('.note-card');
  return {state:tab || noteCard ? 'manager' : /笔记管理/.test(text) && !login ? 'home' : 'loading', label:
    /阅读量/.test(text) ? '阅读量' : /观看量/.test(text) ? '观看量' : '浏览量'};
})()"""

# Trigger the page's own pagination through its normal scrolling behavior.
SCROLL_MORE = r"""(() => {
  const cards = [...document.querySelectorAll('.note-card')];
  if (!cards.length) return false;
  cards[cards.length - 1].scrollIntoView({block:'end'});
  let e = cards[cards.length - 1].parentElement;
  while(e && e !== document.body) {
    if(e.scrollHeight > e.clientHeight + 20 && /(auto|scroll)/.test(getComputedStyle(e).overflowY)) {
      e.scrollTop = e.scrollHeight; return true;
    }
    e=e.parentElement;
  }
  window.scrollTo(0, document.documentElement.scrollHeight); return true;
})()"""


class Collector:
    def __init__(self, window):
        self.window = window
        self.ready = threading.Event()
        self.responses = queue.Queue(maxsize=40)
        self.lock = threading.Lock()
        self.generation = 0
        self.active = False
        self.attached = False
        self.attach_pending = False
        self.closed = False
        self.has_user_login_window = False
        self.read_label = "浏览量"
        self.window.events.before_show += self.attach
        self.window.events.loaded += self.loaded

    def attach(self):
        if self.attached or self.attach_pending or self.closed:
            return
        control = self.window.native.browser.webview
        if control.CoreWebView2 is not None:
            self._attach_core(control.CoreWebView2)
        else:
            # Keep the delegate alive for the lifetime of the control.
            self._ready_handler = self._initialized
            self.attach_pending = True
            control.CoreWebView2InitializationCompleted += self._ready_handler

    def _initialized(self, sender, args):
        sender.CoreWebView2InitializationCompleted -= self._ready_handler
        self.attach_pending = False
        if self.closed:
            return
        if args.IsSuccess:
            self._attach_core(sender.CoreWebView2)
        else:
            LOG.error("WebView2 initialization failed")

    def _attach_core(self, core):
        if self.attached:
            return
        from System.Threading.Tasks import TaskScheduler
        self.scheduler = TaskScheduler.FromCurrentSynchronizationContext()
        core.WebResourceResponseReceived += self._response
        # Limit top-level navigations to official HTTPS destinations.
        core.NavigationStarting += self._navigation
        self.attached = True
        self.ready.set()
        LOG.info("Creator response observer attached")

    def detach(self):
        """Unsubscribe native delegates before disposing a short-lived WebView."""
        from System import Action
        with self.lock:
            self.active = False
            self.generation += 1
            self.closed = True
        self.window.events.before_show -= self.attach
        self.window.events.loaded -= self.loaded

        def release():
            control = self.window.native.browser.webview
            if self.attach_pending:
                control.CoreWebView2InitializationCompleted -= self._ready_handler
                self.attach_pending = False
            if self.attached and control.CoreWebView2 is not None:
                control.CoreWebView2.WebResourceResponseReceived -= self._response
                control.CoreWebView2.NavigationStarting -= self._navigation
                self.attached = False
            control.Dispose()

        self.window.native.Invoke(Action(release))

    def _navigation(self, sender, args):
        from xhs_adapter import is_official
        if not is_official(str(args.Uri)):
            args.Cancel = True

    def loaded(self):
        self.ready.set()

    def _response(self, sender, args):
        page_number = posted_request(str(args.Request.Uri))
        if page_number is None:
            return
        with self.lock:
            if not self.active:
                return
            generation = self.generation
        from System import Action
        from System.IO import Stream, StreamReader
        from System.Threading.Tasks import Task
        status = int(args.Response.StatusCode)

        def completed(task):
            try:
                if task.IsFaulted or task.IsCanceled or task.Result is None:
                    body = None
                else:
                    reader = StreamReader(task.Result)
                    try:
                        # Typical note list is small; never persist the raw response.
                        text = reader.ReadToEnd()
                        body = json.loads(str(text)) if len(text) <= 4_000_000 else None
                    finally:
                        reader.Dispose()
                with self.lock:
                    current = self.active and self.generation == generation
                if current:
                    try:
                        self.responses.put_nowait((generation, page_number, status, body))
                    except queue.Full:
                        LOG.warning("Response queue is full")
            except Exception:
                LOG.exception("Could not parse creator response")

        callback = Action[Task[Stream]](completed)
        args.Response.GetContentAsync().ContinueWith(callback, self.scheduler)

    def show_login(self):
        self.has_user_login_window = True
        self.window.show()
        self.window.restore()

    def collect(self, stop, timeout=70, limit=5):
        if type(limit) is not int or not 1 <= limit <= 50:
            raise ValueError("文章数量需为 1–50")
        if not self.ready.wait(20):
            raise SyncError("创作后台窗口启动超时，请打开后台")
        with self.lock:
            self.generation += 1
            generation = self.generation
            self.active = True
        while True:
            try:
                self.responses.get_nowait()
            except queue.Empty:
                break
        notes, pages, expected_page = [], set(), 0
        started, last_scroll, manager_seen = time.monotonic(), 0.0, False
        try:
            # Reload the read-only manager to get fresh counters using its own session.
            if self.window.get_current_url() == CREATOR_URL:
                # Assigning WebView2.Source to the same URL does not reload it.
                self.window.evaluate_js("window.location.reload()")
            else:
                self.window.load_url(CREATOR_URL)
            while not stop.is_set() and time.monotonic() - started < timeout:
                try:
                    source, number, status, body = self.responses.get(timeout=0.5)
                except queue.Empty:
                    source = None
                if source == generation:
                    if number == expected_page and number not in pages:
                        LOG.info("Published list received: page=%d status=%d body=%s", number, status, body is not None)
                        page = parse_page(body, status, self.read_label)
                        pages.add(number)
                        notes.extend(page.notes)
                        latest = recent_notes(notes, limit)
                        # Pinned old notes are sorted with the whole sample. Continue
                        # until enough ordinary notes ensure pinning cannot hide newer ones.
                        ordinary = {n.id for n in notes if not n.sticky}
                        if page.next_page == -1 or len(ordinary) >= limit:
                            self.has_user_login_window = False
                            self.window.hide()
                            return latest
                        if page.next_page in pages:
                            raise SyncError("后台分页未前进，请打开创作后台检查")
                        expected_page = page.next_page
                        last_scroll = 0
                now = time.monotonic()
                if now - last_scroll >= 2:
                    last_scroll = now
                    probe = self.window.evaluate_js(PAGE_PROBE)
                    if isinstance(probe, dict):
                        if probe.get("state") == "login":
                            raise LoginRequired("请登录小红书创作后台")
                        if probe.get("state") == "verification":
                            raise VerificationRequired("后台需要验证，请打开创作后台")
                        self.read_label = probe.get("label", "浏览量")
                        manager_seen |= probe.get("state") == "manager"
                    if pages:
                        self.window.evaluate_js(SCROLL_MORE)
            if stop.is_set():
                raise SyncError("同步已停止")
            raise SyncError("未能读取文章数据，请打开创作后台检查" if manager_seen else "创作后台连接超时，请检查网络")
        finally:
            with self.lock:
                self.active = False
