import copy
import json
from pathlib import Path
import threading
import time
import unittest
import uuid

from storage import Storage
from sync_service import SyncService
from xhs_adapter import (LoginRequired, VerificationRequired, SyncError, count, parse_page,
                         posted_request, recent_notes, timestamp)


def raw_note(id="a", time_value=1700000000, **changes):
    return {"id": id, "display_title": "测试文章", "time": time_value,
            "view_count": 0, "comments_count": 12, "collected_count": 24,
            "schedule_post_time": 0, "tab_status": 1, **changes}


def payload(notes, page=-1):
    return {"code": 0, "success": True, "data": {"notes": notes, "page": page}}


class AdapterTests(unittest.TestCase):
    def test_exact_values_and_missing_are_distinct(self):
        page = parse_page(payload([raw_note(comments_count=None, collected_count="42")]))
        note = page.notes[0]
        self.assertEqual(note.read_count, 0)
        self.assertIsNone(note.comment_count)
        self.assertEqual(note.collect_count, 42)
        self.assertEqual(note.read_label, "浏览量")
        for unknown in (True, -1, "1.2万", "—", "1,234", float("nan")):
            self.assertIsNone(count(unknown))

    def test_label_preserves_source(self):
        note = parse_page(payload([raw_note()]), read_label="阅读量").notes[0]
        self.assertEqual(note.read_label, "阅读量")

    def test_newest_five_ignore_pin_and_deduplicate(self):
        rows = [raw_note("pinned", 1690000000, sticky=True)]
        rows += [raw_note(str(i), 1700000000 + i) for i in range(7)]
        rows.append(raw_note("6", 1700000006, view_count=999))
        recent = recent_notes(parse_page(payload(rows)).notes)
        self.assertEqual([n.id for n in recent], ["6", "5", "4", "3", "2"])
        self.assertEqual(recent[0].read_count, 999)

    def test_less_than_five_empty_and_new_publication(self):
        self.assertEqual(parse_page(payload([])).notes, [])
        notes = parse_page(payload([raw_note("old")])).notes
        self.assertEqual(len(recent_notes(notes)), 1)
        notes += parse_page(payload([raw_note("new", 1701000000)])).notes
        self.assertEqual(recent_notes(notes)[0].id, "new")

    def test_scheduled_pending_and_rejected_excluded(self):
        rows = [raw_note(), raw_note("scheduled", schedule_post_time=1800000000),
                raw_note("pending", tab_status=2), raw_note("rejected", tab_status=3)]
        self.assertEqual([n.id for n in parse_page(payload(rows)).notes], ["a"])

    def test_timestamps_seconds_milliseconds_and_china(self):
        self.assertEqual(timestamp(1700000000), timestamp(1700000000000))
        self.assertEqual(timestamp("2026-10-07 10:00:00")[0], "2026-10-07T10:00:00+08:00")
        for invalid in (None, "昨天", True, -1):
            with self.assertRaises(SyncError):
                timestamp(invalid)

    def test_schema_changes_and_authentication(self):
        for bad in ({}, {"data": {"notes": []}}, payload([{"id": "a"}]), {"data": {"notes": {}, "page": -1}}):
            with self.assertRaises(SyncError):
                parse_page(bad)
        with self.assertRaises(LoginRequired):
            parse_page({}, 401)
        with self.assertRaises(LoginRequired):
            parse_page({"code": -100, "message": "请登录"})
        with self.assertRaises(VerificationRequired):
            parse_page({}, 429)
        with self.assertRaises(VerificationRequired):
            parse_page({"code": 999, "message": "安全验证"})

    def test_only_published_official_endpoint(self):
        base = "https://edith.xiaohongshu.com/api/galaxy/v2/creator/note/user/posted"
        self.assertEqual(posted_request(base + "?tab=1&page=0"), 0)
        self.assertEqual(posted_request(base + "?tab=1&page=12"), 12)
        for url in (base + "?tab=0", base.replace("edith.xiaohongshu.com", "evil.test") + "?tab=1",
                    base.replace("https:", "http:") + "?tab=1", base + "?tab=1&page=oops"):
            self.assertIsNone(posted_request(url))


