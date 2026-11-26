"""Focused tests for automatic Studio Docker source admission, flavor images and preview tags."""

from __future__ import annotations

import io
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from unittest.mock import patch
from urllib.error import HTTPError, URLError

import studio_docker_publish as publish
from studio_profile_release import runtime_children


REPOSITORY = "elsa-workflows/elsa-foundation"
SHA = "0123456789abcdef0123456789abcdef01234567"
IMAGE = "elsaworkflows/elsa-studio"
WORKBENCH_IMAGE = "elsaworkflows/elsa-studio-workbench"
FOUNDATION_IMAGE = "elsaworkflows/elsa-studio-foundation"
REPO_ROOT = Path(__file__).resolve().parents[2]


def not_found(url: str) -> HTTPError:
    return HTTPError(url, 404, "Not Found", {}, None)


def tags_page(image: str, *names: str) -> dict[str, object]:
    return {"results": [{"name": name} for name in names], "next": None}


def ci_run(**overrides: object) -> dict[str, object]:
    return {
        "id": 12345,
        "name": "CI",
        "path": ".github/workflows/ci.yml@refs/heads/main",
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

        self.assertEqual(20, publish.read_repository_preview_floor(IMAGE, open_page))
        self.assertEqual(
            [
                publish.docker_hub_tags_url(IMAGE),
                "https://hub.docker.com/v2/repositories/elsaworkflows/elsa-studio/tags?name=4.0.0-preview.&page=2&page_size=100",
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
            publish.read_repository_preview_floor(IMAGE, open_page)
        self.assertEqual([publish.docker_hub_tags_url(IMAGE)], requests)

    def test_preview_floor_accepts_tags_path_with_trailing_slash(self) -> None:
        responses = [
            FakeResponse({"results": [{"name": "4.0.0-preview.4"}], "next": "/v2/repositories/elsaworkflows/elsa-studio/tags/?page=2&page_size=100"}),
            FakeResponse({"results": [{"name": "4.0.0-preview.9"}], "next": None}),
        ]
        requests: list[str] = []

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            requests.append(url)
            return responses[len(requests) - 1]

        self.assertEqual(9, publish.read_repository_preview_floor(IMAGE, open_page))
        self.assertEqual(
            "https://hub.docker.com/v2/repositories/elsaworkflows/elsa-studio/tags?name=4.0.0-preview.&page=2&page_size=100",
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
                    publish.read_repository_preview_floor(IMAGE, lambda url, **kwargs: response)

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
            publish.read_repository_preview_floor(IMAGE, open_page)
        self.assertEqual(2, len(requests))

    def test_preview_floor_fails_closed_on_registry_errors_or_bad_pages(self) -> None:
        with self.assertRaises(URLError):
            publish.read_repository_preview_floor(IMAGE, lambda url, **kwargs: (_ for _ in ()).throw(URLError("offline")))
        with self.assertRaisesRegex(ValueError, "invalid tags response"):
            publish.read_repository_preview_floor(IMAGE, lambda url, **kwargs: FakeResponse({"next": None}))
        with self.assertRaisesRegex(ValueError, "malformed tag entry"):
            publish.read_repository_preview_floor(IMAGE, lambda url, **kwargs: FakeResponse({"results": [None], "next": None}))

    def test_preview_floor_rejects_pagination_into_another_studio_image(self) -> None:
        response = FakeResponse({"results": [], "next": "/v2/repositories/elsaworkflows/elsa-studio-workbench/tags?page=2"})
        with self.assertRaisesRegex(ValueError, "outside the Studio tags endpoint"):
            publish.read_repository_preview_floor(IMAGE, lambda url, **kwargs: response)

    def test_a_missing_repository_names_the_exact_repository_to_create(self) -> None:
        def open_page(url: str, *, timeout: int) -> FakeResponse:
            raise not_found(url)

        with self.assertRaises(publish.MissingRepositoryError) as raised:
            publish.read_repository_preview_floor(WORKBENCH_IMAGE, open_page)
        self.assertIn("Docker Hub repository elsaworkflows/elsa-studio-workbench does not exist", str(raised.exception))
        self.assertIn("Create it", str(raised.exception))

    def test_a_404_on_a_later_tags_page_fails_closed_without_claiming_a_missing_repository(self) -> None:
        requests: list[str] = []

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            requests.append(url)
            if len(requests) == 1:
                return FakeResponse({"results": [{"name": "4.0.0-preview.3"}], "next": "?page=2&page_size=100"})
            raise not_found(url)

        with self.assertRaises(HTTPError) as raised:
            publish.read_repository_preview_floor(IMAGE, open_page)
        self.assertNotIsInstance(raised.exception, publish.MissingRepositoryError)

    def test_shared_floor_takes_the_highest_preview_across_the_studio_images(self) -> None:
        floors = {IMAGE: ("4.0.0-preview.20",), WORKBENCH_IMAGE: ("4.0.0-preview.22",), FOUNDATION_IMAGE: ()}

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            image = next(image for image in floors if url == publish.docker_hub_tags_url(image))
            return FakeResponse(tags_page(image, *floors[image]))

        for image in floors:
            with self.subTest(image=image):
                self.assertEqual(22, publish.read_preview_floor(image, open_page))

    def test_shared_floor_ignores_a_sibling_repository_that_does_not_exist_yet(self) -> None:
        def open_page(url: str, *, timeout: int) -> FakeResponse:
            if url == publish.docker_hub_tags_url(IMAGE):
                return FakeResponse(tags_page(IMAGE, "4.0.0-preview.40"))
            raise not_found(url)

        self.assertEqual(40, publish.read_preview_floor(IMAGE, open_page))

    def test_shared_floor_refuses_when_the_image_own_repository_is_missing(self) -> None:
        def open_page(url: str, *, timeout: int) -> FakeResponse:
            if url == publish.docker_hub_tags_url(IMAGE):
                return FakeResponse(tags_page(IMAGE, "4.0.0-preview.40"))
            raise not_found(url)

        with self.assertRaisesRegex(publish.MissingRepositoryError, "elsaworkflows/elsa-studio-foundation"):
            publish.read_preview_floor(FOUNDATION_IMAGE, open_page)

    def test_shared_floor_skips_an_unreadable_sibling_but_never_the_image_itself(self) -> None:
        def sibling_fails(url: str, *, timeout: int) -> FakeResponse:
            if url == publish.docker_hub_tags_url(WORKBENCH_IMAGE):
                return FakeResponse(tags_page(WORKBENCH_IMAGE, "4.0.0-preview.3"))
            raise HTTPError(url, 403, "Forbidden", {}, None)

        with redirect_stderr(io.StringIO()) as notices:
            self.assertEqual(3, publish.read_preview_floor(WORKBENCH_IMAGE, sibling_fails))
        self.assertIn("Skipped the elsaworkflows/elsa-studio preview floor", notices.getvalue())

        def own_fails(url: str, *, timeout: int) -> FakeResponse:
            if url == publish.docker_hub_tags_url(WORKBENCH_IMAGE):
                raise HTTPError(url, 403, "Forbidden", {}, None)
            return FakeResponse(tags_page(IMAGE, "4.0.0-preview.40"))

        with self.assertRaises(HTTPError):
            publish.read_preview_floor(WORKBENCH_IMAGE, own_fails)

    def test_the_floor_read_lists_only_preview_named_tags(self) -> None:
        # Docker Hub refuses anonymous pages past 1,000 tags; the name filter keeps the listing under that offset.
        self.assertEqual(
            "https://hub.docker.com/v2/repositories/elsaworkflows/elsa-studio/tags?name=4.0.0-preview.&page=1&page_size=100",
            publish.docker_hub_tags_url(IMAGE),
        )

    def test_pagination_must_keep_the_preview_name_filter(self) -> None:
        for next_link in ("?name=latest&page=2", "?name=&page=2", "?name=4.0.0-preview.&name=x&page=2"):
            with self.subTest(next_link=next_link):
                response = FakeResponse({"results": [], "next": next_link})
                with self.assertRaises(ValueError):
                    publish.read_repository_preview_floor(IMAGE, lambda url, **kwargs: response)
        responses = [
            FakeResponse({"results": [{"name": "4.0.0-preview.4"}], "next": "?page=2&page_size=100"}),
            FakeResponse({"results": [{"name": "4.0.0-preview.9"}], "next": None}),
        ]
        requests: list[str] = []

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            requests.append(url)
            return responses[len(requests) - 1]

        # A next link that drops the filter gets it back, so the read never pages through every tag.
        self.assertEqual(9, publish.read_repository_preview_floor(IMAGE, open_page))
        self.assertIn("name=4.0.0-preview.", requests[1])

    def test_a_manual_rerun_of_one_flavor_accepts_a_number_another_flavor_already_pushed(self) -> None:
        # A manual main publish chose 4.0.0-preview.708. Essentials pushed it; Workbench failed on its missing repository.
        # Once the repository exists, the Workbench rerun checks the number against its own floor and is admitted.
        floors = {IMAGE: ("4.0.0-preview.708",), WORKBENCH_IMAGE: (), FOUNDATION_IMAGE: ("4.0.0-preview.707",)}

        def open_page(url: str, *, timeout: int) -> FakeResponse:
            image = next(image for image in floors if url == publish.docker_hub_tags_url(image))
            return FakeResponse(tags_page(image, *floors[image]))

        publish.require_preview_above_floor("4.0.0-preview.708", publish.read_repository_preview_floor(WORKBENCH_IMAGE, open_page))
        publish.require_preview_above_floor("4.0.0-preview.708", publish.read_repository_preview_floor(FOUNDATION_IMAGE, open_page))
        # The image's own repository still refuses a number it has already used.
        with self.assertRaises(ValueError):
            publish.require_preview_above_floor("4.0.0-preview.708", publish.read_repository_preview_floor(IMAGE, open_page))
        # The shared floor is what refused the rerun, so the manual path must not use it.
        with self.assertRaises(ValueError):
            publish.require_preview_above_floor("4.0.0-preview.708", publish.read_preview_floor(WORKBENCH_IMAGE, open_page))
        workflow = (REPO_ROOT / ".github/workflows/studio-docker.yml").read_text()
        self.assertIn('require_preview_above_floor(os.environ["PREVIEW_IMAGE_TAG"], read_repository_preview_floor(image))', workflow)
        self.assertNotIn("read_preview_floor", workflow)

    def test_repository_check_distinguishes_present_missing_and_unavailable(self) -> None:
        requests: list[str] = []

        def present(url: str, *, timeout: int) -> FakeResponse:
            requests.append(url)
            return FakeResponse({"name": "elsa-studio-workbench"})

        self.assertTrue(publish.repository_exists(WORKBENCH_IMAGE, present))
        self.assertEqual(["https://hub.docker.com/v2/repositories/elsaworkflows/elsa-studio-workbench/"], requests)
        self.assertFalse(publish.repository_exists(WORKBENCH_IMAGE, lambda url, **kwargs: (_ for _ in ()).throw(not_found(url))))
        with self.assertRaises(HTTPError):
            publish.repository_exists(
                WORKBENCH_IMAGE, lambda url, **kwargs: (_ for _ in ()).throw(HTTPError(url, 503, "Unavailable", {}, None))
            )

    def test_registry_reads_accept_only_the_studio_images(self) -> None:
        for image in ("elsaworkflows/elsa-workbench", "attacker/elsa-studio", "elsaworkflows/elsa-studio/../x", ""):
            with self.subTest(image=image), self.assertRaises(ValueError):
                publish.read_repository_preview_floor(image, lambda url, **kwargs: self.fail("no registry read expected"))
            with self.subTest(image=image), self.assertRaises(ValueError):
                publish.repository_exists(image, lambda url, **kwargs: self.fail("no registry read expected"))

    def test_next_version_starts_at_one_and_advances_the_global_floor(self) -> None:
        self.assertEqual("4.0.0-preview.1", publish.next_preview_version(None))
        self.assertEqual("4.0.0-preview.1", publish.next_preview_version(0))
        self.assertEqual("4.0.0-preview.21", publish.next_preview_version(20))
        with self.assertRaises(ValueError):
            publish.next_preview_version(-1)

    def test_manual_preview_must_exceed_the_floor_when_its_queue_slot_starts(self) -> None:
        for version, floor in (("4.0.0-preview.1", None), ("4.0.0-preview.21", 20), ("4.0.0-preview.30", 20)):
            with self.subTest(version=version, floor=floor):
                publish.require_preview_above_floor(version, floor)
        for version, floor in (("4.0.0-preview.20", 20), ("4.0.0-preview.19", 20), ("4.0.0-preview.0", None), ("invalid", 20)):
            with self.subTest(version=version, floor=floor), self.assertRaises(ValueError):
                publish.require_preview_above_floor(version, floor)

    def test_preview_tags_include_short_compatibility_and_full_unique_sha_without_latest(self) -> None:
        tags = publish.preview_image_tags(IMAGE, SHA, "4.0.0-preview.21")
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
            publish.preview_image_tags(IMAGE, SHA, "4.0.0-preview.bad")
        with self.assertRaises(ValueError):
            publish.preview_image_tags("elsaworkflows/elsa-workbench", SHA, "4.0.0-preview.21")
        self.assertEqual(
            [
                f"{WORKBENCH_IMAGE}:4.0.0-preview.21",
                f"{WORKBENCH_IMAGE}:sha-0123456",
                f"{WORKBENCH_IMAGE}:sha-{SHA}",
            ],
            publish.preview_image_tags(WORKBENCH_IMAGE, SHA, "4.0.0-preview.21"),
        )

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
            with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output), "SOURCE_SHA": SHA, "STUDIO_IMAGE": WORKBENCH_IMAGE}), patch.object(
                publish, "read_preview_floor", return_value=20
            ) as read_floor:
                with redirect_stdout(io.StringIO()):
                    self.assertEqual(0, publish.main(["preview-version"]))
            read_floor.assert_called_once_with(WORKBENCH_IMAGE)
            text = output.read_text()
            self.assertIn("studio_version=4.0.0-preview.21\n", text)
            self.assertIn(f"{WORKBENCH_IMAGE}:sha-{SHA}\n", text)
            self.assertNotIn(":latest", text)
            self.assertNotIn(f"{IMAGE}:", text)

    def test_preview_version_cli_refuses_an_unknown_or_missing_image(self) -> None:
        for image in ("", "elsaworkflows/elsa-workbench"):
            with self.subTest(image=image), tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / "github-output"
                with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output), "SOURCE_SHA": SHA, "STUDIO_IMAGE": image}), patch.object(
                    publish, "read_preview_floor", return_value=20
                ), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                    self.assertEqual(1, publish.main(["preview-version"]))
                self.assertFalse(output.exists())

    def test_preview_version_cli_reports_a_missing_repository_in_the_job_summary(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"
            summary = Path(directory) / "summary"
            stderr = io.StringIO()
            with patch.dict(os.environ, {
                "GITHUB_OUTPUT": str(output),
                "GITHUB_STEP_SUMMARY": str(summary),
                "SOURCE_SHA": SHA,
                "STUDIO_IMAGE": FOUNDATION_IMAGE,
            }), patch.object(
                publish, "read_preview_floor", side_effect=publish.MissingRepositoryError(FOUNDATION_IMAGE)
            ), redirect_stdout(io.StringIO()), redirect_stderr(stderr):
                self.assertEqual(1, publish.main(["preview-version"]))
            self.assertFalse(output.exists())
            self.assertIn("elsaworkflows/elsa-studio-foundation does not exist", stderr.getvalue())
            self.assertIn("elsaworkflows/elsa-studio-foundation does not exist", summary.read_text())

    def test_require_repository_cli_passes_or_names_the_repository_to_create(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            summary = Path(directory) / "summary"
            with patch.dict(os.environ, {"STUDIO_IMAGE": IMAGE, "GITHUB_STEP_SUMMARY": str(summary)}), patch.object(
                publish, "repository_exists", return_value=True
            ), redirect_stdout(io.StringIO()):
                self.assertEqual(0, publish.main(["require-repository"]))
            self.assertFalse(summary.exists())

            stderr = io.StringIO()
            with patch.dict(os.environ, {"STUDIO_IMAGE": WORKBENCH_IMAGE, "GITHUB_STEP_SUMMARY": str(summary)}), patch.object(
                publish, "repository_exists", return_value=False
            ), redirect_stdout(io.StringIO()), redirect_stderr(stderr):
                self.assertEqual(1, publish.main(["require-repository"]))
            self.assertIn("::error::Docker Hub repository elsaworkflows/elsa-studio-workbench does not exist", stderr.getvalue())
            self.assertIn("Create it on Docker Hub", summary.read_text())

    def test_require_repository_cli_fails_closed_on_an_unknown_image_or_registry_error(self) -> None:
        cases = (
            ({"STUDIO_IMAGE": "attacker/elsa-studio"}, None),
            ({"STUDIO_IMAGE": IMAGE}, URLError("offline")),
        )
        for env, failure in cases:
            with self.subTest(env=env), patch.dict(os.environ, env, clear=True), patch.object(
                publish, "repository_exists", side_effect=failure or AssertionError("no registry read expected")
            ), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                self.assertEqual(1, publish.main(["require-repository"]))

    def test_resolve_flavor_cli_writes_the_flavor_image_and_dockerfile(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "github-output"
            with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output), "STUDIO_FLAVOR": "essentials"}), redirect_stdout(io.StringIO()):
                self.assertEqual(0, publish.main(["resolve-flavor"]))
            text = output.read_text()
            self.assertIn("flavor=essentials\n", text)
            self.assertIn(f"image={IMAGE}\n", text)
            self.assertIn("dockerfile=Studio/docker/profiles/essentials/Dockerfile\n", text)
        for flavor in ("", "web", "Essentials"):
            with self.subTest(flavor=flavor), tempfile.TemporaryDirectory() as directory:
                output = Path(directory) / "github-output"
                with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output), "STUDIO_FLAVOR": flavor}), redirect_stdout(
                    io.StringIO()
                ), redirect_stderr(io.StringIO()):
                    self.assertEqual(1, publish.main(["resolve-flavor"]))
                self.assertFalse(output.exists())

    def test_flavor_table_follows_the_owner_image_decision(self) -> None:
        self.assertEqual(
            {
                "essentials": ("elsaworkflows/elsa-studio", "Studio/docker/profiles/essentials/Dockerfile"),
                "workbench": ("elsaworkflows/elsa-studio-workbench", "Studio/src/apps/Elsa.Studio.Workbench.Host/Dockerfile"),
                "foundation": ("elsaworkflows/elsa-studio-foundation", "Studio/src/apps/Elsa.Studio.Foundation.Host/Dockerfile"),
            },
            {flavor: (values["image"], values["dockerfile"]) for flavor, values in publish.STUDIO_FLAVORS.items()},
        )

    def test_flavor_label_values_are_single_line_without_commas(self) -> None:
        for flavor, values in publish.STUDIO_FLAVORS.items():
            for key in ("title", "description"):
                with self.subTest(flavor=flavor, key=key):
                    self.assertTrue(values[key])
                    self.assertNotRegex(values[key], r"[,\r\n]")

    def test_standalone_flavor_dockerfiles_start_their_own_apps(self) -> None:
        for flavor in ("foundation", "workbench"):
            with self.subTest(flavor=flavor):
                dockerfile = REPO_ROOT / publish.STUDIO_FLAVORS[flavor]["dockerfile"]
                self.assertTrue(dockerfile.is_file(), f"{dockerfile} is missing.")
                app = dockerfile.parent.name
                text = dockerfile.read_text()
                self.assertIn(f'ENTRYPOINT ["dotnet", "{app}.dll"]', text)
                self.assertIn(f"dotnet publish Studio/src/apps/{app}/{app}.csproj", text)

    def test_essentials_flavor_links_the_standalone_foundation_build(self) -> None:
        dockerfile = REPO_ROOT / publish.STUDIO_FLAVORS["essentials"]["dockerfile"]
        foundation = publish.STUDIO_FLAVORS["foundation"]["dockerfile"]
        self.assertIn("FROM ${FOUNDATION_IMAGE}", dockerfile.read_text())
        bake = dockerfile.with_name("release.hcl").read_text()
        self.assertIn(f'dockerfile = "{foundation}"', bake)
        self.assertIn('studio-foundation = "target:studio-foundation"', bake)

    def test_staging_index_requires_exact_runtime_children(self) -> None:
        amd64 = {"platform": {"os": "linux", "architecture": "amd64"}, "digest": "sha256:" + "a" * 64}
        arm64 = {"platform": {"os": "linux", "architecture": "arm64"}, "digest": "sha256:" + "b" * 64}
        attestation = {
            "platform": {"os": "unknown", "architecture": "unknown"},
            "annotations": {"vnd.docker.reference.type": "attestation-manifest",
                            "vnd.docker.reference.digest": arm64["digest"]},
        }
        self.assertEqual({"linux/amd64": amd64["digest"], "linux/arm64": arm64["digest"]},
                         runtime_children({"schemaVersion": 2, "manifests": [amd64, attestation, arm64]}))
        for manifests in ([amd64], [amd64, arm64, arm64], [amd64, {**arm64, "digest": amd64["digest"]}],
                          [amd64, arm64, {**attestation, "annotations": {
                              **attestation["annotations"], "vnd.docker.reference.digest": "sha256:" + "c" * 64,
                          }}]):
            with self.subTest(manifests=manifests), self.assertRaises(ValueError):
                runtime_children({"schemaVersion": 2, "manifests": manifests})

    def test_the_docker_workflow_builds_every_flavor_and_nothing_else(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/studio-docker.yml").read_text()
        match = re.search(r"^\s+flavor: \[([a-z, ]+)\]$", workflow, re.MULTILINE)
        self.assertIsNotNone(match, "studio-docker.yml has no flavor matrix.")
        self.assertEqual(sorted(publish.STUDIO_FLAVORS), sorted(item.strip() for item in match.group(1).split(",")))
        # The image and Dockerfile come only from resolve-flavor, never from a literal in the workflow.
        self.assertNotIn("Studio/src/apps/", workflow)
        self.assertLess(workflow.index("Qualify the exact Studio Essentials image"),
                        workflow.index("Apply ordinary Essentials tags"))
        self.assertLess(workflow.index("Apply ordinary Essentials tags"),
                        workflow.index("Promote the verified main digest to latest"))

    def test_manual_archive_payload_verification_follows_frontend_build(self) -> None:
        workflow = (REPO_ROOT / ".github/workflows/studio-docker.yml").read_text()
        trusted = workflow.index("Preserve trusted image validators")
        checkout = workflow.index("Checkout the verified source commit")
        frontend = workflow.index("Build frontend assets")
        payload = workflow.index("Verify the selected Studio package payload")
        first_docker_build = workflow.index("Build and optionally publish versioned image tags")
        self.assertIn("tools/ci/verify-studio-assets.py", workflow[trusted:checkout])
        self.assertIn('verify_packages(Path.cwd(), artifact, manifest["version"], verify_payload=False)', workflow[:frontend])
        self.assertLess(frontend, payload)
        self.assertLess(payload, first_docker_build)
        self.assertIn('$STUDIO_IMAGE_VALIDATORS/verify_studio_nuget_packages.py', workflow[payload:first_docker_build])

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
