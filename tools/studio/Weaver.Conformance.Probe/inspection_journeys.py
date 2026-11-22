"""Mandatory workflow-versions and workflow-executables qualification (spec 1193, story 8; T041, FR-026–FR-028, SC-009).

On each actual adapter, Weaver's read-only inspection of persisted versions and Executables must hold against the packaged
consumer's own design, publishing and runtime services. The seed (owner_resources.seed_versioned_workflow) is one
definition with three versions promoted through the design API (a -> b; a -> b -> c with b's text changed; a -> c), the
second published twice (its default slot and a named side-by-side slot), so its content-addressed Executable carries two
Source References.

workflow-versions
- explain: the caller's workflows.version reference is admitted as the server's own read (data.message.context): exactly
  that version, attributed to its version identity, with the definition's versions as the version API lists them; every
  admission in the turn repeats it, and workflow_read_version returns that exact version's activities by scope path and its
  connections, as the version API holds them (input names only, never values);
- compare: workflow_read_version with compareToVersionId returns the tool's own structural comparison
  (computedBy workflow_read_version) of the two exact versions: activities added, removed and changed, and connections,
  equal to the probe's own comparison of the two version API reads;
- edit refused: in a build-mode, full-auto turn with only the version in context, the engine admits no mutating workflow
  tool for that target (each one's owner authority refuses it), so the adapter is offered the read and none of
  MUTATING_TOOLS; the read completes, nothing is proposed, and the version and the draft read back byte-equal;
- negative controls: an unknown version and a version of another definition are refused at admission as unavailable, a
  comparison against another definition's version is refused at preparation, and an actor without design read is refused
  at admission;
- deployment shape: with the Design-side Weaver feature (WeaverWorkflows) deselected, a workflows.version reference is
  explicitly unavailable (the engine's weaver.context.kind_unsupported); the feature is selected again afterwards;
- in the actual packaged Studio (browser.mjs, workflow-version-history): the editor's version history discloses the selected
  version and its Explain action reads it.

workflow-executables
- explain: the caller's workflows.executable reference, bound to its Artifact Hash, is admitted as the server's own read and
  workflow_read_executable returns the identity, Artifact Hash, graph and Source References the runtime Executable API reads;
- edit refused: the same build-mode, full-auto control with only the Executable in context: only the read is offered, it
  completes, and the Executable reads back byte-equal;
- negative controls: a bound hash that differs is stale, an unknown artifact is unavailable, and an actor without
  workflows/runtime:read is refused, each at admission;
- deployment shape: with the Runtime-side Weaver feature (WeaverWorkflowsRuntime) deselected, a workflows.executable
  reference is explicitly unavailable; selected again afterwards;
- in the actual packaged Studio (browser.mjs, workflow-executable-inspector): the Executable Inspector discloses the exact
  Executable and its Explain action reads it.

The deployment-shape controls deselect one Weaver feature in the packaged combined host. The AppDomain-wide Design-only and
Runtime-only shapes (no Elsa.Workflows.Design* assembly loaded) are proven by the C# composition tests, not by this probe.
Frame orders are each adapter's own, pinned from earlier witnesses (explain_journeys.READ_FRAMES for a completed read,
batch_journeys.REFUSED_FRAMES for a preparation refusal). Nothing is retried.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import json
from pathlib import Path
import os
import secrets
import shutil
import signal
import threading
import time
import uuid

from artifacts import TOOL, check, sha
from authorization_journeys import select_command
from batch_journeys import OUTCOME_ROWS, REFUSED_FRAMES
from explain_journeys import READ_FRAMES
from journey_runtime import browser_inputs, same
from owner_resources import owner_path, seed_versioned_workflow
from runtime import RUNTIME_ENVIRONMENT, private_json
from truth_seam_journeys import READER, same_target


SECTION = "workflowInspectionQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 600
PROVIDERS = ("copilot", "anthropic")
VERSION_CASES = frozenset(provider + "-workflow-versions" for provider in PROVIDERS)
EXECUTABLE_CASES = frozenset(provider + "-workflow-executables" for provider in PROVIDERS)
EXPECTED_CASES = VERSION_CASES | EXECUTABLE_CASES
WORKFLOW_MODULE = "Elsa.Studio.Workflows"
VERSION_TOOL, EXECUTABLE_TOOL, BATCH_TOOL = "workflow_read_version", "workflow_read_executable", "workflow_apply_operation_batch"
VERSION_KIND, EXECUTABLE_KIND = "workflows.version", "workflows.executable"
VERSION_GUIDANCE, EXECUTABLE_GUIDANCE = "workflows.version-guidance", "workflows.executable-guidance"
VERSION_RESOURCE, EXECUTABLE_RESOURCE = "workflow-version", "workflow-executable"
VERSION_CAPABILITY, EXECUTABLE_CAPABILITY, CHANGE_CAPABILITY = "workflow.version-explain", "workflow.executable-explain", "workflow.propose-change"
COMPUTED_BY = VERSION_TOOL
# Every mutating workflow tool the Workflows packages register (slices B–E). With only a version or an Executable in context
# the engine admits none of them (each one's owner authority refuses that target), so none may be offered to the adapter.
MUTATING_TOOLS = frozenset({"workflow_apply_operation_batch", "workflow_set_designer_position", "workflow_library_assign_tags",
                            "workflow_library_set_favorite", "workflow_library_move_to_folder", "workflow_library_save_view",
                            "workflow_library_set_list_state", "workflow_create_dialog_prefill"})
DESIGN_WEAVER_FEATURE, RUNTIME_WEAVER_FEATURE = "WeaverWorkflows", "WeaverWorkflowsRuntime"
# Principals: the original "other" principal holds only the base Weaver grants (no workflows/design:read); the truth-seam
# reader holds design read but not workflows/runtime:read.
DESIGN_DENIED, RUNTIME_DENIED = 1, READER

# The modules' own texts, which the server must use verbatim (each is checked as a substring of the refusal it reports).
TEXTS = {
    "version-not-found": "The workflow version is unavailable.",
    "version-denied": "Current workflow design read permission is required for the workflow version.",
    "executable-not-found": "The workflow Executable is unavailable.",
    "executable-stale": "The workflow Executable's Artifact Hash differs from the bound hash, so the context is stale.",
    "executable-denied": "Current workflow runtime read permission is required for the workflow Executable.",
    # The engine's own text for a context kind no installed module admits.
    "kind-unsupported": "No installed module admits the supplied context kind and owner.",
}
ADMISSION_REFUSAL = {"code": "weaver.context.incomplete", "statusCode": 400}
VERSION_NATIVE = ("explain", "compare", "edit-refused", "unknown-version", "other-definition", "compare-other-definition", "design-denied",
                  "design-weaver-absent")
EXECUTABLE_NATIVE = ("explain", "edit-refused", "stale-hash", "unknown-artifact", "runtime-denied", "runtime-weaver-absent")
VERSION_BROWSER, EXECUTABLE_BROWSER = "workflow-version-history", "workflow-executable-inspector"
VERSION_OBSERVATIONS = {"versionSelected": True, "versionContext": True, "explainCompleted": True, "readOnly": True}
EXECUTABLE_OBSERVATIONS = {"executableContext": True, "hashBound": True, "explainCompleted": True, "readOnly": True}
BROWSER = {VERSION_BROWSER: (VERSION_OBSERVATIONS, {"explain": VERSION_TOOL}), EXECUTABLE_BROWSER: (EXECUTABLE_OBSERVATIONS, {"explain": EXECUTABLE_TOOL})}


# ---- Attachments: the pages' own references, as Studio builds them (useWorkflowInspectionContext). ----

def _client(prefix):
    return {"clientId": "qualification-" + prefix, "generationId": str(uuid.uuid4())}


def version_attachment(definition_id, version_id, version="1.0.0"):
    """The version history's reference to one exact version: its identity only; the server reads the content."""
    check(all(isinstance(value, str) and 0 < len(value) <= 128 for value in (definition_id, version_id)), "A version reference needs bounded identities")
    return {"id": "workflows.version:" + version_id, "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "contentType": VERSION_KIND,
            "sensitivity": "internal", "scope": "selection", "origin": "clientSnapshot", "source": VERSION_RESOURCE, "sourceId": version_id,
            "label": "v" + version, "summary": "Workflow version v" + version,
            "target": {"moduleId": WORKFLOW_MODULE, "resourceType": VERSION_RESOURCE, "resourceId": version_id, "revision": version_id},
            "client": _client("versions"), "content": {"definitionId": definition_id, "versionId": version_id}}


