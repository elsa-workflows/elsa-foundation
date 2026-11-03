"""Mandatory workflow-truth-seams qualification (spec 1193, story 4; T012).

On each actual adapter, Weaver's workflow context must carry the owning services' answers for the current actor and
tenant: the activity catalog (workflow.catalog) equals the designer's own catalog read, and the permissions
(workflow.permissions) equal the owning design API's read and manage decisions. Tool availability follows them: the
actual adapter is offered the reviewed layout tool only when the actor may manage the workflow.

Three dedicated principals run one read-graph turn each: a manager and a read-only reader in the admin's tenant, both
against the admin's seeded workflow, and a manager in a second tenant against a workflow it seeds itself. The host's
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
import signal
import threading
import time
import uuid

from artifacts import TOOL, check, sha
from authorization_journeys import select_command, snapshot_attachment
from journey_runtime import BASE_GRANTS, same
from owner_resources import capability_links, owner_path, seed_workflow


SECTION = "workflowTruthSeamQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 300
PROVIDERS = ("copilot", "anthropic")
EXPECTED_CASES = frozenset(provider + "-workflow-truth-seams" for provider in PROVIDERS)
SEAM_FEATURE = "QualificationTenantActivity"
TENANT_ONLY_VERSION = "qualification-tenant-only-activity-v1"
READ_TOOL = "workflow_read_graph"
LAYOUT_TOOL = "workflow_set_designer_position"
WORKFLOW_TOOLS = frozenset({READ_TOOL, LAYOUT_TOOL})
CATALOG_KIND = "workflow.catalog"
PERMISSIONS_KIND = "workflow.permissions"

# The three dedicated principals, appended after the original five so every earlier index keeps its meaning.
READER, MANAGER, TENANT_MANAGER = 5, 6, 7
READER_GRANTS = [*BASE_GRANTS, "workflows/design:read", "activities/design:read"]
MANAGER_GRANTS = [*BASE_GRANTS, "workflows/design:manage", "activities/design:read"]
# role -> (principal index, seeded workflow, may manage, sees the tenant-restricted activity)
ACTORS = (("manager", MANAGER, "home", True, True), ("reader", READER, "home", False, True),
          ("tenant-manager", TENANT_MANAGER, "other", True, False))


def truth_principals(principals, new_identity):
    """The three dedicated principals: reader and manager in the first principal's tenant, a manager in the third's."""
    check(len(principals) == READER and principals[0]["tenant"] != principals[2]["tenant"],
          "The truth-seam principals extend exactly the original five, which span two tenants")
    return [new_identity("witness-reader", principals[0]["tenant"], READER_GRANTS),
            new_identity("witness-manager", principals[0]["tenant"], MANAGER_GRANTS),
            new_identity("witness-tenant-manager", principals[2]["tenant"], MANAGER_GRANTS)]


def _attachments(frame, kind):
    payload = frame.get("payload") if isinstance(frame, dict) else None
    items = payload.get("attachments") if isinstance(payload, dict) else None
    check(isinstance(items, list), "Malformed admitted context frame")
    return [item for item in items if isinstance(item, dict) and item.get("contentType") == kind]


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
        check(any(item.get("target") == target for item in _attachments(frame, "workflow.definition")),
              "The admission lost the exact working-state target")
        current_catalog, current_permissions = _attachments(frame, CATALOG_KIND), _attachments(frame, PERMISSIONS_KIND)
        check(len(current_catalog) == 1 and current_catalog[0].get("origin") == "authoritativeRead" and current_catalog[0].get("target") is None,
              "Each admission must carry exactly one server-read catalog with no target")
        check(len(current_permissions) == 1 and current_permissions[0].get("origin") == "authoritativeRead"
              and current_permissions[0].get("target") == target, "Each admission must carry the exact target's server-read permissions")
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
    advertised = sorted({tool.get("name") for tool in offered[0]["tools"] if isinstance(tool, dict)} & WORKFLOW_TOOLS)
    return {"catalog": catalog["content"], "permissions": permissions["content"], "advertised": advertised}


def require_catalog(content, owner_entries, *, tenant_only):
    """The catalog equals the designer's own read for the same actor: entries, order, availability and versions."""
    check(isinstance(content, dict) and content.get("status") == "available", "The actor's catalog is not available")
    check(isinstance(owner_entries, list) and owner_entries, "The designer's catalog read is empty")
    owner = [(item["activityVersionId"], item["available"], item["version"]) for item in owner_entries]
    entries = content.get("entries")
    check(isinstance(entries, list) and entries, "The disclosed catalog has no entries")
    disclosed = [(item.get("activityVersionId"), item.get("available"), item.get("version")) for item in entries]
    # A bounded projection is the owner's prefix and says so; it never reorders, narrows or invents.
    check(content.get("totalCount") == len(owner) and content.get("includedCount") == len(disclosed)
          and content.get("truncated") is (len(disclosed) < len(owner)) and disclosed == owner[:len(disclosed)],
          "The disclosed catalog differs from the designer's catalog for the same actor and tenant")
    owned = [item for item in owner_entries if item["activityVersionId"] == TENANT_ONLY_VERSION]
    shown = [item for item in disclosed if item[0] == TENANT_ONLY_VERSION]
    check(len(owned) == len(shown) == (1 if tenant_only else 0),
          "The tenant-restricted activity must appear in exactly its own tenant's designer and Weaver catalogs")
    return {"totalCount": len(owner), "includedCount": len(disclosed), "truncated": content["truncated"], "tenantOnly": bool(owned)}


