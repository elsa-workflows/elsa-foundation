"""Integrity checks for the workflow-versions and workflow-executables driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from artifacts import sha
from batch_journeys import OUTCOME_ROWS, REFUSED_FRAMES
from explain_journeys import READ_FRAMES
from inspection_journeys import (member, normalize_port, require_values_withheld, ADMISSION_REFUSAL, COMPUTED_BY, EXECUTABLE_BROWSER, EXECUTABLE_KIND, EXECUTABLE_NATIVE, EXECUTABLE_OBSERVATIONS,
                                 EXECUTABLE_TOOL, EXPECTED_CASES, MUTATING_TOOLS, TEXTS, VERSION_BROWSER, VERSION_KIND, VERSION_NATIVE, VERSION_OBSERVATIONS,
                                 VERSION_TOOL, BATCH_TOOL, executable_attachment, expected_comparison, flatten_executable, flatten_version,
                                 inspection_manifest_gaps, other_hash, require_admission_refused, require_browser, require_executable_context,
                                 require_edits_unoffered, require_executable_read, require_tool_refused, require_version_context, require_version_read,
                                 require_workflow_inspection, synthetic_browser, synthetic_native, version_attachment)
from owner_resources import VERSION_NODES, version_states

COMMAND = "command-1"
WRITE = {"activityVersionId": "write-v1", "activityTypeKey": "Elsa.Activities.Primitives.Activities.WriteLine"}
FLOWCHART = {"activityVersionId": "flow-v1", "activityTypeKey": "Elsa.Activities.Flowchart.Activities.Flowchart",
             "authoringTemplate": {"structure": {"kind": "elsa.flowchart.structure"}},
             "containerStructure": {"mode": "flowchart", "slots": [{"property": "activities", "cardinality": "many"}],
                                    "initialPayload": {"activities": [], "connections": [], "startNodeId": None, "nodeMetadata": {}, "connectionMetadata": {}, "variables": []}}}
STATES = version_states(WRITE, FLOWCHART)
DEFINITION = {"id": "wf-1", "name": "Versions witness"}
VERSIONS = [{"id": "version-%d" % (index + 1), "version": "%d.0.0" % (index + 1), "definition": DEFINITION, "state": STATES[index]} for index in range(3)]
LISTED = [{"id": version["id"], "version": version["version"], "createdAt": "2026-11-03T00:00:00Z"} for version in VERSIONS]
N = VERSION_NODES


def version_section(version):
    """The server's version section as the slice F contract states it."""
    activities, connections = flatten_version(version["state"])
    return {"versionId": version["id"], "version": version["version"], "createdAt": "2026-11-03T00:00:00Z", "activityCount": len(activities),
            "activities": [{"nodeId": row["nodeId"], "activityVersionId": row["activityVersionId"], "scopePath": row["scopePath"],
                            "slot": "Activities" if row["scopePath"] else None, "inputs": row["inputs"]} for row in activities],
            "activitiesTruncated": False,
            "connections": [dict(zip(("container", "source", "sourcePort", "target", "targetPort"), item)) for item in connections], "connectionsTruncated": False}


def comparison(version, compared):
    expected = expected_comparison(version["state"], compared["state"])
    row = lambda node: {"nodeId": node, "activityVersionId": "write-v1", "scopePath": [N["root"]], "slot": "Activities"}
    connection = lambda item: dict(zip(("container", "source", "sourcePort", "target", "targetPort"), item))
    return {"computedBy": COMPUTED_BY, "note": "The tool's own structural comparison of two exact versions.",
            "activitiesAdded": [row(node) for node in expected["added"]], "activitiesRemoved": [row(node) for node in expected["removed"]],
            "activitiesChanged": [{"nodeId": node, "scopePath": [N["root"]], **value} for node, value in expected["changed"].items()],
            "connectionsAdded": [connection(item) for item in expected["connectionsAdded"]],
            "connectionsRemoved": [connection(item) for item in expected["connectionsRemoved"]], "truncated": False,
            "incomplete": False, "incompleteReasons": []}


