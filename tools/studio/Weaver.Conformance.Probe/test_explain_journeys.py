"""Integrity checks for the workflow-explain driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from artifacts import sha
from batch_journeys import CREDENTIAL
from explain_journeys import (BROWSER_JOURNEY, BROWSER_OBSERVATIONS, CONTROL, CONTROL_ERROR, EXPECTED_CASES, INTERPRETATION, JAVASCRIPT,
                              LITERAL_NODE, READ_FRAMES, READ_TOOL, SECRET_NODE, SECRET_REFERENCE, TURNS, admitted_selection, designer_diagnostics,
                              expected_addresses, explain_manifest_gaps, explain_snapshot, property_context, require_disclosure_refused,
                              require_explain_browser, require_explain_turn, require_no_canary_in_evidence, require_workflow_explain,
                              synthetic_browser, synthetic_native, synthetic_search, working_state)
from owner_resources import EXPLAIN_MISSING_VARIABLE, UNRESOLVED_VARIABLE

# The shape owner_resources.seed_batch_workflow(unresolved_variable=True) returns.
SEED = {"definitionId": "wf-1", "draftId": "draft-1",
        "nodes": {"root": "batch-flow", "a": "batch-a", "b": "batch-b", "nested": "batch-nested", "inner": "batch-inner"},
        "writeLine": {"activityTypeKey": "Elsa.Activities.Primitives.Activities.WriteLine", "version": "1.0.0", "activityVersionId": "write-v1", "displayName": "Write Line"},
        "flowchart": {"activityTypeKey": "Elsa.Activities.Flowchart.Activities.Flowchart", "version": "1.0.0", "activityVersionId": "flow-v1",
                      "displayName": "Flowchart", "slot": "elsa.flowchart.structure:activities", "mode": "flowchart"},
        "validations": [{"code": UNRESOLVED_VARIABLE, "message": "Variable expression on 'batch-b/inputs/text' references variable '"
                         + EXPLAIN_MISSING_VARIABLE + "' that is not visible from this activity's scope.", "nodeId": "batch-b", "member": "inputs/text",
                         "severity": "Error"},
                        {"code": "Workflow/Note", "message": "A workflow-scope note.", "nodeId": None, "member": None, "severity": "Warning"}]}
# The owner catalog's declared inputs, as the designer's catalog read lists them.
CATALOG = {"write-v1": [{"referenceKey": "text", "name": "Text", "type": "String", "isSensitive": False, "isCredential": False}],
           "flow-v1": [],
           CREDENTIAL["activityVersionId"]: [{"referenceKey": CREDENTIAL["input"], "name": "ApiKey", "type": "String", "isSensitive": True, "isCredential": True}]}
CANARY = "CANARY-CREDENTIAL-" + "5" * 32
COMMAND = "command-1"


def attachment_for(turn="explain", canary=CANARY):
    node = TURNS[turn]
    diagnostics, omitted = designer_diagnostics(SEED["validations"])
    return explain_snapshot(SEED, "qualification-unsaved-1", working_state(SEED, canary), CATALOG, diagnostics, omitted, SEED["nodes"].get(node, node))


def server_admission(attachment):
    """The server's admitted projection of the parts the driver compares, as WorkflowAgentSnapshot.TryRead writes them."""
    content = copy.deepcopy(attachment["content"])
    for key in ("selectedActivityType", "inspectedNodeId"):
        content.pop(key, None)
    if "selectedActivity" in content:
        content["selectedActivity"] = admitted_selection(content["selectedActivity"])
    content["connections"] = [{**edge, "targetPort": ""} for edge in content["connections"]]
    return {"contentType": "workflow.definition", "origin": "clientSnapshot", "target": {**attachment["target"], "displayName": None, "route": None},
            "content": content}


def frames(provider, attachment, *, admitted=None, data=None, extra=None):
    item = admitted or server_admission(attachment)
    result = {"typeId": "workflows.graph", "schemaVersion": "1",
              "data": data if data is not None else {**item["content"], "addresses": expected_addresses(SEED), "addressesTruncated": False}}
    def frame(kind):
        if kind == "contextAdmitted": return {"kind": kind, "payload": {"diagnostics": [], "attachments": [copy.deepcopy(item), {"contentType": "workflow.catalog"}]}}
        if kind == "toolCallCompleted": return {"kind": kind, "payload": {"toolName": READ_TOOL, "succeeded": True, "summary": "Read.",
                                                                          "result": {"toolName": READ_TOOL, "succeeded": True, "payload": result}}}
        if kind == "messageDelta": return {"kind": kind, "content": extra or "Tool outcome received."}
        return {"kind": kind}
    return [frame(kind) for kind in READ_FRAMES[provider]]


