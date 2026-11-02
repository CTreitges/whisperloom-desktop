"""Baut die Verteil-Pakete von WhisperLoom: dotnet publish → vpk pack (Velopack) → portable ZIP (nur Windows).

Aufruf: py -3 -X utf8 build/pack.py --version <x.y.z> --rid <win-x64|linux-x64> --out <Releases-Ordner> [--work <ordner>]
        [--channel <stable|beta|test>]

Ergebnis in --out (Velopack-Feed, auch Quelle für `update --feed <ordner>`):
- win-x64:   WhisperLoom-win-Setup.exe, WhisperLoom-<v>-full.nupkg, releases.win.json, RELEASES, …
             und WhisperLoom-<v>-win-x64-portable.zip (ohne Velopack-Updater, für Firmen-Verteilung und eigene Signatur)
- linux-x64: WhisperLoom.AppImage, WhisperLoom-<v>-full.nupkg, releases.linux.json, …
Linux-Pakete lassen sich auch unter Windows bauen (vpk-Direktive [linux]). Signieren gehört nicht hierher: docs/signing.md.
Update-Kanäle (W3-CHAN): ohne --channel bzw. mit stable heißt der Velopack-Kanal wie das OS (win, linux; so wurde 1.0.0
gepackt), beta/test werden zu win-beta/linux-beta bzw. win-test/linux-test (releases.<kanal>.json, Setup-Name mit Kanal).

Doku: docs.velopack.io/getting-started/csharp, docs.velopack.io/packaging/cross-compiling,
docs.velopack.io/packaging/operating-systems/linux (abgerufen 2026-11-01); `dotnet vpk pack -H` (vpk 1.2.161);
Context7 /velopack/velopack.docs, Kanäle: jedes OS braucht einen eigenen Kanal (abgerufen 2026-11-02).
"""
from __future__ import annotations

