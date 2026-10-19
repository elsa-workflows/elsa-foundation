"""Focused tests for automatic Studio Docker source admission and preview tags."""

from __future__ import annotations

import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from unittest.mock import patch
from urllib.error import URLError

import studio_docker_publish as publish


REPOSITORY = "elsa-workflows/elsa-foundation"
SHA = "0123456789abcdef0123456789abcdef01234567"


def ci_run(**overrides: object) -> dict[str, object]:
    return {
        "id": 12345,
        "name": "Studio CI",
        "path": ".github/workflows/studio-ci.yml@refs/heads/main",
        "event": "push",
        "status": "completed",
        "conclusion": "success",
        "head_repository": {"full_name": REPOSITORY},
        "head_branch": "main",
        "head_sha": SHA,
        **overrides,
    }


class FakeResponse:
    def __init__(self, payload: object) -> None:
        self.payload = json.dumps(payload).encode()
        self.closed = False

    def read(self) -> bytes:
        return self.payload

    def close(self) -> None:
        self.closed = True


class StudioDockerPublishTests(unittest.TestCase):
    def test_valid_ci_run_returns_canonical_provenance_outputs(self) -> None:
        self.assertEqual(
            {
                "source_sha": SHA,
                "source_run_id": "12345",
                "source_url": f"https://github.com/{REPOSITORY}/actions/runs/12345",
            },
            publish.validate_ci_run(ci_run(html_url="https://attacker.example/run"), REPOSITORY, SHA),
        )

    def test_source_validation_rejects_invalid_repository_or_expected_sha(self) -> None:
        with self.assertRaisesRegex(ValueError, "owner/name"):
            publish.validate_ci_run(ci_run(), "not-a-repository", SHA)
        with self.assertRaisesRegex(ValueError, "full lowercase SHA-1"):
            publish.validate_ci_run(ci_run(), REPOSITORY, "main")

    def test_ci_run_admission_rejects_untrusted_or_ineligible_sources(self) -> None:
        cases = (
            ("wrong workflow", {"name": "Studio Packages (consolidated gated)"}),
            ("wrong workflow path", {"path": ".github/workflows/packages.yml@refs/heads/main"}),
            ("PR source", {"event": "pull_request"}),
            ("release source", {"event": "release"}),
            ("manual source", {"event": "workflow_dispatch"}),
            ("incomplete run", {"status": "in_progress", "conclusion": None}),
            ("failed run", {"conclusion": "failure"}),
            ("cancelled run", {"conclusion": "cancelled"}),
            ("fork source", {"head_repository": {"full_name": "someone/elsa-foundation"}}),
            ("feature branch", {"head_branch": "feature/images"}),
            ("short SHA", {"head_sha": "0123456"}),
            ("uppercase SHA", {"head_sha": "A" * 40}),
            ("source SHA mismatch", {"head_sha": "f" * 40}),
            ("missing ID", {"id": None}),
            ("boolean ID", {"id": True}),
            ("zero ID", {"id": 0}),
        )
        for label, changes in cases:
            with self.subTest(source=label), self.assertRaises(ValueError):
                publish.validate_ci_run(ci_run(**changes), REPOSITORY, SHA)

    def test_current_main_check_refuses_stale_or_invalid_source(self) -> None:
        self.assertTrue(publish.source_sha_is_current(SHA, SHA))
        self.assertFalse(publish.source_sha_is_current(SHA, "f" * 40))
        self.assertFalse(publish.source_sha_is_current("main", SHA))
        self.assertFalse(publish.source_sha_is_current(SHA, ""))

    def test_preview_floor_reads_all_pages_and_ignores_non_preview_tags(self) -> None:
        first = FakeResponse({
            "results": [
                {"name": "latest"},
                {"name": "4.0.0-preview.3"},
                {"name": "4.0.0-preview.20"},
                {"name": "4.0.0-preview.invalid"},
            ],
            "next": "?page=2&page_size=100",
        })
        second = FakeResponse({
            "results": [{"name": "4.0.0-preview.11"}, {"name": "4.1.0-preview.99"}],
            "next": None,
        })
        requests: list[str] = []

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            self.assertEqual(20, timeout)
            requests.append(url)
            return first if len(requests) == 1 else second

        self.assertEqual(20, publish.read_preview_floor(open_page))
        self.assertEqual(
            [
                publish.DOCKER_HUB_TAGS_URL,
                "https://hub.docker.com/v2/repositories/elsaworkflows/elsa-studio/tags?page=2&page_size=100",
            ],
            requests,
        )
        self.assertTrue(first.closed)
        self.assertTrue(second.closed)

    def test_preview_floor_rejects_untrusted_pagination_hosts(self) -> None:
        response = FakeResponse({"results": [], "next": "https://attacker.example/collect"})
        requests: list[str] = []

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            requests.append(url)
            return response

        with self.assertRaisesRegex(ValueError, "outside the Studio tags endpoint"):
            publish.read_preview_floor(open_page)
        self.assertEqual([publish.DOCKER_HUB_TAGS_URL], requests)

    def test_preview_floor_accepts_tags_path_with_trailing_slash(self) -> None:
        responses = [
            FakeResponse({"results": [{"name": "4.0.0-preview.4"}], "next": "/v2/repositories/elsaworkflows/elsa-studio/tags/?page=2&page_size=100"}),
            FakeResponse({"results": [{"name": "4.0.0-preview.9"}], "next": None}),
        ]
        requests: list[str] = []

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            requests.append(url)
            return responses[len(requests) - 1]

        self.assertEqual(9, publish.read_preview_floor(open_page))
        self.assertEqual(
            "https://hub.docker.com/v2/repositories/elsaworkflows/elsa-studio/tags?page=2&page_size=100",
            requests[1],
        )

    def test_preview_floor_rejects_missing_duplicate_and_unexpected_query_keys(self) -> None:
        for next_link in (
            "?page_size=100",
            "?page=2&page=3",
            "?page=2&token=untrusted",
        ):
            with self.subTest(next_link=next_link):
                response = FakeResponse({"results": [], "next": next_link})
                with self.assertRaises(ValueError):
                    publish.read_preview_floor(lambda url, **kwargs: response)

    def test_preview_floor_rejects_a_repeated_page_link(self) -> None:
        responses = [
            FakeResponse({"results": [], "next": "?page=2&page_size=100"}),
            FakeResponse({"results": [], "next": "?page=2&page_size=100"}),
        ]
        requests: list[str] = []

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            requests.append(url)
            return responses[len(requests) - 1]

        with self.assertRaisesRegex(ValueError, "repeated a page URL"):
            publish.read_preview_floor(open_page)
        self.assertEqual(2, len(requests))

    def test_preview_floor_fails_closed_on_registry_errors_or_bad_pages(self) -> None:
        with self.assertRaises(URLError):
            publish.read_preview_floor(lambda url, **kwargs: (_ for _ in ()).throw(URLError("offline")))
        with self.assertRaisesRegex(ValueError, "invalid tags response"):
            publish.read_preview_floor(lambda url, **kwargs: FakeResponse({"next": None}))
        with self.assertRaisesRegex(ValueError, "malformed tag entry"):
            publish.read_preview_floor(lambda url, **kwargs: FakeResponse({"results": [None], "next": None}))

    def test_next_version_starts_at_one_and_advances_the_global_floor(self) -> None:
        self.assertEqual("4.0.0-preview.1", publish.next_preview_version(None))
        self.assertEqual("4.0.0-preview.1", publish.next_preview_version(0))
        self.assertEqual("4.0.0-preview.21", publish.next_preview_version(20))
        with self.assertRaises(ValueError):
            publish.next_preview_version(-1)

    def test_preview_tags_include_short_compatibility_and_full_unique_sha_without_latest(self) -> None:
        tags = publish.preview_image_tags(SHA, "4.0.0-preview.21")
        self.assertEqual(
            [
                "elsaworkflows/elsa-studio:4.0.0-preview.21",
                "elsaworkflows/elsa-studio:sha-0123456",
                f"elsaworkflows/elsa-studio:sha-{SHA}",
            ],
            tags,
        )
        self.assertNotIn("elsaworkflows/elsa-studio:latest", tags)
        with self.assertRaises(ValueError):
            publish.preview_image_tags(SHA, "4.0.0-preview.bad")

    def test_validate_source_cli_writes_only_verified_source_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"
            with patch.dict(os.environ, {
                "GITHUB_OUTPUT": str(output),
                "GITHUB_REPOSITORY": REPOSITORY,
                "EXPECTED_SHA": SHA,
            }), patch.object(sys, "stdin", io.StringIO(json.dumps(ci_run()))):
                with redirect_stdout(io.StringIO()):
                    self.assertEqual(0, publish.main(["validate-source"]))
            self.assertEqual(
                f"source_sha={SHA}\nsource_run_id=12345\nsource_url=https://github.com/{REPOSITORY}/actions/runs/12345\n",
                output.read_text(),
            )

    def test_preview_version_cli_writes_preview_tags_but_never_latest(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"
            with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output), "SOURCE_SHA": SHA}), patch.object(
                publish, "read_preview_floor", return_value=20
            ):
                with redirect_stdout(io.StringIO()):
                    self.assertEqual(0, publish.main(["preview-version"]))
            text = output.read_text()
            self.assertIn("studio_version=4.0.0-preview.21\n", text)
            self.assertIn(f"elsaworkflows/elsa-studio:sha-{SHA}\n", text)
            self.assertNotIn("elsaworkflows/elsa-studio:latest", text)

    def test_invalid_source_cli_fails_without_writing_outputs(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"
            with patch.dict(os.environ, {
                "GITHUB_OUTPUT": str(output),
                "GITHUB_REPOSITORY": REPOSITORY,
                "EXPECTED_SHA": SHA,
            }), patch.object(sys, "stdin", io.StringIO(json.dumps(ci_run(event="pull_request")))):
                with redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                    self.assertEqual(1, publish.main(["validate-source"]))
            self.assertFalse(output.exists())

    def test_is_current_cli_distinguishes_current_stale_and_malformed_sha(self) -> None:
        cases = ((SHA, SHA, 0), (SHA, "f" * 40, 1), ("main", SHA, 2))
        for source_sha, main_sha, expected in cases:
            with self.subTest(source_sha=source_sha, main_sha=main_sha), patch.dict(
                os.environ, {"SOURCE_SHA": source_sha, "MAIN_SHA": main_sha}, clear=True
            ), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(expected, publish.main(["is-current"]))


if __name__ == "__main__":
    unittest.main()
