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
MANIFEST = "application/vnd.oci.image.manifest.v1+json"
CONFIG = "application/vnd.oci.image.config.v1+json"
STATEMENT = "application/vnd.in-toto+json"
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
            yield kind, name, identity.qualifiers.get("arch")


def resolved_build_input(dependency):
    if not isinstance(dependency, dict):
        return False
    uri, hashes = dependency.get("uri"), dependency.get("digest")
    return (isinstance(uri, str) and re.fullmatch(r"[A-Za-z][A-Za-z0-9+.-]*:[^\s]+", uri)
            and isinstance(hashes, dict) and bool(hashes)
            and all(isinstance(value, str) and re.fullmatch(r"[0-9a-f]+", value)
                    and len(value) % 2 == 0 and (key != "sha256" or len(value) == 64)
                    for key, value in hashes.items()))


def verify(reference, fetch, output, builder_run, application, validate_spdx):
    repository, _, index_digest = reference.rpartition("@")
    require(repository and re.fullmatch(r"sha256:[0-9a-f]{64}", index_digest),
            "Supply an image reference pinned by its SHA-256 index digest")

    def read(kind, digest, descriptor=None, media_type=None):
        require(re.fullmatch(r"sha256:[0-9a-f]{64}", digest), "Invalid OCI descriptor digest")
        data = fetch(kind, repository + "@" + digest)
        require("sha256:" + hashlib.sha256(data).hexdigest() == digest,
                f"Incorrect {kind} digest for {digest}")
        if descriptor is not None:
            require(descriptor.get("mediaType") == media_type and type(descriptor.get("size")) is int
                    and descriptor["size"] == len(data), f"Invalid {kind} descriptor for {digest}")
        return json.loads(data)

    index = read("manifest", index_digest)
    require(index.get("schemaVersion") == 2 and index.get("mediaType") == "application/vnd.oci.image.index.v1+json",
            "Expected an OCI image index")
    platforms = {}
    attestations = {}
    for descriptor in index["manifests"]:
        annotations = descriptor.get("annotations", {})
        target = descriptor["platform"]
        if annotations.get("vnd.docker.reference.type") == "attestation-manifest":
            require(target.get("os") == "unknown" and target.get("architecture") == "unknown",
                    "An attestation must not be selectable as a runnable platform")
            child = annotations["vnd.docker.reference.digest"]
            attestations.setdefault(child, []).append(descriptor)
        else:
            platform = target["os"] + "/" + target["architecture"]
            require(platform not in platforms, f"Duplicate runnable platform {platform}")
            platforms[platform] = descriptor
    require(set(platforms) == {"linux/arm64", "linux/amd64"}, "Expected linux/arm64 and linux/amd64")
    children = {descriptor["digest"] for descriptor in platforms.values()}
    require(len(children) == len(platforms), "Platforms reference the same child")
    require(set(attestations) == children, "Missing or unrelated platform attestation")

    documents = {}
    for platform, descriptor in sorted(platforms.items()):
        child = descriptor["digest"]
        image = read("manifest", child, descriptor, MANIFEST)
        require(image.get("schemaVersion") == 2 and image.get("mediaType") == MANIFEST
                and isinstance(image.get("layers"), list) and image["layers"], f"Invalid runnable manifest for {platform}")
        config = read("blob", image["config"]["digest"], image["config"], CONFIG)
        require(f"{config['os']}/{config['architecture']}" == platform,
                f"Image config disagrees with index platform {platform}")
        rootfs = config.get("rootfs", {})
        diff_ids = rootfs.get("diff_ids", [])
        require(rootfs.get("type") == "layers" and isinstance(diff_ids, list)
                and len(diff_ids) == len(image["layers"])
                and all(isinstance(value, str) and re.fullmatch(r"sha256:[0-9a-f]{64}", value) for value in diff_ids),
                f"Image root filesystem disagrees with its layers for {platform}")
        baseline = "v1" if platform == "linux/amd64" else "v8"
        for target in (descriptor["platform"], config):
            require(target.get("variant", baseline) in ("", baseline)
                    and not target.get("os.features") and not target.get("os.version"),
                    f"Expected the baseline {platform} platform")
        predicates = {}
        for attestation in attestations[child]:
            digest = attestation["digest"]
            manifest = read("manifest", digest, attestation, MANIFEST)
            require(manifest.get("schemaVersion") == 2 and manifest.get("mediaType") == MANIFEST,
                    f"Invalid attestation manifest for {platform}")
            if "subject" in manifest:
                require(all(manifest["subject"].get(key) == descriptor[key] for key in ("mediaType", "size", "digest")),
                        f"Incorrect manifest subject for {platform}")
            for layer in manifest["layers"]:
                kind = layer.get("annotations", {}).get("in-toto.io/predicate-type")
                if kind not in (SPDX, SLSA):
                    continue
                require(kind not in predicates, f"Duplicate {kind} for {platform}")
                statement = read("blob", layer["digest"], layer, STATEMENT)
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
        native = {"amd64", "x86_64"} if platform == "linux/amd64" else {"arm64", "aarch64"}
        os_arches = {arch for kind, _, arch in identities if kind in {"deb", "apk", "rpm"}}
        require(os_arches & native and os_arches <= native | {"all", "noarch"},
                f"Missing or incompatible native operating-system inventory for {platform}")
        require(any(kind == "nuget" and name == application.casefold() for kind, name, _ in identities),
                f"Missing {application} inventory for {platform}")
        definition = predicates[SLSA].get("buildDefinition", {})
        parameters = definition.get("externalParameters", {})
        dependencies = definition.get("resolvedDependencies", [])
        details = predicates[SLSA].get("runDetails", {})
        require(definition.get("buildType") == BUILDKIT
                and isinstance(parameters.get("configSource", {}).get("path"), str)
                and parameters["configSource"]["path"].strip()
                and parameters.get("request", {}).get("frontend") in {"dockerfile.v0", "gateway.v0"}
                and isinstance(dependencies, list) and dependencies and all(resolved_build_input(d) for d in dependencies)
                and isinstance(details.get("metadata", {}).get("invocationId"), str)
                and details["metadata"]["invocationId"].strip(), f"Incomplete SLSA build definition for {platform}")
        builder = details.get("builder", {}).get("id", "")
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
