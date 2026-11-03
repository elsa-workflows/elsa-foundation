"""Integrity checks for the workflow-batch driver; synthetic records are not native evidence."""
import copy
import unittest

from batch_journeys import (BATCH_TOOL, BROWSER_COMMANDS, BROWSER_JOURNEY, BROWSER_OBSERVATIONS, CONTROLS, CREDENTIAL, EXPECTED_REFUSALS,
                            NOTHING_CHANGED, POSITIVE_RISKS, batch_manifest_gaps, batch_snapshot, control_operations, positive_operations,
                            require_batch_browser, require_proposal_turn, require_refused_turn, require_unoffered_turn, require_workflow_batch,
                            synthetic_browser, synthetic_native)

SEED = {"definitionId": "wf-1", "draftId": "draft-1",
        "nodes": {"root": "batch-flow", "a": "batch-a", "b": "batch-b", "seq": "batch-seq", "inner": "batch-inner"},
        "writeLine": {"activityTypeKey": "Elsa.Activities.Primitives.Activities.WriteLine", "version": "1.0.0", "activityVersionId": "write-v1", "displayName": "Write Line"},
        "containers": {"Flowchart": {"activityTypeKey": "Elsa.Activities.Flowchart.Activities.Flowchart", "activityVersionId": "flow-v1", "displayName": "Flowchart",
                                     "slot": "elsa.flowchart.structure:activities", "mode": "flowchart"},
                       "Sequence": {"activityTypeKey": "Elsa.Activities.Sequence.Activities.Sequence", "activityVersionId": "seq-v1", "displayName": "Sequence",
                                    "slot": "elsa.sequence.structure:activities", "mode": "sequence"}}}
COMMAND = "command-1"


def target(revision="rev-1"):
    return {"moduleId": "Elsa.Studio.Workflows", "resourceType": "workflow-definition", "resourceId": "wf-1", "revision": revision}


def admitted(manage="allowed", tools=("workflow_read_graph", "workflow_set_designer_position", BATCH_TOOL)):
    return {"kind": "contextAdmitted", "payload": {"diagnostics": [], "attachments": [
        {"contentType": "workflow.definition", "target": {**target(), "displayName": "Ignored", "route": "/ignored"}, "content": {"structure": {"versions": [], "containers": []}}},
        {"contentType": "workflow.permissions", "content": {"manage": manage, "tools": list(tools)}}]}}


def proposal():
    operations = positive_operations(SEED)
    return {"kind": "proposalCreated", "payload": {"id": "proposal-1", "status": "awaiting-approval", "requiresApproval": True, "preparedInvocation": {
        "toolName": BATCH_TOOL, "requiresApproval": True, "executionLocation": "clientWorkingState", "risk": "destructive", "target": target(),
        "arguments": {"operations": [{"kind": item["kind"], "risk": risk} for item, risk in zip(operations, POSITIVE_RISKS)]},
        "preview": {"typeId": "workflows.batch-result", "data": {"operations": [{"risk": risk} for risk in POSITIVE_RISKS]}}}}}


def rows(provider="copilot", offered=True, called=True, returned=True, command=COMMAND):
    tools = [{"name": "workflow_read_graph"}] + ([{"name": BATCH_TOOL}] if offered else [])
    value = [{"kind": provider + ".request", "data": {"commandId": command, "tools": tools}}]
    if called: value.append({"kind": provider + ".call", "data": {"commandId": command, "toolName": BATCH_TOOL}})
    if returned: value.append({"kind": provider + ".return", "data": {"commandId": command}})
    return value


def refusal_frames(control, content=None):
    start, detail = EXPECTED_REFUSALS[control]
    text = content if content is not None else start + " ... " + detail + " " + NOTHING_CHANGED
    return [{"kind": "started"}, admitted(), {"kind": "toolCallStarted", "payload": {"toolName": BATCH_TOOL}},
            {"kind": "toolCallCompleted", "payload": {"toolName": BATCH_TOOL, "succeeded": False, "content": text}},
            {"kind": "contributedResult"}, {"kind": "completed"}]


