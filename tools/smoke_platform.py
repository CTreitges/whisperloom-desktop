"""OS-Teil der Smokes: Key-Ablage direkt lesen und aufräumen, Zwischenablage lesen.

Windows: Credential Manager über advapi32 (CredReadW/CredDeleteW), Ziel "<präfix><name>".
Linux: secret-tool (service = Präfix ohne ":", account = Name) bzw. <Datenordner>/secrets.json mit Rechten 600.
Tray (nur Windows): Ist das Symbol der App-PID beim Shell registriert? (Shell_NotifyIconGetRect, siehe tray_icon.)
"""
from __future__ import annotations

import ctypes
import json
import os
import stat
import subprocess
import sys
from pathlib import Path

NAMES = ("stt", "stt_fallback", "llm", "llm_fallback")


def _cred_api():
    from ctypes import wintypes

    class CREDENTIAL(ctypes.Structure):
        _fields_ = [
            ("Flags", wintypes.DWORD), ("Type", wintypes.DWORD), ("TargetName", wintypes.LPWSTR),
            ("Comment", wintypes.LPWSTR), ("LastWritten", wintypes.FILETIME), ("CredentialBlobSize", wintypes.DWORD),
            ("CredentialBlob", ctypes.POINTER(ctypes.c_ubyte)), ("Persist", wintypes.DWORD),
            ("AttributeCount", wintypes.DWORD), ("Attributes", ctypes.c_void_p), ("TargetAlias", wintypes.LPWSTR),
            ("UserName", wintypes.LPWSTR),
        ]

    advapi = ctypes.WinDLL("advapi32", use_last_error=True)
    advapi.CredReadW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(ctypes.POINTER(CREDENTIAL))]
    advapi.CredReadW.restype = wintypes.BOOL
    advapi.CredDeleteW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD]
    advapi.CredDeleteW.restype = wintypes.BOOL
    advapi.CredFree.argtypes = [ctypes.c_void_p]
    return advapi, CREDENTIAL


def win_read(target: str) -> str | None:
    advapi, cred_type = _cred_api()
    ptr = ctypes.POINTER(cred_type)()
    if not advapi.CredReadW(target, 1, 0, ctypes.byref(ptr)):
        return None
    try:
        c = ptr.contents
        return bytes(c.CredentialBlob[: c.CredentialBlobSize]).decode("utf-16-le")
    finally:
        advapi.CredFree(ptr)


def win_delete(target: str) -> None:
    advapi, _ = _cred_api()
    advapi.CredDeleteW(target, 1, 0)


def read_secret(platform: str, prefix: str, name: str, data_dir: Path, kind: str | None) -> tuple[str | None, str]:
    """(Wert, Beschreibung der Ablage). Der Wert verlässt den Smoke nie (nur Vergleich)."""
    if platform == "win":
        return win_read(prefix + name), f"Credential Manager {prefix}{name}"
    if kind == "Secret Service":
        r = subprocess.run(["secret-tool", "lookup", "service", prefix.rstrip(":"), "account", name],
                           capture_output=True, text=True)
        return (r.stdout.rstrip("\n") or None), f"Secret Service {prefix.rstrip(':')}/{name}"
    path = data_dir / "secrets.json"
    if not path.exists():
        return None, f"{path} fehlt"
    mode = stat.S_IMODE(os.stat(path).st_mode)
    if mode != 0o600:
        return None, f"{path} hat Rechte {mode:o} statt 600"
    return json.loads(path.read_text(encoding="utf-8")).get(prefix + name), f"Datei {path} (600)"


def cleanup_secrets(platform: str, prefix: str, data_dir: Path) -> None:
    """Löscht alle Testeinträge mit dem Test-Präfix — nie Einträge ohne "WhisperLoom-Test-"."""
    if not prefix.startswith("WhisperLoom-Test-"):
        raise ValueError("cleanup nur für Test-Präfixe")
    if platform == "win":
        for name in NAMES:
            win_delete(prefix + name)
        return
    for name in NAMES:
        subprocess.run(["secret-tool", "clear", "service", prefix.rstrip(":"), "account", name], capture_output=True)
    (data_dir / "secrets.json").unlink(missing_ok=True)


