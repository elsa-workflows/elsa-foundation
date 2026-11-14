#!/usr/bin/env python3
"""Own a BPMN-enabled Studio/Workbench pair and run spec 1184 FR-012/FR-014 live journeys.

Test-only. Copies host configuration into disposable roots, enables ActivitiesBpmnHosting on
that copy, seeds a second administrator, and points Playwright at the pair. Production
Workbench/Studio sources are not changed.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import json
import os
from pathlib import Path
import shutil
import signal
import socket
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]


def stop_group(process):
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except ProcessLookupError:
        pass
    try:
        process.wait(timeout=15)
    except subprocess.TimeoutExpired:
        pass
    finally:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        process.wait(timeout=10)


def command(args, log, *, cwd=ROOT, env=None):
    with log.open("w") as output:
        process = subprocess.Popen(args, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            result = process.wait()
            if result:
                raise subprocess.CalledProcessError(result, args)
        finally:
            stop_group(process)


def clean_environment():
    allowed = {"PATH", "HOME", "USERPROFILE", "DOTNET_ROOT", "DOTNET_ROOT_X64", "TMPDIR", "TEMP", "TMP",
               "DOTNET_CLI_HOME", "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO", "LANG", "LC_ALL"}
    env = {key: value for key, value in os.environ.items() if key in allowed}
    env.update(ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development")
    return env


def free_port():
    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


@contextmanager
def host(directory, dll, url, ready_path, log, env):
    with log.open("w") as output:
        process = subprocess.Popen(["dotnet", str(directory / dll), "--contentRoot", str(directory), "--urls", url],
                                   cwd=directory, env=env, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            deadline = time.monotonic() + 180
            while True:
                if process.poll() is not None:
                    raise RuntimeError(f"{dll} exited; see {log}")
                bound = any(line.strip() == f"Now listening on: {url}" for line in log.read_text().splitlines())
                if bound:
                    try:
                        with urllib.request.urlopen(url + ready_path, timeout=2) as response:
                            if response.status == 200 and process.poll() is None:
                                break
                    except (OSError, TimeoutError):
                        pass
                if time.monotonic() > deadline:
                    raise TimeoutError(f"{dll} not ready; see {log}")
                time.sleep(0.25)
            yield process
        finally:
            stop_group(process)


def prepare_host(project, destination):
    output = project.parent / "bin/Debug/net10.0"
    shutil.copytree(output, destination, ignore=shutil.ignore_patterns(
        "*.db*", "*.sqlite*", "nuplane.lock.json", ".elsa", ".nuplane", "packages", "appsettings*.json", "shells*.json"))
    for file in destination.rglob("*"):
        if file.is_file() and (file.suffix in {".db", ".db-shm", ".db-wal"} or file.name == "nuplane.lock.json"):
            file.unlink()
    for name in ["appsettings.json", "shells.json"]:
        shutil.copy2(project.parent / name, destination / name)


def enable_direct_bpmn(backend, studio, backend_url, studio_url):
    shells = json.loads((backend / "shells.json").read_text(encoding="utf-8-sig"))
    features = shells["CShells"]["Shells"]["default"]["Features"]
    features["ActivitiesBpmnHosting"] = {"AdmitNewExecutions": True}
    features["ActivitiesBpmnHostingApi"] = {}
    features["ActivitiesBpmnDirectPublishing"] = {}
    for name in ("WorkflowsRuntimeDistributed", "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence",
                 "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence"):
        features.pop(name, None)
    features["WorkflowsRuntimeEntityFrameworkCore"]["ConnectionString"] = "Data Source=runtime.db"
    for name in ("ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishingEntityFrameworkCore"):
        if name in features:
            features[name]["ConnectionString"] = "Data Source=design.db"
    if "FoundationIdentityAspNetCoreIdentity" in features:
        features["FoundationIdentityAspNetCoreIdentity"]["AllowedReturnUrlOrigins"] = [studio_url]
    write_json(backend / "shells.json", shells)
    config = json.loads((backend / "appsettings.json").read_text(encoding="utf-8-sig"))
    config["Cors"]["AllowedOrigins"] = [studio_url]
    write_json(backend / "appsettings.json", config)
    config = json.loads((studio / "appsettings.json").read_text(encoding="utf-8-sig"))
    config["Studio"]["BackendBaseUrl"] = backend_url
    write_json(studio / "appsettings.json", config)


def seed_operator(backend):
    shells = json.loads((backend / "shells.json").read_text(encoding="utf-8-sig"))
    seed = shells["CShells"]["Shells"]["default"]["Features"]["FoundationIdentityAspNetCoreIdentityEntityFrameworkCore"]
    seed["SeedAdminUserName"] = "operator"
    seed["SeedAdminEmail"] = "operator@elsa.local"
    write_json(backend / "shells.json", shells)


def run(output, skip_build=False):
    output.mkdir(parents=True, exist_ok=False)
    sha = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    dirty = subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip()
    receipt = {"sourceSha": sha, "dirty": bool(dirty), "status": "failed", "stage": "build"}
    try:
        backend_project = ROOT / "Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj"
        studio_project = ROOT / "Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj"
        if not skip_build:
            command(["dotnet", "build", str(backend_project), "-p:RestoreLockedMode=true", "-v:q", "-nologo", "-m:1", "/nr:false"],
                    output / "backend-build.log")
            command(["pnpm", "build"], output / "frontend-build.log", cwd=ROOT / "Studio")
            command(["dotnet", "build", str(studio_project), "-p:RestoreLockedMode=true", "-v:q", "-nologo", "-m:1", "/nr:false"],
                    output / "studio-build.log")
        elif not (backend_project.parent / "bin/Debug/net10.0/Elsa.Workbench.dll").is_file():
            raise FileNotFoundError("Build the Workbench and Studio hosts before --skip-build.")
        with tempfile.TemporaryDirectory(prefix="elsa-bpmn-settlement-scope-") as temporary:
            data = Path(temporary)
            backend, studio = data / "workbench", data / "studio"
            prepare_host(backend_project, backend)
            prepare_host(studio_project, studio)
            backend_url = f"http://127.0.0.1:{free_port()}"
            studio_url = f"http://127.0.0.1:{free_port()}"
            enable_direct_bpmn(backend, studio, backend_url, studio_url)
            hold_path = data / "hold-native-parent-resume"
            env = dict(clean_environment(), HOME=str(data / "home"), DOTNET_CLI_HOME=str(data / "home"),
                       ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME=str(hold_path),
                       **{"Logging__LogLevel__Microsoft.Hosting.Lifetime": "Information"})
            (data / "home").mkdir()
            receipt.update(stage="host-start", studioUrl=studio_url, backendUrl=backend_url, holdPath=str(hold_path))
            with host(backend, "Elsa.Workbench.dll", backend_url, "/health/ready", output / "backend-seed.log", env):
                pass
            seed_operator(backend)
            with host(backend, "Elsa.Workbench.dll", backend_url, "/health/ready", output / "backend.log", env):
                with host(studio, "Elsa.Studio.Web.dll", studio_url, "/studio-runtime.js", output / "studio.log",
                          dict(env, Studio__BackendBaseUrl=backend_url)):
                    receipt["stage"] = "journeys"
                    browser_env = dict(os.environ, ELSA_BPMN_SETTLEMENT_LIVE="1", ELSA_STUDIO_URL=studio_url,
                                       ELSA_WORKBENCH_URL=backend_url, ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME=str(hold_path))
                    command(["pnpm", "exec", "playwright", "test", "--config", "tests/live/playwright.live.config.ts",
                             "tests/live/bpmn-settlement-scope-journey.spec.ts", "--reporter", "line"],
                            output / "journeys.log", cwd=ROOT / "Studio", env=browser_env)
        receipt.update(status="passed", stage="complete")
    except Exception as error:
        receipt["error"] = str(error)
        raise
    finally:
        receipt["finishedAt"] = time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())
        write_json(output / "receipt.json", receipt)
        print(json.dumps(receipt, indent=2), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="New evidence directory outside disposable stores")
    parser.add_argument("--skip-build", action="store_true", help="Reuse already built Workbench and Studio outputs")
    args = parser.parse_args()

    def terminate(_signum, _frame):
        raise InterruptedError("Owned hosts will be stopped")
    signal.signal(signal.SIGTERM, terminate)
    run(args.output.resolve(), skip_build=args.skip_build)