def version_item(attachment, version):
    activities, _ = flatten_version(version["state"])
    return {"id": attachment["id"], "moduleId": "Elsa.Studio.Workflows", "contentType": VERSION_KIND, "origin": "authoritativeRead",
            "target": {**attachment["target"], "displayName": None, "route": None},
            "content": {"definitionId": DEFINITION["id"], "definitionName": DEFINITION["name"], "versionId": version["id"], "version": version["version"],
                        "createdAt": "2026-11-03T00:00:00Z", "activityCount": len(activities),
                        "versions": [{"versionId": item["id"], "version": item["version"], "createdAt": item["createdAt"]} for item in LISTED],
                        "versionsTruncated": False, "activitiesTruncated": False}}


EXECUTABLE = {"artifactId": "artifact-1", "artifactHash": "abc123", "rootActivityType": "Elsa.Activities.Flowchart.Activities.Flowchart",
              "rootActivityVersion": "1.0.0", "nodeCount": 3, "liveSourceReferenceCount": 2, "dependencies": [],
              "rootActivity": {"executableNodeId": "root", "authoredActivityId": "versions-flow", "activityType": "Flowchart", "activityTypeVersion": "1.0.0",
                               "structureKind": "elsa.flowchart.structure", "inputBindings": [],
                               "childSlots": [{"name": "Activities", "activities": [
                                   {"executableNodeId": "a", "authoredActivityId": "versions-a", "activityType": "WriteLine", "activityTypeVersion": "1.0.0",
                                    "structureKind": None, "inputBindings": [{"inputName": "Text", "source": "Literal", "isSensitive": False}], "childSlots": [], "connections": []},
                                   {"executableNodeId": "b", "authoredActivityId": "versions-b", "activityType": "WriteLine", "activityTypeVersion": "1.0.0",
                                    "structureKind": None, "inputBindings": [{"inputName": "Text", "source": "Literal", "isSensitive": False}], "childSlots": [], "connections": []}]}],
                               "connections": [{"source": {"nodeId": "a", "port": "Done"}, "target": {"nodeId": "b", "port": None}}]},
              "references": [{"sourceReferenceId": "ref-%d" % index, "scope": "Published", "definitionId": "wf-1", "definitionVersionId": "version-2",
                              "artifactVersion": "2.0.0", "live": True, "slotId": slot} for index, slot in ((1, "default"), (2, "qualification-side"))]}
PUBLICATIONS = [{"sourceReferenceId": "ref-1"}, {"sourceReferenceId": "ref-2"}]


def tool_node(node):
    return {"executableNodeId": node["executableNodeId"], "authoredActivityId": node["authoredActivityId"], "activityType": node["activityType"],
            "activityTypeVersion": node["activityTypeVersion"], "structureKind": node["structureKind"],
            "inputs": [{"name": item["inputName"], "source": item["source"], "sensitive": item["isSensitive"], "secretReference": None} for item in node["inputBindings"]],
            "slots": [{"name": slot["name"], "nodes": [tool_node(child) for child in slot["activities"]]} for slot in node["childSlots"]],
            "connections": [{"source": item["source"]["nodeId"], "sourcePort": item["source"]["port"], "target": item["target"]["nodeId"],
                             "targetPort": item["target"]["port"]} for item in node["connections"]]}


def references():
    return [{key: item[key] for key in ("sourceReferenceId", "scope", "definitionId", "definitionVersionId", "artifactVersion", "live")}
            | {"sourceKind": None, "sourceId": None, "sourceVersion": None, "publishedAt": None} for item in EXECUTABLE["references"]]


def executable_item(attachment):
    return {"id": attachment["id"], "moduleId": "Elsa.Studio.Workflows", "contentType": EXECUTABLE_KIND, "origin": "authoritativeRead",
            "target": dict(attachment["target"]),
            "content": {**{key: EXECUTABLE[key] for key in ("artifactId", "artifactHash", "rootActivityType", "rootActivityVersion", "nodeCount",
                                                              "liveSourceReferenceCount")},
                        "createdAt": "2026-11-03T00:00:00Z", "sourceReferences": references(), "sourceReferencesTruncated": False}}


