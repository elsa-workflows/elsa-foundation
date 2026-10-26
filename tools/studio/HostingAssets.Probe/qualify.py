#!/usr/bin/env python3
"""Qualify the production Hosting browser payload in isolated published consumers."""
from __future__ import annotations

import argparse
import base64
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import tempfile
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[3]
IDS = ("Elsa.Studio.Core", "Elsa.Studio.Api", "Elsa.Studio.Hosting", "Elsa.Studio.Dashboard")


def check(value, message):
    if not value:
        raise AssertionError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def package_identity(path):
    with zipfile.ZipFile(path) as archive:
        doc = ET.fromstring(archive.read(next(name for name in archive.namelist() if name.endswith(".nuspec"))))
        values = {node.tag.rsplit("}", 1)[-1]: node.text for node in doc.iter()}
        return values["id"], values["version"]


def request(base, path, headers=None):
    try:
        response = urllib.request.urlopen(urllib.request.Request(base + path, headers=headers or {}), timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, response.read(), {key.lower(): value for key, value in response.headers.items()}


@contextmanager
def serve(dotnet, published, env, log):
    with log.open("w") as output:
        process = subprocess.Popen([dotnet, str(published / "Consumer.dll"), "--contentRoot", str(published), "--urls", "http://127.0.0.1:0"],
                                   cwd=published, env=env, stdout=output, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 60
            while True:
                match = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)", log.read_text(errors="replace"))
                if match:
                    yield match[1]
                    break
                check(process.poll() is None and time.monotonic() < deadline, f"Published host did not start: {log}")
                time.sleep(.1)
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=15)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=10)


