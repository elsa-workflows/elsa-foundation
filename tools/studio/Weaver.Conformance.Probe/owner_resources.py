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