def executable_attachment(artifact_id, artifact_hash, source_reference_id=None):
    """The Executable Inspector's reference to one exact Executable, bound to its Artifact Hash."""
    check(all(isinstance(value, str) and value for value in (artifact_id, artifact_hash)), "An Executable reference needs its identity and hash")
    return {"id": "workflows.executable:" + artifact_id, "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "contentType": EXECUTABLE_KIND,
            "sensitivity": "internal", "scope": "screen", "origin": "clientSnapshot", "source": EXECUTABLE_RESOURCE, "sourceId": artifact_id,
            "label": "Executable " + artifact_id[:24], "summary": "Workflow Executable " + artifact_id,
            "target": {"moduleId": WORKFLOW_MODULE, "resourceType": EXECUTABLE_RESOURCE, "resourceId": artifact_id, "revision": artifact_hash},
            "client": _client("executables"),
            "content": {"artifactId": artifact_id, "artifactHash": artifact_hash, "sourceReferenceId": source_reference_id}}


def other_hash(value):
    """The same hash with its last character changed, in the same alphabet: a stale binding of the same artifact."""
    check(isinstance(value, str) and len(value) > 1, "A stale binding needs the real hash")
    return value[:-1] + ("0" if value[-1] != "0" else "1")


# ---- The probe's own reads of the owning APIs' answers. ----

def member(value, name):
    """A JSON object's property as the runtime's web serializer binds it: case-insensitively, the last match winning (json.loads
    already keeps the last of exact duplicates)."""
    if not isinstance(value, dict): return None
    found = None
    for key, item in value.items():
        if isinstance(key, str) and key.lower() == name.lower(): found = item
    return found


def normalize_port(port):
    """A flowchart connection port as the runtime normalizes it."""
    return port.strip() if isinstance(port, str) and port.strip() else "Done"


def flatten_version(state):
    """Each activity of a version API state by scope path (container ids from the root to its container), with the names of
    its authored inputs, and each flowchart connection by its container; flowcharts are the consumer's only containers."""
    root = state.get("rootActivity") if isinstance(state, dict) else None
    check(isinstance(root, dict), "The version API state has no root activity")
    activities, connections = [], []
    def visit(node, scope):
        inputs = sorted({item.get("referenceKey") for item in node.get("inputs") or [] if isinstance(item, dict) and item.get("referenceKey")})
        activities.append({"nodeId": node["nodeId"], "activityVersionId": node["activityVersionId"], "scopePath": list(scope), "inputs": inputs,
                           # The whole authored input, as the server compares the whole authored argument state.
                           "values": {item["referenceKey"]: json.dumps(item, sort_keys=True) for item in node.get("inputs") or []
                                      if isinstance(item, dict) and item.get("referenceKey")}})
        payload = ((node.get("structure") or {}).get("payload")) if isinstance(node.get("structure"), dict) else None
        if not isinstance(payload, dict): return
        # Read as the runtime binds them: property names in any letter case, the last matching one winning.
        for item in member(payload, "connections") or []:
            source, target = member(item, "source") or {}, member(item, "target") or {}
            # Ports as the flowchart reads them (FlowchartEndpoint.NormalizePort): a blank port is Done, a port is trimmed.
            connections.append((node["nodeId"], member(source, "nodeId"), normalize_port(member(source, "port")),
                                member(target, "nodeId"), normalize_port(member(target, "port"))))
        for child in member(payload, "activities") or []:
            visit(child, [*scope, node["nodeId"]])
    visit(root, [])
    return activities, sorted(connections, key=lambda item: tuple(value or "" for value in item))


def expected_comparison(version_state, compared_state):
    """The changes of version relative to comparedTo: added (in version only), removed (in comparedTo only), changed (in both
    with another catalog version, scope or authored input), and connections added and removed."""
    mine, my_connections = flatten_version(version_state)
    theirs, their_connections = flatten_version(compared_state)
    by_id = {item["nodeId"]: item for item in theirs}
    mine_ids = {item["nodeId"] for item in mine}
    changed = {}
    for item in mine:
        other = by_id.get(item["nodeId"])
        if other is None: continue
        aspects, inputs = [], sorted({key for key in set(item["values"]) | set(other["values"]) if item["values"].get(key) != other["values"].get(key)})
        if item["activityVersionId"] != other["activityVersionId"]: aspects.append("activityVersion")
        if item["scopePath"] != other["scopePath"]: aspects.append("scope")
        if inputs: aspects.append("inputs")
        if aspects: changed[item["nodeId"]] = {"aspects": sorted(aspects), "inputs": inputs}
    return {"added": sorted(item["nodeId"] for item in mine if item["nodeId"] not in by_id),
            "removed": sorted(item["nodeId"] for item in theirs if item["nodeId"] not in mine_ids),
            "changed": changed,
            "connectionsAdded": sorted(set(my_connections) - set(their_connections), key=lambda item: tuple(value or "" for value in item)),
            "connectionsRemoved": sorted(set(their_connections) - set(my_connections), key=lambda item: tuple(value or "" for value in item))}


