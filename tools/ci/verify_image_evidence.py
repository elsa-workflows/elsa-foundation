"""Read BuildKit's per-platform SPDX and SLSA statements from a digest-pinned OCI index."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
from packageurl import PackageURL

SPDX = "https://spdx.dev/Document"
SLSA = "https://slsa.dev/provenance/v1"
BUILDKIT = "https://github.com/moby/buildkit/blob/master/docs/attestations/slsa-definitions.md"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def package_identities(packages):
    """Read versioned OS and NuGet identities that agree with their SPDX package."""
    for package in packages:
        for ref in package.get("externalRefs", []):
            if ref.get("referenceType") != "purl":
                continue
            locator = ref.get("referenceLocator", "")
            try:
                identity = PackageURL.from_string(locator)
            except ValueError:
                continue
            if identity.to_string() != locator or identity.subpath:
                continue
            kind, name, version = identity.type, identity.name.casefold(), identity.version
            if not version or name != package.get("name", "").casefold() or version != package.get("versionInfo"):
                continue
            if kind == "nuget":
                if identity.namespace or not re.fullmatch(r"[0-9]+(?:\.[0-9]+){0,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?", version):
                    continue
            elif kind not in {"deb", "rpm", "apk"}:
                continue
            yield kind, name


def verify(reference, fetch, output, builder_run, application, validate_spdx):
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
    require(index.get("schemaVersion") == 2 and index.get("mediaType") == "application/vnd.oci.image.index.v1+json",
            "Expected an OCI image index")
    platforms = {}
    attestations = {}
    for descriptor in index["manifests"]:
        annotations = descriptor.get("annotations", {})
        if annotations.get("vnd.docker.reference.type") == "attestation-manifest":
            platform = descriptor.get("platform", {})
            require(platform.get("os") == "unknown" and platform.get("architecture") == "unknown",
                    "An attestation must not be selectable as a runnable platform")
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
        require(image.get("schemaVersion") == 2 and image.get("mediaType") == "application/vnd.oci.image.manifest.v1+json"
                and isinstance(image.get("layers"), list) and image["layers"], f"Invalid runnable manifest for {platform}")
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
        validate_spdx(sbom)
        require(sbom.get("spdxVersion") == "SPDX-2.3" and sbom.get("packages"),
                f"Missing SPDX package inventory for {platform}")
        identities = set(package_identities(sbom["packages"]))
        require(any(kind in {"deb", "apk", "rpm"} for kind, _ in identities),
                f"Missing operating-system inventory for {platform}")
        require(("nuget", application.casefold()) in identities,
                f"Missing {application} inventory for {platform}")
        definition = predicates[SLSA].get("buildDefinition", {})
        require(definition.get("buildType") == BUILDKIT and isinstance(definition.get("externalParameters"), dict),
                f"Incomplete SLSA build definition for {platform}")
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
    parser.add_argument("--spdx-schema", type=Path, default=os.environ.get("ELSA_IMAGE_SPDX_SCHEMA"),
                        help="Path to the official SPDX 2.3.1 JSON schema")
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
        require(args.spdx_schema, "Supply --spdx-schema or set ELSA_IMAGE_SPDX_SCHEMA")
        expected = "4126dc29f15e92feec92e9622f4939131ad58b125d6f578aa93e980a6e6f3212"
        require(hashlib.sha256(args.spdx_schema.read_bytes()).hexdigest() == expected, "Unexpected SPDX schema digest")

        def validate_spdx(document):
            subprocess.run(["jsonschema", "validate", str(args.spdx_schema), "-"],
                           input=json.dumps(document).encode(), check=True, stdout=subprocess.DEVNULL)

        verify(args.reference, fetch, args.output, args.builder_run, args.application, validate_spdx)
    except (ValueError, KeyError, OSError, subprocess.CalledProcessError) as error:
        sys.exit(str(error))


if __name__ == "__main__":
    main()
