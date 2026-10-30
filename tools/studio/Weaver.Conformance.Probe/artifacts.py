"""Retained real product distributions and external Weaver authors/consumers."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[3]
TOOL = Path(__file__).resolve().parent
FIXTURES = TOOL / "fixtures"
BACKEND_PACKAGES = [
    "Elsa.Primitives.Hosting", "Elsa.Locking.FileSystem", "Elsa.Events", "Elsa.Mediator",
    "Elsa.Tasks", "Elsa.Serialization.SystemText", "Elsa.Expressions", "Elsa.Expressions.Api",
    "Elsa.Api.Capabilities", "Elsa.Foundation.Identity", "Elsa.Foundation.Identity.Api",
    "Elsa.Foundation.Identity.AspNetCoreIdentity", "Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore",
    "Elsa.Foundation.Identity.OpenIddict", "Elsa.Studio.Preferences.Api",
    "Elsa.Studio.Preferences.Persistence.EntityFrameworkCore", "Elsa.Workflows.Design.Api",
    "Elsa.Workflows.Design.Validations", "Elsa.Workflows.Design.Persistence.EntityFrameworkCore",
    "Elsa.Activities.Design.Api", "Elsa.Activities.Design.Persistence.EntityFrameworkCore",
    "Elsa.Activities.Design.Reconciliation", "Elsa.Activities.Design.Reconciliation.Clr",
    "Elsa.Activities.Flowchart", "Elsa.Activities.Primitives", "Elsa.Activities.Runtime",
    "Elsa.Workflows.Publishing", "Elsa.Workflows.Publishing.Api",
    "Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore", "Elsa.Workflows.Runtime.Api",
    "Elsa.Workflows.Runtime.Resumption", "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore",
    "Elsa.Agent", "Elsa.Agent.Api",
    "Elsa.Agent.Workflows", "Elsa.Agent.GitHubCopilot", "Elsa.Agent.Anthropic",
    "Elsa.Modularity.Api", "Elsa.Modularity.Nuplane", "Elsa.Cluster.Readability",
]
STUDIO_PACKAGES = [
    "Elsa.Studio.Hosting", "Elsa.Studio.Hosting.Nuplane", "Elsa.Studio.Dashboard",
    "Elsa.Studio.Workflows", "Elsa.Studio.Weaver.Workflows",
]


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_json(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2) + "\n")


def package_identity(path: Path) -> dict:
    with zipfile.ZipFile(path) as archive:
        specs = [entry for entry in archive.namelist() if entry.endswith(".nuspec")]
        check(len(specs) == 1, "Expected one nuspec: " + str(path))
        root = ET.fromstring(archive.read(specs[0]))
        text = {node.tag.rsplit("}", 1)[-1]: node.text for node in root.iter()}
        dependencies = [dict(node.attrib) for node in root.iter()
                        if node.tag.rsplit("}", 1)[-1] == "dependency"]
        return {"id": text["id"], "version": text["version"], "sha256": sha(path),
                "path": str(path), "dependencies": dependencies}


class ConformanceRun:
    def __init__(self, output: Path, version: str):
        check(output != REPO and REPO not in output.parents, "Output must be outside the checkout")
        output.mkdir(parents=True, exist_ok=False, mode=0o700)
        self.root = output
        self.version = version
        self.feed = output / "candidate-feed"
        self.feed.mkdir()
        self.packages: dict[str, dict] = {}
        self.profiles = {}
        self.dotnet = shutil.which("dotnet")
        check(self.dotnet, "dotnet must be available on PATH (including any build-slot wrapper)")
        self.source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
        self.report = {"status": "preparing", "source": self.source, "version": version,
                       "evidenceCategory": "deterministic-provider-protocol",
                       "liveProvider": False, "independentPackageWitness": False,
                       "toolInputHashes": {str(p.relative_to(REPO)): sha(p) for p in TOOL.rglob("*")
                                           if p.is_file() and "__pycache__" not in p.parts},
                       "stages": [], "packages": {}, "consumers": {}, "cases": []}
        self.save()

    def save(self):
        write_json(self.root / "report.json", self.report)

    def assert_immutable_inputs(self):
        head = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
        check(head == self.source, "Source changed during qualification; use a fresh committed run")
        check(not subprocess.check_output(["git", "status", "--porcelain", "--untracked-files=all"],
                                         cwd=REPO, text=True).strip(),
              "Qualification inputs are no longer committed and clean")
        current = {str(p.relative_to(REPO)): sha(p) for p in TOOL.rglob("*")
                   if p.is_file() and "__pycache__" not in p.parts}
        check(current == self.report["toolInputHashes"], "Driver/template inputs changed during qualification")

    def run(self, name, command, cwd, env=None):
        self.report["stage"] = name
        self.save()
        log = self.root / (name + ".log")
        with log.open("w") as stream:
            result = subprocess.run(command, cwd=cwd, env=env, stdout=stream, stderr=subprocess.STDOUT)
        self.report["stages"].append({"name": name, "exitCode": result.returncode, "log": str(log)})
        self.save()
        check(result.returncode == 0, f"{name} failed ({result.returncode}); private log: {log}")

    def pin(self, package):
        values = []
        for path in [REPO / "Directory.Packages.props", REPO / "Studio/Directory.Packages.props"]:
            values.extend(node.attrib["Version"] for node in ET.parse(path).getroot().iter("PackageVersion")
                          if node.attrib.get("Include") == package and "Version" in node.attrib)
        check(bool(values) and len(set(values)) == 1, "Missing/ambiguous central pin: " + package)
        return values[0]

    def config(self, target):
        original = next(p for p in REPO.iterdir() if p.name.lower() == "nuget.config")
        root = ET.parse(original).getroot()
        sources = root.find("packageSources")
        check(sources is not None, "Normal NuGet sources required")
        ET.SubElement(sources, "add", key="weaver-candidate", value=str(self.feed))
        mapping = root.find("packageSourceMapping")
        if mapping is None:
            mapping = ET.SubElement(root, "packageSourceMapping")
            for source in sources.findall("add"):
                if source.attrib["key"] != "weaver-candidate":
                    node = ET.SubElement(mapping, "packageSource", key=source.attrib["key"])
                    ET.SubElement(node, "package", pattern="*")
        group = ET.SubElement(mapping, "packageSource", key="weaver-candidate")
        candidate_ids = set(self.packages)
        candidate_ids.update(package_identity(path)["id"] for path in self.feed.glob("*.nupkg"))
        for package in sorted(candidate_ids):
            ET.SubElement(group, "package", pattern=package)
        ET.ElementTree(root).write(target / "NuGet.Config", encoding="unicode")
        shutil.copy2(REPO / "global.json", target / "global.json")

    def produce(self):
        self.assert_immutable_inputs()
        producer = self.root / "producer"
        producer.mkdir()
        self.run("archive", ["git", "archive", "--format=tar", "--output=" + str(self.root / "source.tar"), self.source], REPO)
        self.run("extract", ["tar", "-xf", str(self.root / "source.tar"), "-C", str(producer)], REPO)
        self.run("frontend-install", ["pnpm", "install", "--frozen-lockfile"], producer / "Studio")
        self.run("frontend-build", ["pnpm", "build"], producer / "Studio")
        self.run("sdk-build", ["pnpm", "--filter", "@elsa-workflows/studio-sdk", "build:lib"], producer / "Studio")
        sdk = producer / "Studio/src/essentials/Elsa.Studio.Hosting/StudioSdk"
        self.run("sdk-manifest", ["node", "scripts/prepare-npm-package.mjs", str(sdk), self.version], producer / "Studio")
        npm_feed = self.root / "npm-artifacts"
        npm_feed.mkdir()
        self.run("sdk-pack", ["pnpm", "pack", "--pack-destination", str(npm_feed)], sdk)
        tarballs = list(npm_feed.glob("*.tgz"))
        check(len(tarballs) == 1, "Expected exactly one retained SDK archive")
        self.sdk = tarballs[0]

        projects = {p.stem: p for p in producer.rglob("*.csproj")
                    if not {"obj", "bin", "node_modules"}.intersection(p.parts)}
        selected = []
        seen = set()

        def visit(path):
            path = path.resolve()
            if path in seen:
                return
            seen.add(path)
            check(path.is_relative_to(producer), "Producer project escaped source snapshot")
            tree = ET.parse(path).getroot()
            for entry in tree.iter("ProjectReference"):
                value = entry.attrib["Include"].replace("\\", "/")
                check("$(" not in value and ";" not in value, "Resolve nonliteral producer reference explicitly: " + value)
                visit((path.parent / value).resolve())
            selected.append(path)

        for package in BACKEND_PACKAGES + STUDIO_PACKAGES:
            check(package in projects, "No actual product project for " + package)
            visit(projects[package])
        solution = ET.Element("Solution")
        for project in selected:
            ET.SubElement(solution, "Project", Path=project.relative_to(producer).as_posix())
        slnx = producer / "Weaver.Candidates.slnx"
        ET.ElementTree(solution).write(slnx, encoding="unicode")
        properties = ["-p:Version=" + self.version, "-p:SourceRevisionId=" + self.source]
        self.run("product-build", [self.dotnet, "build", str(slnx), "-c", "Release", *properties], producer)
        for index, project in enumerate(selected):
            tree = ET.parse(project).getroot()
            if any((node.text or "").strip().lower() == "false" for node in tree.iter("IsPackable")):
                continue
            self.run("pack-" + str(index) + "-" + project.stem,
                     [self.dotnet, "pack", str(project), "-c", "Release", "--no-build", *properties,
                      "-o", str(self.feed)], producer)
        for path in self.feed.glob("*.nupkg"):
            value = package_identity(path)
            check(value["id"] not in self.packages, "Duplicate candidate identity")
            check(value["version"] == self.version, "Candidate version mismatch")
            self.packages[value["id"]] = value
        check(set(BACKEND_PACKAGES + STUDIO_PACKAGES).issubset(self.packages), "Product archive set incomplete")
        self.report["packages"] = self.packages
        self.report["producer"] = {"source": self.source, "projectCount": len(selected),
                                   "sdk": {"path": str(self.sdk), "sha256": sha(self.sdk)},
                                   "generatedInputs": {str(p.relative_to(producer)): sha(p)
                                                       for project in selected
                                                       for p in (project.parent / "wwwroot").rglob("*") if p.is_file()}}
        self.save()

    def materialize(self, name, relative, replacements=None):
        self.assert_immutable_inputs()
        source = self.root / name
        source.mkdir()
        values = {"__PRODUCT_VERSION__": self.version, "__FIXTURE_VERSION__": "1.0.0-witness",
                  "__CSHELLS_VERSION__": self.pin("CShells.Abstractions"),
                  "__SDK_TGZ__": str(self.sdk), "__REQUIRED_SDK_VERSION__": "*"}
        values.update(replacements or {})
        template = FIXTURES / relative
        check(template.is_dir(), "Missing fixture producer: " + relative)
        for path in template.rglob("*"):
            if not path.is_file():
                continue
            target = source / path.relative_to(template)
            if target.suffix == ".in":
                target = target.with_suffix("")
            target.parent.mkdir(parents=True, exist_ok=True)
            text = path.read_text()
            for key, value in values.items():
                text = text.replace(key, value)
            import re
            check(not re.search(r"__[A-Z][A-Z0-9_]*__", text),
                  "Unresolved distribution input in " + str(target))
            target.write_text(text)
        self.config(source)
        return source

    def consumer_environment(self, name):
        denied = self.root / (name + "-denied-tools")
        denied.mkdir()
        audit = self.root / (name + "-frontend-invocations.log")
        for command in ["node", "npm", "pnpm", "npx"]:
            path = denied / command
            path.write_text('#!/bin/sh\nprintf "%s\\n" "$0" >> "$WEAVER_FRONTEND_AUDIT"\nexit 71\n')
            path.chmod(0o755)
        prefixes = ("studio__", "cshells__", "nuplane__", "aspnetcore_", "dotnet_environment",
                    "foundationagent", "githubcopilot", "anthropic", "qualification__")
        env = {k: v for k, v in os.environ.items() if not k.lower().startswith(prefixes)}
        env.update(PATH=str(denied) + os.pathsep + env["PATH"], WEAVER_FRONTEND_AUDIT=str(audit),
                   NUGET_PACKAGES=str(self.root / (name + "-nuget-cache")),
                   NUGET_HTTP_CACHE_PATH=str(self.root / (name + "-http-cache")),
                   ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development")
        return env, audit

    def execute(self):
        try:
            self.produce()
            from journeys import qualify_distributions
            qualify_distributions(self)
            self.assert_immutable_inputs()
            check(self.report["status"] == "passed" and self.report["independentPackageWitness"],
                  "Incomplete qualification cannot establish a witness")
        except BaseException as error:
            self.report.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
            self.save()
            raise
