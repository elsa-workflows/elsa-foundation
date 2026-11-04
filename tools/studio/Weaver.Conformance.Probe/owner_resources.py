"""Native, explicitly owned resources for the witness; no shadow store or validation engine."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import time
from urllib.parse import quote, urljoin, urlparse
import uuid

from artifacts import REPO, TOOL, check, sha
from runtime import private_json


def _retain(root, name, value):
    path = Path(root) / (name + ".json")
    private_json(path, value)
    return {"path": str(path), "sha256": sha(path)}


def _new_output(output):
    output = Path(output)
    check(output.is_absolute() and not output.resolve().is_relative_to(REPO), "Owner receipts need a new external output")
    output.mkdir(parents=True, exist_ok=False, mode=0o700)
    return output


def capability_links(http, capability_id, output):
    document, _ = http.json("/capabilities")
    receipt = _retain(output, "capabilities", document)
    selected = [item for item in document["capabilities"] if item["id"] == capability_id]
    check(bool(selected) and all(str(item["contractVersion"]).split(".")[0] == "1" for item in selected),
          "The owning backend does not advertise the supported capability: " + capability_id)
    links = {}
    for item in selected:
        for link in item["links"]:
            check(link["rel"] not in links or links[link["rel"]] == link["href"], "Ambiguous owning capability relation")
            links[link["rel"]] = link["href"]
    return links, receipt


def owner_path(http, href, **identities):
    for name, identity in identities.items():
        href = href.replace("{" + name + "}", quote(identity, safe=""))
    check("{" not in href and "}" not in href, "Unresolved owning resource link")
    url = urlparse(urljoin(http.base + "/", href))
    check(url.scheme + "://" + url.netloc == http.base, "Owning capability link escaped the verified backend")
    return url.path + ("?" + url.query if url.query else "")


def seed_workflow(http, output):
    """Create a real flowchart draft through its advertised API, retaining immutable CLR IDs."""
    output = _new_output(output)
    activities, activity_capability = capability_links(http, "elsa.api.activity-design", output / "activities")
    workflows, workflow_capability = capability_links(http, "elsa.api.workflow-design", output / "workflows")
    check(all(key in workflows for key in ("workflow-definitions", "workflow-drafts", "workflow-draft-validations")),
          "The native workflow witness needs the real create/save/validation relations")
    catalog, _ = http.json(owner_path(http, activities["activity-catalog"]))
    catalog_receipt = _retain(output, "activity-catalog", catalog)
    selected = {}
    for name in ("Flowchart", "WriteLine"):
        expected = "Elsa.Activities." + ("Flowchart" if name == "Flowchart" else "Primitives") + ".Activities." + name
        matches = [item for item in catalog["activities"] if item["activityTypeKey"] == expected and item.get("available") is True]
        check(len(matches) == 1 and bool(matches[0]["activityVersionId"]), "The actual catalog must uniquely supply " + expected)
        selected[name] = matches[0]
    check(any(item["referenceKey"] == "text" for item in selected["WriteLine"]["inputs"]), "Actual WriteLine contract has no text input")
    node_id = "witness-write"
    request = {
        "operationKey": str(uuid.uuid4()), "name": "Weaver package witness " + uuid.uuid4().hex[:8],
        "description": "Owned #3438 layout qualification; no runtime dispatch or publication.",
        "initialState": {"variables": [], "inputs": [], "outputs": [], "rootActivity": {
            "nodeId": "witness-flow", "activityVersionId": selected["Flowchart"]["activityVersionId"], "inputs": [], "outputs": [],
            "structure": {"kind": "elsa.flowchart.structure", "schemaVersion": "1.0.0", "payload": {
                "activities": [{"nodeId": node_id, "activityVersionId": selected["WriteLine"]["activityVersionId"],
                                "inputs": [{"referenceKey": "text", "value": {"value": "Weaver package witness", "expressionType": "Literal"}}],
                                "outputs": [], "structure": None}],
                "connections": [], "startNodeId": node_id, "nodeMetadata": {}, "connectionMetadata": {}, "variables": []}}}},
        "layout": [{"nodeId": node_id, "x": 120, "y": 160, "width": 260, "height": 140,
                    "additionalProperties": {"witness": {"preserve": "authored-layout-metadata"}}}],
        "activityPresentation": []}
    request_receipt = _retain(output, "create-request", request)
    definitions_path = owner_path(http, workflows["workflow-definitions"])
    created, _ = http.json(definitions_path, request, method="POST")
    create_receipt = _retain(output, "created", created)
    definition_id, draft_id = created["definition"]["id"], created["draft"]["id"]
    check(created["draft"]["definitionId"] == definition_id and bool(draft_id), "Native create returned inconsistent owning identities")
    definition_path = definitions_path.rstrip("/") + "/" + quote(definition_id, safe="")
    observed, _ = http.json(definition_path)
    check(observed["definition"]["id"] == definition_id and observed["draft"]["id"] == draft_id,
          "Independent workflow owner readback did not confirm creation")
    observed_receipt = _retain(output, "owner-readback", observed)
    validations, _ = http.json(owner_path(http, workflows["workflow-draft-validations"], draftId=draft_id))
    validation_receipt = _retain(output, "validation-readback", validations)
    check(validations["draftId"] == draft_id and validations["errors"] == [], "The native workflow seed failed ordinary validation")
    return {"definitionId": definition_id, "activityId": node_id, "definitionPath": definition_path,
            "draftId": draft_id, "draftPath": owner_path(http, workflows["workflow-drafts"], draftId=draft_id),
            "validationPath": owner_path(http, workflows["workflow-draft-validations"], draftId=draft_id),
            "evidence": [activity_capability, workflow_capability, catalog_receipt, request_receipt, create_receipt, observed_receipt, validation_receipt]}


# Spec 1193 slice C (T022). The packaged consumer's catalog has one container activity, the Flowchart, so the nested
# scope an Operation Batch addresses is a flowchart inside the root flowchart. A catalog entry describes its container
# as `containerStructure` (mode, slots, initialPayload) and its `authoringTemplate.structure` carries the structure kind;
# Studio's designer builds a new node's structure from exactly those (workflowAdapter.createStructureForActivity), and
# each slot's id is "<kind>:<property>" (workflowAdapter.getFacetChildSlots).
BATCH_FLOWCHART = "Elsa.Activities.Flowchart.Activities.Flowchart"
BATCH_WRITE_LINE = "Elsa.Activities.Primitives.Activities.WriteLine"
BATCH_NODES = {"root": "batch-flow", "a": "batch-a", "b": "batch-b", "nested": "batch-nested", "inner": "batch-inner"}


def _container(entry):
    """The container shape Studio derives from a catalog entry: kind, mode, the one slot's id and the initial payload."""
    structure = entry.get("containerStructure")
    kind = ((entry.get("authoringTemplate") or {}).get("structure") or {}).get("kind")
    check(isinstance(structure, dict) and isinstance(kind, str) and kind and isinstance(structure.get("slots"), list) and len(structure["slots"]) == 1
          and isinstance(structure.get("initialPayload"), dict) and isinstance(structure["initialPayload"].get("activities"), list),
          "The actual " + entry["activityTypeKey"] + " entry does not describe one authorable slot")
    slot = structure["slots"][0]
    check(slot.get("cardinality") == "many" and isinstance(slot.get("property"), str), "The container slot is not a many-activities slot")
    return {"kind": kind, "mode": structure.get("mode"), "slot": kind + ":" + slot["property"], "payload": structure["initialPayload"]}


