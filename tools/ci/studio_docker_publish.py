#!/usr/bin/env python3
"""Validate Studio CI provenance and allocate a non-floating preview image tag."""

from __future__ import annotations

import json
import os
import re
import sys
from typing import Any, Callable
from urllib import parse, request


WORKFLOW_NAME = "Studio CI"
WORKFLOW_PATH = ".github/workflows/studio-ci.yml"
IMAGE = "elsaworkflows/elsa-studio"
DOCKER_HUB_TAGS_URL = "https://hub.docker.com/v2/repositories/elsaworkflows/elsa-studio/tags?page=1&page_size=100"
DOCKER_HUB_TAGS_PATH = "/v2/repositories/elsaworkflows/elsa-studio/tags"
FULL_SHA = re.compile(r"[0-9a-f]{40}\Z")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
PREVIEW_TAG = re.compile(r"4\.0\.0-preview\.(\d+)\Z")


def validate_ci_run(run: dict[str, Any], repository: str, expected_sha: str) -> dict[str, str]:
    """Return safe outputs only for a successful same-repository Studio CI push to main."""
    if not isinstance(repository, str) or not REPOSITORY.fullmatch(repository):
        raise ValueError("Expected repository must be owner/name.")
    if not isinstance(expected_sha, str) or not FULL_SHA.fullmatch(expected_sha):
        raise ValueError("Expected source commit must be a full lowercase SHA-1.")

    path = run.get("path")
    if isinstance(path, str):
        path = path.split("@", 1)[0]
    head_repository = run.get("head_repository")
    head_repository_name = head_repository.get("full_name") if isinstance(head_repository, dict) else None
    head_sha = run.get("head_sha")
    run_id = run.get("id")

    checks = (
        (run.get("name") == WORKFLOW_NAME, "Source run is not Studio CI."),
        (path == WORKFLOW_PATH, "Source run is not the root Studio CI workflow."),
        (run.get("event") == "push", "Source run must come from a push."),
        (run.get("status") == "completed" and run.get("conclusion") == "success", "Source Studio CI run must have completed successfully."),
        (head_repository_name == repository, "Source run must belong to this repository."),
        (run.get("head_branch") == "main", "Source run must be a push to main."),
        (isinstance(head_sha, str) and FULL_SHA.fullmatch(head_sha) is not None, "Source run must identify a full lowercase SHA-1."),
        (head_sha == expected_sha, "Source run SHA does not match the triggering workflow event."),
        (type(run_id) is int and run_id > 0, "Source run must have a positive numeric Actions run ID."),
    )
    for accepted, message in checks:
        if not accepted:
            raise ValueError(message)

    return {
        "source_sha": head_sha,
        "source_run_id": str(run_id),
        "source_url": f"https://github.com/{repository}/actions/runs/{run_id}",
    }


def source_sha_is_current(source_sha: str, main_sha: str) -> bool:
    """Only the current main commit may move the floating latest tag."""
    return bool(
        isinstance(source_sha, str)
        and isinstance(main_sha, str)
        and FULL_SHA.fullmatch(source_sha)
        and FULL_SHA.fullmatch(main_sha)
        and source_sha == main_sha
    )


def _validated_next_url(next_value: str, current_url: str) -> str:
    candidate = parse.urljoin(current_url, next_value)
    parts = parse.urlsplit(candidate)
    if (
        parts.scheme != "https"
        or parts.hostname != "hub.docker.com"
        or parts.port not in (None, 443)
        or parts.username is not None
        or parts.password is not None
        or parts.path not in {DOCKER_HUB_TAGS_PATH, f"{DOCKER_HUB_TAGS_PATH}/"}
        or parts.fragment
    ):
        raise ValueError("Docker Hub pagination returned a URL outside the Studio tags endpoint.")

    query = parse.parse_qs(parts.query, keep_blank_values=True, strict_parsing=True)
    if set(query) - {"page", "page_size"} or "page" not in query:
        raise ValueError("Docker Hub pagination returned unexpected query parameters.")
    if any(len(values) != 1 for values in query.values()):
        raise ValueError("Docker Hub pagination returned duplicate query parameters.")
    page = query["page"][0]
    page_size = query.get("page_size", ["100"])[0]
    if not page.isdecimal() or int(page) < 1 or not page_size.isdecimal() or not 1 <= int(page_size) <= 100:
        raise ValueError("Docker Hub pagination returned invalid page coordinates.")
    return parse.urlunsplit(("https", "hub.docker.com", DOCKER_HUB_TAGS_PATH, parse.urlencode({"page": page, "page_size": page_size}), ""))


