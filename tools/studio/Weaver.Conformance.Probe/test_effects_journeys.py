"""Synthetic receipt integrity controls; never native, provider or package evidence."""
import copy
import hashlib
import json
from pathlib import Path
import shutil
import signal
import subprocess
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

from case_records import retained, retained_json
from effects_journeys import (CHECK_KIND, CHECK_TOOL, EXPECTED_CASES, HOST, MODULE, RESTART_REQUIREMENTS,
                             TOOLS, WIDGET, manifest_gaps, require_effects, require_performed, require_refusal,
                             require_resource_inventory, require_adapter_result, restart_manifest_gaps,
                             require_called_refusal, require_normal_terminal, require_canary_absent, frames_from_raw, _policy,
                             require_nested_observations, _stop_browser_groups, require_browser_stopped)


def check_fixture(passed=True):
    before = {"widgetId": WIDGET, "hostId": HOST, "revision": "revision-one", "visible": True}
    target = {"moduleId": MODULE, "resourceType": "home-widget", "resourceId": WIDGET, "revision": before["revision"]}
    prepared = {"moduleId": MODULE, "toolName": CHECK_TOOL, "toolContractVersion": "1", "executionLocation": "server", "client": None,
                "requiresApproval": True, "risk": "admin", "invocationId": "invocation-one", "toolCallId": "call-one", "target": target,
                "arguments": {"expectedVisible": passed}, "requiredPrivilegeClasses": ["administrative"]}
    data = {**{key: before[key] for key in ("widgetId", "hostId", "revision")},
            "expectedVisible": passed, "actualVisible": True, "passed": passed, "ran": True, "callNumber": 1}
    prepared["preview"] = {"typeId": CHECK_KIND, "schemaVersion": "1", "data": {
        "widgetId": WIDGET, "hostId": HOST, "revision": before["revision"], "expectedVisible": passed}}
    payload = {"typeId": CHECK_KIND, "schemaVersion": "1", "data": data}
    receipt = {"invocationId": prepared["invocationId"], "scope": "noPersistedEffect", "before": copy.deepcopy(target), "after": copy.deepcopy(target),
               "moduleCode": "acme.home.effects.check_passed" if passed else "acme.home.effects.check_failed", "evidence": copy.deepcopy(payload)}
    execution = {"status": "performed", "executed": True, "toolResult": {"toolName": CHECK_TOOL, "toolCallId": "call-one", "succeeded": True,
                                                                       "payload": payload, "receipt": receipt}}
    layout = {"revision": before["revision"], "value": {"widgets": [{"id": WIDGET, "visible": True, "size": "medium"}], "unrelated": [1, 2]}}
    journal = {key: before[key] for key in ("widgetId", "hostId", "revision")}
    old = dict(journal, ranCount=0, runs=[])
    new = dict(journal, ranCount=1, runs=[{"invocationId": "invocation-one", "toolCallId": "call-one", "toolName": CHECK_TOOL,
                                        "revision": before["revision"], "passed": passed, "callNumber": 1}])
    return [prepared, execution, before, copy.deepcopy(before), layout, copy.deepcopy(layout), old, new]