# Spec 1193 slice D (T028): the explanation draft is the same nested shape, with b's text bound to a workflow variable
# that does not exist, so the owner's own draft validation reports it by activity and input.
EXPLAIN_MISSING_VARIABLE = "qualification-missing-variable"
UNRESOLVED_VARIABLE = "Expressions/UnresolvedVariable"
# It also holds one activity of the QualificationBatchCatalog seam's password-hinted activity, with no value: the browser
# journey types a credential canary into that masked input as an unsaved edit, so the canary only ever exists in Studio's
# working state. The seam's entries are not persisted versions, so the owner's validation also reports it as unknown.
PASSWORD_ACTIVITY = {"activityVersionId": "qualification-password-activity-v1", "activityTypeKey": "Qualification.PasswordActivity",
                     "input": "passphrase", "label": "Qualification passphrase", "nodeId": "batch-masked"}


def seed_batch_workflow(http, output, *, explain=False):
    """Create the real nested draft the Operation Batch journeys address: a root flowchart holding two Write Lines and a
    nested flowchart that holds a third, connected a -> b and a -> nested. Retains the catalog entries and slot identities.
    With explain (the explanation journeys) b's text is a Variable expression naming a variable the workflow does not
    declare, and the owner's validation must report exactly that input, and the root also holds the seam's password-hinted
    activity with no value; the batch journeys require a clean draft."""
    output = _new_output(output)
    activities, activity_capability = capability_links(http, "elsa.api.activity-design", output / "activities")
    workflows, workflow_capability = capability_links(http, "elsa.api.workflow-design", output / "workflows")
    check(all(key in workflows for key in ("workflow-definitions", "workflow-drafts", "workflow-draft-validations")),
          "The native batch witness needs the real create/save/validation relations")
    catalog, _ = http.json(owner_path(http, activities["activity-catalog"]))
    catalog_receipt = _retain(output, "activity-catalog", catalog)
    def entry(type_key):
        matches = [item for item in catalog["activities"] if item["activityTypeKey"] == type_key and item.get("available") is True]
        check(len(matches) == 1 and bool(matches[0]["activityVersionId"]), "The actual catalog must uniquely supply " + type_key)
        return matches[0]
    write, flowchart = entry(BATCH_WRITE_LINE), entry(BATCH_FLOWCHART)
    check(any(item["referenceKey"] == "text" for item in write["inputs"]), "Actual WriteLine contract has no text input")
    container = _container(flowchart)
    check(container["kind"] == "elsa.flowchart.structure" and container["mode"] == "flowchart", "The actual Flowchart is not a flowchart container")
    def leaf(node_id, text):
        return {"nodeId": node_id, "activityVersionId": write["activityVersionId"], "outputs": [], "structure": None,
                "inputs": [{"referenceKey": "text", "value": {"value": text, "expressionType": "Literal"}}]}
    def flow(node_id, children, connections, start):
        payload = json.loads(json.dumps(container["payload"]))
        payload.update(activities=children, connections=connections, startNodeId=start)
        return {"nodeId": node_id, "activityVersionId": flowchart["activityVersionId"], "inputs": [], "outputs": [],
                "structure": {"kind": container["kind"], "schemaVersion": "1.0.0", "payload": payload}}
    n = BATCH_NODES
    nested = flow(n["nested"], [leaf(n["inner"], "Inside the nested flowchart")], [], n["inner"])
    second = leaf(n["b"], "Second")
    if explain:
        second["inputs"] = [{"referenceKey": "text", "value": {"value": {"referenceKey": EXPLAIN_MISSING_VARIABLE, "declaringScopeId": "workflow"},
                                                               "expressionType": "Variable"}}]
    masked = [{"nodeId": PASSWORD_ACTIVITY["nodeId"], "activityVersionId": PASSWORD_ACTIVITY["activityVersionId"], "inputs": [], "outputs": [],
               "structure": None}] if explain else []
    root = flow(n["root"], [leaf(n["a"], "First"), second, nested, *masked],
                [{"id": "batch-a-b", "source": {"nodeId": n["a"], "port": "Done"}, "target": {"nodeId": n["b"]}},
                 {"id": "batch-a-nested", "source": {"nodeId": n["a"], "port": "Done"}, "target": {"nodeId": n["nested"]}}], n["a"])
    request = {
        "operationKey": str(uuid.uuid4()), "name": ("Weaver explain witness " if explain else "Weaver batch witness ") + uuid.uuid4().hex[:8],
        "description": ("Owned spec 1193 explanation qualification" if explain else "Owned spec 1193 Operation Batch qualification")
                       + "; no runtime dispatch or publication.",
        "initialState": {"variables": [], "inputs": [], "outputs": [], "rootActivity": root},
        "layout": [{"nodeId": n["a"], "x": 120, "y": 160}, {"nodeId": n["b"], "x": 420, "y": 160}, {"nodeId": n["nested"], "x": 120, "y": 420},
                   {"nodeId": n["inner"], "x": 80, "y": 80}, *([{"nodeId": PASSWORD_ACTIVITY["nodeId"], "x": 720, "y": 160}] if explain else [])],
        "activityPresentation": []}
    request_receipt = _retain(output, "create-request", request)
    definitions_path = owner_path(http, workflows["workflow-definitions"])
    created, _ = http.json(definitions_path, request, method="POST")
    create_receipt = _retain(output, "created", created)
    definition_id, draft_id = created["definition"]["id"], created["draft"]["id"]
    check(created["draft"]["definitionId"] == definition_id and bool(draft_id), "Native create returned inconsistent owning identities")
    definition_path = definitions_path.rstrip("/") + "/" + quote(definition_id, safe="")
    observed, _ = http.json(definition_path)
    check(observed["definition"]["id"] == definition_id and observed["draft"]["id"] == draft_id,
          "Independent workflow owner readback did not confirm creation")
    observed_receipt = _retain(output, "owner-readback", observed)
    validations, _ = http.json(owner_path(http, workflows["workflow-draft-validations"], draftId=draft_id))
    validation_receipt = _retain(output, "validation-readback", validations)
    if explain:
        check(validations["draftId"] == draft_id and any(item.get("nodeId") == n["b"] and item.get("member") == "inputs/text"
                                                         and item.get("code") == UNRESOLVED_VARIABLE for item in validations["errors"]),
              "The owner's validation does not report the explanation seed's unresolved variable on b's text")
    else:
        check(validations["draftId"] == draft_id and validations["errors"] == [], "The native batch seed failed ordinary validation")
    return {"definitionId": definition_id, "draftId": draft_id, "definitionPath": definition_path, "validations": validations["errors"],
            "draftPath": owner_path(http, workflows["workflow-drafts"], draftId=draft_id),
            "validationPath": owner_path(http, workflows["workflow-draft-validations"], draftId=draft_id),
            "nodes": {**n, **({"masked": PASSWORD_ACTIVITY["nodeId"]} if explain else {})}, "writeLine": {"activityTypeKey": write["activityTypeKey"], "version": write["version"],
                                            "activityVersionId": write["activityVersionId"], "displayName": write["displayName"]},
            "flowchart": {"activityTypeKey": flowchart["activityTypeKey"], "version": flowchart["version"], "activityVersionId": flowchart["activityVersionId"],
                          "displayName": flowchart["displayName"], "slot": container["slot"], "mode": container["mode"]},
            "evidence": [activity_capability, workflow_capability, catalog_receipt, request_receipt, create_receipt, observed_receipt, validation_receipt]}


