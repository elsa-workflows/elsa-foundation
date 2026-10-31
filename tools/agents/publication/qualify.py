"""Build private candidate packages and independent specialist research consumers.

Research only: nothing is pushed, published remotely, or connected to a model.
The output directory must be new, absolute, and outside the repository.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
FIXTURES = Path(__file__).resolve().parent
ROOT_PACKAGES = [
    "Elsa.Workflows.Publishing.Api", "Elsa.Activities.Design.Persistence.EntityFrameworkCore",
    "Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore",
    "Elsa.Workflows.Runtime.Api", "Elsa.Activities.Runtime", "Elsa.Serialization.SystemText",
    "Elsa.Activities.Primitives", "Elsa.Activities.Bpmn",
]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    args = parser.parse_args()
    out = args.output
    if not out.is_absolute() or out.exists() or out.resolve().is_relative_to(ROOT):
        parser.error("output must be a new absolute directory outside the checkout")
    out.mkdir(parents=True)
    out = out.resolve()
    feed = out / "feed"
    feed.mkdir()
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    report = {"source": source, "version": args.version, "liveModel": False, "stages": []}

    consumer_env = dict(os.environ, NUGET_PACKAGES=str(out / "consumer-package-cache"), NUGET_HTTP_CACHE_PATH=str(out / "consumer-http-cache"))

    def run(name, command, cwd, expected_marker=None):
        print(name, flush=True)
        with (out / (name + ".log")).open("w") as log:
            result = subprocess.run(command, cwd=cwd, stdout=log, stderr=subprocess.STDOUT,
                                    env=None if cwd == producer else consumer_env)
        recognized = expected_marker is not None and result.returncode != 0 and expected_marker in (out / (name + ".log")).read_text()
        report["stages"].append({"name": name, "exitCode": result.returncode,
                                 "status": "Failed" if result.returncode else "Passed", "expectedFailureRecognized": recognized})
        (out / "report.json").write_text(json.dumps(report, indent=2))
        if (expected_marker is None and result.returncode) or (expected_marker is not None and not recognized):
            raise RuntimeError(name + " failed; inspect " + str(out / (name + ".log")))

    # A private producer snapshot includes the uncommitted research fixtures only below;
    # product code is exactly the recorded Git commit.
    archive = out / "source.tar"
    with archive.open("wb") as stream:
        subprocess.run(["git", "archive", source], cwd=ROOT, stdout=stream, check=True)
    producer = out / "producer"
    producer.mkdir()
    with tarfile.open(archive) as bundle:
        bundle.extractall(producer, filter="data")
    projects = {p.stem: p for p in (producer / "Essentials/src").rglob("*.csproj")}
    selected, seen = [], set()

    def visit(project):
        project = project.resolve()
        if project in seen:
            return
        seen.add(project)
        if not project.is_relative_to(producer):
            raise RuntimeError("project reference escapes producer")
        for ref in ET.parse(project).iter("ProjectReference"):
            include = ref.attrib["Include"].replace("\\", "/")
            if "$(" in include or ";" in include:
                raise RuntimeError("nonliteral producer reference: " + include)
            visit(project.parent / include)
        selected.append(project)

    for package in ROOT_PACKAGES:
        visit(projects[package])
    solution = ET.Element("Solution")
    for project in selected:
        ET.SubElement(solution, "Project", Path=project.relative_to(producer).as_posix())
    slnx = producer / "Specialists.Candidates.slnx"
    ET.ElementTree(solution).write(slnx, encoding="unicode")
    props = ["-p:Version=" + args.version, "-p:SourceRevisionId=" + source]
    run("producer-build", ["dotnet", "build", str(slnx), "-c", "Release", *props], producer)
    for i, project in enumerate(selected):
        if any((n.text or "").lower() == "false" for n in ET.parse(project).iter("IsPackable")):
            continue
        run("pack-" + str(i) + "-" + project.stem,
            ["dotnet", "pack", str(project), "-c", "Release", "--no-build", *props, "-o", str(feed)], producer)
    packages = {}
    import zipfile
    for package in feed.glob("*.nupkg"):
        with zipfile.ZipFile(package) as bundle:
            manifest = ET.fromstring(bundle.read(next(n for n in bundle.namelist() if n.endswith(".nuspec"))))
            metadata = manifest.find("{*}metadata")
            identity = metadata.find("{*}id").text
            version = metadata.find("{*}version").text
            if version != args.version:
                raise RuntimeError("unexpected package version: " + identity)
            packages[identity] = hashlib.sha256(package.read_bytes()).hexdigest()
    report["productPackages"] = packages

    def configure(directory):
        document = ET.parse(producer / "NuGet.config")
        sources = document.getroot().find("packageSources")
        ET.SubElement(sources, "add", key="specialist-candidate", value=str(feed))
        mapping = document.getroot().find("packageSourceMapping")
        group = ET.SubElement(mapping, "packageSource", key="specialist-candidate")
        for package in sorted(packages):
            ET.SubElement(group, "package", pattern=package)
        ET.SubElement(group, "package", pattern="Acme.Specialists.*")
        document.write(directory / "NuGet.Config", encoding="unicode")

    for name in ["Runtime", "Design", "Author", "Consumer", "BpmnConsumer", "Lifecycle"]:
        directory = out / name
        directory.mkdir()
        for fixture in (FIXTURES / name).iterdir():
            if fixture.suffix == ".template":
                (directory / fixture.stem).write_text(fixture.read_text().replace("__VERSION__", args.version))
            else:
                shutil.copy2(fixture, directory / fixture.name)
        report.setdefault("fixtures", {})[name] = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (FIXTURES / name).iterdir()}
        if name == "BpmnConsumer":
            shutil.copy2(FIXTURES / "Consumer/Program.cs", directory / "Program.cs")
            report["fixtures"][name]["Program.cs"] = hashlib.sha256((directory / "Program.cs").read_bytes()).hexdigest()
        configure(directory)
        project = next(directory.glob("*.csproj"))
        run(name + "-restore", ["dotnet", "restore", str(project), "--use-lock-file"], directory)
        run(name + "-locked-restore", ["dotnet", "restore", str(project), "--locked-mode"], directory)
        if name in ["Runtime", "Design"]:
            run(name + "-pack", ["dotnet", "pack", str(project), "-c", "Release", "--no-restore", "-o", str(feed)], directory)
            package = feed / ("Acme.Specialists." + name + "." + args.version + ".nupkg")
            report.setdefault("fixturePackages", {})[package.name] = hashlib.sha256(package.read_bytes()).hexdigest()
        elif name == "Author":
            for control, marker in [("missing-consumer", "activity.runtime.consumer-missing"), ("missing-storage", "activity.runtime.storage-driver-missing")]:
                run("Author-" + control, ["dotnet", "run", "--project", str(project), "-c", "Release", "--no-restore", "--", str(out / control), control], directory,
                    "EXPECTED_CONTROL:" + marker + ":no-publication-or-template")
            run("Author-publish", ["dotnet", "run", "--project", str(project), "-c", "Release", "--no-restore", "--", str(out / "authoring")], directory)
            inbox = out / "runtime-inbox"
            inbox.mkdir()
            shutil.copy2(out / "authoring/template.json", inbox / "template.json")
        elif name in ["Consumer", "BpmnConsumer"]:
            for mode in (["native", "invalid-output"] if name == "Consumer" else ["bpmn"]):
                run("Consumer-" + mode, ["dotnet", "run", "--project", str(project), "-c", "Release", "--no-restore", "--", str(out / "runtime-inbox"), mode], directory,
                    "EXPECTED_CONTROL:specialist.output.invalid:no-accepted-result" if mode == "invalid-output" else None)
        else:
            run("Lifecycle-run", ["dotnet", "run", "--project", str(project), "-c", "Release", "--no-restore", "--", str(out / "lifecycle")], directory)
        assets = json.loads((directory / "obj/project.assets.json").read_text())
        if any(v["type"] != "package" for v in assets["libraries"].values()):
            raise RuntimeError(name + " consumed source project references")
        if name in ["Runtime", "Consumer"] and any("Design" in key or "Publishing" in key for key in assets["libraries"]):
            raise RuntimeError(name + " has an authoring dependency")
        if name == "BpmnConsumer":
            authoring_dependencies = sorted(key for key in assets["libraries"] if "Design" in key or "Publishing" in key)
            expected = ["Elsa.Activities.Design.Core/" + args.version, "Elsa.Workflows.Design.Core/" + args.version]
            if authoring_dependencies != expected:
                raise RuntimeError("BPMN dependency gap changed; reconcile: " + str(authoring_dependencies))
            report["bpmnBoundaryGap"] = {"status": "ObservedUnresolved", "dependencies": authoring_dependencies,
                                        "owner": "Program #3519 M1 architecture and M4 package separation"}
        for identity, library in assets["libraries"].items():
            package_id, version = identity.rsplit("/", 1)
            if package_id not in packages and not package_id.startswith("Acme.Specialists."):
                continue
            if version != args.version:
                raise RuntimeError("Unexpected candidate version in " + name + ": " + identity)
            cached = out / "consumer-package-cache" / library["path"] / (package_id.lower() + "." + version.lower() + ".nupkg")
            archive = feed / (package_id + "." + version + ".nupkg")
            if hashlib.sha256(cached.read_bytes()).digest() != hashlib.sha256(archive.read_bytes()).digest():
                raise RuntimeError("Consumed package archive differs: " + identity)
        report[name] = {"sourceReferences": [], "packages": sorted(assets["libraries"])}
    report["qualification"] = {"status": "PassedWithRecordedGap", "scope": "bounded research fixtures; BPMN Design package coupling unresolved; no full packaged host or live provider acceptance"}
    (out / "report.json").write_text(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
