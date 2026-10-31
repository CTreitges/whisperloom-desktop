"""Echte Eingaben für die Suite input (nur Windows, nur mit UI-Lock): SendInput-Tasten und -Klicks, Vordergrundfenster,
Zwischenablage setzen und ihre Formate beobachten (ohne sie zu öffnen).

Doku: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput, .../nf-winuser-isclipboardformatavailable,
.../dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats (abgerufen 2026-10-31).
"""
from __future__ import annotations

import ctypes
import sys
import threading
import time

VK = {"ctrl": 0xA2, "alt": 0xA4, "shift": 0xA0, "win": 0x5B, "space": 0x20, "enter": 0x0D, "back": 0x08}
EXTENDED = {0x5B}
CF_UNICODETEXT = 13
START_MENU_PROCESSES = ("startmenuexperiencehost.exe", "searchhost.exe", "searchapp.exe", "shellexperiencehost.exe")


def _user32():
    return ctypes.WinDLL("user32", use_last_error=True)


def _input_types():
    from ctypes import wintypes

    class KEYBDINPUT(ctypes.Structure):
        _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD), ("dwFlags", wintypes.DWORD),
                    ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]

    class MOUSEINPUT(ctypes.Structure):
        _fields_ = [("dx", wintypes.LONG), ("dy", wintypes.LONG), ("mouseData", wintypes.DWORD),
                    ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD), ("dwExtraInfo", ctypes.c_size_t)]

    class UNION(ctypes.Union):
        _fields_ = [("mi", MOUSEINPUT), ("ki", KEYBDINPUT)]

    class INPUT(ctypes.Structure):
        _fields_ = [("type", wintypes.DWORD), ("u", UNION)]

    return INPUT, KEYBDINPUT, MOUSEINPUT


class NotForeground(RuntimeError):
    pass


class Keyboard:
    """Drückt und löst Tasten wie ein Mensch (ohne die App-Kennung in dwExtraInfo); merkt sich gedrückte Tasten."""

    def __init__(self) -> None:
        self.user32 = _user32()
        self.INPUT, self.KEYBDINPUT, self.MOUSEINPUT = _input_types()
        self.held: list[int] = []
        self.guard = None  # Callable: True, solange Tasten das Ziel treffen; sonst Abbruch vor dem Drücken.

    def _send(self, *inputs) -> None:
        arr = (self.INPUT * len(inputs))(*inputs)
        self.user32.SendInput(len(inputs), arr, ctypes.sizeof(self.INPUT))

    def _key(self, vk: int, up: bool):
        flags = (0x2 if up else 0) | (0x1 if vk in EXTENDED else 0)
        return self.INPUT(1, self.INPUT._fields_[1][1](ki=self.KEYBDINPUT(vk, 0, flags, 0, 0)))

    def down(self, name: str) -> None:
        if self.guard is not None and not self.guard():
            self.release_all()
            raise NotForeground(f"Vordergrund ist nicht das Ziel ({process_name(foreground())}) — Taste {name} nicht gesendet")
        vk = VK[name]
        self._send(self._key(vk, False))
        self.held.append(vk)
        time.sleep(0.05)

    def up(self, name: str) -> None:
        vk = VK[name]
        self._send(self._key(vk, True))
        if vk in self.held:
            self.held.remove(vk)
        time.sleep(0.05)

    def combo(self, *names: str) -> None:
        """Drückt die Tasten nacheinander und löst sie in umgekehrter Reihenfolge."""
        for n in names:
            self.down(n)
        for n in reversed(names):
            self.up(n)

    def release_all(self) -> None:
        for vk in reversed(list(self.held)):
            self._send(self._key(vk, True))
        self.held.clear()

    def click(self, x: int, y: int) -> None:
        """Linksklick an Bildschirm-Pixeln (Prozess ist Per-Monitor-DPI-aware)."""
        u = self.user32
        left, top = u.GetSystemMetrics(76), u.GetSystemMetrics(77)
        width, height = u.GetSystemMetrics(78), u.GetSystemMetrics(79)
        nx = int((x - left) * 65535 / max(width - 1, 1))
        ny = int((y - top) * 65535 / max(height - 1, 1))
        move = 0x0001 | 0x8000 | 0x4000  # MOVE | ABSOLUTE | VIRTUALDESK
        mk = lambda flags: self.INPUT(0, self.INPUT._fields_[1][1](mi=self.MOUSEINPUT(nx, ny, 0, flags, 0, 0)))  # noqa: E731
        self._send(mk(move))
        time.sleep(0.05)
        self._send(mk(move | 0x0002), mk(move | 0x0004))  # LEFTDOWN, LEFTUP
        time.sleep(0.1)