def read_preview_floor(opener: Callable[..., Any] | None = None) -> int | None:
    """Read every public Studio Docker Hub tag page and return the highest preview number."""
    open_url = opener or request.urlopen
    next_url: str | None = DOCKER_HUB_TAGS_URL
    seen_urls: set[str] = set()
    floor: int | None = None

    while next_url is not None:
        if next_url in seen_urls:
            raise ValueError("Docker Hub pagination repeated a page URL.")
        seen_urls.add(next_url)
        if len(seen_urls) > 1000:
            raise ValueError("Docker Hub tag listing exceeded the page limit.")

        response = open_url(next_url, timeout=20)
        try:
            payload = json.loads(response.read())
        finally:
            close = getattr(response, "close", None)
            if callable(close):
                close()
        if not isinstance(payload, dict) or not isinstance(payload.get("results"), list):
            raise ValueError("Docker Hub returned an invalid tags response.")
        for item in payload["results"]:
            if not isinstance(item, dict) or not isinstance(item.get("name"), str):
                raise ValueError("Docker Hub returned a malformed tag entry.")
            match = PREVIEW_TAG.fullmatch(item["name"])
            if match:
                number = int(match.group(1))
                floor = number if floor is None else max(floor, number)

        page_next = payload.get("next")
        if page_next is None:
            next_url = None
        elif isinstance(page_next, str) and page_next:
            next_url = _validated_next_url(page_next, next_url)
        else:
            raise ValueError("Docker Hub returned an invalid pagination link.")

    return floor


def next_preview_version(floor: int | None) -> str:
    """Allocate the first preview number when absent, otherwise advance the observed floor."""
    if floor is not None and (type(floor) is not int or floor < 0):
        raise ValueError("Preview floor must be a non-negative integer or None.")
    return f"4.0.0-preview.{(floor or 0) + 1}"


def preview_image_tags(source_sha: str, studio_version: str) -> list[str]:
    """Create stable source tags for a preview build, deliberately excluding latest."""
    if not FULL_SHA.fullmatch(source_sha):
        raise ValueError("Source commit must be a full lowercase SHA-1.")
    if PREVIEW_TAG.fullmatch(studio_version) is None:
        raise ValueError("Studio version must be a 4.0.0-preview.N tag.")
    short_sha = source_sha[:7]
    return [
        f"{IMAGE}:{studio_version}",
        f"{IMAGE}:sha-{short_sha}",
        f"{IMAGE}:sha-{source_sha}",
    ]


def _append_outputs(values: dict[str, str], output_path: str) -> None:
    with open(output_path, "a", encoding="utf-8") as output:
        for key, value in values.items():
            if "\n" in value or "\r" in value:
                raise ValueError(f"Output {key} contains a newline.")
            output.write(f"{key}={value}\n")


def _append_tag_output(tags: list[str], output_path: str) -> None:
    with open(output_path, "a", encoding="utf-8") as output:
        output.write("image_tags<<TAGS\n" + "\n".join(tags) + "\nTAGS\n")


def main(argv: list[str] | None = None) -> int:
    args = sys.argv[1:] if argv is None else argv

    if args == ["is-current"]:
        source_sha = os.environ.get("SOURCE_SHA", "")
        main_sha = os.environ.get("MAIN_SHA", "")
        if not FULL_SHA.fullmatch(source_sha) or not FULL_SHA.fullmatch(main_sha):
            print("::error::SOURCE_SHA and MAIN_SHA must be full lowercase SHA-1 values.", file=sys.stderr)
            return 2
        if source_sha_is_current(source_sha, main_sha):
            print("Studio source SHA is the current main SHA.")
            return 0
        print("::notice::Studio source SHA is stale; floating latest promotion is not admitted.")
        return 1

    if args not in (["validate-source"], ["preview-version"]):
        print("Usage: studio_docker_publish.py validate-source|preview-version|is-current", file=sys.stderr)
        return 2

    output_path = os.environ.get("GITHUB_OUTPUT", "")
    if not output_path:
        print("::error::GITHUB_OUTPUT is not set.", file=sys.stderr)
        return 2

    try:
        if args == ["validate-source"]:
            try:
                run = json.load(sys.stdin)
            except json.JSONDecodeError as error:
                raise ValueError("Source Actions run JSON is invalid.") from error
            if not isinstance(run, dict):
                raise ValueError("Source Actions run must be a JSON object.")
            values = validate_ci_run(
                run,
                os.environ.get("GITHUB_REPOSITORY", ""),
                os.environ.get("EXPECTED_SHA", ""),
            )
            _append_outputs(values, output_path)
            print(f"Admitted Studio CI run {values['source_run_id']} at {values['source_sha']}.")
            return 0

        if args == ["preview-version"]:
            source_sha = os.environ.get("SOURCE_SHA", os.environ.get("EXPECTED_SHA", ""))
            if not FULL_SHA.fullmatch(source_sha):
                raise ValueError("Source commit must be a full lowercase SHA-1.")
            version = next_preview_version(read_preview_floor())
            tags = preview_image_tags(source_sha, version)
            _append_outputs({"studio_version": version}, output_path)
            _append_tag_output(tags, output_path)
            print(f"Allocated {version} for {len(tags)} source-specific Studio image tags.")
            return 0

    except (OSError, ValueError, TypeError, json.JSONDecodeError, request.URLError) as error:
        print(f"::error::{error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
