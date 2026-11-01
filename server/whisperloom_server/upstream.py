"""Adapter je Anbieter-Protokoll für den Proxy (W2-SRV2): OpenAI-kompatibel, ElevenLabs, Anthropic Messages, Gemini,
Ollama. Nach außen spricht der Proxy immer OpenAI (Antworten hier normalisiert), nach innen das Protokoll des Anbieters.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/simple_whisper/anbieter.py (feste Endpunkte, Header
je Anbieter, Antwort prüfen, nichts vom Inhalt loggen). Endpunkte und Felder wie die App (src/WhisperLoom.Core/Api:
providers.json, TranscriptionRequest.cs, ElevenLabsStt.cs, OllamaApi.cs).
Quellen: Anthropic per Skill claude-api (POST /v1/messages, x-api-key, anthropic-version 2026-10-21: temperature/top_p/
top_k sind dort 400, usage.input_tokens/output_tokens, stop_reason; abgerufen 2026-11-01).
Doku: https://ai.google.dev/api/generate-content (generateContent, x-goog-api-key, usageMetadata; abgerufen 2026-11-01)
Doku: https://github.com/ollama/ollama/blob/main/docs/api.md (/api/chat, prompt_eval_count, eval_count; abgerufen 2026-11-01)
Doku: https://elevenlabs.io/docs/api-reference/speech-to-text/convert (model_id, language_code; abgerufen 2026-11-01)
"""

from dataclasses import dataclass, field
from urllib.parse import quote

from .ssrf import url_bauen

BASIS = {
    "openai": "https://api.openai.com/v1",
    "groq": "https://api.groq.com/openai/v1",
    "mistral": "https://api.mistral.ai/v1",
    "elevenlabs": "https://api.elevenlabs.io/v1",
    "together": "https://api.together.ai/v1",
    "deepinfra": "https://api.deepinfra.com/v1/openai",
    "openrouter": "https://openrouter.ai/api/v1",
    "anthropic": "https://api.anthropic.com/v1",
    "gemini": "https://generativelanguage.googleapis.com/v1beta",
    "deepseek": "https://api.deepseek.com",
}
# DeepInfra legt Audio nicht unter der Chat-Basis ab (providers.json sttPathOverride).
STT_URL = {"deepinfra": "https://api.deepinfra.com/v1/audio/transcriptions"}
NUR_TEXT = {"anthropic", "gemini", "deepseek", "ollama"}
NUR_ERKENNUNG = {"elevenlabs"}
STT_OHNE_PROMPT = {"mistral", "openrouter"}
STT_OHNE_FORMAT = {"mistral"}
EIGENE_SERVER = {"custom", "ollama"}
ANTHROPIC_VERSION = "2026-10-21"
MAX_TOKENS = 16384  # wie ChatPayload.MaxCompletionTokens der App
# Felder, die die App an /chat/completions schickt (ChatPayload.cs); unbekannte lehnt der Proxy ab (Vertrag §1).
CHAT_FELDER = {"model", "messages", "temperature", "top_p", "max_tokens", "max_completion_tokens", "reasoning_effort",
               "thinking", "stream"}
ROLLEN = {"system", "user", "assistant"}


class AdapterFehler(Exception):
    """Antwort des Anbieters unbrauchbar (kein JSON, Felder fehlen, Ablehnung)."""


@dataclass
class Anfrage:
    url: str
    headers: dict[str, str]
    json: dict | None = None
    daten: dict[str, str | list[str]] | None = None  # Multipart-Textfelder
    datei: tuple[str, bytes, str] | None = None


@dataclass
class Ergebnis:
    antwort: dict
    tokens_ein: int = 0
    tokens_aus: int = 0
    extra: dict = field(default_factory=dict)


def protokoll(zugang: dict) -> str:
    a = zugang["anbieter"]
    if a == "gemini" and zugang["basis_url"].rstrip("/").endswith("/openai"):
        return "openai"  # Basis-URL der OpenAI-Kompatibilitätsschicht wie in der App eingetragen
    return {"elevenlabs": "elevenlabs", "anthropic": "anthropic", "gemini": "gemini", "ollama": "ollama"}.get(a, "openai")


def kann_erkennung(zugang: dict) -> bool:
    return zugang["anbieter"] not in NUR_TEXT


def kann_text(zugang: dict) -> bool:
    return zugang["anbieter"] not in NUR_ERKENNUNG