class PerformedIntegrityTests(unittest.TestCase):
    def test_business_false_is_a_check_that_ran_without_a_persisted_change(self):
        for verdict in (False, True):
            with self.subTest(verdict=verdict):
                self.assertIs(require_performed(*check_fixture(verdict))["passed"], verdict)

    def test_normalized_typed_code_keeps_a_genuine_owner_result(self):
        values = check_fixture()
        values[1]["toolResult"]["receipt"]["moduleCode"] = "module.code.unsupported"
        self.assertTrue(require_performed(*values)["ran"])

    def test_foreign_missing_duplicate_and_wrong_verdict_evidence_fail_closed(self):
        changes = {
            "wrong-module": lambda x: x[0].update(moduleId="Other"),
            "wrong-contract": lambda x: x[0].update(toolContractVersion="2"),
            "wrong-location": lambda x: x[0].update(executionLocation="clientWorkingState"),
            "wrong-preview": lambda x: x[0]["preview"]["data"].update(expectedVisible=False),
            "wrong-call": lambda x: x[1]["toolResult"].update(toolCallId="foreign"),
            "wrong-invocation": lambda x: x[1]["toolResult"]["receipt"].update(invocationId="foreign"),
            "wrong-scope": lambda x: x[1]["toolResult"]["receipt"].update(scope="serverPersisted"),
            "missing-code": lambda x: x[1]["toolResult"]["receipt"].pop("moduleCode"),
            "missing-evidence": lambda x: x[1]["toolResult"]["receipt"].pop("evidence"),
            "foreign-host": lambda x: x[1]["toolResult"]["payload"]["data"].update(hostId="foreign"),
            "wrong-status": lambda x: x[1].update(status="executed"),
            "not-ran": lambda x: x[1]["toolResult"]["payload"]["data"].update(ran=False),
            "changed-widget": lambda x: x[3].update(visible=False),
            "changed-revision": lambda x: x[3].update(revision="two"),
            "unrelated-change": lambda x: x[5]["value"].update(unrelated=[]),
            "missing-journal": lambda x: x[7].update(runs=[]),
            "wrong-total": lambda x: x[7].update(ranCount=2),
            "wrong-verdict": lambda x: x[7]["runs"][0].update(passed=False),
            "wrong-journal-call": lambda x: x[7]["runs"][0].update(toolCallId="foreign"),
            "wrong-call-number": lambda x: x[7]["runs"][0].update(callNumber=0),
            "wrong-after-target": lambda x: x[1]["toolResult"]["receipt"]["after"].update(resourceId="other"),
        }
        for name, mutate in changes.items():
            with self.subTest(name=name):
                values = check_fixture(); mutate(values)
                with self.assertRaises(RuntimeError): require_performed(*values)
        values = check_fixture()
        values[7]["ranCount"] = 2
        values[7]["runs"].append(dict(values[7]["runs"][0], callNumber=2))
        with self.assertRaises(RuntimeError): require_performed(*values)

    def test_absent_old_window_cannot_be_promoted_to_unique_success(self):
        values = check_fixture()
        values[6].update(ranCount=16, runs=[dict(values[7]["runs"][0], invocationId="other-" + str(i), callNumber=i) for i in range(1, 17)])
        values[7].update(ranCount=17, runs=[dict(values[7]["runs"][0], invocationId="other-" + str(i), callNumber=i) for i in range(2, 18)])
        with self.assertRaises(RuntimeError): require_performed(*values)

    def test_actual_adapter_result_binding_rejects_duplicate_foreign_and_changed_receipts(self):
        expected = check_fixture()[1]["toolResult"]
        for provider in ("copilot", "anthropic"):
            results = [copy.deepcopy(expected)] if provider == "copilot" else [{"kind": "functionResult", "callId": "command", "result": json.dumps(expected)}]
            rows = [{"kind": provider + ".continuation", "data": {"commandId": "command", "results": results}}]
            require_adapter_result(rows, provider, "command", expected)
            with self.assertRaises(RuntimeError): require_adapter_result(rows + rows, provider, "command", expected)
            with self.assertRaises(RuntimeError): require_adapter_result(rows, provider, "foreign", expected)
            changed = copy.deepcopy(expected); changed["receipt"]["invocationId"] = "foreign"
            with self.assertRaises(RuntimeError): require_adapter_result(rows, provider, "command", changed)