def seed_personal_layout(http, output):
    """Create an explicit personal document only when absent; never overwrite an existing resource."""
    output = _new_output(output)
    witness = json.loads((TOOL / "witness.json").read_text())
    endpoint = "/_elsa/studio/preferences/home-layout"
    headers = {"X-Elsa-Studio-Host-Id": witness["hostId"]}
    status, body, _ = http.request(endpoint, headers=headers)
    check(status == 404, "The fresh owned witness needs an absent personal document; refusing to overwrite it")
    request = {"schemaVersion": 1, "value": {
        "witnessUnrelatedRoot": "UNRELATED_ROOT_" + uuid.uuid4().hex,
        "widgets": [{"id": witness["widgetId"], "visible": False, "size": "small",
                     "options": {"privateCanary": "PRIVATE_OWN_OPTIONS_" + uuid.uuid4().hex},
                     "witnessUnknownEntry": "UNRELATED_ENTRY_" + uuid.uuid4().hex},
                    {"id": "acme.other:untouched", "visible": True, "size": "medium",
                     "options": {"privateCanary": "PRIVATE_OTHER_OPTIONS_" + uuid.uuid4().hex}}]}}
    request_ref = _retain(output, "create-request", request)
    saved, _ = http.json(endpoint, request, method="PUT", headers={**headers, "If-None-Match": "*"})
    saved_ref = _retain(output, "created", saved)
    observed, _ = http.json(endpoint, headers=headers)
    check(observed == saved and saved["value"] == request["value"] and bool(saved["revision"]), "The native personal owner did not confirm the exact document")
    return {"document": observed, "evidence": [request_ref, saved_ref, _retain(output, "owner-readback", observed)]}


