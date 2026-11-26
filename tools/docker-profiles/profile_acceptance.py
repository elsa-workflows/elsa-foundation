"""Exercise the extra capabilities that distinguish the packaged server profiles."""

import json
from pathlib import Path
import secrets
import time
from urllib.parse import quote
from uuid import uuid4


REPOSITORY = Path(__file__).resolve().parents[2]
IMPORT_FIXTURES = REPOSITORY / "extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures"
MIGRATED_PROFILE = REPOSITORY / "docker/profiles/elsa3-import"
MIGRATED_FIXTURE = MIGRATED_PROFILE / "example-migrated"
DECISION_FIXTURE = REPOSITORY / "specs/1195-business-decisions/evidence/interop/matrix/approval.dmn"


def publish_and_execute(request_json, version_id):
    publication = request_json(f"/publishing/workflows/{quote(version_id, safe='')}/publish", {})
    return execute(request_json, publication["artifactId"], publication["sourceReferenceId"])


def execute(request_json, artifact_id, source_reference_id):
    execution = request_json(f"/runtime/workflows/executables/{quote(artifact_id, safe='')}/execute", {
        "sourceReferenceId": source_reference_id,
    })
    execution_id = execution["workflowExecutionId"]
    path = f"/runtime/workflows/instances/{quote(execution_id, safe='')}"
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        details = request_json(path)
        instance = details["instance"]
        if instance["status"] in ("Faulted", "Cancelled") or instance["incidentCount"]:
            raise RuntimeError(f"Workflow {execution_id} did not complete successfully: {details}")
        if instance["status"] in ("Completed", "Finished"):
            nodes = sorted({item["executableNodeId"] for item in details["activities"]})
            return {"executionId": execution_id, "status": instance["status"], "executedNodes": nodes}
        time.sleep(0.25)
    raise RuntimeError(f"Workflow {execution_id} did not complete within 30s")


def verify_elsa3_import(request_json):
    sources = [json.loads((IMPORT_FIXTURES / f"{name}.json").read_text(encoding="utf-8"))
               for name in ("reusable-two-outcomes", "caller-routes-outcome")]
    route = "/migration/elsa3/reusable-activities"
    uploaded = request_json(f"{route}/collections", sources)
    collection = f"{route}/collections/{quote(uploaded['collectionHandle'], safe='')}"
    analysis = request_json(f"{collection}/analysis")
    version_ids = sorted(item["sourceVersionId"] for item in analysis["items"])
    if version_ids != ["approval-v1", "caller-v1"]:
        raise RuntimeError(f"Import analysis returned unexpected source versions: {version_ids}")
    selection = {"planId": analysis["planId"], "selectedSourceVersionIds": version_ids}
    readiness = request_json(f"{collection}/selection", selection)
    if not readiness["isReady"]:
        raise RuntimeError(f"Elsa 3 import selection could not be applied: {readiness}")

    idempotency_key = f"profile-smoke-{uuid4().hex}"
    receipt = request_json(f"{collection}/apply", {**selection, "idempotencyKey": idempotency_key})
    if sorted(item["sourceVersionId"] for item in receipt["sources"]) != version_ids:
        raise RuntimeError(f"Import did not apply both selected versions: {receipt}")
    publications = receipt["publications"]
    if (len(publications) != 1 or publications[0]["sourceVersionId"] != "approval-v1"
            or publications[0]["status"] != 0):
        raise RuntimeError(f"Import did not publish the reusable activity: {publications}")
    status = request_json(f"{route}/imports/{idempotency_key}")
    if status["receiptId"] != receipt["receiptId"]:
        raise RuntimeError("Import status did not return the applied receipt")
    caller = next(item for item in receipt["sources"] if item["sourceVersionId"] == "caller-v1")
    execution = publish_and_execute(request_json, caller["workflowVersionId"])
    if "on-approved" not in execution["executedNodes"] or "on-rejected" in execution["executedNodes"]:
        raise RuntimeError(f"Imported workflow did not follow the reusable activity's Approved outcome: {execution}")
    return {"sourceVersions": version_ids, "receiptId": receipt["receiptId"],
            "publishedReusableVersion": publications[0]["activityDefinitionVersionId"], "callerExecution": execution}


def verify_migrated_files(request_json, find_activities):
    activity, = json.loads((MIGRATED_FIXTURE / "activities/activities.json").read_text(encoding="utf-8"))
    workflow, = json.loads((MIGRATED_FIXTURE / "workflows/example.json").read_text(encoding="utf-8"))
    node = workflow["state"]["rootActivity"]
    definitions = find_activities(activity["activityTypeKey"])
    if len(definitions) != 1 or definitions[0]["headVersionId"] != node["activityVersionId"]:
        raise RuntimeError(f"Mounted activity did not reconcile at the workflow's pinned version: {definitions}")

    definition_id = workflow["definitionId"]
    slots = request_json(f"/runtime/workflows/activation-slots/{quote(definition_id, safe='')}")
    active = [slot["activeActivationId"] for slot in slots["items"] if slot["activeActivationId"]]
    if len(active) != 1:
        raise RuntimeError(f"Expected one active publication at readiness for {definition_id}: {slots}")
    executables = request_json("/runtime/workflows/executables")
    references = [
        (item["artifactId"], reference["sourceReferenceId"])
        for item in executables["items"] if item["definitionId"] == definition_id
        for reference in item["references"] if reference["live"]
    ]
    if len(references) != 1:
        raise RuntimeError(f"Expected one live runtime source reference for {definition_id}: {references}")
    artifact_id, source_reference_id = references[0]
    execution = execute(request_json, artifact_id, source_reference_id)
    if node["nodeId"] not in execution["executedNodes"]:
        raise RuntimeError(f"Mounted workflow did not execute its reconciled activity: {execution}")
    return {"definitionId": definition_id, "activityVersionId": node["activityVersionId"],
            "activeActivationId": active[0], "execution": execution,
            "output": node["inputs"][0]["value"]["value"]}


def verify_decisions(request_json):
    asset = request_json("/decisions", {
        "name": "Profile smoke approval", "operationId": uuid4().hex,
        "xml": DECISION_FIXTURE.read_text(encoding="utf-8"),
    })
    route = f"/decisions/{quote(asset['id'], safe='')}"
    version = request_json(f"{route}/publish", {
        "operationId": uuid4().hex, "expectedRevision": asset["revision"],
    })
    evaluation = request_json(f"{route}/evaluate", {
        "versionId": version["id"], "inputs": {"value": {"kind": "number", "value": "950"}},
    })
    if evaluation["status"] != "Success" or evaluation["result"]["value"] != "Automatic":
        raise RuntimeError(f"Unexpected decision result: {evaluation}")
    return {"decisionVersion": version["id"], "result": evaluation["result"]["value"]}


def verify_secret_store(request_json):
    name = f"profile-smoke-{uuid4().hex}"
    value = secrets.token_urlsafe(32)
    created = request_json("/secrets", {"name": name, "value": value})
    tested = request_json(f"/secrets/{name}/test", {})
    if value in json.dumps([created, tested]):
        raise RuntimeError("The secret API disclosed a stored value")
    if created["name"] != name or not tested["succeeded"]:
        raise RuntimeError("The profile could not store and resolve an encrypted secret")
    return {"encryptedStore": "verified"}


def verify_variant(variant, request_json):
    if variant == "elsa3-import":
        return {"elsa3Import": verify_elsa3_import(request_json)}
    if variant == "full":
        return {"decision": verify_decisions(request_json), "secrets": verify_secret_store(request_json)}
    return {}