class SnapshotTests(unittest.TestCase):
    def test_the_snapshot_is_the_editors_compact_structured_form(self):
        value = batch_snapshot(SEED, "rev-1")
        content = value["content"]
        self.assertEqual("draft", content["workingState"])
        self.assertEqual(target(), value["target"])
        # Every activity has one version and every activity but the root one slot, by index.
        versions = sorted(index for group in content["structure"]["versions"] for index in group["nodes"])
        members = sorted(index for container in content["structure"]["containers"] for slot in container["slots"] for index in slot["nodes"])
        self.assertEqual(list(range(len(content["activities"]))), versions)
        self.assertEqual(list(range(1, len(content["activities"]))), members)
        self.assertNotIn("activityVersionId", str(content["activities"]))

    def test_the_credential_and_historical_controls_change_only_what_they_name(self):
        credential = batch_snapshot(SEED, "rev-1", credential_node=True)["content"]
        self.assertEqual("batch-credential", credential["activities"][-1]["id"])
        self.assertIn({"id": CREDENTIAL["activityVersionId"], "nodes": [5]}, credential["structure"]["versions"])
        self.assertEqual("historical", batch_snapshot(SEED, "rev-1", working_state="historical")["content"]["workingState"])

    def test_the_positive_batch_has_six_kinds_and_one_nested_add(self):
        operations = positive_operations(SEED)
        self.assertEqual(6, len({item["kind"] for item in operations}))
        self.assertEqual(["batch-flow", "batch-seq"], operations[0]["scopePath"])
        self.assertEqual(["reviewRequired"] * 4 + ["destructive", "reviewRequired"], POSITIVE_RISKS)
        for control in CONTROLS:
            with self.subTest(control=control):
                self.assertEqual(1, len(control_operations(SEED, control, "CANARY")))


class TurnTests(unittest.TestCase):
    def test_the_positive_turn_pauses_at_one_destructive_review_on_each_adapter(self):
        frames = [{"kind": "started"}, admitted(), {"kind": "toolCallStarted", "payload": {"toolName": BATCH_TOOL}}, proposal(), {"kind": "toolApprovalRequested"}]
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                value = require_proposal_turn(frames, rows(provider, returned=False), provider=provider, command_id=COMMAND, target=target(),
                                              operations=positive_operations(SEED))
                self.assertEqual("destructive", value["risk"])

    def test_a_positive_turn_that_was_not_reviewed_exactly_never_qualifies(self):
        def change(path, value):
            frames = [{"kind": "started"}, admitted(), proposal(), {"kind": "toolApprovalRequested"}]
            node = frames[2]["payload"]
            for key in path[:-1]: node = node[key]
            node[path[-1]] = value
            return frames
        cases = {
            "auto-applied": change(["requiresApproval"], False),
            "server-execution": change(["preparedInvocation", "executionLocation"], "server"),
            "ordinary-risk": change(["preparedInvocation", "risk"], "reviewRequired"),
            "other-target": change(["preparedInvocation", "target"], target("other-revision")),
            "relabelled": change(["preparedInvocation", "preview", "data", "operations"], [{"risk": "reviewRequired"}] * 6),
        }
        for name, frames in cases.items():
            with self.subTest(case=name), self.assertRaises(RuntimeError):
                require_proposal_turn(frames, rows(returned=False), provider="copilot", command_id=COMMAND, target=target(), operations=positive_operations(SEED))
        with self.assertRaises(RuntimeError):
            require_proposal_turn([{"kind": "started"}, admitted(), proposal(), {"kind": "toolApprovalRequested"}], rows(offered=False), provider="copilot",
                                  command_id=COMMAND, target=target(), operations=positive_operations(SEED))

    def test_each_control_is_refused_at_preparation_with_the_modules_own_text(self):
        for control in CONTROLS:
            for provider in ("copilot", "anthropic"):
                with self.subTest(control=control, provider=provider):
                    value = require_refused_turn(refusal_frames(control), rows(provider), provider=provider, command_id=COMMAND, target=target(), control=control)
                    self.assertEqual(control, value["control"])

    def test_a_control_refused_for_another_reason_or_reaching_review_never_qualifies(self):
        cases = {
            "wrong-reason": (refusal_frames("missing-activity", "The tool is not admitted for the current turn."), rows()),
            "other-control": (refusal_frames("missing-activity", EXPECTED_REFUSALS["ambiguous-target"][0] + " " + EXPECTED_REFUSALS["ambiguous-target"][1] + " " + NOTHING_CHANGED), rows()),
            "reviewed": (refusal_frames("missing-activity")[:-1] + [proposal(), {"kind": "completed"}], rows()),
            "no-return": (refusal_frames("missing-activity"), rows(returned=False)),
            "not-called": (refusal_frames("missing-activity"), rows(called=False)),
        }
        for name, (frames, trace) in cases.items():
            with self.subTest(case=name), self.assertRaises(RuntimeError):
                require_refused_turn(frames, trace, provider="copilot", command_id=COMMAND, target=target(), control="missing-activity")

    def test_a_credential_literal_never_reaches_disclosed_frames(self):
        frames = refusal_frames("credential-literal")
        require_refused_turn(frames, rows(), provider="copilot", command_id=COMMAND, target=target(), control="credential-literal", canary="CANARY-1")
        leaked = copy.deepcopy(frames)
        leaked[-2] = {"kind": "contributedResult", "payload": {"content": "CANARY-1"}}
        with self.assertRaises(RuntimeError):
            require_refused_turn(leaked, rows(), provider="copilot", command_id=COMMAND, target=target(), control="credential-literal", canary="CANARY-1")

    def test_a_reader_is_never_offered_the_batch_tool(self):
        frames = [{"kind": "started"}, admitted("denied", ("workflow_read_graph",)), {"kind": "error"}]
        self.assertFalse(require_unoffered_turn(frames, rows(offered=False, called=False, returned=False), provider="copilot",
                                                command_id=COMMAND, target=target())["offered"])
        for name, (value, trace) in {"permissions": ([{"kind": "started"}, admitted(), {"kind": "error"}], rows(offered=False, called=False, returned=False)),
                                     "offered": (frames, rows(called=False, returned=False)),
                                     "called": (frames, rows(offered=False, returned=False))}.items():
            with self.subTest(case=name), self.assertRaises(RuntimeError):
                require_unoffered_turn(value, trace, provider="copilot", command_id=COMMAND, target=target())