def turn(provider, kinds, item, completed):
    def frame(kind):
        if kind == "contextAdmitted": return {"kind": kind, "payload": {"diagnostics": [], "attachments": [copy.deepcopy(item), {"contentType": "workflows.version-guidance"}]}}
        if kind == "toolCallCompleted": return copy.deepcopy(completed)
        return {"kind": kind}
    return [frame(kind) for kind in kinds[provider]], {"data": {"message": {"context": [copy.deepcopy(item)]}}}


def rows(provider, tool, offered=True, calls=1):
    value = [{"kind": provider + ".request", "data": {"commandId": COMMAND, "tools": [{"name": "other"}] + ([{"name": tool}] if offered else [])}}]
    value += [{"kind": provider + ".call", "data": {"commandId": COMMAND, "toolName": tool}}] * calls
    value.append({"kind": provider + OUTCOME_ROWS[provider], "data": {"commandId": COMMAND}})
    return value


def succeeded(tool, type_id, data):
    return {"kind": "toolCallCompleted", "payload": {"toolName": tool, "succeeded": True, "result": {"succeeded": True, "payload": {"typeId": type_id, "data": data}}}}


def refused(tool, text):
    return {"kind": "toolCallCompleted", "payload": {"toolName": tool, "succeeded": False, "summary": text,
                                                     "result": {"succeeded": False, "content": text, "payload": None, "receipt": None}}}


class Response:
    def __init__(self, status, value):
        self.status, self.value = status, value

    def json(self):
        return self.value


class PortTests(unittest.TestCase):
    def test_properties_are_read_as_the_runtime_binds_them(self):
        self.assertEqual("y", member(json.loads('{"id":"x","Id":"y"}'), "id"))
        self.assertEqual("z", member(json.loads('{"id":"x","id":"z"}'), "id"))
        self.assertIsNone(member(json.loads('{"other":1}'), "id"))
        self.assertIsNone(member(None, "id"))
        state = {"rootActivity": {"nodeId": "root", "activityVersionId": "flow", "inputs": [], "structure": {"payload": json.loads(
            '{"Connections":[],"connections":[{"Source":{"NodeId":"a","port":"x","Port":" True "},"target":{"nodeId":"b"}}],'
            '"Activities":[{"nodeId":"a","activityVersionId":"w","inputs":[]},{"nodeId":"b","activityVersionId":"w","inputs":[]}]}')}}}
        activities, connections = flatten_version(state)
        self.assertEqual(["root", "a", "b"], [item["nodeId"] for item in activities])
        self.assertEqual([("root", "a", "True", "b", "Done")], connections)

    def test_ports_are_normalized_as_the_flowchart_reads_them(self):
        self.assertEqual(["Done", "Done", "Done", "True"], [normalize_port(value) for value in (None, "", "   ", " True ")])


class AttachmentTests(unittest.TestCase):
    def test_references_carry_identity_only_and_bind_the_executable_to_its_hash(self):
        value = version_attachment("wf-1", "version-2", "2.0.0")
        self.assertEqual(("workflows.version", "workflow-version", "version-2", "version-2"),
                         (value["contentType"], value["target"]["resourceType"], value["target"]["resourceId"], value["target"]["revision"]))
        self.assertEqual({"definitionId": "wf-1", "versionId": "version-2"}, value["content"])
        executable = executable_attachment("artifact-1", "abc123", "ref-1")
        self.assertEqual(("workflow-executable", "artifact-1", "abc123"), (executable["target"]["resourceType"], executable["target"]["resourceId"], executable["target"]["revision"]))
        self.assertEqual({"artifactId", "artifactHash", "sourceReferenceId"}, set(executable["content"]))
        # Far inside the host's 32 KiB per-attachment budget (#3710): the server reads the content itself.
        for item in (value, executable): self.assertLess(len(json.dumps(item)), 2048)
        with self.assertRaises(RuntimeError): version_attachment("wf-1", "v" * 129)
        self.assertNotEqual("abc123", other_hash("abc123"))
        self.assertEqual(len("abc123"), len(other_hash("abc123")))

    def test_the_seed_states_and_the_probes_own_comparison(self):
        activities, connections = flatten_version(STATES[1])
        self.assertEqual([N["root"], N["a"], N["b"], N["c"]], [row["nodeId"] for row in activities])
        self.assertEqual([[], [N["root"]], [N["root"]], [N["root"]]], [row["scopePath"] for row in activities])
        self.assertEqual([(N["root"], N["a"], "Done", N["b"], "Done"), (N["root"], N["b"], "Done", N["c"], "Done")], connections)
        changes = expected_comparison(STATES[1], STATES[0])
        self.assertEqual(([N["c"]], [], {N["b"]: {"aspects": ["inputs"], "inputs": ["text"]}}), (changes["added"], changes["removed"], changes["changed"]))
        self.assertEqual([(N["root"], N["b"], "Done", N["c"], "Done")], changes["connectionsAdded"])
        later = expected_comparison(STATES[2], STATES[0])
        self.assertEqual(([N["c"]], [N["b"]], {}), (later["added"], later["removed"], later["changed"]))
        self.assertEqual([(N["root"], N["a"], "Done", N["b"], "Done")], later["connectionsRemoved"])