def basis(zugang: dict) -> str:
    """Basis ohne Query und Fragment (W2-SFIX: über URL-Bausteine, ein #/? kann keinen Anbieter-Pfad abschneiden)."""
    roh = zugang["basis_url"] or BASIS.get(zugang["anbieter"], "")
    return url_bauen(roh, "") if roh else ""


def lese_zeit_s(zugang: dict) -> float:
    """Wie die App: eigene Server bis 600 s (defaultReadTimeoutSec), sonst 90 s (Http.DefaultReadTimeoutMs)."""
    return 600.0 if zugang["anbieter"] in EIGENE_SERVER else 90.0


def _bearer(key: str) -> dict[str, str]:
    return {"Authorization": f"Bearer {key}"} if key else {}


def _ollama_wurzel(url: str) -> str:
    """Wie OllamaApi.Root der App: Endungen /api und /v1 abschneiden."""
    while True:
        kurz = url.removesuffix("/api").removesuffix("/v1").rstrip("/")
        if kurz == url:
            return url
        url = kurz


# --- Erkennung ---


def erkennung_anfrage(zugang: dict, felder: dict[str, str], wav: bytes) -> Anfrage:
    """felder: geprüfte OpenAI-Felder des Geräts (model, language, prompt, response_format, …); Modell schon ersetzt."""
    if protokoll(zugang) == "elevenlabs":
        daten: dict[str, str | list[str]] = {"model_id": felder.get("model", ""), "tag_audio_events": "false",
                                             "timestamps_granularity": "none"}
        sprache = felder.get("language", "")
        if sprache and sprache != "auto":
            daten["language_code"] = sprache
        return Anfrage(url_bauen(basis(zugang), "/speech-to-text"), {"xi-api-key": zugang["key"]} if zugang["key"] else {},
                       daten=daten, datei=("audio.wav", wav, "audio/wav"))
    a = zugang["anbieter"]
    daten = {k: v for k, v in felder.items() if v}
    if a in STT_OHNE_PROMPT:
        daten.pop("prompt", None)
    if a in STT_OHNE_FORMAT:
        daten.pop("response_format", None)
    url = STT_URL[a] if a in STT_URL and not zugang["basis_url"] else url_bauen(basis(zugang), "/audio/transcriptions")
    return Anfrage(url, _bearer(zugang["key"]), daten=daten, datei=("audio.wav", wav, "audio/wav"))


def erkennung_antwort(zugang: dict, daten) -> Ergebnis:
    if not isinstance(daten, dict) or not isinstance(daten.get("text"), str):
        raise AdapterFehler("text")
    antwort: dict = {"text": daten["text"]}
    sprache = daten.get("language_code") if protokoll(zugang) == "elevenlabs" else daten.get("language")
    if isinstance(sprache, str) and 0 < len(sprache) <= 16:
        antwort["language"] = sprache
    usage = daten.get("usage") if isinstance(daten.get("usage"), dict) else {}
    ein = _zahl(usage.get("input_tokens", usage.get("prompt_tokens")))
    aus = _zahl(usage.get("output_tokens", usage.get("completion_tokens")))
    return Ergebnis(antwort, ein, aus)


# --- Text (chat/completions) ---


def _system_und_rest(nachrichten: list[dict]) -> tuple[str, list[dict]]:
    system = "\n\n".join(n["content"] for n in nachrichten if n["role"] == "system")
    return system, [n for n in nachrichten if n["role"] != "system"]


