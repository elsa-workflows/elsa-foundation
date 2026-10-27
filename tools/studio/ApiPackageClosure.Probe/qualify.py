#!/usr/bin/env python3
"""Restore, publish and run a host with only a Studio.Api NuGet reference."""

import argparse
from copy import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
import time
import urllib.request
import xml.etree.ElementTree as ET
import zipfile


REPO = Path(__file__).resolve().parents[3]
IDS = ("Elsa.Studio.Core", "Elsa.Studio.Api")


def identity(path):
    with zipfile.ZipFile(path) as package:
        names = [name for name in package.namelist() if name.endswith(".nuspec")]
        if len(names) != 1:
            raise RuntimeError(f"Expected one nuspec in {path}")
        doc = ET.fromstring(package.read(names[0]))
        values = {node.tag.rsplit("}", 1)[-1]: node.text for node in doc.iter()}
        return values["id"], values["version"]


def sha(data):
    return hashlib.sha256(data).hexdigest()


def omit_core_dependency(package, originals):
    with zipfile.ZipFile(package) as archive:
        entries = archive.infolist()
        if any(entry.filename.lower() == ".signature.p7s" for entry in entries):
            raise RuntimeError("Dependency control requires unsigned local candidates; signed archives are not rewritten")
        nuspec = next(entry for entry in entries if entry.filename.endswith(".nuspec"))
        original_xml = archive.read(nuspec)
        document = ET.fromstring(original_xml)
        # Keep the manifest's default namespace readable by NuGet after rewriting metadata.
        if document.tag.startswith("{"):
            ET.register_namespace("", document.tag[1:].split("}", 1)[0])
        removed = []
        for parent in document.iter():
            for child in list(parent):
                if child.tag.rsplit("}", 1)[-1] == "dependency" and child.get("id", "").lower() == IDS[0].lower():
                    removed.append(dict(child.attrib))
                    parent.remove(child)
        if not removed:
            raise RuntimeError("Dependency control cannot omit an already missing Api-to-Core dependency")
        changed_xml = ET.tostring(document, encoding="utf-8", xml_declaration=True)
        temporary = package.with_suffix(".mutant")
        with zipfile.ZipFile(temporary, "w") as output:
            for entry in entries:
                output.writestr(copy(entry), changed_xml if entry.filename == nuspec.filename else archive.read(entry))
        with zipfile.ZipFile(temporary) as output:
            for entry in entries:
                if entry.filename != nuspec.filename and output.read(entry.filename) != archive.read(entry):
                    raise RuntimeError("Dependency control changed a non-nuspec payload: " + entry.filename)
        original_hash = sha(package.read_bytes())
    temporary.replace(package)
    return {"originalArchives": str(originals), "originalArchiveSha256": original_hash,
            "mutatedArchiveSha256": sha(package.read_bytes()), "removedRequirements": removed,
            "unchangedPayloadEntries": len(entries) - 1, "payloadUnchanged": True}