# Spec 1193 slice F (T041). One definition with three persisted versions, each promoted from the draft through the owning
# design API's own promote relation after an ordinary draft replacement, and the second one published twice through the
# publishing API (its default slot, then side by side in a named slot), so its content-addressed Executable carries two
# Source References. The versions are flowcharts of Write Lines only:
#   v1: a -> b
#   v2: a -> b -> c, with b's text changed (published)
#   v3: a -> c (b removed)
VERSION_NODES = {"root": "versions-flow", "a": "versions-a", "b": "versions-b", "c": "versions-c"}
SIDE_BY_SIDE_SLOT = "qualification-side"


def version_states(write, flowchart, canary=""):
    """The three authored states, in the wire shape the design API accepts (and Studio saves). Every authored text carries the
    canary, so a disclosed input value is detectable wherever it would appear."""
    container = _container(flowchart)
    n = VERSION_NODES
    def leaf(node_id, text):
        return {"nodeId": node_id, "activityVersionId": write["activityVersionId"], "outputs": [], "structure": None,
                "inputs": [{"referenceKey": "text", "value": {"value": (text + " " + canary).strip(), "expressionType": "Literal"}}]}
    def connection(source, target):
        return {"id": source + "-" + target, "source": {"nodeId": source, "port": "Done"}, "target": {"nodeId": target}}
    def state(children, connections):
        payload = json.loads(json.dumps(container["payload"]))
        payload.update(activities=children, connections=connections, startNodeId=n["a"])
        root = {"nodeId": n["root"], "activityVersionId": flowchart["activityVersionId"], "inputs": [], "outputs": [],
                "structure": {"kind": container["kind"], "schemaVersion": "1.0.0", "payload": payload}}
        return {"variables": [], "inputs": [], "outputs": [], "rootActivity": root}
    return [state([leaf(n["a"], "First"), leaf(n["b"], "Second")], [connection(n["a"], n["b"])]),
            state([leaf(n["a"], "First"), leaf(n["b"], "Second, revised"), leaf(n["c"], "Third")],
                  [connection(n["a"], n["b"]), connection(n["b"], n["c"])]),
            state([leaf(n["a"], "First"), leaf(n["c"], "Third")], [connection(n["a"], n["c"])])]


