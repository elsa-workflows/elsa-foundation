"""Mandatory workflow-truth-seams qualification (spec 1193, story 4; T012).

On each actual adapter, Weaver's workflow context must carry the owning services' answers for the current actor and
tenant: the activity catalog (workflow.catalog) equals the designer's own catalog read, and the permissions
(workflow.permissions) equal the owning design API's read and manage decisions. Tool availability follows them: the
actual adapter is offered the reviewed layout tool only when the actor may manage the workflow.

Three dedicated principals in the admin's tenant run one read-graph turn each against the admin's seeded workflow: a
manager, a read-only reader and a reader without activity design read. A fourth, in a second tenant, compares catalogs
only: the packaged consumer's workflow persistence has one scope, so a definition that tenant created would be stored
under "default" and rightly refused to it; its Weaver catalog is the collection admitted with a target-free workflow
request. The host's generation observation also reports its loaded Agent Workflows package types and
composed catalog, so the packaged consumer is shown to ship and compose no placeholder. Finally the reader runs the
actual packaged Studio journey (browser.mjs, workflow-read-only) with a fresh normal-login state: explanation completes
through the owning catalog and permissions, and a layout request is never offered to the actual adapter, so no proposal,
node move or owning write follows and the stored draft is unchanged. The host's
catalog is CLR-reconciled and tenant-agnostic, so a tenant-restricted activity comes from the host's test-only
QualificationTenantActivity feature, selected through the ordinary native feature apply for this inventory only and
deselected afterwards. It adds one activity to the designer's own catalog read for the first tenant; the designer's
GET and Weaver both read through it, so each tenant's two answers are compared. Nothing is retried.
"""
from __future__ import annotations

from contextlib import contextmanager
import copy
import json
from pathlib import Path
import os
import shutil
import signal
import threading
import time
import uuid

from artifacts import TOOL, check, sha
from authorization_journeys import WORKFLOW_MODULE, select_command, snapshot_attachment
from journey_runtime import BASE_GRANTS, browser_inputs, same
from owner_resources import capability_links, owner_path, seed_workflow
from runtime import RUNTIME_ENVIRONMENT, private_json


SECTION = "workflowTruthSeamQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 300
PROVIDERS = ("copilot", "anthropic")
EXPECTED_CASES = frozenset(provider + "-workflow-truth-seams" for provider in PROVIDERS)
SEAM_FEATURE = "QualificationTenantActivity"
TENANT_ONLY_VERSION = "qualification-tenant-only-activity-v1"
READ_TOOL = "workflow_read_graph"
LAYOUT_TOOL = "workflow_set_designer_position"
BATCH_TOOL = "workflow_apply_operation_batch"
# The read-only journey asks for exactly these two; every advertisement check covers all three module tools.
WORKFLOW_TOOLS = frozenset({READ_TOOL, LAYOUT_TOOL})
ADVERTISED_TOOLS = frozenset({READ_TOOL, LAYOUT_TOOL, BATCH_TOOL})
CATALOG_KIND = "workflow.catalog"
PERMISSIONS_KIND = "workflow.permissions"