def main(args):
    root = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="studio-hosting-assets-")).resolve()
    if args.output:
        root.mkdir(parents=True, exist_ok=False)
    report = {"root": str(root), "status": "running", "stage": "setup"}
    print("Artifacts: " + str(root), flush=True)
    dotnet, node = shutil.which("dotnet"), shutil.which("node")
    check(dotnet and node, "Producer/browser driver requires dotnet and node")

    def run(name, command, cwd, env=None, expected=True):
        report["stage"] = name
        log = root / (name + ".log")
        with log.open("w") as output:
            result = subprocess.run(command, cwd=cwd, env=env, stdout=output, stderr=subprocess.STDOUT)
        if expected:
            check(result.returncode == 0, f"{name} failed ({result.returncode}); see {log}")
        return result.returncode

    try:
        feed = root / "feed"
        feed.mkdir()
        revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
        report.update(toolSourceRevision=revision, toolSha256=sha(Path(__file__).read_bytes()),
                      browserDriverSha256=sha(Path(__file__).with_name("browser.mjs").read_bytes()))
        source_assets = {}
        producer = None
        if args.packages:
            found = {}
            for path in args.packages.resolve().rglob("*.nupkg"):
                package_id, version = package_identity(path)
                if package_id in IDS:
                    check(package_id not in found, "Duplicate candidate " + package_id)
                    found[package_id] = (path, version)
            check(set(found) == set(IDS) and len({version for _, version in found.values()}) == 1, "Supply exactly one version of all four production candidates")
            version = next(iter(found.values()))[1]
            for path, _ in found.values():
                shutil.copy2(path, feed / path.name)
            report["producer"] = "supplied production archives; source revision is external provenance"
        else:
            check(not subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all", "--", "Studio", "Extensions"], cwd=REPO, text=True).strip(), "Commit production inputs before qualification")
            version = args.version
            producer = root / "producer"
            producer.mkdir()
            run("source-archive", ["git", "archive", "--format=tar", "--output", str(root / "source.tar"), revision], REPO)
            with tarfile.open(root / "source.tar") as archive:
                archive.extractall(producer)
            for package_id in ("Elsa.Studio.Hosting", "Elsa.Studio.Dashboard"):
                relative = Path("Studio/src/essentials") / package_id / "wwwroot"
                source = REPO / relative
                check(source.is_dir(), "Build the actual frontend first: " + str(source))
                source_assets[package_id] = {path.relative_to(source).as_posix(): path.read_bytes() for path in source.rglob("*") if path.is_file()}
                shutil.copytree(source, producer / relative)
            for package_id in IDS:
                project = producer / "Studio/src/essentials" / package_id / (package_id + ".csproj")
                run("pack-" + package_id, [dotnet, "pack", str(project), "-c", "Release", "-p:Version=" + version,
                                          "-p:SourceRevisionId=" + revision, "-o", str(feed)], producer)
            report.update(producer="owned committed source snapshot with unchanged production project files", producerSourceRevision=revision)
        report["version"] = version
        archives = {package_identity(path)[0]: path for path in feed.glob("*.nupkg")}
        check(set(archives) == set(IDS), "Unexpected candidate archive set")
        expected, hashes = {}, {}
        report["packages"] = {}
        for package_id, path in archives.items():
            with zipfile.ZipFile(path) as archive:
                hashes[package_id] = sha(archive.read("lib/net10.0/" + package_id + ".dll"))
                assets = {name.removeprefix("staticwebassets/"): archive.read(name) for name in archive.namelist()
                          if name.startswith("staticwebassets/") and not name.endswith("/")}
                check(not any(part in name.split("/") for name in archive.namelist() for part in ("Client", "StudioSdk", "StudioUi", "node_modules")), "Frontend source/tool leak in " + package_id)
                if package_id in source_assets:
                    check(assets == source_assets[package_id], "Source/archive browser bytes differ: " + package_id)
                if package_id in ("Elsa.Studio.Hosting", "Elsa.Studio.Dashboard"):
                    check(bool(assets), "Empty production payload: " + package_id)
                    prefix = "/" if package_id == "Elsa.Studio.Hosting" else "/_content/Elsa.Studio.Dashboard/"
                    for relative, data in assets.items():
                        check(".." not in Path(relative).parts, "Invalid archive asset path")
                        url = prefix + relative
                        check(url not in expected, "Duplicate resource URL " + url)
                        expected[url] = data
                report["packages"][package_id] = {"archiveSha256": sha(path.read_bytes()), "dllSha256": hashes[package_id], "assets": len(assets)}
        index = expected["/studio/index.html"]
        match = re.search(rb'<script\s+type="importmap"\s*>(.*?)</script>', index, re.S)
        check(match is not None, "Standard index has no import map")
        imports = json.loads(match[1])["imports"]
        check(all(url in expected for url in imports.values()), "Import map targets unpackaged resources")
        check(any(url.endswith(".woff2") for url in expected) and any(url.endswith(".css") for url in expected), "Missing font/CSS payload")

        cache = root / "cache"
        denied = root / "denied-frontend"
        denied.mkdir()
        audit = root / "frontend-invocations.log"
        for name in ("node", "npm", "pnpm", "npx"):
            script = denied / name
            script.write_text('#!/bin/sh\necho "$0" >> "$STUDIO_FRONTEND_DENIAL_AUDIT"\nexit 71\n')
            script.chmod(0o755)
        env = {key: value for key, value in os.environ.items() if not key.lower().startswith(("studio__", "cshells__", "aspnetcore_"))}
        env.update(PATH=os.pathsep.join([str(denied), str(Path(dotnet).parent), "/usr/bin", "/bin"]), NUGET_PACKAGES=str(cache),
                   NUGET_HTTP_CACHE_PATH=str(root / "http-cache"), STUDIO_FRONTEND_DENIAL_AUDIT=str(audit),
                   ASPNETCORE_ENVIRONMENT="Production", DOTNET_ENVIRONMENT="Production")
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        for key, value in (("candidate", str(feed)), ("nuget.org", "https://api.nuget.org/v3/index.json"), ("CShells", "https://f.feedz.io/valence-works/cshells/nuget/index.json")):
            ET.SubElement(sources, "add", key=key, value=value)
        mapping = ET.SubElement(config, "packageSourceMapping")
        for key, patterns in (("candidate", IDS), ("nuget.org", ("*",)), ("CShells", ("CShells", "CShells.*"))):
            source = ET.SubElement(mapping, "packageSource", key=key)
            for pattern in patterns:
                ET.SubElement(source, "package", pattern=pattern)

        def consumer(name, optout=False, fallback="studio/index.html", collision=False):
            path = root / name
            path.mkdir()
            exclude = ' ExcludeAssets="build;buildTransitive"' if optout else ""
            (path / "Consumer.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup>
<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
</PropertyGroup><ItemGroup><PackageReference Include="Elsa.Studio.Hosting" Version="{version}"{exclude}/>
<PackageReference Include="Elsa.Studio.Dashboard" Version="{version}"/></ItemGroup></Project>''')
            (path / "Program.cs").write_text('''using CShells.DependencyInjection;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Attributes;
using Elsa.Studio.Dashboard;
using Elsa.Studio.Hosting;
var builder = WebApplication.CreateBuilder(args);
var studio = builder.AddElsaStudio(options => { options.ShellName = "Authoring"; options.IndexFile = "''' + fallback + '''"; })
    .ConfigureShells(shells => shells.WithAssemblies(typeof(DashboardStudioFeature).Assembly));
var app = builder.Build();
app.UseElsaStudio();
app.MapElsaStudio();
app.MapGet("/__qualification", () => new {
    contentRoot = app.Environment.ContentRootPath,
    locations = new[] { typeof(StudioBuilder).Assembly.Location, typeof(StudioApiOptions).Assembly.Location,
        typeof(StudioModuleAttribute).Assembly.Location, typeof(DashboardStudioFeature).Assembly.Location }
});
app.Run();
''')
            (path / "shells.json").write_text(json.dumps({"CShells": {"Shells": {"Authoring": {"Features": {"StudioApi": {}, "DashboardStudio": {}}, "Configuration": {"WebRouting": {"Path": ""}}}}}}))
            shutil.copy2(REPO / "global.json", path / "global.json")
            ET.ElementTree(config).write(path / "NuGet.Config", encoding="utf-8", xml_declaration=True)
            if optout or collision:
                document = path / "wwwroot/studio/index.html"
                document.parent.mkdir(parents=True)
                document.write_bytes(b"<!doctype html><title>Custom frontend</title><h1>Application-owned Studio</h1>")
            elif fallback != "studio/index.html":
                document = path / "wwwroot" / fallback
                document.parent.mkdir(parents=True)
                document.write_bytes(index.replace(b"<title>Elsa Studio</title>", b"<title>Custom Studio</title>"))
            run(name + "-restore", [dotnet, "restore", "Consumer.csproj", "--configfile", "NuGet.Config"], path, env)
            run(name + "-locked", [dotnet, "restore", "Consumer.csproj", "--locked-mode", "--configfile", "NuGet.Config"], path, env)
            graph = json.loads((path / "obj/project.assets.json").read_text())
            check({entry["type"] for entry in graph["libraries"].values()} == {"package"}, "Consumer reaches source/project libraries")
            check(len(graph["packageFolders"]) == 1 and Path(next(iter(graph["packageFolders"]))).resolve() == cache.resolve(), "Consumer uses another cache")
            check(not any(key.startswith(("Nuplane", "Elsa.Studio.Web/")) for key in graph["libraries"]), "Unexpected Web/Nuplane dependency")
            for package_id in IDS:
                library = graph["libraries"].get(package_id + "/" + version)
                check(library and library["type"] == "package", "Candidate missing from package closure: " + package_id)
                archive = archives[package_id]
                check(library["sha512"] == base64.b64encode(hashlib.sha512(archive.read_bytes()).digest()).decode(), "Restored archive hash differs")
                check((cache / package_id.lower() / version.lower() / archive.name.lower()).read_bytes() == archive.read_bytes(), "Cache archive differs")
            published = root / (name + "-published")
            code = run(name + "-publish", [dotnet, "publish", "Consumer.csproj", "-c", "Release", "--no-restore", "-o", str(published)], path, env, expected=not collision)
            if collision:
                log = (root / (name + "-publish.log")).read_text()
                check(code != 0 and ("Sequence contains more than one element" in log or "Conflicting assets" in log), "Same-path overlay did not fail specifically at SDK asset collision")
                return path, None
            for package_id in IDS:
                check(sha((published / (package_id + ".dll")).read_bytes()) == hashes[package_id], "Published DLL differs from candidate")
            for url, data in expected.items():
                if optout and url.startswith("/studio/"):
                    continue
                check((published / "wwwroot" / url.lstrip("/")).read_bytes() == data, "Publish asset bytes differ: " + url)
            if optout:
                check(not (published / "wwwroot/studio/vendor/react.js").exists(), "Opt-out retained Hosting vendors")
            return path, published

        profiles = {}
        owned = []
        for name, options in (("standard", {}), ("fallback", {"fallback": "custom-index.html"}), ("collision", {"collision": True}), ("optout", {"optout": True})):
            path, published = consumer(name, **options)
            owned.append(path)
            if published:
                profiles[name] = published
        for path in ([producer] if producer else []) + owned + [feed, cache]:
            path.rename(root / ("hidden-" + path.name))
        if args.omit_published_vendor:
            (profiles["standard"] / "wwwroot/studio/vendor/react.js").unlink()
        report["runtime"] = {}
        for name, published in profiles.items():
            report["stage"] = name + "-runtime"
            with serve(dotnet, published, env, root / (name + "-runtime.log")) as base:
                rows = []
                for url, data in expected.items():
                    if name == "optout" and url.startswith("/studio/"):
                        continue
                    status, actual, headers = request(base, url)
                    check(status == 200 and actual == data, f"HTTP asset mismatch: {url} (status {status})")
                    suffix = Path(url).suffix
                    mime = headers.get("content-type", "").split(";")[0]
                    allowed = {".js": ("text/javascript", "application/javascript"), ".css": ("text/css",), ".html": ("text/html",), ".woff2": ("font/woff2",), ".png": ("image/png",)}.get(suffix)
                    check(not allowed or mime in allowed, "Unexpected asset MIME: " + url + " " + mime)
                    check("no-cache" in headers.get("cache-control", ""), "Stable resource lost revalidation: " + url)
                    check(request(base, url, {"If-None-Match": headers["etag"]})[0] == 304, "ETag revalidation failed: " + url)
                    rows.append({"url": url, "sha256": sha(actual), "mime": mime, "cacheControl": headers["cache-control"]})
                info = json.loads(request(base, "/__qualification")[1])
                check(Path(info["contentRoot"]).resolve() == published.resolve() and all(Path(path).parent.resolve() == published.resolve() for path in info["locations"]), "Runtime reaches outside publish output")
                modules = json.loads(request(base, "/_elsa/studio/modules")[1])
                check(modules["hostVersion"] == version and modules["sdkVersion"] == version, "Host/SDK candidate versions differ")
                check(any(module["id"] == "Elsa.Studio.Dashboard" and module["version"] == version for module in modules["modules"]), "Actual Dashboard manifest/version missing")
                check(request(base, "/_elsa/not-a-real-api")[0] == 404 and request(base, "/studio/not-a-real-file.js")[0] == 404, "Unknown API/asset fell back to HTML")
                if name == "optout":
                    check(b"Application-owned Studio" in request(base, "/")[1] and request(base, "/studio/vendor/react.js")[0] == 404, "Opt-out frontend contract failed")
                else:
                    expected_index = index if name == "standard" else index.replace(b"<title>Elsa Studio</title>", b"<title>Custom Studio</title>")
                    check(request(base, "/")[1] == expected_index and request(base, "/overview")[1] == expected_index, "Root/deep-link document mismatch")
                    status, data, _ = request(base, "/studio/sdk/index.js", {"Range": "bytes=0-9"})
                    check(status == 206 and data == expected["/studio/sdk/index.js"][:10], "Published range request failed")
                    browser_env = dict(os.environ, STUDIO_HOSTING_ASSET_BASE=base, STUDIO_HOSTING_ASSET_OUTPUT=str(root / (name + "-browser.json")))
                    run(name + "-browser", [node, str(Path(__file__).with_name("browser.mjs"))], REPO, browser_env)
                report["runtime"][name] = {"resources": rows, "hostVersion": modules["hostVersion"], "sdkVersion": modules["sdkVersion"], "publishedOnly": True}
        check(not audit.exists() or not audit.read_text().strip(), "Consumer invoked frontend tooling")
        report.update(status="passed", stage="complete", frontendInvocations=0, hiddenInputs=[path.name for path in root.glob("hidden-*")], samePathCollisionRejected=True)
        return 0
    except Exception as error:
        report.update(status="failed", error=str(error))
        return 1
    finally:
        (root / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps({key: report.get(key) for key in ("root", "status", "stage", "error")}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", type=Path, help="Existing production candidate artifact directory")
    parser.add_argument("--version", default="4.0.0-asset-consumer-probe", help="Local candidate version when packing")
    parser.add_argument("--output", type=Path, help="New owned artifact directory")
    parser.add_argument("--omit-published-vendor", action="store_true", help="Negative control: remove published React before HTTP verification")
    raise SystemExit(main(parser.parse_args()))
