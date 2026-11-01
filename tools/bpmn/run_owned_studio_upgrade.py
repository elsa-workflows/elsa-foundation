#!/usr/bin/env python3
"""Own an isolated source-built Studio/Workbench pair for a recovery journey.

Original/layout restore an actual legacy capture; direct-human/direct-etl use fresh isolated stores.
The browser performs the authoring/publication/run steps. This controller only restores
whole stores, starts its own children, records read-only observations and closes them.
Write {"action":"restart"} or {"action":"finish"} to OUTPUT/command.json.
"""
from __future__ import annotations

import argparse
import gzip
import importlib.util
import json
import os
import re
from pathlib import Path
import shutil
import socket
import signal
import subprocess
import sys
import tempfile
import time
import urllib.request
import urllib.parse

_spec = importlib.util.spec_from_file_location("qualify_hosting", Path(__file__).with_name("qualify-hosting.py"))
_qualification = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_qualification)
Api, REPO, digest, process_field, write_json = (_qualification.Api, _qualification.REPO,
    _qualification.digest, _qualification.process_field, _qualification.write_json)


def free_port():
    with socket.socket() as server:
        server.bind(("127.0.0.1", 0))
        return server.getsockname()[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--family", choices=("original", "layout", "direct-human", "direct-etl"), required=True)
    parser.add_argument("--product-source", help="Reuse unchanged products while committing only this journey's harness/evidence repairs.")
    parser.add_argument("--workbench-assembly", type=Path, required=True)
    parser.add_argument("--studio-assembly", type=Path, required=True)
    parser.add_argument("--reader-assembly", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    source_revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    if subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"], cwd=REPO, text=True).strip():
        raise ValueError("Commit the qualification source before recording exact-source Studio evidence.")
    for field in ("workbench_assembly", "studio_assembly", "reader_assembly"):
        setattr(args, field, getattr(args, field).resolve(strict=True))
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    root = Path(tempfile.mkdtemp(prefix="elsa-studio-upgrade-"))
    backend_root = root / "workbench"; backend_root.mkdir()
    studio_root = root / "studio"; studio_root.mkdir()
    baseline = REPO / "specs/1017-direct-bpmn-hosting/evidence/baseline"
    direct_human = args.family in ("direct-human", "direct-etl")
    direct_etl = args.family == "direct-etl"
    stores = ("runtime", "design", "etl-runtime", "etl-design") if direct_etl else ("runtime", "design")
    family = "studio" if args.family == "original" else "multi-instance-and-studio"
    matrix = None if direct_human else json.loads((baseline / "manifest.json").read_text())
    inputs = {}
    for store in (() if direct_human else ("runtime", "design")):
        name = f"{family}/{store}.sqlite.gz"
        asset = next(asset for asset in matrix["assets"] if asset["path"] == name)
        if digest(baseline / name) != asset["sha256"]:
            raise ValueError("An original Studio input differs from the frozen capture.")
        inputs[name] = asset["sha256"]
        with gzip.open(baseline / name, "rb") as source, (backend_root / f"{store}.db").open("wb") as target:
            shutil.copyfileobj(source, target)
    backend_source = REPO / "Essentials/apps/Elsa.Workbench"
    studio_source = REPO / "Studio/src/apps/Elsa.Studio.Web"
    for source, target in ((backend_source, backend_root), (studio_source, studio_root)):
        for name in ("appsettings.json", "appsettings.Development.json", "shells.json"):
            shutil.copyfile(source / name, target / name)
        if (source / "nuplane-management.json").exists():
            shutil.copyfile(source / "nuplane-management.json", target / "nuplane-management.json")
        (target / "packages").mkdir()
    backend_url = f"http://127.0.0.1:{free_port()}"
    studio_url = f"http://127.0.0.1:{free_port()}"
    shells = json.loads((backend_root / "shells.json").read_text())
    features = shells["CShells"]["Shells"]["default"]["Features"]
    if direct_human:
        # Same single-node composition as the actual authored-publication hosting test.
        features["ActivitiesBpmnHosting"] = {"AdmitNewExecutions": True}
        features["ActivitiesBpmnHostingApi"] = {}
        features["ActivitiesBpmnDirectPublishing"] = {}
        for name in ("WorkflowsRuntimeDistributed", "WorkflowsRuntimeDistributedEntityFrameworkCorePersistence", "WorkflowsRuntimeDistributedCommandTransportEntityFrameworkCorePersistence"):
            features.pop(name, None)
    features["WorkflowsRuntimeEntityFrameworkCore"]["ConnectionString"] = "Data Source=runtime.db"
    for name in ("ActivitiesDesignEntityFrameworkCore", "WorkflowsDesignEntityFrameworkCore", "WorkflowsPublishingEntityFrameworkCore"):
        if name in features:
            features[name]["ConnectionString"] = "Data Source=design.db"
    if "FoundationIdentityAspNetCoreIdentity" in features:
        features["FoundationIdentityAspNetCoreIdentity"]["AllowedReturnUrlOrigins"] = [studio_url]
    clr_source_binding = {"readOnly": True, "sourceIds": []} if direct_human else json.loads(subprocess.check_output(["dotnet", str(args.reader_assembly),
        "--read-clr-source-identities", str(backend_root / "design.db")], text=True))
    if clr_source_binding.get("readOnly") is not True:
        raise ValueError("The captured CLR source identity observer is not read-only.")
    source_ids = clr_source_binding["sourceIds"]
    if source_ids and "ClrActivityReconciliation" in features:
        if len(source_ids) != 1:
            raise ValueError("Multiple captured CLR sources require explicit source-to-folder bindings.")
        options = features["ClrActivityReconciliation"].setdefault("Options", {})
        if options.get("SourceId") and options["SourceId"] != source_ids[0]:
            raise ValueError("The configured CLR source identity differs from the captured catalog authority.")
        options["SourceId"] = source_ids[0]
        # Identity is opaque configuration; FolderPath continues to scan the current app directory.
    write_json(backend_root / "shells.json", shells)
    config = json.loads((backend_root / "appsettings.json").read_text())
    config["Cors"]["AllowedOrigins"] = [studio_url]
    write_json(backend_root / "appsettings.json", config)
    config = json.loads((studio_root / "appsettings.json").read_text())
    config["Studio"]["BackendBaseUrl"] = backend_url
    write_json(studio_root / "appsettings.json", config)
    products = json.loads(subprocess.check_output(["dotnet", str(args.reader_assembly), "--inspect-host-products", str(args.workbench_assembly)], text=True))
    source = products["sourceCommit"]
    studio_products = json.loads(subprocess.check_output(["dotnet", str(args.reader_assembly), "--inspect-studio-products", str(args.studio_assembly)], text=True))
    product_source = args.product_source or source_revision
    if source != product_source or studio_products["sourceCommit"] != product_source:
        raise ValueError("Build both actual host products from this exact committed qualification source.")
    if product_source != source_revision:
        subprocess.run(["git", "merge-base", "--is-ancestor", product_source, source_revision], cwd=REPO, check=True)
        changed = subprocess.check_output(["git", "diff", "--name-only", product_source, source_revision], cwd=REPO, text=True).splitlines()
        allowed = ("tools/bpmn/", "e2e-tests/etl/", "specs/1184-direct-bpmn-studio/")
        if any(not name.startswith(allowed) for name in changed):
            raise ValueError("Product reuse is limited to this journey's harness and evidence changes; rebuild changed product inputs.")
    browser_assets = []
    for wwwroot in sorted(list((REPO / "Studio/src/essentials").glob("*/wwwroot")) + list((REPO / "Extensions").glob("*/studio/*/wwwroot"))):
        for asset in sorted(wwwroot.rglob("*")):
            if asset.is_file(): browser_assets.append({"file": str(asset.relative_to(REPO)), "sha256": digest(asset)})
    children = []
    logs = []
    processes = []

    def start(assembly, directory, url, label, command=None):
        log = (root / f"{label}-{len(processes)}.log").open("w")
        logs.append(log)
        child = subprocess.Popen(command or ["dotnet", str(assembly), "--contentRoot", str(directory), "--urls", url],
            cwd=directory, env={**os.environ, "ASPNETCORE_ENVIRONMENT": "Development"}, stdout=log, stderr=subprocess.STDOUT)
        children.append(child)
        processes.append({"role": label, "processId": child.pid, "controllerProcessId": os.getpid(),
            "started": process_field(child.pid, "lstart"), "executable": process_field(child.pid, "comm"),
            "assemblyName": assembly.name, "assemblySha256": digest(assembly)})
        return child

    def owned(child):
        record = next(item for item in processes if item["processId"] == child.pid)
        if (child.poll() is not None or int(process_field(child.pid, "ppid")) != os.getpid() or
                process_field(child.pid, "lstart") != record["started"] or
                process_field(child.pid, "comm") != record["executable"]):
            raise ValueError("The owned host identity changed.")

    def stop(child):
        if child.poll() is None:
            owned(child)
            child.terminate()
            try: child.wait(timeout=30)
            except subprocess.TimeoutExpired:
                child.kill(); child.wait(timeout=10)

    def ready(child, url):
        end = time.monotonic() + 180
        while time.monotonic() < end:
            if child.poll() is not None:
                raise RuntimeError("An owned host exited before readiness; see its private log.")
            try:
                with urllib.request.urlopen(url, timeout=2) as response:
                    if response.status == 200: return
            except (OSError, TimeoutError): pass
            time.sleep(.2)
        raise TimeoutError("An owned host did not become ready.")

    def snapshot(label, execution_ids=()):
        assets = {}
        # Keep the coupled stores at one owned-host cut and use the same SQLite
        # provider as Workbench. Python only reads the resulting closed evidence.
        owned(backend); os.kill(backend.pid, signal.SIGSTOP)
        try:
            end = time.monotonic() + 5
            while "T" not in process_field(backend.pid, "stat"):
                if time.monotonic() >= end: raise TimeoutError("The owned Workbench did not pause.")
                time.sleep(.02)
            for name in stores:
                backup = root / f"{label}-{name}.sqlite"
                observer = json.loads(subprocess.check_output(["dotnet", str(args.reader_assembly), "--backup-sqlite",
                    str(backend_root / f"{name}.db"), str(backup)], text=True, timeout=30))
                if observer.get("readOnly") is not True or observer.get("snapshotSha256") != digest(backup):
                    raise ValueError("The provider backup lacks its read-only closed-store receipt.")
                assets[name] = {"observer": observer, "captureCut": "same-owned-workbench-paused-across-provider-backups"}
            if direct_etl:
                captured_files = []
                destination = output / f"{label}-etl-files"
                destination.mkdir()
                for folder in ("main-output", "error-output", "snapshots"):
                    for path in sorted((root / "etl" / folder).rglob("*")):
                        if not path.is_file(): continue
                        relative = path.relative_to(root / "etl")
                        target = destination / relative; target.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copyfile(path, target)
                        captured_files.append({"file": str(relative), "sha256": digest(target), "bytes": target.stat().st_size})
                with urllib.request.urlopen(etl_url + "/__fixture/state", timeout=5) as response:
                    write_json(output / f"{label}-http-effects.json", json.load(response))
                write_json(output / f"{label}-etl-files.json", {"captureCut": "same-owned-workbench-paused", "files": captured_files})
        finally:
            owned(backend); os.kill(backend.pid, signal.SIGCONT)
        for name in stores:
            backup = root / f"{label}-{name}.sqlite"
            compressed = output / f"{label}-{name}.sqlite.gz"
            with backup.open("rb") as raw, gzip.open(compressed, "wb") as target: shutil.copyfileobj(raw, target)
            assets[name].update(file=compressed.name, databaseSha256=digest(backup), gzipSha256=digest(compressed))
        if direct_human:
            typed = []
            for execution_id in execution_ids:
                observed = json.loads(subprocess.check_output(["dotnet", str(args.reader_assembly), "--read-bpmn-direct", str(root / f"{label}-runtime.sqlite"), execution_id], text=True))
                if observed.get("readOnly") is not True:
                    raise ValueError("The BPMN snapshot observer must be read-only.")
                typed.append(observed)
            write_json(output / f"{label}-typed-read.json", {"readOnly": True, "models": typed})
        else:
            subprocess.run([sys.executable, str(REPO / "e2e-tests/bpmn/verify_published_contracts.py"), "--database", str(root / f"{label}-design.sqlite"),
                "--source-revision", source, "--closed-snapshot", "--output", str(output / f"{label}-frozen-contract.json")], check=True, stdout=subprocess.DEVNULL)
        return assets

    def observation(label):
        api = Api(backend_url); api.login("admin", "Password123!")
        if direct_human:
            executions = api.request("/bpmn/executions?limit=50")
            if executions.get("nextContinuationToken") is not None:
                raise ValueError("The isolated direct-human journey unexpectedly exceeds one model-state page.")
            views = {item["executionId"]: api.request("/bpmn/executions/" + urllib.parse.quote(item["executionId"], safe="") + "/inspection") for item in executions["items"]}
            write_json(output / f"{label}-bpmn-views.json", {"admission": api.request("/bpmn/admission"),
                "publicationsFirstPage": api.request("/bpmn/publications?limit=50"), "executions": executions, "inspections": views})
            return snapshot(label, views.keys())
        receipts = ["studio/receipt.json"]
        if args.family == "layout": receipts.append("studio/layout-receipt.json")
        views = {json.loads((baseline / name).read_text())["runId"]: api.view(json.loads((baseline / name).read_text())["runId"]) for name in receipts}
        write_json(output / f"{label}-legacy-views.json", views)
        return snapshot(label)

    try:
        etl_url = None
        if direct_etl:
            fixture_script = REPO / "e2e-tests/etl/EtlHttpFixture.mjs"
            ready_path = root / "etl-http-ready.json"
            fixture = start(fixture_script, root, "", "etl-http", ["node", str(fixture_script), "--ready-file", str(ready_path),
                "--lookup-snapshot-root", str(root / "etl" / "snapshots"), "--effect-root", str(root / "etl" / "main-output")])
            deadline = time.monotonic() + 15
            while not ready_path.exists():
                if fixture.poll() is not None: raise RuntimeError("The owned ETL dependency exited before readiness.")
                if time.monotonic() >= deadline: raise TimeoutError("The owned ETL dependency did not become ready.")
                time.sleep(.1)
            etl_url = "http://127.0.0.1:" + str(json.loads(ready_path.read_text())["port"])
            etl_root = root / "etl"
            for name in ("main-output", "error-output", "snapshots"): (etl_root / name).mkdir(parents=True)
            lookup = etl_root / "lookup.csv"; lookup.write_text("id\n1\n2\n3\n4\n")
            features["EtlDesign"] = {}
            features["EtlDesignEntityFrameworkCore"] = {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-design.db;Pooling=False"}
            features["EtlDesignApi"] = {"CursorSigningKey": "owned-direct-etl-design-cursor-key-for-isolated-e2e"}
            features["EtlRuntimeEntityFrameworkCore"] = {"Provider": "Sqlite", "ConnectionString": "Data Source=etl-runtime.db;Pooling=False"}
            features["EtlRuntimeApi"] = {"CursorSigningKey": "owned-direct-etl-runtime-cursor-key-for-isolated-e2e"}
            features["EtlWorkflowsDesign"] = {}
            features["EtlWorkflowsRuntime"] = {}
            features["EtlRuntime"] = {"SnapshotRoot": str(etl_root / "snapshots"), "FileBindings": [
                {"TenantId": "default", "PersistencePartition": "default", "Alias": alias, "Role": role, "Revision": alias + "-v1", "Path": str(etl_root / folder), "IsCurrent": True, "AtomicPublication": True}
                for alias, role, folder in (("output", "MainOutput", "main-output"), ("errors", "ErrorOutput", "error-output"))
            ] + [{"TenantId": "default", "PersistencePartition": "default", "Alias": "lookup", "Role": "Lookup", "Revision": "lookup-v1", "Path": str(lookup),
                "IsCurrent": True, "ImmutableInput": True, "ExpectedSha256": digest(lookup)}], "HttpBindings": [{"TenantId": "default", "PersistencePartition": "default", "Alias": "http-source", "Role": "Source", "Revision": "http-source-v1",
                "Endpoint": etl_url + "/source/shard-a/page", "AllowedPaths": ["/source/shard-a/page", "/source/shard-b/page"], "AllowHttp": True,
                "Shards": [{"Id": shard, "SeedUri": etl_url + "/source/" + shard + "/page"} for shard in ("shard-a", "shard-b")]}]}
            write_json(backend_root / "shells.json", shells)
        backend = start(args.workbench_assembly, backend_root, backend_url, "workbench")
        ready(backend, backend_url + "/health/ready")
        studio = start(args.studio_assembly, studio_root, studio_url, "studio")
        ready(studio, studio_url)
        stages = {"recovered": observation("recovered")}
        write_json(output / "ready.json", {"family": args.family, "sourceRevision": source, "harnessSourceRevision": source_revision, "etlFixtureUrl": etl_url, "backendUrl": backend_url, "studioUrl": studio_url,
            "privateRoot": str(root), "baselineAssets": inputs, "hostProducts": products, "studioProducts": studio_products, "clrSourceBinding": clr_source_binding, "processes": processes})
        print(json.dumps({"studioUrl": studio_url, "backendUrl": backend_url, "output": str(output)}), flush=True)
        while True:
            command = output / "command.json"
            if not command.exists(): time.sleep(.2); continue
            value = json.loads(command.read_text()); command.unlink()
            action = value["action"]
            label = value.get("label", "restarted") if direct_human and action in ("restart", "checkpoint") else "finished" if direct_human else "restarted"
            if direct_human and (not isinstance(label, str) or not re.fullmatch(r"[a-z][a-z0-9-]{0,50}", label) or label in stages):
                raise ValueError("A direct-human checkpoint needs a new safe label; evidence is never overwritten.")
            if action == "restart":
                stop(backend)
                backend = start(args.workbench_assembly, backend_root, backend_url, "workbench")
                ready(backend, backend_url + "/health/ready")
                stages[label] = observation(label)
                write_json(output / (label + ".json"), {"processId": backend.pid, "ready": True})
            elif action == "checkpoint" and direct_human:
                stages[label] = observation(label)
                write_json(output / (label + ".json"), {"processId": backend.pid, "ready": True})
            elif action == "restore-etl-dependency" and direct_etl:
                request = urllib.request.Request(etl_url + "/__fixture/restore-source", data=b"{}", headers={"Content-Type": "application/json"})
                with urllib.request.urlopen(request, timeout=5) as response: restored = json.load(response)
                write_json(output / (label + ".json"), restored)
            elif action == "finish":
                stages["finished"] = observation("finished")
                write_json(output / "process.json", {"family": args.family, "sourceRevision": source, "harnessSourceRevision": source_revision, "etlFixtureUrl": etl_url, "baselineAssets": inputs,
                    "hostProducts": products, "studioProducts": studio_products, "browserAssets": browser_assets, "clrSourceBinding": clr_source_binding, "processes": processes, "stages": stages,
                    "studioAssemblySha256": digest(args.studio_assembly), "controllerSha256": digest(Path(__file__))})
                break
            else: raise ValueError("Unknown owned Studio command.")
        for name, original in inputs.items():
            if digest(baseline / name) != original: raise ValueError("The original Studio input changed.")
    finally:
        for child in reversed(children): stop(child)
        for log in logs: log.close()
        # Keep isolated private stores/logs available for diagnosis; never touch another host.


if __name__ == "__main__":
    main()
