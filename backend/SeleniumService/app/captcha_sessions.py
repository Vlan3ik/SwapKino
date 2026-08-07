import threading
import uuid
import secrets
from pathlib import Path
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone


@dataclass
class CaptchaSession:
    session_id: str
    driver: object
    source_url: str
    include_unrated: bool
    collected_items: list[object]
    page_number: int
    vnc_token: str
    expires_at: datetime

    @property
    def novnc_url(self) -> str:
        return f"/novnc/vnc.html?autoconnect=true&resize=scale&path=novnc/websockify%3Ftoken%3D{self.vnc_token}"


class CaptchaSessionStore:
    """In-memory, single-process store for short-lived manual browser sessions."""

    def __init__(self, ttl_seconds: int = 300, token_file: str = "/tmp/swapkino-novnc.tokens"):
        self.ttl = timedelta(seconds=ttl_seconds)
        self.token_file = Path(token_file)
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
        if expired:
            self._write_tokens_locked()

    def _write_tokens_locked(self) -> None:
        self.token_file.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.token_file.with_suffix(".tmp")
        temporary.write_text("".join(f"{session.vnc_token}: localhost:5900\n" for session in self._sessions.values()), encoding="utf-8")
        temporary.replace(self.token_file)

    def add(self, driver: object, source_url: str, include_unrated: bool, collected_items: list[object] | None = None, page_number: int = 1) -> CaptchaSession:
        with self._lock:
            self._cleanup_locked()
            session = CaptchaSession(
                session_id=uuid.uuid4().hex,
                driver=driver,
                source_url=source_url,
                include_unrated=include_unrated,
                collected_items=collected_items or [],
                page_number=page_number,
                vnc_token=secrets.token_urlsafe(32),
                expires_at=datetime.now(timezone.utc) + self.ttl,
            )
            self._sessions[session.session_id] = session
            self._write_tokens_locked()
            return session

    def get(self, session_id: str) -> CaptchaSession | None:
        with self._lock:
            self._cleanup_locked()
            return self._sessions.get(session_id)

    def cleanup(self) -> None:
        with self._lock:
            self._cleanup_locked()

    def close_all(self) -> None:
        with self._lock:
            sessions = list(self._sessions.values())
            self._sessions.clear()
            self._write_tokens_locked()
        for session in sessions:
            try:
                session.driver.quit()
            except Exception:
                pass

    def remove(self, session_id: str) -> None:
        with self._lock:
            session = self._sessions.pop(session_id, None)
        if session:
            try:
                session.driver.quit()
            except Exception:
                pass
            with self._lock:
                self._write_tokens_locked()
