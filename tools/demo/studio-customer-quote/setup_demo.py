#!/usr/bin/env python3
"""Seed the v3 calculator activity and quote workflow through the local Workbench API.

The script never creates or reads a Secret or Connection credential. The operator must
first create the Secret and its Bearer HTTP Connection in Studio. Only --apply performs
writes; sign-in credentials are read from environment variables and are never printed.
"""

from __future__ import annotations

import argparse
import hashlib
import http.cookiejar
import json
import os
import sys
import uuid
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlencode, urlsplit
from urllib.request import HTTPRedirectHandler, HTTPCookieProcessor, Request, build_opener


WORKBENCH_ORIGIN = "http://127.0.0.1:5595"
STUDIO_ORIGIN = "http://127.0.0.1:5589"
USERNAME_ENV = "ELSA_DEMO_STUDIO_USERNAME"
PASSWORD_ENV = "ELSA_DEMO_STUDIO_PASSWORD"
CONNECTION_NAME = "customer-api"
CONNECTION_DISPLAY_NAME = "Customer API"
DEMO_API_URL = "http://127.0.0.1:5582/api/customers/C-1042"
ENDPOINT_PATH = "quotes"
WORKFLOW_NAME = "Customer quote with protected lookup"
ACTIVITY_DISPLAY_NAME = "CalculateOrderTotal"
ACTIVITY_CATEGORY = "Commerce"
JSON_DRIVER = "elsa.json"
HTTP_ENDPOINT_CONTENT_PROJECTION = "ParsedContent"
SEND_HTTP_RESPONSE_BODY_PROJECTION = "ResponseBody"
SEQUENCE_TYPE = "Elsa.Activities.Sequence.Activities.Sequence"
HTTP_ENDPOINT_TYPE = "Elsa.Activities.Http.Activities.HttpEndpoint"
SEND_HTTP_REQUEST_TYPE = "Elsa.Activities.Http.Activities.SendHttpRequest"
WRITE_HTTP_RESPONSE_TYPE = "Elsa.Activities.Http.Activities.WriteHttpResponse"
GRAPH_COMPLETE_TYPE = "Elsa.Activities.Graph.Runtime.Activities.Complete"
GRAPH_PROVIDER = "elsa.activity-graph"
GRAPH_SCHEMA = "2"
EXPRESSION_PROFILE = "binding-pure-v1"
FAULT_BODY_EXPRESSION = "JSON.parse(args.CustomerResponse).profile.segment"
REPAIRED_BODY_EXPRESSION = (
    "JSON.stringify({customerId: JSON.parse(args.CustomerResponse).customerId, "
    "name: JSON.parse(args.CustomerResponse).name, total: args.Total})"
)
RECEIPT_PATH = Path(__file__).resolve().parent / "evidence" / "setup-result.json"


class DemoSetupError(RuntimeError):
    pass


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


class ElsaApi:
    def __init__(self, username: str, password: str, origin: str = WORKBENCH_ORIGIN) -> None:
        parsed = urlsplit(origin)
        if (parsed.scheme, parsed.hostname, parsed.port, parsed.path, parsed.query, parsed.fragment) != (
            "http",
            "127.0.0.1",
            5595,
            "",
            "",
            "",
        ):
            raise DemoSetupError("Workbench URL must be exactly the isolated loopback origin http://127.0.0.1:5595.")
        self.origin = origin.rstrip("/")
        self.cookie_jar = http.cookiejar.CookieJar()
        self.opener = build_opener(HTTPCookieProcessor(self.cookie_jar), NoRedirect())
        self.request("POST", "_elsa/identity/login", {"username": username, "password": password})
        if not self.cookie_jar:
            raise DemoSetupError("Local sign-in returned without an authentication cookie.")

    def request(self, method: str, path: str, body: object | None = None) -> object:
        payload = None if body is None else json.dumps(body, separators=(",", ":")).encode("utf-8")
        headers = {"Accept": "application/json"}
        if payload is not None:
            headers["Content-Type"] = "application/json"
        request = Request(f"{self.origin}/{path.lstrip('/')}", data=payload, headers=headers, method=method)
        try:
            with self.opener.open(request, timeout=20) as response:
                raw = response.read()
                if not raw:
                    return {}
                return json.loads(raw)
        except HTTPError as error:
            raise DemoSetupError(f"{method} {path} returned HTTP {error.code}.") from None
        except (URLError, TimeoutError, json.JSONDecodeError) as error:
            # Do not render request bodies, cookies, credentials, or low-level URL details.
            raise DemoSetupError(f"{method} {path} failed ({type(error).__name__}).") from None


def arg(reference_key: str, value: object, expression_type: str = "Literal") -> dict[str, object]:
    return {
        "referenceKey": reference_key,
        "value": {"value": value, "expressionType": expression_type},
        "autoEvaluate": None,
        "evaluatorType": None,
        "storageDriverType": None,
        "isSensitive": None,
    }


