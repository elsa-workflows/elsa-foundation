#!/usr/bin/env python3
"""Derive fully qualified Docker Hub tags for the consolidated Studio image."""

from __future__ import annotations

import argparse
import re


IMAGE = "elsaworkflows/elsa-studio"
STABLE_OR_PREVIEW_VERSION = re.compile(r"4\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\Z")
MAIN_PREVIEW_VERSION = re.compile(r"4\.0\.0-preview\.[0-9]+\Z")
DOCKER_TAG = re.compile(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\Z")


def derive_tags(ref: str, version: str, sha: str, publish: bool, preview_tag: str) -> list[str]:
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("Studio source commit must be a full lowercase SHA-1.")

    short_sha = sha[:7]
    if ref == "refs/heads/main":
        if not MAIN_PREVIEW_VERSION.fullmatch(version):
            raise ValueError("Main package artifact has an invalid Studio preview version.")
        tags = [f"{IMAGE}:sha-{short_sha}"]
        if publish:
            if not MAIN_PREVIEW_VERSION.fullmatch(preview_tag):
                raise ValueError("Main image publication requires a floor-checked 4.0.0-preview.N tag.")
            tags = [f"{IMAGE}:latest", f"{IMAGE}:{preview_tag}", f"{IMAGE}:sha-{short_sha}"]
    elif ref.startswith("refs/tags/"):
        if not STABLE_OR_PREVIEW_VERSION.fullmatch(version):
            raise ValueError("Release artifact has an invalid Studio semver.")
        if "-" in version:
            # A prerelease must not update the stable major/minor aliases.
            tags = [f"{IMAGE}:{version}", f"{IMAGE}:sha-{short_sha}"]
        else:
            major, minor, _ = version.split(".")
            tags = [f"{IMAGE}:{version}", f"{IMAGE}:{major}.{minor}", f"{IMAGE}:{major}", f"{IMAGE}:sha-{short_sha}"]
    else:
        tags = [f"{IMAGE}:sha-{short_sha}"]

    if not tags or any(not tag.startswith(f"{IMAGE}:") or not DOCKER_TAG.fullmatch(tag.split(":", 1)[1]) for tag in tags):
        raise ValueError("Every Studio image tag must be fully qualified for elsaworkflows/elsa-studio.")
    return tags


def self_test() -> None:
    sha = "0123456789abcdef0123456789abcdef01234567"
    expected_image = "elsaworkflows/elsa-studio:"
    if IMAGE != "elsaworkflows/elsa-studio":
        raise AssertionError(f"Unexpected Studio Docker Hub identity: {IMAGE}")
    cases = [
        ("refs/heads/main", "4.0.0-preview.12", False, "", ["sha-0123456"]),
        ("refs/heads/main", "4.0.0-preview.12", True, "4.0.0-preview.13", ["latest", "4.0.0-preview.13", "sha-0123456"]),
        ("refs/tags/v4.2.0-preview.1", "4.2.0-preview.1", True, "", ["4.2.0-preview.1", "sha-0123456"]),
        ("refs/tags/v4.2.0", "4.2.0", True, "", ["4.2.0", "4.2", "4", "sha-0123456"]),
        ("refs/heads/feature/example", "4.0.0-feature.1", False, "", ["sha-0123456"]),
    ]
    for ref, version, publish, preview, suffixes in cases:
        actual = derive_tags(ref, version, sha, publish, preview)
        expected = [expected_image + suffix for suffix in suffixes]
        if actual != expected:
            raise AssertionError(f"Unexpected tags for {ref}: {actual!r} != {expected!r}")
    try:
        derive_tags("refs/heads/main", "4.0.0-preview.12", sha, True, "4.0.0-preview.bad")
    except ValueError:
        pass
    else:
        raise AssertionError("Invalid preview image tag was accepted.")


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
