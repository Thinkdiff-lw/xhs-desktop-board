"""Lifecycle regressions: reclaim idle pages without interrupting login/sync."""
import threading
import unittest
from unittest.mock import Mock

from remote_session import RemoteSession


class Event:
    def __isub__(self, callback):
        return self


class SessionTests(unittest.TestCase):
    def make_session(self):
        session = RemoteSession(None)
        created = []

        def ensure():
            if session.collector is None:
                collector = Mock()
                collector.has_user_login_window = False
                collector.window.events.closing = Event()
                collector.collect.return_value = ["note"]
                session.collector, session.window = collector, collector.window
                created.append(collector)
            return session.collector

        session._ensure = ensure
        return session, created

    def test_success_releases_and_next_sync_recreates(self):
        session, created = self.make_session()
        for _ in range(3):
            self.assertEqual(session.collect(threading.Event()), ["note"])
            self.assertIsNone(session.window)
        self.assertEqual(len(created), 3)
        for collector in created:
            collector.detach.assert_called_once()
            collector.window.destroy.assert_called_once()

    def test_hidden_failure_releases_but_visible_login_is_preserved(self):
        session, _ = self.make_session()
        collector = session._ensure()
        collector.collect.side_effect = RuntimeError("offline")
        with self.assertRaises(RuntimeError):
            session.collect(threading.Event())
        collector.window.destroy.assert_called_once()
        collector = session._ensure()
        collector.has_user_login_window = True
        collector.collect.side_effect = RuntimeError("login required")
        with self.assertRaises(RuntimeError):
            session.collect(threading.Event())
        collector.window.destroy.assert_not_called()
        self.assertIs(session.collector, collector)

    def test_user_close_during_sync_defers_release(self):
        session, _ = self.make_session()
        collector = session._ensure()
        session.busy = True
        session.release_idle()
        collector.window.destroy.assert_not_called()
        session.busy = False
        session.release_idle()
        collector.window.destroy.assert_called_once()

    def test_late_login_probe_is_discarded_after_release(self):
        session, _ = self.make_session()
        collector = session._ensure()
        collector.has_user_login_window = True

        def probe(_):
            session.release_idle(force=True)
            return {"state": "manager"}

        collector.window.evaluate_js.side_effect = probe
        self.assertIsNone(session.probe_login())


if __name__ == "__main__":
    unittest.main()
