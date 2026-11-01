"""Audio-Upload des Proxys (W2-SRV2): multipart/form-data im Arbeitsspeicher zerlegen und die Dauer bestimmen.

Kein request.form(): Starlette spoolt Dateien ab 1 MiB in eine Temp-Datei, Audio darf nie auf die Platte. Der Zerleger
arbeitet linear (bytes.split an der Grenze) und lehnt mehr Teile ab, als Felder erlaubt sind, bevor er etwas parst –
ein Body aus vielen kleinen Teilen bindet den Server nicht (Prüfer-Befund W2-SRV2 Nr. 1). Er läuft im Thread.
Doku: https://www.rfc-editor.org/rfc/rfc7578 (multipart/form-data; abgerufen 2026-11-01),
https://docs.python.org/3/library/wave.html (getframerate, getsampwidth, getnchannels; abgerufen 2026-11-01)
"""

import asyncio
import io
import re
import wave
from email import policy as email_policy
from email.parser import BytesParser

STT_FELDER = {"file", "model", "language", "languages[]", "prompt", "response_format", "temperature"}
_GRENZE = re.compile(r"boundary=\"?([0-9A-Za-z'()+_,./:=?-]{1,70})\"?", re.IGNORECASE)
_WAV_KOPF = 44


def _kopf_name(kopf: bytes) -> str | None:
    if len(kopf) > 1024:
        raise ValueError("kopf")
    teil = BytesParser(policy=email_policy.HTTP).parsebytes(kopf + b"\r\n\r\n", headersonly=True)
    return teil.get_param("name", header="content-disposition")


def multipart_lesen(typ: str, body: bytes) -> tuple[dict[str, str], bytes]:
    """→ (Textfelder, Datei). Nur STT_FELDER, jedes höchstens einmal, genau eine nicht leere Datei `file`.
    Sonst ValueError (bzw. UnicodeError bei Text, der kein UTF-8 ist)."""
    treffer = _GRENZE.search(typ) if typ.lower().startswith("multipart/form-data") else None
    if treffer is None:
        raise ValueError("typ")
    trenner = b"--" + treffer.group(1).encode("ascii")
    if body.count(trenner) > len(STT_FELDER) + 1:
        raise ValueError("teile")
    stuecke = body.split(trenner)
    if len(stuecke) < 3 or not stuecke[-1].startswith(b"--"):
        raise ValueError("rahmen")
    felder: dict[str, str] = {}
    datei: bytes | None = None
    for roh in stuecke[1:-1]:
        kopf, trennung, inhalt = roh.removeprefix(b"\r\n").partition(b"\r\n\r\n")
        if not trennung or not inhalt.endswith(b"\r\n"):
            raise ValueError("teil")
        inhalt = inhalt[:-2]
        name = _kopf_name(kopf)
        if name not in STT_FELDER or name in felder or (name == "file" and datei is not None):
            raise ValueError("feld")
        if name == "file":
            datei = inhalt
        elif len(inhalt) > 4000:
            raise ValueError("lang")
        else:
            felder[name] = inhalt.decode("utf-8")
    if not datei:
        raise ValueError("datei")
    return felder, datei


async def multipart_im_thread(typ: str, body: bytes) -> tuple[dict[str, str], bytes]:
    return await asyncio.to_thread(multipart_lesen, typ, body)


def audio_dauer_ms(wav: bytes) -> int | None:
    """Dauer eines WAV; None, wenn es kein lesbares WAV ist. Maßgeblich ist das Größere aus Kopfangabe und Nutzdaten:
    Ein Kopf mit data-Größe 0 drückt die Minuten nicht (Anbieter lesen bis zum Dateiende)."""
    try:
        with wave.open(io.BytesIO(wav)) as w:
            rate, bytes_je_s = w.getframerate(), w.getframerate() * w.getsampwidth() * w.getnchannels()
            kopf_ms = w.getnframes() * 1000 // rate
    except (wave.Error, EOFError, ZeroDivisionError):
        return None
    if bytes_je_s <= 0:
        return None
    return max(kopf_ms, max(len(wav) - _WAV_KOPF, 0) * 1000 // bytes_je_s)
