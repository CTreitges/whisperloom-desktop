"""Lokale Fake-Anbieter für Tests und Smokes — nie echte Anbieter.

- OpenAI-kompatibel: POST /v1/audio/transcriptions (multipart), POST /v1/chat/completions, GET /v1/models
- Anthropic-kompatibel: POST /anthropic/v1/messages, GET /anthropic/v1/models
- Proxy-Fake (zweiter Port): leitet Anfragen in absoluter Form (http://…) weiter und markiert sie
- Steuerung: POST /_control {"fail": {"<pfad>": {"status": 500, "count": 1}}, "stt_text": "…"}
  GET /_requests → aufgezeichnete Anfragen (nur Metadaten: Pfad, Felder language/model, Prompt-Anfang,
  ob ein Key kam, ob über den Proxy) · POST /_reset

Aufruf: py -3 -X utf8 tools/fake_api.py [--port 0] [--proxy-port 0]
Erste Ausgabezeile: FAKE-API port=<n> proxy=<m> (Port 0 = frei gewählt).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import threading
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DEFAULT_STT_TEXT = "This is a fake transcript."


class State:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.requests: list[dict] = []
        self.fail: dict[str, dict] = {}
        self.stt_text = DEFAULT_STT_TEXT

    def take_failure(self, path: str) -> int | None:
        with self.lock:
            rule = self.fail.get(path)
            if not rule or rule.get("count", 1) <= 0:
                return None
            rule["count"] = rule.get("count", 1) - 1
            return int(rule.get("status", 500))

    def record(self, entry: dict) -> None:
        with self.lock:
            self.requests.append(entry)


def multipart_fields(body: bytes, content_type: str) -> dict[str, str]:
    """Nur Textfelder (language, model, prompt …); Dateiinhalte werden verworfen."""
    match = re.search(r"boundary=\"?([^\";]+)\"?", content_type)
    if not match:
        return {}
    fields: dict[str, str] = {}
    for part in body.split(b"--" + match.group(1).encode()):
        head, _, value = part.partition(b"\r\n\r\n")
        name = re.search(rb'name="([^"]+)"', head)
        if not name or b"filename=" in head:
            continue
        fields[name.group(1).decode()] = value.rstrip(b"\r\n").decode("utf-8", "replace")
    return fields


def make_handler(state: State):
    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *args) -> None:  # leise
            pass

        def _send(self, status: int, payload: dict) -> None:
            data = json.dumps(payload).encode()
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        def _body(self) -> bytes:
            length = int(self.headers.get("Content-Length") or 0)
            return self.rfile.read(length) if length else b""

        def _entry(self, path: str) -> dict:
            return {
                "method": self.command,
                "path": path,
                "has_key": bool(self.headers.get("Authorization") or self.headers.get("x-api-key") or self.headers.get("xi-api-key")),
                "via_proxy": self.headers.get("X-Fake-Proxy") == "1",
            }

        def do_GET(self) -> None:
            path = self.path.split("?")[0]
            if path == "/_requests":
                with state.lock:
                    self._send(200, {"requests": list(state.requests)})
                return
            entry = self._entry(path)
            state.record(entry)
            if path in ("/v1/models", "/anthropic/v1/models"):
                if not entry["has_key"]:
                    self._send(401, {"error": "missing key"})
                else:
                    self._send(200, {"data": [{"id": "fake-model"}]})
                return
            self._send(404, {"error": "not found"})

        def do_POST(self) -> None:
            path = self.path.split("?")[0]
            body = self._body()
            if path == "/_control":
                ctl = json.loads(body or b"{}")
                with state.lock:
                    state.fail.update(ctl.get("fail", {}))
                    state.stt_text = ctl.get("stt_text", state.stt_text)
                self._send(200, {"ok": True})
                return
            if path == "/_reset":
                with state.lock:
                    state.requests.clear()
                    state.fail.clear()
                    state.stt_text = DEFAULT_STT_TEXT
                self._send(200, {"ok": True})
                return

            entry = self._entry(path)
            failure = state.take_failure(path)
            if path.endswith("/audio/transcriptions"):
                fields = multipart_fields(body, self.headers.get("Content-Type", ""))
                entry.update({"language": fields.get("language"), "model": fields.get("model")})
                state.record(entry)
                if failure:
                    self._send(failure, {"error": {"message": "fake failure"}})
                else:
                    self._send(200, {"text": state.stt_text})
                return
            if path.endswith("/chat/completions") or path == "/anthropic/v1/messages":
                payload = json.loads(body or b"{}")
                messages = payload.get("messages", [])
                system = payload.get("system") or next((m.get("content", "") for m in messages if m.get("role") == "system"), "")
                user = next((m.get("content", "") for m in messages if m.get("role") == "user"), "")
                if isinstance(user, list):
                    user = " ".join(p.get("text", "") for p in user if isinstance(p, dict))
                entry.update({"model": payload.get("model"), "system_start": str(system)[:80]})
                state.record(entry)
                if failure:
                    self._send(failure, {"error": {"message": "fake failure"}})
                    return
                text = f"[fake-llm] {user}"
                if path == "/anthropic/v1/messages":
                    self._send(200, {"content": [{"type": "text", "text": text}], "stop_reason": "end_turn"})
                else:
                    self._send(200, {"choices": [{"message": {"role": "assistant", "content": text}, "finish_reason": "stop"}]})
                return
            state.record(entry)
            self._send(404, {"error": "not found"})

    return Handler


def make_proxy_handler(state: State):
    class Proxy(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *args) -> None:
            pass

        def _forward(self) -> None:
            if not self.path.startswith("http://"):
                self.send_error(400, "absolute http URL expected")
                return
            length = int(self.headers.get("Content-Length") or 0)
            body = self.rfile.read(length) if length else None
            headers = {k: v for k, v in self.headers.items() if k.lower() not in ("proxy-connection", "connection", "host")}
            headers["X-Fake-Proxy"] = "1"
            state.record({"method": self.command, "path": "proxy:" + self.path.split("?")[0], "has_key": False, "via_proxy": True})
            request = urllib.request.Request(self.path, data=body, headers=headers, method=self.command)
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
            try:
                with opener.open(request, timeout=30) as response:
                    status, data, ctype = response.status, response.read(), response.headers.get("Content-Type", "")
            except urllib.error.HTTPError as e:
                status, data, ctype = e.code, e.read(), e.headers.get("Content-Type", "")
            self.send_response(status)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)

        do_GET = _forward
        do_POST = _forward

    return Proxy


def serve(port: int = 0, proxy_port: int = 0) -> tuple[ThreadingHTTPServer, ThreadingHTTPServer, State]:
    """Startet API und Proxy in Hintergrund-Threads (für Tests)."""
    state = State()
    api = ThreadingHTTPServer(("127.0.0.1", port), make_handler(state))
    proxy = ThreadingHTTPServer(("127.0.0.1", proxy_port), make_proxy_handler(state))
    for server in (api, proxy):
        threading.Thread(target=server.serve_forever, daemon=True).start()
    return api, proxy, state


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Lokale Fake-Anbieter (STT, Chat, Anthropic, Proxy)")
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--proxy-port", type=int, default=0)
    args = parser.parse_args(argv)
    api, proxy, _ = serve(args.port, args.proxy_port)
    print(f"FAKE-API port={api.server_address[1]} proxy={proxy.server_address[1]}", flush=True)
    try:
        threading.Event().wait()
    except KeyboardInterrupt:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
