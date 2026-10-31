"""Cause/integrity checks for the driver; these synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from disclosure_journeys import (require_native_denial, require_no_adapter_admission, require_stored_result,
                                canonical_result, _checkpoint, _no_admission, qualify_context_disclosure)


class DisclosureQualificationTests(unittest.TestCase):
    def stream(self, code="agent.context.incomplete", status=400, *, http=200, extra=None):
        values = [{"kind": "error", "error": {"code": code, "statusCode": status}}]
        values.extend(extra or [])
        return SimpleNamespace(status=http, error_type=None, frames=[SimpleNamespace(value=value) for value in values])

    def test_wrong_auth_session_provider_or_phase_failure_never_qualifies(self):
        require_native_denial(self.stream(), "accepted-message")
        require_native_denial(self.stream("agent.result.disclosure_denied", 403), "accepted-receipt")
        for code, status in (("agent.session.forbidden", 403), ("agent.session.unavailable", 404),
                             ("agent.turn.source_missing", 409), ("agent.provider.anthropic.turn_failed", 502),
                             ("agent.context.incomplete", 401), ("agent.result.disclosure_denied", 403)):
            with self.subTest(code=code, status=status):
                with self.assertRaises(RuntimeError): require_native_denial(self.stream(code, status), "accepted-message")
        with self.assertRaises(RuntimeError): require_native_denial(self.stream(), "accepted-receipt")
        with self.assertRaises(RuntimeError): require_native_denial(self.stream(http=401), "accepted-message")

    def test_provider_output_or_multiple_errors_cannot_hide_behind_correct_denial(self):
        for kind in ("messageDelta", "completed", "proposalCreated", "toolCallStarted", "toolCallCompleted", "error"):
            with self.subTest(kind=kind):
                with self.assertRaises(RuntimeError):
                    require_native_denial(self.stream(extra=[{"kind": kind}]), "accepted-message")

    def test_any_actual_adapter_record_is_rejected_even_with_another_command_identity(self):
        require_no_adapter_admission([{"kind": "protocol.gate.cancelled", "data": {"commandId": "diagnostic"}}])
        for provider in ("copilot", "anthropic"):
            for kind in ("request", "call", "return", "continuation"):
                with self.subTest(provider=provider, kind=kind):
                    with self.assertRaises(RuntimeError):
                        require_no_adapter_admission([{"kind": provider + "." + kind, "data": {"commandId": "unrelated-command"}}])

    def result(self):
        return {"toolCallId": "call-1", "toolName": "owned_tool", "succeeded": True, "content": "Observed saved effect.",
                "payload": {"typeId": "owned.result", "schemaVersion": "1", "data": {"visible": True}},
                "access": {"moduleId": "Owned.Module", "invocationId": "invocation-1", "disclosureSourceId": "owned.context"},
                "receipt": {"scope": "serverPersisted", "invocationId": "invocation-1", "before": {"revision": "r1"},
                            "after": {"revision": "r2"}, "evidence": {"visible": True}}}

    def test_stored_truth_allows_only_verified_scope_serialization_difference(self):
        expected = self.result()
        original = copy.deepcopy(expected)
        for scope in (1, "ServerPersisted", "serverPersisted"):
            actual = copy.deepcopy(expected); actual["receipt"]["scope"] = scope
            observed = require_stored_result([{"role": "tool", "status": "completed", "content": json.dumps(actual)}], expected)
            self.assertEqual(actual, observed)
        self.assertEqual(original, expected)
        with self.assertRaises(RuntimeError): canonical_result({**expected, "receipt": {"scope": True}})

    def test_denial_cannot_replace_duplicate_drop_or_mutate_accepted_outcome(self):
        expected = self.result()
        for change in ("success", "revision", "data", "source", "scope", "duplicate", "missing", "failed-status", "malformed"):
            with self.subTest(change=change):
                actual = copy.deepcopy(expected)
                if change == "success": actual["succeeded"] = False
                elif change == "revision": actual["receipt"]["after"]["revision"] = "r3"
                elif change == "data": actual["payload"]["data"]["visible"] = False
                elif change == "source": actual["access"]["disclosureSourceId"] = "foreign.context"
                elif change == "scope": actual["receipt"]["scope"] = "clientWorkingState"
                message = {"role": "tool", "status": "failed" if change == "failed-status" else "completed",
                           "content": "{" if change == "malformed" else json.dumps(actual)}
                messages = [] if change == "missing" else [message, message] if change == "duplicate" else [message]
                with self.assertRaises((RuntimeError, ValueError)): require_stored_result(messages, expected)

    def runtime(self, root):
        paths = {provider: root / (provider + ".jsonl") for provider in ("copilot", "anthropic")}
        for provider, path in paths.items():
            path.write_text(json.dumps({"kind": provider + ".request", "data": {"commandId": "prior-positive"}}) + "\n")
        sequence = 0
        def output(name):
            nonlocal sequence
            sequence += 1
            path = root / (str(sequence) + "-" + name); path.mkdir()
            return path
        return SimpleNamespace(run=SimpleNamespace(profiles={"backend": {"traceFiles": paths}}),
                               backend=SimpleNamespace(bearer=None), output=output)

    def test_truncated_replaced_or_incomplete_trace_cannot_prove_zero_admission(self):
        for mutation in ("truncate", "replace", "partial"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                runtime = self.runtime(Path(directory))
                checkpoint = _checkpoint(runtime)
                path = runtime.run.profiles["backend"]["traceFiles"]["copilot"]
                if mutation == "truncate": path.write_bytes(b"")
                elif mutation == "replace": path.write_bytes(path.read_bytes().replace(b"prior-positive", b"other-positive"))
                else:
                    with path.open("ab") as stream: stream.write(b'{"kind":')
                with self.assertRaises(RuntimeError): _no_admission(runtime, checkpoint, "negative")

    def test_native_case_failure_is_mandatory_and_preserves_original_failed_case_status(self):
        original = [{"id": "original-m1-control", "status": "failed", "expectedFailureObserved": True}]
        run = SimpleNamespace(report={"cases": copy.deepcopy(original)}, source="committed-source", save=Mock())
        runtime = SimpleNamespace(run=run, backend=object(), witness={"moduleId": "Owned.Module"},
            select_provider=Mock(return_value=[]), native=SimpleNamespace(generation=Mock(return_value=(
                {"contextSources": [{"moduleId": "Owned.Module", "id": "owned.context"}]}, {"receipt": {}}))))
        with patch("disclosure_journeys._message_case", side_effect=RuntimeError("native-case-failed")):
            with self.assertRaisesRegex(RuntimeError, "native-case-failed"):
                qualify_context_disclosure(runtime)
        self.assertEqual(original, run.report["cases"])
        section = run.report["contextDisclosureQualification"]
        self.assertEqual("failed", section["status"])
        self.assertEqual([], section["records"])
        self.assertEqual("copilot-accepted-message-authority-withdrawal", section["activeCase"])
        self.assertEqual("failed", section["stages"][-1]["status"])


if __name__ == "__main__":
    unittest.main()
