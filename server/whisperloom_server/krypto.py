"""Verschlüsselung, HMAC und Zufallswerte.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/krypto.py
Context7: cryptography (pyca) — AESGCM(key).encrypt(nonce, data, aad), 12-Byte-Nonce nie wiederverwenden;
HKDF(algorithm=SHA256(), length=32, salt, info).derive(ikm).
"""

import hashlib
import hmac
import os
import secrets

from cryptography.exceptions import InvalidTag
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

# Crockford-Base32 ohne I, L, O, U
ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
CODE_LAENGE = 16
VERSION = b"\x01"
GERAETE_PRAEFIX = "wld_"


def _ableiten(master: bytes, info: bytes) -> bytes:
    return HKDF(algorithm=hashes.SHA256(), length=32, salt=None, info=info).derive(master)


class Entschluesselungsfehler(Exception):
    pass


class Krypto:
    def __init__(self, master: bytes):
        self._aes = AESGCM(_ableiten(master, b"whisperloom-server aes v1"))
        self._hmac = _ableiten(master, b"whisperloom-server hmac v1")

    def verschluesseln(self, klartext: str, aad: str) -> tuple[bytes, bytes]:
        nonce = os.urandom(12)
        chiffre = VERSION + self._aes.encrypt(nonce, klartext.encode("utf-8"), aad.encode("utf-8"))
        return chiffre, nonce

    def entschluesseln(self, chiffre: bytes, nonce: bytes, aad: str) -> str:
        if not chiffre.startswith(VERSION):
            raise Entschluesselungsfehler("unbekannte Version")
        try:
            return self._aes.decrypt(nonce, chiffre[1:], aad.encode("utf-8")).decode("utf-8")
        except InvalidTag as e:
            raise Entschluesselungsfehler("Prüfsumme falsch") from e

    def hmac(self, wert: str) -> str:
        return hmac.new(self._hmac, wert.encode("utf-8"), hashlib.sha256).hexdigest()


def sha256(wert: str) -> str:
    return hashlib.sha256(wert.encode("utf-8")).hexdigest()


def token(nbytes: int = 32) -> str:
    """URL-sicheres Zufallstoken (Standard 256 bit)."""
    return secrets.token_urlsafe(nbytes)


def geraete_token() -> str:
    return GERAETE_PRAEFIX + secrets.token_urlsafe(32)


def kurz_id(praefix: str, laenge: int = 12) -> str:
    return praefix + "".join(secrets.choice(ALPHABET) for _ in range(laenge)).lower()


def neuer_code() -> str:
    """Firmencode, 16 Zeichen Crockford-Base32 = 80 bit (normalisierte Form, ohne Bindestriche)."""
    return "".join(secrets.choice(ALPHABET) for _ in range(CODE_LAENGE))


def code_normalisieren(eingabe: str) -> str | None:
    """Bindestriche, Leerzeichen und Kleinschreibung egal; O→0, I/L→1. Ungültig → None."""
    s = "".join(eingabe.split()).replace("-", "").upper()
    s = s.replace("O", "0").replace("I", "1").replace("L", "1")
    if len(s) != CODE_LAENGE or any(c not in ALPHABET for c in s):
        return None
    return s


def code_anzeigen(code: str) -> str:
    return "-".join(code[i : i + 4] for i in range(0, CODE_LAENGE, 4))


def gleich(a: str, b: str) -> bool:
    return hmac.compare_digest(a.encode("utf-8"), b.encode("utf-8"))
