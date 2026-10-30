"""Owned real HTTP processes and ordinary Identity sign-in; no synthetic backend."""
from __future__ import annotations

from contextlib import contextmanager
import http.cookiejar
import json
import os
from pathlib import Path
import re
import socket
import subprocess
import time
import urllib.error
from urllib.parse import urlparse
import urllib.request

from artifacts import check, sha, write_json

DEVELOPER_PORTS = {5089, 5095, 7030, 7243}
STARTUP_OBSERVATION_SECONDS = 120
RUNTIME_ENVIRONMENT = {"PATH", "HOME", "USER", "LOGNAME", "TMPDIR", "LANG", "LC_ALL", "TZ", "DOTNET_ROOT",
                       "SSL_CERT_FILE", "SSL_CERT_DIR", "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "WEAVER_FRONTEND_AUDIT"}


def private_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, "w") as stream:
        json.dump(value, stream, indent=2)
        stream.write("\n")
    path.chmod(0o600)


def owned_origin():
    # The launched Kestrel process must actually claim this origin; a race is a failed startup.
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        port = listener.getsockname()[1]
    check(port not in DEVELOPER_PORTS, "Refuse a developer origin")
    return "http://127.0.0.1:" + str(port)


class Http:
    def __init__(self, base, *, bearer=None, opener=None):
        parsed = urlparse(base)
        check(parsed.scheme == "http" and parsed.hostname == "127.0.0.1"
              and parsed.port and parsed.port not in DEVELOPER_PORTS
              and not parsed.username and not parsed.password and parsed.path in ("", "/")
              and not parsed.query and not parsed.fragment,
              "HTTP qualification needs an explicit owned loopback origin")
        self.base = base.rstrip("/")
        self.bearer = bearer
        self.opener = opener or urllib.request.build_opener()

    def request(self, path, body=None, *, method="GET", headers=None):
        check(path.startswith("/") and not path.startswith("//"), "Use a local owning endpoint path")
        values = dict(headers or {})
        if self.bearer:
            values["Authorization"] = "Bearer " + self.bearer
        data = body if isinstance(body, bytes) else None if body is None else json.dumps(body).encode()
        if data is not None:
            values.setdefault("Content-Type", "application/json")
        request = urllib.request.Request(self.base + path, data=data, method=method, headers=values)
        try:
            response = self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as failure:
            response = failure
        with response:
            return response.status, response.read(), {k.lower(): v for k, v in response.headers.items()}

    def json(self, path, body=None, *, method="GET", headers=None, expected=200):
        status, data, response_headers = self.request(path, body, method=method, headers=headers)
        check(status == expected, f"{method} {path} returned {status}, expected {expected}")
        return json.loads(data), response_headers


def normal_sign_in(base, username, password, output):
    """Create private browser state through real native cookie login and token exchange."""
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    client = Http(base, opener=opener)
    session, _ = client.json("/_elsa/identity/login", {"username": username, "password": password}, method="POST")
    check(session.get("status") == "authenticated", "Normal Identity login did not authenticate")
    token, _ = client.json("/_elsa/identity/token")
    check(isinstance(token.get("accessToken"), str) and token["accessToken"], "Normal token exchange failed")
    authenticated = Http(base, bearer=token["accessToken"], opener=opener)
    observed, _ = authenticated.json("/_elsa/identity/session")
    check(observed.get("status") == "authenticated" and observed.get("subject") == session.get("subject")
          and observed.get("tenantId") == session.get("tenantId"), "Bearer/session identity mismatch")
    cookies = []
    for cookie in jar:
        cookies.append({"name": cookie.name, "value": cookie.value, "domain": cookie.domain,
                        "path": cookie.path, "expires": cookie.expires if cookie.expires is not None else -1,
                        "httpOnly": "HttpOnly" in cookie._rest, "secure": cookie.secure,
                        "sameSite": cookie._rest.get("SameSite", "Lax").capitalize()})
    check(bool(cookies), "Normal login did not issue a browser cookie")
    private_json(output, {"cookies": cookies, "origins": []})
    return authenticated, {"subject": observed["subject"], "tenantId": observed["tenantId"],
                           "normalLoginStatus": 200, "normalTokenExchangeStatus": 200,
                           "storageState": str(output), "cookieCount": len(cookies)}


