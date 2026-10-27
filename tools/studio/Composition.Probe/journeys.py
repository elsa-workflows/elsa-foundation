"""Executed HTTP, browser, authorization, reload and persisted-state journeys."""

import base64
import copy
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
import xml.etree.ElementTree as ET
import zipfile

from artifacts import CUSTOM_ID, FIXTURES, PROFILES, REPO, check, digest
from runtime import MANAGEMENT_KEY, SessionBackend, get_json, profile_environment, request, server


def browser(p, name, base, label, theme=False):
    output = p.root / (label + "-browser.json")
    env = dict(os.environ, STUDIO_COMPOSITION_BASE=base, STUDIO_COMPOSITION_HOST_ID="composition-" + name,
               STUDIO_COMPOSITION_MARKER="module-v1", STUDIO_COMPOSITION_OUTPUT=str(output))
    if theme:
        env.update(STUDIO_COMPOSITION_THEME_NAME="Composition owned", STUDIO_COMPOSITION_THEME_ID="composition-owned")
    p.run(label + "-browser", ["node", str(Path(__file__).with_name("browser.mjs"))], REPO, env)
    return json.loads(output.read_text())


def baseline(p):
    results = {}
    with zipfile.ZipFile(p.root / "hidden-build-feed" / f"{CUSTOM_ID}.1.0.0.nupkg") as archive:
        assets = {suffix: archive.read("staticwebassets/studio/modules/composition/module." + suffix) for suffix in ["js", "css"]}
    for name, (_, constants) in PROFILES.items():
        with server(p.dotnet, p.profiles[name]["published"], p.profiles[name]["env"], p.root / (name + "-baseline-server.log")) as base:
            modules = get_json(base, "/_elsa/studio/modules")
            custom = next(module for module in modules["modules"] if module["id"] == CUSTOM_ID)
            resources = [custom["entry"], *custom["styles"]]
            observed = {}
            for resource in resources:
                suffix = "css" if ".css" in resource else "js"
                status, body, headers = request(base, resource)
                check(status == 200 and body == assets[suffix], "Custom packaged asset mismatch: " + resource)
                check("css" in headers.get("content-type", "") if suffix == "css" else "javascript" in headers.get("content-type", ""), "Custom asset MIME mismatch")
                observed[resource] = digest(body)
            check(set(observed) and len(resources) == 2, "Custom JS/CSS inventory is incomplete")
            witness = get_json(base, "/__qualification/generation")
            check(witness["value"] == "module-v1", "Real module service is absent")
            check(get_json(base, "/__qualification/read")["coldAtReady"], "Profile did not start cold")
            if "THEMES" not in constants:
                for path, method in [("/_elsa/theme-store", "GET"), ("/_elsa/theme-store/themes/absent", "PUT")]:
                    check(request(base, path, method=method)[0] == 404, "Absent Themes route was installed")
            if "ADMINISTRATION" not in constants:
                for path, method in [("/_elsa/module-management/registry", "GET"), ("/_elsa/module-management/feeds", "POST")]:
                    check(request(base, path, method=method)[0] == 404, "Absent administration route was installed")
            results[name] = {"resources": observed, "generation": witness, "browser": browser(p, name, base, name)}
    return results


def actual_reference_web(p):
    published = p.root / "reference-web-published"
    check((published / "Elsa.Studio.Web.dll").exists(), "An actual published reference Web distribution is required")
    env = profile_environment(p.root, "reference")
    env.update(Studio__Auth__Enabled="false", Studio__HostId="composition-reference-web", Studio__BackendBaseUrl="",
               Studio__BackendServerBaseUrl="", Themes__StoreFile="state/themes.json", Themes__AssetsDirectory="state/materials")
    with server(p.dotnet, published, env, p.root / "actual-reference-web-server.log", assembly="Elsa.Studio.Web.dll", ready=False) as base:
        modules = get_json(base, "/_elsa/studio/modules")
        check(len(modules["modules"]) >= 14, "Reference distribution module set is incomplete")
        output = p.root / "actual-reference-web-browser.json"
        driver = dict(os.environ, STUDIO_HOSTING_ASSET_BASE=base, STUDIO_HOSTING_ASSET_OUTPUT=str(output),
                      STUDIO_HOSTING_RUNTIME_HOST_ID="composition-reference-web")
        p.run("actual-reference-web-browser", ["node", str(REPO / "tools/studio/HostingAssets.Probe/browser.mjs")], REPO, driver)
        return {"moduleIds": [module["id"] for module in modules["modules"]], "browser": json.loads(output.read_text()),
                "composition": "actual unchanged distribution, separate from external reference-style consumer"}


