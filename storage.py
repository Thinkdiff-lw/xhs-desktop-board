"""Local settings and the last successful snapshot; writes never remove directories."""
from __future__ import annotations

import json
import logging
import os
import sys
import threading
from pathlib import Path


def resource_dir() -> Path:
    return Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))


def app_dir() -> Path:
    return Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else Path(__file__).resolve().parent


THEMES = ("glass", "transparent")
DEFAULTS = {"visible": True, "on_top": False, "click_through": False, "position": None, "theme": "glass", "note_count": 5, "size": [380, 520]}


class Storage:
    def __init__(self, directory: Path):
        self.directory = directory.resolve()
        self.directory.mkdir(parents=True, exist_ok=True)
        self.lock = threading.RLock()
        saved = self.read("settings.json", {})
        self.settings = dict(DEFAULTS)
        if isinstance(saved, dict):
            if type(saved.get("note_count")) is int and 1 <= saved["note_count"] <= 50:
                self.settings["note_count"] = saved["note_count"]
            size = saved.get("size")
            if isinstance(size, list) and len(size) == 2 and all(type(v) in (int, float) for v in size) and 320 <= size[0] <= 2400 and 260 <= size[1] <= 2400:
                self.settings["size"] = size
            if saved.get("theme") in THEMES:
                self.settings["theme"] = saved["theme"]
            for key in ("visible", "on_top", "click_through"):
                if isinstance(saved.get(key), bool):
                    self.settings[key] = saved[key]
            pos = saved.get("position")
            if isinstance(pos, list) and len(pos) == 2 and all(type(v) in (int, float) for v in pos):
                self.settings["position"] = pos

    def read(self, filename, default):
        try:
            return json.loads((self.directory / filename).read_text(encoding="utf-8"))
        except FileNotFoundError:
            return default
        except (OSError, ValueError):
            logging.getLogger("xhsboard").exception("Cannot read %s", filename)
            return default

    def write(self, filename, value):
        with self.lock:
            destination = self.directory / filename
            temporary = destination.with_suffix(destination.suffix + ".tmp")
            with temporary.open("w", encoding="utf-8") as stream:
                json.dump(value, stream, ensure_ascii=False, indent=2, allow_nan=False)
                stream.flush()
                os.fsync(stream.fileno())
            os.replace(temporary, destination)

    def get_settings(self):
        with self.lock:
            return dict(self.settings)

    def set(self, key, value):
        if key not in DEFAULTS:
            raise ValueError("Unknown setting")
        if key == "theme" and value not in THEMES:
            raise ValueError("未知主题")
        with self.lock:
            updated = {**self.settings, key: value}
            self.write("settings.json", updated)
            self.settings = updated
