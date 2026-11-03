"""Native, explicitly owned resources for the witness; no shadow store or validation engine."""
from __future__ import annotations

import json
from pathlib import Path
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


# Spec 1193 slice C (T022): the containers an Operation Batch addresses, with their structure kinds as the activities
# declare them ([ActivityStructure]) and the one slot each declares, as Studio resolves it ("<kind>:<property>").
BATCH_CONTAINERS = {"Flowchart": ("Elsa.Activities.Flowchart.Activities.Flowchart", "elsa.flowchart.structure", "flowchart"),
                    "Sequence": ("Elsa.Activities.Sequence.Activities.Sequence", "elsa.sequence.structure", "sequence")}
BATCH_WRITE_LINE = "Elsa.Activities.Primitives.Activities.WriteLine"
BATCH_NODES = {"root": "batch-flow", "a": "batch-a", "b": "batch-b", "seq": "batch-seq", "inner": "batch-inner"}


def seed_batch_workflow(http, output):
    """Create the real nested draft the Operation Batch journeys address: a root flowchart holding two Write Lines and a
    sequence that holds a third, connected a -> b and a -> sequence. Retains the catalog entries and slot identities."""
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
    write = entry(BATCH_WRITE_LINE)
    check(any(item["referenceKey"] == "text" for item in write["inputs"]), "Actual WriteLine contract has no text input")
    containers = {}
    for name, (type_key, kind, mode) in BATCH_CONTAINERS.items():
        item = entry(type_key)
        template = ((item.get("authoringTemplate") or {}).get("structure") or {})
        check(template.get("kind") == kind and isinstance(template.get("payload"), dict) and isinstance(template["payload"].get("activities"), list),
              "The actual " + name + " entry does not author the expected '" + kind + "' structure")
        containers[name] = {"entry": item, "kind": kind, "mode": mode, "slot": kind + ":activities", "payload": template["payload"]}
    def leaf(node_id, text):
        return {"nodeId": node_id, "activityVersionId": write["activityVersionId"], "outputs": [], "structure": None,
                "inputs": [{"referenceKey": "text", "value": {"value": text, "expressionType": "Literal"}}]}
    def container(name, node_id, children, **extra):
        payload = json.loads(json.dumps(containers[name]["payload"]))
        payload.update(activities=children, **extra)
        return {"nodeId": node_id, "activityVersionId": containers[name]["entry"]["activityVersionId"], "inputs": [], "outputs": [],
                "structure": {"kind": containers[name]["kind"], "schemaVersion": "1.0.0", "payload": payload}}
    n = BATCH_NODES
    sequence = container("Sequence", n["seq"], [leaf(n["inner"], "Inside the sequence")])
    connections = [{"id": "batch-a-b", "source": {"nodeId": n["a"], "port": "Done"}, "target": {"nodeId": n["b"]}},
                   {"id": "batch-a-seq", "source": {"nodeId": n["a"], "port": "Done"}, "target": {"nodeId": n["seq"]}}]
    root = container("Flowchart", n["root"], [leaf(n["a"], "First"), leaf(n["b"], "Second"), sequence],
                     connections=connections, startNodeId=n["a"])
    request = {
        "operationKey": str(uuid.uuid4()), "name": "Weaver batch witness " + uuid.uuid4().hex[:8],
        "description": "Owned spec 1193 Operation Batch qualification; no runtime dispatch or publication.",
        "initialState": {"variables": [], "inputs": [], "outputs": [], "rootActivity": root},
        "layout": [{"nodeId": n["a"], "x": 120, "y": 160}, {"nodeId": n["b"], "x": 420, "y": 160}, {"nodeId": n["seq"], "x": 120, "y": 420},
                   {"nodeId": n["inner"], "x": 80, "y": 80}],
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
    check(validations["draftId"] == draft_id and validations["errors"] == [], "The native batch seed failed ordinary validation")
    return {"definitionId": definition_id, "draftId": draft_id, "definitionPath": definition_path,
            "draftPath": owner_path(http, workflows["workflow-drafts"], draftId=draft_id),
            "validationPath": owner_path(http, workflows["workflow-draft-validations"], draftId=draft_id),
            "nodes": dict(n), "writeLine": {"activityTypeKey": write["activityTypeKey"], "version": write["version"],
                                            "activityVersionId": write["activityVersionId"], "displayName": write["displayName"]},
            "containers": {name: {"activityVersionId": value["entry"]["activityVersionId"], "activityTypeKey": value["entry"]["activityTypeKey"],
                                  "displayName": value["entry"]["displayName"], "slot": value["slot"], "mode": value["mode"]}
                           for name, value in containers.items()},
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
