"""Anbieter-Parität Kotlin → C#: vergleicht ``ProviderCatalog.kt`` (Android) mit ``providers.json`` (Desktop).

Aufruf:
    py -3 -X utf8 tools/check_provider_parity.py --android <android-repo>
        [--json src/WhisperLoom.Core/Api/providers.json] [--exceptions docs/parity-exceptions.md]

Verglichen werden je Anbieter alle Felder von ``Provider`` (IDs, Name, Protokoll, Basis-URL, Pfad-Override, Flags,
Zeitlimit, Upload-Limit, Notizen, Empfehlung zum Umformulieren) und je Modell alle Felder von ``ModelOption`` (ID,
Label, Notiz, Flags), dazu die Reihenfolge von Anbietern und Modellen (= Standardmodelle) und ``CATALOG_DATE``.
Fehlende Felder gelten mit den Kotlin-Standardwerten.

Unterschiede heißen ``catalogDate``, ``providers.order``, ``<id>`` (Anbieter fehlt/zusätzlich), ``<id>.<feld>``,
``<id>.<sttModels|llmModels>.order`` oder ``<id>.<sttModels|llmModels>[<modell>].<feld>``. Ausnahmen stehen in
``docs/parity-exceptions.md`` im Abschnitt „## Anbieter“ als Tabelle ``| Unterschied | Begründung |``; jede Ausnahme
braucht eine Begründung und muss einen echten Unterschied treffen.

Letzte Zeile: ``PROVIDER-PARITY OK providers=<n> exceptions=<k>`` (Exit 0) oder ``PROVIDER-PARITY FAIL …`` (Exit 1).
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

KOTLIN_PATH = Path("app/src/main/java/com/chris/whisperloom/api/ProviderCatalog.kt")
REPO = Path(__file__).resolve().parent.parent

PROVIDER_DEFAULTS: dict[str, Any] = {
    "needsKey": True, "keyUrl": "", "allowsHttp": False, "defaultReadTimeoutSec": 90, "sttModels": [], "llmModels": [],
    "sttSendsPrompt": True, "sttSendsResponseFormat": True, "sttPathOverride": None, "sttMaxBytes": None,
    "api": "OPENAI", "notes": "", "rewriteLlmModel": "",
}
PROVIDER_FIELDS = ["id", "name", "baseUrl", *PROVIDER_DEFAULTS]
MODEL_PARAMS = ["id", "label", "note", "temperatureSupported", "reasoningEffort", "languageField", "thinkingType"]
MODEL_DEFAULTS: dict[str, Any] = {
    "note": "", "temperatureSupported": True, "reasoningEffort": None, "languageField": "language", "thinkingType": None,
}

TOKEN = re.compile(
    r'(?P<ws>\s+)|(?P<comment>//[^\n]*|/\*[\s\S]*?\*/)|(?P<string>"(?:\\.|[^"\\])*")'
    r"|(?P<number>\d[\d_]*)|(?P<name>[A-Za-z_][\w.]*)|(?P<op>[(),=+])"
)


@dataclass
class Call:
    name: str
    args: list[tuple[str | None, Any]]


class Parser:
    """Mini-Parser für die Ausdrücke in ProviderCatalog.kt: Aufrufe, benannte Argumente, Strings mit ``+``."""

    def __init__(self, text: str, constants: dict[str, Any]):
        self.tokens = [(m.lastgroup, m.group()) for m in TOKEN.finditer(text) if m.lastgroup not in ("ws", "comment")]
        self.pos = 0
        self.constants = constants

    def peek(self, offset: int = 0) -> tuple[str, str] | None:
        i = self.pos + offset
        return self.tokens[i] if i < len(self.tokens) else None

    def take(self, value: str | None = None) -> tuple[str, str]:
        token = self.tokens[self.pos]
        if value is not None and token[1] != value:
            raise ValueError(f"erwartet '{value}', gefunden '{token[1]}'")
        self.pos += 1
        return token

    def expression(self) -> Any:
        value = self.term()
        while self.peek() and self.peek()[1] == "+":
            self.take("+")
            value = value + self.term()
        return value

    def term(self) -> Any:
        kind, text = self.take()
        if kind == "string":
            return kotlin_string(text)
        if kind == "number":
            return int(text.replace("_", ""))
        if kind == "name":
            if self.peek() and self.peek()[1] == "(":
                return self.call(text)
            if text in ("true", "false"):
                return text == "true"
            if text == "null":
                return None
            if text.startswith("ApiStyle."):
                return text.split(".", 1)[1]
            if text in self.constants:
                return self.constants[text]
            raise ValueError(f"unbekannter Name {text}")
        raise ValueError(f"unerwartetes Token {text}")

    def call(self, name: str) -> Any:
        self.take("(")
        args: list[tuple[str | None, Any]] = []
        while self.peek()[1] != ")":
            label = None
            if self.peek()[0] == "name" and self.peek(1) and self.peek(1)[1] == "=":
                label = self.take()[1]
                self.take("=")
            args.append((label, self.expression()))
            if self.peek()[1] == ",":
                self.take(",")
        self.take(")")
        if name in ("listOf", "emptyList"):
            return [value for _, value in args]
        return Call(name, args)


def kotlin_string(literal: str) -> str:
    body = literal[1:-1]
    return re.sub(r'\\(.)', lambda m: {"n": "\n", "t": "\t", "\\": "\\", '"': '"', "$": "$"}.get(m.group(1), m.group(1)), body)


def parse_kotlin(path: Path) -> tuple[str, list[dict[str, Any]]]:
    text = path.read_text(encoding="utf-8")
    constants: dict[str, Any] = {}
    for m in re.finditer(r"const val (\w+)\s*=\s*([^\n]+)", text):
        constants[m.group(1)] = Parser(m.group(2), {}).expression()
    start = text.index("val providers: List<Provider> = listOf(")
    parser = Parser(text[start + len("val providers: List<Provider> = "):], constants)
    calls = parser.expression()
    return constants["CATALOG_DATE"], [provider_dict(c) for c in calls]


def provider_dict(call: Call) -> dict[str, Any]:
    if call.name != "Provider" or any(label is None for label, _ in call.args):
        raise ValueError("Provider(...) nur mit benannten Argumenten erwartet")
    values = {label: value for label, value in call.args}
    result = {field: values.get(field, PROVIDER_DEFAULTS.get(field)) for field in PROVIDER_FIELDS}
    for kind in ("sttModels", "llmModels"):
        result[kind] = [model_dict(m) for m in values.get(kind, [])]
    return result


def model_dict(call: Call) -> dict[str, Any]:
    values: dict[str, Any] = {}
    for i, (label, value) in enumerate(call.args):
        values[label or MODEL_PARAMS[i]] = value
    return {field: values.get(field, MODEL_DEFAULTS.get(field)) for field in MODEL_PARAMS}


def normalize_json(data: dict[str, Any]) -> tuple[str, list[dict[str, Any]]]:
    providers = []
    for p in data["providers"]:
        result = {field: p.get(field, PROVIDER_DEFAULTS.get(field)) for field in PROVIDER_FIELDS}
        for kind in ("sttModels", "llmModels"):
            result[kind] = [{f: m.get(f, MODEL_DEFAULTS.get(f)) for f in MODEL_PARAMS} for m in p.get(kind, [])]
        providers.append(result)
    return data["catalogDate"], providers


def differences(kotlin: tuple[str, list[dict]], desktop: tuple[str, list[dict]]) -> list[str]:
    diffs: list[str] = []
    if kotlin[0] != desktop[0]:
        diffs.append("catalogDate")
    k_by_id = {p["id"]: p for p in kotlin[1]}
    d_by_id = {p["id"]: p for p in desktop[1]}
    diffs += sorted(set(k_by_id) ^ set(d_by_id))
    common = [i for i in k_by_id if i in d_by_id]
    if common != [i for i in d_by_id if i in k_by_id]:
        diffs.append("providers.order")
    for pid in common:
        k, d = k_by_id[pid], d_by_id[pid]
        for field in PROVIDER_FIELDS:
            if field in ("sttModels", "llmModels"):
                diffs += model_differences(pid, field, k[field], d[field])
            elif k[field] != d[field]:
                diffs.append(f"{pid}.{field}")
    return diffs


def model_differences(pid: str, kind: str, kotlin: list[dict], desktop: list[dict]) -> list[str]:
    diffs = []
    k_by_id = {m["id"]: m for m in kotlin}
    d_by_id = {m["id"]: m for m in desktop}
    diffs += [f"{pid}.{kind}[{mid}]" for mid in sorted(set(k_by_id) ^ set(d_by_id))]
    if [m for m in k_by_id if m in d_by_id] != [m for m in d_by_id if m in k_by_id]:
        diffs.append(f"{pid}.{kind}.order")
    for mid in (m for m in k_by_id if m in d_by_id):
        diffs += [f"{pid}.{kind}[{mid}].{f}" for f in MODEL_PARAMS if k_by_id[mid][f] != d_by_id[mid][f]]
    return diffs


def read_exceptions(path: Path) -> tuple[dict[str, str], list[str]]:
    """Tabelle im Abschnitt „## Anbieter“: Unterschied → Begründung."""
    if not path.is_file():
        return {}, []
    exceptions: dict[str, str] = {}
    errors: list[str] = []
    in_section = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            in_section = line.strip() == "## Anbieter"
            continue
        if not in_section or not line.strip().startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if cells[0].lower() == "unterschied" or set("".join(cells)) <= set("-: "):
            continue
        key, reason = cells[0].strip("`"), (cells[1] if len(cells) > 1 else "")
        if not reason:
            errors.append(f"Ausnahme {key}: Begründung fehlt")
        exceptions[key] = reason
    return exceptions, errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Anbieter-Parität Kotlin → C#")
    parser.add_argument("--android", required=True, type=Path, help="Wurzel des Android-Repos")
    parser.add_argument("--json", type=Path, default=REPO / "src/WhisperLoom.Core/Api/providers.json")
    parser.add_argument("--exceptions", type=Path, default=REPO / "docs/parity-exceptions.md")
    args = parser.parse_args(argv)

    kotlin_file = args.android / KOTLIN_PATH
    if not kotlin_file.is_file():
        print(f"PROVIDER-PARITY FAIL: {kotlin_file} fehlt")
        return 1
    try:
        kotlin = parse_kotlin(kotlin_file)
    except (ValueError, IndexError, KeyError) as e:
        print(f"PROVIDER-PARITY FAIL: ProviderCatalog.kt nicht lesbar: {e}")
        return 1
    desktop = normalize_json(json.loads(args.json.read_text(encoding="utf-8")))
    exceptions, errors = read_exceptions(args.exceptions)
    diffs = differences(kotlin, desktop)
    for diff in diffs:
        if diff not in exceptions:
            errors.append(f"Unterschied ohne Ausnahme: {diff}")
    for key in exceptions:
        if key not in diffs:
            errors.append(f"Ausnahme ohne Unterschied: {key}")
    print(f"Kotlin: {len(kotlin[1])} Anbieter, {sum(len(p['sttModels']) + len(p['llmModels']) for p in kotlin[1])} Modelle")
    for error in errors:
        print(f"FEHLER: {error}")
    if errors:
        print(f"PROVIDER-PARITY FAIL errors={len(errors)}")
        return 1
    print(f"PROVIDER-PARITY OK providers={len(desktop[1])} exceptions={len(exceptions)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