def dpi_aware() -> None:
    try:
        _user32().SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))  # PER_MONITOR_AWARE_V2
    except (AttributeError, OSError):
        pass


def force_foreground(hwnd: int) -> None:
    """SetForegroundWindow nach eigener (unbelegter) Taste: so gilt dieser Prozess als Absender der letzten Eingabe."""
    k = Keyboard()
    k._send(k._key(0xE8, False), k._key(0xE8, True))
    _user32().SetForegroundWindow(hwnd)


def foreground() -> int:
    return _user32().GetForegroundWindow() or 0


def window_rect(hwnd: int) -> tuple[int, int, int, int]:
    from ctypes import wintypes
    r = wintypes.RECT()
    _user32().GetWindowRect(wintypes.HWND(hwnd), ctypes.byref(r))
    return r.left, r.top, r.right, r.bottom


def process_name(hwnd: int) -> str:
    """Exe-Name des Fensterbesitzers (klein), "" wenn nicht lesbar."""
    from ctypes import wintypes
    pid = wintypes.DWORD()
    _user32().GetWindowThreadProcessId(wintypes.HWND(hwnd), ctypes.byref(pid))
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    h = k32.OpenProcess(0x1000, False, pid.value)
    if not h:
        return ""
    try:
        buf = ctypes.create_unicode_buffer(512)
        size = wintypes.DWORD(512)
        if not k32.QueryFullProcessImageNameW(h, 0, buf, ctypes.byref(size)):
            return ""
        return buf.value.rsplit("\\", 1)[-1].lower()
    finally:
        k32.CloseHandle(h)


def set_clipboard(text: str) -> None:
    """Normaler Zwischenablage-Eintrag (wie Strg+C eines Nutzers)."""
    u = _user32()
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.GlobalAlloc.restype = ctypes.c_void_p
    k32.GlobalLock.restype = ctypes.c_void_p
    k32.GlobalLock.argtypes = [ctypes.c_void_p]
    k32.GlobalUnlock.argtypes = [ctypes.c_void_p]
    u.SetClipboardData.argtypes = [ctypes.c_uint, ctypes.c_void_p]
    data = text.encode("utf-16-le") + b"\0\0"
    for _ in range(20):
        if u.OpenClipboard(None):
            break
        time.sleep(0.05)
    else:
        raise OSError("Zwischenablage belegt")
    try:
        u.EmptyClipboard()
        h = k32.GlobalAlloc(0x0002, len(data))
        p = k32.GlobalLock(h)
        ctypes.memmove(p, data, len(data))
        k32.GlobalUnlock(h)
        u.SetClipboardData(CF_UNICODETEXT, h)
    finally:
        u.CloseClipboard()


class ClipboardWatch:
    """Beobachtet jede Änderung der Zwischenablage (Sequenznummer) und notiert, welche Formate es gibt —
    nur über IsClipboardFormatAvailable, ohne die Zwischenablage zu öffnen (stört das Einfügen nicht)."""

    def __init__(self) -> None:
        u = _user32()
        u.RegisterClipboardFormatW.argtypes = [ctypes.c_wchar_p]
        self.exclude = u.RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing")
        self.history = u.RegisterClipboardFormatW("CanIncludeInClipboardHistory")
        self.observations: list[dict] = []
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._run, daemon=True)

    def _run(self) -> None:
        u = _user32()
        last = u.GetClipboardSequenceNumber()
        while not self._stop.is_set():
            seq = u.GetClipboardSequenceNumber()
            if seq != last:
                last = seq
                self.observations.append({"seq": seq, "text": bool(u.IsClipboardFormatAvailable(CF_UNICODETEXT)),
                                          "exclude": bool(u.IsClipboardFormatAvailable(self.exclude)),
                                          "history": bool(u.IsClipboardFormatAvailable(self.history))})
            time.sleep(0.001)

    def __enter__(self) -> ClipboardWatch:
        self._thread.start()
        return self

    def __exit__(self, *exc) -> None:
        self._stop.set()
        self._thread.join(timeout=2)


def clipboard_private(observations: list[dict]) -> tuple[bool, str]:
    """OK = es gab Text mit beiden Ausschluss-Formaten und nie Text ohne sie."""
    with_text = [o for o in observations if o["text"]]
    leaks = [o for o in with_text if not (o["exclude"] and o["history"])]
    if not with_text:
        return False, f"keine Textänderung beobachtet ({len(observations)} Änderungen)"
    if leaks:
        return False, f"{len(leaks)} von {len(with_text)} Textständen ohne Ausschluss-Formate"
    return True, f"{len(with_text)} Textstände, alle ausgeschlossen"


if __name__ == "__main__":
    sys.exit("Modul von tools/smoke.py")
