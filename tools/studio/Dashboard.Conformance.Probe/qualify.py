#!/usr/bin/env python3
"""Build and exercise an owned package-only native Dashboard business contribution."""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import signal
import subprocess
import tarfile
import time
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

REPO = Path(__file__).resolve().parents[3]
FIXTURES = Path(__file__).with_name("fixtures")
ROOTS = (
    "Extensions/Examples/src/Acme.Processing/Module/Acme.Processing.csproj",
    "Essentials/src/Studio/Dashboard/Api/Elsa.Studio.Dashboard.Api.csproj",
    "Essentials/src/Studio/Dashboard/Persistence/EntityFrameworkCore/Elsa.Studio.Dashboard.Persistence.EntityFrameworkCore.csproj",
    "Essentials/src/Foundation/Identity/Elsa.Foundation.Identity/Elsa.Foundation.Identity.csproj",
    "Essentials/src/Events/Elsa.Events/Elsa.Events.csproj",
)
CASE_IDS = (
    "installed-module-bytes", "native-discovery", "native-business-record-create", "author-numerical-oracle",
    "viewer-numerical-oracle", "record-denied-control", "native-authorized-drill", "tenant-isolation-control",
    "field-denied-control", "aggregate-without-record-control", "record-without-aggregate-control", "anonymous-control",
    "native-question-save", "native-saved-question-refresh", "private-definition-control", "disabled-contribution-control",
    "persistent-question-source-unavailable", "restored-module-native-refresh", "installed-common-bytes-unchanged",
)


def public_receipt(report):
    """Only explicit non-secret evidence fields may leave the private witness directory."""
    keys = ("status", "source", "preliminary", "version", "scope", "providerConfigured", "fullFeatureQualified",
            "exactCommitQualified", "independentPackageWitness", "producerInputManifestSha256", "toolSha256",
            "nativeProfileCommonBytesUnchanged")
    result = {key: report[key] for key in keys if key in report}
    result["cases"] = [{key: case[key] for key in ("id", "status")} for case in report.get("cases", [])]
    result["stages"] = [{key: stage[key] for key in ("id", "status")} for stage in report.get("stages", [])]
    result["hostProcesses"] = [{key: host[key] for key in ("label", "status", "exitCode") if key in host}
                               for host in report.get("hostProcesses", [])]
    result["packages"] = {name: {key: package[key] for key in ("version", "sha256")}
                          for name, package in report.get("packages", {}).items()}
    consumer = report.get("consumer", {})
    result["consumer"] = {key: consumer[key] for key in ("packageOnly", "offlineRestore", "frontendInvocations", "lockSha256") if key in consumer}
    return result


def validate_complete(report):
    ids = [case["id"] for case in report["cases"]]
    require(len(ids) == len(CASE_IDS) and set(ids) == set(CASE_IDS), "The exact native witness inventory is incomplete or duplicated")
    require(all(case["status"] == "passed" for case in report["cases"]), "A native case is incomplete or failed")
    require(report["stages"] and all(stage["status"] == "passed" for stage in report["stages"]), "A distribution stage is incomplete or failed")
    required_stages = {"producer-build", "consumer-online-restore", "consumer-offline-restore", "consumer-publish"}
    required_stages.update("producer-pack-" + str(index) for index in range(len(report["packages"])))
    if not report["preliminary"]: required_stages.add("source-archive")
    stage_ids = [stage["id"] for stage in report["stages"]]
    require(len(stage_ids) == len(set(stage_ids)) and required_stages.issubset(stage_ids), "A required distribution stage is missing or duplicated")
    hosts = report["hostProcesses"]
    require(len(hosts) == 3 and {host["label"] for host in hosts} == {"enabled", "disabled", "restored"}, "The native host inventory is incomplete")
    require(all(host["status"] == "stopped" and host["exitCode"] == 0 for host in hosts), "An owned host did not stop cleanly")
    require(report.get("nativeProfileCommonBytesUnchanged") is True, "Common installed bytes were not preserved")
    require(report["consumer"]["packageOnly"] and report["consumer"]["offlineRestore"] and report["consumer"]["frontendInvocations"] == 0,
            "The consumer package boundary is incomplete")


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + "\n")


