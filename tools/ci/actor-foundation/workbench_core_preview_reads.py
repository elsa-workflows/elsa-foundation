"""Qualify the opt-in read route against real Workbench Identity and PostgreSQL."""

from contextlib import contextmanager
import http.cookiejar
import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request


WORKBENCH = Path(sys.argv[1])
RUNNER = Path(sys.argv[2])
CONNECTION = os.environ["KERNEL_PREVIEW_POSTGRES"]
ROUTE = "/runtime/core-preview/workflows/instances"
LEGACY_ROUTE = "/runtime/workflows/instances/page"
TENANT = "workbench-preview"
READ = "workflows/runtime:read"
INSPECT = "workflows/activity-executions:inspect"
PROFILE = os.environ.get("WORKBENCH_READS_PROFILE", "ordinary")
if PROFILE not in ("ordinary", "timed"):
    raise ValueError("Choose the ordinary or timed Workbench read profile.")
TIMED = PROFILE == "timed"


def expect(condition, message):
    if not condition:
        raise AssertionError(message)
    print(f"ok {message}", flush=True)


def request(client, base, path, payload=None):
    headers = {"Content-Type": "application/json"} if payload else {}
    query = urllib.request.Request(base + path, json.dumps(payload).encode() if payload else None, headers)
    try:
        with client.open(query, timeout=15) as response:
            body = response.read()
            data = json.loads(body) if body and "json" in response.headers.get("Content-Type", "") else None
            return response.status, data
    except urllib.error.HTTPError as error:
        return error.code, None


@contextmanager
def host(workspace, principals, resource_handler=False, replacement=False, enabled=True, reads=True):
    workspace.mkdir()
    for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
        shutil.copy(WORKBENCH.parent / name, workspace / name)
    (workspace / "packages").mkdir()
    if not resource_handler:
        shells_file = workspace / "shells.json"
        shells = json.loads(shells_file.read_text())
        features = shells["CShells"]["Shells"]["default"]["Features"]
        del features["DecisionsWorkflowsApi"]
        if replacement:
            features["PreviewReadQualification"] = {}
        shells_file.write_text(json.dumps(shells))

    env = {name: os.environ[name] for name in ("PATH", "DOTNET_ROOT") if name in os.environ}
    env.update({name: str(workspace) for name in ("HOME", "USERPROFILE", "TMPDIR", "DOTNET_CLI_HOME", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "XDG_CACHE_HOME")})
    env.update({
        "ASPNETCORE_ENVIRONMENT": "Development", "DOTNET_ENVIRONMENT": "Development",
        "ConnectionStrings__KernelCorePreview": CONNECTION, "Elsa__CorePreview__Enabled": str(enabled),
        "Nuplane__Setup__StateFilePath": str(workspace / ".nuplane/store-state.json"),
        "Logging__LogLevel__Microsoft.Hosting.Lifetime": "Information",
    })
    if reads:
        env["Elsa__CorePreview__ReadsTenant"] = TENANT
    if TIMED:
        env["Elsa__CorePreview__TimedNativeJobs"] = "true"
    logins = {}
    for index, (name, tenant, grants) in enumerate(principals):
        password = secrets.token_urlsafe(24) + "aA!14"
        logins[name] = {"username": name, "password": password, "tenantId": tenant}
        prefix = f"Qualification__Principals__{index}__"
        for key, value in (("subject", name), ("tenant", tenant), ("username", name), ("password", password)):
            env[prefix + key] = value
        for grant_index, grant in enumerate(grants):
            env[f"{prefix}exactDirectPermissions__{grant_index}"] = grant

    log = workspace / "host.log"
    with log.open("w") as output:
        child = subprocess.Popen([WORKBENCH, "--contentRoot", workspace, "--urls", "http://127.0.0.1:0"],
                                 cwd=workspace, env=env, stdout=output, stderr=subprocess.STDOUT)
        try:
            stop = time.monotonic() + 85
            base = None
            anonymous = urllib.request.build_opener()
            while time.monotonic() < stop:
                if child.poll() is not None:
                    raise AssertionError("Workbench exited during startup")
                for line in log.read_text().splitlines():
                    if "Now listening on:" in line:
                        base = line.split("Now listening on:", 1)[1].strip()
                        break
                if base and request(anonymous, base, "/__qualification/ready")[0] == 200:
                    break
                time.sleep(.25)
            else:
                raise AssertionError("Workbench did not reach principal bootstrap readiness")

            clients = {}
            for name, login in logins.items():
                client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
                code, _ = request(client, base, "/_elsa/identity/login", login)
                expect(code == 200, f"{name} authenticated through Workbench Identity (status {code})")
                clients[name] = client
            yield base, anonymous, clients
        finally:
            child.terminate()
            try:
                child.wait(timeout=15)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait()


