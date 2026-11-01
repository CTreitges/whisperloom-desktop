"""Gleitendes Fenster im Arbeitsspeicher. Ein Prozess, ein Neustart setzt zurück.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/ratelimit.py
"""

import threading
from collections import defaultdict, deque
from collections.abc import Callable


class Begrenzer:
    def __init__(self, uhr: Callable[[], int]):
        self._uhr = uhr
        self._treffer: dict[str, deque[int]] = defaultdict(deque)
        self._lock = threading.Lock()

    def erlaubt(self, schluessel: str, grenze: int, fenster_ms: int) -> int | None:
        """Zählt einen Treffer. None = erlaubt, sonst Sekunden bis zum nächsten freien Platz (Retry-After)."""
        jetzt = self._uhr()
        with self._lock:
            q = self._treffer[schluessel]
            while q and q[0] <= jetzt - fenster_ms:
                q.popleft()
            if len(q) >= grenze:
                return max(1, (q[0] + fenster_ms - jetzt + 999) // 1000)
            q.append(jetzt)
            if len(self._treffer) > 100_000:  # Schutz gegen unbegrenztes Wachstum
                self._aufraeumen(jetzt)
            return None

    def _aufraeumen(self, jetzt: int) -> None:
        for k in [k for k, q in self._treffer.items() if not q or q[-1] < jetzt - 86_400_000]:
            del self._treffer[k]


STUNDE = 3_600_000
MINUTE = 60_000
VIERTELSTUNDE = 15 * MINUTE
