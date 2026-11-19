"""Lightweight fail-closed tests for native StorePruning scratch selection."""

from __future__ import annotations

import copy
import io
import json
import os
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from contextlib import redirect_stdout
from unittest.mock import patch

import qualify_studio_consumers as qualify


def preflight(parent: Path, **changes: object) -> dict[str, object]:
    return {
        "status": "passed",
        "preflight": True,
        "membership": "Unenrolled",
        "rootIdentityPresent": True,
        "filesystemType": "ext4",
        "tempDirectory": str(parent),
        "root": str(parent / "studio-store-probe-owned"),
        **changes,
    }


class NativePreflightAdmissionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.parent = Path(tempfile.gettempdir()).resolve() / "studio-native-test-parent"

    def test_public_unenrolled_native_observation_qualifies(self) -> None:
        self.assertTrue(qualify.native_preflight_qualified(0, preflight(self.parent), self.parent))
        self.assertTrue(qualify.native_preflight_qualified(0, preflight(self.parent)))

    def test_malformed_status_object_does_not_qualify(self) -> None:
        for data in (None, [], "passed", 0):
            with self.subTest(data=data):
                self.assertFalse(qualify.native_preflight_qualified(0, data, self.parent))

    def test_refused_or_incomplete_preflight_does_not_qualify(self) -> None:
        for label, code, changes in (
            ("nonzero exit", 1, {}),
            ("failed status", 0, {"status": "failed"}),
            ("skipped status", 0, {"status": "skipped"}),
            ("missing preflight", 0, {"preflight": None}),
            ("nonliteral preflight", 0, {"preflight": 1}),
            ("unsupported membership", 0, {"membership": "Unsupported"}),
            ("already enrolled", 0, {"membership": "Complete"}),
            ("missing identity", 0, {"rootIdentityPresent": False}),
            ("nonliteral identity", 0, {"rootIdentityPresent": 1}),
        ):
            with self.subTest(reason=label):
                self.assertFalse(qualify.native_preflight_qualified(code, preflight(self.parent, **changes), self.parent))

    def test_unusable_or_missing_filesystem_metadata_does_not_qualify(self) -> None:
        for value in (None, False, 1, [], {}, "", " ", "x" * 65):
            with self.subTest(filesystemType=value):
                self.assertFalse(qualify.native_preflight_qualified(0, preflight(self.parent, filesystemType=value), self.parent))
        for field in preflight(self.parent):
            with self.subTest(missing=field):
                data = preflight(self.parent)
                del data[field]
                self.assertFalse(qualify.native_preflight_qualified(0, data, self.parent))

    def test_malformed_or_mismatched_paths_do_not_qualify(self) -> None:
        for field, value in (
            ("tempDirectory", None),
            ("tempDirectory", []),
            ("tempDirectory", "relative"),
            ("tempDirectory", str(self.parent.parent / "another-parent")),
            ("root", None),
            ("root", []),
            ("root", "studio-store-probe-relative"),
            ("root", str(self.parent)),
            ("root", str(self.parent / "unowned")),
            ("root", str(self.parent / "nested" / "studio-store-probe-owned")),
            ("root", str(self.parent.parent / "studio-store-probe-owned")),
        ):
            with self.subTest(field=field, value=value):
                self.assertFalse(qualify.native_preflight_qualified(0, preflight(self.parent, **{field: value}), self.parent))


class NativeScratchSelectionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.default = self.root / "default-temp"
        self.default.mkdir()
        self.owned = self.root / "native-probe-scratch"
        self.qualification = qualify.Qualification.__new__(qualify.Qualification)
        self.qualification.root = self.root
        self.qualification.report = {"stage": "prepare", "observations": {}}
        self.dll = self.root / "StorePruning.dll"
        self.dll.write_bytes(b"owned test fixture")
        self.calls: list[tuple[str, list[str], object, object]] = []

    def install_preflights(self, default: tuple[int, dict[str, object]], owned: tuple[int, dict[str, object]]) -> None:
        responses = iter((default, owned))

        def run(label: str, command: list[str], expected: object = 0, env: object = None) -> int:
            self.calls.append((label, command, expected, env))
            code, data = next(responses)
            (self.root / (label + ".log")).write_text(json.dumps(data))
            return code

        self.qualification.run = run

    def select(self) -> object:
        return self.qualification.native_scratch(self.dll)

    def test_qualified_owned_parent_is_preferred_even_when_default_qualifies(self) -> None:
        self.install_preflights((0, preflight(self.default)), (0, preflight(self.owned)))
        original = dict(os.environ)
        selected = self.select()
        self.assertEqual(dict(original, TMPDIR=str(self.owned)), selected)
        self.assertEqual(original, dict(os.environ))
        self.assertEqual(2, len(self.calls))
        self.assertIsNone(self.calls[0][3])
        self.assertEqual(str(self.owned), self.calls[1][3]["TMPDIR"])
        self.assertEqual(2, len(self.qualification.report["nativeFilesystems"]))
        self.assertEqual("owned", self.qualification.report["nativeScratchLocation"])
        self.assertEqual(["default", "owned"], [row["location"] for row in self.qualification.report["nativeFilesystems"]])
        self.assertEqual([True, True], [row["qualified"] for row in self.qualification.report["nativeFilesystems"]])
        for _, command, expected, _ in self.calls:
            self.assertEqual(["dotnet", str(self.dll), "--filesystem-preflight"], command)
            self.assertIsNone(expected)

    def test_genuinely_qualified_default_is_fallback_when_owned_is_refused(self) -> None:
        self.install_preflights(
            (0, preflight(self.default)),
            (1, preflight(self.owned, status="failed", membership="Unsupported", rootIdentityPresent=False)),
        )
        self.assertIsNone(self.select())
        self.assertEqual(2, len(self.calls))
        self.assertEqual(2, len(self.qualification.report["nativeFilesystems"]))
        self.assertEqual("default", self.qualification.report["nativeScratchLocation"])
        self.assertEqual([True, False], [row["qualified"] for row in self.qualification.report["nativeFilesystems"]])

    def test_owned_native_parent_is_selected_when_default_is_refused(self) -> None:
        self.install_preflights(
            (1, preflight(self.default, status="failed", membership="Unsupported", rootIdentityPresent=False)),
            (0, preflight(self.owned)),
        )
        self.assertEqual(str(self.owned), self.select()["TMPDIR"])
        self.assertEqual(2, len(self.calls))

    def test_neither_refused_parent_can_be_promoted_to_a_success(self) -> None:
        self.install_preflights(
            (1, preflight(self.default, status="failed", membership="Unsupported", rootIdentityPresent=False)),
            (1, preflight(self.owned, status="failed", membership="Unsupported", rootIdentityPresent=False)),
        )
        with self.assertRaises(RuntimeError):
            self.select()
        self.assertEqual(2, len(self.calls))
        self.assertEqual(2, len(self.qualification.report["nativeFilesystems"]))
        self.assertEqual([False, False], [row["qualified"] for row in self.qualification.report["nativeFilesystems"]])
        self.assertEqual([1, 1], [row["exitCode"] for row in self.qualification.report["nativeFilesystems"]])
        self.assertNotIn("nativeScratchLocation", self.qualification.report)
        self.assertEqual(self.qualification.report["nativeFilesystems"], json.loads((self.root / "report.json").read_text())["nativeFilesystems"])

    def test_passed_but_mismatched_metadata_is_not_used_as_fallback(self) -> None:
        self.install_preflights(
            (0, preflight(self.default, root=str(self.owned / "studio-store-probe-owned"))),
            (0, preflight(self.default)),
        )
        with self.assertRaises(RuntimeError):
            self.select()
        self.assertEqual(2, len(self.calls))


class LifecycleScratchIntegrationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.qualification = qualify.Qualification.__new__(qualify.Qualification)
        self.qualification.root = self.root
        self.qualification.report = {"stage": "prepare", "observations": {}}
        self.probes = self.root / "probes"
        for relative in ("Runner/bin/Release/net10.0/HostingReuse.Probe.dll", "StorePruning/bin/Release/net10.0/StorePruning.dll"):
            dll = self.probes / "HostingReuse.Probe" / relative
            dll.parent.mkdir(parents=True)
            dll.write_bytes(b"owned lifecycle fixture")
        self.calls: list[tuple[str, list[str], object]] = []
        self.overrides: dict[str, tuple[int, dict[str, object]]] = {}

        def run(label: str, command: list[str], expected: object = 0, env: object = None) -> int:
            self.calls.append((label, command, env))
            if label.endswith(("-restore", "-build")):
                return 0
            if label.startswith("pruning-filesystem-"):
                parent = Path(env["TMPDIR"]) if env else self.root / "default-temp"
                data = preflight(parent, status="failed", membership="Unsupported", rootIdentityPresent=False)
                (self.root / (label + ".log")).write_text(json.dumps(data))
                return 1
            negative = label.endswith("-control")
            pruning = label.startswith("pruning-")
            data = {
                "status": "failed" if negative else "passed",
                "groups" if pruning else "completed": list(range((4 if negative else 6) if pruning else (1 if negative else 4))),
            }
            if negative:
                data["error"] = "Admitted HTTP range response failed." if pruning else "An update did not replace the serving shell generation."
            code, data = self.overrides.get(label, (1 if negative else 0, data))
            (self.root / (label + ".log")).write_text(json.dumps(data))
            return code

        self.qualification.run = run

    def lifecycle(self) -> None:
        with patch.object(qualify, "PROBES", self.probes):
            self.qualification.lifecycle()

    def test_selected_environment_is_scoped_to_all_three_pruning_variants(self) -> None:
        selected = {"TMPDIR": str(self.root / "native-probe-scratch")}
        with patch.object(self.qualification, "native_scratch", return_value=selected) as native_scratch:
            self.lifecycle()
        native_scratch.assert_called_once()
        self.assertEqual({"reuse-normal", "reuse-control", "reuse-restored", "pruning-normal", "pruning-control", "pruning-restored"}, set(self.qualification.report["observations"]))
        for label, command, env in self.calls:
            with self.subTest(label=label):
                self.assertIs(selected if label in {"pruning-normal", "pruning-control", "pruning-restored"} else None, env)
        self.assertIn("--release-reader-before-read", next(command for label, command, _ in self.calls if label == "pruning-control"))
        self.assertTrue(self.qualification.report["observations"]["pruning-control"]["expectedFailureObserved"])

    def test_failed_preflights_stop_before_any_pruning_variant(self) -> None:
        with self.assertRaisesRegex(RuntimeError, "native filesystem precondition"):
            self.lifecycle()
        self.assertFalse(any(label in {"pruning-normal", "pruning-control", "pruning-restored"} for label, _, _ in self.calls))
        self.assertEqual(2, sum(label.startswith("pruning-filesystem-") for label, _, _ in self.calls))
        self.assertEqual([False, False], [row["qualified"] for row in self.qualification.report["nativeFilesystems"]])

    def test_malformed_preflight_protocol_stops_before_pruning(self) -> None:
        ordinary_run = self.qualification.run

        def run(label: str, command: list[str], expected: object = 0, env: object = None) -> int:
            if label.startswith("pruning-filesystem-"):
                self.calls.append((label, command, env))
                (self.root / (label + ".log")).write_text("not a native status object")
                return 0
            return ordinary_run(label, command, expected=expected, env=env)

        self.qualification.run = run
        with self.assertRaisesRegex(RuntimeError, "Expected one lifecycle status object"):
            self.lifecycle()
        self.assertFalse(any(label in {"pruning-normal", "pruning-control", "pruning-restored"} for label, _, _ in self.calls))
        self.assertEqual(1, sum(label.startswith("pruning-filesystem-") for label, _, _ in self.calls))

    def test_original_group_counts_and_control_diagnostic_remain_fail_closed(self) -> None:
        cases = (
            ("reuse-normal", 0, {"status": "passed", "completed": [1, 2, 3]}),
            ("reuse-control", 1, {"status": "failed", "completed": [1], "error": "unrelated"}),
            ("pruning-normal", 0, {"status": "passed", "groups": list(range(5))}),
            ("pruning-control", 1, {"status": "failed", "groups": list(range(3)), "error": "Admitted HTTP range response failed."}),
            ("pruning-control", 1, {"status": "failed", "groups": list(range(4)), "error": "unrelated"}),
            ("pruning-control", 0, {"status": "passed", "groups": list(range(4)), "error": "Admitted HTTP range response failed."}),
            ("pruning-restored", 1, {"status": "failed", "groups": list(range(6))}),
        )
        for label, code, data in cases:
            with self.subTest(label=label, code=code, data=data):
                self.overrides = {label: (code, copy.deepcopy(data))}
                with patch.object(self.qualification, "native_scratch", return_value=None):
                    with self.assertRaises(RuntimeError):
                        self.lifecycle()


class QualificationRunEnvironmentTests(unittest.TestCase):
    def test_subprocess_receives_only_the_explicit_environment_for_that_run(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            qualification = qualify.Qualification.__new__(qualify.Qualification)
            qualification.root = Path(directory)
            qualification.report = {"stage": "prepare", "observations": {}}
            selected = {"TMPDIR": str(Path(directory) / "native-probe-scratch")}
            with patch.object(qualify.subprocess, "run", return_value=SimpleNamespace(returncode=0)) as run, redirect_stdout(io.StringIO()):
                qualification.run("owned", ["not-executed"], env=selected)
                qualification.run("default", ["not-executed"])
            self.assertIs(selected, run.call_args_list[0].kwargs["env"])
            self.assertIsNone(run.call_args_list[1].kwargs["env"])


if __name__ == "__main__":
    unittest.main()