def run(scratch):
    receipt = scratch / "receipt.json"
    env = {"KERNEL_PREVIEW_WORKBENCH": str(WORKBENCH), "KERNEL_PREVIEW_POSTGRES": CONNECTION, "PATH": os.environ["PATH"]}
    profile_args = ["timed"] if TIMED else []
    subprocess.run([RUNNER, "pg-workbench-core-preview", "birth", receipt, *profile_args], env=env, check=True, timeout=100)
    actor = json.loads(receipt.read_text())["Actor"]
    if TIMED:
        subprocess.run([RUNNER, "pg-workbench-core-preview", "restart", receipt, *profile_args], env=env, check=True, timeout=100)
    seed_file = scratch / "expected-roots.json"
    subprocess.run([RUNNER, "pg-workbench-preview-reads-seed", actor, seed_file, *profile_args], env=env, check=True, timeout=45)
    manifest = json.loads(seed_file.read_text())
    seeded = manifest["References"]
    references = sorted(seeded)
    expected_running = sorted(seeded[2:]) if TIMED else references[:25]
    reader = ("preview-allowed", TENANT, [READ, INSPECT])
    principals = [reader, ("preview-other", "other-preview-tenant", [READ, INSPECT]),
                  ("preview-no-inspection", TENANT, [READ]), ("preview-no-runtime", TENANT, [INSPECT])]
    with host(scratch / "enabled", principals) as (base, anonymous, clients):
        allowed = clients[reader[0]]
        stop = time.monotonic() + 30
        while True:
            code, snapshot = request(allowed, base, ROUTE)
            if code != 200:
                raise AssertionError(f"Expected an authorized preview page, received HTTP {code}")
            if snapshot.get("next") or time.monotonic() >= stop:
                break
            time.sleep(.25)
        expect(len(references) == 26 and [item["ref"] for item in snapshot["items"]] == references[:25] and snapshot["next"],
               "the authorized first page has 25 ordered exact references and an opaque continuation")
        query = urllib.parse.urlencode({"cursor": snapshot["next"]})
        code, continuation = request(allowed, base, ROUTE + "?" + query)
        expect(code == 200 and [item["ref"] for item in continuation["items"]] == references[25:] and continuation["next"] is None,
               "the provider-issued continuation reaches the distinct final reference")
        expect(request(allowed, base, ROUTE + "?" + query + "&status=Running")[0] == 400,
               "a valid continuation is refused when its filters change")
        rows = snapshot["items"] + continuation["items"]
        row = next(item for item in rows if item["ref"] == actor)
        status = "Finished" if TIMED else "Running"
        definition = "direct-approval-with-timed-job" if TIMED else "direct-approval"
        expect(row["ref"] == actor and row["status"] == status and row["definition"] == definition,
               "the preview page contains the exact persisted workflow, projected status and definition")
        if TIMED:
            expect(row["endedAt"] == manifest["TerminalAt"],
                   "the HTTP projection matches the exact actor's committed terminal timestamp")
        expect(set(row) == {"ref", "status", "definition", "version", "endedAt"} and snapshot["asOf"] > 0,
               "the preview returns only bounded metadata and the foundation read time")
        for name in ("preview-other", "preview-no-inspection", "preview-no-runtime"):
            status, _ = request(clients[name], base, ROUTE)
            expect(status == 403, f"{name} cannot read the preview page (status {status})")
        expect(request(anonymous, base, ROUTE)[0] == 401, "anonymous requests are challenged")
        expect(request(allowed, base, ROUTE + "?cursor=invalid")[0] == 400, "a bad cursor is refused")
        for status in ("Finished,Faulted", "0"):
            query = urllib.parse.urlencode({"status": status})
            expect(request(allowed, base, ROUTE + "?" + query)[0] == 400, "the status must be one named state")
        code, matches = request(allowed, base, ROUTE + "?status=running&definition=direct-approval")
        expect(code == 200 and [item["ref"] for item in matches["items"]] == expected_running, "status and definition filter the projection")
        code, empty = request(allowed, base, ROUTE + "?definition=no-such-definition")
        expect(code == 200 and empty["items"] == [], "a different definition is a clean miss")
        code, inherited = request(allowed, base, LEGACY_ROUTE)
        expect(code == 200 and all(item.get("workflowExecutionId") != actor for item in inherited["items"]),
               "the legacy workflow list stays on its existing data source")

    with host(scratch / "resource-policy", [reader], resource_handler=True) as (base, _, clients):
        allowed = clients[reader[0]]
        expect(request(allowed, base, LEGACY_ROUTE)[0] == 200, "the stock shell accepts the same tenant's read permissions")
        expect(request(allowed, base, ROUTE)[0] == 403, "the preview refuses the stock shell's custom resource policy")
    with host(scratch / "replacement", [reader], replacement=True) as (base, _, clients):
        allowed = clients[reader[0]]
        expect(request(allowed, base, LEGACY_ROUTE)[0] == 200, "the replacement-only shell accepts the tenant's endpoint permissions")
        expect(request(allowed, base, ROUTE)[0] == 403, "the preview refuses the replacement inspection context without a resource handler")
    with host(scratch / "preview-off", [], enabled=False) as (base, anonymous, _):
        expect(request(anonymous, base, ROUTE)[0] == 404, "the endpoint is absent when Core Preview is disabled")
    with host(scratch / "reads-off", [], reads=False) as (base, anonymous, _):
        expect(request(anonymous, base, ROUTE)[0] == 404, "the endpoint is absent without a configured read tenant")


with tempfile.TemporaryDirectory(prefix="elsa-workbench-preview-reads-") as temp:
    run(Path(temp))
