# WhisperLoom Firmen-Manager. Nicht-root, Daten unter /data (Volume), Healthcheck /healthz.
# Doku: https://docs.docker.com/reference/dockerfile/ (HEALTHCHECK, USER; abgerufen 2026-11-01)
# Basis per Digest (Review N-16; ermittelt 2026-11-01 per `docker buildx imagetools inspect python:3.12-slim` und
# Registry-API, Index-Digest für alle Plattformen). Aktualisieren: Digest neu ermitteln, Image bauen, Tests.
FROM python:3.12-slim@sha256:aa943ce411423c7582288f18707ff66eddcbaeb510502d747a72aeb34f30d159

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    WL_DATA_DIR=/data

RUN groupadd --system --gid 10001 whisperloom \
 && useradd --system --uid 10001 --gid whisperloom --home-dir /data --shell /usr/sbin/nologin whisperloom \
 && mkdir -p /data && chown whisperloom:whisperloom /data && chmod 700 /data

WORKDIR /app
COPY requirements-hashes.txt .
RUN pip install --require-hashes --no-deps -r requirements-hashes.txt
COPY whisperloom_server ./whisperloom_server

USER whisperloom
VOLUME ["/data"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["python", "-c", "import sys, urllib.request; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8080/healthz', timeout=4).status == 200 else 1)"]
CMD ["python", "-m", "whisperloom_server", "serve", "--init", "--host", "0.0.0.0", "--port", "8080"]
