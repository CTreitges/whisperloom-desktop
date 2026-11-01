#!/usr/bin/env bash
# Nur CI (server-docker, W2-SRV2): Proxy-Aufruf gegen den Fake-Anbieter im Compose-Netz (ci/compose-proxy.yml).
# Firma + Zugänge per CLI (Key über stdin), Gerät per API enrollen und per CLI freigeben, dann Erkennung, Text und
# Modellliste über den Proxy. Läuft im Ordner server/. Letzte Zeile: PROXY CHECK OK.
set -euo pipefail
basis=http://127.0.0.1:8080
dc() { docker compose -f docker-compose.yml -f ci/compose-proxy.yml "$@"; }
cli() { dc exec -T whisperloom-server python -m whisperloom_server "$@"; }

code=$(cli tenant-create proxy "Proxy CI GmbH" --show | sed -n 's/^Firmencode: //p')
test -n "$code"
echo "test-key-not-real-1" | cli key-set proxy stt --provider custom --base-url http://fake-upstream:9000/v1 --model ci-whisper
echo "test-key-not-real-1" | cli key-set proxy llm --provider custom --base-url http://fake-upstream:9000/v1 --model ci-llm
# Interne Ziele außerhalb der Freigabe lehnt der Server ab (SSRF):
if echo "test-key-not-real-1" | cli key-set proxy stt_fallback --provider custom --base-url http://169.254.169.254/v1; then
  echo "SSRF-Schutz fehlt"; exit 1
fi

geraet=$(curl -sf -H 'Content-Type: application/json' \
  -d "{\"code\": \"$code\", \"deviceName\": \"CI-Proxy\", \"platform\": \"linux\", \"appVersion\": \"1.0.0\"}" \
  "$basis/api/v1/enroll")
id=$(echo "$geraet" | python3 -c 'import json,sys; print(json.load(sys.stdin)["deviceId"])')
token=$(echo "$geraet" | python3 -c 'import json,sys; print(json.load(sys.stdin)["deviceToken"])')
cli device-approve "$id"

python3 -c 'import sys,wave; w=wave.open(sys.argv[1],"wb"); w.setnchannels(1); w.setsampwidth(2); w.setframerate(16000); w.writeframes(b"\0\0"*16000); w.close()' /tmp/ci.wav
stt=$(curl -sf -H "Authorization: Bearer $token" -F model=egal -F language=de -F file=@/tmp/ci.wav \
  "$basis/api/v1/proxy/audio/transcriptions")
echo "$stt" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["text"] == "Hallo von ci-fake", d; print("erkennung ok")'

text=$(curl -sf -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -d '{"model": "egal", "messages": [{"role": "user", "content": "hallo"}], "temperature": 0}' \
  "$basis/api/v1/proxy/chat/completions")
echo "$text" | python3 -c 'import json,sys; d=json.load(sys.stdin); assert d["choices"][0]["message"]["content"] == "Hallo von ci-fake" and d["usage"]["total_tokens"] == 18, d; print("text ok")'

curl -sf -H "Authorization: Bearer $token" "$basis/api/v1/proxy/models" \
  | python3 -c 'import json,sys; d=json.load(sys.stdin); assert {m["id"] for m in d["data"]} == {"ci-whisper", "ci-llm"}, d; print("modelle ok")'
if dc logs whisperloom-server 2>&1 | grep -q -e "test-key-not-real-1" -e "$token"; then
  echo "Key oder Token im Log"; exit 1
fi
echo "PROXY CHECK OK"