def activity_node(node_id: str, version_id: str, inputs: list[dict[str, object]]) -> dict[str, object]:
    return {"nodeId": node_id, "activityVersionId": version_id, "inputs": inputs, "outputs": []}


def portable_js(source: str, result_alias: str, parameters: dict[str, object]) -> dict[str, object]:
    """Build the serialized ExpressionDefinition consumed by binding-pure-v1."""
    return {
        "language": "JavaScript",
        "source": source,
        "resultType": {"alias": result_alias, "collectionKind": "Single"},
        "parameters": parameters,
        "options": {},
        "capabilityProfile": EXPRESSION_PROFILE,
        "metadata": {},
    }


def expression_arg(reference_key: str, definition: dict[str, object]) -> dict[str, object]:
    return arg(reference_key, definition, "JavaScript")


def activity_result_parameter(producer_node_id: str, projection_key: str) -> dict[str, object]:
    return {
        "kind": "ActivityResult",
        "producerNodeId": producer_node_id,
        "projectionKey": projection_key,
        "isOptional": False,
    }


def workflow_request_state(sequence_version_id: str, http_endpoint_version_id: str,
                           send_http_request_version_id: str, write_http_response_version_id: str,
                           calculator_version_id: str) -> dict[str, object]:
    parsed_content = {
        "ParsedContent": activity_result_parameter("http-endpoint", HTTP_ENDPOINT_CONTENT_PROJECTION)
    }
    calculator_inputs = [
        expression_arg("subtotal", portable_js("args.ParsedContent.subtotal", "Int32", parsed_content)),
        expression_arg("shipping", portable_js("args.ParsedContent.shipping", "Int32", parsed_content)),
    ]
    response_parameters = {
        "CustomerResponse": activity_result_parameter("customer-lookup", SEND_HTTP_RESPONSE_BODY_PROJECTION),
        "Total": activity_result_parameter("calculate-order-total", "total"),
    }
    response_expression = portable_js(REPAIRED_BODY_EXPRESSION, "String", response_parameters)

    activities = [
        activity_node(
            "http-endpoint",
            http_endpoint_version_id,
            [
                arg("Path", ENDPOINT_PATH),
                arg("CanStartWorkflow", True),
                arg("SupportedMethods", ["POST"]),
                arg("ResponseMode", "Sync"),
            ],
        ),
        activity_node(
            "customer-lookup",
            send_http_request_version_id,
            [
                arg("Url", DEMO_API_URL),
                arg("Method", "GET"),
                arg("RequestHeaders", {"X-Demo-Request": "quote-demo-v3"}),
                arg("Connection", {"connection": CONNECTION_NAME, "type": "http"}, "Connection"),
            ],
        ),
        activity_node("calculate-order-total", calculator_version_id, calculator_inputs),
        activity_node(
            "write-http-response",
            write_http_response_version_id,
            [
                arg("StatusCode", 200),
                expression_arg("Body", response_expression),
                arg("ContentType", "application/json"),
            ],
        ),
    ]
    return {
        "variables": [],
        "rootActivity": {
            "nodeId": "root",
            "activityVersionId": sequence_version_id,
            "inputs": [],
            "outputs": [],
            "structure": {
                "kind": "elsa.sequence.structure",
                "schemaVersion": "1.0.0",
                "payload": {"activities": activities},
            },
        },
        "inputs": [],
        "outputs": [],
        "strategyOptions": None,
    }