def flatten_executable(node):
    """The runtime Executable API's node tree, depth first: each node's identity, inputs, slots and connections."""
    check(isinstance(node, dict) and isinstance(node.get("executableNodeId"), str), "The Executable API node has no identity")
    rows = []
    def visit(current):
        rows.append({"executableNodeId": current["executableNodeId"], "authoredActivityId": current.get("authoredActivityId"),
                     "activityType": current.get("activityType"), "activityTypeVersion": current.get("activityTypeVersion"),
                     "structureKind": current.get("structureKind"),
                     "inputs": sorted((item.get("inputName"), item.get("source"), item.get("isSensitive") is True) for item in current.get("inputBindings") or []),
                     "slots": [(slot.get("name"), [child.get("executableNodeId") for child in slot.get("activities") or []]) for slot in current.get("childSlots") or []],
                     "connections": sorted(((item.get("source") or {}).get("nodeId"), (item.get("source") or {}).get("port"),
                                            (item.get("target") or {}).get("nodeId"), (item.get("target") or {}).get("port"))
                                           for item in current.get("connections") or [])})
        for slot in current.get("childSlots") or []:
            for child in slot.get("activities") or []: visit(child)
    visit(node)
    return rows


def flatten_tool_graph(node):
    """The same rows from workflow_read_executable's graph (its own node shape)."""
    check(isinstance(node, dict) and isinstance(node.get("executableNodeId"), str), "The read Executable graph has no root node")
    rows = []
    def visit(current):
        rows.append({"executableNodeId": current["executableNodeId"], "authoredActivityId": current.get("authoredActivityId"),
                     "activityType": current.get("activityType"), "activityTypeVersion": current.get("activityTypeVersion"),
                     "structureKind": current.get("structureKind"),
                     "inputs": sorted((item.get("name"), item.get("source"), item.get("sensitive") is True) for item in current.get("inputs") or []),
                     "slots": [(slot.get("name"), [child.get("executableNodeId") for child in slot.get("nodes") or []]) for slot in current.get("slots") or []],
                     "connections": sorted((item.get("source"), item.get("sourcePort"), item.get("target"), item.get("targetPort"))
                                           for item in current.get("connections") or [])})
        for slot in current.get("slots") or []:
            for child in slot.get("nodes") or []: visit(child)
    visit(node)
    return rows


REFERENCE_FIELDS = ("sourceReferenceId", "scope", "definitionId", "definitionVersionId", "artifactVersion", "live")


def references_of(items):
    check(isinstance(items, list), "Source References must be a list")
    return sorted([tuple(item.get(key) for key in REFERENCE_FIELDS) for item in items if isinstance(item, dict)])


# ---- Pure checks over a native turn's frames and adapter rows. ----

def _frames(frames):
    values = [frame for frame in frames if isinstance(frame, dict)]
    check(len(values) == len(frames) and values, "Malformed native stream frame")
    return values


def _kinds(values, expected, what):
    kinds = [frame.get("kind") for frame in values]
    check(kinds == expected, what + " did not follow this adapter's frame order: " + json.dumps(kinds))
    return kinds


def _owned(rows, provider, command_id):
    return [row for row in rows if isinstance(row, dict) and (row.get("data") or {}).get("commandId") == command_id
            and str(row.get("kind", "")).startswith(provider + ".")]


def _offered(owned, provider):
    offered = [row["data"] for row in owned if row.get("kind") == provider + ".request" and row["data"].get("tools")]
    check(bool(offered), "The actual adapter recorded no tool advertisement")
    return sorted({tool.get("name") for tool in offered[0]["tools"] if isinstance(tool, dict)})


def _authoritative(item):
    return isinstance(item, dict) and item.get("origin") in ("authoritativeRead", "AuthoritativeRead")


def require_version_context(item, attachment, version, versions):
    """The server's own read of the referenced version: exactly it, by its version identity, with the definition's versions."""
    check(isinstance(item, dict) and item.get("contentType") == VERSION_KIND and _authoritative(item) and item.get("moduleId") == WORKFLOW_MODULE
          and same_target(item.get("target"), attachment["target"]), "The version reference was not admitted as the server's read of that exact version")
    content = item.get("content")
    activities, _ = flatten_version(version["state"])
    check(isinstance(content, dict) and content.get("versionId") == version["id"] and content.get("version") == version["version"]
          and content.get("definitionId") == version["definition"]["id"] and content.get("definitionName") == version["definition"]["name"]
          and content.get("activityCount") == len(activities) and content.get("activitiesTruncated") is False, "The admitted version is not the version API's read of that version")
    listed = content.get("versions")
    check(isinstance(listed, list) and content.get("versionsTruncated") is False
          and sorted((row.get("versionId"), row.get("version")) for row in listed if isinstance(row, dict))
          == sorted((row["id"], row["version"]) for row in versions), "The admitted version list differs from the version API's list")
    return content


def require_admissions(values, accepted, kind, check_item):
    """The accepted message (data.message.context) holds exactly one admitted item of the kind, and every admission in the turn
    repeats that same projection, with no diagnostic."""
    message = (accepted.get("data") or {}).get("message") if isinstance(accepted, dict) else None
    context = message.get("context") if isinstance(message, dict) else None
    check(isinstance(context, list), "The accepted message carries no admitted context")
    items = [item for item in context if isinstance(item, dict) and item.get("contentType") == kind]
    check(len(items) == 1, "The accepted message must admit exactly one " + kind + " context")
    content = check_item(items[0])
    for frame in values:
        if frame.get("kind") != "contextAdmitted": continue
        payload = frame.get("payload")
        check(isinstance(payload, dict) and payload.get("diagnostics") == [], "Context admission reported a diagnostic")
        current = [item for item in payload.get("attachments") or [] if isinstance(item, dict) and item.get("contentType") == kind]
        check(len(current) == 1 and same(check_item(current[0]), content), "An admission in the turn differs from the accepted " + kind + " context")
    return content


def _completed(values, tool, *, succeeded):
    completed = [frame.get("payload") for frame in values if frame.get("kind") == "toolCallCompleted"]
    check(len(completed) == 1 and isinstance(completed[0], dict) and completed[0].get("toolName") == tool and completed[0].get("succeeded") is succeeded,
          "The " + tool + " tool did not complete once " + ("successfully" if succeeded else "refused"))
    return completed[0]


