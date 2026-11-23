"""Rescan the immutable artifacts in Foundation's designated OSS releases."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import zipfile
import xml.etree.ElementTree as ET
from datetime import datetime, timedelta, timezone

ROOT = Path(__file__).resolve().parents[2]
REPO = "elsa-workflows/elsa-foundation"
PRODUCTS = {"elsa-workbench": "Elsa.Workbench", "elsa-studio": "Elsa.Studio.Web",
            "elsa-foundation-host": "Elsa.Foundation.Host"}
PLATFORMS = {"linux/amd64", "linux/arm64"}
LEGACY = {"sha256:73ef26e41760eaa4618a37cfbd7285002408f634e4089201b3864cb1f6147904",
          "sha256:1a4401d2f5bee207c3edbc51d4157ecb23ba796fb1dacc9e5ff6775f6c4f6ad0"}
sys.path.insert(0, str(ROOT / "tools/versioning/package-evidence"))
SCANNER = "0.122.1"
SPDX = "https://spdx.dev/Document"
SHA = re.compile(r"sha256:[0-9a-f]{64}")
CHECKPOINT = re.compile(r"contributor-preview-(\d{4}-\d{2}-\d{2})\.([1-9]\d*)")
STABLE = re.compile(r"v?\d+\.\d+\.\d+")


FAILURES = (ValueError, OSError, KeyError, TypeError, AttributeError, zipfile.BadZipFile, ET.ParseError, subprocess.SubprocessError)


class MissingInventory(ValueError):
    """A reachable registry returned an explicitly incomplete inventory set."""

    def __init__(self, message, missing):
        super().__init__(message)
        self.missing = set(missing)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def run(*args, timeout=120):
    return subprocess.check_output(args, timeout=timeout)


def sha(data):
    return "sha256:" + hashlib.sha256(data).hexdigest()


def release_tags(releases):
    published = [r for r in releases if not r["draft"]]
    previews = [r["tag_name"] for r in published if r["prerelease"] and CHECKPOINT.fullmatch(r["tag_name"])]
    stable = {r["tag_name"] for r in published if not r["prerelease"] and STABLE.fullmatch(r["tag_name"])}
    if previews:
        def order(tag):
            date, ordinal = CHECKPOINT.fullmatch(tag).groups()
            return datetime.strptime(date, "%Y-%m-%d"), int(ordinal)
        stable.add(max(previews, key=order))
        if "contributor-preview-2026-10-07.1" in previews:
            stable.add("contributor-preview-2026-10-07.1")
    require(stable, "No published compatibility checkpoint or stable release discovered")
    return stable


def checked_oci(repository, digest):
    require(SHA.fullmatch(digest), "Invalid OCI digest")
    data = run("oras", "manifest", "fetch", "--", f"{repository}@{digest}")
    require(sha(data) == digest, "OCI registry bytes differ from pinned digest")
    return json.loads(data)


def image_inventories(entry, directory):
    product, index = entry["product"], entry["index"]
    app = PRODUCTS[product]
    repository = "docker.io/elsaworkflows/" + product
    reference = f"{repository}@{index}"
    manifests = checked_oci(repository, index)["manifests"]
    children = {}
    attestations = []
    for descriptor in manifests:
        platform = descriptor["platform"]
        annotations = descriptor.get("annotations", {})
        if annotations.get("vnd.docker.reference.type") == "attestation-manifest":
            attestations.append(descriptor)
        else:
            target = platform["os"] + "/" + platform["architecture"]
            require(target not in children, "Duplicate platform")
            children[target] = descriptor["digest"]
    require(set(children) == PLATFORMS and children == entry["children"], "Unexpected platform subjects")
    coverage = {child: set() for child in children.values()}
    for descriptor in attestations:
        child = descriptor["annotations"]["vnd.docker.reference.digest"]
        manifest = checked_oci(repository, descriptor["digest"])
        coverage[child].update(layer.get("annotations", {}).get("in-toto.io/predicate-type")
                               for layer in manifest["layers"])
    if index in LEGACY:
        # The original metadata-only checkpoint names immutable legacy indexes. Recheck their predicates.
        require(all(kinds and SPDX not in kinds for kinds in coverage.values()), "Legacy predicate set changed")
        return None
    missing = {child for child, kinds in coverage.items() if SPDX not in kinds}
    if missing:
        raise MissingInventory("Index lacks an attached SPDX inventory for an expected platform", missing)
    commit, producer = entry["publisherCommit"], entry["producerRun"]
    require(re.fullmatch(r"[0-9a-f]{40}", commit) and type(producer) is int and producer > 0, "Invalid producer")
    path = ".github/workflows/studio-docker.yml" if product == "elsa-studio" else ".github/workflows/docker.yml"
    record = json.loads(run("gh", "api", f"repos/{REPO}/actions/runs/{producer}"))
    require(record["head_sha"] == commit and record["path"] == path and record["conclusion"] == "success"
            and record["head_branch"] == "main", "Unexpected image publisher run")
    if product == "elsa-studio":
        run("gh", "attestation", "verify", "oci://" + reference, "--repo", REPO, "--source-ref",
            "refs/heads/main", "--source-digest", commit, "--signer-workflow", REPO + "/" + path)
    else:
        require(attestations, "Missing Cosign subjects")
        for descriptor in attestations:
            run("cosign", "verify", "--experimental-oci11", "--new-bundle-format",
                "--certificate-oidc-issuer", "https://token.actions.githubusercontent.com",
                "--certificate-identity", "https://github.com/docker/github-builder/.github/workflows/build.yml@refs/tags/v1.16.0",
                "--certificate-github-workflow-repository", REPO, "--certificate-github-workflow-sha", commit,
                "--certificate-github-workflow-name", "Docker Images", "--certificate-github-workflow-ref", "refs/heads/main",
                repository + "@" + descriptor["digest"])
    run(sys.executable, ROOT / "tools/ci/verify_image_evidence.py", reference, "--application", app,
        "--builder-run", f"https://github.com/{REPO}/actions/runs/{producer}", "--output", directory, timeout=240)
    return {child: directory / (platform.replace("/", "-") + ".spdx.json") for platform, child in children.items()}


def package_inventory(entry, directory):
    # Use the publisher's consumer contract for both signature predicates and their common invocation.
    import verify
    name, version = entry["id"], entry["version"]
    require(all(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._+-]*", field) for field in (name, version)), "Invalid NuGet identity")
    require(entry["feed"] in (verify.FEED, "https://api.nuget.org/v3/index.json"), "Unexpected official feed")
    package = directory / "subject.nupkg"
    verify.download(entry["feed"], name, version, package)
    require("sha256:" + verify.sha256(package) == entry["sha256"] and (name, version) == verify.identity(package)[:2],
            "Downloaded package differs from release subject")
    try:
        bom = verify.signed_inventory(package)
    except verify.NoAttestations as error:
        raise MissingInventory(str(error), [entry["sha256"]]) from error
    output = directory / "signed.cdx.json"
    output.write_text(json.dumps(bom))
    return output


def database(status):
    require(status["valid"] is True, "Grype database invalid")
    built = datetime.fromisoformat(status["built"].replace("Z", "+00:00"))
    require(timedelta() <= datetime.now(timezone.utc) - built <= timedelta(days=5), "Grype database stale or future dated")
    checksum = re.search(r"[?&]checksum=sha256(?:%3A|:)([a-f0-9]{64})(?:&|$)", status["from"])
    require(checksum, "Grype database has no checksum")
    return {"built": status["built"], "schema": status["schemaVersion"], "sha256": checksum[1]}


def scan(inventory, revision):
    result = json.loads(run("grype", "sbom:" + str(inventory), "-o", "json", timeout=180))
    desc = result["descriptor"]
    require(desc["version"] == SCANNER and database(desc["db"]["status"]) == revision, "Scanner or database changed")
    matches = {}
    for match in result["matches"]:
        component, advisory = match["artifact"], match["vulnerability"]
        identity = component["purl"] or f"{component['type']}:{component['name']}@{component['version']}"
        matches[identity, advisory["id"]] = {"component": identity, "advisory": advisory["id"],
                                              "severity": advisory["severity"], "fix": advisory["fix"]}
    return {"state": "findings" if matches else "no-findings", "inventory": sha(inventory.read_bytes()),
            "matches": [matches[key] for key in sorted(matches)]}


def subjects(tag, release):
    records = []
    for kind, entries in release.items():
        require(kind in ("packages", "images"), f"Unknown release artifact family {kind}")
        for entry in entries:
            if kind == "images":
                require(set(entry["children"]) == PLATFORMS and SHA.fullmatch(entry["index"]), "Invalid image manifest")
                expected = [(platform, digest) for platform, digest in entry["children"].items()]
                product = entry["product"]
            else:
                expected = [("nupkg", entry["sha256"])]
                product = f"{entry['id']} {entry['version']}"
            for platform, digest in expected:
                require(SHA.fullmatch(digest), "Invalid release subject digest")
                record = {"release": tag, "product": product, "platform": platform, "digest": digest,
                          "state": "error", "detail": "Verification did not finish"}
                if kind == "images":
                    record["index"] = entry["index"]
                records.append((record, entry, kind))
    require(records and len({(row["product"], row["platform"]) for row, _, _ in records}) == len(records),
            "Release subject list is empty or ambiguous")
    return records


def scan_subject(row, path, revision, scanned):
    digest, inventory = row["digest"], sha(path.read_bytes())
    # Identical independently versioned packages can belong to more than one release.
    if digest not in scanned:
        scanned[digest] = scan(path, revision)
    result = scanned[digest]
    require(result["inventory"] == inventory, "Verified releases disagree on the inventory for one subject")
    row.update(state=result["state"], inventory=inventory, matchCount=len(result["matches"]))
    del row["detail"]


def monitor(report_file):
    catalog = json.loads(Path(__file__).with_name("supported.json").read_text())
    reports, work = [], []
    report = {"scanner": "grype " + SCANNER, "database": None, "results": reports, "scans": {}, "errors": []}

    def save():
        staging = report_file.with_suffix(".tmp")
        staging.write_text(json.dumps(report, indent=2) + "\n")
        staging.replace(report_file)

    try:
        pages = json.loads(run("gh", "api", "--paginate", "--slurp", f"repos/{REPO}/releases?per_page=100"))
        selected = release_tags([item for page in pages for item in page])
        for tag in sorted(selected):
            if tag not in catalog:
                report["errors"].append(f"No reviewed immutable subject list for {tag}")
                continue
            work.extend(subjects(tag, catalog[tag]))
        reports.extend(row for row, _, _ in work)
        save()
        run("grype", "db", "update", timeout=600)
        revision = database(json.loads(run("grype", "db", "status", "-o", "json")))
        report["database"] = revision
        save()
        for tag in sorted(selected & catalog.keys()):
            entries = catalog[tag]
            for kind in ("images", "packages"):
                for entry in entries[kind]:
                    rows = [r for r, e, _ in work if e is entry]
                    try:
                        with tempfile.TemporaryDirectory() as temp:
                            directory = Path(temp)
                            paths = (image_inventories(entry, directory) if kind == "images" else
                                     {entry["sha256"]: package_inventory(entry, directory)})
                            for row in rows:
                                if paths is None:
                                    row.update(state="unavailable", detail="Checkpoint index predates attached SPDX inventories")
                                else:
                                    try:
                                        scan_subject(row, paths[row["digest"]], revision, report["scans"])
                                    except FAILURES as error:
                                        row["detail"] = str(error)
                                save()
                    except FAILURES as error:
                        for row in rows:
                            if row["state"] == "error":
                                row["detail"] = str(error)
                                if isinstance(error, MissingInventory):
                                    if row["digest"] in error.missing:
                                        row["state"] = "missing"
                                    else:
                                        row["detail"] = "The complete image index could not be verified: another platform lacks SPDX"
                        save()
    except FAILURES as error:
        report["errors"].append(str(error))
    save()
    print(f"Grype {SCANNER}; advisory database: {report['database']}")
    for row in reports:
        print(f"- {row['release']} {row['product']} {row['platform']} {row['digest']} — {row['state']}")
    for error in report["errors"]:
        print(error, file=sys.stderr)
    return int(bool(report["errors"]) or any(row["state"] in {"error", "missing"} for row in reports))


if __name__ == "__main__":
    os.environ["GRYPE_DB_AUTO_UPDATE"] = "false"
    sys.exit(monitor(Path(sys.argv[1])))