# The four dedicated principals, appended after the original five so every earlier index keeps its meaning.
READER, MANAGER, TENANT_READER, CATALOG_DENIED = 5, 6, 7, 8
# Exactly what each actor's journey reaches, beyond the agent and preferences BASE_GRANTS:
# - api-capabilities:read: GET /capabilities, through which every owner link (activity catalog, workflow definitions,
#   drafts, validations, expressions) is discovered, by the driver and by Studio alike;
# - workflows/design:read: the definition GET, its draft validations and the read-graph tool;
# - workflows/design:manage (manager): the promotion preflight and the layout tool; the evaluator lets it satisfy read;
# - activities/design:read: the designer catalog GET and Weaver's catalog (never the catalog-denied actor);
# - expressions:read (reader only): the packaged Studio editor's expression descriptors in the browser journey.
CAPABILITIES_READ = "api-capabilities:read"
READER_GRANTS = [*BASE_GRANTS, CAPABILITIES_READ, "workflows/design:read", "activities/design:read", "expressions:read"]
# A reader without activity design read: the owner withholds its catalog, and Weaver must disclose none.
CATALOG_DENIED_GRANTS = [*BASE_GRANTS, CAPABILITIES_READ, "workflows/design:read"]
MANAGER_GRANTS = [*BASE_GRANTS, CAPABILITIES_READ, "workflows/design:manage", "activities/design:read"]
# The second tenant's actor compares catalogs only. The packaged consumer's workflow persistence has one scope, so every
# definition is stored under that scope's tenant ("default") whatever tenant creates it, and the owner rightly refuses a
# principal of another tenant (its stored tenant must equal the principal's). This actor therefore seeds no workflow and
# runs no turn: its Weaver catalog is the collection admitted with its message, over a target-free workflow request,
# which needs workflow design read.
TENANT_READER_GRANTS = [*BASE_GRANTS, CAPABILITIES_READ, "workflows/design:read", "activities/design:read"]
# role -> (principal index, seeded workflow or None: catalog only, may manage, sees the tenant-restricted activity;
# None: no catalog read)
ACTORS = (("manager", MANAGER, "home", True, True), ("reader", READER, "home", False, True),
          ("tenant-reader", TENANT_READER, None, None, False), ("catalog-denied", CATALOG_DENIED, "home", False, None))


def truth_principals(principals, new_identity):
    """The dedicated principals: a reader, a manager and a catalog-denied reader in the first principal's tenant, and a
    catalog-only reader in the third's."""
    check(len(principals) == READER and principals[0]["tenant"] != principals[2]["tenant"],
          "The truth-seam principals extend exactly the original five, which span two tenants")
    return [new_identity("witness-reader", principals[0]["tenant"], READER_GRANTS),
            new_identity("witness-manager", principals[0]["tenant"], MANAGER_GRANTS),
            new_identity("witness-tenant-reader", principals[2]["tenant"], TENANT_READER_GRANTS),
            new_identity("witness-catalog-denied", principals[0]["tenant"], CATALOG_DENIED_GRANTS)]


def _attachments(frame, kind):
    payload = frame.get("payload") if isinstance(frame, dict) else None
    items = payload.get("attachments") if isinstance(payload, dict) else None
    check(isinstance(items, list), "Malformed admitted context frame")
    return [item for item in items if isinstance(item, dict) and item.get("contentType") == kind]


TARGET_IDENTITY = ("moduleId", "resourceType", "resourceId", "revision")


def same_target(observed, expected):
    """A resource target's canonical identity: module, type, id and revision. The wire target also carries the optional
    presentation fields displayName and route (null for these snapshots), which never retarget a resource, as the engine's
    own target comparison ignores them; an unknown extra member is refused."""
    return (isinstance(observed, dict) and isinstance(expected, dict)
            and set(observed) <= {*TARGET_IDENTITY, "displayName", "route"}
            and all(observed.get(key) == expected.get(key) for key in TARGET_IDENTITY)
            and all(isinstance(expected.get(key), str) and expected[key] for key in TARGET_IDENTITY))