def authenticated_env(p, name, backend):
    env = profile_environment(p.root, name)
    env.update(Studio__Auth__Enabled="true", Studio__BackendBaseUrl="https://browser.invalid", Studio__BackendServerBaseUrl=backend.base,
               Studio__BackendModuleManagementApiKey=MANAGEMENT_KEY)
    return env


def cold_authorization(p):
    results = {}
    with SessionBackend() as backend:
        for name in PROFILES:
            rows = []
            for label, token, expected in [("anonymous", None, 401), ("invalid", "invalid-owned-token", 401),
                                           ("unprivileged", "composition-no-permissions", 403), ("permitted", "composition-owner", 200)]:
                count = len(backend.snapshot())
                with server(p.dotnet, p.profiles[name]["published"], authenticated_env(p, name, backend), p.root / (name + "-cold-" + label + ".log")) as base:
                    status, body, _ = request(base, "/__qualification/read", bearer=token)
                    check(status == expected, f"{name} cold {label}: expected {expected}, observed {status}")
                    calls = backend.snapshot()[count:]
                    check(len(calls) == (0 if token is None else 1), "Cold request unexpectedly warmed/repeated session validation")
                    check(all(not call["managementKeyPresent"] for call in calls), "Session validation received the management key")
                    if status == 200:
                        check(json.loads(body)["coldAtReady"], "First request was not cold")
                    check(MANAGEMENT_KEY.encode() not in request(base, "/studio-runtime.js")[1], "Browser runtime exposed management key")
                    rows.append({"case": label, "status": status, "sessionCalls": len(calls)})
            results[name] = rows
    return results


def policies(p):
    results = {}
    with SessionBackend() as backend:
        with server(p.dotnet, p.profiles["reference"]["published"], authenticated_env(p, "reference", backend), p.root / "policy-server.log") as base:
            hub = "/_elsa/studio/diagnostics/console-logs/hub/negotiate?negotiateVersion=1"
            check(request(base, hub, method="POST")[0] == 401, "Anonymous hub negotiation was admitted")
            status, body, _ = request(base, hub + "&access_token=composition-owner", method="POST")
            check(status == 200 and json.loads(body).get("connectionId"), "Trusted hub query bearer was refused")
            check(request(base, "/__qualification/read?access_token=composition-owner")[0] == 401, "Ordinary read admitted query token")
            check(request(base, "/__qualification/mutate?access_token=composition-owner", method="POST")[0] == 401, "Ordinary mutation admitted query token")
            check(request(base, "/__qualification/mutate", method="POST", bearer="composition-reader")[0] == 403, "Read-only bearer mutated")
            check(request(base, "/__qualification/mutate", method="POST", bearer="composition-owner")[0] == 204, "Manage bearer was refused")
            status = get_json(base, "/_elsa/studio/backend-management/status", bearer="composition-owner")
            check(status["status"] == "available" and MANAGEMENT_KEY not in json.dumps(status), "Privileged bridge unavailable or exposed key")
            records = backend.snapshot()
            sessions = [row for row in records if row["path"].endswith("/identity/session")]
            privileged = [row for row in records if not row["path"].endswith("/identity/session")]
            check(sessions and all(not row["managementKeyPresent"] for row in sessions), "Session key contamination")
            check(privileged and all(row["managementKeyMatches"] for row in privileged), "Privileged request lost the configured server key")
            results["authorization"] = {"trustedHubQuery": True, "ordinaryQueryRefused": True, "readManageBoundary": True,
                                         "privilegedStatus": status["status"], "sessionCalls": len(sessions), "privilegedCalls": len(privileged)}
    for name in ["minimal", "administration"]:
        env = profile_environment(p.root, name)
        env["Studio__Api__DisabledModuleIds__0"] = CUSTOM_ID
        with server(p.dotnet, p.profiles[name]["published"], env, p.root / (name + "-suppression.log")) as base:
            check(not any(module["id"] == CUSTOM_ID for module in get_json(base, "/_elsa/studio/modules")["modules"]), "Suppressed module remained in client manifest")
            registry = get_json(base, "/_elsa/studio/module-registry")
            module = next(row for row in registry["modules"] if row["id"] == CUSTOM_ID)
            check(module["status"] == "disabled", "Suppressed module diagnostic is missing")
            check(get_json(base, "/__qualification/generation")["value"] == "module-v1", "UI suppression changed the server feature")
            results[name] = {"clientManifestSuppressed": True, "registryStatus": "disabled", "serverFeatureStillActive": True}
    return results