import argparse
import platform
import re
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "WhisperLoom.App"
PACK_ID = "WhisperLoom"
AUTHORS = "Christof Treitges"
RIDS = ("win-x64", "linux-x64")
CHANNELS = ("stable", "beta", "test")
SEMVER = re.compile(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")


def host_os() -> str:
    return "win" if platform.system() == "Windows" else "linux"


def check_version(version: str) -> str:
    if not SEMVER.match(version):
        raise ValueError(f"Version muss SemVer sein (x.y.z[-pre]): {version!r}")
    return version


def publish_cmd(version: str, rid: str, publish_dir: Path) -> list[str]:
    return ["dotnet", "publish", str(APP), "-c", "Release", "-r", rid, "--self-contained", *readytorun_args(rid),
            f"-p:Version={version}", "-o", str(publish_dir)]


def readytorun_args(rid: str) -> list[str]:
    """ReadyToRun (PERF-3): vorkompiliert, weniger JIT beim Start und beim ersten Öffnen jeder Seite.

    Windows ohne Composite: Composite schreibt die .NET-Laufzeit in ein eigenes Image und entfernt damit die
    Microsoft-Signatur von ~175 Laufzeit-DLLs (WDAC/AppLocker „Herausgeber Microsoft“, docs/signing.md) – Entscheidung
    Christof 2026-11-02. Linux mit Composite (keine Authenticode-Signaturen; Doku empfiehlt es dort für den besten Start).
    Messung win-x64 am selben PC (Start bereit p50): ohne R2R 1572–1774 ms, R2R 979–1052 ms, Composite 828–872 ms.
    Doku: Context7 /dotnet/docs, docs/core/deploying/ready-to-run.md (abgerufen 2026-11-02): braucht RID, Composite nur
    self-contained.
    """
    args = ["-p:PublishReadyToRun=true"]
    if rid.startswith("linux"):
        args.append("-p:PublishReadyToRunComposite=true")
    return args


def target_os(rid: str) -> str:
    return "win" if rid.startswith("win") else "linux"


def channel_name(channel: str | None, rid: str) -> str | None:
    """Velopack-Kanal; None = ohne --channel (Stabil, Kanal = OS-Name wie bei 1.0.0)."""
    if channel in (None, "", "stable"):
        return None
    if channel not in CHANNELS:
        raise ValueError(f"Kanal muss stable, beta oder test sein: {channel!r}")
    return f"{target_os(rid)}-{channel}"


def vpk_cmd(version: str, rid: str, publish_dir: Path, out: Path, host: str, channel: str | None = None) -> list[str]:
    """vpk pack; [linux] nur beim Cross-Packen von einem anderen OS (sonst lehnt vpk die Direktive nicht ab, braucht sie aber nicht).

    channel: stable/beta/test (None = stable). Stabil setzt kein --channel, damit installierte 1.0.0 (Kanal win/linux)
    weiter Updates finden.
    """
    target = target_os(rid)
    cmd = ["dotnet", "vpk"]
    if target != host:
        cmd.append(f"[{target}]")
    cmd += ["pack", "--packId", PACK_ID, "--packVersion", version, "--packDir", str(publish_dir),
            "--outputDir", str(out), "--runtime", rid, "--packTitle", PACK_ID, "--packAuthors", AUTHORS]
    if (name := channel_name(channel, rid)) is not None:
        cmd += ["--channel", name]
    if target == "win":
        # Eigene portable ZIP statt der von Velopack (die enthält Update.exe und aktualisiert sich selbst).
        cmd += ["--mainExe", f"{PACK_ID}.exe", "--icon", str(APP / "Assets" / "whisperloom.ico"), "--noPortable"]
    else:
        cmd += ["--mainExe", PACK_ID, "--icon", str(APP / "Assets" / "whisperloom.png"), "--categories", "Utility"]
    return cmd


def portable_name(version: str, rid: str) -> str:
    return f"{PACK_ID}-{version}-{rid}-portable.zip"


def zip_dir(source: Path, target: Path) -> int:
    """Packt den Inhalt von source (ohne den Ordner selbst) nach target; liefert die Anzahl Dateien."""
    target.parent.mkdir(parents=True, exist_ok=True)
    count = 0
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as z:
        for path in sorted(source.rglob("*")):
            if path.is_file() and path.suffix != ".pdb":
                z.write(path, path.relative_to(source).as_posix())
                count += 1
    return count


def run(cmd: list[str]) -> None:
    print("+ " + " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True, cwd=ROOT)


def published_exe(publish_dir: Path, rid: str) -> Path:
    return publish_dir / (f"{PACK_ID}.exe" if target_os(rid) == "win" else PACK_ID)


def start_check(publish_dir: Path, rid: str, version: str, host: str) -> bool:
    """Starttest der veröffentlichten Binärdatei vor dem Packen (W3-PERF R1): `<exe> --version`, Exit 0, stdout genau
    `WhisperLoom <version>`.

    Prüft genau die Dateien, die danach in Setup, AppImage und portable ZIP landen (ReadyToRun, self-contained).
    Cross-Pack (Ziel-OS ≠ Host) lässt sich nicht starten: dann übersprungen, Rückgabe False. Fehlschlag → RuntimeError.
    """
    if target_os(rid) != host:
        print(f"start-check {rid}: übersprungen (Cross-Pack von {host})", flush=True)
        return False
    exe = published_exe(publish_dir, rid)
    print(f"+ {exe} --version", flush=True)
    try:
        result = subprocess.run([str(exe), "--version"], capture_output=True, text=True, timeout=120, cwd=ROOT)
    except (OSError, subprocess.TimeoutExpired) as e:
        raise RuntimeError(f"start-check {rid}: {type(e).__name__}") from e
    if result.returncode != 0 or result.stdout.strip() != f"{PACK_ID} {version}":
        raise RuntimeError(f"start-check {rid}: exit={result.returncode} stdout={result.stdout.strip()!r}")
    print(f"start-check {rid}: {result.stdout.strip()}", flush=True)
    return True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--version", required=True)
    parser.add_argument("--rid", required=True, choices=RIDS)
    parser.add_argument("--out", required=True, type=Path, help="Releases-Ordner (Velopack-Feed)")
    parser.add_argument("--work", type=Path, help="Arbeitsordner für publish (Standard: <out>/../publish)")
    parser.add_argument("--channel", choices=CHANNELS, default="stable", help="Update-Kanal (Standard stable = ohne --channel)")
    args = parser.parse_args(argv)
    version = check_version(args.version)
    channel_name(args.channel, args.rid)
    out = args.out.resolve()
    publish_dir = ((args.work or out.parent / "publish").resolve()) / f"{args.rid}-{version}"

    run(publish_cmd(version, args.rid, publish_dir))
    try:
        start_check(publish_dir, args.rid, version, host_os())
    except RuntimeError as e:
        print(f"PACK FEHLER {e}", file=sys.stderr, flush=True)
        return 1
    run(["dotnet", "tool", "restore"])
    run(vpk_cmd(version, args.rid, publish_dir, out, host_os(), args.channel))
    if args.rid.startswith("win"):
        target = out / portable_name(version, args.rid)
        print(f"portable {target.name} files={zip_dir(publish_dir, target)}")
    print(f"PACK OK {args.rid} {version} channel={channel_name(args.channel, args.rid) or target_os(args.rid)} -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