def _called_once(rows, provider, command_id, tool):
    owned = _owned(rows, provider, command_id)
    calls = [row for row in owned if row.get("kind") == provider + ".call"]
    check(tool in _offered(owned, provider) and len(calls) == 1 and calls[0]["data"].get("toolName") == tool,
          "The actual adapter was not offered " + tool + " or did not call it exactly once")
    return owned


def require_version_activities(data, version):
    """The tool's version section: that exact version's activities by scope path (input names only) and its connections."""
    expected, connections = flatten_version(version["state"])
    check(isinstance(data, dict) and data.get("versionId") == version["id"] and data.get("version") == version["version"]
          and data.get("activityCount") == len(expected) and data.get("activitiesTruncated") is False and data.get("connectionsTruncated") is False,
          "The read version is not the exact version the context names")
    observed = data.get("activities")
    check(isinstance(observed, list) and sorted((row.get("nodeId"), row.get("activityVersionId"), tuple(row.get("scopePath") or []), tuple(sorted(row.get("inputs") or [])))
                                                for row in observed if isinstance(row, dict))
          == sorted((row["nodeId"], row["activityVersionId"], tuple(row["scopePath"]), tuple(row["inputs"])) for row in expected),
          "The read activities differ from the version API's, by scope path")
    slots = {row.get("slot") for row in observed if isinstance(row, dict) and row.get("scopePath")}
    root = [row for row in observed if isinstance(row, dict) and not row.get("scopePath")]
    check(len(root) == 1 and root[0].get("slot") is None and len(slots) == 1 and all(isinstance(slot, str) and slot for slot in slots),
          "The read activities do not address their slots consistently")
    check(sorted((row.get("container"), row.get("source"), row.get("sourcePort"), row.get("target"), row.get("targetPort")) for row in data.get("connections") or [])
          == [tuple(item) for item in connections], "The read connections differ from the version API's")
    return len(expected)


ASSERTED_ASPECTS = frozenset({"activityVersion", "scope", "inputs"})


def require_values_withheld(canary, accepted, frames, rows, what):
    """No seeded input value (each carries the canary) reaches the admitted context, the turn's frames or the actual adapter's
    request rows: names only, never values."""
    if canary is None: return None
    check(isinstance(canary, str) and len(canary) >= 12, "A value canary must be a distinctive token")
    for label, value in (("admitted context", accepted), ("turn frames", frames), ("adapter rows", rows)):
        check(canary not in json.dumps(value), "A seeded input value reached the " + label + " of " + what)
    return {"canary": "absent"}


def require_comparison(comparison, version, compared):
    """The tool's own comparison of the two exact versions equals the probe's comparison of the two version API reads."""
    expected = expected_comparison(version["state"], compared["state"])
    check(isinstance(comparison, dict) and comparison.get("computedBy") == COMPUTED_BY and isinstance(comparison.get("note"), str) and comparison["note"]
          and comparison.get("truncated") is False, "The comparison is not labelled as workflow_read_version's own, complete comparison")
    ids = lambda key: sorted(row.get("nodeId") for row in comparison.get(key) or [] if isinstance(row, dict))
    check(ids("activitiesAdded") == expected["added"] and ids("activitiesRemoved") == expected["removed"],
          "The activities added or removed differ from the two version reads")
    # Only the aspects the probe models from the two version API reads are compared; the server also reports structure, child
    # order, outputs, intrinsic and placement changes, which these seeds do not assert.
    changed = {}
    for row in comparison.get("activitiesChanged") or []:
        if not isinstance(row, dict): continue
        aspects = sorted(aspect for aspect in row.get("aspects") or [] if aspect in ASSERTED_ASPECTS)
        if aspects: changed[row.get("nodeId")] = {"aspects": aspects, "inputs": sorted(row.get("inputs") or [])}
    check(changed == expected["changed"], "The activities changed differ from the two version reads: " + json.dumps(changed)[:300])
    check(comparison.get("incomplete") is False, "The comparison of two flowchart versions must be complete")
    connection = lambda row: (row.get("container"), row.get("source"), row.get("sourcePort"), row.get("target"), row.get("targetPort"))
    check(sorted(connection(row) for row in comparison.get("connectionsAdded") or []) == [tuple(item) for item in expected["connectionsAdded"]]
          and sorted(connection(row) for row in comparison.get("connectionsRemoved") or []) == [tuple(item) for item in expected["connectionsRemoved"]],
          "The connections added or removed differ from the two version reads")
    return {"added": expected["added"], "removed": expected["removed"], "changed": sorted(expected["changed"]),
            "connectionsAdded": len(expected["connectionsAdded"]), "connectionsRemoved": len(expected["connectionsRemoved"])}