def require_truth_turn(frames, rows, *, provider, command_id, target):
    """One completed read-graph turn whose every admission carried the same catalog and the target's permissions."""
    check(provider in PROVIDERS, "Unknown adapter")
    values = [frame for frame in frames if isinstance(frame, dict)]
    check(len(values) == len(frames) and values and values[-1].get("kind") == "completed", "The read-graph turn did not complete")
    admitted = [frame for frame in values if frame.get("kind") == "contextAdmitted"]
    check(bool(admitted), "The turn admitted no context")
    catalog = permissions = None
    for frame in admitted:
        payload = frame.get("payload")
        check(isinstance(payload, dict) and payload.get("diagnostics") == [], "Context admission reported a diagnostic")
        check(any(same_target(item.get("target"), target) for item in _attachments(frame, "workflow.definition")),
              "The admission lost the exact working-state target")
        current_catalog, current_permissions = _attachments(frame, CATALOG_KIND), _attachments(frame, PERMISSIONS_KIND)
        check(len(current_catalog) == 1 and current_catalog[0].get("origin") == "authoritativeRead" and current_catalog[0].get("target") is None,
              "Each admission must carry exactly one server-read catalog with no target")
        check(len(current_permissions) == 1 and current_permissions[0].get("origin") == "authoritativeRead"
              and same_target(current_permissions[0].get("target"), target), "Each admission must carry the exact target's server-read permissions")
        # Each turn reads both again; an unchanged answer is the identical projection, never a drifting one.
        catalog = catalog or current_catalog[0]
        permissions = permissions or current_permissions[0]
        check(same(catalog, current_catalog[0]) and same(permissions, current_permissions[0]),
              "An unchanged catalog or permission answer changed between admissions of one turn")
    completed = [frame.get("payload") for frame in values if frame.get("kind") == "toolCallCompleted"]
    check(len(completed) == 1 and isinstance(completed[0], dict) and completed[0].get("toolName") == READ_TOOL
          and completed[0].get("succeeded") is True, "The read-graph tool did not complete once")
    owned = [row for row in rows if isinstance(row, dict) and (row.get("data") or {}).get("commandId") == command_id]
    calls = [row for row in owned if row.get("kind") == provider + ".call"]
    check(len(calls) == 1 and calls[0]["data"].get("toolName") == READ_TOOL, "The actual adapter did not make exactly one read-graph call")
    offered = [row["data"] for row in owned if row.get("kind") == provider + ".request" and row["data"].get("tools")]
    check(bool(offered), "The actual adapter recorded no tool advertisement")
    advertised = sorted({tool.get("name") for tool in offered[0]["tools"] if isinstance(tool, dict)} & ADVERTISED_TOOLS)
    return {"catalog": catalog["content"], "permissions": permissions["content"], "advertised": advertised}


def _inputs(entry):
    inputs = entry.get("inputs") if isinstance(entry, dict) else None
    check(isinstance(inputs, list), "A catalog entry has no declared inputs list")
    return [tuple(item.get(key) for key in ("referenceKey", "name", "type", "isRequired", "isSensitive", "isCredential", "uiHint")) for item in inputs]


def _masked(item):
    """What the designer masks: a sensitive or credential input, or one hinted as a password (Activities Design IsMasked)."""
    return item.get("isSensitive") is True or item.get("isCredential") is True or str(item.get("uiHint") or "").lower() == "password"


def _row(entry):
    return (entry.get("activityVersionId"), entry.get("available"), entry.get("version"), entry.get("availabilityReason"), _inputs(entry))


def require_catalog(content, owner_status, owner_entries, *, tenant_only):
    """The catalog equals the designer's own read for the same actor: entries, order, availability, versions and declared
    inputs (with isCredential), or the maximal prefix that fits the declared budget. tenant_only None: the owner withholds
    the catalog from this actor, and Weaver discloses no entry."""
    check(isinstance(content, dict), "The actor's catalog is not a projection")
    if tenant_only is None:
        check(owner_status == 403, "The designer's catalog read did not refuse an actor without activity design read")
        check(content.get("status") == "denied" and content.get("entries") == [] and content.get("totalCount") == 0,
              "A catalog the owner withholds was disclosed")
        return {"status": "denied", "totalCount": 0, "includedCount": 0, "truncated": False, "tenantOnly": None}
    check(owner_status == 200 and content.get("status") == "available", "The actor's catalog is not available")
    check(isinstance(owner_entries, list) and owner_entries, "The designer's catalog read is empty")
    owner = [_row(item) for item in owner_entries]
    entries = content.get("entries")
    check(isinstance(entries, list) and entries, "The disclosed catalog has no entries")
    disclosed = [_row(item) for item in entries]
    # Spec 1193 slice D: each disclosed input says whether the designer masks it, as the owner's own fields decide.
    check(all(isinstance(item, dict) and item.get("isMasked") is _masked(item) for entry in entries for item in entry.get("inputs") or []),
          "A disclosed input's isMasked differs from what the designer masks")
    # A bounded projection is the owner's prefix and says so; it never reorders, narrows or invents.
    check(content.get("totalCount") == len(owner) and content.get("includedCount") == len(disclosed)
          and content.get("truncated") is (len(disclosed) < len(owner)) and disclosed == owner[:len(disclosed)],
          "The disclosed catalog differs from the designer's catalog for the same actor and tenant")
    budget, used, following = content.get("budgetBytes"), content.get("includedBytes"), content.get("nextEntryBytes")
    check(all(type(value) is int and value >= 0 for value in (budget, used)) and used <= budget,
          "The disclosed catalog does not declare a budget it kept")
    if content["truncated"]:
        # Maximal: the prefix stopped only because the next entry would not fit the declared budget.
        check(type(following) is int and following > 0 and used + following > budget, "A truncated catalog is not the maximal prefix")
    else:
        check(following is None, "An untruncated catalog reports an entry left out")
    owned = [row for row in owner if row[0] == TENANT_ONLY_VERSION]
    shown = [row for row in disclosed if row[0] == TENANT_ONLY_VERSION]
    check(len(owned) == len(shown) == (1 if tenant_only else 0),
          "The tenant-restricted activity must appear in exactly its own tenant's designer and Weaver catalogs")
    return {"status": "available", "totalCount": len(owner), "includedCount": len(disclosed), "truncated": content["truncated"],
            "tenantOnly": bool(owned)}


