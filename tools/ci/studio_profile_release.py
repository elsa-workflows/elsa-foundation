#!/usr/bin/env python3
"""Stage source packages and qualify the exact ordinary Studio Essentials release image."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess


PLATFORMS = ("linux/amd64", "linux/arm64")
DIGEST = re.compile(r"sha256:[a-f0-9]{64}\Z")


def json_command(*args):
    return json.loads(subprocess.check_output(args))


def pack_source(profile, output, version):
    identities = json.loads(Path("tools/ci/studio-package-identities.json").read_bytes())
    projects = {entry["id"]: entry["project"] for entry in identities["packable_projects"]}
    package_ids = (profile / "packages.txt").read_text(encoding="utf-8").split()
    restore_config = Path("Studio/NuGet.config").resolve()
    output.mkdir()
    for package_id in [*package_ids, "Elsa.Studio.Etl"]:
        subprocess.run([
            "dotnet", "pack", projects[package_id], "-c", "Release", "-p:WarningsNotAsErrors=NU1603",
            f"-p:RestoreConfigFile={restore_config}", f"-p:Version={version}",
            f"-p:PackageOutputPath={output.resolve()}",
        ], check=True)


def inspect_manifest(reference):
    return json_command("docker", "buildx", "imagetools", "inspect", "--raw", reference)


def runtime_children(index):
    if index["schemaVersion"] != 2:
        raise ValueError("Expected a schema 2 Studio staging index")
    children = {}
    attestations = []
    for descriptor in index["manifests"]:
        platform = f"{descriptor['platform']['os']}/{descriptor['platform']['architecture']}"
        annotations = descriptor.get("annotations", {})
        if platform == "unknown/unknown" and annotations.get("vnd.docker.reference.type") == "attestation-manifest":
            attestations.append(annotations["vnd.docker.reference.digest"])
            continue
        digest = descriptor["digest"]
        if platform not in PLATFORMS or platform in children or not DIGEST.fullmatch(digest):
            raise ValueError(f"Unexpected Studio staging manifest for {platform}: {digest}")
        children[platform] = digest
    if set(children) != set(PLATFORMS) or len(set(children.values())) != len(PLATFORMS):
        raise ValueError("Expected distinct AMD64 and ARM64 Studio staging manifests")
    if set(attestations) - set(children.values()):
        raise ValueError("Studio staging index contains an attestation for an unqualified manifest")
    return children


def qualify(args):
    context = args.output / "context"
    etl_packages = list(args.packages.glob(f"Elsa.Studio.Etl.{args.version}.nupkg"))
    if len(etl_packages) != 1:
        raise ValueError(f"Expected a single Elsa.Studio.Etl archive at {args.version} in {args.packages}")
    etl = etl_packages[0]
    if args.index:
        if not DIGEST.fullmatch(args.index):
            raise ValueError(f"Invalid Studio staging index digest: {args.index}")
        repository = args.image.split(":", 1)[0]
        children = runtime_children(inspect_manifest(f"{repository}@{args.index}"))
    else:
        children = {"linux/amd64": None}

    docker_arch = subprocess.check_output(["docker", "info", "--format", "{{.Architecture}}"], text=True).strip()
    native_arch = {"x86_64": "amd64", "aarch64": "arm64"}.get(docker_arch, docker_arch)
    receipt = {"sourceCommit": args.source, "version": args.version, "indexDigest": args.index, "platforms": {}}
    for platform, digest in sorted(children.items()):
        image = args.image
        config = None
        if digest:
            image = f"{repository}@{digest}"
            manifest = inspect_manifest(image)
            config = manifest["config"]["digest"]
            if manifest["schemaVersion"] != 2 or not DIGEST.fullmatch(config):
                raise ValueError(f"Invalid Studio child manifest for {platform}: {image}")
            subprocess.run(["docker", "pull", "--platform", platform, image], check=True)

        actual, = json_command("docker", "image", "inspect", image)
        labels = actual["Config"]["Labels"]
        # Docker can expose either the config or manifest digest as the local image ID.
        if (f"{actual['Os']}/{actual['Architecture']}" != platform
            or (config and actual["Id"] not in (config, digest))
            or labels["org.opencontainers.image.revision"] != args.source
            or labels["org.opencontainers.image.version"] != args.version):
            raise ValueError(f"Studio image config does not match the verified source, version and platform: {image}")

        architecture = platform.split("/")[1]
        runtime = "native" if architecture == native_arch else "emulated"
        print(f"Qualifying Studio {platform} ({runtime}) at {actual['Id']}", flush=True)
        evidence = args.output / "evidence" / architecture
        subprocess.run([
            "python3", str(args.profile / "smoke.py"), image, "--context", str(context),
            "--etl", str(etl), "--output", str(evidence),
        ], check=True)
        smoke = json.loads((evidence / "smoke.json").read_bytes())
        if (smoke["imageId"], smoke["platform"], smoke["network"]) != (actual["Id"], platform, "none"):
            raise ValueError(f"Studio smoke receipt did not test {actual['Id']} on {platform}")
        receipt["platforms"][platform] = {
            "runtime": runtime, "manifestDigest": digest, "configDigest": config, "imageId": actual["Id"],
        }

    (args.output / "evidence" / "release.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    if args.index:
        with Path(os.environ["GITHUB_OUTPUT"]).open("a", encoding="utf-8") as output:
            output.write(f"digest={args.index}\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("stage", choices=("pack", "qualify"))
    parser.add_argument("--profile", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", required=True)
    parser.add_argument("--source")
    parser.add_argument("--image")
    parser.add_argument("--packages", type=Path)
    parser.add_argument("--index")
    args = parser.parse_args()
    try:
        if args.stage == "pack":
            pack_source(args.profile, args.output, args.version)
        else:
            if not (args.source and args.image and args.packages):
                parser.error("Qualification requires --source, --image and --packages")
            qualify(args)
    except (KeyError, OSError, ValueError, subprocess.CalledProcessError) as error:
        parser.exit(1, f"Studio profile release failed: {error}\n")


if __name__ == "__main__":
    main()
