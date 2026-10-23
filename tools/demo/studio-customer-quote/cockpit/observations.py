"""Thread-safe, process-local observations for the local demo API cockpit.

The HTTP handler can call :meth:`ObservationStore.record_request` when a real API
response is ready, passing the already measured duration. Credential reveal is a
separate, explicit operation; unknown Authorization values are never retained.
"""

from __future__ import annotations

import hashlib
import json
import math
import threading
import uuid
from collections import deque
from collections.abc import Mapping
from copy import deepcopy
from datetime import datetime, timezone
from typing import Any
from urllib.parse import urlsplit


class RequestNotFound(LookupError):
    """The requested observation is missing or has aged out of the bounded store."""


class CredentialNotRevealable(PermissionError):
    """The request did not match a recognized, disposable demo credential."""


class ObservationStore:
    """Keep at most 100 actual API observations in memory, newest first."""

    DEFAULT_LIMIT = 100
    _REQUEST_HEADERS = {"accept": "Accept", "content-type": "Content-Type", "user-agent": "User-Agent", "x-demo-request": "X-Demo-Request"}
    _RESPONSE_HEADERS = {"content-type": "Content-Type"}
    _MAX_HEADER_VALUE_LENGTH = 2048

    def __init__(self, known_credentials: Mapping[str, str], *, max_entries: int = DEFAULT_LIMIT):
        if not 1 <= max_entries <= self.DEFAULT_LIMIT:
            raise ValueError(f"max_entries must be between 1 and {self.DEFAULT_LIMIT}")
        by_version: dict[str, str] = {}
        for credential, version in known_credentials.items():
            if not isinstance(credential, str) or not credential or version not in {"v1", "v2"}:
                raise ValueError("known credentials must be non-empty values mapped to v1 or v2")
            if version in by_version:
                raise ValueError(f"only one known credential may be configured for {version}")
            by_version[version] = credential

        self._credentials_by_version = by_version
        self._requests: deque[dict[str, Any]] = deque(maxlen=max_entries)
        self._quote: dict[str, Any] | None = None
        self._lock = threading.RLock()

    def record_request(
        self,
        *,
        method: str,
        path: str,
        status: int,
        duration_ms: float,
        request_headers: Mapping[str, Any],
        response_headers: Mapping[str, Any] | None = None,
        request_body: Any = None,
        response_body: Any = None,
        timestamp: datetime | None = None,
    ) -> dict[str, Any]:
        """Record one completed request; call only for real API traffic, never polling."""
        duration = _duration(duration_ms)
        auth = self._authentication(request_headers)
        request = {
            "id": uuid.uuid4().hex,
            "timestamp": _timestamp(timestamp),
            "method": str(method).upper(),
            "path": _path_only(path),
            "durationMs": duration,
            "status": int(status),
            "headers": self._safe_headers(request_headers, self._REQUEST_HEADERS, authorization=True),
            "body": _json_copy(request_body),
            "authentication": auth,
            "response": {
                "headers": self._safe_headers(response_headers or {}, self._RESPONSE_HEADERS),
                "body": _json_copy(response_body),
            },
        }
        with self._lock:
            self._requests.appendleft(request)
        return deepcopy(request)

    def record_quote(
        self,
        *,
        status: int,
        duration_ms: float,
        request: Any,
        body: Any,
        response_valid: bool = True,
        timestamp: datetime | None = None,
    ) -> dict[str, Any]:
        """Retain the observed workflow quote result separately from API requests."""
        quote = {
            "timestamp": _timestamp(timestamp),
            "status": int(status),
            "durationMs": _duration(duration_ms),
            "request": _json_copy(request),
            "body": _json_copy(body),
            "responseValid": response_valid,
        }
        with self._lock:
            self._quote = quote
        return deepcopy(quote)

    def snapshot(self) -> dict[str, Any]:
        """Return a detached state object suitable for the cockpit JSON endpoint."""
        with self._lock:
            return {"requests": deepcopy(list(self._requests)), "quote": deepcopy(self._quote)}

    def get_request(self, request_id: str) -> dict[str, Any] | None:
        """Return one detached request observation, if it remains in the bounded history."""
        with self._lock:
            request = next((item for item in self._requests if item["id"] == request_id), None)
            return deepcopy(request) if request is not None else None

    def reveal_authorization(self, request_id: str) -> str:
        """Return a known demo Bearer value for an explicitly selected matched request.

        Raises :class:`RequestNotFound` when the request is absent and
        :class:`CredentialNotRevealable` for unknown credentials or unmatched auth.
        """
        with self._lock:
            request = next((item for item in self._requests if item["id"] == request_id), None)
            if request is None:
                raise RequestNotFound(request_id)
            auth = request["authentication"]
            version = auth["version"]
            if not auth["canReveal"] or version not in self._credentials_by_version:
                raise CredentialNotRevealable(request_id)
            return f"Bearer {self._credentials_by_version[version]}"

    def _authentication(self, headers: Mapping[str, Any]) -> dict[str, Any]:
        authorization = _header_value(headers, "authorization")
        if not authorization:
            return {"accepted": False, "version": None, "fingerprint": None, "canReveal": False}

        scheme, credential = _authorization_parts(authorization)
        fingerprint = hashlib.sha256(credential.encode("utf-8")).hexdigest()[:8] if credential else None
        version = next((v for v, known in self._credentials_by_version.items() if credential == known), None)
        accepted = scheme.casefold() == "bearer" and version is not None
        return {
            "accepted": accepted,
            "version": version if accepted else None,
            "fingerprint": fingerprint,
            "canReveal": accepted and version in {"v1", "v2"},
        }

    def _safe_headers(
        self,
        headers: Mapping[str, Any],
        allowlist: Mapping[str, str],
        *,
        authorization: bool = False,
    ) -> dict[str, str]:
        result: dict[str, str] = {}
        for name, value in headers.items():
            key = str(name).casefold()
            if authorization and key == "authorization":
                raw = str(value).strip()
                parts = raw.split(None, 1)
                # Only echo the known, non-secret Bearer scheme. A malformed single-word
                # Authorization value may itself be a credential, so never treat it as a scheme.
                result["Authorization"] = (
                    "Bearer ••••••••"
                    if parts and parts[0].casefold() == "bearer" and len(parts) == 2 and parts[1].strip()
                    else "Bearer" if parts and parts[0].casefold() == "bearer"
                    else "••••••••"
                )
            elif key in allowlist:
                safe = str(value)
                for credential in self._credentials_by_version.values():
                    safe = safe.replace(credential, "[redacted]")
                result[allowlist[key]] = safe[: self._MAX_HEADER_VALUE_LENGTH]
        return result