def require_permissions(content, *, manage, read_status, manage_status, advertised, target):
    """The permissions equal the owning design API's read and manage answers, and the advertised tools follow them."""
    check(read_status == 200, "The actor cannot read the definition through the owning design API")
    check(manage_status == (200 if manage else 403), "The owning design API's manage decision differs from the actor's grants")
    expected = "allowed" if manage else "denied"
    tools = [READ_TOOL, LAYOUT_TOOL] if manage else [READ_TOOL]
    capabilities = ["workflow.explain", "workflow.propose-change"] if manage else ["workflow.explain"]
    check(isinstance(content, dict) and content.get("read") == "allowed" and content.get("manage") == expected
          and content.get("workflowDefinitionId") == target["resourceId"] and content.get("revision") == target["revision"]
          and content.get("tools") == tools and content.get("capabilities") == capabilities,
          "The disclosed permissions differ from the owning design API's answers")
    check(advertised == sorted(tools), "The actual adapter was offered tools that do not follow the disclosed permissions")
    return {"read": "allowed", "manage": expected, "tools": tools}


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
    owner, catalog_record = runtime.native.json(http, name + "-owner-catalog", path + ("&" if "?" in path else "?") + "availability=all")
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
    catalog = require_catalog(turn["catalog"], owner.get("activities"), tenant_only=tenant_only)
    permissions = require_permissions(turn["permissions"], manage=manage, read_status=read["status"], manage_status=preflight["status"],
                                      advertised=turn["advertised"], target=attachment["target"])
    return {"commandId": command_id, "sessionId": session, "catalog": catalog, "permissions": permissions, "advertised": turn["advertised"],
            "owner": {"read": read["status"], "manage": preflight["status"]},
            "evidence": [capability, catalog_record["receipt"], read["receipt"], preflight["receipt"], session_response.receipt, command,
                         message.receipt, stream.receipt, trace["receipt"]]}


def _case(runtime, provider):
    name = provider + "-workflow-truth-seams"
    evidence = list(runtime.select_provider(provider))
    _, admin = runtime.sign_in()
    seeds = {"home": seed_workflow(runtime.backend, runtime.output(name + "-home-workflow") / "workflow-seed")}
    clients = {}
    for _role, index, _seed, _manage, _tenant_only in ACTORS:
        clients[index], identity = runtime.sign_in(index)
        check(identity["tenantId"] == (runtime.principals[index]["tenant"]), "A truth-seam principal signed in to another tenant")
    home = runtime.principals[MANAGER]["tenant"]
    check(admin["tenantId"] == home != runtime.principals[TENANT_MANAGER]["tenant"],
          "The admin's workflow must be in the home tenant and the tenant manager in another")
    seeds["other"] = seed_workflow(clients[TENANT_MANAGER], runtime.output(name + "-other-workflow") / "workflow-seed")
    for seed in seeds.values(): evidence.extend(seed["evidence"])
    evidence.extend(runtime.apply_features(name + "-select-seam", {SEAM_FEATURE: (True, {"Tenant": home})}))
    actors = {}
    try:
        for role, index, seed, manage, tenant_only in ACTORS:
            observed = _actor(runtime, name + "-" + role, clients[index], seeds[seed], manage=manage, tenant_only=tenant_only)
            evidence.extend(observed.pop("evidence"))
            actors[role] = observed
    finally:
        evidence.extend(runtime.apply_features(name + "-deselect-seam", {SEAM_FEATURE: (False, {"Tenant": home})}))
    check(actors["reader"]["catalog"] == actors["manager"]["catalog"], "Same-tenant actors with catalog read saw different catalogs")
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "actors": actors, "evidence": evidence}


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
                  and all(actors[role].get("permissions", {}).get("manage") == ("allowed" if manage else "denied")
                          and actors[role].get("catalog", {}).get("tenantOnly") is tenant_only
                          for role, _index, _seed, manage, tenant_only in ACTORS),
                  "Workflow truth-seam record differs: " + row["id"])
    return section["records"]


def synthetic_actors():
    """The minimal record shape the final gate checks per actor; for synthetic self-tests only, never native evidence."""
    return {role: {"permissions": {"manage": "allowed" if manage else "denied"}, "catalog": {"tenantOnly": tenant_only}}
            for role, _index, _seed, manage, tenant_only in ACTORS}
