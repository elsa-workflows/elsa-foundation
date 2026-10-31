#!/usr/bin/env python3
"""Build committed Connections packages and exercise public APIs in isolated consumers. Python 3.12+."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
MODULE = "Essentials/src/Connections/Persistence/EntityFrameworkCore/Elsa.Connections.Persistence.EntityFrameworkCore.csproj"
PROBE = "Essentials/tests/Connections/Persistence/EntityFrameworkCore/RestartProbe/Program.cs"


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", default="HEAD")
    parser.add_argument("--version", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    out = args.output
    if not out.is_absolute() or out.exists() or out.resolve().is_relative_to(ROOT):
        parser.error("output must be a new absolute directory outside the source checkout")
    out.mkdir(parents=True)
    source = subprocess.check_output(["git", "rev-parse", args.source + "^{commit}"], cwd=ROOT, text=True).strip()
    report = {"source": source, "version": args.version, "status": "InProgress", "stages": [], "packages": []}

    def save():
        (out / "report.json").write_text(json.dumps(report, indent=2) + "\n")

    def run(name, command, cwd, environment=None):
        stage = {"name": name, "command": list(map(str, command)), "status": "Running"}
        report["stages"].append(stage)
        save()
        print(name, flush=True)
        with (out / (name + ".log")).open("w") as log:
            process = subprocess.Popen(command, cwd=cwd, env=environment, stdout=log, stderr=subprocess.STDOUT)
            code = process.wait()
        stage.update(status="Passed" if code == 0 else "Failed", exitCode=code)
        save()
        if code:
            raise RuntimeError(name + " failed; inspect its retained log")

    save()
    try:
        archive = out / "source.tar"
        with archive.open("wb") as stream:
            subprocess.run(["git", "archive", source], cwd=ROOT, stdout=stream, check=True)
        report["sourceArchiveSha256"] = sha(archive)
        producer = out / "producer"
        producer.mkdir()
        with tarfile.open(archive) as bundle:
            bundle.extractall(producer, filter="data")
        projects = set()

        def visit(project):
            project = project.resolve()
            if project in projects:
                return
            if not project.is_relative_to(producer / "Essentials/src"):
                raise RuntimeError("candidate reference is outside the owned source graph")
            projects.add(project)
            for edge in ET.parse(project).findall(".//ProjectReference"):
                visit(project.parent / edge.attrib["Include"].replace("\\", "/"))

        visit(producer / MODULE)
        properties = ["-p:Version=" + args.version, "-p:SourceRevisionId=" + source]
        run("producer-build", ["dotnet", "build", MODULE, "-c", "Release", *properties], producer)
        feed = out / "feed"
        feed.mkdir()
        for index, project in enumerate(sorted(projects)):
            run("pack-" + str(index), ["dotnet", "pack", str(project), "-c", "Release", "--no-build", *properties, "-o", str(feed)], producer)
        pins = {item.attrib["Include"]: item.attrib.get("Version") for item in ET.parse(producer / "Directory.Packages.props").findall(".//PackageVersion") if "Include" in item.attrib}
        consumer = out / "consumer"
        consumer.mkdir()
        shutil.copyfile(producer / PROBE, consumer / "Program.cs")
        project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
        group = ET.SubElement(project, "PropertyGroup")
        for key, value in {"OutputType": "Exe", "TargetFramework": "net10.0", "ImplicitUsings": "enable", "Nullable": "enable"}.items():
            ET.SubElement(group, key).text = value
        references = ET.SubElement(project, "ItemGroup")
        ET.SubElement(references, "PackageReference", Include="Elsa.Connections.Persistence.EntityFrameworkCore", Version=args.version)
        consumer_pins = {}
        for package in ["Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.Extensions.DependencyInjection", "SQLitePCLRaw.bundle_e_sqlite3"]:
            if not pins.get(package) or "$" in pins[package]:
                raise RuntimeError("consumer dependency has no literal reviewed pin: " + package)
            ET.SubElement(references, "PackageReference", Include=package, Version=pins[package])
            consumer_pins[package] = pins[package]
        ET.ElementTree(project).write(consumer / "Consumer.csproj")
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="candidate", value=str(feed))
        ET.SubElement(sources, "add", key="nuget.org", value="https://api.nuget.org/v3/index.json")
        ET.ElementTree(config).write(consumer / "NuGet.Config")
        environment = os.environ.copy()
        environment.update(NUGET_PACKAGES=str(out / "normal-packages"), NUGET_HTTP_CACHE_PATH=str(out / "http-cache"))
        run("consumer-restore", ["dotnet", "restore", "--use-lock-file", "--configfile", "NuGet.Config"], consumer, environment)
        environment["NUGET_PACKAGES"] = str(out / "locked-packages")
        run("consumer-locked", ["dotnet", "restore", "--locked-mode", "--configfile", "NuGet.Config"], consumer, environment)
        run("consumer-build", ["dotnet", "build", "-c", "Release", "--no-restore"], consumer, environment)
        assets = json.loads((consumer / "obj/project.assets.json").read_text())
        resolved = {identity.split("/")[0].lower(): identity.split("/")[1] for identity in assets["libraries"]}
        for package, version in consumer_pins.items():
            if resolved.get(package.lower()) != version:
                raise RuntimeError("consumer dependency differs from the archived profile: " + package)
        report["consumerDependencyPins"] = consumer_pins
        report["nativeSqlitePackages"] = [identity for identity in assets["libraries"] if identity.lower().startswith("sqlitepclraw.")]
        if any(item.get("type") == "project" for item in assets["libraries"].values()) or any(
                framework.get("projectReferences") for framework in assets["project"]["restore"]["frameworks"].values()):
            raise RuntimeError("independent consumer contains a source project reference")
        if any(".design" in key.lower() or ".publishing" in key.lower() for key in assets["libraries"]):
            raise RuntimeError("metadata consumer imports authoring packages")
        for candidate in sorted(feed.glob("*.nupkg")):
            with zipfile.ZipFile(candidate) as package:
                nuspec = ET.fromstring(package.read(next(name for name in package.namelist() if name.endswith(".nuspec"))))
                namespace = {"n": nuspec.tag.split("}")[0].removeprefix("{")}
                identity = nuspec.find("n:metadata/n:id", namespace).text
                version = nuspec.find("n:metadata/n:version", namespace).text
                cache = Path(environment["NUGET_PACKAGES"]) / identity.lower() / version.lower()
                consumed = cache / (identity.lower() + "." + version.lower() + ".nupkg")
                if not consumed.is_file() or sha(candidate) != sha(consumed):
                    raise RuntimeError("consumer did not resolve the retained candidate bytes: " + identity)
                dlls = {}
                for name in package.namelist():
                    if name.startswith("lib/") and name.endswith(".dll"):
                        digest = hashlib.sha256(package.read(name)).hexdigest()
                        if sha(cache / name) != digest:
                            raise RuntimeError("consumer DLL differs from candidate: " + identity)
                        dlls[name] = digest
                report["packages"].append({"id": identity, "version": version, "archiveSha256": sha(candidate), "dllSha256": dlls})
        process_ids = []
        for profile in ["module", "consumer-context"]:
            database = out / (profile + ".db")
            for operation in ["write", "read"]:
                name = profile + "-" + operation
                command = ["dotnet", str(consumer / "bin/Release/net10.0/Consumer.dll"), operation, str(database)]
                if profile == "consumer-context":
                    command.append("--consumer-context")
                run(name, command, consumer, environment)
                text = (out / (name + ".log")).read_text()
                expected = "metadata-committed" if operation == "write" else "metadata-restored"
                if expected not in text or not (match := re.search(r"process-id=(\d+)", text)):
                    raise RuntimeError("consumer has no execution receipt")
                process_ids.append(int(match[1]))
        if len(set(process_ids)) != 4:
            raise RuntimeError("consumer profiles did not execute in four independent processes")
        report.update(status="PassedConnectionsMetadataProfile", sourceReferences=[], processIds=process_ids,
                      authoringClosure=False, credentialsDurability=False, liveModel=False)
    except Exception as exception:
        report.update(status="Failed", failure=str(exception))
        raise
    finally:
        save()
    print(json.dumps({"source": source, "status": report["status"], "packages": len(report["packages"])}))


if __name__ == "__main__":
    main()
