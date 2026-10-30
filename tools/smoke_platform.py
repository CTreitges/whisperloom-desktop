"""OS-Teil der Smokes: Key-Ablage direkt lesen und aufräumen, Zwischenablage lesen.

Windows: Credential Manager über advapi32 (CredReadW/CredDeleteW), Ziel "<präfix><name>".
Linux: secret-tool (service = Präfix ohne ":", account = Name) bzw. <Datenordner>/secrets.json mit Rechten 600.
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