def accepted(attachment, admitted=None):
    return {"data": {"message": {"context": [admitted or server_admission(attachment), {"contentType": "workflow.guidance"}]}}}


def rows(provider="copilot", calls=1, command=COMMAND, tools=(READ_TOOL,)):
    value = [{"kind": provider + ".request", "data": {"commandId": command, "tools": [{"name": name} for name in tools]}}]
    value += [{"kind": provider + ".call", "data": {"commandId": command, "toolName": READ_TOOL}} for _ in range(calls)]
    return value


class Response:
    def __init__(self, status, value):
        self.status, self.body = status, json.dumps(value).encode()

    def json(self):
        return json.loads(self.body)


class ExplainSnapshotTests(unittest.TestCase):
    def test_the_working_state_is_disclosed_as_studios_bridge_builds_it_and_the_canary_never(self):
        for turn in TURNS:
            content = attachment_for(turn)["content"]
            self.assertNotIn(CANARY, json.dumps(content))
            self.assertEqual([{"name": SECRET_REFERENCE["name"], "typeName": SECRET_REFERENCE["typeName"], "scope": SECRET_REFERENCE["scope"]}],
                             content["secretReferences"])
        explain = attachment_for("explain")["content"]["selectedActivity"]
        self.assertEqual({"kind": "withheld", "expressionType": "Literal"}, explain["properties"][0]["value"])
        self.assertTrue(explain["properties"][0]["isCredential"] and explain["properties"][0]["isSensitive"])
        expression = attachment_for("expression")["content"]["selectedActivity"]["properties"][0]["value"]
        self.assertEqual({"kind": "expression", "expressionType": "JavaScript", "content": JAVASCRIPT, "truncated": False, "length": len(JAVASCRIPT)}, expression)
        variable = attachment_for("validation")["content"]["selectedActivity"]["properties"][0]["value"]
        self.assertEqual("Variable", variable["expressionType"])
        self.assertEqual({"referenceKey": EXPLAIN_MISSING_VARIABLE, "declaringScopeId": "workflow"}, json.loads(variable["content"]))
        secret = attachment_for("secret-reference")["content"]["selectedActivity"]["properties"][0]["value"]
        self.assertEqual({"kind": "secretReference", "secretReference": SECRET_REFERENCE}, secret)

    def test_the_designers_diagnostics_are_the_owners_validations_by_activity_and_input(self):
        diagnostics, omitted = designer_diagnostics(SEED["validations"])
        self.assertEqual([{"severity": "error", "type": UNRESOLVED_VARIABLE, "nodeId": "batch-b", "property": "text", "message": SEED["validations"][0]["message"]},
                          {"severity": "warning", "type": "Workflow/Note", "message": "A workflow-scope note."}], diagnostics)
        self.assertEqual(0, omitted)
        many, omitted = designer_diagnostics([{"code": "X", "message": "m"}] * 70)
        self.assertEqual((64, 6), (len(many), omitted))

    def test_property_content_is_bounded_and_its_truncation_marked(self):
        state = {"version": "write-v1", "inputs": {"text": {"expression": {"type": "JavaScript", "value": "x" * 3000}}}}
        value = property_context("n", state, CATALOG["write-v1"])["properties"][0]["value"]
        self.assertEqual((2048, True, 3000), (len(value["content"]), value["truncated"], value["length"]))

    def test_the_server_stamps_interpretation_only_on_non_literal_expressions(self):
        selected = admitted_selection(attachment_for("expression")["content"]["selectedActivity"])
        self.assertEqual(INTERPRETATION, selected["properties"][0]["value"]["interpretation"])
        literal = admitted_selection(property_context("a", working_state(SEED, CANARY)["batch-a"], CATALOG["write-v1"]))
        self.assertNotIn("interpretation", literal["properties"][0]["value"])

    def test_addresses_follow_scope_paths_through_the_nested_container(self):
        addresses = {item["id"]: (item["scopePath"], item["slot"]) for item in expected_addresses(SEED)}
        self.assertEqual(([], None), addresses["batch-flow"])
        self.assertEqual((["batch-flow"], SEED["flowchart"]["slot"]), addresses[LITERAL_NODE])
        self.assertEqual((["batch-flow", "batch-nested"], SEED["flowchart"]["slot"]), addresses["batch-inner"])


