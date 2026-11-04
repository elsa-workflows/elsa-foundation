#!/usr/bin/env python3
"""Isolated native block prototype host; build both actual products and the read-only metadata observer first."""

from pathlib import Path
import importlib.util, json, os, subprocess, time, urllib.request
import argparse

REPO = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(
    description="Own a fresh normal Studio/Workbench pair for block qualification; accepts restart/finish commands in its output directory."
)
parser.add_argument("--output", type=Path, required=True)
OUT = parser.parse_args().output.resolve()
OUT.mkdir(parents=True, exist_ok=False)
spec = importlib.util.spec_from_file_location(
    "owned_helpers", REPO / "tools/bpmn/run_owned_studio_upgrade.py"
)
h = importlib.util.module_from_spec(spec)
spec.loader.exec_module(h)
source = subprocess.check_output(
    ["git", "rev-parse", "HEAD"], cwd=REPO, text=True
).strip()
if subprocess.check_output(
    ["git", "status", "--porcelain"], cwd=REPO, text=True
).strip():
    raise ValueError("Commit all source before host proof")
backend = OUT / "workbench"
backend.mkdir()
studio = OUT / "studio"
studio.mkdir()
backend_url = "http://127.0.0.1:" + str(h.free_port())
studio_url = "http://127.0.0.1:" + str(h.free_port())
shells, features = h.compose_host_roots(
    backend, studio, backend_url, studio_url, direct_human=False
)
h.write_json(backend / "shells.json", shells)
reader = (
    REPO / "tools/bpmn/DeliveryRecoveryRead/bin/Debug/net10.0/DeliveryRecoveryRead.dll"
)
assemblies = {
    "workbench": REPO
    / "Essentials/apps/Elsa.Workbench/bin/Debug/net10.0/Elsa.Workbench.dll",
    "studio": REPO
    / "Studio/src/apps/Elsa.Studio.Web/bin/Debug/net10.0/Elsa.Studio.Web.dll",
}
products = {
    role: json.loads(
        subprocess.check_output(
            [
                "dotnet",
                str(reader),
                "--inspect-host-products"
                if role == "workbench"
                else "--inspect-studio-products",
                str(dll),
            ],
            text=True,
        )
    )
    for role, dll in assemblies.items()
}
if any(p["sourceCommit"] != source for p in products.values()):
    raise ValueError("Host products are from different source")
assets = {
    str(p.relative_to(REPO)): h.digest(p)
    for root in list((REPO / "Studio/src/essentials").glob("*/wwwroot"))
    + list((REPO / "Extensions").glob("*/studio/*/wwwroot"))
    for p in root.rglob("*")
    if p.is_file()
}
processes = []
logs = []
children = []


def verify_products():
    for role, product_set in products.items():
        for product in product_set["actualProductVersions"]:
            if h.digest(assemblies[role].parent / product["name"]) != product["sha256"]:
                raise ValueError("An owned host product changed during qualification")
    for relative, expected in assets.items():
        if h.digest(REPO / relative) != expected:
            raise ValueError("An owned browser asset changed during qualification")


def start(role, root, url):
    verify_products()
    log = (OUT / (role + "-" + str(len(processes)) + ".log")).open("w")
    logs.append(log)
    child = subprocess.Popen(
        ["dotnet", str(assemblies[role]), "--contentRoot", str(root), "--urls", url],
        cwd=root,
        env={**os.environ, "ASPNETCORE_ENVIRONMENT": "Development"},
        stdout=log,
        stderr=subprocess.STDOUT,
    )
    children.append(child)
    rec = {
        "role": role,
        "pid": child.pid,
        "parentPid": os.getpid(),
        "started": h.process_field(child.pid, "lstart"),
        "executable": h.process_field(child.pid, "comm"),
    }
    processes.append(rec)
    end = time.monotonic() + 180
    while time.monotonic() < end:
        if child.poll() is not None:
            raise RuntimeError(role + " exited")
        try:
            with urllib.request.urlopen(
                url + ("/health/ready" if role == "workbench" else ""), timeout=2
            ) as response:
                if response.status == 200:
                    return child
        except (OSError, TimeoutError):
            pass
        time.sleep(0.25)
    raise TimeoutError(role + " readiness")


def stop(child):
    if child.poll() is not None:
        return
    rec = next(r for r in processes if r["pid"] == child.pid)
    if (
        int(h.process_field(child.pid, "ppid")) != os.getpid()
        or h.process_field(child.pid, "lstart") != rec["started"]
        or h.process_field(child.pid, "comm") != rec["executable"]
    ):
        raise ValueError("Owned process identity changed")
    child.terminate()
    try:
        child.wait(timeout=30)
    except subprocess.TimeoutExpired:
        child.kill()
        child.wait(timeout=10)


try:
    wb = start("workbench", backend, backend_url)
    st = start("studio", studio, studio_url)
    receipt = {
        "source": source,
        "repositoryRoot": str(REPO),
        "controlDirectory": str(OUT),
        "controller": {
            "pid": os.getpid(),
            "started": h.process_field(os.getpid(), "lstart"),
            "executable": h.process_field(os.getpid(), "comm"),
            "command": h.process_field(os.getpid(), "args"),
            "scriptSha256": h.digest(Path(__file__)),
        },
        "backendUrl": backend_url,
        "studioUrl": studio_url,
        "products": products,
        "browserAssets": assets,
        "processes": processes,
        "compositionSha256": h.digest(backend / "shells.json"),
    }
    h.write_json(OUT / "ready.json", receipt)
    print(
        json.dumps(
            {"studioUrl": studio_url, "backendUrl": backend_url, "output": str(OUT)}
        ),
        flush=True,
    )
    while True:
        cmd = OUT / "command.json"
        if not cmd.exists():
            time.sleep(0.25)
            continue
        value = json.loads(cmd.read_text())
        cmd.unlink()
        if value["action"] == "finish":
            break
        if value["action"] == "restart":
            stop(wb)
            wb = start("workbench", backend, backend_url)
            h.write_json(
                OUT / "restarted.json",
                {"source": source, "processes": processes, "ready": True},
            )
        else:
            raise ValueError("Unknown command")
finally:
    for child in reversed(children):
        stop(child)
    for log in logs:
        log.close()
    h.write_json(
        OUT / "closed.json",
        {
            "source": source,
            "processes": processes,
            "allOwnedChildrenStopped": all(c.poll() is not None for c in children),
        },
    )
