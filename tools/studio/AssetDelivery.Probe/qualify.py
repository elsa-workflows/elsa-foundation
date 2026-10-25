#!/usr/bin/env python3
"""Pack actual prebuilt Studio assets and qualify an isolated, published .NET-only host."""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
import zipfile
from xml.sax.saxutils import quoteattr

REPO = Path(__file__).resolve().parents[3]
VERSION = "0.0.1-local"
PREFIX = "Studio.AssetQualification."


def check(value: bool, message: str) -> None:
    if not value:
        raise AssertionError(message)


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def run(command: list[str], log: Path, env: dict[str, str], cwd: Path, *, success: bool = True) -> int:
    with log.open("w") as output:
        result = subprocess.run(command, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT)
    if success:
        check(result.returncode == 0, f"Command failed ({result.returncode}); see {log}")
    return result.returncode


def project(package: str, base: str) -> str:
    return f'''<Project Sdk="Microsoft.NET.Sdk.Razor">
<PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>{PREFIX}{package}</PackageId>
<Version>{VERSION}</Version><StaticWebAssetBasePath>{base}</StaticWebAssetBasePath>
<AddRazorSupportForMvc>true</AddRazorSupportForMvc><Authors>Studio qualification</Authors>
<Description>Temporary Studio asset delivery qualification; not a production package</Description>
</PropertyGroup><ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup></Project>'''