class BrowserTests(unittest.TestCase):
    def observed(self):
        return {"journey": BROWSER_JOURNEY, "status": "passed", "provider": "copilot", "pageErrors": [], "observations": copy.deepcopy(BROWSER_OBSERVATIONS),
                "ownerBefore": {"path": "a"}, "ownerAfter": {"path": "b"}, "evidence": [{"path": "c"}],
                "commands": {step: "command-" + step for step in BROWSER_COMMANDS}}

    def commands(self):
        return [{"commandId": "command-" + step, "toolName": BATCH_TOOL, "arguments": {"operations": []}} for step in BROWSER_COMMANDS]

    def trace(self, skip=None):
        return [row for step in BROWSER_COMMANDS if step != skip for row in rows(command="command-" + step)]

    def test_the_journey_holds_every_step_and_the_adapter_called_once_per_retained_command(self):
        value = require_batch_browser(self.observed(), self.commands(), self.trace(), provider="copilot")
        self.assertEqual(BROWSER_OBSERVATIONS, value["observations"])

    def test_a_changed_outcome_or_missing_call_never_qualifies(self):
        def changed(**observations):
            value = self.observed(); value["observations"].update(observations); return value
        cases = {
            "autosaved": (changed(autosaveSuspended=False), self.commands(), self.trace()),
            "undo-missed": (changed(undoRestored=False), self.commands(), self.trace()),
            "stale-applied": (changed(staleEdit="applied"), self.commands(), self.trace()),
            "missing-call": (self.observed(), self.commands(), self.trace(skip="remount")),
            "missing-command": (self.observed(), self.commands()[1:], self.trace()),
            "failed": ({**self.observed(), "status": "failed"}, self.commands(), self.trace()),
        }
        for name, (observed, commands, trace) in cases.items():
            with self.subTest(case=name), self.assertRaises(RuntimeError):
                require_batch_browser(observed, commands, trace, provider="copilot")


class SectionTests(unittest.TestCase):
    def section(self):
        names = ["copilot-workflow-batch", "anthropic-workflow-batch"]
        return {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False, "source": "s",
                "caseIDs": names, "records": [{"id": name, "status": "passed", "record": {"path": "x", "sha256": "y"}} for name in names]}

    def test_both_adapters_passed_exactly_once(self):
        self.assertEqual([], batch_manifest_gaps(self.section(), "s"))
        require_workflow_batch(self.section(), source="s")
        self.assertEqual({"positive", *CONTROLS, "no-manage"}, set(synthetic_native()))
        self.assertEqual(BROWSER_OBSERVATIONS, synthetic_browser()["observations"])

    def test_missing_failed_or_live_sections_never_qualify(self):
        for name, change in {"failed": lambda s: s.update(status="failed"), "live": lambda s: s.update(liveProvider=True),
                             "missing": lambda s: (s["caseIDs"].pop(), s["records"].pop()), "source": lambda s: s.update(source="other")}.items():
            section = self.section(); change(section)
            with self.subTest(case=name):
                self.assertTrue(batch_manifest_gaps(section, "s"))
        self.assertTrue(batch_manifest_gaps(None))


if __name__ == "__main__":
    unittest.main()