class CoreTests(unittest.TestCase):
    def setUp(self):
        # Keep test artifacts rather than bulk-deleting directories.
        self.path = Path(__file__).resolve().parents[1] / ".cache" / "tests" / uuid.uuid4().hex
        self.store = Storage(self.path)

    def transport(self, result=None, error=None):
        class Transport:
            def collect(self, stop):
                if error:
                    raise error
                return result or []
        return Transport()

    def test_cache_success_restart_and_network_failure(self):
        notes = parse_page(payload([raw_note()])).notes
        service = SyncService(self.store, self.transport(notes))
        service.once()
        before = service.snapshot()
        self.assertEqual(before["status"], "ready")
        self.assertFalse(before["cached"])
        self.assertGreater(service.next_due - time.monotonic(), 895)
        restarted = SyncService(self.store, self.transport(error=SyncError("连接超时")))
        self.assertTrue(restarted.snapshot()["cached"])
        restarted.once()
        failed = restarted.snapshot()
        self.assertEqual(failed["notes"], before["notes"])
        self.assertEqual(failed["last_success"], before["last_success"])
        self.assertEqual(failed["status"], "error")

    def test_auth_pauses_and_requires_resume(self):
        for error, state in ((LoginRequired("需要登录"), "login_required"), (VerificationRequired("需要验证"), "verification_required")):
            service = SyncService(self.store, self.transport(error=error))
            service.once()
            self.assertEqual(service.snapshot()["status"], state)
            self.assertTrue(service.paused)
            self.assertFalse(service.refresh())
            self.assertFalse(service.once())
            self.assertTrue(service.refresh(resume=True))
            self.assertFalse(service.paused)

    def test_manual_and_scheduled_do_not_overlap(self):
        entered, release = threading.Event(), threading.Event()
        class SlowTransport:
            calls = 0
            def collect(self, stop):
                self.calls += 1
                entered.set()
                release.wait(3)
                return []
        transport = SlowTransport()
        service = SyncService(self.store, transport)
        worker = threading.Thread(target=service.once)
        worker.start()
        self.assertTrue(entered.wait(1))
        self.assertFalse(service.refresh())
        self.assertFalse(service.once())
        release.set()
        worker.join(2)
        self.assertEqual(transport.calls, 1)

    def test_storage_error_keeps_previous_snapshot(self):
        service = SyncService(self.store, self.transport(parse_page(payload([raw_note()])).notes))
        service.once()
        before = service.snapshot()
        self.store.write = lambda *_: (_ for _ in ()).throw(OSError("保存失败"))
        service.transport = self.transport([])
        service.once()
        self.assertEqual(service.snapshot()["notes"], before["notes"])
        self.assertEqual(service.snapshot()["status"], "error")

    def test_settings_persist_and_corruption_is_recoverable(self):
        self.store.set("position", [-200, 300])
        self.store.set("on_top", True)
        restored = Storage(self.path)
        self.assertTrue(restored.settings["on_top"])
        self.assertEqual(restored.settings["position"], [-200, 300])
        (self.path / "settings.json").write_text('{broken', encoding="utf-8")
        with self.assertLogs("xhsboard", level="ERROR"):
            self.assertFalse(Storage(self.path).settings["on_top"])
        self.assertEqual((self.path / "settings.json").read_text(), '{broken')

    def test_malformed_snapshot_does_not_break_ui(self):
        self.store.write("snapshot.json", {"notes": [None], "last_success": "2026-10-07T12:00:00+08:00"})
        service = SyncService(self.store, self.transport())
        self.assertEqual(service.snapshot()["notes"], [])
        self.assertIsNone(service.snapshot()["last_success"])


class CollectorTests(unittest.TestCase):
    def test_configured_recent_count_and_invalid_limit(self):
        from xhs_adapter import recent_notes
        notes = parse_page(payload([raw_note(str(i), 1700000000+i) for i in range(30)])).notes
        self.assertEqual(len(recent_notes(notes, 1)), 1)
        self.assertEqual([n.id for n in recent_notes(notes, 10)], [str(i) for i in range(29, 19, -1)])
        self.assertEqual(len(recent_notes(notes, 50)), 30)
        for limit in (0, 51, True, 3.5):
            with self.assertRaises(ValueError):
                recent_notes(notes, limit)

    def make_collector(self, pages):
        from collector import Collector
        class Event:
            def __iadd__(self, callback):
                return self
        class Events:
            before_show, loaded = Event(), Event()
        class Window:
            events = Events()
            hidden = False
            remaining = copy.deepcopy(pages)
            def load_url(self, url):
                self.feed()
            def get_current_url(self):
                return ""
            def feed(self):
                if self.remaining:
                    number, data = self.remaining.pop(0)
                    self.collector.responses.put((self.collector.generation, number, 200, data))
            def evaluate_js(self, script):
                if 'scrollIntoView' in script:
                    self.feed()
                    return True
                return {"state": "manager", "label": "浏览量"}
            def hide(self):
                self.hidden = True
        window = Window()
        collector = Collector(window)
        window.collector = collector
        collector.ready.set()
        return collector, window

    def test_pagination_pinned_notes_and_hidden_window(self):
        collector, window = self.make_collector([
            (0, payload([raw_note("pin", 1690000000, sticky=True), raw_note("1")], page=1)),
            (1, payload([raw_note(str(i), 1700000000+i) for i in range(2, 6)], page=-1))])
        result = collector.collect(threading.Event(), timeout=5)
        self.assertEqual([n.id for n in result], ["5", "4", "3", "2", "1"])
        self.assertTrue(window.hidden)
        self.assertFalse(collector.active)

    def test_changed_pagination_does_not_report_success(self):
        collector, _ = self.make_collector([(0, payload([raw_note()], page=0))])
        with self.assertRaises(SyncError):
            collector.collect(threading.Event(), timeout=5)

    def test_more_than_five_collects_required_pages(self):
        collector, _ = self.make_collector([
            (0, payload([raw_note(str(i), 1700000000+i) for i in range(6)], page=1)),
            (1, payload([raw_note(str(i), 1700000000+i) for i in range(6, 12)], page=-1))])
        result = collector.collect(threading.Event(), timeout=5, limit=10)
        self.assertEqual([n.id for n in result], [str(i) for i in range(11, 1, -1)])


if __name__ == "__main__":
    unittest.main()