def require_permissions(content, *, manage, read_status, manage_status, advertised, target):
    """The permissions equal the owning design API's read and manage answers, and the advertised tools follow them."""
    check(read_status == 200, "The actor cannot read the definition through the owning design API")
    check(manage_status == (200 if manage else 403), "The owning design API's manage decision differs from the actor's grants")
    expected = "allowed" if manage else "denied"
    tools = [READ_TOOL, LAYOUT_TOOL, BATCH_TOOL] if manage else [READ_TOOL]
    capabilities = ["workflow.explain", "workflow.propose-change"] if manage else ["workflow.explain"]
    check(isinstance(content, dict) and content.get("read") == "allowed" and content.get("manage") == expected
          and content.get("workflowDefinitionId") == target["resourceId"] and content.get("revision") == target["revision"]
          and content.get("tools") == tools and content.get("capabilities") == capabilities,
          "The disclosed permissions differ from the owning design API's answers")
    check(advertised == sorted(tools), "The actual adapter was offered tools that do not follow the disclosed permissions")
    return {"read": "allowed", "manage": expected, "tools": tools}


BROWSER_JOURNEY = "workflow-read-only"
BROWSER_OBSERVATIONS = {"catalogAvailable": True, "explainCompleted": True, "layoutOffered": False, "proposalCount": 0,
                        "nodeMoved": False, "draftUnchanged": True, "owningWrites": 0,
                        "permissionsDisclosed": {"manage": "denied", "tools": [READ_TOOL]}}


def require_read_only_browser(observed, commands, rows, *, provider):
    """The reader's actual Studio journey: explanation through the owning reads, and a layout request that the actual
    adapter was never offered, so no proposal, node move or owning write followed. The browser's own observation is
    checked against the adapter trace for each retained command, never against its own claims alone."""
    check(provider in PROVIDERS, "Unknown adapter")
    check(isinstance(observed, dict) and observed.get("journey") == BROWSER_JOURNEY and observed.get("status") == "passed"
          and not observed.get("failure") and not observed.get("pageErrors") and observed.get("provider") == provider,
          "The read-only Studio journey did not pass on this adapter")
    check(same(observed.get("observations"), BROWSER_OBSERVATIONS), "The read-only Studio journey observed another outcome")
    check(isinstance(observed.get("ownerBefore"), dict) and isinstance(observed.get("ownerAfter"), dict)
          and isinstance(observed.get("evidence"), list) and observed["evidence"], "The read-only Studio journey retained no owner evidence")
    by_tool = {}
    for item in commands:
        check(isinstance(item, dict) and isinstance(item.get("commandId"), str) and item.get("toolName") in WORKFLOW_TOOLS
              and item["toolName"] not in by_tool, "The read-only journey must retain exactly one command per workflow tool")
        by_tool[item["toolName"]] = item["commandId"]
    check(set(by_tool) == WORKFLOW_TOOLS, "The read-only journey must ask for both the explanation and the layout change")
    advertised = {}
    for tool, command_id in by_tool.items():
        owned = [row for row in rows if isinstance(row, dict) and (row.get("data") or {}).get("commandId") == command_id]
        offered = [row["data"] for row in owned if row.get("kind") == provider + ".request" and row["data"].get("tools")]
        check(bool(offered), "The actual adapter recorded no tool advertisement for the " + tool + " request")
        names = [sorted({entry.get("name") for entry in item["tools"] if isinstance(entry, dict)} & ADVERTISED_TOOLS) for item in offered]
        check(all(value == [READ_TOOL] for value in names), "The actual adapter was offered the layout tool for a read-only actor")
        calls = [row["data"] for row in owned if row.get("kind") == provider + ".call"]
        expected = 1 if tool == READ_TOOL else 0
        check(len(calls) == expected and all(call.get("toolName") == READ_TOOL for call in calls),
              "The actual adapter's calls do not follow the read-only actor's tools")
        advertised[tool] = names[0]
    return {"journey": BROWSER_JOURNEY, "observations": observed["observations"], "commands": by_tool, "advertised": advertised,
            "ownerBefore": observed["ownerBefore"], "ownerAfter": observed["ownerAfter"]}