def require_version_read(frames, rows, accepted, *, provider, command_id, attachment, version, versions, compared=None, canary=None):
    """One completed version read: the admitted context, this adapter's pinned order, the exact version, and (with compared)
    the tool's own comparison."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    _kinds(values, READ_FRAMES[provider], "The version read turn")
    require_admissions(values, accepted, VERSION_KIND, lambda item: require_version_context(item, attachment, version, versions))
    completed = _completed(values, VERSION_TOOL, succeeded=True)
    payload = (completed.get("result") or {}).get("payload") or {}
    data = payload.get("data")
    check(payload.get("typeId") == VERSION_KIND and isinstance(data, dict) and data.get("readOnly") is True
          and data.get("definitionId") == version["definition"]["id"], "The version tool did not return its typed, read-only result")
    activities = require_version_activities(data.get("version"), version)
    record = {"versionId": version["id"], "version": version["version"], "activities": activities}
    if compared is None:
        check(data.get("comparedTo") is None and data.get("comparison") is None, "A comparison was returned that was not requested")
    else:
        to = data.get("comparedTo") or {}
        check(to.get("versionId") == compared["id"] and to.get("version") == compared["version"], "The comparison is not against the requested exact version")
        record.update(comparedTo=compared["id"], comparison=require_comparison(data.get("comparison"), version, compared))
    _called_once(rows, provider, command_id, VERSION_TOOL)
    if canary is not None: record["values"] = require_values_withheld(canary, accepted, frames, rows, "the version read")
    return record


def require_executable_context(item, attachment, executable):
    """The server's own read of the referenced Executable, bound to the hash, with the runtime API's Source References."""
    check(isinstance(item, dict) and item.get("contentType") == EXECUTABLE_KIND and _authoritative(item) and item.get("moduleId") == WORKFLOW_MODULE
          and same_target(item.get("target"), attachment["target"]), "The Executable reference was not admitted as the server's read bound to its hash")
    content = item.get("content")
    check(isinstance(content, dict) and all(content.get(key) == executable.get(key) for key in
          ("artifactId", "artifactHash", "rootActivityType", "rootActivityVersion", "nodeCount", "liveSourceReferenceCount"))
          and content.get("sourceReferencesTruncated") is False and references_of(content.get("sourceReferences")) == references_of(executable["references"]),
          "The admitted Executable is not the runtime API's read of that Executable")
    return content


def require_executable_read(frames, rows, accepted, *, provider, command_id, attachment, executable, publications, canary=None):
    """One completed Executable read: the admitted context, this adapter's pinned order, the identity, Artifact Hash, graph and
    Source References as the runtime Executable API reads them, including both publications' references."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    _kinds(values, READ_FRAMES[provider], "The Executable read turn")
    require_admissions(values, accepted, EXECUTABLE_KIND, lambda item: require_executable_context(item, attachment, executable))
    completed = _completed(values, EXECUTABLE_TOOL, succeeded=True)
    payload = (completed.get("result") or {}).get("payload") or {}
    data = payload.get("data")
    check(payload.get("typeId") == EXECUTABLE_KIND and isinstance(data, dict) and data.get("readOnly") is True
          and all(data.get(key) == executable.get(key) for key in ("artifactId", "artifactHash", "rootActivityType", "rootActivityVersion", "nodeCount")),
          "The Executable tool did not return the runtime API's identity and Artifact Hash")
    expected = flatten_executable(executable["rootActivity"])
    check(data.get("graphTruncated") is False and data.get("nodesIncluded") == len(expected) and same(flatten_tool_graph(data.get("graph")), expected),
          "The read graph differs from the runtime API's node tree")
    references = references_of(data.get("sourceReferences"))
    check(data.get("sourceReferencesTruncated") is False and references == references_of(executable["references"])
          and {item["sourceReferenceId"] for item in publications} <= {row[0] for row in references},
          "The read Source References differ from the runtime API's or lack a publication's reference")
    check(sorted((row.get("artifactId"), row.get("artifactHash")) for row in data.get("dependencies") or [])
          == sorted((row.get("artifactId"), row.get("artifactHash")) for row in executable.get("dependencies") or []),
          "The read dependencies differ from the runtime API's")
    _called_once(rows, provider, command_id, EXECUTABLE_TOOL)
    return {"artifactId": executable["artifactId"], "artifactHash": executable["artifactHash"], "nodes": len(expected), "sourceReferences": len(references),
            **({"values": require_values_withheld(canary, accepted, frames, rows, "the Executable read")} if canary is not None else {})}


def require_tool_refused(frames, rows, accepted, *, provider, command_id, tool, kind, check_item, text):
    """Preparation refused with the module's own text in this adapter's pinned order; no review followed."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    _kinds(values, REFUSED_FRAMES[provider], "The refused " + tool + " turn")
    require_admissions(values, accepted, kind, check_item)
    completed = _completed(values, tool, succeeded=False)
    result = completed.get("result")
    check(isinstance(result, dict) and result.get("succeeded") is False and result.get("payload") is None and result.get("receipt") is None,
          "The refused " + tool + " carried a payload or receipt")
    summary = completed.get("summary")
    check(isinstance(summary, str) and text in summary, "Refused for another reason: " + str(summary)[:300])
    owned = _called_once(rows, provider, command_id, tool)
    check(any(row.get("kind") == provider + OUTCOME_ROWS[provider] for row in owned), "The actual adapter did not receive the refusal")
    check(not any(frame.get("kind") in ("proposalCreated", "toolApprovalRequested") for frame in values), "A refused turn proposed a change")
    return summary[:400]


def require_edits_unoffered(frames, rows, *, provider, command_id, tool):
    """An edit request with only an inspected resource in context: the turn's read is offered, no mutating workflow tool is,
    and nothing is proposed. Returns the offered tools."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = _frames(frames)
    check(not any(frame.get("kind") in ("proposalCreated", "toolApprovalRequested") for frame in values), "An inspection turn proposed a change")
    offered = _offered(_owned(rows, provider, command_id), provider)
    check(tool in offered and not MUTATING_TOOLS & set(offered),
          "With only an inspected resource in context the adapter must be offered the read and no mutating workflow tool: " + json.dumps(offered))
    return offered


def require_admission_refused(response, text):
    """Refused at admission with the module's (or engine's) own text: no turn follows."""
    check(response.status == ADMISSION_REFUSAL["statusCode"], "The context was not refused at admission (HTTP " + str(response.status) + ")")
    value = response.json()
    error = value.get("error") if isinstance(value, dict) else None
    check(isinstance(error, dict) and error.get("code") == ADMISSION_REFUSAL["code"] and isinstance(error.get("message"), str)
          and text in error["message"], "The context was refused for another reason: " + json.dumps(error)[:300])
    return {"code": error["code"], "message": error["message"][:400]}


def require_unchanged(before, after, what):
    check(before["status"] == after["status"] == 200 and before["body"]["sha256"] == after["body"]["sha256"], what + " changed")
    return True


# ---- Native runner. ----

@contextmanager
def _bounded(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native inspection qualification requires an interruptible main-thread stage deadline")
    check(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report[SECTION]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native inspection stage expired without retry: " + name)
    handler = signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, CASE_SECONDS)
    try:
        yield
    except BaseException:
        stage["status"] = "failed"
        raise
    else:
        stage["status"] = "completed"
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, handler)
        stage["elapsedSeconds"] = time.monotonic() - started
        runtime.run.save()


def _session(transport, mode, autonomy):
    created = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": mode, "activeSurface": {"route": "/workflows/definitions"},
                                        "autonomyMode": autonomy}).require_status(200)
    return created, created.json()["data"]["sessionId"]


def _turn(runtime, name, http, attachment, tool, arguments, capability, *, mode="explain", autonomy="auto-read-only"):
    capabilities = list(capability) if isinstance(capability, (list, tuple)) else [capability]
    """One native turn whose adapter calls exactly this tool with exactly these arguments, in a fresh session."""
    transport = runtime.transport(name, http=http)
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    created, session = _session(transport, mode, autonomy)
    command_id = str(uuid.uuid4())
    command = select_command(transport, command_id, tool, arguments)
    message = transport.message(session, {"content": "Owned workflow inspection qualification request", "mode": mode, "requestId": str(uuid.uuid4()),
        "requestedCapabilities": capabilities, "contextAttachments": [attachment]}).require_status(200)
    stream = transport.collect_turn(session)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the " + name + " turn")
    return {"commandId": command_id, "frames": [frame.value for frame in stream.frames], "rows": trace["records"], "accepted": message.json(),
            "evidence": [created.receipt, command, message.receipt, stream.receipt, trace["receipt"]]}


