#!/usr/bin/env python3
"""Authenticated, process-free API rehearsal for the renewal demo.

This runner assumes the cockpit already owns Foundation.Host A and B and Studio.  It
never starts or stops a process.  The only subprocesses are the fixed, allowlisted
``prepare.py`` publish/migrate/status operations; all HTTP calls use the authenticated
Foundation.Host API and the local management key supplied by ``DEMO_KEY``.

The report is deliberately an evidence record.  A failed expected transition stops
the run, writes ``artifacts/demo/renewals/api-rehearsal.json`` with ``passed: false``,
and exits non-zero.  It never turns an unavailable or unexpected response into a
successful claim about the demo.

Workflow payloads follow ``e2e-tests/_ElsaCommon.ps1`` and
``e2e-tests/Test-SequenceWorkflow.ps1``; the renewal-specific routes are the
sample endpoints in ``samples/Elsa.Samples.Nuplane.Renewals``.
"""

from __future__ import annotations

import argparse
import json
import os
import queue
import signal
import subprocess
import sys
import threading
import time
from dataclasses import dataclass
from collections import deque
from http.cookiejar import CookieJar
from pathlib import Path
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import HTTPCookieProcessor, Request, build_opener


ROOT = Path(__file__).resolve().parents[3]
DEMO = ROOT / "artifacts" / "demo" / "renewals"
PREPARE = ROOT / "tools" / "demo" / "renewals" / "prepare.py"
REPORT = DEMO / "api-rehearsal.json"
HOSTS = {"a": 5311, "b": 5312}
STUDIO_PORT = 5313
RENEWAL_TYPE = "Elsa.Samples.Nuplane.Renewals.Activities.RegisterRenewal"
RENEWAL_PACKAGE_IDS = (
    "Elsa.Samples.Nuplane.Renewals",
    "Elsa.Samples.Nuplane.Renewals.Activities",
)
SEQUENCE_TYPE = "Elsa.Activities.Sequence.Activities.Sequence"
SCHEMA_FAMILY = "SamplesRenewals"
TERMINAL_STATUSES = {"Completed", "Finished"}
OBSERVABLE_TERMINAL_STATUSES = TERMINAL_STATUSES | {"Faulted", "Cancelled", "Terminated"}
API_REQUEST_TIMEOUT = 30
MANAGEMENT_REQUEST_TIMEOUT = 600
SHARED_INSTALL_TIMEOUT = 900
READINESS_TIMEOUT = 300


class RehearsalFailure(RuntimeError):
    """A failed gate with a safe, reportable step name."""

    def __init__(self, step: str, detail: str, evidence: Any = None):
        super().__init__(detail)
        self.step = step
        self.detail = detail
        self.evidence = evidence


@dataclass
class Response:
    status: int | None
    data: Any
    body: str
    error: str | None = None


def json_safe(value: Any) -> Any:
    """Convert transient HTTP/subprocess values into reportable JSON values."""

    if isinstance(value, Response):
        return {
            "status": value.status,
            "data": json_safe(value.data),
            "body": value.body,
            "error": value.error,
        }
    if isinstance(value, (bytes, bytearray)):
        return bytes(value).decode("utf-8", errors="replace")
    if isinstance(value, Path):
        return str(value)
    if isinstance(value, tuple):
        return [json_safe(item) for item in value]
    if isinstance(value, list):
        return [json_safe(item) for item in value]
    if isinstance(value, dict):
        return {str(key): json_safe(item) for key, item in value.items()}
    return value


def json_or_none(body: str) -> Any:
    try:
        return json.loads(body)
    except (TypeError, json.JSONDecodeError):
        return None


def redact(value: Any, secret: str | None) -> Any:
    """Remove the management key and connection-like values from report material."""

    if isinstance(value, str):
        text = value
        secrets = [secret, os.environ.get("DEMO_ADMIN_PASSWORD"), os.environ.get("ELSA_EF_CONNECTION")]
        for candidate in secrets:
            if candidate:
                replacement = "[management key]" if candidate == secret else "[redacted secret]"
                text = text.replace(candidate, replacement)
        return text
    if isinstance(value, list):
        return [redact(item, secret) for item in value]
    if isinstance(value, dict):
        return {str(key): redact(item, secret) for key, item in value.items()}
    return value


def compact(value: Any, secret: str | None, limit: int = 12000) -> Any:
    safe = redact(value, secret)
    if isinstance(safe, str) and len(safe) > limit:
        return safe[:limit] + "…"
    return safe