def theme_definition():
    keys = ["primary", "primaryForeground", "secondary", "secondaryForeground", "accent", "accentForeground", "success", "successForeground",
            "warning", "warningForeground", "danger", "dangerForeground", "background", "foreground", "card", "cardForeground", "muted", "mutedForeground",
            "border", "input", "sidebar", "sidebarForeground", "sidebarActive", "sidebarActiveForeground", "ring"]
    palette = {key: "#f9fafb" if key.endswith("Foreground") or key == "foreground" else "#1f2937" for key in keys}
    palette.update(primary="#2563eb", accent="#2563eb", success="#15803d", warning="#a16207", danger="#b91c1c", ring="#60a5fa",
                   chartColors=["#2563eb", "#15803d", "#a16207", "#b91c1c", "#7c3aed"])
    return {"id": "composition-owned", "name": "Composition owned", "source": "custom", "version": 1, "enabled": True, "published": True,
            "modes": {"light": copy.deepcopy(palette), "dark": copy.deepcopy(palette)}}


def theme_restart(p):
    results = {}
    image = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==")
    for name in ["themes", "reference"]:
        published, env = p.profiles[name]["published"], p.profiles[name]["env"]
        source = published / "sources/themes.json"
        original = digest(source.read_bytes())
        with server(p.dotnet, published, env, p.root / (name + "-theme-before.log")) as base:
            status, body, _ = request(base, "/_elsa/theme-store/themes/composition-owned", theme_definition(), "PUT")
            check(status == 200, "Managed theme save failed: " + str(status) + " " + body.decode())
            check(request(base, "/_elsa/theme-store/default", {"themeId": "composition-owned"}, "PUT")[0] == 200, "Default save failed")
            boundary = "composition-owned-boundary"
            data = ("--" + boundary + '\r\nContent-Disposition: form-data; name="file"; filename="pixel.png"\r\nContent-Type: image/png\r\n\r\n').encode() + image + ("\r\n--" + boundary + "--\r\n").encode()
            status, body, _ = request(base, "/_elsa/theme-store/assets", data, "POST", content_type="multipart/form-data; boundary=" + boundary)
            check(status == 200, "Material upload failed")
            asset = json.loads(body)
            path = "/_elsa/theme-store/assets/" + asset["id"] + "/content"
            check(request(base, path)[1] == image, "Material upload bytes differ")
        with server(p.dotnet, published, env, p.root / (name + "-theme-after.log")) as base:
            store = get_json(base, "/_elsa/theme-store")
            check(store["defaultThemeId"] == "composition-owned" and any(row["id"] == "composition-owned" for row in store["themes"]), "Managed theme/default lost after restart")
            status, body, headers = request(base, path)
            check(status == 200 and body == image and headers["content-type"] == "image/png", "Material lost after restart")
            check(digest(source.read_bytes()) == original, "Read-only theme source changed")
            results[name] = {"default": store["defaultThemeId"], "materialSha256": digest(image), "sourceSha256": original,
                             "browser": browser(p, name, base, name + "-theme-restarted", theme=True)}
    return results


def missing_asset_control(p):
    published = p.profiles["minimal"]["published"]
    asset = published / "wwwroot/_content/Acme.Studio.Composition/studio/modules/composition/module.js"
    original = asset.read_bytes()
    asset.rename(asset.with_name("module.js.control-hidden"))
    try:
        with server(p.dotnet, published, p.profiles["minimal"]["env"], p.root / "missing-custom-server.log") as base:
            entry = next(row["entry"] for row in get_json(base, "/_elsa/studio/modules")["modules"] if row["id"] == CUSTOM_ID)
            status, _, _ = request(base, entry)
            check(status == 404, "Missing custom asset did not fail specifically HTTP404")
    finally:
        asset.with_name("module.js.control-hidden").rename(asset)
    check(asset.read_bytes() == original, "Restored custom asset changed")
    with server(p.dotnet, published, p.profiles["minimal"]["env"], p.root / "restored-custom-server.log") as base:
        status, body, _ = request(base, entry)
        check(status == 200 and body == original, "Restored custom asset HTTP mismatch")
        restored = browser(p, "minimal", base, "restored-custom")
    return {"negative": {"status": "failed", "stage": "custom-asset", "httpStatus": 404, "entry": entry, "expectedFailureObserved": True},
            "restored": {"status": "passed", "sha256": digest(original), "browser": restored}}