def calculator_definition_request(sequence_version_id: str, complete_version_id: str) -> dict[str, object]:
    complete = activity_node(
        "complete-calculated",
        complete_version_id,
        [arg("outcome", "calculated")],
    )
    return {
        "category": ACTIVITY_CATEGORY,
        "displayName": ACTIVITY_DISPLAY_NAME,
        "description": "Adds a subtotal and shipping amount and returns a typed total.",
        "provider": {
            "providerKey": GRAPH_PROVIDER,
            "schemaVersion": GRAPH_SCHEMA,
            "payload": {
                "variables": [],
                "rootActivity": {
                    "nodeId": "graph-root",
                    "activityVersionId": sequence_version_id,
                    "inputs": [],
                    "outputs": [],
                    "structure": {
                        "kind": "elsa.sequence.structure",
                        "schemaVersion": "1.0.0",
                        "payload": {"activities": [complete]},
                    },
                },
                "outputMappings": [
                    {
                        "outputReferenceKey": "total",
                        "source": {"syntax": "JavaScript", "value": "args.Subtotal + args.Shipping"},
                    }
                ],
                "outcomeMappings": [
                    {"sourceOutcomeReferenceKey": "Done", "boundaryOutcomeReferenceKey": "calculated"}
                ],
            },
        },
        "contract": {
            "contractSchemaVersion": "1",
            "inputs": [
                {
                    "referenceKey": "subtotal",
                    "name": "Subtotal",
                    "type": {"alias": "Int32", "collectionKind": "Single"},
                    "isRequired": True,
                    "isNullable": False,
                    "default": None,
                    "storageDriverKey": JSON_DRIVER,
                    "durability": "required",
                    "order": 0,
                },
                {
                    "referenceKey": "shipping",
                    "name": "Shipping",
                    "type": {"alias": "Int32", "collectionKind": "Single"},
                    "isRequired": True,
                    "isNullable": False,
                    "default": None,
                    "storageDriverKey": JSON_DRIVER,
                    "durability": "required",
                    "order": 1,
                },
            ],
            "outputs": [
                {
                    "referenceKey": "total",
                    "name": "Total",
                    "type": {"alias": "Int32", "collectionKind": "Single"},
                    "isRequired": True,
                    "isNullable": False,
                    "storageDriverKey": JSON_DRIVER,
                    "durability": "required",
                    "order": 0,
                }
            ],
            "outcomes": [{"referenceKey": "calculated", "name": "Calculated", "isEmitted": True}],
        },
        "layout": [],
    }


def read_activity_catalog(api: ElsaApi) -> list[dict[str, object]]:
    response = api.request("GET", "design/activities/catalog")
    rows = response.get("activities") if isinstance(response, dict) else None
    if not isinstance(rows, list):
        raise DemoSetupError("The activity catalog response did not contain an activities array.")
    return [item for item in rows if isinstance(item, dict)]


def validate_catalog_contracts(rows: list[dict[str, object]]) -> dict[str, dict[str, list[str]]]:
    descriptors = {
        item.get("activityTypeKey"): item
        for item in rows
        if isinstance(item.get("activityTypeKey"), str)
    }
    expected = {
        HTTP_ENDPOINT_TYPE: (
            {"Path", "CanStartWorkflow", "SupportedMethods", "ResponseMode"},
            {HTTP_ENDPOINT_CONTENT_PROJECTION},
        ),
        SEND_HTTP_REQUEST_TYPE: (
            {"Url", "Method", "RequestHeaders", "Connection"},
            {SEND_HTTP_RESPONSE_BODY_PROJECTION},
        ),
        WRITE_HTTP_RESPONSE_TYPE: ({"StatusCode", "Body", "ContentType"}, set()),
        GRAPH_COMPLETE_TYPE: ({"outcome"}, set()),
        SEQUENCE_TYPE: (set(), set()),
    }
    summary: dict[str, dict[str, list[str]]] = {}
    for activity_type, (required_inputs, required_outputs) in expected.items():
        descriptor = descriptors.get(activity_type)
        if not isinstance(descriptor, dict):
            raise DemoSetupError(f"Catalog omitted required activity descriptor {activity_type}.")
        inputs = descriptor.get("inputs")
        outputs = descriptor.get("outputs")
        if not isinstance(inputs, list) or not isinstance(outputs, list):
            raise DemoSetupError(f"Catalog descriptor {activity_type} omitted its input or output arrays.")
        input_keys = {item.get("referenceKey") for item in inputs if isinstance(item, dict)}
        output_keys = {item.get("referenceKey") for item in outputs if isinstance(item, dict)}
        missing_inputs = required_inputs.difference(input_keys)
        missing_outputs = required_outputs.difference(output_keys)
        if missing_inputs or missing_outputs:
            missing = sorted(missing_inputs | missing_outputs)
            raise DemoSetupError(f"Catalog descriptor {activity_type} is missing expected contract keys: {', '.join(missing)}.")
        if activity_type == SEND_HTTP_REQUEST_TYPE:
            connection = next((item for item in inputs if isinstance(item, dict) and item.get("referenceKey") == "Connection"), None)
            if not isinstance(connection, dict) or connection.get("connectionType") != "http":
                raise DemoSetupError("Catalog does not declare SendHttpRequest.Connection as an HTTP Connection input.")
        summary[activity_type] = {
            "inputs": sorted(required_inputs),
            "outputs": sorted(required_outputs),
        }
    return summary


def catalog_versions(rows: list[dict[str, object]]) -> dict[str, str]:
    validate_catalog_contracts(rows)
    wanted = {
        SEQUENCE_TYPE,
        HTTP_ENDPOINT_TYPE,
        SEND_HTTP_REQUEST_TYPE,
        WRITE_HTTP_RESPONSE_TYPE,
        GRAPH_COMPLETE_TYPE,
    }
    result: dict[str, str] = {}
    for item in rows:
        type_key = item.get("activityTypeKey")
        if type_key in wanted:
            version_id = item.get("activityVersionId")
            if not isinstance(version_id, str) or not version_id:
                raise DemoSetupError(f"Catalog entry {type_key!r} has no activityVersionId.")
            result[type_key] = version_id
    missing = sorted(wanted.difference(result))
    if missing:
        raise DemoSetupError("Required catalog entries are unavailable: " + ", ".join(missing))
    return result


