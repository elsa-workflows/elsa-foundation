#!/usr/bin/env python3
"""Loopback-only synthetic customer API for Elsa Studio demo v3.

Credentials are supplied as raw, synthetic values through environment variables:
DEMO_CUSTOMER_API_TOKEN_V1 and DEMO_CUSTOMER_API_TOKEN_V2. Values are never
printed, returned, or written to the call ledger.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Lock
from typing import Any


HOST = "127.0.0.1"
CUSTOMER_ID = "C-1042"
CUSTOMER = {
    "customerId": CUSTOMER_ID,
    "name": "Alex Morgan",
    "company": "Northstar Bikes",
}
_state_dir = os.environ.get("ELSA_DEMO_STATE_DIR")
STATE_DIR = (
    Path(_state_dir).expanduser().resolve()
    if _state_dir
    else Path(__file__).resolve().parent / "state"
)
CALLS_FILE = STATE_DIR / "demo-api-calls.jsonl"
CALLS_LOCK = Lock()


def json_bytes(payload: Any) -> bytes:
    return json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def credential_versions() -> dict[str, str]:
    v1 = os.environ.get("DEMO_CUSTOMER_API_TOKEN_V1", "")
    v2 = os.environ.get("DEMO_CUSTOMER_API_TOKEN_V2", "")
    if not v1 or not v2:
        raise SystemExit(
            "Set DEMO_CUSTOMER_API_TOKEN_V1 and DEMO_CUSTOMER_API_TOKEN_V2 "
            "to distinct raw synthetic values in the private process environment."
        )
    if v1 == v2:
        raise SystemExit("The two synthetic demo token values must be different.")
    if v1.lower().startswith("bearer ") or v2.lower().startswith("bearer "):
        raise SystemExit("Supply raw token values without a Bearer prefix.")
    return {v1: "v1", v2: "v2"}


def append_call(method: str, path: str, version: str | None, status: int) -> None:
    record = {
        "timestamp": datetime.now(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z"),
        "method": method,
        "path": path,
        "customerId": CUSTOMER_ID if path == f"/api/customers/{CUSTOMER_ID}" else None,
        "credentialVersion": version,
        "status": status,
    }
    STATE_DIR.mkdir(parents=True, exist_ok=True)
    with CALLS_LOCK, CALLS_FILE.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(record, separators=(",", ":")) + "\n")


def read_calls() -> list[dict[str, Any]]:
    if not CALLS_FILE.exists():
        return []
    calls: list[dict[str, Any]] = []
    with CALLS_LOCK, CALLS_FILE.open("r", encoding="utf-8") as stream:
        for line in stream:
            try:
                item = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(item, dict) and set(item) == {
                "timestamp", "method", "path", "customerId", "credentialVersion", "status"
            }:
                calls.append(item)
    return calls


class DemoApiHandler(BaseHTTPRequestHandler):
    server_version = "ElsaStudioDemoV3/1"
    sys_version = ""

    def _reply(self, status: int, payload: Any) -> None:
        body = json_bytes(payload)
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        if self.path == "/health":
            self._reply(200, {"status": "ok", "service": "customer-api-demo-v3"})
            return

        if self.path == "/status":
            self._reply(200, {"service": "customer-api-demo-v3", "calls": read_calls()})
            return

        if self.path != f"/api/customers/{CUSTOMER_ID}":
            self._reply(404, {"error": "not_found"})
            self._safe_log(404, None)
            return

        versions: dict[str, str] = self.server.credential_versions  # type: ignore[attr-defined]
        authorization = self.headers.get("Authorization", "")
        scheme, separator, token = authorization.partition(" ")
        version = versions.get(token.strip()) if separator and scheme.lower() == "bearer" else None
        if version is None:
            append_call("GET", self.path, None, 401)
            self._reply(401, {"error": "unauthorized"})
            self._safe_log(401, None)
            return

        append_call("GET", self.path, version, 200)
        self._reply(200, CUSTOMER)
        self._safe_log(200, version)

    def _safe_log(self, status: int, version: str | None) -> None:
        # Log only fixed route metadata and credential version, never headers or values.
        print(f"GET customer-lookup -> {status} credentialVersion={version or 'unknown'}", flush=True)

    def log_message(self, format: str, *args: object) -> None:
        # Suppress BaseHTTPRequestHandler's default access line.
        return


class DemoApiServer(ThreadingHTTPServer):
    allow_reuse_address = True
    daemon_threads = True


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=5582)
    parser.add_argument(
        "--clear-history",
        action="store_true",
        help="remove only this demo's generated call ledger before serving",
    )
    args = parser.parse_args()
    if not (1 <= args.port <= 65535):
        parser.error("--port must be between 1 and 65535")

    credentials = credential_versions()
    if args.clear_history:
        CALLS_FILE.unlink(missing_ok=True)

    server = DemoApiServer((HOST, args.port), DemoApiHandler)
    server.credential_versions = credentials  # type: ignore[attr-defined]
    print(f"customer-api-demo-v3 listening on http://{HOST}:{args.port}", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        print("customer-api-demo-v3 stopped", flush=True)


if __name__ == "__main__":
    main()
