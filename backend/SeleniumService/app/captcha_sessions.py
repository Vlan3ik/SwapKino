import threading
import uuid
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone


@dataclass
class CaptchaSession:
    session_id: str
    driver: object
    source_url: str
    include_unrated: bool
    expires_at: datetime


class CaptchaSessionStore:
    """In-memory, single-process store for short-lived manual browser sessions."""

    def __init__(self, ttl_seconds: int = 300):
        self.ttl = timedelta(seconds=ttl_seconds)
        self._sessions: dict[str, CaptchaSession] = {}
        self._lock = threading.Lock()

    def _cleanup_locked(self) -> None:
        now = datetime.now(timezone.utc)
        expired = [key for key, session in self._sessions.items() if session.expires_at <= now]
        for key in expired:
            session = self._sessions.pop(key)
            try:
                session.driver.quit()
            except Exception:
                pass

    def add(self, driver: object, source_url: str, include_unrated: bool) -> CaptchaSession:
        with self._lock:
            self._cleanup_locked()
            session = CaptchaSession(
                session_id=uuid.uuid4().hex,
                driver=driver,
                source_url=source_url,
                include_unrated=include_unrated,
                expires_at=datetime.now(timezone.utc) + self.ttl,
            )
            self._sessions[session.session_id] = session
            return session

    def get(self, session_id: str) -> CaptchaSession | None:
        with self._lock:
            self._cleanup_locked()
            return self._sessions.get(session_id)

    def remove(self, session_id: str) -> None:
        with self._lock:
            session = self._sessions.pop(session_id, None)
        if session:
            try:
                session.driver.quit()
            except Exception:
                pass