def ensure_connection(api: ElsaApi) -> None:
    response = api.request("GET", f"connections/{quote(CONNECTION_NAME, safe='')}")
    if not isinstance(response, dict):
        raise DemoSetupError("The pre-created Customer API Connection could not be read.")
    if response.get("name") != CONNECTION_NAME:
        raise DemoSetupError("The existing Connection name does not match the configured customer-api reference.")
    if str(response.get("typeName", "")).lower() != "http" or str(response.get("authScheme", "")).lower() != "bearer":
        raise DemoSetupError("customer-api must be an HTTP Connection using Bearer authentication.")
    if str(response.get("status", "")).lower() != "ready":
        raise DemoSetupError("customer-api must be in Ready status before workflow setup.")
    secret_fields = response.get("secretFieldNames")
    if not isinstance(secret_fields, list) or not any(str(field).lower() == "token" for field in secret_fields):
        raise DemoSetupError("customer-api metadata does not list the Bearer token field.")
    settings = response.get("settings")
    if not isinstance(settings, dict):
        raise DemoSetupError("customer-api metadata does not contain its host policy settings.")
    allowed_hosts = {part.strip().lower() for part in str(settings.get("allowedHosts", "")).split(",") if part.strip()}
    insecure_http = str(settings.get("allowInsecureHttp", "")).lower() == "true"
    if allowed_hosts != {"127.0.0.1"} or not insecure_http:
        raise DemoSetupError("customer-api must allow only 127.0.0.1 and explicit HTTP for this local mock.")


def find_activity_definition(api: ElsaApi) -> dict[str, object] | None:
    query = urlencode({"search": ACTIVITY_DISPLAY_NAME})
    response = api.request("GET", f"design/activities/definitions?{query}")
    items = response.get("items") if isinstance(response, dict) else None
    if not isinstance(items, list):
        raise DemoSetupError("Activity definitions response did not contain an items array.")
    for item in items:
        definition = item.get("definition") if isinstance(item, dict) else None
        if isinstance(definition, dict) and definition.get("displayName") == ACTIVITY_DISPLAY_NAME:
            return definition
    return None


def ensure_names_available(api: ElsaApi) -> None:
    if find_activity_definition(api) is not None:
        raise DemoSetupError(
            f"{ACTIVITY_DISPLAY_NAME!r} already exists. Inspect the existing definition or reset the disposable demo database before reseeding."
        )
    query = urlencode({"name": WORKFLOW_NAME})
    response = api.request("GET", f"design/workflows/definitions?{query}")
    items = response.get("items") if isinstance(response, dict) else None
    if not isinstance(items, list):
        raise DemoSetupError("Workflow definitions response did not contain an items array.")
    if any(isinstance(item, dict) and item.get("name") == WORKFLOW_NAME for item in items):
        raise DemoSetupError(
            f"{WORKFLOW_NAME!r} already exists. Inspect it or reset the disposable demo database before reseeding."
        )


def publish_calculator_draft(api: ElsaApi, definition_id: str, draft_id: str, revision: int,
                             expected_head_version_id: str | None) -> tuple[str, str]:
    preflight = api.request(
        "POST",
        f"design/activities/drafts/{quote(str(draft_id), safe='')}/publication-preflight",
        {"expectedDraftRevision": revision, "expectedDefinitionHeadVersionId": expected_head_version_id},
    )
    if not isinstance(preflight, dict) or not preflight.get("isPublishable"):
        diagnostics = preflight.get("diagnostics", []) if isinstance(preflight, dict) else []
        raise DemoSetupError("Calculator draft failed publication preflight: " + json.dumps(diagnostics, ensure_ascii=False)[:1800])
    receipt = api.request(
        "POST",
        f"design/activities/drafts/{quote(str(draft_id), safe='')}/publish",
        {
            "expectedDraftRevision": preflight["draftRevision"],
            "expectedDefinitionHeadVersionId": preflight.get("definitionHeadVersionId"),
            "version": preflight["reviewedVersion"],
            "reviewToken": preflight["reviewToken"],
            "idempotencyKey": str(uuid.uuid4()),
        },
    )
    try:
        if receipt.get("status") != "Applied":
            raise DemoSetupError(f"Calculator publication returned status {receipt.get('status')!r}.")
        version_id = receipt["outcome"]["definitionVersionId"]
    except (TypeError, KeyError) as error:
        raise DemoSetupError("Calculator publication receipt did not confirm an applied version.") from error
    return str(definition_id), str(version_id)