def _header_value(headers: Mapping[str, Any], wanted: str) -> str:
    for name, value in headers.items():
        if str(name).casefold() == wanted:
            return str(value).strip()
    return ""


def _authorization_parts(value: str) -> tuple[str, str]:
    scheme, separator, credential = value.partition(" ")
    if separator:
        return scheme, credential.strip()
    if scheme.casefold() == "bearer":
        return scheme, ""
    # Match the synthetic API's exact scheme separator. Unknown or malformed
    # values are fingerprinted only and must never appear as accepted traffic.
    return "", value


def _timestamp(value: datetime | None) -> str:
    current = value or datetime.now(timezone.utc)
    if current.tzinfo is None:
        current = current.replace(tzinfo=timezone.utc)
    return current.astimezone(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def _duration(value: float) -> float:
    duration = float(value)
    if not math.isfinite(duration) or duration < 0:
        raise ValueError("duration_ms must be finite and non-negative")
    return duration


def _path_only(value: str) -> str:
    parsed = urlsplit(str(value))
    return parsed.path or "/"


def _json_copy(value: Any) -> Any:
    if value is None:
        return None
    # A JSON round-trip both detaches the captured result from handler-owned objects and
    # keeps browser-provided strings as inert data; the client must render them with textContent.
    return json.loads(json.dumps(value, ensure_ascii=False, allow_nan=False))