def seed_versioned_workflow(http, output, *, publish=True):
    """Create the definition, promote three versions, and (with publish) publish the second twice. Retains every read."""
    output = _new_output(output)
    activities, activity_capability = capability_links(http, "elsa.api.activity-design", output / "activities")
    workflows, workflow_capability = capability_links(http, "elsa.api.workflow-design", output / "workflows")
    check(all(key in workflows for key in ("workflow-definitions", "workflow-drafts", "workflow-draft-promote-exact-version", "workflow-versions")),
          "The native version witness needs the real create, draft, promote and version relations")
    evidence = [activity_capability, workflow_capability]
    catalog, _ = http.json(owner_path(http, activities["activity-catalog"]))
    evidence.append(_retain(output, "activity-catalog", catalog))
    def entry(type_key):
        matches = [item for item in catalog["activities"] if item["activityTypeKey"] == type_key and item.get("available") is True]
        check(len(matches) == 1 and bool(matches[0]["activityVersionId"]), "The actual catalog must uniquely supply " + type_key)
        return matches[0]
    write, flowchart = entry(BATCH_WRITE_LINE), entry(BATCH_FLOWCHART)
    canary = "vcanary-" + uuid.uuid4().hex[:16]
    states = version_states(write, flowchart, canary)
    request = {"operationKey": str(uuid.uuid4()), "name": "Weaver versions witness " + uuid.uuid4().hex[:8],
               "description": "Owned spec 1193 version and Executable qualification.", "initialState": states[0],
               "layout": [{"nodeId": node, "x": 120 + 300 * index, "y": 160} for index, node in enumerate(("versions-a", "versions-b", "versions-c"))],
               "activityPresentation": []}
    evidence.append(_retain(output, "create-request", request))
    definitions_path = owner_path(http, workflows["workflow-definitions"])
    created, _ = http.json(definitions_path, request, method="POST")
    evidence.append(_retain(output, "created", created))
    definition_id, draft_id = created["definition"]["id"], created["draft"]["id"]
    check(created["draft"]["definitionId"] == definition_id and bool(draft_id), "Native create returned inconsistent owning identities")
    definition_path = definitions_path.rstrip("/") + "/" + quote(definition_id, safe="")
    draft_path = owner_path(http, workflows["workflow-drafts"], draftId=draft_id)
    promote_path = owner_path(http, workflows["workflow-draft-promote-exact-version"], draftId=draft_id)
    versions = []
    for index, state in enumerate(states):
        if index:
            replaced, _ = http.json(draft_path, {"operationKey": str(uuid.uuid4()), "state": state, "layout": request["layout"], "activityPresentation": []},
                                    method="PUT")
            evidence.append(_retain(output, "draft-" + str(index + 1), replaced))
        promoted, _ = http.json(promote_path, {"operationKey": str(uuid.uuid4())}, method="POST", expected=201)
        evidence.append(_retain(output, "promoted-" + str(index + 1), promoted))
        check(isinstance(promoted.get("id"), str) and promoted["id"] and promoted["definition"]["id"] == definition_id
              and promoted["id"] not in [item["id"] for item in versions], "The owner did not promote a new version of the seeded definition")
        observed, _ = http.json(owner_path(http, workflows["workflow-versions"], versionId=promoted["id"]))
        evidence.append(_retain(output, "version-" + str(index + 1), observed))
        check(observed["id"] == promoted["id"] and observed["version"] == promoted["version"], "The version API does not read the promoted version back")
        versions.append(observed)
    listed, _ = http.json(definition_path + "/versions")
    evidence.append(_retain(output, "version-list", listed))
    check(sorted(item["id"] for item in listed) == sorted(item["id"] for item in versions), "The version list differs from the promoted versions")
    seed = {"definitionId": definition_id, "draftId": draft_id, "definitionPath": definition_path, "draftPath": draft_path,
            "versionPath": workflows["workflow-versions"], "versions": versions, "list": listed, "nodes": dict(VERSION_NODES),
            "writeLine": {"activityVersionId": write["activityVersionId"], "activityTypeKey": write["activityTypeKey"]},
            "flowchart": {"activityVersionId": flowchart["activityVersionId"], "activityTypeKey": flowchart["activityTypeKey"]},
            "valueCanary": canary, "evidence": evidence}
    if publish:
        publishing, publishing_capability = capability_links(http, "elsa.api.publishing", output / "publishing")
        runtime, runtime_capability = capability_links(http, "elsa.api.runtime", output / "runtime")
        check("workflow-publish" in publishing and "workflow-executable" in runtime, "The native Executable witness needs the publish and Executable relations")
        evidence += [publishing_capability, runtime_capability]
        published = versions[1]
        publish_path = owner_path(http, publishing["workflow-publish"], versionId=published["id"])
        publications = []
        for name, body in (("default", {}), ("side-by-side", {"action": "sideBySide", "slotName": SIDE_BY_SIDE_SLOT})):
            publication, _ = http.json(publish_path, body, method="POST", expected=201)
            evidence.append(_retain(output, "publication-" + name, publication))
            publications.append(publication)
        check(len({item["artifactId"] for item in publications}) == 1 and len({item["artifactHash"] for item in publications}) == 1
              and len({item["sourceReferenceId"] for item in publications}) == 2,
              "The two publications of one version must reference one content-addressed Executable through two Source References")
        artifact_id = publications[0]["artifactId"]
        executable_path = owner_path(http, runtime["workflow-executable"], artifactId=artifact_id)
        executable, _ = http.json(executable_path)
        evidence.append(_retain(output, "executable", executable))
        references = {item["sourceReferenceId"] for item in executable.get("references") or []}
        check(executable["artifactId"] == artifact_id and executable["artifactHash"] == publications[0]["artifactHash"]
              and {item["sourceReferenceId"] for item in publications} <= references, "The Executable API does not list both publications' Source References")
        seed.update(publishedVersionId=published["id"], publications=publications, artifactId=artifact_id, artifactHash=executable["artifactHash"],
                    executablePath=executable_path, executable=executable, executableLink=runtime["workflow-executable"])
    return seed


