"""Mapping verified against the official creator site's note-manager bundle.

Endpoint and fields observed 2026-10-07 in the site's publicly served assets.
Only normal page requests are observed: no cookie export or request signing.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass
from datetime import datetime, timezone, timedelta
import math
import re
from urllib.parse import parse_qs, urlparse

CREATOR_URL = "https://creator.xiaohongshu.com/new/note-manager"
POSTED_PATH = "/api/galaxy/v2/creator/note/user/posted"
CHINA = timezone(timedelta(hours=8))


class SyncError(Exception):
    pass


class LoginRequired(SyncError):
    pass


class VerificationRequired(SyncError):
    pass


@dataclass(frozen=True)
class Note:
    id: str
    title: str
    published_at: str
    published_timestamp: float
    read_label: str
    read_count: int | None
    comment_count: int | None
    collect_count: int | None
    sticky: bool = False

    def to_dict(self):
        return asdict(self)


@dataclass
class Page:
    notes: list[Note]
    next_page: int


def is_official(url):
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    return parsed.scheme == "https" and (host == "xiaohongshu.com" or host.endswith(".xiaohongshu.com"))


def posted_request(url):
    parsed = urlparse(url)
    if not is_official(url) or parsed.path != POSTED_PATH:
        return None
    query = parse_qs(parsed.query)
    # Reading the published tab avoids drafts, scheduled notes and pending reviews.
    if query.get("tab") != ["1"]:
        return None
    try:
        return int(query.get("page", ["0"])[0])
    except ValueError:
        return None


def count(value):
    if type(value) is int:
        return value if value >= 0 else None
    if type(value) is float and math.isfinite(value) and value.is_integer() and value >= 0:
        return int(value)
    if isinstance(value, str) and re.fullmatch(r"\d+", value.strip()):
        return int(value.strip())
    # Rounded numbers such as 1.2万 are not exact counters.
    return None


def timestamp(value):
    if isinstance(value, bool) or value is None:
        raise SyncError("文章发布时间缺失，无法可靠确定最近文章")
    try:
        if isinstance(value, (float, int)) or (isinstance(value, str) and re.fullmatch(r"\d+(\.\d+)?", value)):
            seconds = float(value)
            if seconds > 10**11:
                seconds /= 1000
            if not math.isfinite(seconds) or seconds <= 0:
                raise ValueError()
            date = datetime.fromtimestamp(seconds, CHINA)
        else:
            text = str(value).strip().replace("发布于", "").strip().replace("/", "-")
            date = datetime.fromisoformat(text.replace("Z", "+00:00"))
            if date.tzinfo is None:
                date = date.replace(tzinfo=CHINA)
        return date.isoformat(timespec="seconds"), date.timestamp()
    except (OverflowError, OSError, ValueError):
        raise SyncError("文章发布时间格式变化，需要检查创作后台") from None


def parse_page(payload, status=200, read_label="浏览量"):
    if status in (401,):
        raise LoginRequired("登录已失效，请重新登录")
    if status in (403, 429):
        raise VerificationRequired("后台需要验证或暂时限制访问，请打开创作后台")
    if status != 200 or not isinstance(payload, dict):
        raise SyncError("小红书后台返回异常，请稍后刷新")
    code = payload.get("code", 0)
    message = str(payload.get("message") or payload.get("msg") or "")
    if code in (-100, -101, -1, 10001) and any(w in message.lower() for w in ("登录", "登陆", "login", "session")):
        raise LoginRequired("登录已失效，请重新登录")
    if any(w in message for w in ("验证码", "安全验证", "访问频繁")):
        raise VerificationRequired("后台需要验证，请打开创作后台")
    if code != 0 or payload.get("success") is False:
        raise SyncError("创作后台未返回文章数据，请打开后台检查")
    data = payload.get("data", payload)
    if not isinstance(data, dict) or not isinstance(data.get("notes"), list):
        raise SyncError("文章列表结构变化，需要检查创作后台")
    next_page = data.get("page")
    if type(next_page) is not int or next_page < -1:
        raise SyncError("文章分页结构变化，需要检查创作后台")
    notes = []
    for raw in data["notes"]:
        if not isinstance(raw, dict) or not raw.get("id"):
            raise SyncError("文章字段变化，需要检查创作后台")
        if count(raw.get("schedule_post_time")) not in (None, 0) or raw.get("tab_status") in (2, 3, 4):
            continue
        published, seconds = timestamp(raw.get("time"))
        notes.append(Note(str(raw["id"]), str(raw.get("display_title") or "无标题笔记"),
                          published, seconds, read_label, count(raw.get("view_count")),
                          count(raw.get("comments_count")), count(raw.get("collected_count")),
                          bool(raw.get("sticky", False))))
    return Page(notes, next_page)


def recent_notes(notes, limit=5):
    if type(limit) is not int or not 1 <= limit <= 50:
        raise ValueError("文章数量需为 1–50")
    # Prefer the last response for duplicate IDs (the pinned note may recur).
    deduplicated = {note.id: note for note in notes}
    return sorted(deduplicated.values(), key=lambda note: (note.published_timestamp, note.id), reverse=True)[:limit]
