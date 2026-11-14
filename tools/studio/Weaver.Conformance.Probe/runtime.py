"""Owned real HTTP processes and ordinary Identity sign-in; no synthetic backend."""
from __future__ import annotations

from contextlib import contextmanager
import http.cookiejar
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import socket
import subprocess
import time
import urllib.error
from urllib.parse import urlparse
import urllib.request

from artifacts import check, sha, write_json

DEVELOPER_PORTS = {5089, 5095, 7030, 7243}
STARTUP_OBSERVATION_SECONDS = 120
SHUTDOWN_OBSERVATION_SECONDS = 15
RUNTIME_ENVIRONMENT = {"PATH", "HOME", "USER", "LOGNAME", "TMPDIR", "LANG", "LC_ALL", "TZ", "DOTNET_ROOT",
                       "SSL_CERT_FILE", "SSL_CERT_DIR", "ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT", "WEAVER_FRONTEND_AUDIT"}


def owned_content_root(run, profile):
    """Resolve an existing owned runtime directory; absent selection preserves the published-directory default.

    A separate root contains fresh, caller-generated runtime configuration/state. This helper never copies inputs or
    creates it, and refuses overlap with a binary publish so configuration writers cannot edit immutable upgrade inputs.
    """
    selected = profile.get("contentRoot", profile["published"])
    check(isinstance(selected, (str, os.PathLike)), "Consumer content root must be an owned directory path")
    selected = Path(selected)
    root = run.root.resolve()
    check(selected.is_absolute() and selected.is_dir() and selected.resolve().is_relative_to(root),
          "Consumer content root must be an existing directory under the owned run root")
    check(all(not parent.is_symlink() for parent in (selected, *selected.parents)
              if parent == root or parent.is_relative_to(root)),
          "Consumer content root must not contain a symlink under the owned run root")
    selected = selected.resolve()
    published = profile["published"].resolve()
    if selected != published:
        for other in run.profiles.values():
            binary_root = other["published"].resolve()
            check(not selected.is_relative_to(binary_root) and not binary_root.is_relative_to(selected),
                  "Separate consumer content root must not overlap a published binary directory")
    return selected


def published_files(directory):
    """Inventory an immutable binary publish, including dependencies rather than only its entry assembly."""
    directory = Path(directory)
    check(directory.is_dir() and not directory.is_symlink(), "Published files need an ordinary binary directory")
    files = {}
    for path in sorted(directory.rglob("*")):
        check(not path.is_symlink(), "Published files cannot contain symlinks")
        if path.is_dir():
            continue
        check(path.is_file(), "Published files cannot contain special entries")
        files[path.relative_to(directory).as_posix()] = {"bytes": path.stat().st_size, "sha256": sha(path)}
    check(bool(files), "Published file inventory cannot be empty")
    return files