class EffectsEvidenceTests(unittest.TestCase):
    def test_refusal_requires_the_exact_typed_cause_and_classification(self):
        for owner in (False, True):
            code = "module.owner.unavailable" if owner else "module.code.unsupported"
            values = [{"error": {"code": code, "ownerUnavailable": owner}}]
            self.assertTrue(require_refusal(values, code, owner_unavailable=owner))
            for wrong in ("weaver.actor.unresolved", "weaver.session.owned_elsewhere", "provider.unavailable", "weaver.proposal.closed"):
                with self.subTest(owner=owner, wrong=wrong):
                    with self.assertRaises(RuntimeError): require_refusal([{"error": {"code": wrong}}], code, owner_unavailable=owner)
            with self.assertRaises(RuntimeError): require_refusal(values, code, owner_unavailable=not owner)

    def test_absent_default_false_is_allowed_but_present_non_boolean_classification_is_not(self):
        self.assertTrue(require_refusal({"code": "module.code.unsupported"}, "module.code.unsupported", owner_unavailable=False))
        for value in (None, "false", 0, 1, [], {}):
            with self.subTest(value=value), self.assertRaises(RuntimeError):
                require_refusal({"code": "module.code.unsupported", "ownerUnavailable": value}, "module.code.unsupported", owner_unavailable=False)

    def test_new_inventory_keeps_each_original_tool_and_context_instead_of_replacing_them(self):
        witness = {"moduleId": MODULE, "readTool": "acme_home_read_widget", "writeTool": "acme_home_set_visibility", "sizeTool": "acme_home_set_size"}
        rows = [{"moduleId": MODULE, "contractVersion": "1", "name": name} for name in [*TOOLS, witness["readTool"], witness["writeTool"], witness["sizeTool"]]]
        contexts = [{"moduleId": MODULE, "id": name} for name in ("acme.home.widget-context", "acme.home.effects-context")]
        require_resource_inventory(rows, contexts, witness)
        for changed in (rows[:-1], rows + [rows[0]], [dict(row, name="foreign") if row["name"] == witness["readTool"] else row for row in rows]):
            with self.assertRaises(RuntimeError): require_resource_inventory(changed, contexts, witness)
        with self.assertRaises(RuntimeError): require_resource_inventory(rows, contexts[:1], witness)

    def test_owned_reference_rejects_missing_rehashed_foreign_and_duplicate_keys(self):
        with tempfile.TemporaryDirectory() as directory, tempfile.TemporaryDirectory() as foreign:
            root = Path(directory); path = root / "actual.json"; path.write_text('{"value":1}')
            ref = retained(path)
            self.assertEqual({"value": 1}, retained_json(root, ref))
            path.write_text('{"value":2}')
            with self.assertRaises(RuntimeError): retained_json(root, ref)
            other = Path(foreign) / "other.json"; other.write_text('{}')
            with self.assertRaises(RuntimeError): retained_json(root, retained(other))
            path.write_text('{"value":1,"value":2}')
            with self.assertRaises(ValueError): retained_json(root, retained(path))
            path.unlink()
            with self.assertRaises(RuntimeError): retained_json(root, ref)

    def test_entire_matrix_is_mandatory_and_neither_section_can_self_promote(self):
        source = "a" * 40
        ids = list(EXPECTED_CASES)
        section = {"source": source, "status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False,
                   "caseIDs": ids, "records": [{"id": name, "status": "passed", "record": {"path": "/unexecuted", "sha256": "b" * 64}} for name in ids],
                   "stages": [{"name": name, "status": "completed", "boundSeconds": 180} for name in ids]}
        self.assertEqual([], manifest_gaps(section, source))  # Structural only; require_effects must still read every receipt.
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(RuntimeError): require_effects(section, Path(directory), source=source)
        for change in (lambda x: x["records"].pop(), lambda x: x["caseIDs"].append(ids[0]), lambda x: x.update(source="c" * 40),
                       lambda x: x.update(liveProvider=True), lambda x: x["records"][0].update(status="skipped"),
                       lambda x: x["stages"][0].update(status="failed")):
            value = copy.deepcopy(section); change(value)
            self.assertTrue(manifest_gaps(value, source))
        self.assertTrue(restart_manifest_gaps({"status": "passed", "source": source, "caseIDs": list(RESTART_REQUIREMENTS)}, source))

    def test_all_six_tools_and_both_adapters_have_each_live_class_boundary(self):
        self.assertEqual(6, len(TOOLS))
        self.assertEqual(len(EXPECTED_CASES), len(set(EXPECTED_CASES)))
        for provider in ("copilot", "anthropic"):
            for tool, classes in TOOLS.items():
                for condition in ("unconfigured", "administrative-only", "all-enabled", "approval-disabled", "execution-disabled", "cancelled", "stale", "duplicate-delivery"):
                    self.assertIn(provider + "/" + tool + ":" + condition, EXPECTED_CASES)
                for required in classes:
                    self.assertIn(provider + "/" + tool + ":missing-" + required, EXPECTED_CASES)


def refusal_fixture(provider, reviewed=False):
    """Synthetic actual-wire shapes, never a host/provider witness."""
    command, tool, session, turn = "command-one", CHECK_TOOL, "session-one", "turn-one"
    call = command if provider == "anthropic" else "native-call-one"
    error = {"code": "module.code.unsupported", "message": "The module returned an unsupported outcome code.", "statusCode": 409}
    result = {"toolCallId": call, "toolName": tool, "succeeded": False, "content": error["message"], "error": error}
    def frame(kind, payload=None): return {"id": kind, "kind": kind, "payload": payload}
    started = frame("started", {"turnId": turn})
    called = frame("toolCallStarted", {"toolCallId": call, "toolName": tool})
    completion = [frame("toolCallCompleted", {"toolCallId": call, "toolName": tool, "succeeded": False, "result": result}),
                  dict(frame("contributedResult", result), resultKind="moduleResult"), frame("completed")]
    proposal = "proposal-one" if reviewed else None
    initial = [started, called, *completion]
    terminal = initial
    if reviewed:
        initial = [started, called, frame("proposalCreated", {"id": proposal, "preparedInvocation": {"turnId": turn, "toolCallId": call}}),
                   frame("toolApprovalRequested")]
        terminal = [frame("started", {"turnId": "continuation-turn"}), frame("completed")]
    args = {"expectedVisible": True}
    request = {"commandId": command, "sessionId": session, "tools": [{"name": tool}],
               "messages": [{"role": "user", "contents": [{"kind": "text", "text": "Current native prompt"}]}]}
    trace = [{"kind": provider + ".request", "data": request},
             {"kind": provider + ".call", "data": {"commandId": command, "toolName": tool, "arguments": args}}]
    if provider == "copilot":
        returned = {"status": "awaiting-approval", "executed": False, "proposalId": proposal} if reviewed else {"status": "denied", "executed": False, "error": error}
        trace.append({"kind": "copilot.return", "data": {"commandId": command, "toolName": tool, "result": returned}})
        if reviewed:
            trace.extend([{"kind": "copilot.request", "data": {"commandId": command, "sessionId": session, "tools": []}},
                          {"kind": "copilot.continuation", "data": {"commandId": command, "sessionId": session, "results": [result]}}])
    else:
        values = [{"kind": "functionResult", "callId": command, "result": json.dumps(result)}]
        trace.extend([{"kind": "anthropic.request", "data": {"commandId": command, "tools": [],
                      "messages": [*request["messages"], {"role": "tool", "contents": values}]}},
                      {"kind": "anthropic.continuation", "data": {"commandId": command, "results": values}}])
    reopened = {"sessionId": session, "messages": [{"role": "tool", "status": "completed", "content": json.dumps(result)}]}
    return [trace, provider, command, tool, args, session, initial, terminal, error["code"], reopened], {"proposal_id": proposal}


