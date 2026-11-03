"""Owned HTTP processes and controlled backend for actual composition journeys."""

from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import re
import subprocess
import threading
import time
import urllib.error
import urllib.request

from artifacts import check

MANAGEMENT_KEY = "composition-owned-management-sentinel"
PERMISSIONS = {
    "composition-owner": ["module-management:manage"],
    "composition-reader": ["module-management:read"],
    "composition-no-permissions": [],
}


def profile_environment(root, name):
    env = {key: value for key, value in os.environ.items() if not key.lower().startswith(
        ("studio__", "cshells__", "nuplane__", "themes__", "aspnetcore_", "qualification__"))}
    env.update(PATH=str(root / (name + "-denied")) + os.pathsep + env["PATH"],
               NUGET_PACKAGES=str(root / (name + "-cache")),
               STUDIO_FRONTEND_DENIAL_AUDIT=str(root / (name + "-frontend-invocations.log")),
               ASPNETCORE_ENVIRONMENT="Production", DOTNET_ENVIRONMENT="Production")
    return env


def request(base, path, body=None, method="GET", bearer=None, content_type="application/json"):
    headers = {"Content-Type": content_type}
    if bearer:
        headers["Authorization"] = "Bearer " + bearer
    data = body if isinstance(body, bytes) else None if body is None else json.dumps(body).encode()
    req = urllib.request.Request(base + path, data=data, method=method, headers=headers)
    try:
        response = urllib.request.urlopen(req, timeout=25)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, response.read(), {key.lower(): value for key, value in response.headers.items()}


def get_json(base, path, bearer=None):
    status, body, _ = request(base, path, bearer=bearer)
    check(status == 200, f"{path} returned {status}")
    return json.loads(body)


@contextmanager
def server(dotnet, published, env, log, assembly="Consumer.dll", ready=True):
    with log.open("w") as output:
        process = subprocess.Popen([dotnet, str(published / assembly), "--contentRoot", str(published), "--urls", "http://127.0.0.1:0"],
                                   cwd=published, env=env, stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 90
            while True:
                text = log.read_text(errors="replace")
                port = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)", text)
                if port and (not ready or "COMPOSITION_QUALIFICATION_READY" in text):
                    yield port[1]
                    break
                check(process.poll() is None and time.monotonic() < deadline, "Actual published server did not become ready: " + str(log))
                time.sleep(.1)
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=10)


class SessionBackend:
    """A synthetic external dependency using the existing session HTTP protocol."""

    def __init__(self):
        self.requests = []
        self.lock = threading.Lock()
        owner = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *_):
                pass

            def do_GET(self):
                bearer = self.headers.get("Authorization", "").removeprefix("Bearer ")
                key = self.headers.get("X-Elsa-Module-Management-Key")
                with owner.lock:
                    owner.requests.append({"path": self.path, "bearer": bearer, "managementKeyPresent": key is not None,
                                           "managementKeyMatches": key == MANAGEMENT_KEY})
                if self.path.rstrip("/").endswith("/identity/session"):
                    payload = {"status": "authenticated", "subject": "composition-user", "permissions": PERMISSIONS[bearer]} if bearer in PERMISSIONS else {"status": "anonymous"}
                else:
                    payload = {"host": {"id": "composition-backend"}, "modules": [], "features": [], "capabilities": {}}
                data = json.dumps(payload).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.base = "http://127.0.0.1:" + str(self.server.server_port)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)

    def __enter__(self):
        self.thread.start()
        return self

    def __exit__(self, *_):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)

    def snapshot(self):
        with self.lock:
            return list(self.requests)