def _read_only_browser(runtime, name, seed):
    """Runs the actual browser journey as the reader with a fresh normal-login state of its own."""
    _, identity = runtime.sign_in(READER)
    root = runtime.output(name)
    output = root / "observation"
    workflow = {key: seed[key] for key in ("definitionId", "activityId", "draftId", "definitionPath", "draftPath", "validationPath")}
    config = {"journey": BROWSER_JOURNEY, "provider": runtime.provider, "studioOrigin": runtime.studio_origin,
              "backendOrigin": runtime.backend_origin, "storageState": identity["storageState"],
              "commandFile": str(runtime.command_file), "output": str(output), "workflow": workflow}
    private_json(root / "config-private.json", config)
    transport = runtime.transport(name + "-protocol")
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    node = shutil.which("node")
    check(node, "The actual browser driver needs Node")
    runtime.run.run(name, [node, str(TOOL / "browser.mjs"), str(root / "config-private.json")], TOOL,
                    env={key: value for key, value in os.environ.items() if key in RUNTIME_ENVIRONMENT})
    observed = json.loads((output / "observation.json").read_text())
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the read-only journey")
    # The browser retains a frozen copy of each command it selected; the shared command file is never evidence.
    commands = [json.loads(path.read_text()) for path in sorted(output.glob("command-*.json"))]
    value = require_read_only_browser(observed, commands, trace["records"], provider=runtime.provider)
    observation = output / "observation.json"
    evidence = [*observed["evidence"], observed["ownerBefore"], observed["ownerAfter"], {"path": str(observation), "sha256": sha(observation)},
                runtime.save(root, "browser-inputs", browser_inputs(config)), trace["receipt"]]
    return value, evidence


LEGACY_PLACEHOLDERS = frozenset({
    "IWorkflowAgentContextProvider", "IWorkflowActivityCatalogProvider", "DefaultWorkflowActivityCatalogProvider",
    "IWorkflowRevisionProvider", "DefaultWorkflowRevisionProvider", "IWorkflowChangePermissionEvaluator",
    "DenyAllWorkflowChangePermissionEvaluator", "IWorkflowChangeProposalService", "DefaultWorkflowChangeProposalService",
    "IWorkflowGraphOperationBatchRiskClassifier", "DefaultWorkflowGraphOperationBatchRiskClassifier",
    "IWorkflowAuthoringAuditService", "DefaultWorkflowAuthoringAuditService", "WorkflowAgentContext", "WorkflowAgentPermissionSummary"})


def require_no_placeholders(generation):
    """SC-003 for the packaged consumer: its loaded Agent Workflows package ships no placeholder type, and the shell
    composes exactly the owning activity catalog, as the host's generation observation reports them."""
    agent = generation.get("workflowAgent") if isinstance(generation, dict) else None
    check(isinstance(agent, dict) and isinstance(agent.get("types"), list) and agent["types"], "The host does not report its workflow agent package")
    types = set(agent["types"])
    check("DefaultWorkflowWeaverContextProvider" in types and "WorkflowActivityCatalog" in types,
          "The reported types are not the loaded Agent Workflows package")
    check(not types & LEGACY_PLACEHOLDERS, "The packaged consumer still ships a legacy workflow placeholder")
    check(agent.get("activityAuthoringCatalogs") == ["HttpActivityAuthoringCatalog"],
          "The packaged consumer does not compose exactly the owning activity authoring catalog")
    return {"package": agent.get("assembly"), "placeholders": [], "activityAuthoringCatalogs": agent["activityAuthoringCatalogs"]}