class RefusalContinuationIntegrityTests(unittest.TestCase):
    def test_each_actual_adapter_shape_requires_the_native_reopened_typed_failure(self):
        for provider in ("copilot", "anthropic"):
            for reviewed in (False, True):
                args, kwargs = refusal_fixture(provider, reviewed)
                with self.subTest(provider=provider, reviewed=reviewed):
                    self.assertEqual("module.code.unsupported", require_called_refusal(*args, **kwargs)["error"]["code"])

    def test_provider_exception_missing_duplicate_foreign_or_changed_completion_is_rejected(self):
        for provider in ("copilot", "anthropic"):
            for reviewed in (False, True):
                mutations = {
                    "missing-actual-return-or-continuation": lambda x: x[0].pop(),
                    "duplicate-actual-return-or-continuation": lambda x: x[0].append(copy.deepcopy(x[0][-1])),
                    "wrong-current-command": lambda x: x[0][-1]["data"].update(commandId="historical-command"),
                    "wrong-arguments": lambda x: x[0][1]["data"].update(arguments={"expectedVisible": False}),
                    "wrong-session": lambda x: x[9].update(sessionId="another-session"),
                    "missing-recorded-refusal": lambda x: x[9].update(messages=[]),
                    "duplicate-recorded-refusal": lambda x: x[9]["messages"].append(copy.deepcopy(x[9]["messages"][0])),
                    "error-terminal": lambda x: x[7][-1].update(kind="error"),
                    "cancelled-terminal": lambda x: x[7][-1].update(kind="turnCancelled"),
                }
                for name, mutate in mutations.items():
                    with self.subTest(provider=provider, reviewed=reviewed, mutation=name):
                        args, kwargs = refusal_fixture(provider, reviewed); mutate(args)
                        with self.assertRaises(RuntimeError): require_called_refusal(*args, **kwargs)
                for member, value in (("code", "sdk.error"), ("ownerUnavailable", True), ("statusCode", "409")):
                    args, kwargs = refusal_fixture(provider, reviewed)
                    native = json.loads(args[9]["messages"][0]["content"]); native["error"][member] = value
                    args[9]["messages"][0]["content"] = json.dumps(native)
                    with self.subTest(provider=provider, reviewed=reviewed, member=member), self.assertRaises(RuntimeError):
                        require_called_refusal(*args, **kwargs)

    def test_bare_sdk_error_or_a_result_with_dropped_error_cannot_qualify(self):
        for provider in ("copilot", "anthropic"):
            for reviewed in (False, True):
                args, kwargs = refusal_fixture(provider, reviewed)
                if provider == "copilot" and not reviewed:
                    args[0][-1]["data"]["result"]["error"] = "SDK tool execution failed"
                elif provider == "copilot":
                    args[0][-1]["data"]["results"][0].pop("error")
                else:
                    result = json.loads(args[0][-1]["data"]["results"][0]["result"]); result.pop("error")
                    args[0][-1]["data"]["results"][0]["result"] = json.dumps(result)
                with self.subTest(provider=provider, reviewed=reviewed), self.assertRaises(RuntimeError):
                    require_called_refusal(*args, **kwargs)

    def test_anthropic_historical_function_result_cannot_satisfy_a_current_refusal(self):
        args, kwargs = refusal_fixture("anthropic")
        current = args[0][-2]["data"]["messages"]
        current.append({"role": "user", "contents": [{"kind": "text", "text": "A new user prompt"}]})
        with self.assertRaises(RuntimeError): require_called_refusal(*args, **kwargs)

    def test_human_denial_keeps_its_real_lifecycle_status_instead_of_inventing_an_error(self):
        for provider in ("copilot", "anthropic"):
            args, kwargs = refusal_fixture(provider, True)
            args[8] = "weaver.proposal.closed"
            prepared = args[6][2]["payload"]["preparedInvocation"]
            prepared["invocationId"] = "invocation-one"
            denied = {"id": "proposal-one", "sessionId": "session-one", "status": "denied", "preparedInvocation": prepared}
            result = json.loads(args[9]["messages"][0]["content"])
            result.pop("error")
            result["payload"] = {"typeId": "weaver.operation-outcome", "schemaVersion": "1", "data": {"status": "denied", "InvocationId": "invocation-one"}}
            args[9]["messages"][0]["content"] = json.dumps(result)
            if provider == "copilot": args[0][-1]["data"]["results"] = [result]
            else:
                values = [{"kind": "functionResult", "callId": "command-one", "result": json.dumps(result)}]
                args[0][-1]["data"]["results"] = values
                args[0][-2]["data"]["messages"][-1]["contents"] = values
            with self.subTest(provider=provider):
                self.assertEqual("denied", require_called_refusal(*args, **kwargs, human_denial=denied)["payload"]["data"]["status"])
                with self.assertRaises(RuntimeError): require_called_refusal(*args, **kwargs)
                with self.assertRaises(RuntimeError): require_called_refusal(*args, **kwargs, human_denial=dict(denied, status="approved"))

    def test_refusal_needs_unique_complete_native_frames_and_keeps_error_during_normalization(self):
        args, _ = refusal_fixture("copilot")
        raw = "".join("data: " + json.dumps(frame) + "\n\n" for frame in args[7])
        self.assertEqual(args[7], frames_from_raw(raw))
        for bad in ("", raw[:-1], 'data: {"id":"one","kind":"completed","kind":"error"}\n\n'):
            with self.subTest(raw=bad), self.assertRaises((RuntimeError, ValueError)): frames_from_raw(bad)
        from case_records import _normalized_result
        value = {"toolCallId": "call", "error": {"code": "module.code.unsupported", "ownerUnavailable": True}}
        self.assertEqual(value, _normalized_result(value))
        with self.assertRaises(RuntimeError): require_normal_terminal(args[7] + [args[7][-1]])