@contextmanager
def server(run, name, origin):
    """Launch/stop only this invocation's process; retain its exact executable and origin."""
    profile = run.profiles[name]
    root = profile["published"].resolve()
    check(root.is_relative_to(run.root), "Published consumer escaped owned output")
    Http(origin)  # Validate origin before process creation.
    log = run.root / (name + "-runtime.log")
    marker = "WEAVER_" + name.upper() + "_CONSUMER_READY"
    assembly = root / profile["assembly"]
    command = [run.dotnet, str(assembly), "--contentRoot", str(root), "--urls", origin]
    # Restore caches are producer inputs, not undeclared native runtime feeds. Each launch
    # receives new empty caches; the explicit retained archive closure must suffice.
    runtime_inputs = run.root / (name + "-runtime-inputs-" + str(time.time_ns()))
    runtime_inputs.mkdir(mode=0o700)
    # Runtime inputs are explicit private files/feed archives. Ambient provider credentials,
    # interposers, startup hooks and restore/build-cache overrides are not consumer inputs.
    runtime_env = {key: value for key, value in profile["env"].items() if key in RUNTIME_ENVIRONMENT}
    caches = {}
    for key, folder in (("NUGET_PACKAGES", "packages"), ("NUGET_HTTP_CACHE_PATH", "http")):
        cache = runtime_inputs / folder
        cache.mkdir(mode=0o700)
        runtime_env[key] = str(cache)
        caches[key] = str(cache)
    with log.open("w") as stream:
        process = subprocess.Popen(command, cwd=root, env=runtime_env, stdin=subprocess.DEVNULL,
                                   stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
    started = {"pid": process.pid, "command": command, "assemblySha256": sha(assembly), "origin": origin,
               "contentRoot": str(root), "log": str(log), "startedAt": time.time(),
               "runtimePackageCaches": caches}
    private_json(run.root / (name + "-process.json"), started)
    try:
        deadline = time.monotonic() + STARTUP_OBSERVATION_SECONDS
        while True:
            text = log.read_text(errors="replace")
            check(process.poll() is None, "Owned consumer exited during activation: " + str(log))
            if marker in text:
                listening = re.findall(r"Now listening on: (http://127\.0\.0\.1:\d+)", text)
                check(origin in listening, "Ready marker did not match actual Kestrel listener")
                break
            if time.monotonic() >= deadline:
                started.setdefault("startupObservations", []).append({"status": "pending", "pid": process.pid, "at": time.time()})
                private_json(run.root / (name + "-process.json"), started)
                # A pending observation is not a terminal host. Keep observing this exact
                # process; a parent can inspect the receipt/log without restarting it.
                deadline = time.monotonic() + STARTUP_OBSERVATION_SECONDS
            time.sleep(0.1)
        yield started
        check(process.poll() is None, "Owned consumer exited before qualification completed: " + str(log))
        check(not any(path.is_file() for folder in caches.values() for path in Path(folder).rglob("*")),
              "Native runtime populated an undeclared NuGet cache instead of using its retained feed closure")
    finally:
        if process.poll() is None:
            process.terminate()
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
                process.wait(timeout=10)
        cache_files = {key: [str(path.relative_to(Path(folder))) for path in Path(folder).rglob("*") if path.is_file()]
                       for key, folder in caches.items()}
        started.update(stoppedAt=time.time(), exitCode=process.returncode, runtimeCacheFiles=cache_files)
        private_json(run.root / (name + "-process.json"), started)
