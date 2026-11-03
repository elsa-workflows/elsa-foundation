#!/usr/bin/env python3
"""Run genuine Studio archive/consumer/lifecycle proofs without publishing."""

import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

from verify_studio_nuget_packages import verify_packages

REPO = Path(__file__).resolve().parents[2]
PROBES = REPO / "tools/studio"
COMPOSITION = PROBES / "Composition.Probe"
sys.path.insert(0, str(COMPOSITION))
from artifacts import Artifacts, CUSTOM_ID, identity
from journeys import require_package_update


def check(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def result_gate(code, data, tool_hash, stage="complete", error=None):
    check(data.get("toolSha256") == tool_hash, "Child tool hash differs")
    check(code == (1 if error else 0) and data.get("status") == ("failed" if error else "passed")
          and data.get("stage") == stage, "Child exit/status/stage differs")
    if error:
        check(data.get("error") == error, "Child failed for a different reason")


def audit(root, shim_directory):
    for name in ["node", "npm", "pnpm", "npx"]:
        path = root / shim_directory / name
        check(path.is_file() and os.access(path, os.X_OK), "Consumer tool denial is absent: " + str(path))
    for path in root.glob("*frontend-invocations.log"):
        check(not path.read_text().strip(), "Consumer invoked frontend tooling: " + str(path))


def status_object(text):
    decoder = json.JSONDecoder()
    rows = []
    for index, character in enumerate(text):
        if character != "{" or (index and text[index - 1] != "\n"):
            continue
        try:
            value, _ = decoder.raw_decode(text[index:])
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict) and "status" in value:
            rows.append(value)
    check(len(rows) == 1, "Expected one lifecycle status object")
    return rows[0]


def self_test():
    good = {"status": "failed", "stage": "standard-runtime", "error": "intended", "toolSha256": "tool"}
    result_gate(1, good, "tool", "standard-runtime", "intended")
    for field, bad in [("status", "passed"), ("stage", "restore"), ("error", "unrelated network failure"), ("toolSha256", "other")]:
        try:
            result_gate(1, dict(good, **{field: bad}), "tool", "standard-runtime", "intended")
        except RuntimeError:
            pass
        else:
            raise AssertionError("Wrong child observation was accepted: " + field)
    try:
        result_gate(0, good, "tool", "standard-runtime", "intended")
    except RuntimeError:
        pass
    else:
        raise AssertionError("Exit0 was accepted as a failed control")
    print("Studio consumer aggregation guard self-test passed.")