# Spec 1193 slice G (T045): one Run of a seeded workflow that faults. Its first activity is a Write Line whose text, a
# private canary, is authored sensitive (the run case selects the QualificationRunCapture seam first, so the runtime captures
# it into value evidence, which the run API never returns inline); its second is a Fault, which faults
# the Run with one blocking incident. A second, waiting Run (an Event that does not start the workflow, so its activity
# suspends on a bookmark while the Run stays Running) is still in progress while it is explained.
RUN_NODES = {"root": "run-flow", "sensitive": "run-sensitive", "fault": "run-fault", "wait": "run-wait"}
RUN_FAULT = {"code": "qualification.run.fault", "message": "Qualification Run fault."}
RUN_WAIT_EVENT = "qualification-run-wait"
RUN_FAULT_TYPES = ("Elsa.Activities.Primitives.Activities.Fault", "Elsa.Fault")
RUN_EVENT_TYPES = ("Elsa.Activities.Primitives.Activities.Event", "Elsa.Event")
TERMINAL_RUN_STATUSES = ("Completed", "Faulted", "Cancelled")


def run_terminal(value):
    """A Workflow Instance read that has settled finally: Completed, Faulted or Cancelled."""
    return isinstance(value, dict) and (value.get("instance") or {}).get("status") in TERMINAL_RUN_STATUSES