class ExplainTurnTests(unittest.TestCase):
    def check(self, provider="copilot", turn="validation", **overrides):
        attachment = attachment_for(turn)
        values = overrides.pop("frames", None) or frames(provider, attachment)
        return require_explain_turn(values, overrides.pop("rows", None) or rows(provider), overrides.pop("accepted", None) or accepted(attachment),
                                    provider=provider, command_id=COMMAND, attachment=attachment, addresses=expected_addresses(SEED), canary=CANARY)

    def test_each_adapters_pinned_order_and_the_working_state_are_accepted(self):
        for provider in READ_FRAMES:
            for turn in TURNS:
                record = self.check(provider, turn)
                self.assertEqual("qualification-unsaved-1", record["revision"])
                self.assertEqual(2, record["diagnostics"])
        record = self.check(turn="expression")
        self.assertEqual([{"referenceKey": "text", "kind": "expression", "expressionType": "JavaScript", "interpretation": INTERPRETATION}], record["properties"])

    def test_another_frame_order_is_refused(self):
        attachment = attachment_for()
        with self.assertRaises(RuntimeError): self.check(frames=frames("anthropic", attachment))
        with self.assertRaises(RuntimeError): self.check(frames=frames("copilot", attachment)[:-1])

    def test_an_admission_that_differs_from_the_working_state_is_refused(self):
        attachment = attachment_for("validation")
        mutations = {
            "diagnostics": lambda item: item["content"]["diagnostics"].pop(),
            "interpretation": lambda item: item["content"]["selectedActivity"]["properties"][0]["value"].pop("interpretation"),
            "claimed": lambda item: item["content"]["selectedActivity"]["properties"][0]["value"].update(interpretation="available"),
            "structure": lambda item: item["content"]["structure"]["containers"].pop(),
            "graph": lambda item: item["content"].update(graphActivities=[{"activityDefinitionId": "x"}]),
            "revision": lambda item: item["target"].update(revision="persisted"),
            "secret-value": lambda item: item["content"]["secretReferences"][0].update(value="raw"),
        }
        for name, mutate in mutations.items():
            with self.subTest(name):
                item = server_admission(attachment)
                mutate(item)
                with self.assertRaises(RuntimeError): self.check(accepted=accepted(attachment, item))
                with self.assertRaises(RuntimeError): self.check(frames=frames("copilot", attachment, admitted=item))

    def test_a_read_result_without_scope_path_addresses_is_refused(self):
        attachment = attachment_for()
        content = server_admission(attachment)["content"]
        with self.assertRaises(RuntimeError): self.check(frames=frames("copilot", attachment, data=dict(content)))
        cut = {**content, "addresses": expected_addresses(SEED)[:-1], "addressesTruncated": True}
        with self.assertRaises(RuntimeError): self.check(frames=frames("copilot", attachment, data=cut))

    def test_the_canary_anywhere_in_frames_trace_or_acceptance_is_refused(self):
        attachment = attachment_for()
        with self.assertRaises(RuntimeError): self.check(frames=frames("copilot", attachment, extra="The key is " + CANARY))
        with self.assertRaises(RuntimeError): self.check(rows=rows() + [{"kind": "copilot.response", "data": {"commandId": COMMAND, "text": CANARY}}])
        leaked = server_admission(attachment)
        leaked["content"]["note"] = CANARY
        with self.assertRaises(RuntimeError): self.check(accepted=accepted(attachment, leaked))

    def test_the_adapter_must_be_offered_and_call_the_read_once(self):
        with self.assertRaises(RuntimeError): self.check(rows=rows(calls=0))
        with self.assertRaises(RuntimeError): self.check(rows=rows(calls=2))
        with self.assertRaises(RuntimeError): self.check(rows=rows(tools=()))


class DisclosureControlTests(unittest.TestCase):
    MARKER = "DISCLOSED-CREDENTIAL-CONTROL-" + "9" * 32

    def test_the_modules_refusal_at_admission_is_required(self):
        error = {"code": CONTROL_ERROR["code"], "message": CONTROL_ERROR["message"]}
        self.assertEqual(CONTROL, require_disclosure_refused(Response(400, {"error": error}), self.MARKER)["control"])
        for status, value in ((200, {"data": {}}), (400, {"error": {**error, "message": "Another reason."}}),
                              (400, {"error": {**error, "message": error["message"] + " " + self.MARKER}})):
            with self.assertRaises(RuntimeError): require_disclosure_refused(Response(status, value), self.MARKER)


