#!/usr/bin/env python3
"""Verify the manual Studio qualification bundles and publish versioned candidates."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tarfile
from zipfile import BadZipFile, ZipFile


REPOSITORY = "elsa-workflows/elsa-foundation"
IMAGE = "elsaworkflows/elsa-studio"
ARCHITECTURES = ("amd64", "arm64")
IMAGES = {"foundation": "elsa-studio-foundation", "profile": "elsa-studio-essentials"}
DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
VERSION = re.compile(r"4\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\Z")


def run(*args, **options):
    return subprocess.run(args, check=True, **options)


def read_command(*args):
    return json.loads(run(*args, capture_output=True).stdout)


def read_json(path):
    return json.loads(path.read_bytes())


def write_json(path, data):
    path.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(path):
    with path.open("rb") as stream:
        return "sha256:" + hashlib.file_digest(stream, "sha256").hexdigest()


def bundle_for(root, arch, run_id, attempt, source_commit):
    name = f"studio-essentials-profile-{arch}-{run_id}-{attempt}"
    artifacts = read_command("gh", "api", f"repos/{REPOSITORY}/actions/runs/{run_id}/artifacts?name={name}")["artifacts"]
    matching = [entry for entry in artifacts if entry["name"] == name and entry["expired"] is False
                and entry["workflow_run"]["head_sha"] == source_commit and str(entry["workflow_run"]["id"]) == run_id]
    require(len(matching) == 1, f"Expected one bundle named {name} from this qualification run and attempt")
    artifact = matching[0]
    require(DIGEST.fullmatch(artifact["digest"]), f"Missing immutable artifact digest for {name}")
    download = root / f"{arch}.zip"
    with download.open("wb") as stream:
        run("gh", "api", f"repos/{REPOSITORY}/actions/artifacts/{artifact['id']}/zip", stdout=stream)
    require(digest(download) == artifact["digest"], f"ZIP digest mismatch for {name}")

    bundle = root / arch
    with ZipFile(download) as archive:
        archive.extractall(bundle)
    download.unlink()
    covered = {line.partition("  ")[2] for line in (bundle / "SHA256SUMS").read_text().splitlines()}
    required = {"./build.json", "./images.tar.gz", "./qualification/smoke.json", "./studio-package-build.json",
                "./nuget-subjects.json", "./foundation-metadata.json", "./profile-metadata.json"}
    require(required <= covered, f"Missing required checksums in {name}")
    run("sha256sum", "--check", "--strict", "--quiet", "SHA256SUMS", cwd=bundle)
    return bundle, {"id": artifact["id"], "digest": artifact["digest"]}


def saved_manifest_digests(archive):
    # Containerd can report an OCI manifest ID after loading a classic builder's archive.
    try:
        index = json.load(archive.extractfile("index.json"))
    except KeyError:
        return {}
    manifests = {}
    for descriptor in index["manifests"]:
        identity = descriptor["digest"]
        require(DIGEST.fullmatch(identity), f"Invalid saved manifest digest: {identity}")
        blob = archive.extractfile("blobs/" + identity.replace(":", "/", 1)).read()
        require("sha256:" + hashlib.sha256(blob).hexdigest() == identity, f"Saved manifest digest mismatch: {identity}")
        config = json.loads(blob).get("config")
        if config:
            manifests.setdefault(config["digest"], set()).add(identity)
    return manifests


def saved_configs(archive_path, images, arch):
    # A Docker archive retains config bytes. Their digests stay stable even if
    # the registry exporter changes a manifest's media type or layer compression.
    with tarfile.open(archive_path, "r:gz") as archive:
        manifest = json.load(archive.extractfile("manifest.json"))
        require(sorted(row["RepoTags"] for row in manifest) == sorted([image] for image in images.values()),
                f"Image archive contains unexpected tags for linux/{arch}")
        digests = saved_manifest_digests(archive)
        result = {}
        for row in manifest:
            config = archive.extractfile(row["Config"]).read()
            document = json.loads(config)
            require(document["os"] == "linux" and document["architecture"] == arch,
                    f"Image config in {row['RepoTags'][0]} targets the wrong platform")
            config_digest = "sha256:" + hashlib.sha256(config).hexdigest()
            result[row["RepoTags"][0]] = {
                "digest": config_digest,
                "identities": {config_digest, *digests.get(config_digest, ())},
                "layers": document["rootfs"]["diff_ids"],
            }
        return result


def verify_bundle(bundle, arch, run_id, attempt, provenance):
    build = read_json(bundle / "build.json")
    expected = {**provenance, "platform": f"linux/{arch}"}
    require(all(build.get(key) == value for key, value in expected.items()), f"Build receipt mismatch for linux/{arch}")
    version = build["version"]
    require(VERSION.fullmatch(version), f"Invalid Studio version: {version!r}")
    producer = read_json(bundle / "studio-package-build.json")
    require((producer["repository"], producer["workflow"], producer["commit"], producer["run_id"], producer["version"]) ==
            (REPOSITORY, "Studio Packages (consolidated gated)", provenance["sourceCommit"], provenance["producerRun"], version),
            f"Package receipt mismatch for linux/{arch}")

    smoke = read_json(bundle / "qualification" / "smoke.json")
    require((smoke["imageId"], smoke["platform"], smoke["network"]) ==
            (build["profileImageId"], expected["platform"], "none"), f"Smoke receipt mismatch for linux/{arch}")
    etl = next(row for row in read_json(bundle / "nuget-subjects.json") if row["id"] == "Elsa.Studio.Etl")
    require(smoke["defaultAuth"]["shell"] == 200 and smoke["defaultAuth"]["managementRead"] == 401
            and smoke["defaultAuth"]["managementWrite"] == 401 and smoke["defaultAuth"]["assets"]
            and "EtlStudio" in smoke["demo"]["enabledFeatures"] and smoke["demo"]["etlAssets"]
            and smoke["demo"]["etlArchiveSha256"] == etl["sha256"],
            f"Smoke evidence is incomplete for linux/{arch}")

    images = {role: f"{image}:qualification-{run_id}-{attempt}-{arch}" for role, image in IMAGES.items()}
    require(all(build[f"{role}Image"] == image for role, image in images.items()), f"Unexpected image tag for linux/{arch}")
    archive_path = bundle / "images.tar.gz"
    configs = saved_configs(archive_path, images, arch)
    run("docker", "image", "load", "-i", str(archive_path))
    verified = {}
    layers = {}
    for role, image in images.items():
        image_id = build[f"{role}ImageId"]
        manifest_digest = build[f"{role}Digest"]
        metadata = read_json(bundle / f"{role}-metadata.json")
        config = configs[image]
        metadata_config = metadata.get("containerimage.config.digest")
        require(image_id in config["identities"] and manifest_digest in config["identities"]
                and metadata["containerimage.digest"] == manifest_digest
                and metadata_config in (None, config["digest"]), f"Inconsistent image digests for {image}")
        loaded = read_command("docker", "image", "inspect", image)[0]
        labels = loaded["Config"]["Labels"]
        loaded_id = loaded["Id"]
        # Docker's image stores can report either a config ID or a manifest ID after loading the same archive.
        require(loaded_id in config["identities"] and loaded["RootFS"]["Layers"] == config["layers"]
                and (loaded["Os"], loaded["Architecture"], labels["org.opencontainers.image.revision"],
                     labels["org.opencontainers.image.version"]) == ("linux", arch, provenance["sourceCommit"], version),
                f"Loaded image does not match qualification evidence: {image}")
        layers[role] = config["layers"]
        verified[role] = {"image": image, "imageId": image_id, "loadedImageId": loaded_id,
                          "buildDigest": manifest_digest, "configDigest": config["digest"]}

    foundation = layers["foundation"]
    profile = layers["profile"]
    require(foundation and len(profile) > len(foundation) and profile[:len(foundation)] == foundation,
            f"The Studio profile does not extend its verified Foundation image on linux/{arch}")
    verified["profile"]["baseImageId"] = verified["foundation"]["imageId"]
    return version, verified


def verify(root, run_id, attempt):
    provenance = {"repository": REPOSITORY, "sourceCommit": os.environ["GITHUB_SHA"],
                  "producerRun": os.environ["PACKAGES_RUN_ID"], "producerArtifactId": os.environ["PRODUCER_ARTIFACT_ID"],
                  "producerArtifactDigest": os.environ["PRODUCER_ARTIFACT_DIGEST"]}
    require(DIGEST.fullmatch(provenance["producerArtifactDigest"]) and provenance["producerArtifactId"].isdigit(),
            "Qualification did not report its official package artifact")
    platforms = {}
    for arch in ARCHITECTURES:
        bundle, artifact = bundle_for(root, arch, run_id, attempt, provenance["sourceCommit"])
        version, verified = verify_bundle(bundle, arch, run_id, attempt, provenance)
        platforms[arch] = {"artifact": artifact, "version": version, "images": verified}
    amd64, arm64 = (platforms[arch] for arch in ARCHITECTURES)
    require(amd64["version"] == arm64["version"], "Qualified versions differ across architectures")
    for name in ("studio-package-build.json", "nuget-subjects.json"):
        require(digest(root / "amd64" / name) == digest(root / "arm64" / name), f"Official package receipts differ: {name}")
    write_json(root / "verified.json", {**provenance, "runId": run_id, "runAttempt": attempt,
                                       "version": amd64["version"], "platforms": platforms})


def registry_digest(reference):
    descriptor = read_command("docker", "buildx", "imagetools", "inspect", reference, "--format", "{{json .Manifest}}")
    require(DIGEST.fullmatch(descriptor["digest"]), f"No registry manifest digest for {reference}")
    return descriptor["digest"]


def publish_image(reference, native):
    run("docker", "image", "tag", native["loadedImageId"], reference)
    run("docker", "image", "push", reference)
    manifest_digest = registry_digest(reference)
    manifest = read_command("docker", "buildx", "imagetools", "inspect", "--raw", f"{IMAGE}@{manifest_digest}")
    require(manifest.get("schemaVersion") == 2 and manifest.get("config", {}).get("digest") == native["configDigest"],
            f"Registry image does not match the tested config for {reference}")
    return manifest_digest


def publish(root, run_id, attempt):
    receipt = read_json(root / "verified.json")
    require((receipt["sourceCommit"], receipt["runId"], receipt["runAttempt"]) ==
            (os.environ["GITHUB_SHA"], run_id, attempt), "Publication evidence belongs to another workflow execution")
    version = receipt["version"]
    require(VERSION.fullmatch(version), f"Invalid Studio version: {version!r}")
    published = {}
    for role, prefix in (("foundation", "foundation"), ("profile", "essentials")):
        reference = f"{IMAGE}:{prefix}-candidate-{version}-{run_id}-{attempt}"
        children = {}
        for arch in ARCHITECTURES:
            native = receipt["platforms"][arch]["images"][role]
            child_ref = f"{reference}-{arch}"
            children[arch] = {"ref": child_ref, "digest": publish_image(child_ref, native)}
        run("docker", "buildx", "imagetools", "create", "--tag", reference,
            *(f"{IMAGE}@{child['digest']}" for child in children.values()))
        index_digest = registry_digest(reference)
        index = read_command("docker", "buildx", "imagetools", "inspect", "--raw", f"{IMAGE}@{index_digest}")
        children_by_platform = {f"{item['platform']['os']}/{item['platform']['architecture']}": item["digest"]
                                for item in index["manifests"]}
        require(index["schemaVersion"] == 2 and len(index["manifests"]) == len(ARCHITECTURES) and
                children_by_platform == {f"linux/{arch}": child["digest"] for arch, child in children.items()},
                f"Published index does not contain exactly the qualified AMD64 and ARM64 manifests: {reference}")
        published[role] = {"ref": reference, "digest": index_digest, "platforms": children}
    write_json(root / "publication.json", {**receipt, "published": published})
    with Path(os.environ["GITHUB_STEP_SUMMARY"]).open("a", encoding="utf-8") as summary:
        summary.write("## Studio profile candidates\n\n")
        for image in published.values():
            summary.write(f"- `{image['ref']}` at `{IMAGE}@{image['digest']}`\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage", choices=("verify", "publish"))
    args = parser.parse_args()
    try:
        require((os.environ["GITHUB_EVENT_NAME"], os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_REF"]) ==
                ("workflow_dispatch", REPOSITORY, "refs/heads/main"), "Candidate publication requires a manual main dispatch")
        run_id, attempt = os.environ["GITHUB_RUN_ID"], os.environ["GITHUB_RUN_ATTEMPT"]
        require(run_id.isdigit() and attempt.isdigit(), "Invalid Actions run or attempt")
        root = Path(os.environ["RUNNER_TEMP"]) / "studio-profile-publication"
        if args.stage == "verify":
            root.mkdir()
            verify(root, run_id, attempt)
        else:
            publish(root, run_id, attempt)
    except (OSError, ValueError, KeyError, StopIteration, subprocess.CalledProcessError, tarfile.TarError, BadZipFile) as error:
        parser.exit(1, f"Studio candidate publication failed: {error}\n")


if __name__ == "__main__":
    main()