class Api:
    def __init__(self, secret: str):
        self.secret = secret
        # Foundation identity login establishes an HttpOnly session cookie; keep it on every
        # later design/runtime request without copying credentials into an Authorization header.
        self.opener = build_opener(HTTPCookieProcessor(CookieJar()))
        self.steps: list[dict[str, Any]] = []
        self.base = {name: f"http://127.0.0.1:{port}" for name, port in HOSTS.items()}

    def request(
        self,
        host: str | None,
        path: str,
        *,
        method: str = "GET",
        payload: Any = None,
        expected: set[int] | None = None,
        management: bool = False,
        timeout: float | None = None,
    ) -> Response:
        request_timeout = timeout if timeout is not None else (
            MANAGEMENT_REQUEST_TIMEOUT if management else API_REQUEST_TIMEOUT
        )
        if host is None:
            url = f"http://127.0.0.1:{STUDIO_PORT}{path}"
        else:
            if host not in self.base:
                raise ValueError(f"Unknown fixed host {host!r}")
            url = self.base[host] + path
        data = None if payload is None else json.dumps(payload, separators=(",", ":")).encode("utf-8")
        headers = {"Accept": "application/json"}
        if data is not None:
            headers["Content-Type"] = "application/json"
        if management:
            headers["X-Elsa-Module-Management-Key"] = self.secret
        request = Request(url, data=data, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=request_timeout) as response:
                body = response.read().decode("utf-8", errors="replace")
                result = Response(response.status, json_or_none(body), body)
        except HTTPError as error:
            body = error.read().decode("utf-8", errors="replace")
            result = Response(error.code, json_or_none(body), body, error=str(error.reason))
        except (URLError, TimeoutError, OSError) as error:
            result = Response(None, None, "", error=str(error))
        if expected is not None and result.status not in expected:
            raise RehearsalFailure(
                f"HTTP {method} {url}",
                f"Expected HTTP {sorted(expected)}, received {result.status or 'no response'}.",
                self.response_evidence(result),
            )
        return result

    def response_evidence(self, response: Response) -> dict[str, Any]:
        return {
            "status": response.status,
            "body": compact(response.data if response.data is not None else response.body, self.secret),
            "error": redact(response.error, self.secret),
        }

    def record(self, name: str, passed: bool, evidence: Any = None, detail: str | None = None) -> None:
        self.steps.append(
            {
                "name": name,
                "passed": passed,
                "detail": redact(detail, self.secret) if detail else None,
                "evidence": compact(evidence, self.secret) if evidence is not None else None,
            }
        )

def pid_file(host: str) -> Path:
    override = os.environ.get(f"DEMO_HOST_{host.upper()}_PID_FILE")
    return Path(override) if override else DEMO / "hosts" / host / "cockpit.pid"


def read_pid(host: str) -> int | None:
    try:
        value = pid_file(host).read_text(encoding="utf-8").strip()
        return int(value) if value.isdigit() else None
    except (OSError, ValueError):
        return None


