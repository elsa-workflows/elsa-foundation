#!/usr/bin/env python3
"""Derive fully qualified Docker Hub tags for the consolidated Studio flavor images."""

from __future__ import annotations

import argparse
import re

from studio_docker_publish import STUDIO_FLAVORS, require_studio_image

STABLE_OR_PREVIEW_VERSION = re.compile(r"4\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\Z")
MAIN_PREVIEW_VERSION = re.compile(r"4\.0\.0-preview\.[0-9]+\Z")
DOCKER_TAG = re.compile(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\Z")


def derive_tags(image: str, ref: str, version: str, sha: str, publish: bool, preview_tag: str) -> list[str]:
    image = require_studio_image(image)
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("Studio source commit must be a full lowercase SHA-1.")
    if not STABLE_OR_PREVIEW_VERSION.fullmatch(version):
        raise ValueError("Studio package artifact has an invalid version.")

    short_sha = sha[:7]
    if ref == "refs/heads/main":
        if not MAIN_PREVIEW_VERSION.fullmatch(version):
            raise ValueError("Main package artifact has an invalid Studio preview version.")
        tags = [f"{image}:sha-{short_sha}"]
        if publish:
            if not MAIN_PREVIEW_VERSION.fullmatch(preview_tag):
                raise ValueError("Main image publication requires a floor-checked 4.0.0-preview.N tag.")
            tags = [f"{image}:latest", f"{image}:{preview_tag}", f"{image}:sha-{short_sha}"]
    elif ref.startswith("refs/tags/"):
        if "-" in version:
            # A prerelease must not update the stable major/minor aliases.
            tags = [f"{image}:{version}", f"{image}:sha-{short_sha}"]
        else:
            major, minor, _ = version.split(".")
            tags = [f"{image}:{version}", f"{image}:{major}.{minor}", f"{image}:{major}", f"{image}:sha-{short_sha}"]
    else:
        tags = [f"{image}:sha-{short_sha}"]

    if not tags or any(not tag.startswith(f"{image}:") or not DOCKER_TAG.fullmatch(tag.split(":", 1)[1]) for tag in tags):
        raise ValueError(f"Every Studio image tag must be fully qualified for {image}.")
    return tags


def self_test() -> None:
    sha = "0123456789abcdef0123456789abcdef01234567"
    # The owner decision (#5768): elsa-studio publishes Studio Essentials; the other flavors get their own repositories.
    expected_images = {
        "essentials": "elsaworkflows/elsa-studio",
        "workbench": "elsaworkflows/elsa-studio-workbench",
        "foundation": "elsaworkflows/elsa-studio-foundation",
    }
    actual_images = {flavor: settings["image"] for flavor, settings in STUDIO_FLAVORS.items()}
    if actual_images != expected_images:
        raise AssertionError(f"Unexpected Studio Docker Hub identities: {actual_images!r}")
    cases = [
        ("refs/heads/main", "4.0.0-preview.12", False, "", ["sha-0123456"]),
        ("refs/heads/main", "4.0.0-preview.12", True, "4.0.0-preview.13", ["latest", "4.0.0-preview.13", "sha-0123456"]),
        ("refs/tags/v4.2.0-preview.1", "4.2.0-preview.1", True, "", ["4.2.0-preview.1", "sha-0123456"]),
        ("refs/tags/v4.2.0", "4.2.0", True, "", ["4.2.0", "4.2", "4", "sha-0123456"]),
        ("refs/heads/feature/example", "4.0.0-feature.1", False, "", ["sha-0123456"]),
    ]
    for image in expected_images.values():
        for ref, version, publish, preview, suffixes in cases:
            actual = derive_tags(image, ref, version, sha, publish, preview)
            expected = [f"{image}:{suffix}" for suffix in suffixes]
            if actual != expected:
                raise AssertionError(f"Unexpected tags for {image} at {ref}: {actual!r} != {expected!r}")
    invalid_cases = [
        ("elsaworkflows/elsa-studio", "refs/heads/main", "4.0.0-preview.12", True, "4.0.0-preview.bad"),
        ("elsaworkflows/elsa-studio", "refs/heads/feature/example", "4.0.0-feature.1\npublish_image=true", False, ""),
        ("elsaworkflows/elsa-studio", "refs/tags/v4.2.0", "4.2.0\npublish_image=true", False, ""),
        ("elsaworkflows/elsa-workbench", "refs/heads/main", "4.0.0-preview.12", False, ""),
        ("attacker/elsa-studio", "refs/heads/main", "4.0.0-preview.12", False, ""),
    ]
    for image, ref, version, publish, preview in invalid_cases:
        try:
            derive_tags(image, ref, version, sha, publish, preview)
        except ValueError:
            pass
        else:
            raise AssertionError("Invalid Studio image metadata was accepted.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true", help="check the tag rules without repository files")
    args = parser.parse_args()
    if args.self_test:
        self_test()
        print("Studio image tag self-test passed.")
    else:
        parser.error("use --self-test or import derive_tags from the workflow")


if __name__ == "__main__":
    main()