class Qualification:
    def __init__(self, args):
        self.args = args
        self.root = args.output.resolve() if args.output else Path(tempfile.mkdtemp(prefix="studio-consumer-ci-")).resolve()
        if args.output:
            self.root.mkdir(parents=True, exist_ok=False)
        self.source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPO, text=True).strip()
        self.report = {"root": str(self.root), "status": "running", "stage": "prepare", "toolSourceRevision": self.source,
                       "toolSha256": sha(__file__), "observations": {}, "platform": sys.platform}
        self.report["toolInputHashes"] = {str(p.relative_to(REPO)): sha(p) for folder in [REPO / "tools/ci", *[PROBES / n for n in ["ApiPackageClosure.Probe", "HostingAssets.Probe", "Composition.Probe", "HostingReuse.Probe"]]]
                                          for p in folder.rglob("*") if p.is_file() and not any(part in {"__pycache__", "bin", "obj"} for part in p.parts)}
        self.packages = {}
        print("Artifacts: " + str(self.root), flush=True)

    def save(self):
        (self.root / "report.json").write_text(json.dumps(self.report, indent=2) + "\n")

    def run(self, label, command, expected=0):
        self.report["stage"] = label
        self.save()
        print(label, flush=True)
        with (self.root / (label + ".log")).open("w") as log:
            result = subprocess.run(command, cwd=REPO, stdout=log, stderr=subprocess.STDOUT)
        if expected is not None:
            check(result.returncode == expected, f"{label} exited {result.returncode}; see {self.root / (label + '.log')}")
        return result.returncode

    def record(self, label, code, child, expected=False):
        data = json.loads((child / "report.json").read_text())
        self.report["observations"][label] = {"exitCode": code, "status": data["status"], "stage": data["stage"],
                                              "report": str(child / "report.json"), "reportSha256": sha(child / "report.json"),
                                              "expectedFailureObserved": expected}
        self.save()
        return data

    def prepare(self):
        supplied = bool(self.args.packages)
        check(not supplied or (self.args.modularity_packages and self.args.reference_web), "Supplied Studio candidates require matching --modularity-packages and --reference-web")
        if supplied:
            studio, modules, web = self.args.packages.resolve(), self.args.modularity_packages.resolve(), self.args.reference_web.resolve()
            verify_packages(REPO, studio)
            self.report["producer"] = "supplied genuine archives/reference distribution; repository metadata retained separately"
        else:
            p = Artifacts(self.root / "producer-batch", version=self.args.version)
            p.produce()
            studio = modules = p.feed
            web = p.root / "reference-web-published"
            self.report.update(producer="owned committed production snapshot", producerSource=self.source)
        self.studio, self.all = self.root / "studio-candidates", self.root / "composition-candidates"
        self.studio.mkdir()
        self.all.mkdir()
        inventory = json.loads((REPO / "tools/ci/studio-package-identities.json").read_text())
        ids = {x["id"] for x in inventory["packable_projects"]}
        support_ids = {item["id"] for item in inventory["support_projects"]}
        for folder, selected in [(studio, ids), (modules, support_ids)]:
            for path in folder.rglob("*.nupkg"):
                name, version = identity(path)
                if name not in selected:
                    continue
                check(name not in self.packages, "Duplicate candidate: " + name)
                destination = self.all / path.name
                shutil.copy2(path, destination)
                if name in ids:
                    shutil.copy2(path, self.studio / path.name)
                with zipfile.ZipFile(path) as archive:
                    doc = ET.fromstring(archive.read(next(n for n in archive.namelist() if n.endswith(".nuspec"))))
                    repository = next((dict(n.attrib) for n in doc.iter() if n.tag.rsplit("}", 1)[-1] == "repository"), {})
                self.packages[name] = {"path": str(destination), "version": version, "archiveSha256": sha(path), "repository": repository}
        check(set(self.packages) == ids | support_ids, "Missing genuine candidates")
        versions = {v["version"] for v in self.packages.values()}
        check(len(versions) == 1, "Candidate versions differ")
        self.version = versions.pop()
        if self.args.expected_producer_commit:
            check(self.source == self.args.expected_producer_commit and all(p["repository"].get("commit") == self.source for p in self.packages.values()), "Candidate/tool source provenance differs from CI producer commit")
        verify_packages(REPO, self.studio, self.version)
        self.web = self.root / "reference-web"
        shutil.copytree(web, self.web)
        dependencies = json.loads((self.web / "Elsa.Studio.Web.deps.json").read_text())["libraries"]
        check("Elsa.Studio.Web/" + self.version in dependencies, "Reference Web has a different build stamp")
        matched = {}
        for name, row in self.packages.items():
            dll = self.web / (name + ".dll")
            if not dll.exists():
                continue  # Reference distribution owns its selected modules, including omitted samples.
            with zipfile.ZipFile(row["path"]) as archive:
                check(hashlib.sha256(archive.read("lib/net10.0/" + name + ".dll")).hexdigest() == sha(dll), "Reference Web does not match candidate DLL: " + name)
            matched[name] = sha(dll)
        check({"Elsa.Studio.Hosting", "Elsa.Studio.Api", "Elsa.Studio.Core", "Elsa.Studio.Hosting.Themes", "Elsa.Studio.Hosting.Nuplane", "Elsa.Studio.ConsoleStream", "Elsa.Modularity.Core", "Elsa.Modularity.Nuplane"} <= set(matched), "Reference Web is not the actual combined distribution")
        self.report.update(version=self.version, packages=self.packages, referenceWebCandidateDllHashes=matched)
        self.save()

    def bound_packages(self, data, omitted_api=False):
        for name, row in data["packages"].items():
            if omitted_api and name == "Elsa.Studio.Api":
                check(data["negativeControl"]["originalArchiveSha256"] == self.packages[name]["archiveSha256"], "API mutation original differs")
            else:
                check(row["archiveSha256"] == self.packages[name]["archiveSha256"], "Child consumed another candidate: " + name)
        check(data["version"] == self.version, "Child candidate version differs")

    def consumers(self):
        verifier = REPO / "tools/ci/verify_studio_nuget_packages.py"
        self.run("archive-controls", [sys.executable, str(verifier), "--packages", str(self.studio), "--version", self.version,
                                      "--controls-output", str(self.root / "archive-controls")])
        archive_controls = self.record("archive-controls", 0, self.root / "archive-controls")
        result_gate(0, archive_controls, sha(verifier))
        expected_controls = {"api-core-omitted", "console-runtime-omitted", "framework-omitted", "vendor-missing", "module-css-missing", "lazy-chunk-missing", "entry-empty", "sdk-changed", "dependency-exclusive-minimum", "dependency-empty-range"}
        check(set(archive_controls["observations"]) == expected_controls | {"positive", "restored"}, "Archive control matrix is incomplete")
        check(all(archive_controls["observations"][name]["expectedFailureObserved"] and archive_controls["observations"][name]["status"] == "failed" for name in expected_controls), "Archive failure was not observed precisely")
        api = PROBES / "ApiPackageClosure.Probe/qualify.py"
        for label, negative in [("api-normal", False), ("api-omission", True), ("api-restored", False)]:
            child = self.root / label
            code = self.run(label, [sys.executable, str(api), "--packages", str(self.all), "--output", str(child), *(["--omit-api-core-dependency"] if negative else [])], expected=None)
            data = self.record(label, code, child)
            if negative:
                check(code == 1 and data["status"] == "failed" and data["stage"] == "publish" and data["toolSha256"] == sha(api)
                      and data["negativeControl"]["expectedFailureObserved"] and not data["restoredCoreLibraries"]
                      and data["candidateLibraryTypes"]["Elsa.Studio.Api"] == "package" and data["negativeControl"]["payloadUnchanged"], "API omission failed for an unrelated reason")
                check("CS0234" in (child / "publish.log").read_text(), "API omission lost its missing-Core diagnostic")
            else:
                result_gate(code, data, sha(api))
            check(data["frontendInvocations"] == 0, "API consumer invoked frontend tooling")
            audit(child, "denied-tools")
            self.bound_packages(data, negative)
            self.record(label, code, child, negative)
        hosting = PROBES / "HostingAssets.Probe/qualify.py"
        cases = [("hosting-normal", [], None), ("hosting-vendor", ["--omit-published-vendor"], "HTTP asset mismatch: /studio/vendor/react.js (status 404)"),
                 ("hosting-runtime-missing", ["--runtime-script-fault", "missing"], "Runtime configuration script has status 404"),
                 ("hosting-runtime-empty", ["--runtime-script-fault", "empty"], "Runtime configuration script did not assign window.__ELSA_STUDIO_RUNTIME__"),
                 ("hosting-restored", [], None)]
        for label, flags, error in cases:
            child = self.root / label
            code = self.run(label, [sys.executable, str(hosting), "--packages", str(self.studio), "--support-packages", str(self.all), "--output", str(child), *flags], expected=None)
            data = self.record(label, code, child)
            result_gate(code, data, sha(hosting), "standard-runtime" if error else "complete", error)
            check(data["browserDriverSha256"] == sha(hosting.with_name("browser.mjs")), "Hosting browser driver differs")
            self.bound_packages(data)
            audit(child, "denied-frontend")
            if not error:
                check(data["frontendInvocations"] == 0 and data["samePathCollisionRejected"] and set(data["runtime"]) == {"standard", "fallback", "optout"}, "Incomplete Hosting profiles")
                check(all(row["runtimeConfig"]["hostId"] == "package-consumer-proof" for row in data["runtime"].values()), "Hosting runtime configuration not observed")
            self.record(label, code, child, bool(error))
        child = self.root / "composition"
        self.run("composition", [sys.executable, str(COMPOSITION / "qualify.py"), "--packages", str(self.all), "--reference-web", str(self.web), "--output", str(child)])
        data = self.record("composition", 0, child)
        result_gate(0, data, sha(COMPOSITION / "artifacts.py"))
        self.bound_packages(data)
        check(set(data["profiles"]) == {"minimal", "themes", "administration", "reference"} and all(p["status"] == "qualified" and p["frontendInvocations"] == 0 for p in data["profiles"].values()), "Incomplete composition profiles")
        for profile in data["profiles"]: audit(child, profile + "-denied")
        for name, value in data["qualificationInputHashes"].items():
            check(value == sha(COMPOSITION / name), "Composition tool/fixture differs: " + name)
        expected_hash = "sha512:" + base64.b64encode(hashlib.sha512((child / f"hidden-build-feed/{CUSTOM_ID}.2.0.0.nupkg").read_bytes()).digest()).decode()
        for name, row in data["results"]["liveStream"].items():
            require_package_update(row["reconcile"], expected_hash)
            if name == "disabledAutoReload": check(row["status"] == "failed" and row["expectedFailureObserved"] and row["stage"] == "serving-generation", "Reload control is invalid")
            else: check(row["status"] == "passed" and row["observed"]["cleanDisconnect"], "Live stream positive is incomplete")
        guard = data["results"]["noUpdateControlGuard"]
        check(guard["status"] == "failed" and guard["expectedFailureObserved"] and guard["stage"] == "expected-package-update", "No-update guard was not exercised")

    def lifecycle(self):
        specs = [("reuse", "Runner/HostingReuse.Probe.csproj", "HostingReuse.Probe", "--disable-auto-reload", "An update did not replace the serving shell generation.", "completed", 4, 1),
                 ("pruning", "StorePruning/StorePruning.csproj", "StorePruning", "--release-reader-before-read", "Admitted HTTP range response failed.", "groups", 6, 4)]
        for label, relative, assembly, flag, diagnostic, groups, positive_count, negative_count in specs:
            project = PROBES / "HostingReuse.Probe" / relative
            self.run(label + "-restore", ["dotnet", "restore", str(project), "--locked-mode"])
            self.run(label + "-build", ["dotnet", "build", str(project), "-c", "Release", "--no-restore"])
            dll = project.parent / ("bin/Release/net10.0/" + assembly + ".dll")
            check(dll.exists(), "Lifecycle build output is absent: " + str(dll))
            for variant, negative in [("normal", False), ("control", True), ("restored", False)]:
                name = label + "-" + variant
                code = self.run(name, ["dotnet", str(dll), *([flag] if negative else [])], expected=None)
                data = status_object((self.root / (name + ".log")).read_text())
                check(code == (1 if negative else 0) and data["status"] == ("failed" if negative else "passed")
                      and len(data[groups]) == (negative_count if negative else positive_count), "Lifecycle outcome differs: " + name)
                if negative: check(diagnostic in data.get("error", ""), "Lifecycle control failed for an unrelated reason: " + name)
                self.report["observations"][name] = {"exitCode": code, "status": data["status"], "expectedFailureObserved": negative, "observed": data, "executedAssemblySha256": sha(dll)}
                self.save()

    def retain_evidence(self):
        """Copy receipts/graphs and private mutants, without caches or published trees."""
        selected = []
        for path in self.root.rglob("*"):
            if not path.is_file():
                continue
            relative = path.relative_to(self.root)
            if "evidence" in relative.parts:
                continue
            if (path.suffix in {".log", ".png"} or path.name in {"report.json", "project.assets.json", "packages.lock.json"}
                    or path.name.endswith(("browser.json", "-reconcile.json"))):
                selected.append(path)
            elif path.name == "Program.cs" and any("consumer" in part or "stream-client-source" in part for part in relative.parts):
                selected.append(path)
        archive_report = self.root / "archive-controls/report.json"
        if archive_report.exists():
            archive_data = json.loads(archive_report.read_text())
            for row in archive_data.get("observations", {}).values():
                receipt = Path(row["report"]).resolve()
                check(receipt.is_relative_to(self.root) and receipt.is_file() and sha(receipt) == row["reportSha256"], "Archive child receipt is missing, changed or outside this run")
                selected.append(receipt)
            mutations = archive_data.get("mutations", {})
            for label, row in mutations.items():
                selected.append(self.root / "archive-controls" / label / Path(self.packages[row["package"]]["path"]).name)
        api = self.root / "api-omission/report.json"
        if api.exists():
            wanted = json.loads(api.read_text()).get("negativeControl", {}).get("mutatedArchiveSha256")
            if wanted:
                candidates = (self.root / "api-omission").rglob("*.nupkg")
                selected.append(next(path for path in candidates if sha(path) == wanted))
        evidence = self.root / "evidence"
        evidence.mkdir(exist_ok=True)
        index = {}
        for path in sorted(set(selected)):
            relative = path.relative_to(self.root)
            target = evidence / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)
            index[relative.as_posix()] = sha(target)
        (evidence / "index.json").write_text(json.dumps(index, indent=2) + "\n")
        summary = os.environ.get("GITHUB_STEP_SUMMARY")
        if summary:
            with open(summary, "a") as output:
                output.write("\nStudio package qualification: **" + self.report["status"] + "**\n\n")
                output.write("| Observation | Status | Expected failure observed |\n| --- | --- | --- |\n")
                for name, row in self.report["observations"].items():
                    output.write(f"| {name} | {row['status']} | {row.get('expectedFailureObserved', False)} |\n")
                if self.report.get("error"):
                    output.write("\nFailure: " + self.report["error"].replace("\n", " ") + "\n")

    def execute(self):
        code = 1
        try:
            self.prepare()
            self.consumers()
            self.lifecycle()
            check(all(sha(row["path"]) == row["archiveSha256"] for row in self.packages.values()), "Original candidate changed")
            self.report.update(status="passed", stage="complete", frontendInvocations=0)
            code = 0
        except Exception as error:
            self.report.update(status="failed", error=str(error))
        finally:
            self.save()
            try:
                self.retain_evidence()
            except Exception as error:
                self.report.update(status="failed", evidenceError=str(error))
                code = 1
                self.save()
            print(json.dumps({key: self.report.get(key) for key in ["root", "status", "stage", "error"]}), flush=True)
        return code


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, help="New owned qualification directory")
    parser.add_argument("--packages", type=Path, help="Actual22Studio candidate archives; otherwise produce a committed snapshot")
    parser.add_argument("--modularity-packages", type=Path, help="Matching actual mandatory support archives from the Studio inventory")
    parser.add_argument("--reference-web", type=Path, help="Matching actual published reference distribution")
    parser.add_argument("--expected-producer-commit", help="CI producer/tool commit required by all supplied archive metadata")
    parser.add_argument("--version", default="4.0.0-studio-ci-qualification", help="Local snapshot candidate version")
    parser.add_argument("--self-test", action="store_true", help="Exercise precise child-result rejection without running consumers")
    args = parser.parse_args()
    if args.self_test:
        self_test()
    else:
        raise SystemExit(Qualification(args).execute())