def request(base: str, path: str, headers: dict[str, str] | None = None) -> tuple[int, bytes, dict[str, str]]:
    try:
        with urllib.request.urlopen(urllib.request.Request(base + path, headers=headers or {}), timeout=10) as response:
            return response.status, response.read(), dict(response.headers)
    except urllib.error.HTTPError as response:
        return response.code, response.read(), dict(response.headers)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="new artifact directory; defaults to a fresh temporary directory")
    parser.add_argument("--omit-published-vendor", action="store_true", help="negative control: delete the published React shim before verification")
    args = parser.parse_args()
    root = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="studio-asset-proof-"))
    if args.output:
        root.mkdir(parents=True, exist_ok=False)
    print(f"Artifacts: {root}", flush=True)
    env = dict(os.environ)
    dotnet = shutil.which("dotnet")
    node = shutil.which("node")
    check(dotnet is not None and node is not None, "Producer/browser automation needs dotnet and node on PATH.")
    subprocess.run(["git", "diff", "--exit-code", "HEAD", "--", "Studio", "Extensions"], cwd=REPO, check=True)
    check(not subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all", "--", "Studio", "Extensions"], cwd=REPO, text=True).strip(), "Commit frontend input changes before archive qualification")
    source_revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
    inputs = {
        "Hosting": (REPO / "Studio/src/apps/Elsa.Studio.Web/wwwroot", "/"),
        "Dashboard": (REPO / "Studio/src/essentials/Elsa.Studio.Dashboard/wwwroot", "_content/Elsa.Studio.Dashboard"),
    }
    check((inputs["Hosting"][0] / "studio/vendor/react.js").is_file(), "Build the actual Web shell first.")
    check((inputs["Dashboard"][0] / "studio/modules/dashboard/module.js").is_file(), "Build Dashboard first.")
    feed = root / "feed"
    feed.mkdir()
    expected: dict[str, bytes] = {}
    archives = []
    producers = root / "producers"
    for name, (source, base) in inputs.items():
        target = producers / name
        target.mkdir(parents=True)
        shutil.copytree(source, target / "wwwroot")
        csproj = target / f"{name}.csproj"
        csproj.write_text(project(name, base))
        run([dotnet, "pack", str(csproj), "-c", "Release", "-o", str(feed)], root / f"pack-{name}.log", env, root)
        package = feed / f"{PREFIX}{name}.{VERSION}.nupkg"
        files = {p.relative_to(source).as_posix(): p.read_bytes() for p in source.rglob("*") if p.is_file()}
        check(bool(files), f"Empty {name} input")
        with zipfile.ZipFile(package) as archive:
            packed = {p.removeprefix("staticwebassets/"): archive.read(p) for p in archive.namelist() if p.startswith("staticwebassets/") and not p.endswith("/")}
            check(packed == files, f"{name} archive payload differs from actual built assets")
        for path, data in files.items():
            url = "/" + ("" if base == "/" else base + "/") + path
            check(url not in expected, f"Duplicate URL {url}")
            expected[url] = data
        archives.append({"id": PREFIX + name, "version": VERSION, "sha256": digest(package.read_bytes()), "assetCount": len(files)})
    index = expected["/studio/index.html"].decode()
    match = re.search(r'<script\s+type="importmap"\s*>(.*?)</script>', index, re.S)
    check(match is not None, "Actual index has no import map")
    imports = json.loads(match[1])["imports"]
    for url in imports.values():
        check(url in expected, f"Import map refers to an unpackaged URL: {url}")
    check(any(url.endswith(".woff2") for url in expected), "No actual font assets")
    check(any(url.endswith(".css") for url in expected), "No actual CSS assets")

    consumer = root / "consumer"
    consumer.mkdir()
    csproj = consumer / "Consumer.csproj"
    csproj.write_text(f'''<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup><ItemGroup><PackageReference Include="{PREFIX}Hosting" Version="{VERSION}" />
<PackageReference Include="{PREFIX}Dashboard" Version="{VERSION}" /></ItemGroup></Project>''')
    custom = consumer / "wwwroot/custom-index.html"
    custom.parent.mkdir()
    custom_bytes = index.replace("<title>Elsa Studio</title>", "<title>Custom Studio</title>").encode()
    check(custom_bytes != expected["/studio/index.html"], "Actual title changed; requalify custom-document example")
    custom.write_bytes(custom_bytes)
    (consumer / "Program.cs").write_text('''var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var files = new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache" };
app.UseStaticFiles(files);
app.MapGet("/__asset-qualification", () => Results.Content("<!doctype html><title>Asset qualification</title>", "text/html"));
app.MapGet("/_elsa/{**path}", () => Results.NotFound());
app.MapFallbackToFile("/custom-studio/{**path:nonfile}", "custom-index.html", files);
app.MapFallbackToFile("studio/index.html", files);
app.Run();
''')
    config = root / "NuGet.Config"
    config.write_text(f'<configuration><packageSources><clear/><add key="local" value={quoteattr(str(feed))}/></packageSources></configuration>')
    cache = root / "cache"
    denied = root / "denied-frontend"
    denied.mkdir()
    audit = root / "frontend-invocations.log"
    for name in ["node", "npm", "pnpm", "npx"]:
        script = denied / name
        # Paths are not interpolated into shell code; the audit path is passed through the environment.
        script.write_text('#!/bin/sh\necho "$0" >> "$STUDIO_FRONTEND_DENIAL_AUDIT"\nexit 71\n')
        script.chmod(0o755)
    isolated = dict(env, PATH=os.pathsep.join([str(denied), str(Path(dotnet).parent), "/usr/bin", "/bin"]),
                    NUGET_PACKAGES=str(cache), STUDIO_FRONTEND_DENIAL_AUDIT=str(audit), ASPNETCORE_ENVIRONMENT="Production", DOTNET_ENVIRONMENT="Production")
    run([dotnet, "restore", str(csproj), "--configfile", str(config)], root / "consumer-restore.log", isolated, consumer)
    run([dotnet, "restore", str(csproj), "--locked-mode", "--configfile", str(config)], root / "consumer-locked.log", isolated, consumer)
    assets = json.loads((consumer / "obj/project.assets.json").read_text())
    check(len(assets["packageFolders"]) == 1 and Path(next(iter(assets["packageFolders"]))).resolve() == cache.resolve(), "Consumer uses another package cache/fallback")
    check({x["type"] for x in assets["libraries"].values()} == {"package"}, "Consumer reaches project/source references")
    check(set(assets["libraries"]) == {PREFIX + name + "/" + VERSION for name in inputs}, "Unexpected consumer dependency graph")
    for library, metadata in assets["libraries"].items():
        package = feed / f"{library.replace('/', '.')}.nupkg"
        check(metadata["sha512"] == base64.b64encode(hashlib.sha512(package.read_bytes()).digest()).decode(), f"Consumer archive mismatch: {library}")
        cached = cache / library.lower() / (library.replace('/', '.').lower() + ".nupkg")
        check(cached.read_bytes() == package.read_bytes(), f"Isolated cache differs: {library}")
    published = root / "published"
    run([dotnet, "publish", str(csproj), "-c", "Release", "--no-restore", "-o", str(published)], root / "consumer-publish.log", isolated, consumer)
    for url, data in expected.items():
        check((published / "wwwroot" / url.lstrip("/")).read_bytes() == data, f"Publish bytes differ: {url}")

    # Observe the SDK's real collision behavior, rather than promising arbitrary package-file overrides.
    override = root / "override-consumer"
    override.mkdir()
    for path in [csproj, consumer / "Program.cs"]:
        shutil.copyfile(path, override / path.name)
    duplicate = override / "wwwroot/studio/index.html"
    duplicate.parent.mkdir(parents=True)
    duplicate.write_text("<!doctype html><title>Consumer override</title>")
    result = run([dotnet, "publish", str(override / csproj.name), "-c", "Release", "-o", str(root / "override-published"), "--configfile", str(config)], root / "override-publish.log", isolated, override, success=False)
    collision_log = (root / "override-publish.log").read_text()
    check(result != 0 and "GenerateStaticWebAssetsDevelopmentManifest" in collision_log and "Sequence contains more than one element" in collision_log,
          "SDK duplicate-path behavior changed; requalify/document the override contract")

    # Published execution must remain independent of producer directories, source projects and NuGet cache.
    for directory in [producers, consumer, override, cache, feed]:
        directory.rename(root / (directory.name + "-hidden"))
    check(not audit.exists(), "Consumer restore/publish invoked a frontend tool")
    with socket.socket() as port_probe:
        port_probe.bind(("127.0.0.1", 0))
        port = port_probe.getsockname()[1]
    base = f"http://127.0.0.1:{port}"
    runtime = dict(isolated, ASPNETCORE_URLS=base)
    server_log = (root / "server.log").open("w")
    process = subprocess.Popen([dotnet, str(published / "Consumer.dll")], cwd=published, env=runtime, stdout=server_log, stderr=subprocess.STDOUT)
    try:
        deadline = time.monotonic() + 30
        while True:
            check(process.poll() is None, f"Published host stopped; see {root / 'server.log'}")
            try:
                if request(base, "/studio/index.html")[0] == 200:
                    break
            except (urllib.error.URLError, TimeoutError):
                pass
            check(time.monotonic() < deadline, "Published host did not become ready")
            time.sleep(0.1)
        def verify() -> list[dict[str, str | int]]:
            rows = []
            for url, data in expected.items():
                status, actual, headers = request(base, url)
                check(status == 200 and actual == data, f"HTTP asset mismatch: {url} (status {status})")
                check(headers.get("Cache-Control") == "no-cache", f"Missing revalidation: {url}")
                kind = headers.get("Content-Type", "")
                if url.endswith(".js"):
                    check("javascript" in kind, f"Bad JS MIME: {url}")
                if url.endswith(".css"):
                    check(kind.startswith("text/css"), f"Bad CSS MIME: {url}")
                if url.endswith((".woff", ".woff2")):
                    check("font" in kind or "woff" in kind, f"Bad font MIME: {url}")
                rows.append({"url": url, "status": status, "contentType": kind, "sha256": digest(actual)})
            return rows
        if args.omit_published_vendor:
            (published / "wwwroot/studio/vendor/react.js").unlink()
        rows = verify()
        check(request(base, "/")[1] == expected["/studio/index.html"], "Root SPA fallback differs")
        check(request(base, "/studio/custom-route")[1] == expected["/studio/index.html"], "Studio SPA fallback differs")
        check(request(base, "/custom-studio/example")[1] == custom_bytes, "Consumer custom fallback document failed")
        check(request(base, "/_elsa/missing")[0] == 404 and request(base, "/studio/missing.js")[0] == 404, "Unknown API/asset returned fallback HTML")
        range_status, body, _ = request(base, "/studio/vendor/react.js", {"Range": "bytes=0-15"})
        check(range_status == 206 and body == expected["/studio/vendor/react.js"][:16], "Range response differs")
        victim = published / "wwwroot/studio/vendor/react.js"
        victim.unlink()
        detected = False
        try:
            verify()
        except AssertionError as error:
            check("/studio/vendor/react.js" in str(error), "Control failed for an unrelated asset")
            detected = True
        check(detected, "Missing-vendor control falsely passed")
        victim.write_bytes(expected["/studio/vendor/react.js"])
        check(len(verify()) == len(rows), "Restored publish did not pass")
        browser_env = dict(env, STUDIO_ASSET_PROBE_BASE=base, STUDIO_ASSET_PROBE_IMPORTS=json.dumps(imports), STUDIO_ASSET_PROBE_OUTPUT=str(root / "browser.json"))
        run([node, str(Path(__file__).with_name("browser.mjs"))], root / "browser.log", browser_env, REPO / "Studio")
        check(not audit.exists(), "Published host invoked a frontend tool")
        check(process.poll() is None, "Owned published host stopped during verification")
        evidence = {"status": "passed", "sourceRevision": source_revision, "sdk": subprocess.check_output([dotnet, "--version"], text=True).strip(),
                    "probeSha256": digest(Path(__file__).read_bytes()), "browserProbeSha256": digest(Path(__file__).with_name("browser.mjs").read_bytes()),
                    "frontendLockSha256": digest((REPO / "Studio/pnpm-lock.yaml").read_bytes()), "archives": archives, "httpAssets": rows, "consumerGraph": list(assets["libraries"]),
                    "frontendInvocations": 0, "hiddenInputs": ["producers", "consumer", "override-consumer", "cache", "feed"],
                    "override": "duplicate studio/index.html rejected by SDK; separately named custom fallback document coexists", "negativeControl": "missing published react.js fails; restoring bytes passes",
                    "browser": json.loads((root / "browser.json").read_text())}
        (root / "evidence.json").write_text(json.dumps(evidence, indent=2))
        print(json.dumps({"status": "passed", "httpAssetCount": len(rows), "evidence": str(root / "evidence.json")}), flush=True)
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)
        server_log.close()


if __name__ == "__main__":
    main()