@contextmanager
def _bounded(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native truth-seam qualification requires an interruptible main-thread stage deadline")
    check(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report[SECTION]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native truth-seam stage expired without retry: " + name)
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


def _actor(runtime, name, http, seed, *, manage, tenant_only):
    """The owning services' answers for one actor, then one actual read-graph turn as that actor."""
    links, capability = capability_links(http, "elsa.api.activity-design", runtime.output(name + "-capabilities"))
    path = owner_path(http, links["activity-catalog"])
    catalog_record, catalog_data = runtime.native.request(http, name + "-owner-catalog", path + ("&" if "?" in path else "?") + "availability=all")
    owner = json.loads(catalog_data) if catalog_record["status"] == 200 else {}
    read, _ = runtime.native.request(http, name + "-owner-read", seed["definitionPath"])
    preflight, _ = runtime.native.request(http, name + "-owner-manage", seed["draftPath"].rstrip("/") + "/promotion-preflight", {}, method="POST")
    revision = "qualification-unsaved-" + uuid.uuid4().hex[:8]
    attachment = snapshot_attachment(seed, revision)
    transport = runtime.transport(name, http=http)
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    session_response = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": "explain",
        "activeSurface": {"route": "/workflows/definitions"}, "autonomyMode": "auto-read-only"}).require_status(200)
    session = session_response.json()["data"]["sessionId"]
    command_id = str(uuid.uuid4())
    command = select_command(transport, command_id, READ_TOOL, {})
    message = transport.message(session, {"content": "Owned workflow truth-seam qualification request", "mode": "explain",
        "requestId": str(uuid.uuid4()), "requestedCapabilities": ["workflow.explain"], "contextAttachments": [attachment]}).require_status(200)
    stream = transport.collect_turn(session)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the advertised tools")
    turn = require_truth_turn([frame.value for frame in stream.frames], trace["records"], provider=runtime.provider,
                              command_id=command_id, target=attachment["target"])
    catalog = require_catalog(turn["catalog"], catalog_record["status"], owner.get("activities"), tenant_only=tenant_only)
    permissions = require_permissions(turn["permissions"], manage=manage, read_status=read["status"], manage_status=preflight["status"],
                                      advertised=turn["advertised"], target=attachment["target"])
    return {"commandId": command_id, "sessionId": session, "catalog": catalog, "permissions": permissions, "advertised": turn["advertised"],
            "owner": {"read": read["status"], "manage": preflight["status"]},
            "evidence": [capability, catalog_record["receipt"], read["receipt"], preflight["receipt"], session_response.receipt, command,
                         message.receipt, stream.receipt, trace["receipt"]]}


def admitted_context(response):
    """The context admitted with an accepted message: the wire is {data: {message: {context: [...]}, warnings}}, the
    shape Studio's agent client reads (agentClient.ts, message.context)."""
    data = response.get("data") if isinstance(response, dict) else None
    message = data.get("message") if isinstance(data, dict) else None
    context = message.get("context") if isinstance(message, dict) else None
    check(isinstance(context, list), "The accepted message carries no admitted context")
    return context


