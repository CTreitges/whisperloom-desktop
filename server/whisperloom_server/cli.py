"""Kommandozeile `python -m whisperloom_server` (Instanz-Verwaltung und Start).

Basis: whisperloom-android feat/simple-portal server/simple-whisper/admin.py
Geheimnisse (Firmencodes, Einladungs-Links) gehen in eine Datei mit Rechten 600 unter <daten>/ausgabe/; nur mit --show
auf stdout (für Skripte, z. B. CI). Nie in Logs.
"""

import argparse
import os
import sys
import time

from . import __version__, auth, db, firmen, konten, krypto, schluessel, ssrf
from .config import Einstellungen, KonfigFehler, lade_master
from .krypto import Krypto


class CliFehler(Exception):
    pass


def _ausgeben(einst: Einstellungen, art: str, inhalt: str, zeigen: bool) -> str:
    if zeigen:
        return inhalt.rstrip("\n")
    einst.ausgabe.mkdir(mode=0o700, parents=True, exist_ok=True)
    pfad = einst.ausgabe / f"{art}-{time.strftime('%Y%m%d-%H%M%S')}-{krypto.token(4)}.txt"
    fd = os.open(pfad, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as f:
        f.write(inhalt)
    return f"geschrieben: {pfad}"


def init(einst: Einstellungen) -> str:
    einst.daten.mkdir(mode=0o700, parents=True, exist_ok=True)
    if os.name == "posix":
        os.chmod(einst.daten, 0o700)
    pfad = einst.master_pfad()
    if not einst.master_hex and not pfad.exists():
        fd = os.open(pfad, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, "w", encoding="ascii") as f:
            f.write(os.urandom(32).hex())
    db.einrichten(einst.db_pfad)
    return f"bereit: {einst.daten}"


def _firma(con, slug: str):
    row = con.execute("SELECT * FROM firma WHERE slug = ?", (slug,)).fetchone()
    if row is None:
        raise CliFehler(f"unbekannte Firma {slug}")
    return row


def _einladung_text(con, einst: Einstellungen, aid: str, benutzer: str) -> str:
    link, ablauf = konten.einladung(con, einst.basis_url, aid, einst.uhr())
    bis = time.strftime("%Y-%m-%d %H:%M", time.localtime(ablauf / 1000))
    return f"Admin: {benutzer}\nEinladung (gültig bis {bis}, einmal verwendbar): {link}\n"


def ausfuehren(a: argparse.Namespace, einst: Einstellungen) -> str:
    if a.befehl == "init":
        return init(einst)
    kry = Krypto(lade_master(einst))
    db.einrichten(einst.db_pfad)
    con = db.verbinden(einst.db_pfad)
    jetzt = einst.uhr()
    try:
        if a.befehl == "instance-admin-create":
            aid = konten.anlegen(con, None, a.benutzer, "instance", jetzt)
            return _ausgeben(einst, "instanz-admin", _einladung_text(con, einst, aid, a.benutzer.lower()), a.show)
        if a.befehl == "tenant-create":
            fid, code = firmen.anlegen(con, kry, a.slug, a.name, jetzt)
            con.execute("UPDATE firma SET auto_freigabe = ?, key_modus = ? WHERE id = ?", (int(a.auto_approve), a.key_mode, fid))
            firmen.protokoll(con, None, "cli", "firma_neu", fid, jetzt)
            text = f"Firma: {a.name}\nFirmencode: {krypto.code_anzeigen(code)}\n"
            if a.owner:
                aid = konten.anlegen(con, fid, a.owner, "owner", jetzt)
                text += _einladung_text(con, einst, aid, a.owner.lower())
            return _ausgeben(einst, f"firma-{a.slug}", text, a.show)
        if a.befehl == "tenant-list":
            rows = con.execute("SELECT f.slug, f.name, f.gesperrt, (SELECT COUNT(*) FROM geraet g WHERE g.firma_id = f.id) "
                               "AS n FROM firma f ORDER BY f.slug").fetchall()
            return "\n".join(f"{r['slug']}\t{'gesperrt' if r['gesperrt'] else 'aktiv'}\t{r['n']} Geräte\t{r['name']}"
                             for r in rows) or "(keine)"
        if a.befehl in ("code-rotate", "code-show"):
            f = _firma(con, a.slug)
            code = (krypto.code_anzeigen(firmen.code_setzen(con, kry, f["id"], None, jetzt)) if a.befehl == "code-rotate"
                    else firmen.code_lesen(con, kry, f["id"], None))
            if a.befehl == "code-rotate":
                firmen.protokoll(con, f["id"], "cli", "code_rotiert", f["id"], jetzt)
            return _ausgeben(einst, f"code-{a.slug}", f"Firma: {f['name']}\nFirmencode: {code}\n", a.show)
        if a.befehl == "admin-create":
            f = _firma(con, a.slug)
            aid = konten.anlegen(con, f["id"], a.benutzer, a.role, jetzt)
            firmen.protokoll(con, f["id"], "cli", "admin_neu", a.benutzer.lower(), jetzt)
            return _ausgeben(einst, "admin", _einladung_text(con, einst, aid, a.benutzer.lower()), a.show)
        if a.befehl in ("admin-invite", "admin-totp-reset"):
            row = con.execute("SELECT * FROM admin WHERE benutzer = ?", (a.benutzer.lower(),)).fetchone()
            if row is None:
                raise CliFehler("unbekannter Admin")
            if a.befehl == "admin-totp-reset":
                auth.totp_speichern(con, kry, row["id"], None)
                auth.sitzungen_beenden(con, row["id"])
                return "ok"
            return _ausgeben(einst, "admin", _einladung_text(con, einst, row["id"], row["benutzer"]), a.show)
        if a.befehl == "device-list":
            f = _firma(con, a.slug)
            rows = con.execute("SELECT id, status, plattform, name FROM geraet WHERE firma_id = ? ORDER BY angelegt_ms",
                               (f["id"],)).fetchall()
            return "\n".join(f"{r['id']}\t{r['status']}\t{r['plattform']}\t{r['name']}" for r in rows) or "(keine)"
        if a.befehl in ("device-approve", "device-block"):
            status = "approved" if a.befehl == "device-approve" else "blocked"
            if con.execute("UPDATE geraet SET status = ? WHERE id = ?", (status, a.geraet)).rowcount != 1:
                raise CliFehler("unbekanntes Gerät")
            return "ok"
        if a.befehl == "key-set":
            # Key von stdin, nie als Argument (Prozessliste, Shell-Verlauf). Leer = eigener Server ohne Key.
            f = _firma(con, a.slug)
            if a.base_url and not ssrf.beim_speichern_ok(a.base_url, einst.upstream_erlaubt, einst.aufloeser):
                raise CliFehler("Ziel zeigt auf ein internes Netz; mit WL_UPSTREAM_ALLOW freigeben")
            key = sys.stdin.readline().strip()
            schluessel.speichern(con, kry, f["id"], a.zugang, a.provider, a.base_url, key, jetzt, modell=a.model)
            firmen.protokoll(con, f["id"], "cli", "key_gespeichert", a.zugang, jetzt)
            return "ok"
    except schluessel.SchluesselFehler as e:
        raise CliFehler(f"Key nicht gespeichert ({str(e).removeprefix('h.')})") from e
    except (firmen.FirmenFehler, konten.KontoFehler) as e:
        raise CliFehler(str(e)) from e
    finally:
        con.close()
    raise CliFehler("unbekannter Befehl")


def _parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="python -m whisperloom_server",
        description="WhisperLoom Firmen-Manager. Einstellungen über Umgebungsvariablen WL_* (siehe server/README.md).",
    )
    p.add_argument("--version", action="version", version=__version__)
    sub = p.add_subparsers(dest="befehl", required=True, metavar="BEFEHL")
    s = sub.add_parser("serve", help="Dienst starten")
    s.add_argument("--host", default="127.0.0.1")
    s.add_argument("--port", type=int, default=8080)
    s.add_argument("--init", action="store_true", help="vorher init ausführen (Container-Start)")
    sub.add_parser("init", help="Datenordner, Hauptschlüssel und Datenbank anlegen")
    s = sub.add_parser("instance-admin-create", help="Instanz-Admin anlegen (Einladungs-Link)")
    s.add_argument("benutzer")
    s = sub.add_parser("tenant-create", help="Firma anlegen (Firmencode, optional erster owner)")
    s.add_argument("slug")
    s.add_argument("name")
    s.add_argument("--owner")
    s.add_argument("--auto-approve", action="store_true")
    s.add_argument("--key-mode", choices=("proxy", "distribute"), default="proxy")
    sub.add_parser("tenant-list", help="Firmen auflisten")
    for name, hilfe in (("code-rotate", "Firmencode erneuern (alter Code sofort ungültig)"), ("code-show", "Firmencode zeigen")):
        sub.add_parser(name, help=hilfe).add_argument("slug")
    s = sub.add_parser("admin-create", help="Admin einer Firma anlegen")
    s.add_argument("slug")
    s.add_argument("benutzer")
    s.add_argument("--role", choices=konten.ROLLEN, default="owner")
    sub.add_parser("admin-invite", help="Neuer Einladungs-Link (Passwort vergessen)").add_argument("benutzer")
    sub.add_parser("admin-totp-reset", help="TOTP eines Admins zurücksetzen").add_argument("benutzer")
    sub.add_parser("device-list", help="Geräte einer Firma").add_argument("slug")
    sub.add_parser("device-approve", help="Gerät freigeben").add_argument("geraet")
    sub.add_parser("device-block", help="Gerät sperren").add_argument("geraet")
    s = sub.add_parser("key-set", help="Zugang einer Firma speichern (Key von stdin)")
    s.add_argument("slug")
    s.add_argument("zugang", help="stt, stt_fallback, llm, llm_fallback oder eine Anbieter-ID")
    s.add_argument("--provider", default="")
    s.add_argument("--base-url", default="")
    s.add_argument("--model", default="")
    for name in ("instance-admin-create", "tenant-create", "code-rotate", "code-show", "admin-create", "admin-invite"):
        sub.choices[name].add_argument("--show", action="store_true", help="Geheimnisse auf stdout statt in eine Datei")
    return p


def main(argv: list[str] | None = None, einst: Einstellungen | None = None) -> int:
    # Umlaute auch bei umgeleiteter Ausgabe unter Windows (cp1252) lesbar.
    for strom in (sys.stdout, sys.stderr):
        if hasattr(strom, "reconfigure"):
            strom.reconfigure(encoding="utf-8")
    a = _parser().parse_args(argv)
    try:
        einst = einst or Einstellungen.aus_umgebung()
        if a.befehl == "serve":
            import uvicorn

            from .app import create_app

            if a.init:
                init(einst)
            # Ohne proxy_headers: Client-IP und trusted_header wertet netz.py anhand WL_TRUSTED_PROXIES aus.
            uvicorn.run(create_app(einst), host=a.host, port=a.port, proxy_headers=False, access_log=False,
                        server_header=False)
            return 0
        print(ausfuehren(a, einst))
    except (CliFehler, KonfigFehler) as e:
        print(f"fehler: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