def run_waiting(value, node_id):
    """A Workflow Instance read that is still in progress and waiting: the runtime reports such a Run as Running (observed
    natively on d5d731de2), with the waiting activity execution Suspended on its bookmark (subStatus BookmarkWaiting, one
    bookmark at least). The Run's own status never says Suspended, so the activity's is the waiting signal."""
    if not isinstance(value, dict) or run_terminal(value) or (value.get("instance") or {}).get("status") not in ("Running", "Suspended"):
        return False
    return any(isinstance(item, dict) and item.get("authoredActivityId") == node_id and item.get("status") == "Suspended"
               and item.get("subStatus") == "BookmarkWaiting" and (item.get("bookmarkCount") or 0) >= 1 for item in value.get("activities") or [])


def _digest(root, name, value):
    """Retains only the SHA-256 of a read that carries the private canary (the definition's own sensitive literal): the
    evidence proves which read it was without holding the value."""
    data = json.dumps(value, sort_keys=True).encode()
    return _retain(root, name + "-digest", {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data)})


def _run_once(http, output, evidence, workflows, runtime, publishing, name, state, layout, *, settled):
    """Create, promote and publish one definition, then execute it once and read its Workflow Instance until `settled`
    accepts it. Every design read that holds the authored state is retained by digest only."""
    request = {"operationKey": str(uuid.uuid4()), "name": "Weaver run witness " + name + " " + uuid.uuid4().hex[:8],
               "description": "Owned spec 1193 Run qualification.", "initialState": state, "layout": layout, "activityPresentation": []}
    evidence.append(_digest(output, name + "-create-request", request))
    created, _ = http.json(owner_path(http, workflows["workflow-definitions"]), request, method="POST")
    evidence.append(_digest(output, name + "-created", created))
    draft_id = created["draft"]["id"]
    promoted, _ = http.json(owner_path(http, workflows["workflow-draft-promote-exact-version"], draftId=draft_id), {"operationKey": str(uuid.uuid4())},
                            method="POST", expected=201)
    evidence.append(_digest(output, name + "-promoted", promoted))
    publication, _ = http.json(owner_path(http, publishing["workflow-publish"], versionId=promoted["id"]), {}, method="POST", expected=201)
    evidence.append(_retain(output, name + "-publication", publication))
    started, _ = http.json(owner_path(http, runtime["workflow-execute"], artifactId=publication["artifactId"]),
                           {"sourceReferenceId": publication["sourceReferenceId"]}, method="POST")
    evidence.append(_retain(output, name + "-started", started))
    run_id = started["workflowExecutionId"]
    check(isinstance(run_id, str) and run_id, "The runtime did not start the " + name + " Run")
    instance_path = owner_path(http, runtime["workflow-instance"], workflowExecutionId=run_id)
    # Execution is asynchronous: read the instance until it settles, without retrying anything.
    deadline = time.monotonic() + 60
    while True:
        instance, _ = http.json(instance_path)
        if settled(instance) or time.monotonic() > deadline: break
        time.sleep(0.5)
    evidence.append(_retain(output, name + "-instance", instance))
    check(settled(instance), "The " + name + " Run did not settle: " + str((instance.get("instance") or {}).get("status")))
    return {"workflowExecutionId": run_id, "instancePath": instance_path, "instance": instance, "publication": publication,
            "definitionId": created["definition"]["id"], "versionId": promoted["id"]}