def _tenant_catalog(runtime, name, http, *, tenant_only):
    """The second tenant's catalogs: the designer's GET and the Weaver catalog admitted with a message over a target-free
    workflow request. No stream is opened, so no turn or provider call follows the admission."""
    links, capability = capability_links(http, "elsa.api.activity-design", runtime.output(name + "-capabilities"))
    path = owner_path(http, links["activity-catalog"])
    catalog_record, catalog_data = runtime.native.request(http, name + "-owner-catalog", path + ("&" if "?" in path else "?") + "availability=all")
    owner = json.loads(catalog_data) if catalog_record["status"] == 200 else {}
    transport = runtime.transport(name, http=http)
    session_response = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": "explain",
        "activeSurface": {"route": "/workflows/definitions"}, "autonomyMode": "auto-read-only"}).require_status(200)
    session = session_response.json()["data"]["sessionId"]
    request = {"id": str(uuid.uuid4()), "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "source": WORKFLOW_MODULE, "sourceId": "qualification-tenant-catalog",
               "label": "Workflow request", "contentType": "workflow.request", "sensitivity": "internal", "scope": "selection",
               "origin": "clientSnapshot", "content": {"requestKind": "workflow-definition", "referenceId": "qualification-tenant-catalog",
                                                       "intent": "Which activities can this tenant use?"}}
    message = transport.message(session, {"content": "Owned workflow truth-seam tenant catalog request", "mode": "explain",
        "requestId": str(uuid.uuid4()), "requestedCapabilities": ["workflow.explain"], "contextAttachments": [request]}).require_status(200)
    admitted = admitted_context(message.json())
    catalogs = [item for item in admitted if isinstance(item, dict) and item.get("contentType") == CATALOG_KIND]
    check(len(catalogs) == 1 and catalogs[0].get("origin") == "authoritativeRead" and catalogs[0].get("target") is None,
          "The second tenant's message admitted no server-read catalog")
    check(not any(isinstance(item, dict) and item.get("contentType") == PERMISSIONS_KIND for item in admitted),
          "A target-free workflow request must disclose no workflow permissions")
    catalog = require_catalog(catalogs[0].get("content"), catalog_record["status"], owner.get("activities"), tenant_only=tenant_only)
    return {"sessionId": session, "catalog": catalog, "permissions": None,
            "evidence": [capability, catalog_record["receipt"], session_response.receipt, message.receipt]}


def _case(runtime, provider):
    name = provider + "-workflow-truth-seams"
    evidence = list(runtime.select_provider(provider))
    _, admin = runtime.sign_in()
    generation, generation_record = runtime.native.generation(runtime.backend, name + "-registrations")
    registrations = require_no_placeholders(generation)
    evidence.append(generation_record["receipt"])
    seeds = {"home": seed_workflow(runtime.backend, runtime.output(name + "-home-workflow") / "workflow-seed")}
    clients = {}
    for _role, index, _seed, _manage, _tenant_only in ACTORS:
        clients[index], identity = runtime.sign_in(index)
        check(identity["tenantId"] == (runtime.principals[index]["tenant"]), "A truth-seam principal signed in to another tenant")
    home = runtime.principals[MANAGER]["tenant"]
    check(admin["tenantId"] == home != runtime.principals[TENANT_READER]["tenant"],
          "The admin's workflow must be in the home tenant and the tenant reader in another")
    for seed in seeds.values(): evidence.extend(seed["evidence"])
    evidence.extend(runtime.apply_features(name + "-select-seam", {SEAM_FEATURE: (True, {"Tenant": home})}))
    actors = {}
    try:
        for role, index, seed, manage, tenant_only in ACTORS:
            observed = (_tenant_catalog(runtime, name + "-" + role, clients[index], tenant_only=tenant_only) if seed is None
                        else _actor(runtime, name + "-" + role, clients[index], seeds[seed], manage=manage, tenant_only=tenant_only))
            evidence.extend(observed.pop("evidence"))
            actors[role] = observed
        # The reader's actual Studio journey over the same seeded workflow (FR-036 normal-host evidence).
        browser, browser_evidence = _read_only_browser(runtime, name + "-read-only-browser", seeds["home"])
        evidence.extend(browser_evidence)
    finally:
        evidence.extend(runtime.apply_features(name + "-deselect-seam", {SEAM_FEATURE: (False, {"Tenant": home})}))
    check(actors["reader"]["catalog"] == actors["manager"]["catalog"], "Same-tenant actors with catalog read saw different catalogs")
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "actors": actors,
            "registrations": registrations, "browser": browser, "evidence": evidence}