def published_files_digest(files):
    return hashlib.sha256(json.dumps(files, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


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
        check(status == expected, f"{method} {path} returned {status}, expected {expected}" + failure_excerpt(status, data))
        return json.loads(data), response_headers


_SECRET_TEXT = re.compile(r'\b(?:Bearer|Basic|Digest|Negotiate)\s+\S+|'
                          r'\b(?:access_?token|refresh_?token|id_?token|token|password|api_?key|client_?secret|secret|cookie)\b\s*[:=]\s*\S+',
                          re.I)


def failure_excerpt(status, data):
    """A bounded, redacted excerpt of an unexpected 4xx problem body, so a refusal names its own cause in the failure.

    Only a parsed JSON problem's own message fields are used: a 5xx, or any body that is not a JSON object (such as a
    development exception page), contributes nothing, because its text can carry private exception detail. The whole
    excerpt is redacted before it is bounded, so a secret crossing the bound can never leave a readable prefix.
    """
    if not isinstance(status, int) or not 400 <= status < 500 or not data:
        return ""
    try:
        problem = json.loads(data)
    except (ValueError, UnicodeDecodeError):
        return ""
    if not isinstance(problem, dict):
        return ""
    parts = [problem[key] for key in ("title", "detail", "message", "errorCode") if isinstance(problem.get(key), str)]
    errors = problem.get("errors")
    if isinstance(errors, dict):
        parts.extend(f"{key}: {item}" for key, values in errors.items() if isinstance(values, list)
                     for item in values if isinstance(item, str))
    text = _SECRET_TEXT.sub("[redacted]", "; ".join(parts))
    return "; body: " + text[:400] if text else ""


def normal_sign_in(base, username, password, output, *, tenant_id=None):
    """Create private browser state through real native cookie login and token exchange."""
    jar = http.cookiejar.CookieJar()
    opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    client = Http(base, opener=opener)
    login = {"username": username, "password": password}
    if tenant_id is not None:
        check(isinstance(tenant_id, str) and bool(tenant_id.strip()) and tenant_id == tenant_id.strip(),
              "Normal Identity login needs an explicit nonblank tenant")
        login["tenantId"] = tenant_id
    session, _ = client.json("/_elsa/identity/login", login, method="POST")
    check(session.get("status") == "authenticated", "Normal Identity login did not authenticate")
    if tenant_id is not None:
        check(session.get("tenantId") == tenant_id, "Normal Identity login selected another tenant")
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


class _OwnedProcess:
    """One owned consumer process generation: its exact executable, origin, log, receipt and empty runtime caches.

    Generation 1 keeps the original receipt names; a restarted generation gets its own, so no receipt is overwritten.
    """

    def __init__(self, run, name, origin, generation=1):
        check(type(generation) is int and generation >= 1, "A host generation is a positive integer")
        profile = run.profiles[name]
        self.run, self.name, self.origin, self.generation = run, name, origin, generation
        self.root = profile["published"].resolve()
        check(self.root.is_relative_to(run.root), "Published consumer escaped owned output")
        self.profile = dict(profile)
        self.content_root = owned_content_root(run, self.profile)
        Http(origin)  # Validate origin before process creation.
        suffix = "" if generation == 1 else "-g" + str(generation)
        self.log = run.root / (name + "-runtime" + suffix + ".log")
        self.receipt = run.root / (name + "-process" + suffix + ".json")
        check(generation == 1 or not (self.log.exists() or self.receipt.exists()), "Never overwrite a retained host generation")
        self.marker = "WEAVER_" + name.upper() + "_CONSUMER_READY"
        self.assembly = self.root / profile["assembly"]
        check(self.assembly.is_file() and not self.assembly.is_symlink() and self.assembly.resolve().parent == self.root,
              "Consumer executable must be an ordinary file in its published binary directory")
        self.published_files = None
        if "publishedFiles" in profile:
            check(self.content_root != self.root, "An immutable publish requires a separate runtime content root")
            actual = published_files(self.root)
            check(profile["publishedFiles"] == actual, "Published file inventory differs from the retained producer inputs")
            self.published_files = actual
        self.env = profile["env"]
        self.process, self.started, self.caches, self.cache_files = None, None, {}, None

    def spawn(self):
        check(owned_content_root(self.run, self.profile) == self.content_root, "Consumer content root changed before launch")
        if self.published_files is not None:
            check(published_files(self.root) == self.published_files, "Published files changed before process creation")
        identity = self.content_root.stat()
        assembly_hash = sha(self.assembly)
        command = [self.run.dotnet, str(self.assembly), "--contentRoot", str(self.content_root), "--urls", self.origin]
        # Restore caches are producer inputs, not undeclared native runtime feeds. Each launch
        # receives new empty caches; the explicit retained archive closure must suffice.
        runtime_inputs = self.run.root / (self.name + "-runtime-inputs-" + str(time.time_ns()))
        runtime_inputs.mkdir(mode=0o700)
        # Runtime inputs are explicit private files/feed archives. Ambient provider credentials,
        # interposers, startup hooks and restore/build-cache overrides are not consumer inputs.
        runtime_env = {key: value for key, value in self.env.items() if key in RUNTIME_ENVIRONMENT}
        for key, folder in (("NUGET_PACKAGES", "packages"), ("NUGET_HTTP_CACHE_PATH", "http")):
            cache = runtime_inputs / folder
            cache.mkdir(mode=0o700)
            runtime_env[key] = str(cache)
            self.caches[key] = str(cache)
        with self.log.open("w") as stream:
            self.process = subprocess.Popen(command, cwd=self.root, env=runtime_env, stdin=subprocess.DEVNULL,
                                            stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        self.started = {"pid": self.process.pid, "command": command, "assemblySha256": assembly_hash, "origin": self.origin,
                        "publishedDirectory": str(self.root), "contentRoot": str(self.content_root),
                        "contentRootIdentity": {"device": identity.st_dev, "inode": identity.st_ino},
                        "log": str(self.log), "startedAt": time.time(),
                        "runtimePackageCaches": dict(self.caches)}
        if self.published_files is not None:
            self.started.update(publishedFilesSha256=published_files_digest(self.published_files),
                                publishedFileCount=len(self.published_files))
        if self.generation != 1:
            self.started["generation"] = self.generation
        self.save()
        return self.started

    def save(self):
        private_json(self.receipt, self.started)

    def await_ready(self):
        process, log = self.process, self.log
        deadline = time.monotonic() + STARTUP_OBSERVATION_SECONDS
        while True:
            text = log.read_text(errors="replace")
            check(process.poll() is None, "Owned consumer exited during activation: " + str(log))
            if self.marker in text:
                listening = re.findall(r"Now listening on: (http://127\.0\.0\.1:\d+)", text)
                check(self.origin in listening, "Ready marker did not match actual Kestrel listener")
                break
            if time.monotonic() >= deadline:
                self.started.setdefault("startupObservations", []).append({"status": "pending", "pid": process.pid, "at": time.time()})
                self.save()
                # A pending observation is not a terminal host. Keep observing this exact
                # process; a parent can inspect the receipt/log without restarting it.
                deadline = time.monotonic() + STARTUP_OBSERVATION_SECONDS
            time.sleep(0.1)
        self.started["readyAt"] = time.time()
        self.save()
        return self.started

    def require_running(self):
        check(self.process.poll() is None, "Owned consumer exited before qualification completed: " + str(self.log))
        self.require_unchanged_inputs()
        check(not any(path.is_file() for folder in self.caches.values() for path in Path(folder).rglob("*")),
              "Native runtime populated an undeclared NuGet cache instead of using its retained feed closure")

    def require_unchanged_inputs(self):
        check(owned_content_root(self.run, self.profile) == self.content_root, "Consumer content root changed during its generation")
        identity = self.content_root.stat()
        check({"device": identity.st_dev, "inode": identity.st_ino} == self.started["contentRootIdentity"],
              "Consumer content root directory was replaced during its generation")
        check(self.assembly.is_file() and not self.assembly.is_symlink()
              and self.assembly.resolve().parent == self.root and sha(self.assembly) == self.started["assemblySha256"],
              "Published consumer assembly changed during its generation")
        if self.published_files is not None:
            check(published_files(self.root) == self.published_files, "Published dependency files changed during their generation")

    def shutdown(self, *, abrupt=False):
        process = self.process
        if process is not None and process.poll() is None:
            if abrupt:
                # A crash of this owned host: kill its own process group (start_new_session made it the leader).
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait()
            else:
                process.terminate()
                while True:
                    try:
                        process.wait(timeout=SHUTDOWN_OBSERVATION_SECONDS)
                        break
                    except subprocess.TimeoutExpired:
                        self.started.setdefault("shutdownObservations", []).append({"status": "pending", "pid": process.pid, "at": time.time()})
                        self.save()
                        # SIGTERM was sent once. A pending shutdown remains this same owned
                        # process; retain the original exception until it actually exits.
        self.cache_files = {key: [str(path.relative_to(Path(folder))) for path in Path(folder).rglob("*") if path.is_file()]
                            for key, folder in self.caches.items()}
        self.started.update(stoppedAt=time.time(), exitCode=process.returncode if process is not None else None,
                            runtimeCacheFiles=self.cache_files)
        if abrupt:
            self.started["stop"] = "abrupt"
        self.save()
        return self.started

    def require_stopped(self, *, abrupt=False):
        expected = -signal.SIGKILL if abrupt else 0
        check(self.process.returncode == expected,
              ("Owned consumer was not observed killed by its abrupt restart: " if abrupt else
               "Owned consumer exited unsuccessfully during shutdown: ") + str(self.log))
        check(not any(self.cache_files.values()),
              "Native runtime populated an undeclared NuGet cache instead of using its retained feed closure")
        self.require_unchanged_inputs()
        self.started["inputsUnchangedAtCloseout"] = True
        self.save()


@contextmanager
def server(run, name, origin, *, generation=1):
    """Launch/stop only this invocation's process; retain its exact executable and origin."""
    owned = _OwnedProcess(run, name, origin, generation)
    owned.spawn()
    try:
        owned.await_ready()
        yield owned.started
        owned.require_running()
    finally:
        owned.shutdown()
    # Only a successful body reaches these checks. Retain final shutdown evidence first,
    # and let an original startup/qualification failure propagate without replacing it.
    owned.require_stopped()


class RestartableServer:
    """One owned consumer host on one fixed owned origin that the driver itself can restart between phases.

    Each generation is a separate owned process with its own log, receipt and new empty runtime caches, and is held
    to the same rules as server(): a graceful stop sends SIGTERM once and must exit 0, an abrupt stop kills the owned
    process group and must be observed killed, and neither may populate a runtime NuGet cache. Configuration between
    phases changes only while no generation runs. A failure keeps its own cause; the last generation always stops.
    """

    def __init__(self, run, name, origin):
        self.run, self.name, self.origin = run, name, origin
        self.generation, self.current, self.restarts = 0, None, []

    def __enter__(self):
        self._start()
        return self

    @property
    def started(self):
        check(self.current is not None, "No owned host generation is running")
        return self.current.started

    def _start(self):
        owned = _OwnedProcess(self.run, self.name, self.origin, self.generation + 1)
        self.generation += 1
        owned.spawn()
        try:
            owned.await_ready()
        except BaseException:
            owned.shutdown()
            raise
        self.current = owned
        return owned.started

    def restart(self, reason, *, abrupt=False, while_stopped=None):
        """Stop the running generation (gracefully or abruptly), run while_stopped, then start the next generation."""
        check(self.current is not None, "Restart needs a running owned host generation")
        check(isinstance(reason, str) and reason.strip(), "Name the phase boundary a restart serves")
        owned, self.current = self.current, None
        try:
            owned.require_running()
        finally:
            owned.shutdown(abrupt=abrupt)
        owned.require_stopped(abrupt=abrupt)
        stopped_at = time.time()
        provenance = ("publishedDirectory", "contentRoot", "contentRootIdentity", "assemblySha256", "command",
                      "publishedFilesSha256", "publishedFileCount")
        stopped = {key: owned.started.get(key) for key in ("pid", "origin", "log", "startedAt", "readyAt", "stoppedAt", "exitCode", "runtimeCacheFiles", "inputsUnchangedAtCloseout", *provenance)}
        waited = while_stopped() if while_stopped is not None else None
        started_at = time.time()
        started = self._start()
        record = {"name": self.name, "reason": reason, "kind": "abrupt" if abrupt else "graceful", "origin": self.origin,
                  "fromGeneration": owned.generation, "toGeneration": self.current.generation,
                  "stoppedAt": stopped_at, "startedAt": started_at, "readyAt": started.get("readyAt"),
                  "stoppedProcess": stopped, "startedProcess": {key: started.get(key) for key in ("pid", "origin", "log", "startedAt", "readyAt", *provenance)},
                  "whileStopped": waited}
        self.restarts.append(record)
        return record

    def __exit__(self, kind, error, traceback):
        owned, self.current = self.current, None
        if owned is None:
            return False
        if kind is not None:
            # Keep the original failure; still stop this exact owned generation.
            owned.shutdown()
            return False
        try:
            owned.require_running()
        finally:
            owned.shutdown()
        owned.require_stopped()
        return False