def qualify(args):
    root = Path(tempfile.mkdtemp(prefix="studio-api-closure-")).resolve()
    feed, consumer, cache, published = [root / name for name in ("feed", "consumer", "cache", "published")]
    feed.mkdir()
    consumer.mkdir()
    report = {"root": str(root), "status": "running", "stage": "producer", "packages": {},
              "toolSha256": sha(Path(__file__).read_bytes()),
              "producer": "supplied production archives; source revision is external provenance" if args.packages else "current repository Core/Api production projects"}
    if args.omit_api_core_dependency:
        report["negativeControl"] = {"kind": "omit-api-core-dependency", "omittedDependency": IDS[0],
                                     "expectedStage": "publish", "expectedFailureObserved": False}
    denied, audit = root / "denied-tools", root / "frontend-invocations.log"
    denied.mkdir()
    for command in ("node", "npm", "pnpm", "npx"):
        executable = denied / command
        executable.write_text('#!/bin/sh\nprintf "%s\\n" "' + command + '" >> "$STUDIO_FRONTEND_DENIAL_AUDIT"\nexit 93\n')
        executable.chmod(0o755)
    child_env = dict(os.environ, NUGET_PACKAGES=str(cache), NUGET_HTTP_CACHE_PATH=str(root / "http-cache"),
                     ASPNETCORE_ENVIRONMENT="Production", DOTNET_ENVIRONMENT="Production",
                     PATH=str(denied) + os.pathsep + os.environ.get("PATH", ""), STUDIO_FRONTEND_DENIAL_AUDIT=str(audit))

    def run(name, command, cwd, env=None):
        report["stage"] = name
        with (root / (name + ".log")).open("w") as log:
            result = subprocess.run(command, cwd=cwd, env=env, stdout=log, stderr=subprocess.STDOUT)
        if result.returncode:
            report["failureLogTail"] = (root / (name + ".log")).read_text(errors="replace").splitlines()[-15:]
            raise RuntimeError(f"{name} exited {result.returncode}; see {root / (name + '.log')}")

    try:
        if args.packages:
            found = {}
            for path in Path(args.packages).resolve().rglob("*.nupkg"):
                package_id, version = identity(path)
                if package_id in IDS:
                    if package_id in found:
                        raise RuntimeError(f"Duplicate {package_id} package")
                    found[package_id] = (path, version)
            if set(found) != set(IDS) or len({value[1] for value in found.values()}) != 1:
                raise RuntimeError("Expected exactly one Core and Api package at the same version")
            version = next(iter(found.values()))[1]
            for path, _ in found.values():
                shutil.copy2(path, feed / path.name)
        else:
            version = args.version
            for package_id in IDS:
                project = REPO / "Studio/src/essentials" / package_id / (package_id + ".csproj")
                run("pack-" + package_id, ["dotnet", "pack", str(project), "-c", "Release", "-p:Version=" + version, "-o", str(feed)], REPO)

        packages = {identity(path)[0]: path for path in feed.glob("*.nupkg")}
        if set(packages) != set(IDS):
            raise RuntimeError("Unexpected producer package set")
        report["version"] = version
        if args.omit_api_core_dependency:
            report["stage"] = "mutation"
            originals = root / "original-packages"
            originals.mkdir()
            for path in packages.values():
                shutil.copy2(path, originals / path.name)
            report["negativeControl"].update(omit_core_dependency(packages[IDS[1]], originals))
        for package_id, path in packages.items():
            with zipfile.ZipFile(path) as archive:
                report["packages"][package_id] = {"archiveSha256": sha(path.read_bytes()), "dllSha256": sha(archive.read(f"lib/net10.0/{package_id}.dll"))}

        (consumer / "Consumer.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile></PropertyGroup>
  <ItemGroup><PackageReference Include="Elsa.Studio.Api" Version="{version}" /></ItemGroup>
</Project>
''')
        shutil.copy2(REPO / "global.json", consumer / "global.json")
        (consumer / "Program.cs").write_text('''using Elsa.Studio.Api.Extensions;
using Elsa.Studio.Api.Features;
using Elsa.Studio.Api.Options;
using Elsa.Studio.Core.Attributes;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddElsaStudioApi(options => options.HostVersion = "9.8.7");
var app = builder.Build();
app.MapGet("/package-probe", (IOptions<StudioApiOptions> options) => new {
    hostVersion = options.Value.HostVersion,
    apiTypeCount = typeof(StudioApiFeature).Assembly.GetTypes().Length,
    contracts = typeof(StudioApiFeature).GetInterfaces().Select(type => type.FullName).ToArray(),
    apiLocation = typeof(StudioApiFeature).Assembly.Location,
    coreLocation = typeof(StudioModuleAttribute).Assembly.Location
});
app.Run();
''')
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        for key, value in (("candidate", str(feed)), ("nuget.org", "https://api.nuget.org/v3/index.json"), ("CShells", "https://f.feedz.io/valence-works/cshells/nuget/index.json")):
            ET.SubElement(sources, "add", key=key, value=value)
        mapping = ET.SubElement(config, "packageSourceMapping")
        for key, patterns in (("candidate", IDS), ("nuget.org", ("*",)), ("CShells", ("CShells", "CShells.*"))):
            node = ET.SubElement(mapping, "packageSource", key=key)
            for pattern in patterns:
                ET.SubElement(node, "package", pattern=pattern)
        ET.ElementTree(config).write(consumer / "NuGet.Config", encoding="utf-8", xml_declaration=True)
        run("restore", ["dotnet", "restore", "Consumer.csproj", "--configfile", "NuGet.Config", "--no-http-cache"], consumer, child_env)
        run("locked-restore", ["dotnet", "restore", "Consumer.csproj", "--locked-mode", "--configfile", "NuGet.Config", "--no-http-cache"], consumer, child_env)
        assets = json.loads((consumer / "obj/project.assets.json").read_text())
        report["candidateLibraryTypes"] = {package_id: assets["libraries"].get(f"{package_id}/{version}", {}).get("type") for package_id in IDS}
        report["restoredCoreLibraries"] = [key for key in assets["libraries"] if key.split("/")[0].lower() == IDS[0].lower()]
        if {entry["type"] for entry in assets["libraries"].values()} != {"package"}:
            raise RuntimeError("Consumer reaches source/project libraries")
        if len(assets["packageFolders"]) != 1 or Path(next(iter(assets["packageFolders"]))).resolve() != cache.resolve():
            raise RuntimeError("Consumer uses another package cache")
        report["cachedArchiveSha256"] = {}
        for package_id, path in packages.items():
            if report["candidateLibraryTypes"][package_id] == "package":
                cached = cache / package_id.lower() / version.lower() / path.name.lower()
                actual = sha(cached.read_bytes())
                if actual != report["packages"][package_id]["archiveSha256"]:
                    raise RuntimeError(f"{package_id} restored archive bytes differ")
                report["cachedArchiveSha256"][package_id] = actual
        run("publish", ["dotnet", "publish", "Consumer.csproj", "-c", "Release", "--no-restore", "-o", str(published)], consumer, child_env)
        if set(report["candidateLibraryTypes"].values()) != {"package"}:
            raise RuntimeError("Candidate libraries were not both restored as NuGet packages")
        for package_id in IDS:
            expected = report["packages"][package_id]["dllSha256"]
            cached = cache / package_id.lower() / version.lower() / "lib/net10.0" / (package_id + ".dll")
            if sha(cached.read_bytes()) != expected or sha((published / (package_id + ".dll")).read_bytes()) != expected:
                raise RuntimeError(f"{package_id} archive/cache/publish DLL bytes differ")
        # Runtime gets only the published payload, outside the producer and consumer trees.
        for path in (feed, consumer, cache):
            path.rename(root / ("hidden-" + path.name))
        report["stage"] = "runtime"
        with (root / "runtime.log").open("w") as log:
            process = subprocess.Popen(["dotnet", str(published / "Consumer.dll"), "--urls", "http://127.0.0.1:0"], cwd=published, env=child_env, stdout=log, stderr=subprocess.STDOUT)
            try:
                deadline = time.monotonic() + 45
                while True:
                    match = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)", (root / "runtime.log").read_text())
                    if match:
                        with urllib.request.urlopen(match[1] + "/package-probe", timeout=10) as response:
                            payload = json.load(response)
                        break
                    if process.poll() is not None or time.monotonic() >= deadline:
                        raise RuntimeError(f"Published host did not start; see {root / 'runtime.log'}")
                    time.sleep(0.1)
                if payload["hostVersion"] != "9.8.7" or payload["apiTypeCount"] < 1 or "CShells.AspNetCore.Features.IWebShellFeature" not in payload["contracts"]:
                    raise RuntimeError(f"Unexpected public API result: {payload}")
                for key in ("apiLocation", "coreLocation"):
                    if Path(payload[key]).parent.resolve() != published.resolve():
                        raise RuntimeError(f"Runtime loaded {key} outside published payload")
                report["runtime"] = payload
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=10)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait(timeout=10)
        if audit.exists() and audit.read_text().strip():
            raise RuntimeError("Consumer invoked frontend tooling")
        report.update(status="passed", stage="complete", hiddenInputs=["feed", "consumer", "cache"])
        return 0
    except Exception as error:
        report.update(status="failed", error=str(error))
        return 1
    finally:
        report["frontendInvocations"] = len(audit.read_text().splitlines()) if audit.exists() else 0
        if args.omit_api_core_dependency:
            diagnostic = (root / "publish.log").read_text(errors="replace") if (root / "publish.log").exists() else ""
            report["negativeControl"]["expectedFailureObserved"] = (
                report["status"] == "failed" and report["stage"] == "publish"
                and report["negativeControl"].get("payloadUnchanged", False)
                and report.get("candidateLibraryTypes", {}).get(IDS[1]) == "package"
                and not report.get("restoredCoreLibraries", ["unknown"])
                and "CS0234" in diagnostic and "Core" in diagnostic and "Elsa.Studio" in diagnostic
                and report["frontendInvocations"] == 0)
        (root / "report.json").write_text(json.dumps(report, indent=2) + "\n")
        print(json.dumps(report, indent=2), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", help="Existing candidate artifact directory; otherwise pack Core and Api")
    parser.add_argument("--version", default="4.0.0-reuse-probe", help="Local producer version when packing")
    parser.add_argument("--omit-api-core-dependency", action="store_true", help="Negative control: omit Core from a private unsigned Api nuspec copy; the expected product failure still exits 1")
    raise SystemExit(qualify(parser.parse_args()))
