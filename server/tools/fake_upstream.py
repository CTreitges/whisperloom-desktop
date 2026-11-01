"""Fake-Anbieter für Tests, Smoke und Docker-CI des Proxys (W2-SRV2). Nur stdlib, lauscht lokal, keine echten Anbieter.

Spricht die Protokolle, die der Proxy nach innen nutzt: OpenAI-kompatibel (/audio/transcriptions, /chat/completions),
ElevenLabs (/speech-to-text), Anthropic (/messages), Gemini (/models/<m>:generateContent), Ollama (/api/chat).
Prüft den Key im jeweiligen Header; ein falscher Key gibt 401 und spiegelt ihn im Body (wie manche Anbieter), damit
Tests zeigen, dass der Proxy Anbieter-Antworten nie roh durchreicht.

Verhalten per `modus`: ok | fehler500 | langsam (wartet `warte_s`) | temperatur400 (lehnt temperature/reasoning_effort
ab) | kaputt (kein JSON) | weiterleitung (307 auf die Metadaten-Adresse).
Jede Anfrage landet in `anfragen` (Pfad, Header, Felder, JSON, Dateigröße).

Aufruf: python server/tools/fake_upstream.py --port 9000 --key test-key-not-real-1 [--name fake] [--host 0.0.0.0]
"""

import argparse
import json
import threading
import time
from email.parser import BytesParser
from email.policy import HTTP
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class FakeUpstream(ThreadingHTTPServer):
    daemon_threads = True

    def __init__(self, key: str = "", name: str = "fake", host: str = "127.0.0.1", port: int = 0):
        super().__init__((host, port), _Handler)
        self.key, self.name = key, name
        self.modus = "ok"
        self.warte_s = 2.0
        self.anfragen: list[dict] = []
        self._lock = threading.Lock()

    @property
    def basis(self) -> str:
        return f"http://127.0.0.1:{self.server_address[1]}"

    def starten(self) -> "FakeUpstream":
        threading.Thread(target=self.serve_forever, daemon=True).start()
        return self

    def stoppen(self) -> None:
        self.shutdown()
        self.server_close()


def _multipart(kopf: str, body: bytes) -> tuple[dict[str, list[str]], int]:
    nachricht = BytesParser(policy=HTTP).parsebytes(b"Content-Type: " + kopf.encode() + b"\r\n\r\n" + body)
    felder: dict[str, list[str]] = {}
    groesse = -1
    for teil in nachricht.iter_parts():
        name = teil.get_param("name", header="content-disposition")
        inhalt = teil.get_payload(decode=True) or b""
        if teil.get_filename():
            groesse = len(inhalt)
        else:
            felder.setdefault(name, []).append(inhalt.decode("utf-8"))
    return felder, groesse


class _Handler(BaseHTTPRequestHandler):
    server: FakeUpstream

    def log_message(self, *_args):  # kein Log: Inhalte sollen nirgends landen
        pass

    def _senden(self, status: int, daten) -> None:
        roh = daten if isinstance(daten, bytes) else json.dumps(daten).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(roh)))
        self.end_headers()
        self.wfile.write(roh)

    def do_GET(self):  # noqa: N802
        self._senden(200 if self.path == "/healthz" else 404, {"status": "ok"})

    def do_POST(self):  # noqa: N802
        s = self.server
        body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        eintrag: dict = {"pfad": self.path, "header": {k.lower(): v for k, v in self.headers.items()}}
        typ = self.headers.get("Content-Type", "")
        if typ.startswith("multipart/form-data"):
            eintrag["felder"], eintrag["datei"] = _multipart(typ, body)
        else:
            try:
                eintrag["json"] = json.loads(body or b"{}")
            except ValueError:
                eintrag["json"] = None
        with s._lock:
            s.anfragen.append(eintrag)
        h = eintrag["header"]
        geschickt = (h.get("authorization", "").removeprefix("Bearer ") or h.get("x-api-key") or h.get("xi-api-key")
                     or h.get("x-goog-api-key") or "")
        if s.key and geschickt != s.key:
            self._senden(401, {"error": {"message": f"Incorrect API key provided: {geschickt}"}})
            return
        if s.modus == "fehler500":
            self._senden(500, {"error": "kaputt"})
            return
        if s.modus == "langsam":
            time.sleep(s.warte_s)
        if s.modus == "kaputt":
            self._senden(200, b"<html>kein json</html>")
            return
        if s.modus == "tropfen":  # Antwort Byte für Byte, je Byte unter dem Lese-Zeitlimit (Gesamt-Deadline testen)
            roh = json.dumps({"text": "spät", "choices": []}).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(roh)))
            self.end_headers()
            for b in roh:
                self.wfile.write(bytes([b]))
                self.wfile.flush()
                time.sleep(0.2)
            return
        if s.modus == "weiterleitung":  # Versuch, den Proxy auf die Metadaten-Adresse umzulenken
            self.send_response(307)
            self.send_header("Location", "http://169.254.169.254/latest/meta-data/")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return
        js = eintrag.get("json") or {}
        if s.modus == "temperatur400" and ("temperature" in js or "reasoning_effort" in js):
            self._senden(400, {"error": {"message": "Unsupported parameter: 'temperature' / 'reasoning_effort'"}})
            return
        self._senden(200, self._antwort(eintrag, js))

    def _antwort(self, eintrag: dict, js: dict) -> dict:
        text = f"Hallo von {self.server.name}"
        p = self.path
        if p.endswith("/audio/transcriptions"):
            return {"text": text, "language": "de"}
        if p.endswith("/speech-to-text"):
            return {"text": text, "language_code": "deu"}
        if p.endswith("/chat/completions"):
            return {"id": "x", "object": "chat.completion", "model": js.get("model"), "choices": [
                {"index": 0, "message": {"role": "assistant", "content": text, "reasoning": "geheim"}, "finish_reason": "stop"}],
                "usage": {"prompt_tokens": 11, "completion_tokens": 7, "total_tokens": 18}}
        if p.endswith("/messages"):
            return {"type": "message", "role": "assistant", "content": [{"type": "text", "text": text}],
                    "stop_reason": "end_turn", "usage": {"input_tokens": 13, "output_tokens": 5}}
        if ":generateContent" in p:
            return {"candidates": [{"content": {"parts": [{"text": text}]}, "finishReason": "STOP"}],
                    "usageMetadata": {"promptTokenCount": 17, "candidatesTokenCount": 3}}
        if p.endswith("/api/chat"):
            return {"message": {"role": "assistant", "content": text}, "done": True, "done_reason": "stop",
                    "prompt_eval_count": 19, "eval_count": 2}
        return {"unbekannt": p}


def main() -> None:
    a = argparse.ArgumentParser(description="Fake-Anbieter für den WhisperLoom-Proxy (nur Tests)")
    a.add_argument("--host", default="127.0.0.1")
    a.add_argument("--port", type=int, default=9000)
    a.add_argument("--key", default="")
    a.add_argument("--name", default="fake")
    args = a.parse_args()
    s = FakeUpstream(args.key, args.name, args.host, args.port)
    print(f"FAKE UPSTREAM {args.host}:{s.server_address[1]}", flush=True)
    s.serve_forever()


if __name__ == "__main__":
    main()