def root_input_files(repository, revision):
    # Git paths are case-sensitive even when the checkout filesystem is not.
    wanted = {name.casefold() for name in ("global.json", "VersionLines.props", "PackageVersioning.props",
              "PackageRanges.targets", "NuGet.config")}
    names = subprocess.check_output(["git", "-c", "core.fsmonitor=false", "ls-tree", "--name-only", "-z", revision], cwd=repository).decode().split("\0")
    return [repository / name for name in names if name.casefold() in wanted]


def identity(path):
    with zipfile.ZipFile(path) as archive:
        specs = [entry for entry in archive.namelist() if entry.endswith(".nuspec")]
        require(len(specs) == 1, "An archive must contain exactly one nuspec")
        root = ET.fromstring(archive.read(specs[0]))
    values = {node.tag.rsplit("}", 1)[-1]: node.text for node in root.iter()}
    return values["id"], values["version"]


class Probe:
    def __init__(self, root, version, allow_dirty):
        require(root.is_absolute() and not root.is_relative_to(REPO), "Use an absolute output outside the checkout")
        root.mkdir(parents=True, exist_ok=False)
        root.chmod(0o700)
        self.root, self.version = root, version
        self.dotnet = shutil.which("dotnet")
        require(self.dotnet, "The normal dotnet tool is required")
        self.source = subprocess.check_output(["git", "-c", "core.fsmonitor=false", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
        dirty = subprocess.check_output(["git", "-c", "core.fsmonitor=false", "status", "--porcelain"], cwd=REPO, text=True).splitlines()
        require(allow_dirty or not dirty, "Final-source proof requires a clean committed checkout; preliminary runs need --allow-dirty")
        self.report = {"status": "preparing", "source": self.source, "preliminary": allow_dirty,
                       "dirtyPaths": dirty, "version": version, "stages": [], "cases": [],
                       "scope": "independent native package contribution", "providerConfigured": False,
                       "fullFeatureQualified": False, "exactCommitQualified": False, "independentPackageWitness": False,
                       "ownedRoot": str(root), "hostProcesses": [], "toolSha256": sha(__file__)}
        self.feed = root / "producer-feed"; self.feed.mkdir()
        self.packages = {}
        self.save()

    def save(self):
        write_json(self.root / "report.json", self.report)
        write_json(self.root / "safe-receipt.json", public_receipt(self.report))

    def env(self, cache=None, frontend_denied=False):
        safe = {key: value for key, value in os.environ.items() if key in {
            "PATH", "HOME", "TMPDIR", "TEMP", "TMP", "USER", "LANG", "LC_ALL", "DOTNET_ROOT",
            "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "no_proxy",
            "SSL_CERT_FILE", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE"}}
        safe.update(DOTNET_CLI_HOME=str(self.root / "cli-home"), DOTNET_CLI_TELEMETRY_OPTOUT="1",
                    DOTNET_NOLOGO="1", ASPNETCORE_ENVIRONMENT="Production", DOTNET_ENVIRONMENT="Production")
        if cache:
            safe["NUGET_PACKAGES"] = str(cache)
        if frontend_denied:
            safe["PATH"] = str(self.root / "frontend-denied") + os.pathsep + safe["PATH"]
            safe["DASHBOARD_FRONTEND_AUDIT"] = str(self.root / "frontend-invocations.log")
        return safe

    def run(self, name, command, cwd, env=None):
        print("stage: " + name, flush=True)
        record = {"id": name, "command": command, "cwd": str(cwd), "status": "running", "log": str(self.root / (name + ".log"))}
        self.report["stages"].append(record); self.save()
        with Path(record["log"]).open("w") as log:
            process = subprocess.Popen(command, cwd=cwd, env=env or self.env(), stdout=log, stderr=subprocess.STDOUT)
            record["pid"] = process.pid; self.save()
            try:
                code = process.wait()
            except BaseException:
                if process.poll() is None:
                    process.terminate()
                    try: process.wait(timeout=20)
                    except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
                raise
        record.update(status="passed" if code == 0 else "failed", exitCode=code); self.save()
        require(code == 0, name + " failed; retained log: " + record["log"])

    def closure(self):
        selected, seen = [], set()
        def visit(path):
            path = path.resolve()
            require(path.is_relative_to(REPO), "A producer project escaped the checkout")
            if path in seen: return
            seen.add(path)
            for reference in ET.parse(path).getroot().iter("ProjectReference"):
                value = reference.attrib["Include"].replace("\\", "/")
                require("$(" not in value and ";" not in value, "Resolve a nonliteral producer reference explicitly")
                visit(path.parent / value)
            selected.append(path)
        for root in ROOTS: visit(REPO / root)
        return selected

    def produce(self):
        selected = self.closure()
        producer = self.root / "producer"; producer.mkdir()
        files = set()
        for project in selected + [REPO / "Essentials/build/Analyzers/Elsa.Architecture.Analyzers.csproj"]:
            for path in project.parent.rglob("*"):
                if path.is_file() and not {"bin", "obj", "node_modules", ".git"}.intersection(path.relative_to(project.parent).parts):
                    files.add(path)
            for ancestor in project.parents:
                if ancestor == REPO.parent: break
                for name in ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", ".editorconfig"):
                    if (ancestor / name).is_file(): files.add(ancestor / name)
        files.update(root_input_files(REPO, self.source))
        inputs = {}
        for path in sorted(files):
            require(path.is_relative_to(REPO), "A source input escaped the checkout")
            relative = path.relative_to(REPO); target = producer / relative; target.parent.mkdir(parents=True, exist_ok=True)
            data = path.read_bytes(); target.write_bytes(data)
            inputs[relative.as_posix()] = hashlib.sha256(data).hexdigest()
        if not self.report["preliminary"]:
            # Final qualification compiles the exact committed tree, even if the live checkout changes later.
            archive = self.root / "source.tar"
            directories = {project.parent.relative_to(REPO).as_posix() for project in selected}
            directories.update({"Essentials/build/Analyzers", "tools/studio/Dashboard.Conformance.Probe"})
            roots = {path.relative_to(REPO).as_posix() for path in files
                     if not any(path.relative_to(REPO).is_relative_to(Path(directory)) for directory in directories)}
            self.run("source-archive", ["git", "-c", "core.fsmonitor=false", "archive", "--format=tar", "--output=" + str(archive), self.source, *sorted(directories | roots)], REPO)
            shutil.rmtree(producer); producer.mkdir()
            with tarfile.open(archive) as source: source.extractall(producer, filter="data")
            inputs = {path.relative_to(producer).as_posix(): sha(path) for path in producer.rglob("*") if path.is_file()}
        self.report["producerInputs"] = inputs
        self.report["producerInputManifestSha256"] = hashlib.sha256(json.dumps(inputs, sort_keys=True).encode()).hexdigest()
        solution = ET.Element("Solution")
        for project in selected: ET.SubElement(solution, "Project", Path=project.relative_to(REPO).as_posix())
        ET.ElementTree(solution).write(producer / "Dashboard.Candidates.slnx", encoding="unicode")
        # Example projects stay outside publishing. The owned producer opts in for
        # both build and pack so their version stamps describe the same inputs.
        properties = ["-p:IsPackable=true", "-p:Version=" + self.version, "-p:SourceRevisionId=" + self.source]
        self.run("producer-build", [self.dotnet, "build", "Dashboard.Candidates.slnx", "-c", "Release", *properties], producer)
        for index, project in enumerate(selected):
            self.run("producer-pack-" + str(index), [self.dotnet, "pack", str(project.relative_to(REPO)), "-c", "Release", "--no-build", *properties,
                     "-o", str(self.feed)], producer)
        for archive in self.feed.glob("*.nupkg"):
            package, version = identity(archive)
            require(version == self.version and package not in self.packages, "Candidate package identity/version mismatch")
            self.packages[package] = {"path": str(archive), "version": version, "sha256": sha(archive)}
        require({Path(root).stem for root in ROOTS}.issubset(self.packages), "A required actual package is missing")
        self.report["packages"] = self.packages; self.save()

    def config(self, destination, offline=False):
        root = ET.Element("configuration"); sources = ET.SubElement(root, "packageSources"); ET.SubElement(sources, "clear")
        ET.SubElement(sources, "add", key="candidate", value=str(self.root / "offline-feed" if offline else self.feed))
        mapping = ET.SubElement(root, "packageSourceMapping"); candidate = ET.SubElement(mapping, "packageSource", key="candidate")
        for package in (["*"] if offline else sorted(self.packages)):
            ET.SubElement(candidate, "package", pattern=package)
        if not offline:
            configs = [path for path in root_input_files(REPO, self.source) if path.name.casefold() == "nuget.config"]
            require(len(configs) == 1, "Exactly one tracked NuGet.config is required for the consumer")
            source_file = configs[0] if self.report["preliminary"] else self.root / "producer" / configs[0].name
            original = ET.parse(source_file).getroot()
            require(original.find("packageSourceCredentials") is None, "Repository package config unexpectedly contains credentials")
            for source in original.findall("./packageSources/add"):
                address = urllib.parse.urlparse(source.attrib["value"])
                require(address.scheme == "https" and not address.username and not address.password and not address.query,
                        "Only explicit public package URLs are admitted")
                ET.SubElement(sources, "add", **source.attrib)
            original_mapping = original.find("packageSourceMapping")
            if original_mapping is not None:
                for group in original_mapping: mapping.append(ET.fromstring(ET.tostring(group)))
        fallback = ET.SubElement(root, "fallbackPackageFolders"); ET.SubElement(fallback, "clear")
        ET.ElementTree(root).write(destination, encoding="unicode")

    def consumer(self):
        consumer = self.root / "consumer"; consumer.mkdir()
        direct = [Path(root).stem for root in ROOTS]
        inputs = REPO if self.report["preliminary"] else self.root / "producer"
        sqlite = next(node.attrib["Version"] for node in ET.parse(inputs / "Directory.Packages.props").getroot().iter("PackageVersion")
                      if node.attrib.get("Include") == "Microsoft.EntityFrameworkCore.Sqlite")
        references = "\n".join('    <PackageReference Include="' + package + '" Version="[' + self.version + ']" />' for package in direct)
        references += '\n    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="[' + sqlite + ']" />'
        fixtures = FIXTURES if self.report["preliminary"] else self.root / "producer/tools/studio/Dashboard.Conformance.Probe/fixtures"
        (consumer / "Consumer.csproj").write_text((fixtures / "Consumer.csproj.in").read_text().replace("__PACKAGE_REFERENCES__", references))
        shutil.copy2(fixtures / "Program.cs", consumer / "Program.cs"); shutil.copy2(self.root / "producer/global.json", consumer / "global.json")
        self.config(consumer / "NuGet.online.config")
        denied = self.root / "frontend-denied"; denied.mkdir()
        (self.root / "frontend-invocations.log").write_text("")
        for name in ("node", "npm", "pnpm", "yarn", "npx"):
            path = denied / name
            path.write_text('#!/bin/sh\nprintf "%s\\n" "' + name + ' denied" >> "$DASHBOARD_FRONTEND_AUDIT"\nexit 97\n'); path.chmod(0o755)
        cache = self.root / "online-cache"
        self.run("consumer-online-restore", [self.dotnet, "restore", "Consumer.csproj", "--configfile", "NuGet.online.config", "-p:Configuration=Release"], consumer, self.env(cache, True))
        graph = json.loads((consumer / "obj/project.assets.json").read_text())
        require(all(value["type"] == "package" for value in graph["libraries"].values()), "Consumer resolved product/project source")
        require({Path(path).resolve() for path in graph["packageFolders"]} == {cache.resolve()}, "Consumer used an external package cache")
        offline = self.root / "offline-feed"; offline.mkdir()
        archives = []
        for package_identity in sorted(graph["libraries"]):
            package, version = package_identity.rsplit("/", 1)
            original = cache / package.lower() / version.lower() / (package.lower() + "." + version.lower() + ".nupkg")
            require(original.is_file(), "A resolved archive is missing from the owned cache: " + package_identity)
            target = offline / original.name; shutil.copy2(original, target)
            archives.append({"identity": package_identity, "path": str(target), "sha256": sha(target)})
        self.config(consumer / "NuGet.offline.config", True)
        self.run("consumer-offline-restore", [self.dotnet, "restore", "Consumer.csproj", "--locked-mode", "--configfile", "NuGet.offline.config", "-p:Configuration=Release"], consumer, self.env(self.root / "offline-cache", True))
        published = self.root / "published"
        self.run("consumer-publish", [self.dotnet, "publish", "Consumer.csproj", "--no-restore", "-c", "Release", "-o", str(published)], consumer, self.env(self.root / "offline-cache", True))
        comparisons = []
        for package, value in self.packages.items():
            dll = published / (package + ".dll")
            if not dll.is_file(): continue
            with zipfile.ZipFile(value["path"]) as archive:
                member = "lib/net10.0/" + package + ".dll"
                require(member in archive.namelist(), "A published candidate has no expected archive DLL")
                archived_hash = hashlib.sha256(archive.read(member)).hexdigest()
            require(sha(dll) == archived_hash, "Published bytes differ from the actual nupkg: " + package)
            comparisons.append({"package": package, "publishedSha256": sha(dll), "archiveSha256": archived_hash})
        require({"Acme.Processing", "Acme.Processing.Core", "Elsa.Studio.Dashboard.Core"}.issubset({item["package"] for item in comparisons}), "Key loaded contract/module comparisons are missing")
        require(not (self.root / "frontend-invocations.log").read_text(), "Consumer invoked frontend tools")
        self.report["consumer"] = {"packageOnly": True, "offlineRestore": True, "lockSha256": sha(consumer / "packages.lock.json"),
                                   "assetsSha256": sha(consumer / "obj/project.assets.json"), "archives": archives, "publishedComparisons": comparisons,
                                   "programSha256": sha(consumer / "Program.cs"), "projectSha256": sha(consumer / "Consumer.csproj"),
                                   "onlineConfigSha256": sha(consumer / "NuGet.online.config"), "offlineConfigSha256": sha(consumer / "NuGet.offline.config"),
                                   "frontendInvocations": 0, "published": str(published)}
        self.save(); return published

    def profiles(self):
        dashboard = ["studio/" + resource + ":" + verb for resource in ("questions", "dashboards") for verb in ("read", "create", "edit", "share", "delete")]
        business = ["acme/processing:read", "acme/processing:create", "acme/processing/category:read", "acme/processing-questions:read"]
        values = []
        for name, subject, tenant, denied in (
            ("alice", "alice", "tenant-a", None), ("bob", "bob", "tenant-a", None),
            ("bob-no-category", "bob", "tenant-a", "acme/processing/category:read"),
            ("bob-no-data", "bob", "tenant-a", "acme/processing:read"),
            ("other-tenant", "bob", "tenant-b", None), ("no-aggregate", "alice", "tenant-a", "acme/processing-questions:read")):
            values.append({"name": name, "subject": subject, "tenant": tenant, "token": secrets.token_urlsafe(32),
                           "permissions": dashboard + [permission for permission in business if permission != denied]})
        path = self.root / "private-profiles.json"; write_json(path, {"profiles": values}); path.chmod(0o600)
        return path, {value["name"]: value["token"] for value in values}

    @contextmanager
    def server(self, published, profiles, enabled, label):
        log_path = self.root / ("host-" + label + ".log")
        command = [self.dotnet, str(published / "Consumer.dll"), "--contentRoot", str(published), "--urls", "http://127.0.0.1:0",
                   "--profiles", str(profiles), "--state", str(self.root / "native-state"), "--module-enabled", str(enabled).lower()]
        with log_path.open("w") as log:
            process = subprocess.Popen(command, cwd=published, env=self.env(self.root / "offline-cache", True), stdout=log, stderr=subprocess.STDOUT)
            owner = {"label": label, "pid": process.pid, "log": str(log_path), "status": "starting"}; self.report["hostProcesses"].append(owner); self.save()
            try:
                deadline = time.monotonic() + 90
                while True:
                    text = log_path.read_text(errors="replace")
                    address = re.search(r"Now listening on: (http://127\.0\.0\.1:\d+)", text)
                    if address and "DASHBOARD_PACKAGE_PROBE_READY" in text: break
                    require(process.poll() is None and time.monotonic() < deadline, "Owned native consumer did not become ready: " + str(log_path))
                    time.sleep(.1)
                owner.update(status="ready", base=address[1]); self.save(); yield address[1]
            finally:
                if process.poll() is None:
                    process.terminate()
                    try: process.wait(timeout=20)
                    except subprocess.TimeoutExpired: process.kill(); process.wait(timeout=10)
                owner.update(status="stopped", exitCode=process.returncode); self.save()

    def request(self, base, path, token=None, body=None, method="GET", headers=None):
        require(base.startswith("http://127.0.0.1:"), "Only the owned loopback consumer is admitted")
        request_headers = {"Content-Type": "application/json", **(headers or {})}
        if token: request_headers["Authorization"] = "Bearer " + token
        request = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=request_headers, method=method)
        try: response = urllib.request.urlopen(request, timeout=30)
        except urllib.error.HTTPError as error: response = error
        with response:
            raw = response.read(); value = json.loads(raw) if raw else None
            return response.status, value, dict(response.headers)

    def case(self, name, action):
        print("case: " + name, flush=True)
        item = {"id": name, "status": "running"}; self.report["cases"].append(item); self.save()
        try: item.update(status="passed", observation=action())
        except BaseException as error: item.update(status="failed", cause=str(error)); self.save(); raise
        self.save(); return item["observation"]

    def journeys(self, published):
        profiles, tokens = self.profiles()
        question = {"query": {"schemaVersion": 1, "dataset": {"id": "acme.processing.records", "schemaRevision": "processing-v1"},
                              "form": "aggregate", "parameters": [], "fields": [], "groupBy": ["customerCategory"],
                              "metrics": [{"alias": "mean", "metric": "processing.average-time-ms", "revision": "finished-valid-pairs-v1"},
                                          {"alias": "count", "metric": "processing.record-count", "revision": "v1"}],
                              "time": {"field": "startedAt", "from": "2026-11-02T00:00:00Z", "to": "2026-11-06T00:00:00Z", "timeZone": "UTC", "weekStart": "monday"},
                              "sort": [{"reference": "customerCategory", "direction": "ascending", "nulls": "last"}], "limit": 50},
                    "visualization": {"kind": "bar", "x": "customerCategory", "y": ["mean"]}, "parameterValues": {}}
        def expect(base, path, actor, status=200, body=None, method="GET", headers=None):
            actual, value, response_headers = self.request(base, path, tokens.get(actor), body, method, headers)
            require(actual == status, path + " returned " + str(actual) + ", expected " + str(status) + ": " + json.dumps(value))
            return value
        def seed(base):
            ids = {}
            for name, category, seconds, readers, actor in (
                ("shared-retail", "retail", 60, ["bob"], "alice"), ("private-retail", "retail", 180, [], "alice"),
                ("shared-enterprise", "enterprise", 300, ["bob"], "alice"), ("private-category", "restricted-category", 1200, [], "alice"),
                ("other-tenant", "retail", 900, [], "other-tenant")):
                from datetime import datetime, timedelta, timezone
                start = datetime(2026, 11, 3, 12, tzinfo=timezone.utc)
                value = expect(base, "/_acme/processing/records", actor, body={"customerCategory": category, "startedAt": start.isoformat(),
                    "completedAt": (start + timedelta(seconds=seconds)).isoformat(), "status": "completed", "readerIds": readers}, method="POST")
                ids[name] = value["id"]
            return ids
        def category(result, name):
            return next(row for row in result["rows"] if row["values"]["customerCategory"]["value"] == name)
        def value(row, metric):
            from decimal import Decimal
            return Decimal(row["values"][metric]["value"])
        with self.server(published, profiles, True, "enabled") as base:
            def loaded_case():
                loaded = expect(base, "/__qualification/loaded", "alice")
                for assembly in loaded["assemblies"]:
                    candidate = next(item for item in self.report["consumer"]["publishedComparisons"] if item["package"] == assembly["name"])
                    require(assembly["sha256"] == candidate["archiveSha256"], "Loaded bytes differ from retained archive")
                require(loaded["contributionCount"] == 1 and not loaded["providerConfigured"], "Unexpected contribution/provider composition")
                return loaded
            self.case("installed-module-bytes", loaded_case)
            def discovery():
                result = expect(base, "/_elsa/studio/dashboard-datasets", "alice")
                item = next(item for item in result["items"] if item["id"] == "acme.processing.records")
                require(any(field["id"] == "customerCategory" and field["businessDimension"] for field in item["fields"]), "Explicit business dimension not discovered")
                return result
            self.case("native-discovery", discovery)
            ids = self.case("native-business-record-create", lambda: seed(base))
            def author_oracle():
                author = expect(base, "/_elsa/studio/dashboard-queries/preview", "alice", body=question, method="POST")
                require(value(category(author, "retail"), "mean") == 120000 and value(category(author, "retail"), "count") == 2, "Author business oracle mismatch")
                return author
            self.case("author-numerical-oracle", author_oracle)
            def viewer_oracle():
                viewer = expect(base, "/_elsa/studio/dashboard-queries/preview", "bob", body=question, method="POST")
                require(value(category(viewer, "retail"), "mean") == 60000 and value(category(viewer, "retail"), "count") == 1, "Viewer business oracle mismatch")
                require("restricted-category" not in json.dumps(viewer) and ids["private-retail"] not in json.dumps(viewer), "Author values leaked to viewer")
                return viewer
            viewer = self.case("viewer-numerical-oracle", viewer_oracle)
            self.case("record-denied-control", lambda: expect(base, "/_acme/processing/records/" + ids["private-retail"], "bob", 404))
            def drill():
                result = expect(base, "/_elsa/studio/dashboard-queries/drill", "bob", body={"question": question, "descriptor": category(viewer, "retail")["drill"], "pageSize": 25}, method="POST")
                require([row["values"]["recordId"]["value"] for row in result["rows"]] == [ids["shared-retail"]], "Drill returned wrong or forbidden source records")
                return result
            self.case("native-authorized-drill", drill)
            def other_tenant():
                result = expect(base, "/_elsa/studio/dashboard-queries/preview", "other-tenant", body=question, method="POST")
                require(len(result["rows"]) == 1 and value(category(result, "retail"), "mean") == 900000, "Cross-tenant source isolation failed")
                return result
            self.case("tenant-isolation-control", other_tenant)
            def field_denied():
                result = expect(base, "/_elsa/studio/dashboard-datasets", "bob-no-category")
                require(all(field["id"] != "customerCategory" for item in result["items"] for field in item["fields"]), "Denied field remained discoverable")
                problem = expect(base, "/_elsa/studio/dashboard-queries/preview", "bob-no-category", 422, question, "POST")
                require(problem.get("code") == "UNSUPPORTED_QUERY" and "restricted-category" not in json.dumps(problem), "Wrong-cause field denial")
                record = expect(base, "/_acme/processing/records/" + ids["shared-retail"], "bob-no-category")
                require(record["customerCategory"] is None and not record["categoryAvailable"], "Ordinary record view leaked category")
                return {"catalog": result, "queryProblem": problem, "record": record}
            self.case("field-denied-control", field_denied)
            def no_data():
                result = expect(base, "/_elsa/studio/dashboard-queries/preview", "bob-no-data", body=question, method="POST")
                require(result["rows"] == [], "Aggregate grant bypassed record read authority")
                return result
            self.case("aggregate-without-record-control", no_data)
            def no_aggregate():
                result = expect(base, "/_elsa/studio/dashboard-datasets", "no-aggregate")
                require(result["items"] == [], "Record authority implied aggregate authority")
                return result
            self.case("record-without-aggregate-control", no_aggregate)
            self.case("anonymous-control", lambda: expect(base, "/_elsa/studio/dashboard-datasets", None, 401))
            saved = self.case("native-question-save", lambda: expect(base, "/_elsa/studio/questions", "alice", body={"name": "Processing time by customer category", "operationKey": "business-question-1", "definition": question, "tiles": []}, method="POST"))
            saved_id, saved_revision = saved["id"], saved["revision"]
            def refresh():
                refreshed = expect(base, "/_elsa/studio/questions/" + saved_id + "/execute", "alice", body={}, method="POST", headers={"If-Match": '"' + saved_revision + '"'})
                require(value(category(refreshed, "retail"), "mean") == 120000, "Saved question changed accepted meaning")
                return refreshed
            self.case("native-saved-question-refresh", refresh)
            self.case("private-definition-control", lambda: expect(base, "/_elsa/studio/questions/" + saved_id, "bob", 404))
        binaries_before = {path.name: sha(path) for path in published.glob("*.dll")}
        with self.server(published, profiles, False, "disabled") as base:
            def absent():
                result = expect(base, "/_elsa/studio/dashboard-datasets", "alice")
                require(result["items"] == [], "Disabled contribution remained available")
                problem = expect(base, "/_elsa/studio/dashboard-queries/preview", "alice", 404, question, "POST")
                require(problem.get("code") == "DATASET_UNAVAILABLE", "Disabled source silently substituted or failed for another cause")
                return {"catalog": result, "problem": problem}
            self.case("disabled-contribution-control", absent)
            def persistent_unavailable():
                observed = expect(base, "/_elsa/studio/questions/" + saved_id, "alice")
                require(observed["id"] == saved_id and observed["revision"] == saved_revision and observed["definition"] is None,
                        "Source removal destroyed or redisclosed the saved definition")
                return observed
            self.case("persistent-question-source-unavailable", persistent_unavailable)
        with self.server(published, profiles, True, "restored") as base:
            seed(base)
            def restored_refresh():
                restored = expect(base, "/_elsa/studio/questions/" + saved_id + "/execute", "alice", body={}, method="POST", headers={"If-Match": '"' + saved_revision + '"'})
                require(value(category(restored, "retail"), "mean") == 120000, "Restored source failed the independent oracle")
                return restored
            self.case("restored-module-native-refresh", restored_refresh)
        def common_bytes():
            require(binaries_before == {path.name: sha(path) for path in published.glob("*.dll")}, "Native profile transition changed installed common bytes")
            self.report["nativeProfileCommonBytesUnchanged"] = True
            return {"unchanged": True, "dllCount": len(binaries_before)}
        self.case("installed-common-bytes-unchanged", common_bytes)

    def execute(self):
        try:
            self.produce(); published = self.consumer(); self.journeys(published)
            validate_complete(self.report)
            require(not (self.root / "frontend-invocations.log").read_text(), "Frontend tools were invoked")
            self.report.update(status="passed", independentPackageWitness=True, exactCommitQualified=not self.report["preliminary"])
        except BaseException as error:
            self.report.update(status="failed", cause=str(error)); raise
        finally:
            self.save()
            print(json.dumps({key: self.report[key] for key in ("status", "source", "preliminary", "independentPackageWitness", "exactCommitQualified", "fullFeatureQualified")}
                             | {"report": str(self.root / "report.json")}), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--allow-dirty", action="store_true", help="Explicitly preliminary; does not qualify a source commit")
    options = parser.parse_args()
    require(re.fullmatch(r"\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?", options.version), "Use a unique local semantic version")
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(SystemExit(143)))
    Probe(options.output.resolve(), options.version, options.allow_dirty).execute()


if __name__ == "__main__": main()
