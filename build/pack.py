"""Baut die Verteil-Pakete von WhisperLoom: dotnet publish → vpk pack (Velopack) → portable ZIP (nur Windows).

Aufruf: py -3 -X utf8 build/pack.py --version <x.y.z> --rid <win-x64|linux-x64> --out <Releases-Ordner> [--work <ordner>]

Ergebnis in --out (Velopack-Feed, auch Quelle für `update --feed <ordner>`):
- win-x64:   WhisperLoom-win-Setup.exe, WhisperLoom-<v>-full.nupkg, releases.win.json, RELEASES, …
             und WhisperLoom-<v>-win-x64-portable.zip (ohne Velopack-Updater, für Firmen-Verteilung und eigene Signatur)
- linux-x64: WhisperLoom.AppImage, WhisperLoom-<v>-full.nupkg, releases.linux.json, …
Linux-Pakete lassen sich auch unter Windows bauen (vpk-Direktive [linux]). Signieren gehört nicht hierher: docs/signing.md.

Doku: docs.velopack.io/getting-started/csharp, docs.velopack.io/packaging/cross-compiling,
docs.velopack.io/packaging/operating-systems/linux (abgerufen 2026-11-01); `dotnet vpk pack -H` (vpk 1.2.161).
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


def vpk_cmd(version: str, rid: str, publish_dir: Path, out: Path, host: str) -> list[str]:
    """vpk pack; [linux] nur beim Cross-Packen von einem anderen OS (sonst lehnt vpk die Direktive nicht ab, braucht sie aber nicht)."""
    target = "win" if rid.startswith("win") else "linux"
    cmd = ["dotnet", "vpk"]
    if target != host:
        cmd.append(f"[{target}]")
    cmd += ["pack", "--packId", PACK_ID, "--packVersion", version, "--packDir", str(publish_dir),
            "--outputDir", str(out), "--runtime", rid, "--packTitle", PACK_ID, "--packAuthors", AUTHORS]
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


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--version", required=True)
    parser.add_argument("--rid", required=True, choices=RIDS)
    parser.add_argument("--out", required=True, type=Path, help="Releases-Ordner (Velopack-Feed)")
    parser.add_argument("--work", type=Path, help="Arbeitsordner für publish (Standard: <out>/../publish)")
    args = parser.parse_args(argv)
    version = check_version(args.version)
    out = args.out.resolve()
    publish_dir = ((args.work or out.parent / "publish").resolve()) / f"{args.rid}-{version}"

    run(publish_cmd(version, args.rid, publish_dir))
    run(["dotnet", "tool", "restore"])
    run(vpk_cmd(version, args.rid, publish_dir, out, host_os()))
    if args.rid.startswith("win"):
        target = out / portable_name(version, args.rid)
        print(f"portable {target.name} files={zip_dir(publish_dir, target)}")
    print(f"PACK OK {args.rid} {version} -> {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
