"""Windows window styles, single-instance activation, and opt-in autostart."""
import ctypes
from ctypes import wintypes as wt
import hashlib
from pathlib import Path
import subprocess
import sys
import threading
import winreg

from storage import app_dir

GWL_EXSTYLE = -20
WS_EX_TOOLWINDOW, WS_EX_APPWINDOW = 0x80, 0x40000
WS_EX_LAYERED, WS_EX_TRANSPARENT, WS_EX_NOACTIVATE = 0x80000, 0x20, 0x8000000

user32 = ctypes.WinDLL("user32", use_last_error=True)
user32.GetWindowLongPtrW.argtypes = [wt.HWND, ctypes.c_int]
user32.GetWindowLongPtrW.restype = ctypes.c_ssize_t
user32.SetWindowLongPtrW.argtypes = [wt.HWND, ctypes.c_int, ctypes.c_ssize_t]
user32.SetWindowLongPtrW.restype = ctypes.c_ssize_t
user32.SetWindowPos.argtypes = [wt.HWND, wt.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.UINT]
user32.SetWindowPos.restype = wt.BOOL


class SingleInstance:
    def __init__(self, directory):
        identity = hashlib.sha256(str(directory).lower().encode()).hexdigest()[:16]
        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        self.api.CreateMutexW.argtypes = [ctypes.c_void_p, wt.BOOL, wt.LPCWSTR]
        self.api.CreateMutexW.restype = wt.HANDLE
        self.api.CreateEventW.argtypes = [ctypes.c_void_p, wt.BOOL, wt.BOOL, wt.LPCWSTR]
        self.api.CreateEventW.restype = wt.HANDLE
        self.api.OpenEventW.argtypes = [wt.DWORD, wt.BOOL, wt.LPCWSTR]
        self.api.OpenEventW.restype = wt.HANDLE
        self.api.SetEvent.argtypes = [wt.HANDLE]
        self.api.WaitForSingleObject.argtypes = [wt.HANDLE, wt.DWORD]
        self.api.CloseHandle.argtypes = [wt.HANDLE]
        self.mutex = self.api.CreateMutexW(None, False, "Local\\XhsBoard-" + identity)
        if not self.mutex:
            raise ctypes.WinError(ctypes.get_last_error())
        self.existing = ctypes.get_last_error() == 183
        name = "Local\\XhsBoard-Activate-" + identity
        self.event = self.api.CreateEventW(None, False, False, name)
        if not self.event:
            self.api.CloseHandle(self.mutex)
            raise ctypes.WinError(ctypes.get_last_error())
        if self.existing:
            self.api.SetEvent(self.event)

    def listen(self, stop, callback):
        def worker():
            while not stop.is_set():
                if self.api.WaitForSingleObject(self.event, 500) == 0 and not stop.is_set():
                    callback()
        self.thread = threading.Thread(target=worker, daemon=True, name="activate-listener")
        self.thread.start()

    def close(self):
        if getattr(self, "thread", None):
            self.thread.join(timeout=1)
        self.api.CloseHandle(self.event)
        self.api.CloseHandle(self.mutex)


def hwnd(window):
    return wt.HWND(window.native.Handle.ToInt64())


def round_window(window):
    handle = hwnd(window)
    rect = wt.RECT()
    user32.GetClientRect(handle, ctypes.byref(rect))
    user32.GetDpiForWindow.argtypes = [wt.HWND]
    user32.GetDpiForWindow.restype = wt.UINT
    corner = round(32 * (user32.GetDpiForWindow(handle) or 96) / 96)
    gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
    gdi32.CreateRoundRectRgn.argtypes = [ctypes.c_int] * 6
    gdi32.CreateRoundRectRgn.restype = ctypes.c_void_p
    gdi32.DeleteObject.argtypes = [ctypes.c_void_p]
    user32.SetWindowRgn.argtypes = [wt.HWND, ctypes.c_void_p, wt.BOOL]
    region = gdi32.CreateRoundRectRgn(0, 0, rect.right + 1, rect.bottom + 1, corner, corner)
    if not region:
        raise ctypes.WinError(ctypes.get_last_error())
    if not user32.SetWindowRgn(handle, region, True):
        gdi32.DeleteObject(region)
        raise ctypes.WinError(ctypes.get_last_error())