def seed_faulted_run(http, output, canary):
    """Seed the faulted Run (with the sensitive canary input) and the waiting Run, and read each one's activity executions
    through the run API. Returns the owning paths and reads the driver compares every Weaver answer with."""
    check(isinstance(canary, str) and len(canary) >= 24, "Use a distinct Run canary")
    output = _new_output(output)
    activities, activity_capability = capability_links(http, "elsa.api.activity-design", output / "activities")
    workflows, workflow_capability = capability_links(http, "elsa.api.workflow-design", output / "workflows")
    publishing, publishing_capability = capability_links(http, "elsa.api.publishing", output / "publishing")
    runtime, runtime_capability = capability_links(http, "elsa.api.runtime", output / "runtime")
    check(all(key in runtime for key in ("workflow-execute", "workflow-instance", "activity-execution", "workflow-incidents"))
          and "workflow-publish" in publishing and "workflow-draft-promote-exact-version" in workflows,
          "The native Run witness needs the real publish, execute, instance, activity execution and incident relations")
    evidence = [activity_capability, workflow_capability, publishing_capability, runtime_capability]
    catalog, _ = http.json(owner_path(http, activities["activity-catalog"]))
    evidence.append(_retain(output, "activity-catalog", catalog))
    def entry(*type_keys):
        matches = [item for item in catalog["activities"] if item["activityTypeKey"] in type_keys and item.get("available") is True]
        check(len(matches) == 1 and bool(matches[0]["activityVersionId"]), "The actual catalog must uniquely supply one of " + ", ".join(type_keys))
        return matches[0]
    write, flowchart, fault, event = entry(BATCH_WRITE_LINE), entry(BATCH_FLOWCHART), entry(*RUN_FAULT_TYPES), entry(*RUN_EVENT_TYPES)
    container, n = _container(flowchart), RUN_NODES
    literal = lambda key, value, sensitive=None: {"referenceKey": key, "value": {"value": value, "expressionType": "Literal"}, "autoEvaluate": None,
                                                  "evaluatorType": None, "storageDriverType": None, "isSensitive": sensitive}
    def root(children, connections, start):
        payload = json.loads(json.dumps(container["payload"]))
        payload.update(activities=children, connections=connections, startNodeId=start)
        return {"variables": [], "inputs": [], "outputs": [], "rootActivity": {"nodeId": n["root"], "activityVersionId": flowchart["activityVersionId"],
                "inputs": [], "outputs": [], "structure": {"kind": container["kind"], "schemaVersion": "1.0.0", "payload": payload}}}
    leaf = lambda node_id, version, inputs: {"nodeId": node_id, "activityVersionId": version, "outputs": [], "structure": None, "inputs": inputs}
    faulted = root([leaf(n["sensitive"], write["activityVersionId"], [literal("text", canary, True)]),
                    leaf(n["fault"], fault["activityVersionId"], [literal("code", RUN_FAULT["code"]), literal("message", RUN_FAULT["message"])])],
                   [{"id": n["sensitive"] + "-" + n["fault"], "source": {"nodeId": n["sensitive"], "port": "Done"}, "target": {"nodeId": n["fault"]}}], n["sensitive"])
    waiting = root([leaf(n["wait"], event["activityVersionId"], [literal("EventName", RUN_WAIT_EVENT), literal("CanStartWorkflow", False)])], [], n["wait"])
    layout = lambda nodes: [{"nodeId": node, "x": 120 + 300 * index, "y": 160} for index, node in enumerate(nodes)]
    fault_run = _run_once(http, output, evidence, workflows, runtime, publishing, "faulted", faulted, layout([n["sensitive"], n["fault"]]), settled=run_terminal)
    check(fault_run["instance"]["instance"]["status"] == "Faulted", "The seeded Run did not fault")
    wait_run = _run_once(http, output, evidence, workflows, runtime, publishing, "waiting", waiting, layout([n["wait"]]),
                         settled=lambda value: run_waiting(value, n["wait"]) or run_terminal(value))
    check(run_waiting(wait_run["instance"], n["wait"]), "The seeded waiting Run is not in progress, waiting on its bookmark: "
          + str((wait_run["instance"].get("instance") or {}).get("status")))
    # The run API's own reads of each activity execution, by authored node.
    for run in (fault_run, wait_run):
        run["activities"] = {}
        for item in run["instance"].get("activities") or []:
            path = owner_path(http, runtime["activity-execution"], workflowExecutionId=run["workflowExecutionId"], activityExecutionId=item["activityExecutionId"])
            detail, _ = http.json(path)
            evidence.append(_retain(output, "activity-" + item["activityExecutionId"], detail))
            run["activities"][item["authoredActivityId"]] = {"path": path, "detail": detail}
        incidents, _ = http.json(owner_path(http, runtime["workflow-incidents"], workflowExecutionId=run["workflowExecutionId"]))
        evidence.append(_retain(output, "incidents-" + run["workflowExecutionId"], incidents))
        run["incidents"] = incidents
    check(n["sensitive"] in fault_run["activities"] and n["fault"] in fault_run["activities"] and n["wait"] in wait_run["activities"],
          "The run API does not list every seeded activity execution")
    # Retained evidence must never hold the canary: every retained read above is the run API's, which never discloses it.
    for ref in evidence:
        check(canary.encode() not in Path(ref["path"]).read_bytes(), "A retained seed read holds the Run canary: " + Path(ref["path"]).name)
    return {"faulted": fault_run, "waiting": wait_run, "nodes": dict(n), "fault": dict(RUN_FAULT), "runtimeLinks": runtime, "evidence": evidence}