# Avalonia (Win32): TrayIconImpl meldet das Symbol mit hWnd = Win32Platform.Instance.Handle (verstecktes Fenster der
# Klasse "AvaloniaMessageWindow <guid>") und uID = 1, 2, … an.
# Doku: github.com/AvaloniaUI/Avalonia src/Windows/Avalonia.Win32/TrayIconImpl.cs und Win32Platform.cs (abgerufen 2026-10-30);
# learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shell_notifyicongetrect (abgerufen 2026-10-30).
TRAY_WINDOW_CLASS = "AvaloniaMessageWindow "
MAX_TRAY_IDS = 4


def tray_owner_windows(pid: int, windows) -> list[int]:
    """Fenster der Prozess-PID, über die Avalonia Tray-Symbole anmeldet. windows: (hwnd, pid, Klassenname)."""
    return [hwnd for hwnd, owner, cls in windows if owner == pid and cls.startswith(TRAY_WINDOW_CLASS)]


def find_tray_icon(pid: int, windows, icon_rect) -> str | None:
    """Beschreibung des ersten registrierten Symbols der PID, sonst None. icon_rect(hwnd, uid) → Rechteck oder None."""
    for hwnd in tray_owner_windows(pid, windows):
        for uid in range(1, MAX_TRAY_IDS + 1):
            rect = icon_rect(hwnd, uid)
            if rect:
                return f"hwnd=0x{hwnd:x} uid={uid} rect={rect}"
    return None


def win_windows() -> list[tuple[int, int, str]]:
    """Alle Top-Level-Fenster (auch unsichtbare) als (hwnd, pid, Klassenname)."""
    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    found: list[tuple[int, int, str]] = []
    proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def visit(hwnd, _):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        name = ctypes.create_unicode_buffer(256)
        user32.GetClassNameW(hwnd, name, 256)
        found.append((hwnd or 0, pid.value, name.value))
        return True

    user32.EnumWindows(proc(visit), 0)
    return found


def win_icon_rect(hwnd: int, uid: int) -> tuple[int, int, int, int] | None:
    """Rechteck des Symbols (hwnd, uid) im Infobereich; None, wenn der Shell es nicht kennt."""
    from ctypes import wintypes

    class NOTIFYICONIDENTIFIER(ctypes.Structure):
        _fields_ = [("cbSize", wintypes.DWORD), ("hWnd", wintypes.HWND), ("uID", wintypes.UINT), ("guidItem", ctypes.c_ubyte * 16)]

    ident = NOTIFYICONIDENTIFIER(ctypes.sizeof(NOTIFYICONIDENTIFIER), hwnd, uid)
    rect = wintypes.RECT()
    shell32 = ctypes.WinDLL("shell32")
    shell32.Shell_NotifyIconGetRect.restype = ctypes.c_long
    if shell32.Shell_NotifyIconGetRect(ctypes.byref(ident), ctypes.byref(rect)) != 0:
        return None
    return rect.left, rect.top, rect.right, rect.bottom


def tray_icon(pid: int) -> str | None:
    """Tray-Symbol der App (nur Windows); liest nur, klickt nichts."""
    return find_tray_icon(pid, win_windows(), win_icon_rect)


def read_clipboard(platform: str) -> str | None:
    """Echte Zwischenablage — nur in den Suites ui/input mit Lock."""
    if platform == "win":
        r = subprocess.run(["powershell", "-NoProfile", "-Command", "Get-Clipboard -Raw"], capture_output=True, text=True, encoding="utf-8")
        return r.stdout.rstrip("\r\n") if r.returncode == 0 else None
    for cmd in (["wl-paste", "--no-newline"], ["xclip", "-selection", "clipboard", "-o"]):
        try:
            r = subprocess.run(cmd, capture_output=True, text=True, timeout=5)
        except (OSError, subprocess.TimeoutExpired):
            continue
        if r.returncode == 0:
            return r.stdout
    return None


if __name__ == "__main__":
    sys.exit("Modul von tools/smoke.py")