def qualify_workflow_truth_seams(runtime):
    run = runtime.run
    check(SECTION not in run.report, "Never resume or overwrite a prior workflow truth-seam qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "source": run.source,
               "caseIDs": [], "records": [], "stages": [],
               "driver": {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/truth_seam_journeys.py",
                          "sha256": sha(TOOL / "truth_seam_journeys.py")}}
    run.report[SECTION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            name = provider + "-workflow-truth-seams"
            section["activeCase"] = name
            with _bounded(runtime, name):
                value = _case(runtime, provider)
                for ref in value["evidence"]:
                    path = Path(ref["path"])
                    check(path.resolve().is_relative_to(run.root.resolve()) and path.is_file() and sha(path) == ref["sha256"],
                          "Workflow truth-seam evidence is missing, changed or outside the owned run")
                record = runtime.save(runtime.output(name + "-record"), "observation", value)
                section["caseIDs"].append(name)
                section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == len(EXPECTED_CASES),
              "Each workflow truth-seam case must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Workflow truth-seam qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()


def truth_seam_manifest_gaps(section, source=None):
    """Structural reasons a section cannot complete a witness; reads no files."""
    if not isinstance(section, dict):
        return ["The mandatory workflow truth-seam section is absent"]
    gaps = []
    names = section.get("caseIDs") if isinstance(section.get("caseIDs"), list) else []
    records = section.get("records") if isinstance(section.get("records"), list) else []
    if section.get("status") != "passed":
        gaps.append("Workflow truth-seam section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported workflow truth-seam provider evidence claim")
    if source is not None and section.get("source") != source:
        gaps.append("Workflow truth-seam section source differs from the witness source")
    if (sorted(names) != sorted(EXPECTED_CASES) or len(names) != len(EXPECTED_CASES)
            or [row.get("id") if isinstance(row, dict) else None for row in records] != names
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The workflow truth-seam cases were not each observed passed exactly once")
    return gaps


def require_workflow_truth_seams(section, owned_root=None, *, source=None):
    """Mandatory final gate: both adapters passed, and each retained record still says so for all three actors."""
    gaps = truth_seam_manifest_gaps(section, source)
    check(not gaps, "The workflow truth-seam inventory is mandatory on each actual adapter: " + "; ".join(gaps))
    if owned_root is not None:
        root = Path(owned_root).resolve()
        for row in section["records"]:
            path = Path(row["record"]["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file() and sha(path) == row["record"]["sha256"],
                  "Workflow truth-seam record is missing, changed or outside the owned run: " + row["id"])
            observed = json.loads(path.read_text())
            actors = observed.get("actors") if isinstance(observed.get("actors"), dict) else {}
            check(observed.get("id") == row["id"] and observed.get("status") == "passed"
                  and observed.get("adapter") == row["id"].split("-", 1)[0]
                  and sorted(actors) == sorted(role for role, *_ in ACTORS)
                  and all((actors[role].get("permissions") is None if manage is None
                           else (actors[role].get("permissions") or {}).get("manage") == ("allowed" if manage else "denied"))
                          and actors[role].get("catalog", {}).get("tenantOnly") is tenant_only
                          and actors[role].get("catalog", {}).get("status") == ("denied" if tenant_only is None else "available")
                          for role, _index, _seed, manage, tenant_only in ACTORS)
                  and observed.get("registrations", {}).get("placeholders") == []
                  and same((observed.get("browser") or {}).get("observations"), BROWSER_OBSERVATIONS),
                  "Workflow truth-seam record differs: " + row["id"])
    return section["records"]


def synthetic_actors():
    """The minimal record shape the final gate checks per actor; for synthetic self-tests only, never native evidence."""
    return {role: {"permissions": None if manage is None else {"manage": "allowed" if manage else "denied"},
                   "catalog": {"tenantOnly": tenant_only, "status": "denied" if tenant_only is None else "available"}}
            for role, _index, _seed, manage, tenant_only in ACTORS}


def synthetic_browser():
    """The minimal browser shape the final gate checks; for synthetic self-tests only."""
    return {"journey": BROWSER_JOURNEY, "observations": copy.deepcopy(BROWSER_OBSERVATIONS)}


def synthetic_registrations():
    """The minimal registrations shape the final gate checks; for synthetic self-tests only."""
    return {"package": "Elsa.Weaver.Workflows", "placeholders": [], "activityAuthoringCatalogs": ["HttpActivityAuthoringCatalog"]}