class CardStyle:
    def __init__(self, window):
        self.window = window
        self.lock = threading.RLock()
        self.original = None

    def apply(self, through=False):
        with self.lock:
            handle = hwnd(self.window)
            current = user32.GetWindowLongPtrW(handle, GWL_EXSTYLE)
            desired = (current | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW
            mask = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE
            if through:
                if self.original is None:
                    self.original = current & mask
                desired |= mask
            elif self.original is not None:
                desired = (desired & ~mask) | self.original
                self.original = None
            ctypes.set_last_error(0)
            user32.SetWindowLongPtrW(handle, GWL_EXSTYLE, desired)
            if ctypes.get_last_error():
                raise ctypes.WinError(ctypes.get_last_error())
            # No SetLayeredWindowAttributes: WebView2 owns the transparent surface.
            round_window(self.window)

    def pin(self, enabled):
        if not user32.SetWindowPos(hwnd(self.window), wt.HWND(-1 if enabled else -2), 0, 0, 0, 0, 0x13):
            raise ctypes.WinError(ctypes.get_last_error())

    def remove_taskbar(self):
        import comtypes
        from comtypes import COMMETHOD, GUID, HRESULT, IUnknown

        class ITaskbarList(IUnknown):
            _iid_ = GUID("{56FDF342-FD6D-11D0-958A-006097C9A090}")
            _methods_ = [COMMETHOD([], HRESULT, "HrInit"),
                        COMMETHOD([], HRESULT, "AddTab", (["in"], wt.HWND, "hwnd")),
                        COMMETHOD([], HRESULT, "DeleteTab", (["in"], wt.HWND, "hwnd")),
                        COMMETHOD([], HRESULT, "ActivateTab", (["in"], wt.HWND, "hwnd")),
                        COMMETHOD([], HRESULT, "SetActiveAlt", (["in"], wt.HWND, "hwnd"))]
        comtypes.CoInitialize()
        try:
            taskbar = comtypes.CoCreateInstance(GUID("{56FDF344-FD6D-11D0-958A-006097C9A090}"), interface=ITaskbarList)
            taskbar.HrInit()
            taskbar.DeleteTab(hwnd(self.window))
        finally:
            comtypes.CoUninitialize()


def initial_position(position, width=380, height=520):
    # pywebview takes logical coordinates and internally applies system DPI.
    user32.GetDpiForSystem.restype = wt.UINT
    scale = (user32.GetDpiForSystem() or 96) / 96
    if position:
        left, top = user32.GetSystemMetrics(76) / scale, user32.GetSystemMetrics(77) / scale
        right = left + user32.GetSystemMetrics(78) / scale
        bottom = top + user32.GetSystemMetrics(79) / scale
        x, y = position
        if left <= x <= right - width and top <= y <= bottom - height:
            return x, y
    work = wt.RECT()
    user32.SystemParametersInfoW(0x30, 0, ctypes.byref(work), 0)
    return max(round(work.left / scale + 16), round(work.right / scale - width - 24)), max(round(work.top / scale + 16), round(work.bottom / scale - height - 24))


RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
RUN_NAME = "XhsDesktopBoard"


def autostart_command():
    if getattr(sys, "frozen", False):
        return subprocess.list2cmdline([str(Path(sys.executable).resolve()), "--background"])
    pythonw = Path(sys.executable).with_name("pythonw.exe")
    return subprocess.list2cmdline([str(pythonw), str(app_dir() / "app.py"), "--background"])


def get_autostart():
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
            return winreg.QueryValueEx(key, RUN_NAME)[0] == autostart_command()
    except FileNotFoundError:
        return False


def set_autostart(enabled):
    with winreg.CreateKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
        if enabled:
            winreg.SetValueEx(key, RUN_NAME, 0, winreg.REG_SZ, autostart_command())
        else:
            try:
                winreg.DeleteValue(key, RUN_NAME)
            except FileNotFoundError:
                pass
