"""Read BuildKit's per-platform SPDX and SLSA statements from a digest-pinned OCI index."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import sys

SPDX = "https://spdx.dev/Document"
SLSA = "https://slsa.dev/provenance/v1"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def verify(reference, fetch, output, builder_run, application):
    repository, _, index_digest = reference.rpartition("@")
    require(repository and re.fullmatch(r"sha256:[0-9a-f]{64}", index_digest),
            "Supply an image reference pinned by its SHA-256 index digest")

    def read(kind, digest):
        require(re.fullmatch(r"sha256:[0-9a-f]{64}", digest), "Invalid OCI descriptor digest")
        data = fetch(kind, repository + "@" + digest)
        require("sha256:" + hashlib.sha256(data).hexdigest() == digest,
                f"Incorrect {kind} digest for {digest}")
        return json.loads(data)

    index = read("manifest", index_digest)
    platforms = {}
    attestations = {}
    for descriptor in index["manifests"]:
        annotations = descriptor.get("annotations", {})
        if annotations.get("vnd.docker.reference.type") == "attestation-manifest":
            child = annotations["vnd.docker.reference.digest"]
            attestations.setdefault(child, []).append(descriptor["digest"])
        else:
            target = descriptor["platform"]
            platform = target["os"] + "/" + target["architecture"]
            require(platform not in platforms, f"Duplicate runnable platform {platform}")
            platforms[platform] = descriptor["digest"]
    require(set(platforms) == {"linux/arm64", "linux/amd64"}, "Expected linux/arm64 and linux/amd64")
    require(len(set(platforms.values())) == len(platforms), "Platforms reference the same child")
    require(set(attestations) == set(platforms.values()), "Missing or unrelated platform attestation")

    documents = {}
    for platform, child in sorted(platforms.items()):
        image = read("manifest", child)
        config = read("blob", image["config"]["digest"])
        require(f"{config['os']}/{config['architecture']}" == platform,
                f"Image config disagrees with index platform {platform}")
        predicates = {}
        for digest in attestations[child]:
            manifest = read("manifest", digest)
            if "subject" in manifest:
                require(manifest["subject"]["digest"] == child, f"Incorrect manifest subject for {platform}")
            for layer in manifest["layers"]:
                kind = layer.get("annotations", {}).get("in-toto.io/predicate-type")
                if kind not in (SPDX, SLSA):
                    continue
                require(kind not in predicates, f"Duplicate {kind} for {platform}")
                require(layer.get("mediaType") == "application/vnd.in-toto+json",
                        f"Incorrect statement media type for {platform}")
                statement = read("blob", layer["digest"])
                require(statement.get("_type") in {"https://in-toto.io/Statement/v0.1",
                                                  "https://in-toto.io/Statement/v1"},
                        f"Invalid in-toto statement for {platform}")
                subjects = statement.get("subject", [])
                require(statement.get("predicateType") == kind and subjects and
                        all(s.get("digest", {}).get("sha256") == child[7:] for s in subjects),
                        f"Incorrect {kind} subject for {platform}")
                predicates[kind] = statement["predicate"]
            print(f"{platform} child={child} attestation={digest}")
        require(set(predicates) == {SPDX, SLSA}, f"Missing SPDX or SLSA statement for {platform}")
        sbom = predicates[SPDX]
        require(sbom.get("spdxVersion", "").startswith("SPDX-2.") and sbom.get("packages"),
                f"Missing SPDX package inventory for {platform}")
        purls = [ref.get("referenceLocator", "") for package in sbom["packages"]
                 for ref in package.get("externalRefs", []) if ref.get("referenceType") == "purl"]
        require(any(purl.startswith(("pkg:deb/", "pkg:apk/", "pkg:rpm/")) for purl in purls),
                f"Missing operating-system inventory for {platform}")
        require(any(purl.casefold().startswith(f"pkg:nuget/{application}@".casefold()) for purl in purls),
                f"Missing {application} inventory for {platform}")
        builder = predicates[SLSA].get("runDetails", {}).get("builder", {}).get("id", "")
        require(re.fullmatch(re.escape(builder_run.rstrip("/")) + r"/attempts/[1-9][0-9]*", builder),
                f"Unexpected SLSA builder for {platform}: {builder}")
        documents[platform.replace("/", "-") + ".spdx.json"] = sbom
    if output:
        output.mkdir(parents=True, exist_ok=True)
        for name, document in documents.items():
            (output / name).write_text(json.dumps(document, indent=2) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("reference")
    parser.add_argument("--application", required=True, help="Expected Elsa application package ID")
    parser.add_argument("--builder-run", required=True, help="Expected GitHub Actions producer run URL")
    parser.add_argument("--oci-layout", action="store_true", help="Read a local OCI layout with ORAS")
    parser.add_argument("--output", type=Path, help="Save validated SPDX documents in this directory")
    args = parser.parse_args()

    def fetch(kind, reference):
        command = ["oras", kind, "fetch"]
        if args.oci_layout:
            command.append("--oci-layout")
        if kind == "blob":
            command.extend(["--output", "-"])
        return subprocess.check_output([*command, "--", reference])

    try:
        verify(args.reference, fetch, args.output, args.builder_run, args.application)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        sys.exit(str(error))


if __name__ == "__main__":
    main()