def publish_calculator(api: ElsaApi, sequence_version_id: str, complete_version_id: str) -> tuple[str, str]:
    created = api.request(
        "POST",
        "design/activities/definitions",
        calculator_definition_request(sequence_version_id, complete_version_id),
    )
    try:
        draft = created["draft"]
        draft_id = draft["draftId"]
        revision = draft["revision"]
        definition_id = created["definition"]["definitionId"]
    except (TypeError, KeyError) as error:
        raise DemoSetupError("The calculator definition response omitted its draft or definition identity.") from error
    return publish_calculator_draft(api, str(definition_id), str(draft_id), int(revision), None)


def repair_calculator_draft(api: ElsaApi, draft_id: str, sequence_version_id: str,
                            complete_version_id: str) -> tuple[str, str]:
    draft_path = f"design/activities/drafts/{quote(draft_id, safe='')}"
    current = api.request("GET", draft_path)
    if not isinstance(current, dict) or current.get("draftId") != draft_id:
        raise DemoSetupError("The requested calculator draft could not be read.")
    definition_id = current.get("definitionId")
    revision = current.get("revision")
    if not isinstance(definition_id, str) or not isinstance(revision, int):
        raise DemoSetupError("The calculator draft omitted its definition identity or revision.")

    definition = find_activity_definition(api)
    if not isinstance(definition, dict) or definition.get("definitionId") != definition_id:
        raise DemoSetupError("The requested draft is not the current CalculateOrderTotal definition.")
    expected_head_version_id = definition.get("headVersionId")
    if expected_head_version_id is not None and not isinstance(expected_head_version_id, str):
        raise DemoSetupError("The calculator definition returned an invalid head version identity.")

    authored = calculator_definition_request(sequence_version_id, complete_version_id)
    replaced = api.request(
        "PUT",
        draft_path,
        {
            "expectedRevision": revision,
            "contract": authored["contract"],
            "provider": authored["provider"],
            "layout": authored["layout"],
            "presentationLabel": ACTIVITY_DISPLAY_NAME,
        },
    )
    try:
        updated_revision = replaced["revision"]
        if replaced["draftId"] != draft_id:
            raise DemoSetupError("The draft replacement returned a different draft identity.")
    except (TypeError, KeyError) as error:
        raise DemoSetupError("The replaced calculator draft omitted its revision or identity.") from error
    return publish_calculator_draft(
        api,
        definition_id,
        draft_id,
        int(updated_revision),
        expected_head_version_id,
    )


