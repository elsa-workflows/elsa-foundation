#!/usr/bin/env python3
"""Validate CI provenance and allocate a non-floating preview image tag for each Studio flavor image."""

from __future__ import annotations

import json
import os
import re
import sys
from typing import Any, Callable
from urllib import error, parse, request


WORKFLOW_NAME = "CI"
WORKFLOW_PATH = ".github/workflows/ci.yml"
# The Studio flavors and the Docker Hub repository each one publishes (owner decision, #5768). elsa-studio publishes Studio
# Essentials, the pair of elsaworkflows/elsa-essentials-host. This table is the only place a flavor maps to its image.
# Titles and descriptions become image labels, which docker/build-push-action reads one per line: no commas or newlines.
STUDIO_FLAVORS: dict[str, dict[str, str]] = {
    "essentials": {
        "image": "elsaworkflows/elsa-studio",
        "dockerfile": "Studio/docker/profiles/essentials/Dockerfile",
        "title": "Elsa Studio",
        "description": "Elsa 4 workflow studio (Studio Essentials) for elsaworkflows/elsa-essentials-host",
    },
    "workbench": {
        "image": "elsaworkflows/elsa-studio-workbench",
        "dockerfile": "Studio/src/apps/Elsa.Studio.Workbench.Host/Dockerfile",
        "title": "Elsa Studio Workbench",
        "description": "Elsa 4 workflow studio with every module compiled in for elsaworkflows/elsa-workbench",
    },
    "foundation": {
        "image": "elsaworkflows/elsa-studio-foundation",
        "dockerfile": "Studio/src/apps/Elsa.Studio.Foundation.Host/Dockerfile",
        "title": "Elsa Studio Foundation",
        "description": "The bare Elsa 4 Studio shell; every module comes from the Nuplane feed",
    },
}
STUDIO_IMAGES = tuple(flavor["image"] for flavor in STUDIO_FLAVORS.values())
FULL_SHA = re.compile(r"[0-9a-f]{40}\Z")
REPOSITORY = re.compile(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\Z")
PREVIEW_TAG = re.compile(r"4\.0\.0-preview\.(\d+)\Z")
# Docker Hub refuses anonymous pages past the first 1,000 tags (403 "pagination offset too large"). elsa-studio passed 1,000
# tags on 2026-11-25, so the floor read lists only tags whose name contains the preview prefix.
PREVIEW_NAME_FILTER = "4.0.0-preview."


def validate_ci_run(run: dict[str, Any], repository: str, expected_sha: str) -> dict[str, str]:
    """Return safe outputs only for a successful same-repository CI push to main."""
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
        (run.get("name") == WORKFLOW_NAME, "Source run is not CI."),
        (path == WORKFLOW_PATH, "Source run is not the root CI workflow."),
        (run.get("event") == "push", "Source run must come from a push."),
        (run.get("status") == "completed" and run.get("conclusion") == "success", "Source CI run must have completed successfully."),
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


class MissingRepositoryError(ValueError):
    """A Studio image's Docker Hub repository does not exist yet."""

    def __init__(self, image: str) -> None:
        super().__init__(
            f"Docker Hub repository {image} does not exist. Create it on Docker Hub as a public repository in the "
            f"elsaworkflows organization, then rerun this job. The other Studio images publish independently."
        )
        self.image = image


def require_studio_image(image: str) -> str:
    """Accept only the fixed Studio image names, so no input can redirect a push or a registry read."""
    if image not in STUDIO_IMAGES:
        raise ValueError(f"Unknown Studio image {image!r}; expected one of {', '.join(STUDIO_IMAGES)}.")
    return image


def resolve_flavor(flavor: str) -> dict[str, str]:
    if flavor not in STUDIO_FLAVORS:
        raise ValueError(f"Unknown Studio flavor {flavor!r}; expected one of {', '.join(STUDIO_FLAVORS)}.")
    return {"flavor": flavor, **STUDIO_FLAVORS[flavor]}


def docker_hub_repository_url(image: str) -> str:
    return f"https://hub.docker.com/v2/repositories/{require_studio_image(image)}/"


def docker_hub_tags_path(image: str) -> str:
    return f"/v2/repositories/{require_studio_image(image)}/tags"


def docker_hub_tags_url(image: str) -> str:
    query = parse.urlencode({"name": PREVIEW_NAME_FILTER, "page": "1", "page_size": "100"})
    return f"https://hub.docker.com{docker_hub_tags_path(image)}?{query}"


def _validated_next_url(next_value: str, current_url: str, image: str) -> str:
    tags_path = docker_hub_tags_path(image)
    candidate = parse.urljoin(current_url, next_value)
    parts = parse.urlsplit(candidate)
    if (
        parts.scheme != "https"
        or parts.hostname != "hub.docker.com"
        or parts.port not in (None, 443)
        or parts.username is not None
        or parts.password is not None
        or parts.path not in {tags_path, f"{tags_path}/"}
        or parts.fragment
    ):
        raise ValueError("Docker Hub pagination returned a URL outside the Studio tags endpoint.")

    query = parse.parse_qs(parts.query, keep_blank_values=True, strict_parsing=True)
    if set(query) - {"page", "page_size", "name"} or "page" not in query:
        raise ValueError("Docker Hub pagination returned unexpected query parameters.")
    if any(len(values) != 1 for values in query.values()):
        raise ValueError("Docker Hub pagination returned duplicate query parameters.")
    if query.get("name", [PREVIEW_NAME_FILTER])[0] != PREVIEW_NAME_FILTER:
        raise ValueError("Docker Hub pagination changed the preview tag name filter.")
    page = query["page"][0]
    page_size = query.get("page_size", ["100"])[0]
    if not page.isdecimal() or int(page) < 1 or not page_size.isdecimal() or not 1 <= int(page_size) <= 100:
        raise ValueError("Docker Hub pagination returned invalid page coordinates.")
    query_string = parse.urlencode({"name": PREVIEW_NAME_FILTER, "page": page, "page_size": page_size})
    return parse.urlunsplit(("https", "hub.docker.com", tags_path, query_string, ""))


def repository_exists(image: str, opener: Callable[..., Any] | None = None) -> bool:
    """True when the image's Docker Hub repository exists; a 404 means it has not been created yet."""
    open_url = opener or request.urlopen
    try:
        response = open_url(docker_hub_repository_url(image), timeout=20)
    except error.HTTPError as failure:
        if failure.code == 404:
            return False
        raise
    close = getattr(response, "close", None)
    if callable(close):
        close()
    return True


def read_repository_preview_floor(image: str, opener: Callable[..., Any] | None = None) -> int | None:
    """Read every public Docker Hub tag page of one Studio image and return its highest preview number."""
    open_url = opener or request.urlopen
    next_url: str | None = docker_hub_tags_url(image)
    seen_urls: set[str] = set()
    floor: int | None = None

    while next_url is not None:
        if next_url in seen_urls:
            raise ValueError("Docker Hub pagination repeated a page URL.")
        seen_urls.add(next_url)
        if len(seen_urls) > 1000:
            raise ValueError("Docker Hub tag listing exceeded the page limit.")

        try:
            response = open_url(next_url, timeout=20)
        except error.HTTPError as failure:
            # Only a missing first page means a missing repository; a later 404 is a broken listing and fails closed.
            if failure.code == 404 and len(seen_urls) == 1:
                raise MissingRepositoryError(image) from failure
            raise
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
            next_url = _validated_next_url(page_next, next_url, image)
        else:
            raise ValueError("Docker Hub returned an invalid pagination link.")

    return floor


def read_preview_floor(image: str, opener: Callable[..., Any] | None = None) -> int | None:
    """Return the highest preview number across the Studio images, so one source commit usually gets one number.

    The image's own repository must exist and read cleanly: its floor alone keeps its preview tags unique, so its read fails
    closed. A sibling's floor can only raise the number, so a sibling that is missing or unreadable adds nothing and never
    blocks this image.
    """
    floors = [read_repository_preview_floor(image, opener)]
    for sibling in STUDIO_IMAGES:
        if sibling == image:
            continue
        try:
            floors.append(read_repository_preview_floor(sibling, opener))
        except (OSError, ValueError, TypeError, json.JSONDecodeError) as failure:
            print(f"::notice::Skipped the {sibling} preview floor: {failure}", file=sys.stderr)
    known = [floor for floor in floors if floor is not None]
    return max(known) if known else None


def next_preview_version(floor: int | None) -> str:
    """Allocate the first preview number when absent, otherwise advance the observed floor."""
    if floor is not None and (type(floor) is not int or floor < 0):
        raise ValueError("Preview floor must be a non-negative integer or None.")
    return f"4.0.0-preview.{(floor or 0) + 1}"


def require_preview_above_floor(version: str, floor: int | None) -> None:
    """Reject a manual preview that an earlier queued publisher has already used."""
    minimum = int(next_preview_version(floor).rsplit(".", 1)[1])
    match = PREVIEW_TAG.fullmatch(version)
    if match is None or int(match.group(1)) < minimum:
        raise ValueError(f"Manual image tag must be 4.0.0-preview.N with N at least {minimum}.")


def preview_image_tags(image: str, source_sha: str, studio_version: str) -> list[str]:
    """Create stable source tags for a preview build, deliberately excluding latest."""
    image = require_studio_image(image)
    if not FULL_SHA.fullmatch(source_sha):
        raise ValueError("Source commit must be a full lowercase SHA-1.")
    if PREVIEW_TAG.fullmatch(studio_version) is None:
        raise ValueError("Studio version must be a 4.0.0-preview.N tag.")
    short_sha = source_sha[:7]
    return [
        f"{image}:{studio_version}",
        f"{image}:sha-{short_sha}",
        f"{image}:sha-{source_sha}",
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


def _append_summary(message: str) -> None:
    summary_path = os.environ.get("GITHUB_STEP_SUMMARY", "")
    if summary_path:
        with open(summary_path, "a", encoding="utf-8") as summary:
            summary.write(f"{message}\n")


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

    if args == ["require-repository"]:
        try:
            image = require_studio_image(os.environ.get("STUDIO_IMAGE", ""))
            if repository_exists(image):
                print(f"Docker Hub repository {image} exists.")
                return 0
            message = str(MissingRepositoryError(image))
        except (OSError, ValueError, TypeError, request.URLError) as failure:
            message = str(failure)
        print(f"::error::{message}", file=sys.stderr)
        _append_summary(message)
        return 1

    if args not in (["validate-source"], ["preview-version"], ["resolve-flavor"]):
        print("Usage: studio_docker_publish.py validate-source|preview-version|is-current|resolve-flavor|require-repository", file=sys.stderr)
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
            print(f"Admitted CI run {values['source_run_id']} at {values['source_sha']}.")
            return 0

        if args == ["resolve-flavor"]:
            values = resolve_flavor(os.environ.get("STUDIO_FLAVOR", ""))
            _append_outputs(values, output_path)
            print(f"Studio flavor {values['flavor']} builds {values['dockerfile']} as {values['image']}.")
            return 0

        if args == ["preview-version"]:
            image = require_studio_image(os.environ.get("STUDIO_IMAGE", ""))
            source_sha = os.environ.get("SOURCE_SHA", os.environ.get("EXPECTED_SHA", ""))
            if not FULL_SHA.fullmatch(source_sha):
                raise ValueError("Source commit must be a full lowercase SHA-1.")
            version = next_preview_version(read_preview_floor(image))
            tags = preview_image_tags(image, source_sha, version)
            _append_outputs({"studio_version": version}, output_path)
            _append_tag_output(tags, output_path)
            print(f"Allocated {version} for {len(tags)} source-specific {image} tags.")
            return 0

    except (OSError, ValueError, TypeError, json.JSONDecodeError, request.URLError) as failure:
        print(f"::error::{failure}", file=sys.stderr)
        if isinstance(failure, MissingRepositoryError):
            _append_summary(str(failure))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