def installed_version(host: str) -> tuple[str | None, dict[str, Any]]:
    state_path = DEMO / "hosts" / host / ".nuplane" / "store-state.json"
    try:
        state = json.loads(state_path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None, {"path": str(state_path), "error": "missing"}
    except (OSError, json.JSONDecodeError) as error:
        return None, {"path": str(state_path), "error": str(error)}
    active = state.get("activeVersionById")
    if not isinstance(active, dict):
        return None, {"path": str(state_path), "error": "activeVersionById missing", "keys": sorted(state)}
    versions = {
        expected_id: next(
            (str(version) for package_id, version in active.items() if str(package_id).lower() == expected_id.lower()),
            None,
        )
        for expected_id in RENEWAL_PACKAGE_IDS
    }
    if any(version is None for version in versions.values()):
        return None, {"path": str(state_path), "error": "both renewal packages are not installed", "versions": versions, "activeVersionById": active}
    if len(set(versions.values())) != 1:
        return None, {"path": str(state_path), "error": "renewal package versions do not match", "versions": versions, "activeVersionById": active}
    return next(iter(versions.values())), {"path": str(state_path), "versions": versions, "activeVersionById": active}


def wait_until(label: str, action, predicate, *, timeout: float = 120, interval: float = 2) -> Any:
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        last = action()
        if predicate(last):
            return last
        time.sleep(interval)
    raise RehearsalFailure(label, "Timed out waiting for the expected state.", last)


def wait_for_shared_installation(version: str) -> dict[str, tuple[str | None, dict[str, Any]]]:
    """Wait once, with one bounded deadline, until both independent stores confirm a release."""

    def installations() -> dict[str, tuple[str | None, dict[str, Any]]]:
        return {host: installed_version(host) for host in HOSTS}

    return wait_until(
        f"await Nuplane installation {version} on both hosts from the shared publication",
        installations,
        lambda states: all(states[host][0] == version for host in HOSTS),
        timeout=SHARED_INSTALL_TIMEOUT,
        interval=2,
    )


PREPARE_TIMEOUT = 900
PREPARE_OUTPUT_LINES = 300
PREPARE_OUTPUT_LINE_LIMIT = 2000
_ACTIVE_PREPARE: subprocess.Popen[str] | None = None
_SIGNAL_CLEANUP_ACTIVE = False


def terminate_process_tree(process: subprocess.Popen[str]) -> None:
    """Stop the fixed prepare process and descendants after a bounded timeout."""

    try:
        if os.name == "nt":
            if process.poll() is None:
                process.send_signal(getattr(signal, "CTRL_BREAK_EVENT", signal.SIGTERM))
        else:
            # start_new_session=True makes the child the process-group leader; kill the
            # group even when the Python wrapper has already exited but a descendant remains.
            os.killpg(process.pid, signal.SIGTERM)
    except (ProcessLookupError, OSError):
        pass
    if process.poll() is None:
        try:
            process.wait(timeout=10)
            return
        except subprocess.TimeoutExpired:
            pass
    try:
        if os.name == "nt":
            process.kill()
        else:
            os.killpg(process.pid, signal.SIGKILL)
    except (ProcessLookupError, OSError):
        pass
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        pass


def _stream_prepare(api: Api, command: list[str], label: str, process: subprocess.Popen[str]) -> dict[str, Any]:
    lines: deque[str] = deque(maxlen=PREPARE_OUTPUT_LINES)
    output_queue: queue.Queue[str | None] = queue.Queue()

    def read_output() -> None:
        assert process.stdout is not None
        try:
            for line in iter(process.stdout.readline, ""):
                output_queue.put(line)
        finally:
            output_queue.put(None)

    reader = threading.Thread(target=read_output, name="renewals-prepare-output", daemon=True)
    reader.start()
    deadline = time.monotonic() + PREPARE_TIMEOUT
    reader_done = False
    timed_out = False
    while not reader_done:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            timed_out = True
            terminate_process_tree(process)
            break
        try:
            line = output_queue.get(timeout=min(0.25, remaining))
        except queue.Empty:
            if process.poll() is not None and reader_done:
                break
            continue
        if line is None:
            reader_done = True
            continue
        safe_line = compact(line.rstrip("\r\n"), api.secret, PREPARE_OUTPUT_LINE_LIMIT)
        lines.append(str(safe_line))
        print(f"[{label}] {safe_line}", flush=True)

    reader.join(timeout=5)
    while True:
        try:
            line = output_queue.get_nowait()
        except queue.Empty:
            break
        if line is not None:
            safe_line = compact(line.rstrip("\r\n"), api.secret, PREPARE_OUTPUT_LINE_LIMIT)
            lines.append(str(safe_line))
            print(f"[{label}] {safe_line}", flush=True)
    if process.poll() is None:
        terminate_process_tree(process)
    exit_code = process.wait(timeout=10)
    output = "\n".join(lines)
    evidence = {
        "command": [str(item) for item in command],
        "exitCode": exit_code,
        "output": output,
        "stdout": output,
        "stderr": "",
        "outputTruncated": len(lines) == PREPARE_OUTPUT_LINES,
    }
    if timed_out:
        evidence["timeout"] = PREPARE_TIMEOUT
        raise RehearsalFailure(label, "prepare.py timed out; its process group was terminated and no success claim was recorded.", evidence)
    if exit_code != 0:
        raise RehearsalFailure(label, f"prepare.py exited with {exit_code}.", evidence)
    return compact(evidence, api.secret)


def run_prepare(api: Api, args: list[str], label: str) -> dict[str, Any]:
    # The argument list is assembled solely from fixed constants and the fixed host/release allowlist.
    command = [sys.executable, str(PREPARE), *args]
    popen_options: dict[str, Any] = {
        "cwd": ROOT,
        "stdin": subprocess.DEVNULL,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.STDOUT,
        "text": True,
        "bufsize": 1,
        "env": {key: value for key, value in os.environ.items() if key != "DEMO_KEY"},
    }
    if os.name == "nt":
        popen_options["creationflags"] = getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
    else:
        popen_options["start_new_session"] = True

    global _ACTIVE_PREPARE
    process = subprocess.Popen(command, **popen_options)
    _ACTIVE_PREPARE = process
    try:
        return _stream_prepare(api, command, label, process)
    finally:
        # Covers ordinary exceptions as well as an interrupted rehearsal. A completed child is
        # left alone; a still-running child is always terminated before ownership is cleared.
        if process.poll() is None:
            terminate_process_tree(process)
        if _ACTIVE_PREPARE is process:
            _ACTIVE_PREPARE = None


def cleanup_active_prepare(signum: int, _frame: Any) -> None:
    """Terminate only this rehearsal's active prepare child before exiting on SIGINT/SIGTERM."""

    global _SIGNAL_CLEANUP_ACTIVE
    if _SIGNAL_CLEANUP_ACTIVE:
        raise SystemExit(128 + signum)
    _SIGNAL_CLEANUP_ACTIVE = True
    try:
        if _ACTIVE_PREPARE is not None:
            terminate_process_tree(_ACTIVE_PREPARE)
    finally:
        _SIGNAL_CLEANUP_ACTIVE = False
    raise SystemExit(128 + signum)


def install_signal_cleanup() -> dict[int, Any]:
    previous: dict[int, Any] = {}
    for signum in (signal.SIGINT, signal.SIGTERM):
        previous[signum] = signal.getsignal(signum)
        signal.signal(signum, cleanup_active_prepare)
    return previous


def restore_signal_cleanup(previous: dict[int, Any]) -> None:
    for signum, handler in previous.items():
        signal.signal(signum, handler)


def release(api: Api, host: str) -> dict[str, Any]:
    response = api.request(host, "/demo/renewals/release", expected={200})
    if not isinstance(response.data, dict):
        raise RehearsalFailure("read release", "The release endpoint did not return an object.", api.response_evidence(response))
    return response.data


def assert_release(api: Api, host: str, version: str) -> dict[str, Any]:
    data = release(api, host)
    if data.get("packageRelease") != version:
        raise RehearsalFailure(
            f"{host} serves {version}",
            f"The real release endpoint reports {data.get('packageRelease')!r}.",
            data,
        )
    if data.get("schemaFamily") != SCHEMA_FAMILY:
        raise RehearsalFailure(f"{host} serves {version}", f"The release endpoint reports schema family {data.get('schemaFamily')!r}.", data)
    expected_readable = ["1.0.0"] if version == "1.0.0" else ["1.0.0", "2.0.0"]
    if data.get("readableVersions") != expected_readable:
        raise RehearsalFailure(
            f"{host} serves {version}",
            f"The release endpoint reports readable versions {data.get('readableVersions')!r}, expected {expected_readable!r}.",
            data,
        )
    return data


def fixed_literal(reference_key: str, value: Any) -> dict[str, Any]:
    return {
        "referenceKey": reference_key,
        "value": {"value": value, "expressionType": "Literal"},
        "autoEvaluate": None,
        "evaluatorType": None,
        "storageDriverType": None,
        "isSensitive": None,
    }


def activity_node(node_id: str, version_id: str, inputs: list[dict[str, Any]] | None = None, structure: dict[str, Any] | None = None) -> dict[str, Any]:
    node = {"nodeId": node_id, "activityVersionId": version_id, "inputs": inputs or [], "outputs": []}
    if structure is not None:
        node["structure"] = structure
    return node


def renewal_root(sequence_id: str, renewal_id: str, policy: str, premium: int | None = None) -> dict[str, Any]:
    inputs = [fixed_literal("PolicyReference", policy)]
    if premium is not None:
        # Keep this a JSON number so the Decimal contract receives a numeric literal.
        inputs.append(fixed_literal("ProposedPremium", premium))
    child = activity_node("register-renewal", renewal_id, inputs)
    structure = {
        "kind": "elsa.sequence.structure",
        "schemaVersion": "1.0.0",
        "payload": {"activities": [child]},
    }
    return activity_node("sequence-root", sequence_id, structure=structure)


def execute_artifact(
    api: Api,
    host: str,
    artifact_id: str,
    source_reference_id: str,
    label: str,
    expected_activity_version: str,
) -> tuple[Response, dict[str, Any], list[dict[str, Any]]]:
    execute = api.request(
        host,
        f"/runtime/workflows/executables/{artifact_id}/execute",
        method="POST",
        payload={"sourceReferenceId": source_reference_id},
        expected={200},
    )
    if not isinstance(execute.data, dict) or not execute.data.get("workflowExecutionId"):
        raise RehearsalFailure(f"execute {label}", "Execute response did not contain workflowExecutionId.", api.response_evidence(execute))
    execution_id = execute.data["workflowExecutionId"]
    instance = wait_until(
        label,
        lambda: api.request(host, f"/runtime/workflows/instances/{execution_id}", expected={200}).data,
        lambda data: isinstance(data, dict) and isinstance(data.get("instance"), dict) and data["instance"].get("status") in OBSERVABLE_TERMINAL_STATUSES,
        timeout=45,
        interval=0.5,
    )
    if not isinstance(instance, dict) or instance.get("instance", {}).get("status") not in TERMINAL_STATUSES:
        raise RehearsalFailure(label, "Workflow did not reach a terminal success status.", instance)
    if instance.get("incidents"):
        raise RehearsalFailure(label, "Workflow completed with incidents.", instance)
    activities = instance.get("activities") if isinstance(instance, dict) else None
    renewal_runs = [item for item in activities or [] if isinstance(item, dict) and item.get("activityType") == RENEWAL_TYPE]
    if not renewal_runs or any(item.get("status") not in TERMINAL_STATUSES for item in renewal_runs):
        raise RehearsalFailure(f"{label} activity proof", "No completed RegisterRenewal activity execution was observed.", {"instance": instance, "expectedType": RENEWAL_TYPE})
    observed_version = renewal_runs[0].get("activityTypeVersion")
    if observed_version != expected_activity_version:
        raise RehearsalFailure(
            f"{label} activity proof",
            f"The completed RegisterRenewal activity reported version {observed_version!r}, expected {expected_activity_version!r}.",
            {"activity": renewal_runs[0], "expectedVersion": expected_activity_version},
        )
    return execute, instance, renewal_runs


def catalog_ids(api: Api, host: str, renewal_version: str) -> dict[str, str]:
    query = urlencode({"availability": "all"})
    response = api.request(host, f"/design/activities/catalog?{query}", expected={200})
    activities = response.data.get("activities") if isinstance(response.data, dict) else None
    if not isinstance(activities, list):
        raise RehearsalFailure("resolve activity catalog", "Catalog response has no activities array.", api.response_evidence(response))
    matches = [item for item in activities if isinstance(item, dict) and item.get("activityTypeKey") == RENEWAL_TYPE]
    selected = [item for item in matches if item.get("version") == renewal_version]
    sequences = [item for item in activities if isinstance(item, dict) and item.get("activityTypeKey") == SEQUENCE_TYPE]
    if len(selected) != 1:
        raise RehearsalFailure(
            f"resolve RegisterRenewal {renewal_version}",
            f"Expected exactly one catalog row, found {len(selected)}.",
            {"matches": matches},
        )
    if not sequences:
        raise RehearsalFailure("resolve Sequence", "The Sequence activity is absent from the catalog.", {"catalogCount": len(activities)})
    selected_item = selected[0]
    sequence = sorted(sequences, key=lambda item: str(item.get("version", "")), reverse=True)[0]
    if not selected_item.get("available", True):
        raise RehearsalFailure(
            f"resolve RegisterRenewal {renewal_version}",
            f"The exact catalog row is unavailable: {selected_item.get('availabilityReason')}",
            selected_item,
        )
    return {"renewal": str(selected_item["activityVersionId"]), "sequence": str(sequence["activityVersionId"]), "renewalType": RENEWAL_TYPE, "renewalVersion": renewal_version}


def create_publish_execute(api: Api, host: str, ids: dict[str, str], name: str, policy: str, premium: int | None = None) -> dict[str, Any]:
    state = {
        "variables": [],
        "inputs": [],
        "outputs": [],
        "workflowActivityOptions": None,
        "strategyOptions": None,
        "rootActivity": renewal_root(ids["sequence"], ids["renewal"], policy, premium),
    }
    submitted = api.request(
        host,
        "/design/workflows/definitions/submit",
        method="POST",
        payload={"name": name, "description": "Renewal API rehearsal", "state": state},
        expected={200, 201},
    )
    if not isinstance(submitted.data, dict) or not isinstance(submitted.data.get("version"), dict):
        raise RehearsalFailure("submit workflow", "Submit response did not contain version metadata.", api.response_evidence(submitted))
    version_id = submitted.data["version"].get("id")
    if not version_id:
        raise RehearsalFailure("submit workflow", "Submit response did not contain version.id.", api.response_evidence(submitted))
    published = api.request(host, f"/publishing/workflows/{version_id}/publish", method="POST", payload={}, expected={200, 201})
    if not isinstance(published.data, dict) or not published.data.get("artifactId") or not published.data.get("sourceReferenceId"):
        raise RehearsalFailure("publish workflow", "Publish response did not contain artifactId/sourceReferenceId.", api.response_evidence(published))
    execute, instance, renewal_runs = execute_artifact(
        api,
        host,
        published.data["artifactId"],
        published.data["sourceReferenceId"],
        f"workflow {name}",
        ids["renewalVersion"],
    )
    execution_id = execute.data["workflowExecutionId"]
    observed_version = renewal_runs[0].get("activityTypeVersion")
    return {
        "definitionId": submitted.data.get("definition", {}).get("id"),
        "versionId": version_id,
        "artifactId": published.data["artifactId"],
        "sourceReferenceId": published.data["sourceReferenceId"],
        "workflowExecutionId": execution_id,
        "activityTypeVersion": observed_version,
        "policyReference": policy,
        "proposedPremium": premium,
        "status": instance["instance"].get("status"),
    }


def rows(api: Api, host: str, path: str = "/demo/renewals") -> tuple[Response, list[dict[str, Any]]]:
    response = api.request(host, path, expected={200})
    data = response.data
    if not isinstance(data, list):
        raise RehearsalFailure(f"read {path}", "Renewal endpoint did not return an array.", api.response_evidence(response))
    return response, [item for item in data if isinstance(item, dict)]


def assert_pending_reader(status_output: dict[str, Any], host_id: str) -> None:
    output = str(status_output.get("output", "")).lower()
    required = (host_id.lower(), "waits for:", "reads 1.0.0")
    if not all(marker in output for marker in required):
        raise RehearsalFailure(
            "capture pending migration reader",
            f"CLI status did not identify {host_id} as the 1.0.0 reader blocking finalization.",
            status_output,
        )


def run_rehearsal(api: Api) -> dict[str, Any]:
    studio = api.request(None, "/studio-runtime.js", expected={200})
    api.record("Studio is reachable", True, api.response_evidence(studio))

    for host in HOSTS:
        ready = api.request(host, "/health/ready", expected={200})
        release_data = assert_release(api, host, "1.0.0")
        premium = api.request(host, "/demo/renewals/with-premium", expected={404})
        api.record(f"Host {host.upper()} baseline serves 1.0.0", True, {"ready": api.response_evidence(ready), "release": release_data, "premium": api.response_evidence(premium)})

    login = api.request("a", "/_elsa/identity/login", method="POST", payload={"username": "admin", "password": os.environ.get("DEMO_ADMIN_PASSWORD", "Password123!")}, expected={200})
    # AspNetCoreIdentitySignInService returns the provider-neutral AuthSession, whose source contract is
    # status="authenticated" plus a subject after PasswordSignInAsync succeeds. HTTP 200 alone is not auth proof.
    if not isinstance(login.data, dict) or login.data.get("status") != "authenticated" or not login.data.get("subject"):
        raise RehearsalFailure("authenticate host A", "Login did not return an authenticated AuthSession with a subject.", api.response_evidence(login))
    api.record("Authenticate host A", True, {"status": login.data.get("status"), "provider": login.data.get("provider")})

    ids_v1 = catalog_ids(api, "a", "1.0.0")
    api.record("Resolve Sequence and RegisterRenewal 1.0.0", True, ids_v1)

    original_one = create_publish_execute(api, "a", ids_v1, "Renewal API rehearsal v1 POL-1042", "POL-1042")
    api.record("Create, publish, execute original 1.0.0 POL-1042", True, original_one)
    original_two = create_publish_execute(api, "a", ids_v1, "Renewal API rehearsal v1 POL-1043", "POL-1043")
    api.record("Create, publish, execute second original 1.0.0 definition", True, original_two)

    before_publish_rows = rows(api, "a")[1]
    if not {"POL-1042", "POL-1043"}.issubset({item.get("policyReference") for item in before_publish_rows}):
        raise RehearsalFailure("retain baseline renewal rows", "The two baseline policies were not visible through the base endpoint.", before_publish_rows)
    if any(not item.get("id") for item in before_publish_rows):
        raise RehearsalFailure("capture baseline row identities", "The baseline listing omitted a row identity.", before_publish_rows)
    api.record("Retain baseline renewal rows", True, {"rows": before_publish_rows})

    run_prepare(api, ["publish", "--host", "a", "--release", "2"], "Publish release 1.1.0 once to the shared feed watched by both hosts")
    shared_installation = wait_for_shared_installation("1.1.0")
    installed_a = shared_installation["a"]
    api.record("Host A installed release 1.1.0", True, installed_a)
    installed_b = shared_installation["b"]
    api.record("Host B installed release 1.1.0 from the same publication", True, installed_b)
    served_a_before_reload = assert_release(api, "a", "1.0.0")
    served_b_before_reload = assert_release(api, "b", "1.0.0")
    premium_b_before_reload = api.request("b", "/demo/renewals/with-premium", expected={404})
    api.record(
        "One shared publication installs 1.1.0 on both hosts while host B still serves 1.0.0",
        True,
        {"hostAInstalled": installed_a, "hostBInstalled": installed_b, "hostAServed": served_a_before_reload, "hostBServed": served_b_before_reload, "hostBPremium": api.response_evidence(premium_b_before_reload)},
    )

    pid_before = read_pid("a")
    if pid_before is None:
        raise RehearsalFailure("capture host A PID before reload", f"No valid PID at {pid_file('a')}; the cockpit must expose its owned host PID.")
    reload_a_pending = api.request("a", "/_module-management/reload", method="POST", management=True, expected={409})
    api.record("Host A Validate reload refuses pending migration with 409", True, api.response_evidence(reload_a_pending))
    served_a_pending = assert_release(api, "a", "1.0.0")
    api.record("Host A continues serving 1.0.0 after refusal", True, served_a_pending)
    premium_route_pending = api.request("a", "/demo/renewals/with-premium", expected={404})
    api.record("Host A keeps the premium route absent while the old generation serves", True, api.response_evidence(premium_route_pending))

    run_prepare(api, ["migrate", "--host", "a"], "Apply release 1.1.0 migration through prepare.py")
    reload_a_ready = api.request("a", "/_module-management/reload", method="POST", management=True, expected={200})
    pid_after = read_pid("a")
    if pid_after is None or pid_after != pid_before:
        raise RehearsalFailure("verify host A reload keeps PID", f"Expected PID {pid_before}, observed {pid_after}; a shell reload must not be presented as a process restart.", {"pidBefore": pid_before, "pidAfter": pid_after, "reload": api.response_evidence(reload_a_ready)})
    served_a_ready = assert_release(api, "a", "1.1.0")
    api.record("Host A reloads with HTTP 200, serves 1.1.0, and keeps the same PID", True, {"reload": api.response_evidence(reload_a_ready), "release": served_a_ready, "pidBefore": pid_before, "pidAfter": pid_after})

    served_b_pending = assert_release(api, "b", "1.0.0")
    premium_still_pending = api.request("a", "/demo/renewals/with-premium", expected={409})
    status_output = run_prepare(api, ["status", "--host", "a"], "Capture pending-migration CLI status")
    assert_pending_reader(status_output, "toolbox-renewals-b")
    api.record("Premium remains dormant while host B serves 1.0.0 and CLI identifies its blocking reader", True, {"hostBRelease": served_b_pending, "premium": api.response_evidence(premium_still_pending), "cli": status_output})

    reload_b = api.request("b", "/_module-management/reload", method="POST", management=True, expected={200})
    api.record("Host B reloads with HTTP 200", True, api.response_evidence(reload_b))
    served_b = assert_release(api, "b", "1.1.0")
    api.record("Host B serves 1.1.0", True, served_b)

    premium_ready = wait_until(
        "await premium endpoint readiness",
        lambda: api.request("a", "/demo/renewals/with-premium"),
        lambda response: response.status == 200,
        timeout=READINESS_TIMEOUT,
        interval=2,
    )
    if not isinstance(premium_ready.data, list):
        raise RehearsalFailure("read ready premium rows", "Premium endpoint returned no row array after both hosts switched.", api.response_evidence(premium_ready))
    api.record("Premium endpoint becomes ready after both hosts serve 1.1.0", True, api.response_evidence(premium_ready))

    ids_v2 = catalog_ids(api, "a", "1.1.0")
    api.record("Resolve Sequence and RegisterRenewal 1.1.0", True, ids_v2)
    premium_run = create_publish_execute(api, "a", ids_v2, "Renewal API rehearsal v1.1 POL-2048", "POL-2048", 1250)
    if premium_run.get("activityTypeVersion") != "1.1.0":
        raise RehearsalFailure("observe RegisterRenewal 1.1.0 execution", "The completed activity did not report the exact 1.1.0 activity version.", premium_run)
    api.record("Create, publish, execute 1.1.0 POL-2048 with numeric premium", True, premium_run)

    compatibility_run, compatibility_instance, _ = execute_artifact(
        api,
        "a",
        original_one["artifactId"],
        original_one["sourceReferenceId"],
        "retained 1.0 compatibility run",
        "1.0.0",
    )
    api.record("Reexecute original 1.0 artifact after 1.1.0 switch", True, {"dispatch": api.response_evidence(compatibility_run), "instance": compatibility_instance})

    _, final_rows = rows(api, "a", "/demo/renewals/with-premium")
    premium_row = next((row for row in final_rows if row.get("policyReference") == "POL-2048"), None)
    if premium_row is None or not isinstance(premium_row.get("proposedPremium"), (int, float)) or isinstance(premium_row.get("proposedPremium"), bool) or premium_row.get("proposedPremium") != 1250:
        raise RehearsalFailure("retain numeric premium row", "POL-2048 was not retained with numeric proposedPremium 1250.", final_rows)
    policies = {row.get("policyReference") for row in final_rows}
    if not {"POL-1042", "POL-1043", "POL-2048"}.issubset(policies):
        raise RehearsalFailure("retain all renewal rows", "The final premium listing did not contain all retained policy rows.", final_rows)
    final_by_id = {row.get("id"): row for row in final_rows}
    for original in before_publish_rows:
        retained = final_by_id.get(original["id"])
        if retained is None or any(retained.get(field) != original.get(field) for field in ("id", "policyReference", "createdAt")) or "proposedPremium" not in retained or retained["proposedPremium"] is not None:
            raise RehearsalFailure("preserve baseline rows through schema upgrade", "A baseline row changed identity or business fields, disappeared, or gained a premium.", {"before": original, "after": retained})
    if any(row.get("proposedPremium") is not None for row in final_rows if row.get("policyReference") in {"POL-1042", "POL-1043"}):
        raise RehearsalFailure("keep original activity contract premium optional", "An original-contract execution unexpectedly recorded a premium.", final_rows)
    api.record("Retain baseline and premium renewal rows", True, {"rows": final_rows})

    return {
        "passed": True,
        "scope": "Authenticated Foundation.Host API integration only; this report is not Studio browser evidence.",
        "hosts": {host: {"url": api.base[host], "release": release(api, host)} for host in HOSTS},
        "steps": api.steps,
        "artifacts": {"originalV1": original_one, "secondOriginalV1": original_two, "premiumV2": premium_run},
        "rows": final_rows,
    }


def write_report(path: Path, report: dict[str, Any], secret: str | None = None) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    # Normalize response/subprocess objects first, then redact every resulting string, including raw HTTP bodies.
    path.write_text(json.dumps(redact(json_safe(report), secret), indent=2, sort_keys=True) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--report", type=Path, default=REPORT, help="Evidence JSON path; defaults to artifacts/demo/renewals/api-rehearsal.json")
    args = parser.parse_args()
    secret = os.environ.get("DEMO_KEY")
    if not secret:
        failure = {"passed": False, "scope": "Authenticated Foundation.Host API integration only; this report is not Studio browser evidence.", "failure": "DEMO_KEY is required; no management call was attempted."}
        write_report(args.report, failure)
        print(f"FAIL: DEMO_KEY is required; report written to {args.report}", file=sys.stderr)
        return 1
    api = Api(secret)
    started = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
    previous_signal_handlers = install_signal_cleanup()
    try:
        result = run_rehearsal(api)
        result["startedAt"] = started
        result["finishedAt"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        write_report(args.report, result, secret)
        print(f"PASS: renewal API rehearsal report written to {args.report}")
        return 0
    except RehearsalFailure as failure:
        result = {
            "passed": False,
            "scope": "Authenticated Foundation.Host API integration only; this report is not Studio browser evidence.",
            "startedAt": started,
            "finishedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "failedStep": failure.step,
            "failure": failure.detail,
            "failureEvidence": failure.evidence,
            "steps": api.steps,
        }
        write_report(args.report, result, secret)
        print(f"FAIL: {failure.step}; report written to {args.report}", file=sys.stderr)
        return 1
    except Exception as error:
        result = {
            "passed": False,
            "scope": "Authenticated Foundation.Host API integration only; this report is not Studio browser evidence.",
            "startedAt": started,
            "finishedAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            "failedStep": "unexpected rehearsal error",
            "failure": str(error),
            "failureEvidence": None,
            "steps": api.steps,
        }
        write_report(args.report, result, secret)
        print(f"FAIL: unexpected rehearsal error; report written to {args.report}", file=sys.stderr)
        return 1
    finally:
        restore_signal_cleanup(previous_signal_handlers)


if __name__ == "__main__":
    raise SystemExit(main())