class EffectsShutdownTests(unittest.TestCase):
    def setUp(self):
        safe_group = patch("effects_journeys.os.getpgrp", return_value=42)
        safe_group.start(); self.addCleanup(safe_group.stop)

    def test_detached_browser_is_stopped_and_absence_proven_before_restoration(self):
        with tempfile.TemporaryDirectory() as directory:
            lease = Path(directory) / "lease.json"
            lease.write_text(json.dumps({"token": "owned", "nodePid": 900, "browserPid": 901}))
            browser = {"parent": 900, "group": 901, "session": 901, "started": "original-browser"}
            renderer = {"parent": 901, "group": 901, "session": 901, "started": "renderer"}
            # Node already exited and the detached browser was reparented.
            processes = {901: dict(browser, parent=1), 902: renderer}
            sent = []
            def stop(group, sig):
                sent.append((group, sig))
                for pid in list(processes):
                    if processes[pid]["group"] == group: processes.pop(pid)
            process = SimpleNamespace(pid=900, poll=lambda: 0)
            result = _stop_browser_groups(process, lease, "owned", {901: browser, 902: renderer}, snapshot=lambda: copy.deepcopy(processes), send=stop)
            self.assertEqual([(901, signal.SIGTERM)], sent)
            self.assertTrue(result["provenAbsent"])
            require_browser_stopped(SimpleNamespace(effects_browser_stopped=result["provenAbsent"]))

    def test_surviving_descendant_blocks_restoration_after_term_and_kill(self):
        with tempfile.TemporaryDirectory() as directory:
            lease = Path(directory) / "lease.json"
            lease.write_text(json.dumps({"token": "owned", "nodePid": 900, "browserPid": 901}))
            browser = {"parent": 900, "group": 901, "session": 901, "started": "original-browser"}
            sent = []
            with self.assertRaises(RuntimeError):
                _stop_browser_groups(SimpleNamespace(pid=900, poll=lambda: 0), lease, "owned", {901: browser},
                    snapshot=lambda: {901: browser}, send=lambda group, sig: sent.append((group, sig)),
                    now=iter((0, 6, 7, 13)).__next__, sleep=lambda _: None)
            self.assertEqual([(901, signal.SIGTERM), (901, signal.SIGKILL)], sent)
            with self.assertRaises(RuntimeError): require_browser_stopped(SimpleNamespace(effects_browser_stopped=False))
            with self.assertRaises(RuntimeError): require_browser_stopped(SimpleNamespace(effects_browser_stopped=True, effects_management_settled=False))

    def test_missing_lease_stops_node_but_cannot_authorize_restoration(self):
        with tempfile.TemporaryDirectory() as directory:
            node = {"parent": 1, "group": 900, "session": 900, "started": "owned-node"}
            processes = {900: node}
            sent = []
            def stop(group, sig): sent.append((group, sig)); processes.clear()
            with self.assertRaises(RuntimeError):
                _stop_browser_groups(SimpleNamespace(pid=900, poll=lambda: 0), Path(directory) / "missing.json", "owned", {900: node},
                                     snapshot=lambda: copy.deepcopy(processes), send=stop)
            self.assertEqual([(900, signal.SIGTERM)], sent)

    def test_reused_browser_pid_is_never_signalled_as_our_browser(self):
        with tempfile.TemporaryDirectory() as directory:
            lease = Path(directory) / "lease.json"
            lease.write_text(json.dumps({"token": "owned", "nodePid": 900, "browserPid": 901}))
            original = {"parent": 900, "group": 901, "session": 901, "started": "original-browser"}
            sent = []
            with self.assertRaises(RuntimeError):
                _stop_browser_groups(SimpleNamespace(pid=900, poll=lambda: 0), lease, "owned", {901: original},
                    snapshot=lambda: {901: dict(original, started="reused-pid")}, send=lambda group, sig: sent.append((group, sig)))
            self.assertEqual([], sent)

    def test_late_pid_reuse_after_term_blocks_kill_and_restoration(self):
        with tempfile.TemporaryDirectory() as directory:
            lease = Path(directory) / "lease.json"
            lease.write_text(json.dumps({"token": "owned", "nodePid": 900, "browserPid": 901}))
            original = {"parent": 900, "group": 901, "session": 901, "started": "birth-A"}
            replacement = dict(original, parent=1, started="birth-B")
            sent = []
            snapshots = iter(({901: original}, {901: original}, {901: original}, {901: original}, {901: replacement}))
            # A remains unchanged through the final TERM wait. The very next
            # pre-KILL census sees replacement B; no numeric-PGID KILL may follow.
            with self.assertRaises(RuntimeError):
                _stop_browser_groups(SimpleNamespace(pid=900, poll=lambda: 0), lease, "owned", {901: original},
                    snapshot=lambda: copy.deepcopy(next(snapshots)), send=lambda group, sig: sent.append((group, sig)),
                    now=iter((0, 6)).__next__, sleep=lambda _: None)
            self.assertEqual([(901, signal.SIGTERM)], sent)
            with self.assertRaises(RuntimeError): require_browser_stopped(SimpleNamespace(effects_browser_stopped=False))

    def test_lease_without_observed_ancestry_cannot_authorize_a_live_group(self):
        with tempfile.TemporaryDirectory() as directory:
            lease = Path(directory) / "lease.json"
            lease.write_text(json.dumps({"token": "owned", "nodePid": 900, "browserPid": 901}))
            member = {"parent": 1, "group": 901, "session": 901, "started": "unobserved-member"}
            sent = []
            with self.assertRaises(RuntimeError):
                _stop_browser_groups(SimpleNamespace(pid=900, poll=lambda: 0), lease, "owned", {},
                    snapshot=lambda: {902: member}, send=lambda group, sig: sent.append((group, sig)))
            self.assertEqual([], sent)

    def test_new_or_changed_session_members_block_all_further_signals(self):
        for mutation in ("new-member", "changed-session", "changed-group"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                lease = Path(directory) / "lease.json"
                lease.write_text(json.dumps({"token": "owned", "nodePid": 900, "browserPid": 901}))
                original = {"parent": 900, "group": 901, "session": 901, "started": "birth-A"}
                changed = {901: dict(original)}
                if mutation == "new-member": changed[902] = dict(original, parent=901, started="new-renderer")
                elif mutation == "changed-session": changed[901]["session"] = 999
                else: changed[901]["group"] = 999
                snapshots = iter(({901: original}, {901: original}, {901: original}, changed))
                sent = []
                with self.assertRaises(RuntimeError):
                    _stop_browser_groups(SimpleNamespace(pid=900, poll=lambda: 0), lease, "owned", {901: original},
                        snapshot=lambda: copy.deepcopy(next(snapshots)), send=lambda group, sig: sent.append((group, sig)))
                self.assertEqual([(901, signal.SIGTERM)], sent)


def nested_observe_fixture(provider):
    native, trace = {}, []
    own = [{"moduleId": MODULE, "origin": "authoritativeRead", "contentType": "acme.home.widget"},
           {"moduleId": MODULE, "origin": "installedGuidance", "summary": "Acme owns personal widget semantics."}]
    unrelated = [{"moduleId": "Elsa.Workflows", "origin": "authoritativeRead", "contentType": "workflows.list"},
                 {"moduleId": "Elsa.Workflows", "origin": "installedGuidance", "summary": "Workflows owns workflow list semantics."}]
    for index, name in enumerate(("nestedBefore", "unrelatedBefore", "nestedAfter", "unrelatedAfter")):
        context = own if name == "nestedBefore" else [] if name == "nestedAfter" else unrelated
        tools = [{"name": "acme_home_read_widget"}] if name == "nestedBefore" else [] if name == "nestedAfter" else [{"name": "workflow_read_graph"}]
        command, session = "observe-" + str(index), "native-session-" + str(index)
        native[name] = {"commandId": command, "sessionId": session, "frames": [
            {"kind": "started", "payload": {"turnId": "turn-" + str(index)}},
            {"kind": "contextAdmitted", "payload": {"attachments": copy.deepcopy(context)}}, {"kind": "completed"}]}
        data = {"commandId": command, "tools": copy.deepcopy(tools)}
        observation = {"commandId": command, "toolCount": len(tools)}
        if provider == "copilot":
            data.update(sessionId=session, prompt=json.dumps(context), systemMessage="Installed host prompt.")
            observation["sessionId"] = session
        else: data["messages"] = [{"role": "system", "contents": [{"kind": "text", "text": json.dumps(context)}]}]
        trace.extend([{"kind": provider + ".request", "data": data}, {"kind": provider + ".observation", "data": observation}])
    return native, trace


class NestedRemovalEvidenceTests(unittest.TestCase):
    def test_actual_observe_pairs_keep_unrelated_context_guidance_and_tools(self):
        for provider in ("copilot", "anthropic"):
            self.assertTrue(require_nested_observations(*nested_observe_fixture(provider), provider))

    def test_stale_context_guidance_tools_missing_support_and_sdk_failures_are_rejected(self):
        for provider in ("copilot", "anthropic"):
            mutations = [
                lambda n, t: n["nestedAfter"]["frames"][1]["payload"]["attachments"].append({"moduleId": MODULE}),
                lambda n, t: t[4]["data"]["tools"].append({"name": "acme_home_read_widget"}),
                lambda n, t: t[4]["data"].update(prompt="Acme owns personal widget semantics."),
                lambda n, t: n["unrelatedAfter"]["frames"][1]["payload"].update(attachments=[]),
                lambda n, t: t[6]["data"].update(tools=[]),
                lambda n, t: n["nestedAfter"]["frames"][-1].update(kind="error"),
                lambda n, t: n["nestedAfter"].update(sessionId=n["nestedBefore"]["sessionId"]),
                lambda n, t: t.append({"kind": provider + ".call", "data": {"commandId": n["nestedAfter"]["commandId"]}}),
            ]
            if provider == "copilot": mutations.append(lambda n, t: t[5]["data"].update(sessionId="foreign"))
            for index, mutate in enumerate(mutations):
                with self.subTest(provider=provider, mutation=index):
                    native, trace = nested_observe_fixture(provider); mutate(native, trace)
                    with self.assertRaises(RuntimeError): require_nested_observations(native, trace, provider)


class RawAuditCanaryTests(unittest.TestCase):
    def test_actual_retained_http_body_is_decoded_before_private_canary_search(self):
        canary = "ab" * 24; digest = hashlib.sha256(canary.encode()).hexdigest()
        response = {"origin": "http://127.0.0.1:53001", "method": "GET", "path": "/_elsa/weaver/audit?take=100&sessionId=native",
                    "status": 200, "rawBody": '{"data":[{"message":"' + "\\u0061\\u0062" * 24 + '"}]}'}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "audit.json"; path.write_text(json.dumps(response))
            with self.assertRaises(RuntimeError): require_canary_absent(json.loads(path.read_text()), digest)
            response["rawBody"] = '{"data":[{"message":"Safe engine diagnostic."}]}'
            require_canary_absent(response, digest)


class PolicyContentRootTests(unittest.TestCase):
    def test_unconfigured_and_configured_policy_share_owned_content_and_leave_binary_publish_unchanged(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory).resolve(); published, content = root / "binary", root / "content"
            published.mkdir(); content.mkdir()
            original = {"Elsa": {"Weaver": {"Privileges": dict.fromkeys(("Administrative", "Irrecoverable", "Activation"), True), "Retain": "unchanged"}}}
            for folder in (published, content): (folder / "appsettings.json").write_text(json.dumps(original))
            before = (published / "appsettings.json").read_bytes()
            run = SimpleNamespace(root=root, profiles={"backend": {"published": published, "contentRoot": content}})
            def bootstrap():
                policy = json.loads((content / "appsettings.json").read_text())["Elsa"]["Weaver"].get("Privileges", {})
                states = {name.lower(): policy.get(name, False) for name in ("Administrative", "Irrecoverable", "Activation")}
                return SimpleNamespace(require_status=lambda _: SimpleNamespace(json=lambda: {"data": {"privileges": states}}, receipt={"observed": states}))
            runtime = SimpleNamespace(run=run, transport=lambda _: SimpleNamespace(bootstrap=bootstrap))
            with patch("effects_journeys.time.monotonic", side_effect=[0, 16]):
                self.assertEqual(dict.fromkeys(("administrative", "irrecoverable", "activation"), False), _policy(runtime, [False] * 3, unconfigured=True)["observed"])
            self.assertNotIn("Privileges", json.loads((content / "appsettings.json").read_text())["Elsa"]["Weaver"])
            _policy(runtime, [True, False, True])
            self.assertEqual({"Administrative": True, "Irrecoverable": False, "Activation": True},
                             json.loads((content / "appsettings.json").read_text())["Elsa"]["Weaver"]["Privileges"])
            self.assertEqual(before, (published / "appsettings.json").read_bytes())


class CanaryDisclosureIntegrityTests(unittest.TestCase):
    def test_retained_raw_and_decoded_canaries_fail_even_when_a_summary_claims_absence(self):
        canary = "ab" * 24
        digest = hashlib.sha256(canary.encode()).hexdigest()
        require_canary_absent({"found": 0, "raw": "clean", "decoded": {"data": []}}, digest)
        for value in ({"found": 0, "audit": {"message": canary}}, "outside.declared." + canary, "prefix" + canary + "suffix"):
            with self.subTest(value=type(value).__name__), self.assertRaises(RuntimeError): require_canary_absent(value, digest)

    def test_actual_browser_gate_rejects_audit_only_leaks_and_absent_unreadable_or_partial_traffic(self):
        # Execute the production driver's pure gate with synthetic wire inputs,
        # rather than assert its source spelling. No browser or application runs.
        browser = Path(__file__).with_name("browser.mjs").read_text()
        function = browser[browser.index("function requireEffectsDisclosureCapture("):browser.index("// Spec1203's independently packaged author.")]
        script = r'''
const assert = require("node:assert/strict"), crypto = require("node:crypto");
const need = (condition, message) => { if (!condition) throw new Error(message); };
const digest = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
''' + function + r'''
const origin="http://127.0.0.1:18001", sessionPath="/_elsa/weaver/sessions/current", canary="ab".repeat(24);
const frame={id:"completed-one",kind:"completed"};
const rawBody="data: "+JSON.stringify(frame)+"\n\n";
const fresh=()=>[{url:origin+"/_elsa/weaver/audit?take=100&sessionId=current",status:200,rawBody:'{"data":[]}',value:{data:[]}},
  [{method:"POST",url:origin+sessionPath+"/messages",body:"{}"},{url:origin+sessionPath+"/messages",status:200,body:"{}"}],
  [{url:origin+sessionPath+"/stream",status:200,contentType:"text/event-stream",captureError:null,captureValidationError:null,
    captureMethod:"streamResourceContent",trailingIncompleteUtf8Bytes:0,textTruncation:"none",rawBody}],"Clean visible card",canary,origin,sessionPath];
assert.equal(requireEffectsDisclosureCapture(...fresh()).canarySha256,digest(Buffer.from(canary)));
const mutations=[
 x=>{x[0].value.data=[{message:canary}];x[0].rawBody=JSON.stringify(x[0].value);},
 x=>{x[0].value.data=[{message:canary}];x[0].rawBody='{"data":[{"message":"'+"\\u0061\\u0062".repeat(24)+'"}]}';},
 x=>{x[0].rawBody=JSON.stringify({data:[{message:canary}]});},
 x=>{x[0].value.data=[{message:canary}];},
 x=>{x[0].url=origin+"/_elsa/weaver/audit?take=100&sessionId=other";},
 x=>{x[1]=[];}, x=>{x[1][1]={url:origin+sessionPath+"/messages",unreadable:"response read failed"};},
 x=>{x[1][0].url=origin+"/_elsa/weaver/sessions/other/messages";},
 x=>{x[2]=[];}, x=>{x[2][0].captureError="body missing";}, x=>{x[2][0].captureValidationError="lossy";},
 x=>{x[2][0].rawBody="";}, x=>{x[2][0].rawBody=rawBody.slice(0,-1);},
 x=>{x[2][0].trailingIncompleteUtf8Bytes=1;}, x=>{x[2][0].textTruncation="trailing-incomplete-utf8";},
 x=>{x[2][0].url=origin+"/_elsa/weaver/sessions/other/stream";},
 x=>{x[2][0].rawBody="data: "+JSON.stringify({id:"started",kind:"started"})+"\n\n";},
 x=>{x[2][0].rawBody="data: "+JSON.stringify({id:"completed",kind:"completed",message:canary})+"\n\n";},
 x=>{x[3]=canary;}
];
for(const mutate of mutations){const values=fresh();mutate(values);assert.throws(()=>requireEffectsDisclosureCapture(...values));}
'''
        node = shutil.which("node")
        self.assertIsNotNone(node, "Node is required to exercise the actual browser disclosure gate")
        completed = subprocess.run([node, "-e", script], capture_output=True, text=True, timeout=30)
        self.assertEqual(0, completed.returncode, completed.stderr)


if __name__ == "__main__": unittest.main()
