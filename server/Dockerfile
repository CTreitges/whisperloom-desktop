# WhisperLoom Firmen-Manager. Nicht-root, Daten unter /data (Volume), Healthcheck /healthz.
# Doku: https://docs.docker.com/reference/dockerfile/ (HEALTHCHECK, USER; abgerufen 2026-11-01)
FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    WL_DATA_DIR=/data

RUN groupadd --system --gid 10001 whisperloom \
 && useradd --system --uid 10001 --gid whisperloom --home-dir /data --shell /usr/sbin/nologin whisperloom \
 && mkdir -p /data && chown whisperloom:whisperloom /data && chmod 700 /data

WORKDIR /app
COPY requirements.txt .
RUN pip install -r requirements.txt
COPY whisperloom_server ./whisperloom_server

USER whisperloom
VOLUME ["/data"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD ["python", "-c", "import sys, urllib.request; sys.exit(0 if urllib.request.urlopen('http://127.0.0.1:8080/healthz', timeout=4).status == 200 else 1)"]
CMD ["python", "-m", "whisperloom_server", "serve", "--init", "--host", "0.0.0.0", "--port", "8080"]