def chat_anfrage(zugang: dict, body: dict) -> Anfrage:
    """body: geprüfte OpenAI-Anfrage des Geräts (CHAT_FELDER, Modell schon ersetzt)."""
    p, b = protokoll(zugang), basis(zugang)
    grenze = body.get("max_completion_tokens") or body.get("max_tokens") or MAX_TOKENS
    if p == "anthropic":
        system, rest = _system_und_rest(body["messages"])
        js: dict = {"model": body["model"], "max_tokens": grenze, "messages": rest}
        if system:
            js["system"] = system
        # thinking des Geräts passt zu dessen Modellwahl, nicht zwingend zum Modell dieses Zugangs (Opus 5.5 lehnt
        # "disabled" ab) – der Proxy lässt es weg, das Modell nutzt seinen Standard.
        return Anfrage(url_bauen(b, "/messages"), {"x-api-key": zugang["key"], "anthropic-version": ANTHROPIC_VERSION}, json=js)
    if p == "gemini":
        system, rest = _system_und_rest(body["messages"])
        js = {"contents": [{"role": "model" if n["role"] == "assistant" else "user", "parts": [{"text": n["content"]}]}
                           for n in rest],
              "generationConfig": {"maxOutputTokens": grenze}}
        if system:
            js["systemInstruction"] = {"parts": [{"text": system}]}
        if "temperature" in body:
            js["generationConfig"]["temperature"] = body["temperature"]
        # Modellname kann vom Gerät stammen: als ein Pfadsegment kodieren (kein ../, ?, # im Pfad des Anbieters).
        return Anfrage(url_bauen(b, f"/models/{quote(body['model'], safe='')}:generateContent"),
                       {"x-goog-api-key": zugang["key"]} if zugang["key"] else {}, json=js)
    if p == "ollama":
        js = {"model": body["model"], "stream": False, "messages": body["messages"]}
        if "temperature" in body:
            js["options"] = {"temperature": body["temperature"]}
        return Anfrage(url_bauen(_ollama_wurzel(b), "/api/chat"), _bearer(zugang["key"]), json=js)
    js = {k: v for k, v in body.items() if k != "stream"}
    return Anfrage(url_bauen(b, "/chat/completions"), _bearer(zugang["key"]), json=js)


def _chat(modell: str, text: str, ende: str, ein: int, aus: int) -> Ergebnis:
    antwort = {
        "id": "chatcmpl-proxy", "object": "chat.completion", "model": modell,
        "choices": [{"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": ende}],
        "usage": {"prompt_tokens": ein, "completion_tokens": aus, "total_tokens": ein + aus},
    }
    return Ergebnis(antwort, ein, aus)


def _zahl(wert) -> int:
    return wert if isinstance(wert, int) and not isinstance(wert, bool) and wert >= 0 else 0


def chat_antwort(zugang: dict, modell: str, daten) -> Ergebnis:
    """Antwort des Anbieters → OpenAI-chat.completion (nur Text, Ende-Grund, Token-Zähler)."""
    if not isinstance(daten, dict):
        raise AdapterFehler("json")
    p = protokoll(zugang)
    try:
        if p == "anthropic":
            grund = daten.get("stop_reason")
            if grund == "refusal":
                raise AdapterFehler("refusal")
            text = "".join(b.get("text", "") for b in daten["content"] if b.get("type") == "text")
            u = daten.get("usage") or {}
            ende = "length" if grund == "max_tokens" else "stop"
            return _chat(modell, text, ende, _zahl(u.get("input_tokens")), _zahl(u.get("output_tokens")))
        if p == "gemini":
            kandidat = daten["candidates"][0]
            text = "".join(t.get("text", "") for t in kandidat.get("content", {}).get("parts", []) if not t.get("thought"))
            u = daten.get("usageMetadata") or {}
            ende = "length" if kandidat.get("finishReason") == "MAX_TOKENS" else "stop"
            return _chat(modell, text, ende, _zahl(u.get("promptTokenCount")), _zahl(u.get("candidatesTokenCount")))
        if p == "ollama":
            text = daten["message"]["content"]
            ende = "length" if daten.get("done_reason") == "length" else "stop"
            return _chat(modell, text, ende, _zahl(daten.get("prompt_eval_count")), _zahl(daten.get("eval_count")))
        wahl = daten["choices"][0]
        u = daten.get("usage") or {}
        return _chat(modell, wahl["message"].get("content") or "", wahl.get("finish_reason") or "stop",
                     _zahl(u.get("prompt_tokens")), _zahl(u.get("completion_tokens")))
    except (KeyError, IndexError, TypeError, AttributeError) as e:
        raise AdapterFehler("felder") from e


def abgelehnte_felder(body: dict, fehlertext: str) -> set[str]:
    """400 eines Anbieters, der ein Feld nicht kennt („Unsupported parameter: 'temperature'“): diese Felder einmal
    weglassen, wie TextRefiner.cs es für temperature tut. Der Fehlertext wird nur hier gelesen, nie geloggt."""
    text = fehlertext.lower()
    return {f for f in ("temperature", "reasoning_effort", "thinking", "max_completion_tokens", "top_p")
            if f in body and f in text}