def submit_and_publish_workflow(api: ElsaApi, versions: dict[str, str], calculator_version_id: str) -> dict[str, str]:
    state = workflow_request_state(
        versions[SEQUENCE_TYPE],
        versions[HTTP_ENDPOINT_TYPE],
        versions[SEND_HTTP_REQUEST_TYPE],
        versions[WRITE_HTTP_RESPONSE_TYPE],
        calculator_version_id,
    )
    state_hash = hashlib.sha256(json.dumps(state, sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    operation_key = str(uuid.uuid5(uuid.NAMESPACE_URL, f"elsa-demo-v3:{WORKFLOW_NAME}:{state_hash}"))
    submitted = api.request(
        "POST",
        "design/workflows/definitions/submit",
        {
            "operationKey": operation_key,
            "name": WORKFLOW_NAME,
            "description": "Quotes an order using a Secret-backed customer lookup and reusable total calculation.",
            "state": state,
        },
    )
    try:
        definition = submitted["definition"]
        version = submitted["version"]
        definition_id = definition["id"]
        draft_id = submitted["draftId"]
        version_id = version["id"]
    except (TypeError, KeyError) as error:
        raise DemoSetupError("Workflow submission response omitted its definition, draft, or version identity.") from error

    published = publish_workflow_version(api, str(version_id))
    if not isinstance(published, dict) or not published.get("artifactId"):
        raise DemoSetupError("Workflow publication response did not include an artifactId.")
    return {
        "definitionId": str(definition_id),
        "draftId": str(draft_id),
        "versionId": str(version_id),
        "artifactId": str(published["artifactId"]),
        "sourceReferenceId": str(published.get("sourceReferenceId", "")),
    }


def publish_workflow_version(api: ElsaApi, version_id: str) -> dict[str, object]:
    published = api.request("POST", f"publishing/workflows/{quote(version_id, safe='')}/publish", {})
    if not isinstance(published, dict) or not published.get("artifactId"):
        raise DemoSetupError("Workflow publication response did not include an artifactId.")
    return published


def next_patch_version(versions: list[object]) -> str:
    parsed: list[tuple[int, int, int]] = []
    for item in versions:
        value = item.get("version") if isinstance(item, dict) else None
        parts = value.split(".") if isinstance(value, str) else []
        if len(parts) == 3 and all(part.isdigit() for part in parts):
            parsed.append(tuple(int(part) for part in parts))
    if not parsed:
        return "1.0.0"
    major, minor, patch = max(parsed)
    return f"{major}.{minor}.{patch + 1}"


def repair_workflow_draft(api: ElsaApi, draft_id: str, state: dict[str, object]) -> dict[str, str]:
    path = f"design/workflows/drafts/{quote(draft_id, safe='')}"
    draft = api.request("GET", path)
    if not isinstance(draft, dict) or draft.get("id") != draft_id:
        raise DemoSetupError("The requested workflow draft could not be read.")
    definition_id = draft.get("definitionId")
    if not isinstance(definition_id, str):
        raise DemoSetupError("The workflow draft omitted its definition identity.")
    definition_details = api.request("GET", f"design/workflows/definitions/{quote(definition_id, safe='')}")
    definition = definition_details.get("definition") if isinstance(definition_details, dict) else None
    if not isinstance(definition, dict) or definition.get("name") != WORKFLOW_NAME:
        raise DemoSetupError("The requested workflow draft does not belong to the configured demo workflow.")

    updated = api.request(
        "PUT",
        path,
        {
            "operationKey": str(uuid.uuid4()),
            "draftId": draft_id,
            "state": state,
        },
    )
    if not isinstance(updated, dict) or updated.get("id") != draft_id:
        raise DemoSetupError("Workflow draft replacement did not confirm the expected draft identity.")

    validations = api.request("GET", f"{path}/validations")
    errors = validations.get("errors") if isinstance(validations, dict) else None
    if isinstance(errors, list) and errors:
        safe_errors = [
            {key: item.get(key) for key in ("code", "path", "member", "nodeId", "severity") if key in item}
            for item in errors
            if isinstance(item, dict)
        ]
        raise DemoSetupError("Workflow draft validation still has errors: " + json.dumps(safe_errors, ensure_ascii=False)[:1800])

    versions = definition_details.get("versions", []) if isinstance(definition_details, dict) else []
    requested_version = next_patch_version(versions if isinstance(versions, list) else [])
    promotion_preflight = api.request(
        "POST",
        f"{path}/promotion-preflight",
        {"draftId": draft_id, "requestedVersion": requested_version},
    )
    if not isinstance(promotion_preflight, dict) or not promotion_preflight.get("isReady"):
        issues = promotion_preflight.get("issues", []) if isinstance(promotion_preflight, dict) else []
        raise DemoSetupError("Workflow draft promotion preflight failed: " + json.dumps(issues, ensure_ascii=False)[:1800])
    promoted = api.request(
        "POST",
        f"{path}/promote",
        {
            "operationKey": str(uuid.uuid4()),
            "draftId": draft_id,
            "requestedVersion": requested_version,
        },
    )
    if not isinstance(promoted, dict) or not isinstance(promoted.get("id"), str):
        raise DemoSetupError("Workflow draft promotion did not return a version identity.")
    published = publish_workflow_version(api, promoted["id"])
    return {
        "definitionId": definition_id,
        "draftId": draft_id,
        "versionId": promoted["id"],
        "version": str(promoted.get("version", requested_version)),
        "artifactId": str(published["artifactId"]),
        "sourceReferenceId": str(published.get("sourceReferenceId", "")),
    }


def replace_consumer_body_source(api: ElsaApi, draft_id: str, source: str) -> tuple[dict[str, object], str, dict[str, object]]:
    """Change only the canonical Body expression source, preserving its declared bindings and all other state."""
    path = f"design/workflows/drafts/{quote(draft_id, safe='')}"
    draft = api.request("GET", path)
    if not isinstance(draft, dict) or draft.get("id") != draft_id:
        raise DemoSetupError("The requested quote workflow draft could not be read.")
    state = draft.get("state")
    if not isinstance(state, dict):
        raise DemoSetupError("The quote workflow draft omitted its authored state.")
    root = state.get("rootActivity")
    structure = root.get("structure") if isinstance(root, dict) else None
    payload = structure.get("payload") if isinstance(structure, dict) else None
    activities = payload.get("activities") if isinstance(payload, dict) else None
    if not isinstance(activities, list):
        raise DemoSetupError("The quote workflow draft omitted its Sequence activities.")

    nodes = {node.get("nodeId"): node for node in activities if isinstance(node, dict)}
    calculator = nodes.get("calculate-order-total")
    lookup = nodes.get("customer-lookup")
    response = nodes.get("write-http-response")
    if not all(isinstance(node, dict) for node in (calculator, lookup, response)):
        raise DemoSetupError("The quote workflow draft is missing a configured lookup, calculator, or response node.")

    connection_input = next((item for item in lookup.get("inputs", []) if isinstance(item, dict) and item.get("referenceKey") == "Connection"), None)
    connection_ref = connection_input.get("value", {}).get("value") if isinstance(connection_input, dict) else None
    if connection_ref != {"connection": CONNECTION_NAME, "type": "http"}:
        raise DemoSetupError("The quote workflow Connection reference is not the expected customer-api HTTP reference.")

    body_input = next((item for item in response.get("inputs", []) if isinstance(item, dict) and item.get("referenceKey") == "Body"), None)
    body_value = body_input.get("value", {}).get("value") if isinstance(body_input, dict) and isinstance(body_input.get("value"), dict) else None
    if not isinstance(body_value, dict) or body_input.get("value", {}).get("expressionType") != "JavaScript":
        raise DemoSetupError("WriteHttpResponse.Body is not authored as a JavaScript expression.")
    parameters = body_value.get("parameters")
    if not isinstance(parameters, dict):
        raise DemoSetupError("WriteHttpResponse.Body omitted its declared expression parameters.")
    expected_parameters = {
        "CustomerResponse": ("customer-lookup", SEND_HTTP_RESPONSE_BODY_PROJECTION),
        "Total": ("calculate-order-total", "total"),
    }
    for parameter_name, (producer_node, projection_key) in expected_parameters.items():
        binding = parameters.get(parameter_name)
        if not isinstance(binding, dict) or (
            binding.get("kind"), binding.get("producerNodeId"), binding.get("projectionKey")
        ) != ("ActivityResult", producer_node, projection_key):
            raise DemoSetupError(f"WriteHttpResponse.Body parameter {parameter_name!r} has an unexpected ActivityResult reference.")
    if set(parameters) != set(expected_parameters):
        raise DemoSetupError("WriteHttpResponse.Body must keep exactly the declared CustomerResponse and Total parameters.")

    body_value["source"] = source
    calculator_version_id = calculator.get("activityVersionId")
    if not isinstance(calculator_version_id, str) or not calculator_version_id:
        raise DemoSetupError("The quote workflow does not pin a CalculateOrderTotal version.")
    return state, calculator_version_id, connection_ref


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--inspect", action="store_true", help="Sign in and inspect read-only catalog/name preconditions.")
    mode.add_argument("--apply", action="store_true", help="Create and publish the calculator and consumer workflow.")
    mode.add_argument("--repair-calculator-draft", metavar="DRAFT_ID", help="Replace and publish a failed CalculateOrderTotal draft in place, then publish the consumer.")
    mode.add_argument("--repair-workflow-draft", metavar="DRAFT_ID", help="Replace and promote a failed quote workflow draft in place, then publish it.")
    mode.add_argument("--fault-consumer", metavar="DRAFT_ID", help="Publish only the prepared WriteHttpResponse.Body runtime fault, preserving all other workflow state. Does not execute it.")
    mode.add_argument("--repair-consumer", metavar="DRAFT_ID", help="Publish only the corrected WriteHttpResponse.Body source, preserving its bindings and all other workflow state. Does not execute it.")
    args = parser.parse_args()

    if not args.apply and not args.inspect and not args.repair_calculator_draft and not args.repair_workflow_draft and not args.fault_consumer and not args.repair_consumer:
        print(json.dumps({
            "mode": "plan-only",
            "workbenchApi": WORKBENCH_ORIGIN,
            "studio": STUDIO_ORIGIN,
            "connection": CONNECTION_NAME,
            "secretCreation": "manual in Studio; never performed by this script",
            "activity": ACTIVITY_DISPLAY_NAME,
            "workflow": WORKFLOW_NAME,
            "endpoint": f"POST {WORKBENCH_ORIGIN}/workflows/http/{ENDPOINT_PATH}",
            "outboundUrl": DEMO_API_URL,
            "writes": False,
        }, indent=2))
        return 0

    username = os.environ.get(USERNAME_ENV, "")
    password = os.environ.get(PASSWORD_ENV, "")
    if not username or not password:
        raise DemoSetupError(f"Set {USERNAME_ENV} and {PASSWORD_ENV} in the private environment before API access.")

    api = ElsaApi(username, password)

    if args.inspect:
        # This path is read-only: catalog and definition searches only.
        rows = read_activity_catalog(api)
        versions = catalog_versions(rows)
        contract_shapes = validate_catalog_contracts(rows)
        ensure_names_available(api)
        print(json.dumps({
            "mode": "readonly-inspect",
            "workbenchApi": WORKBENCH_ORIGIN,
            "activityCatalog": versions,
            "catalogContractShapes": contract_shapes,
            "namePreconditions": "no matching activity or workflow definition exists",
            "writes": False,
            "credentialFieldsRead": False,
        }, indent=2))
        return 0

    if args.fault_consumer or args.repair_consumer:
        ensure_connection(api)
        mode_name = "fault-consumer" if args.fault_consumer else "repair-consumer"
        draft_id = args.fault_consumer or args.repair_consumer
        source = FAULT_BODY_EXPRESSION if args.fault_consumer else REPAIRED_BODY_EXPRESSION
        state, calculator_version_id, connection_ref = replace_consumer_body_source(api, draft_id, source)
        workflow = repair_workflow_draft(api, draft_id, state)
        result = {
            "status": "published",
            "mode": mode_name,
            "workbenchApi": WORKBENCH_ORIGIN,
            "workflow": workflow,
            "route": f"POST /workflows/http/{ENDPOINT_PATH}",
            "changedInput": "write-http-response.Body.source",
            "bodyExpressionSource": source,
            "calculatorVersionId": calculator_version_id,
            "connectionReference": connection_ref,
            "writesSecretOrConnection": False,
            "executionPerformed": False,
        }
        receipt_path = RECEIPT_PATH.parent / f"{mode_name}-publication.json"
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        receipt_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({**result, "receipt": str(receipt_path)}, indent=2))
        return 0

    if args.repair_workflow_draft:
        ensure_connection(api)
        rows = read_activity_catalog(api)
        versions = catalog_versions(rows)
        calculator = find_activity_definition(api)
        calculator_version_id = calculator.get("headVersionId") if isinstance(calculator, dict) else None
        if not isinstance(calculator_version_id, str) or not calculator_version_id:
            raise DemoSetupError("CalculateOrderTotal must have a published head version before repairing the consumer draft.")
        state = workflow_request_state(
            versions[SEQUENCE_TYPE],
            versions[HTTP_ENDPOINT_TYPE],
            versions[SEND_HTTP_REQUEST_TYPE],
            versions[WRITE_HTTP_RESPONSE_TYPE],
            calculator_version_id,
        )
        workflow = repair_workflow_draft(api, args.repair_workflow_draft, state)
        result = {
            "status": "published",
            "mode": "repair-workflow-draft",
            "workbenchApi": WORKBENCH_ORIGIN,
            "connection": CONNECTION_NAME,
            "connectionCredentialSource": "not inspected; verify existing-secret selection in Studio",
            "calculatorDefinitionId": calculator.get("definitionId"),
            "calculatorVersionId": calculator_version_id,
            "workflow": workflow,
            "route": f"POST /workflows/http/{ENDPOINT_PATH}",
            "requestBody": {"subtotal": 40, "shipping": 2},
            "writesSecretOrConnection": False,
            "executionPerformed": False,
        }
        RECEIPT_PATH.parent.mkdir(parents=True, exist_ok=True)
        RECEIPT_PATH.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({**result, "receipt": str(RECEIPT_PATH)}, indent=2))
        return 0

    # Complete every read-only precondition before the first mutation.
    ensure_connection(api)
    if args.repair_calculator_draft:
        workflow_query = urlencode({"name": WORKFLOW_NAME})
        workflow_rows = api.request("GET", f"design/workflows/definitions?{workflow_query}").get("items", [])
        if any(isinstance(item, dict) and item.get("name") == WORKFLOW_NAME for item in workflow_rows):
            raise DemoSetupError(f"{WORKFLOW_NAME!r} already exists; inspect it before publishing another consumer.")
    else:
        ensure_names_available(api)
    rows = read_activity_catalog(api)
    versions = catalog_versions(rows)

    if args.repair_calculator_draft:
        calculator_definition_id, calculator_version_id = repair_calculator_draft(
            api,
            args.repair_calculator_draft,
            versions[SEQUENCE_TYPE],
            versions[GRAPH_COMPLETE_TYPE],
        )
    else:
        calculator_definition_id, calculator_version_id = publish_calculator(
            api, versions[SEQUENCE_TYPE], versions[GRAPH_COMPLETE_TYPE]
        )
    workflow = submit_and_publish_workflow(api, versions, calculator_version_id)
    result = {
        "status": "published",
        "mode": "apply",
        "workbenchApi": WORKBENCH_ORIGIN,
        "connection": CONNECTION_NAME,
        "connectionCredentialSource": "not inspected; verify existing-secret selection in Studio",
        "calculatorDefinitionId": calculator_definition_id,
        "calculatorVersionId": calculator_version_id,
        "workflow": workflow,
        "route": f"POST /workflows/http/{ENDPOINT_PATH}",
        "requestBody": {"subtotal": 40, "shipping": 2},
        "writesSecretOrConnection": False,
        "executionPerformed": False,
    }
    RECEIPT_PATH.parent.mkdir(parents=True, exist_ok=True)
    RECEIPT_PATH.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({**result, "receipt": str(RECEIPT_PATH)}, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except DemoSetupError as error:
        print(f"setup failed: {error}", file=sys.stderr)
        raise SystemExit(2)