def _refused_at_admission(runtime, name, http, attachment, capability, text):
    """A message whose context is refused at admission: no stream is collected, because no turn starts."""
    transport = runtime.transport(name, http=http)
    created, session = _session(transport, "explain", "auto-read-only")
    refused = transport.message(session, {"content": "Owned workflow inspection negative control", "mode": "explain", "requestId": str(uuid.uuid4()),
        "requestedCapabilities": [capability] if capability else [], "contextAttachments": [attachment]})
    return require_admission_refused(refused, text), [created.receipt, refused.receipt]


def _edit_refused(runtime, name, attachment, tool, capability, read, owner_paths):
    """An edit request while only this inspected resource is in context, in build mode and full-auto: the engine admits no
    mutating workflow tool for that target, so the adapter is offered only the read, which completes; nothing is proposed and
    every owner read is unchanged."""
    before = [runtime.native.request(runtime.backend, name + "-owner-before-" + str(index), path)[0] for index, path in enumerate(owner_paths)]
    turn = _turn(runtime, name, runtime.backend, attachment, tool, {}, [CHANGE_CAPABILITY, capability], mode="build", autonomy="full-auto")
    record = read(turn)
    offered = require_edits_unoffered(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=tool)
    after = [runtime.native.request(runtime.backend, name + "-owner-after-" + str(index), path)[0] for index, path in enumerate(owner_paths)]
    for first, second in zip(before, after): require_unchanged(first, second, "An inspected resource or its draft")
    return {"control": "edit-refused", "read": record, "offered": offered, "unchanged": True}, [*turn["evidence"], *[item["receipt"] for item in before + after]]


def _without_feature(runtime, name, feature, control):
    """Run control with one Weaver feature deselected in the packaged host, then select it again."""
    catalog, ref = runtime.native.json(runtime.backend, name + "-features", "/modularity/features")
    original = next(item for item in catalog["features"] if item["id"] == feature)
    check(original["enabled"] is True, "The packaged host must select " + feature + " before its absence control")
    evidence = [ref["receipt"], *runtime.apply_features(name + "-deselect", {feature: (False, original["configuration"])})]
    try:
        record, refs = control()
        evidence += refs
    finally:
        evidence += runtime.apply_features(name + "-reselect", {feature: (True, original["configuration"])})
    return record, evidence


def _version_native(runtime, name, seed, other):
    records, evidence = {}, []
    v1, v2, v3 = seed["versions"]
    versions = seed["list"]
    attachment = version_attachment(seed["definitionId"], v2["id"], v2["version"])
    check_v2 = lambda item: require_version_context(item, attachment, v2, versions)
    turn = _turn(runtime, name + "-explain", runtime.backend, attachment, VERSION_TOOL, {}, VERSION_CAPABILITY)
    records["explain"] = {"control": "explain", **require_version_read(turn["frames"], turn["rows"], turn["accepted"], provider=runtime.provider,
                          command_id=turn["commandId"], attachment=attachment, version=v2, versions=versions, canary=seed.get("valueCanary"))}
    evidence += turn["evidence"]
    attachment = version_attachment(seed["definitionId"], v2["id"], v2["version"])
    turn = _turn(runtime, name + "-compare", runtime.backend, attachment, VERSION_TOOL, {"compareToVersionId": v1["id"]}, VERSION_CAPABILITY)
    records["compare"] = {"control": "compare", **require_version_read(turn["frames"], turn["rows"], turn["accepted"], provider=runtime.provider,
                          command_id=turn["commandId"], attachment=attachment, version=v2, versions=versions, compared=v1, canary=seed.get("valueCanary"))}
    evidence += turn["evidence"]
    # A version is immutable: with only the version in context no mutating workflow tool is admitted, so nothing can change.
    version_path = owner_path(runtime.backend, seed["versionPath"], versionId=v3["id"])
    attachment = version_attachment(seed["definitionId"], v3["id"], v3["version"])
    record, refs = _edit_refused(runtime, name + "-edit-refused", attachment, VERSION_TOOL, VERSION_CAPABILITY,
                                 lambda turn: require_version_read(turn["frames"], turn["rows"], turn["accepted"], provider=runtime.provider,
                                                                   command_id=turn["commandId"], attachment=attachment, version=v3, versions=versions,
                                                                   canary=seed.get("valueCanary")),
                                 [version_path, seed["definitionPath"]])
    records["edit-refused"], evidence = record, evidence + refs
    # Unavailable at admission: an unknown version, and another definition's version named under this definition.
    for control, version_id in (("unknown-version", "qualification-missing-version-" + secrets.token_hex(6)), ("other-definition", other["versions"][0]["id"])):
        refusal, refs = _refused_at_admission(runtime, name + "-" + control, runtime.backend, version_attachment(seed["definitionId"], version_id),
                                              VERSION_CAPABILITY, TEXTS["version-not-found"])
        records[control] = {"control": control, **refusal}
        evidence += refs
    # A comparison against another definition's version is refused at preparation with the same unavailable text.
    attachment = version_attachment(seed["definitionId"], v2["id"], v2["version"])
    turn = _turn(runtime, name + "-compare-other-definition", runtime.backend, attachment, VERSION_TOOL, {"compareToVersionId": other["versions"][0]["id"]},
                 VERSION_CAPABILITY)
    records["compare-other-definition"] = {"control": "compare-other-definition", "refusal": require_tool_refused(
        turn["frames"], turn["rows"], turn["accepted"], provider=runtime.provider, command_id=turn["commandId"], tool=VERSION_TOOL, kind=VERSION_KIND,
        check_item=check_v2, text=TEXTS["version-not-found"])}
    evidence += turn["evidence"]
    # An actor without design read.
    denied, _ = runtime.sign_in(DESIGN_DENIED)
    refusal, refs = _refused_at_admission(runtime, name + "-design-denied", denied, version_attachment(seed["definitionId"], v2["id"], v2["version"]),
                                          VERSION_CAPABILITY, TEXTS["version-denied"])
    records["design-denied"], evidence = {"control": "design-denied", **refusal}, evidence + refs
    runtime.sign_in()
    # Without the Design-side Weaver feature, a version reference is explicitly unavailable. No capability is requested: the
    # absent module contributes none, and the context kind alone must be refused.
    def absent():
        value, refs = _refused_at_admission(runtime, name + "-design-weaver-absent", runtime.backend,
                                            version_attachment(seed["definitionId"], v2["id"], v2["version"]), None, TEXTS["kind-unsupported"])
        return {"control": "design-weaver-absent", **value}, refs
    record, refs = _without_feature(runtime, name + "-design-weaver", DESIGN_WEAVER_FEATURE, absent)
    records["design-weaver-absent"], evidence = record, evidence + refs
    return records, evidence


