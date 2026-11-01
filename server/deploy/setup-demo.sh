#!/bin/sh
# Einrichtung der Demo-Instanz des Firmen-Managers als systemd-User-Dienst (W2-DEMO). Auf dem Server ausführen:
#   sh ~/whisperloom-admin/app/server/deploy/setup-demo.sh
# Voraussetzung: Repo nach ~/whisperloom-admin/app geklont. Wiederholbar: Vorhandenes (venv, Schlüssel, env, Admins,
# „Demo GmbH“) bleibt. Erzeugte Passwörter stehen nur in ~/.config/whisperloom-admin/admin-initial.txt (600), nie auf
# stdout. Startet die Dienste nicht neu, wenn sie schon laufen (dafür: systemctl --user restart whisperloom-admin).
set -eu
umask 077
BASE="$HOME/whisperloom-admin"
CONF="$HOME/.config/whisperloom-admin"
APP="$BASE/app"
DEPLOY="$APP/server/deploy"
WL="$DEPLOY/wl-admin"
UNITS="$HOME/.config/systemd/user"

mkdir -p "$BASE/data" "$CONF" "$UNITS"
chmod 700 "$BASE" "$BASE/data" "$CONF"

[ -x "$BASE/venv/bin/python" ] || python3 -m venv "$BASE/venv"
"$BASE/venv/bin/python" -m pip install -q --disable-pip-version-check -r "$APP/server/requirements.txt" -e "$APP/server"

[ -s "$CONF/master.key" ] || python3 -c 'import os; print(os.urandom(32).hex())' > "$CONF/master.key"
[ -s "$CONF/env" ] || sed "s|@HOME@|$HOME|g" "$DEPLOY/env.example" > "$CONF/env"
chmod 600 "$CONF/master.key" "$CONF/env"
chmod 700 "$WL"

"$WL" init >/dev/null

if ! "$WL" tenant-list | awk -F'\t' '$1 == "demo" { f = 1 } END { exit !f }'; then
  "$WL" tenant-create demo "Demo GmbH" >/dev/null
  # Fake-Anbieter (whisperloom-admin-fake.service) für Erkennung und Text; Christof ersetzt das im Portal durch seinen Key.
  echo "test-key-not-real-2" | "$WL" key-set demo stt --provider custom --base-url "http://$(sed -n 's/^WL_DEMO_FAKE=//p' "$CONF/env")/v1" --model fake-stt
  echo "test-key-not-real-2" | "$WL" key-set demo llm --provider custom --base-url "http://$(sed -n 's/^WL_DEMO_FAKE=//p' "$CONF/env")/v1" --model fake-llm
fi

if [ ! -s "$CONF/admin-initial.txt" ]; then
  PW_INSTANZ=$(python3 -c 'import secrets; print(secrets.token_urlsafe(18))')
  PW_DEMO=$(python3 -c 'import secrets; print(secrets.token_urlsafe(18))')
  echo "$PW_INSTANZ" | "$WL" instance-admin-create admin --password-stdin >/dev/null
  echo "$PW_DEMO" | "$WL" admin-create demo demo --password-stdin >/dev/null
  {
    echo "Portal: https://whisperloom-admin.ctreitges.de/login (zweiter Faktor TOTP wird beim ersten Login eingerichtet)"
    echo "Instanz-Admin: admin / $PW_INSTANZ"
    echo "Demo GmbH (owner): demo / $PW_DEMO"
  } > "$CONF/admin-initial.txt"
  chmod 600 "$CONF/admin-initial.txt"
fi

install -m 644 "$DEPLOY/whisperloom-admin.service" "$DEPLOY/whisperloom-admin-fake.service" "$UNITS/"
systemctl --user daemon-reload
systemctl --user enable --now whisperloom-admin-fake whisperloom-admin
systemctl --user is-active whisperloom-admin whisperloom-admin-fake
