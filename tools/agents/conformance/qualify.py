"""Qualify committed specialist product packages with independent bounded consumers.

Nothing is pushed or published remotely. Model inference is deterministic and process-local.
The output directory must be new, absolute and outside the source checkout.
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
import zipfile

ROOT = Path(__file__).resolve().parents[3]
PRODUCT_ROOTS = [
    "Elsa.Agent.Specialists.Design", "Elsa.Agent.Specialists", "Elsa.Activities.Runtime",
    "Elsa.Serialization.SystemText", "Elsa.Secrets", "Elsa.Workflows.Publishing.Api",
    "Elsa.Activities.Design.Persistence.EntityFrameworkCore",
    "Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore", "Elsa.Locking.FileSystem",
]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source", default="HEAD")
    args = parser.parse_args()
    if not args.output.is_absolute() or args.output.exists() or args.output.resolve().is_relative_to(ROOT):
        parser.error("output must be a new absolute directory outside the checkout")
    out = args.output
    out.mkdir(parents=True)
    out = out.resolve()
    source = subprocess.check_output(["git", "rev-parse", args.source + "^{commit}"], cwd=ROOT, text=True).strip()
    producer = out / "producer"
    feed = out / "feed"
    producer.mkdir()
    feed.mkdir()
    report = {"source": source, "version": args.version, "status": "InProgress", "liveModel": False,
              "durableJournal": False, "stages": [], "consumers": {}}
    environment = dict(os.environ, NUGET_PACKAGES=str(out / "consumer-packages"), NUGET_HTTP_CACHE_PATH=str(out / "consumer-http-cache"))

    def save():
        (out / "report.json").write_text(json.dumps(report, indent=2))

    def run(name, command, directory):
        print(name, flush=True)
        with (out / (name + ".log")).open("w") as log:
            result = subprocess.run(command, cwd=directory, stdout=log, stderr=subprocess.STDOUT,
                                    env=None if directory == producer else environment)
        report["stages"].append({"name": name, "exitCode": result.returncode, "status": "Passed" if result.returncode == 0 else "Failed"})
        if result.returncode:
            report["status"] = "Failed"
            save()
            raise RuntimeError(name + " failed; inspect " + str(out / (name + ".log")))
        save()

    archive = out / "source.tar"
    with archive.open("wb") as stream:
        subprocess.run(["git", "archive", source], cwd=ROOT, stdout=stream, check=True)
    with tarfile.open(archive) as bundle:
        bundle.extractall(producer, filter="data")
    report["sourceArchiveSha256"] = sha(archive)
    fixtures = producer / "tools/agents/conformance"
    projects = {p.stem: p for area in ["Essentials/src", "Extensions"] for p in (producer / area).rglob("*.csproj")}
    selected, seen = [], set()

    def visit(project):
        project = project.resolve()
        if project in seen:
            return
        if not project.is_relative_to(producer):
            raise RuntimeError("producer project reference escapes its committed tree")
        seen.add(project)
        for reference in ET.parse(project).iter("ProjectReference"):
            include = reference.attrib["Include"].replace("\\", "/")
            if "$(" in include or ";" in include:
                raise RuntimeError("nonliteral project reference: " + include)
            visit(project.parent / include)
        selected.append(project)

    for name in PRODUCT_ROOTS:
        visit(projects[name])
    solution = ET.Element("Solution")
    for project in selected:
        ET.SubElement(solution, "Project", Path=project.relative_to(producer).as_posix())
    candidate_solution = producer / "Specialist.Candidates.slnx"
    ET.ElementTree(solution).write(candidate_solution, encoding="unicode")
    properties = ["-p:Version=" + args.version, "-p:SourceRevisionId=" + source]
    run("producer-build", ["dotnet", "build", str(candidate_solution), "-c", "Release", *properties], producer)
    for index, project in enumerate(selected):
        if any((x.text or "").lower() == "false" for x in ET.parse(project).iter("IsPackable")):
            continue
        run("pack-" + str(index) + "-" + project.stem,
            ["dotnet", "pack", str(project), "-c", "Release", "--no-build", *properties, "-o", str(feed)], producer)
    packages = {}
    for package in feed.glob("*.nupkg"):
        with zipfile.ZipFile(package) as bundle:
            manifest = ET.fromstring(bundle.read(next(x for x in bundle.namelist() if x.endswith(".nuspec"))))
            metadata = manifest.find("{*}metadata")
            identity, version = metadata.find("{*}id").text, metadata.find("{*}version").text
            if version != args.version:
                raise RuntimeError("unexpected producer package version: " + identity)
            packages[identity] = {"version": version, "sha256": sha(package)}
    report["productPackages"] = packages

    def configure(directory):
        config = ET.parse(producer / "NuGet.config")
        ET.SubElement(config.getroot().find("packageSources"), "add", key="specialist-candidate", value=str(feed))
        mapping = config.getroot().find("packageSourceMapping")
        group = ET.SubElement(mapping, "packageSource", key="specialist-candidate")
        for identity in sorted(packages):
            ET.SubElement(group, "package", pattern=identity)
        ET.SubElement(group, "package", pattern="Acme.Specialist.*")
        config.write(directory / "NuGet.Config", encoding="unicode")
        shutil.copy2(producer / "global.json", directory / "global.json")

    def inspect(name, directory):
        assets = json.loads((directory / "obj/project.assets.json").read_text())
        if any(x["type"] != "package" for x in assets["libraries"].values()):
            raise RuntimeError(name + " consumed a source project reference")
        authoring = sorted(x for x in assets["libraries"] if x.startswith("Elsa.") and (".Design" in x or ".Publishing" in x))
        if name in ["Model", "Consumer"] and authoring:
            raise RuntimeError(name + " has an authoring dependency: " + str(authoring))
        resolved = {}
        for identity, library in assets["libraries"].items():
            package_id, version = identity.rsplit("/", 1)
            if package_id not in packages and not package_id.startswith("Acme.Specialist."):
                continue
            if version != args.version:
                raise RuntimeError("wrong candidate package version: " + identity)
            archive = Path(environment["NUGET_PACKAGES"]) / library["path"] / (package_id.lower() + "." + version.lower() + ".nupkg")
            candidate = next((p for p in feed.glob("*.nupkg") if p.name.lower() == archive.name.lower()), None)
            if candidate is None or sha(archive) != sha(candidate):
                raise RuntimeError("consumed archive differs from retained candidate: " + identity)
            dlls = {}
            with zipfile.ZipFile(candidate) as bundle:
                for dll in (archive.parent / "lib").rglob("*.dll"):
                    relative = dll.relative_to(archive.parent).as_posix()
                    digest = sha(dll)
                    if hashlib.sha256(bundle.read(relative)).hexdigest() != digest:
                        raise RuntimeError("consumed DLL differs from its retained archive: " + identity + ":" + relative)
                    dlls[relative] = digest
            resolved[identity] = {"archiveSha256": sha(archive), "dlls": dlls}
        report["consumers"][name] = {"sourceReferences": [], "authoringDependencies": authoring, "resolvedCandidates": resolved}
        save()

    central = ET.parse(producer / "Directory.Packages.props")
    sqlite_bundle_version = next(p.attrib["Version"] for p in central.iter("PackageVersion") if p.attrib.get("Include") == "SQLitePCLRaw.bundle_e_sqlite3")
    for name in ["Model", "Author", "Consumer"]:
        directory = out / name
        directory.mkdir()
        for path in (fixtures / name).iterdir():
            if path.suffix != ".template":
                continue
            (directory / path.stem).write_text(path.read_text().replace("__VERSION__", args.version).replace("__SQLITE_BUNDLE_VERSION__", sqlite_bundle_version))
        if name == "Author":
            test_source = producer / "Extensions/Agent/tests/Specialists/Tests/Publication/SpecialistPublicationTests.cs"
            shutil.copy2(test_source, directory / test_source.name)
            report["authorPublicApiFixtureSha256"] = sha(test_source)
        configure(directory)
        project = next(directory.glob("*.csproj"))
        run(name + "-restore", ["dotnet", "restore", str(project), "--use-lock-file"], directory)
        run(name + "-locked", ["dotnet", "restore", str(project), "--locked-mode"], directory)
        if name == "Model":
            run("Model-pack", ["dotnet", "pack", str(project), "-c", "Release", "--no-restore", "-o", str(feed)], directory)
        elif name == "Author":
            run("Author-execute", ["dotnet", "run", "--project", str(project), "-c", "Release", "--no-restore", "--", str(out / "runtime-inbox")], directory)
        else:
            for mode in ["native", "invalid-result"]:
                run("Consumer-" + mode, ["dotnet", "run", "--project", str(project), "-c", "Release", "--no-restore", "--", str(out / "runtime-inbox"), mode], directory)
        inspect(name, directory)
    report["status"] = "PassedBoundedKernelProfile"
    report["independentPackageConsumers"] = True
    report["runtimeClosureDesignFree"] = True
    save()
    print(json.dumps({"source": source, "status": report["status"], "productPackages": len(packages), "liveModel": False, "durableJournal": False}))


if __name__ == "__main__":
    main()