def _executable_native(runtime, name, seed):
    records, evidence = {}, []
    executable, publications = seed["executable"], seed["publications"]
    attachment = executable_attachment(seed["artifactId"], seed["artifactHash"], publications[0]["sourceReferenceId"])
    turn = _turn(runtime, name + "-explain", runtime.backend, attachment, EXECUTABLE_TOOL, {}, EXECUTABLE_CAPABILITY)
    records["explain"] = {"control": "explain", **require_executable_read(turn["frames"], turn["rows"], turn["accepted"], provider=runtime.provider,
                          command_id=turn["commandId"], attachment=attachment, executable=executable, publications=publications,
                          canary=seed.get("valueCanary"))}
    evidence += turn["evidence"]
    attachment = executable_attachment(seed["artifactId"], seed["artifactHash"])
    record, refs = _edit_refused(runtime, name + "-edit-refused", attachment, EXECUTABLE_TOOL, EXECUTABLE_CAPABILITY,
                                 lambda turn: require_executable_read(turn["frames"], turn["rows"], turn["accepted"], provider=runtime.provider,
                                                                      command_id=turn["commandId"], attachment=attachment, executable=executable,
                                                                      publications=publications, canary=seed.get("valueCanary")),
                                 [seed["executablePath"]])
    records["edit-refused"], evidence = record, evidence + refs
    for control, value, text in (("stale-hash", executable_attachment(seed["artifactId"], other_hash(seed["artifactHash"])), TEXTS["executable-stale"]),
                                 ("unknown-artifact", executable_attachment("qualification-missing-artifact-" + secrets.token_hex(6), seed["artifactHash"]),
                                  TEXTS["executable-not-found"])):
        refusal, refs = _refused_at_admission(runtime, name + "-" + control, runtime.backend, value, EXECUTABLE_CAPABILITY, text)
        records[control], evidence = {"control": control, **refusal}, evidence + refs
    denied, _ = runtime.sign_in(RUNTIME_DENIED)
    refusal, refs = _refused_at_admission(runtime, name + "-runtime-denied", denied, executable_attachment(seed["artifactId"], seed["artifactHash"]),
                                          EXECUTABLE_CAPABILITY, TEXTS["executable-denied"])
    records["runtime-denied"], evidence = {"control": "runtime-denied", **refusal}, evidence + refs
    runtime.sign_in()
    def absent():
        value, refs = _refused_at_admission(runtime, name + "-runtime-weaver-absent", runtime.backend,
                                            executable_attachment(seed["artifactId"], seed["artifactHash"]), None, TEXTS["kind-unsupported"])
        return {"control": "runtime-weaver-absent", **value}, refs
    record, refs = _without_feature(runtime, name + "-runtime-weaver", RUNTIME_WEAVER_FEATURE, absent)
    records["runtime-weaver-absent"], evidence = record, evidence + refs
    after, after_ref = runtime.native.json(runtime.backend, name + "-executable-after", seed["executablePath"])
    check(references_of(after["references"]) == references_of(executable["references"]) and after["artifactHash"] == executable["artifactHash"],
          "An inspection turn changed the Executable or its Source References")
    evidence.append(after_ref["receipt"])
    return records, evidence


def _browser(runtime, name, journey, workflow):
    _, identity = runtime.sign_in()
    root = runtime.output(name)
    output = root / "observation"
    config = {"journey": journey, "provider": runtime.provider, "studioOrigin": runtime.studio_origin, "backendOrigin": runtime.backend_origin,
              "storageState": identity["storageState"], "commandFile": str(runtime.command_file), "output": str(output), "workflow": workflow}
    private_json(root / "config-private.json", config)
    transport = runtime.transport(name + "-protocol")
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    node = shutil.which("node")
    check(node, "The actual browser driver needs Node")
    runtime.run.run(name, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                    env={key: value for key, value in os.environ.items() if key in RUNTIME_ENVIRONMENT})
    observation = output / "observation.json"
    observed = json.loads(observation.read_text())
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the " + journey + " journey")
    commands = [json.loads(path.read_text()) for path in sorted(output.glob("command-*.json"))]
    value = require_browser(observed, commands, trace["records"], provider=runtime.provider, journey=journey)
    evidence = [*observed["evidence"], observed["ownerBefore"], observed["ownerAfter"], {"path": str(observation), "sha256": sha(observation)},
                runtime.save(root, "browser-inputs", browser_inputs(config)), trace["receipt"]]
    return value, evidence


def require_browser(observed, commands, rows, *, provider, journey):
    """The page's own journey, checked against the adapter trace for every command the browser retained."""
    check(provider in PROVIDERS and journey in BROWSER, "Unknown adapter or journey")
    expected, steps = BROWSER[journey]
    check(isinstance(observed, dict) and observed.get("journey") == journey and observed.get("status") == "passed"
          and not observed.get("failure") and not observed.get("pageErrors") and observed.get("provider") == provider,
          "The " + journey + " Studio journey did not pass on this adapter")
    check(same(observed.get("observations"), expected), "The " + journey + " Studio journey observed another outcome")
    check(isinstance(observed.get("ownerBefore"), dict) and isinstance(observed.get("ownerAfter"), dict)
          and isinstance(observed.get("evidence"), list) and observed["evidence"], "The " + journey + " journey retained no owner evidence")
    selected = observed.get("commands")
    check(isinstance(selected, dict) and sorted(selected) == sorted(steps) and len(set(selected.values())) == len(steps),
          "The " + journey + " journey must retain one distinct command per step")
    by_id = {}
    for item in commands:
        check(isinstance(item, dict) and isinstance(item.get("commandId"), str) and item["commandId"] not in by_id
              and isinstance(item.get("arguments"), dict), "Each retained browser command must be one distinct request")
        by_id[item["commandId"]] = item
    check(set(by_id) == set(selected.values()), "The retained commands differ from the journey's steps")
    for step, command_id in selected.items():
        tool = steps[step]
        check(by_id[command_id].get("toolName") == tool, "The " + step + " step commanded another tool")
        _called_once(rows, provider, command_id, tool)
    return {"journey": journey, "observations": observed["observations"], "commands": selected,
            "ownerBefore": observed["ownerBefore"], "ownerAfter": observed["ownerAfter"]}


def _seed_other(runtime, name):
    """Another definition with its own promoted versions, never published: the target of the other-definition controls."""
    return seed_versioned_workflow(runtime.backend, runtime.output(name) / "workflow-seed", publish=False)