def stream_client(p):
    source = p.root / "stream-client-source"
    source.mkdir()
    version = next(node.attrib["Version"] for node in ET.parse(REPO / "Studio/Directory.Packages.props").getroot().iter("PackageVersion")
                   if node.attrib.get("Include") == "Microsoft.AspNetCore.SignalR.Client")
    (source / "StreamClient.csproj").write_text((FIXTURES / "StreamClient.csproj.in").read_text().replace("__SIGNALR_VERSION__", version))
    shutil.copy2(FIXTURES / "StreamClient.cs.in", source / "Program.cs")
    shutil.copy2(REPO / "global.json", source / "global.json")
    (source / "NuGet.Config").write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    env = dict(os.environ, NUGET_PACKAGES=str(p.root / "stream-client-cache"), NUGET_HTTP_CACHE_PATH=str(p.root / "stream-client-http-cache"))
    for label, args in [("restore", ["restore", "--configfile", "NuGet.Config"]), ("locked", ["restore", "--configfile", "NuGet.Config", "--locked-mode"]),
                        ("publish", ["publish", "-c", "Release", "--no-restore", "-o", str(p.root / "stream-client-published")])]:
        p.run("stream-client-" + label, [p.dotnet, *args], source, env)
    source.rename(p.root / "hidden-stream-client-source")
    (p.root / "stream-client-cache").rename(p.root / "hidden-stream-client-cache")


def live_stream(p, label, control=False):
    published = p.profiles["reference"]["published"]
    feed = published / "runtime-feed"
    archives = p.root / "hidden-build-feed"
    def reset(version):
        for path in feed.glob("*.nupkg"):
            check(path.name.startswith(CUSTOM_ID + "."), "Unexpected owned runtime feed content")
            path.unlink()
        shutil.copy2(archives / f"{CUSTOM_ID}.{version}.nupkg", feed)
    reset("1.0.0")
    with SessionBackend() as backend:
        env = authenticated_env(p, "reference", backend)
        if control:
            env["Qualification__DisableAutomaticReload"] = "true"
        server_log = p.root / (label + "-server.log")
        with server(p.dotnet, published, env, server_log) as base:
            log = p.root / (label + "-client.log")
            with log.open("w") as output:
                client = subprocess.Popen([p.dotnet, str(p.root / "stream-client-published/StreamClient.dll"), base], cwd=p.root / "stream-client-published",
                                          env=env, stdin=subprocess.PIPE, stdout=output, stderr=subprocess.STDOUT, text=True)
                try:
                    deadline, before = time.monotonic() + 45, None
                    while before is None:
                        for line in log.read_text(errors="replace").splitlines():
                            if line.startswith("{"):
                                row = json.loads(line)
                                if row.get("stage") == "before":
                                    before = row
                        check(client.poll() is None and time.monotonic() < deadline, "Stream pre-marker did not arrive: " + str(log))
                        time.sleep(.1)
                    reset("2.0.0")
                    status, body, _ = request(base, "/__qualification/reconcile", method="POST", bearer="composition-owner")
                    outcome = json.loads(body)
                    check(status == 200 and outcome.get("runResult", {}).get("isDegraded") is False, "Actual package reconcile failed/degraded")
                    client.stdin.write("after\n")
                    client.stdin.flush()
                    code = client.wait(timeout=35)
                    if control:
                        check(code != 0 and "Actual package reconciliation did not replace the serving generation" in log.read_text(), "Wrong disabled-reload failure")
                        return {"status": "failed", "stage": "serving-generation", "exitCode": code, "expectedFailureObserved": True, "reconcile": outcome}
                    check(code == 0, "Live connection/generation assertion failed: " + str(log))
                    rows = [json.loads(line) for line in log.read_text().splitlines() if line.startswith("{")]
                    result = next(row for row in rows if row.get("stage") == "passed")
                    check(result["cleanDisconnect"] and "ObjectDisposedException" not in server_log.read_text(errors="replace"), "Disposed provider or unclean disconnect")
                    return {"status": "passed", "observed": result, "reconcile": outcome}
                finally:
                    if client.poll() is None:
                        client.terminate()
                        try:
                            client.wait(timeout=10)
                        except subprocess.TimeoutExpired:
                            client.kill()
                            client.wait(timeout=10)
                    reset("1.0.0")