class VersionTurnTests(unittest.TestCase):
    def setUp(self):
        self.attachment = version_attachment(DEFINITION["id"], "version-2", "2.0.0")
        self.item = version_item(self.attachment, VERSIONS[1])

    def read(self, provider, data, item=None, kinds=READ_FRAMES, offered=True):
        frames, accepted = turn(provider, kinds, item or self.item, succeeded(VERSION_TOOL, VERSION_KIND, data))
        return require_version_read(frames, rows(provider, VERSION_TOOL, offered), accepted, provider=provider, command_id=COMMAND, attachment=self.attachment,
                                    version=VERSIONS[1], versions=LISTED, compared=VERSIONS[0] if data.get("comparison") else None)

    def data(self, compared=False):
        return {"readOnly": True, "definitionId": DEFINITION["id"], "definitionName": DEFINITION["name"], "version": version_section(VERSIONS[1]),
                "comparedTo": {"versionId": "version-1", "version": "1.0.0", "createdAt": "x", "activityCount": 3} if compared else None,
                "comparison": comparison(VERSIONS[1], VERSIONS[0]) if compared else None}

    def test_an_exact_version_read_and_its_comparison_in_each_adapters_pinned_order(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                self.assertEqual({"versionId": "version-2", "version": "2.0.0", "activities": 4}, self.read(provider, self.data()))
                record = self.read(provider, self.data(compared=True))
                self.assertEqual({"added": [N["c"]], "removed": [], "changed": [N["b"]], "connectionsAdded": 1, "connectionsRemoved": 0}, record["comparison"])

    def test_aspects_the_seeds_do_not_model_are_tolerated_but_asserted_ones_must_match(self):
        value = self.data(compared=True)
        # The server also reports a container's structure and child order; the probe asserts only what it models.
        value["comparison"]["activitiesChanged"].append({"nodeId": N["root"], "scopePath": [], "aspects": ["childOrder", "structure"], "inputs": []})
        self.assertEqual([N["b"]], self.read("copilot", value)["comparison"]["changed"])
        value["comparison"]["activitiesChanged"][0]["aspects"].append("activityVersion")
        with self.assertRaises(RuntimeError): self.read("copilot", value)

    def test_seeded_values_never_reach_the_context_the_frames_or_the_adapter(self):
        canary = "vcanary-0123456789abcdef"
        frames, accepted = turn("copilot", READ_FRAMES, self.item, succeeded(VERSION_TOOL, VERSION_KIND, self.data()))
        read = lambda frames, accepted, adapter: require_version_read(frames, adapter, accepted, provider="copilot", command_id=COMMAND,
                                                                      attachment=self.attachment, version=VERSIONS[1], versions=LISTED, canary=canary)
        self.assertEqual({"canary": "absent"}, read(frames, accepted, rows("copilot", VERSION_TOOL))["values"])
        leaked = rows("copilot", VERSION_TOOL); leaked[0]["data"]["prompt"] = "First " + canary
        with self.assertRaises(RuntimeError): read(frames, accepted, leaked)
        with self.assertRaises(RuntimeError): require_values_withheld("short", accepted, frames, [], "a read")

    def test_refuses_any_other_read(self):
        def broken(change, *, item=None, kinds=READ_FRAMES, offered=True, compared=True):
            value = self.data(compared=compared); change(value)
            with self.assertRaises(RuntimeError): self.read("copilot", value, item=item, kinds=kinds, offered=offered)
        broken(lambda value: None, kinds=REFUSED_FRAMES | {"copilot": REFUSED_FRAMES["copilot"][:-1]})
        broken(lambda value: None, offered=False)
        broken(lambda value: value["comparison"].update(computedBy="model"))
        broken(lambda value: value["comparison"].update(incomplete=True))
        broken(lambda value: value["comparison"].update(activitiesRemoved=[{"nodeId": N["a"]}]))
        broken(lambda value: value["comparison"].update(activitiesChanged=[]))
        broken(lambda value: value["comparison"].update(activitiesAdded=[], activitiesRemoved=[{"nodeId": N["c"]}]))
        broken(lambda value: value["comparedTo"].update(versionId="version-3"))
        broken(lambda value: value["version"].update(versionId="version-3"), compared=False)
        broken(lambda value: value["version"]["activities"].pop(), compared=False)
        broken(lambda value: value["version"]["activities"][1].update(scopePath=[]), compared=False)
        broken(lambda value: value["version"].update(activitiesTruncated=True), compared=False)
        broken(lambda value: value.update(readOnly=False), compared=False)
        other = copy.deepcopy(self.item); other["target"]["revision"] = "version-3"
        broken(lambda value: None, item=other, compared=False)
        client = copy.deepcopy(self.item); client["origin"] = "clientSnapshot"
        broken(lambda value: None, item=client, compared=False)
        listed = copy.deepcopy(self.item); listed["content"]["versions"].pop()
        broken(lambda value: None, item=listed, compared=False)

    def test_an_edit_request_is_offered_only_the_read(self):
        for provider in ("copilot", "anthropic"):
            for tool in (VERSION_TOOL, EXECUTABLE_TOOL):
                with self.subTest(provider=provider, tool=tool):
                    frames, _ = turn(provider, READ_FRAMES, self.item, succeeded(tool, VERSION_KIND, {}))
                    self.assertEqual(["other", tool], require_edits_unoffered(frames, rows(provider, tool), provider=provider, command_id=COMMAND, tool=tool))
                    for mutating in sorted(MUTATING_TOOLS):
                        offered = rows(provider, tool); offered[0]["data"]["tools"].append({"name": mutating})
                        with self.assertRaises(RuntimeError):
                            require_edits_unoffered(frames, offered, provider=provider, command_id=COMMAND, tool=tool)
                    with self.assertRaises(RuntimeError):
                        require_edits_unoffered(frames, rows(provider, tool, offered=False), provider=provider, command_id=COMMAND, tool=tool)
                    with self.assertRaises(RuntimeError):
                        require_edits_unoffered(frames[:2] + [{"kind": "proposalCreated"}] + frames[2:], rows(provider, tool), provider=provider,
                                                command_id=COMMAND, tool=tool)
        self.assertTrue({BATCH_TOOL, "workflow_set_designer_position"} <= MUTATING_TOOLS)

    def test_a_foreign_comparison_is_refused_at_preparation(self):
        check = lambda item: require_version_context(item, self.attachment, VERSIONS[1], LISTED)
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                for tool, text in ((VERSION_TOOL, TEXTS["version-not-found"]),):
                    frames, accepted = turn(provider, REFUSED_FRAMES, self.item, refused(tool, text))
                    self.assertIn(text, require_tool_refused(frames, rows(provider, tool), accepted, provider=provider, command_id=COMMAND,
                                                             tool=tool, kind=VERSION_KIND, check_item=check, text=text))
                    with self.assertRaises(RuntimeError):
                        require_tool_refused(frames, rows(provider, tool), accepted, provider=provider, command_id=COMMAND, tool=tool, kind=VERSION_KIND,
                                             check_item=check, text=TEXTS["version-denied"])
                    proposed = frames[:2] + [{"kind": "proposalCreated"}] + frames[3:]
                    with self.assertRaises(RuntimeError):
                        require_tool_refused(proposed, rows(provider, tool), accepted, provider=provider, command_id=COMMAND, tool=tool, kind=VERSION_KIND,
                                             check_item=check, text=text)

    def test_admission_refusals_name_their_own_cause(self):
        value = {"error": {"code": "weaver.context.incomplete", "message": TEXTS["version-not-found"]}}
        self.assertEqual("weaver.context.incomplete", require_admission_refused(Response(400, value), TEXTS["version-not-found"])["code"])
        for response, text in ((Response(200, value), TEXTS["version-not-found"]), (Response(400, value), TEXTS["version-denied"]),
                               (Response(503, value), TEXTS["version-not-found"]),
                               (Response(400, {"error": {"code": "weaver.policyDenied", "message": TEXTS["version-not-found"]}}), TEXTS["version-not-found"])):
            with self.assertRaises(RuntimeError): require_admission_refused(response, text)


class ExecutableTurnTests(unittest.TestCase):
    def setUp(self):
        self.attachment = executable_attachment("artifact-1", "abc123", "ref-1")
        self.item = executable_item(self.attachment)

    def data(self):
        return {"readOnly": True, **{key: EXECUTABLE[key] for key in ("artifactId", "artifactHash", "rootActivityType", "rootActivityVersion", "nodeCount")},
                "graph": tool_node(EXECUTABLE["rootActivity"]), "nodesIncluded": 3, "graphTruncated": False, "sourceReferences": references(),
                "sourceReferencesTruncated": False, "dependencies": []}

    def read(self, provider, data, item=None, publications=PUBLICATIONS):
        frames, accepted = turn(provider, READ_FRAMES, item or self.item, succeeded(EXECUTABLE_TOOL, EXECUTABLE_KIND, data))
        return require_executable_read(frames, rows(provider, EXECUTABLE_TOOL), accepted, provider=provider, command_id=COMMAND,
                                       attachment=self.attachment, executable=EXECUTABLE, publications=publications)

    def test_the_read_is_the_runtime_apis_identity_hash_graph_and_source_references(self):
        self.assertEqual(3, len(flatten_executable(EXECUTABLE["rootActivity"])))
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                self.assertEqual({"artifactId": "artifact-1", "artifactHash": "abc123", "nodes": 3, "sourceReferences": 2}, self.read(provider, self.data()))

    def test_refuses_any_other_read(self):
        def broken(change, item=None, publications=PUBLICATIONS):
            value = self.data(); change(value)
            with self.assertRaises(RuntimeError): self.read("copilot", value, item=item, publications=publications)
        broken(lambda value: value.update(artifactHash="other"))
        broken(lambda value: value["graph"]["slots"][0]["nodes"].pop())
        broken(lambda value: value["graph"]["connections"].clear())
        broken(lambda value: value["graph"]["slots"][0]["nodes"][0]["inputs"][0].update(sensitive=True))
        broken(lambda value: value.update(graphTruncated=True))
        broken(lambda value: value["sourceReferences"].pop())
        broken(lambda value: None, publications=[*PUBLICATIONS, {"sourceReferenceId": "ref-3"}])
        stale = copy.deepcopy(self.item); stale["target"]["revision"] = other_hash("abc123")
        broken(lambda value: None, item=stale)
        listed = copy.deepcopy(self.item); listed["content"]["sourceReferences"][0]["live"] = False
        broken(lambda value: None, item=listed)
        self.assertEqual("abc123", require_executable_context(self.item, self.attachment, EXECUTABLE)["artifactHash"])


def write(root, value):
    path = Path(root) / (str(len(list(Path(root).iterdir()))) + ".json")
    path.write_text(json.dumps(value))
    return {"path": str(path), "sha256": sha(path)}


class GateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def section(self, change=None):
        names = sorted(EXPECTED_CASES)
        records = []
        for name in names:
            value = {"id": name, "status": "passed", "adapter": name.split("-", 1)[0], "native": synthetic_native(name), "browser": synthetic_browser(name)}
            if change: change(name, value)
            records.append({"id": name, "status": "passed", "record": write(self.root, value)})
        return {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False, "source": "a" * 40,
                "caseIDs": names, "records": records, "stages": []}

    def test_complete_section_and_its_mutations(self):
        self.assertEqual(4, len(require_workflow_inspection(self.section(), self.root, source="a" * 40)))
        self.assertEqual(sorted(VERSION_NATIVE), sorted(synthetic_native("copilot-workflow-versions")))
        self.assertEqual(sorted(EXECUTABLE_NATIVE), sorted(synthetic_native("copilot-workflow-executables")))
        self.assertTrue(inspection_manifest_gaps(None))
        self.assertTrue(inspection_manifest_gaps({**self.section(), "status": "failed"}))
        self.assertTrue(inspection_manifest_gaps({**self.section(), "liveProvider": True}))
        for change in (lambda name, value: value["native"].pop("explain"),
                       lambda name, value: value["native"]["edit-refused"].update(unchanged=False),
                       lambda name, value: value["native"]["edit-refused"].update(offered=[BATCH_TOOL]),
                       lambda name, value: value["native"]["edit-refused"].pop("offered"),
                       lambda name, value: value["native"].pop("design-agent-absent" if name.endswith("versions") else "runtime-agent-absent"),
                       lambda name, value: value["native"]["stale-hash" if name.endswith("executables") else "compare"].update(control="other"),
                       lambda name, value: value["browser"]["observations"].update(readOnly=False),
                       lambda name, value: value.update(adapter="other")):
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                require_workflow_inspection(self.section(change), self.root, source="a" * 40)


class BrowserTests(unittest.TestCase):
    def observed(self, provider, journey, observations, command_id=COMMAND):
        return {"journey": journey, "status": "passed", "provider": provider, "observations": copy.deepcopy(observations), "ownerBefore": {"path": "a"},
                "ownerAfter": {"path": "b"}, "evidence": [{"path": "c"}], "commands": {"explain": command_id}, "pageErrors": []}

    def test_each_journey_reads_through_its_own_tool(self):
        for journey, observations, tool in ((VERSION_BROWSER, VERSION_OBSERVATIONS, VERSION_TOOL), (EXECUTABLE_BROWSER, EXECUTABLE_OBSERVATIONS, EXECUTABLE_TOOL)):
            for provider in ("copilot", "anthropic"):
                with self.subTest(journey=journey, provider=provider):
                    commands = [{"commandId": COMMAND, "toolName": tool, "arguments": {}}]
                    self.assertEqual(journey, require_browser(self.observed(provider, journey, observations), commands, rows(provider, tool),
                                                              provider=provider, journey=journey)["journey"])
                    with self.assertRaises(RuntimeError):
                        require_browser(self.observed(provider, journey, {**observations, "readOnly": False}), commands, rows(provider, tool), provider=provider, journey=journey)
                    with self.assertRaises(RuntimeError):
                        require_browser(self.observed(provider, journey, observations), [{**commands[0], "toolName": BATCH_TOOL}], rows(provider, tool),
                                        provider=provider, journey=journey)
                    with self.assertRaises(RuntimeError):
                        require_browser(self.observed(provider, journey, observations), commands, rows(provider, tool, calls=2), provider=provider, journey=journey)


class VersionSeedCanaryTests(unittest.TestCase):
    """Witness dbe305ff2: the canary must be in every seeded version input, never in the batch seed."""

    def test_every_seeded_version_text_carries_the_canary(self):
        write = {"activityVersionId": "write-v1"}
        flowchart = {"activityVersionId": "flow-v1", "activityTypeKey": "Elsa.Flowchart",
                     "authoringTemplate": {"structure": {"kind": "elsa.flowchart.structure"}},
                     "containerStructure": {"mode": "flowchart", "slots": [{"property": "activities", "cardinality": "many"}],
                                            "initialPayload": {"activities": [], "connections": []}}}
        states = version_states(write, flowchart, "vcanary-0123456789abcdef")
        texts = [leaf["inputs"][0]["value"]["value"] for state in states for leaf in state["rootActivity"]["structure"]["payload"]["activities"]]
        self.assertTrue(texts)
        self.assertTrue(all("vcanary-0123456789abcdef" in text for text in texts))


if __name__ == "__main__":
    unittest.main()