class ExplainBrowserTests(unittest.TestCase):
    def observed(self, **changes):
        return {"journey": BROWSER_JOURNEY, "status": "passed", "provider": "copilot", "observations": copy.deepcopy(BROWSER_OBSERVATIONS),
                "ownerBefore": {"path": "/x", "sha256": "0"}, "ownerAfter": {"path": "/y", "sha256": "0"}, "evidence": [{"path": "/z"}],
                "commands": {"before": "c-1", "after": "c-2"}, **changes}

    def commands(self):
        return [{"commandId": "c-1", "toolName": READ_TOOL, "arguments": {}}, {"commandId": "c-2", "toolName": READ_TOOL, "arguments": {}}]

    def rows(self):
        return rows(command="c-1") + rows(command="c-2")

    def test_the_editors_journey_is_checked_against_the_adapter_trace(self):
        self.assertEqual(BROWSER_OBSERVATIONS, require_explain_browser(self.observed(), self.commands(), self.rows(), provider="copilot")["observations"])
        for observed, commands, trace in (
                (self.observed(observations={**BROWSER_OBSERVATIONS, "draftUnchanged": False}), self.commands(), self.rows()),
                (self.observed(commands={"before": "c-1", "after": "c-1"}), self.commands(), self.rows()),
                (self.observed(), [{**self.commands()[0], "arguments": {"x": 1}}, self.commands()[1]], self.rows()),
                (self.observed(), self.commands(), rows(command="c-1")),
                (self.observed(status="failed"), self.commands(), self.rows())):
            with self.assertRaises(RuntimeError): require_explain_browser(observed, commands, trace, provider="copilot")


class CanarySearchTests(unittest.TestCase):
    def test_every_retained_file_is_searched_and_a_planted_canary_is_found(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            clean = root / "clean.json"
            clean.write_text(json.dumps({"value": "nothing"}))
            refs = [{"path": str(clean), "sha256": sha(clean)}]
            self.assertEqual(1, require_no_canary_in_evidence(refs, CANARY, root))
            leaked = root / "leaked.json"
            leaked.write_text(json.dumps({"value": CANARY}))
            with self.assertRaises(RuntimeError): require_no_canary_in_evidence(refs + [{"path": str(leaked), "sha256": sha(leaked)}], CANARY, root)
            with self.assertRaises(RuntimeError): require_no_canary_in_evidence([{"path": str(clean), "sha256": "0" * 64}], CANARY, root)
            with self.assertRaises(RuntimeError): require_no_canary_in_evidence([], CANARY, root)


class ExplainGateTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.root = Path(self.directory.name)

    def tearDown(self):
        self.directory.cleanup()

    def write(self, value):
        path = self.root / (str(len(list(self.root.iterdir()))) + ".json")
        path.write_text(json.dumps(value))
        return {"path": str(path), "sha256": sha(path)}

    def section(self, native=None, browser=None, search=None):
        names = sorted(EXPECTED_CASES)
        return {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False, "source": "a" * 40, "caseIDs": names,
                "records": [{"id": name, "status": "passed", "record": self.write({"id": name, "status": "passed", "adapter": name.split("-", 1)[0],
                    "native": native or synthetic_native(), "browser": browser or synthetic_browser(), "canarySearch": search or synthetic_search()})}
                            for name in names]}

    def test_both_adapters_with_every_turn_control_journey_and_an_empty_canary_search_pass(self):
        section = self.section()
        self.assertEqual([], explain_manifest_gaps(section, "a" * 40))
        self.assertEqual(2, len(require_workflow_explain(section, self.root, source="a" * 40)))

    def test_missing_failed_or_changed_records_never_pass(self):
        self.assertTrue(explain_manifest_gaps(None))
        for change in ({"status": "failed"}, {"liveProvider": True}, {"source": "b" * 40}):
            section = {**self.section(), **change}
            self.assertTrue(explain_manifest_gaps(section, "a" * 40))
        native = synthetic_native()
        native.pop("expression")
        for section in (self.section(native=native), self.section(search={"files": 2, "found": 1}), self.section(search={"files": 0, "found": 0}),
                        self.section(browser={"observations": {**BROWSER_OBSERVATIONS, "owningWrites": 1}}),
                        self.section(native={**synthetic_native(), CONTROL: {"code": "other"}})):
            with self.assertRaises(RuntimeError): require_workflow_explain(section, self.root, source="a" * 40)


if __name__ == "__main__":
    unittest.main()