def _version_case(runtime, provider):
    name = provider + "-workflow-versions"
    evidence = list(runtime.select_provider(provider))
    runtime.sign_in()
    seed = seed_versioned_workflow(runtime.backend, runtime.output(name + "-workflow") / "workflow-seed")
    other = _seed_other(runtime, name + "-other")
    evidence += [*seed["evidence"], *other["evidence"]]
    native, native_evidence = _version_native(runtime, name, seed, other)
    evidence += native_evidence
    v1, v2, _ = seed["versions"]
    workflow = {"definitionId": seed["definitionId"], "definitionPath": seed["definitionPath"], "draftPath": seed["draftPath"],
                "version": {"id": v2["id"], "version": v2["version"], "compareToVersionId": v1["id"],
                            "path": owner_path(runtime.backend, seed["versionPath"], versionId=v2["id"])}}
    browser, browser_evidence = _browser(runtime, name + "-browser", VERSION_BROWSER, workflow)
    evidence += browser_evidence
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "native": native, "browser": browser, "evidence": evidence}


def _executable_case(runtime, provider):
    name = provider + "-workflow-executables"
    evidence = list(runtime.select_provider(provider))
    runtime.sign_in()
    seed = seed_versioned_workflow(runtime.backend, runtime.output(name + "-workflow") / "workflow-seed")
    evidence += seed["evidence"]
    native, native_evidence = _executable_native(runtime, name, seed)
    evidence += native_evidence
    workflow = {"executable": {"artifactId": seed["artifactId"], "artifactHash": seed["artifactHash"], "path": seed["executablePath"]}}
    browser, browser_evidence = _browser(runtime, name + "-browser", EXECUTABLE_BROWSER, workflow)
    evidence += browser_evidence
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "native": native, "browser": browser, "evidence": evidence}


def qualify_workflow_inspection(runtime):
    run = runtime.run
    check(SECTION not in run.report, "Never resume or overwrite a prior workflow inspection qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "source": run.source,
               "caseIDs": [], "records": [], "stages": [],
               "driver": {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/inspection_journeys.py", "sha256": sha(TOOL / "inspection_journeys.py")}}
    run.report[SECTION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            for suffix, case in (("-workflow-versions", _version_case), ("-workflow-executables", _executable_case)):
                name = provider + suffix
                section["activeCase"] = name
                with _bounded(runtime, name):
                    value = case(runtime, provider)
                    for ref in value["evidence"]:
                        path = Path(ref["path"])
                        check(path.resolve().is_relative_to(run.root.resolve()) and path.is_file() and sha(path) == ref["sha256"],
                              "Workflow inspection evidence is missing, changed or outside the owned run")
                    record = runtime.save(runtime.output(name + "-record"), "observation", value)
                    section["caseIDs"].append(name)
                    section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == len(EXPECTED_CASES),
              "Each workflow inspection case must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Workflow inspection qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()


def inspection_manifest_gaps(section, source=None):
    """Structural reasons a section cannot complete a witness; reads no files."""
    if not isinstance(section, dict):
        return ["The mandatory workflow inspection section is absent"]
    gaps = []
    names = section.get("caseIDs") if isinstance(section.get("caseIDs"), list) else []
    records = section.get("records") if isinstance(section.get("records"), list) else []
    if section.get("status") != "passed":
        gaps.append("Workflow inspection section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported workflow inspection provider evidence claim")
    if source is not None and section.get("source") != source:
        gaps.append("Workflow inspection section source differs from the witness source")
    if (sorted(names) != sorted(EXPECTED_CASES) or len(names) != len(EXPECTED_CASES)
            or [row.get("id") if isinstance(row, dict) else None for row in records] != names
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The workflow inspection cases were not each observed passed exactly once")
    return gaps


def require_workflow_inspection(section, owned_root=None, *, source=None):
    """Mandatory final gate: both adapters passed both cases, and each retained record still holds every native control and
    journey, including the comparison and both deployment-shape controls."""
    gaps = inspection_manifest_gaps(section, source)
    check(not gaps, "The workflow inspection inventory is mandatory on each actual adapter: " + "; ".join(gaps))
    if owned_root is not None:
        root = Path(owned_root).resolve()
        for row in section["records"]:
            path = Path(row["record"]["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file() and sha(path) == row["record"]["sha256"],
                  "Workflow inspection record is missing, changed or outside the owned run: " + row["id"])
            observed = json.loads(path.read_text())
            native = observed.get("native") if isinstance(observed.get("native"), dict) else {}
            versions = row["id"].endswith("-workflow-versions")
            controls = VERSION_NATIVE if versions else EXECUTABLE_NATIVE
            shape = "design-weaver-absent" if versions else "runtime-weaver-absent"
            check(observed.get("id") == row["id"] and observed.get("status") == "passed" and observed.get("adapter") == row["id"].split("-", 1)[0]
                  and sorted(native) == sorted(controls) and all((native.get(control) or {}).get("control") == control for control in controls)
                  and (native.get("edit-refused") or {}).get("unchanged") is True
                  and isinstance((native.get("edit-refused") or {}).get("offered"), list)
                  and not MUTATING_TOOLS & set(native["edit-refused"]["offered"])
                  and (native.get(shape) or {}).get("code") == ADMISSION_REFUSAL["code"]
                  and (not versions or isinstance((native.get("compare") or {}).get("comparison"), dict))
                  and same((observed.get("browser") or {}).get("observations"), VERSION_OBSERVATIONS if versions else EXECUTABLE_OBSERVATIONS),
                  "Workflow inspection record differs: " + row["id"])
    return section["records"]


def synthetic_native(case):
    """The minimal native record shape the final gate checks; for synthetic self-tests only, never native evidence."""
    versions = case.endswith("-workflow-versions")
    value = {control: {"control": control} for control in (VERSION_NATIVE if versions else EXECUTABLE_NATIVE)}
    value["edit-refused"].update(unchanged=True, offered=[VERSION_TOOL if versions else EXECUTABLE_TOOL])
    value["design-weaver-absent" if versions else "runtime-weaver-absent"]["code"] = ADMISSION_REFUSAL["code"]
    if versions: value["compare"]["comparison"] = {"added": ["c"]}
    return value


def synthetic_browser(case):
    """The minimal browser shape the final gate checks; for synthetic self-tests only."""
    versions = case.endswith("-workflow-versions")
    return {"journey": VERSION_BROWSER if versions else EXECUTABLE_BROWSER,
            "observations": copy.deepcopy(VERSION_OBSERVATIONS if versions else EXECUTABLE_OBSERVATIONS)}
