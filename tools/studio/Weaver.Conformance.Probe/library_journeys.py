"""Mandatory workflow-library and workflow-templates qualification (spec 1193, stories 6 and 7; T036, SC-008).

On each actual adapter, Weaver's library contributions must hold against the packaged consumer's actual library store:

workflow-library
- the visible list (workflows.library) is admitted at its revision with the server's own workflows.library-organization
  read, and each library write (workflow_library_assign_tags, workflow_library_save_view, workflow_library_set_favorite)
  is a reviewed proposal even in full-auto, then executes through the library once approved;
- a lost response is resolved by its operation key: the host's test-only QualificationLibraryResponses seam lets the
  actual library commit a keyed tag creation (and, in its own turn, a keyed view creation) and then loses that response
  once; the tool rereads by the frozen key and reports the change applied, and the owner holds exactly one such tag
  (view), whose identity is the one the seam logged as committed;
- an actor with tag assignment but neither tag management nor view sharing is refused at preparation, with the module's
  own text, for a new tag and for a shared view, and nothing is created;
- deleting definitions is never offered: a commanded delete tool is not advertised to the actual adapter;
- with organization disabled for the backend (WorkflowsDesignApi's WorkflowOrganizationEnabled through the ordinary native
  feature apply), the organization read says so with the backend's reason, a tag change is refused at preparation, and
  ordinary list narrowing (workflow_library_set_list_state) is still proposed;
- in the actual packaged Studio (browser.mjs, workflow-library): a reviewed list narrowing is applied by the list page and
  reverted, and a reviewed prefill fills the open create dialog, whose Create the journey never presses.

workflow-templates
- a template editor's working state (workflows.template-editor) is admitted as its own resource, with its persisted
  revision; one reviewed Operation Batch targets the template (workflow-template, never a definition) and is denied;
- the visible template list and a create dialog listing the template are admitted by template identity and revision, and
  a prefill choosing the template is proposed; no definition is read, changed or created by any template turn;
- in the actual packaged Studio (browser.mjs, workflow-templates): the template editor applies an approved batch as one
  history step with autosave held, and persistence is the user's own Save with the template's expected revision.

Frame orders are each adapter's own, pinned from earlier witnesses (batch_journeys). An executed server write's
continuation is checked structurally, as lifecycle_journeys checks it: the originating turn starts once, completes, and
makes no new tool call or proposal. Nothing is retried.
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
from urllib.parse import quote

from artifacts import TOOL, check, sha
from authorization_journeys import select_command
from batch_journeys import (DENIED_CONTINUATION_FRAMES, OUTCOME_ROWS, PROPOSAL_FRAMES, REFUSED_FRAMES, UNOFFERED_ERROR, UNOFFERED_FRAMES,
                            UNOFFERED_MESSAGE, batch_snapshot)
from journey_runtime import BASE_GRANTS, browser_inputs, same
from owner_resources import capability_links, owner_path, seed_batch_workflow, seed_workflow
from runtime import RUNTIME_ENVIRONMENT, private_json
from truth_seam_journeys import CAPABILITIES_READ, same_target


SECTION = "workflowLibraryQualification"
EVIDENCE_CATEGORY = "deterministic-provider-protocol"
CASE_SECONDS = 600
PROVIDERS = ("copilot", "anthropic")
LIBRARY_CASES = frozenset(provider + "-workflow-library" for provider in PROVIDERS)
TEMPLATE_CASES = frozenset(provider + "-workflow-templates" for provider in PROVIDERS)
EXPECTED_CASES = LIBRARY_CASES | TEMPLATE_CASES
SEAM_FEATURE = "QualificationLibraryResponses"
DESIGN_FEATURE = "WorkflowsDesignApi"
WORKFLOW_MODULE = "Elsa.Studio.Workflows"
ASSIGN_TOOL = "workflow_library_assign_tags"
FAVORITE_TOOL = "workflow_library_set_favorite"
VIEW_TOOL = "workflow_library_save_view"
LIST_TOOL = "workflow_library_set_list_state"
PREFILL_TOOL = "workflow_create_dialog_prefill"
BATCH_TOOL = "workflow_apply_operation_batch"
# A tool the module never registers: deleting definitions is the library's own action, never Weaver's.
DELETE_TOOL = "workflow_library_delete_definitions"
LIBRARY_KIND, DIALOG_KIND, TEMPLATE_LIST_KIND = "workflows.library", "workflows.create-dialog", "workflows.template-list"
ORGANIZATION_KIND = "workflows.library-organization"
TEMPLATE_KIND = "workflows.template-editor"
LIBRARY_RESULT, LIST_RESULT, PREFILL_RESULT, BATCH_RESULT = "workflows.library-result", "workflows.list-state", "workflows.create-dialog-prefill", "workflows.batch-result"
DISABLED_REASON = "Workflow organization is disabled by this backend's administrator."
UNAVAILABLE_DIAGNOSTIC = "weaver.workflow.library_unavailable"

# The dedicated library actor, appended after the four truth-seam principals so every earlier index keeps its meaning:
# design read and tag assignment (so the library tools are offered and existing tags can be assigned), but neither tag
# management nor view sharing. api-capabilities:read discovers the library's own links, as Studio does.
LIBRARY_LIMITED = 9
LIBRARY_LIMITED_GRANTS = [*BASE_GRANTS, CAPABILITIES_READ, "workflows/design:read", "workflows/design/tag-assignments:manage"]

REFUSALS = {
    "no-tag-management": "creating it needs tag-management permission; nothing was changed.",
    "no-view-sharing": "View-sharing permission is required to save a shared view; nothing was changed.",
    "organization-disabled": DISABLED_REASON,
}
LIBRARY_NATIVE = ("tags-dropped", "view-dropped", "favorite", "list-state", "delete-unoffered", "no-tag-management", "no-view-sharing",
                  "organization-disabled", "organization-disabled-list")
TEMPLATE_NATIVE = ("template-batch", "template-prefill")
LIBRARY_BROWSER = "workflow-library"
TEMPLATE_BROWSER = "workflow-templates"
LIBRARY_OBSERVATIONS = {"listNarrowed": True, "listReverted": True, "dialogPrefilled": True, "createNotPressed": True, "noLibraryWrite": True}
TEMPLATE_OBSERVATIONS = {"applied": True, "templateTarget": True, "oneHistoryStep": True, "autosaveHeld": True, "savedWithExpectedRevision": True,
                         "definitionUntouched": True}
# An approved server write's continuation, pinned per adapter. Copilot's was observed on the c58c9f149 witness (tags-dropped,
# view-dropped and favorite): the same order as a denied proposal's continuation. Anthropic's is pinned to its own observed
# continuation order (the engine continues the turn the same way after any decided proposal) until its witness confirms it.
EXECUTED_CONTINUATION_FRAMES = {"copilot": ["started", "contextAdmitted", "messageDelta", "completed"],
                                "anthropic": DENIED_CONTINUATION_FRAMES["anthropic"]}
# An approved server write's adapter rows, as lifecycle_journeys pins them: advertise, call, then continue once with the result
# (Copilot's SDK also returns the pending call before the review).
EXECUTED_SEQUENCES = {"copilot": (["request", "call", "request", "continuation"], ["request", "call", "return", "request", "continuation"]),
                      "anthropic": (["request", "call", "request", "continuation"],)}


def library_principal(principals, new_identity):
    """The library actor, in the first principal's tenant, after the original five and the four truth-seam principals."""
    check(len(principals) == LIBRARY_LIMITED, "The library principal extends exactly the nine earlier principals")
    return [new_identity("witness-library-limited", principals[0]["tenant"], LIBRARY_LIMITED_GRANTS)]


# ---- Attachments: the pages' own client contexts, as Studio builds them (workflowLibraryAgent.ts). ----

def _client(prefix):
    return {"clientId": "qualification-" + prefix, "generationId": str(uuid.uuid4())}


def _attachment(kind, resource_type, resource_id, content, client):
    revision = content["revision"]
    return {"id": str(uuid.uuid4()), "moduleId": WORKFLOW_MODULE, "schemaVersion": "1", "source": "workflow-library", "sourceId": resource_id,
            "label": kind, "contentType": kind, "sensitivity": "internal", "scope": "screen", "origin": "clientSnapshot",
            "target": {"moduleId": WORKFLOW_MODULE, "resourceType": resource_type, "resourceId": resource_id, "revision": revision},
            "client": client, "content": content}


def library_attachment(rows, *, permissions, organization="enabled", search=None, selection=None):
    """The visible list: the native page's own rows and permission flags, every row selected unless a selection is given."""
    content = {"revision": "qualification-list-" + uuid.uuid4().hex,
               "query": {"search": search, "state": "active", "folderId": None, "unfiled": False, "includeDescendants": False,
                         "tagFilters": [], "favoritesOnly": False, "sort": "name", "publicationState": None},
               "view": None, "columns": ["name", "tags"], "selection": [row["id"] for row in rows] if selection is None else selection,
               "items": [{"id": row["id"], "name": row["name"][:256], "folderId": row.get("folderId"), "tags": row.get("tags") or [],
                          "favorite": row.get("favorite") is True, "publicationState": None, "organizationRevision": None} for row in rows],
               "totalCount": len(rows), "permissions": {key: permissions.get(key) is True for key in ("canAssignTags", "canManageFolders", "canManageTags", "canShareViews")},
               "organization": organization}
    # The module admits at most 16 KiB of page context, well under the host's 32 KiB attachment budget (#3710).
    check(len(json.dumps(content)) < 15 * 1024, "The qualification list context would exceed the module's 16 KiB bound")
    return _attachment(LIBRARY_KIND, "workflow-library", "library", content, _client("library"))


def dialog_attachment(templates=()):
    content = {"revision": "qualification-dialog-" + uuid.uuid4().hex, "name": "", "description": "", "rootActivityVersionId": None, "templateId": None,
               "templates": [{"id": item["id"], "name": item["name"], "description": item.get("description"), "revision": item["revision"]} for item in templates]}
    return _attachment(DIALOG_KIND, "workflow-create-dialog", "create-dialog", content, _client("dialog"))


def template_list_attachment(templates, selection):
    content = {"revision": "qualification-templates-" + uuid.uuid4().hex, "filter": {"search": None}, "selection": selection,
               "templates": [{"id": item["id"], "name": item["name"], "description": item.get("description"), "revision": item["revision"]} for item in templates]}
    return _attachment(TEMPLATE_LIST_KIND, "workflow-template-list", "templates", content, _client("templates"))


def template_snapshot(seed, template):
    """The template editor's working state: the batch seed's shape, as its own resource with its persisted revision."""
    attachment = batch_snapshot(seed, "qualification-template-unsaved-" + uuid.uuid4().hex[:8])
    content = {key: value for key, value in attachment["content"].items() if key not in ("workflowDefinitionId", "draftId")}
    content = {"templateId": template["id"], "templateRevision": template["revision"], **content}
    attachment.update(contentType=TEMPLATE_KIND, sourceId=template["id"], content=content,
                      target={**attachment["target"], "resourceType": "workflow-template", "resourceId": template["id"]})
    return attachment


def template_operations(seed):
    n = seed["nodes"]
    return [{"kind": "update", "activity": {"id": n["a"]}, "fields": {"displayName": "Template greeting"}},
            {"kind": "position", "activity": {"id": n["b"]}, "x": 480, "y": 200}]


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


def require_admitted(values, attachment, *, kinds=(), organization=None, diagnostics=()):
    """Every admission holds the exact page context by target identity, the server's organization read as expected, and
    only the listed non-required diagnostics."""
    admitted = [frame for frame in values if frame.get("kind") == "contextAdmitted"]
    check(bool(admitted), "The turn admitted no context")
    for frame in admitted:
        payload = frame.get("payload")
        check(isinstance(payload, dict) and isinstance(payload.get("attachments"), list), "Malformed admitted context frame")
        observed = sorted({item.get("code") for item in payload.get("diagnostics") or [] if isinstance(item, dict)})
        check(observed == sorted(set(diagnostics)), "Context admission reported other diagnostics: " + json.dumps(observed))
        items = [item for item in payload["attachments"] if isinstance(item, dict)]
        pages = [item for item in items if item.get("contentType") == attachment["contentType"]]
        check(len(pages) == 1 and same_target(pages[0].get("target"), attachment["target"]),
              "The admission lost the exact page context or its revision")
        for kind in kinds:
            check(any(item.get("contentType") == kind for item in items), "The admission lacks the " + kind + " context")
        if organization is not None:
            reads = [item for item in items if item.get("contentType") == ORGANIZATION_KIND]
            check(len(reads) == 1 and reads[0].get("origin") in ("authoritativeRead", "AuthoritativeRead"), "The library's own authority read is missing")
            content = reads[0].get("content") or {}
            check(content.get("status") == organization["status"] and content.get("reason") == organization.get("reason")
                  and content.get("refused") == ["delete", "permanent-delete"], "The organization read differs: " + json.dumps(content)[:300])
    return admitted


def require_proposal(values, rows, *, provider, command_id, tool, location, target, attachment, organization=None, diagnostics=()):
    """One reviewed proposal of the commanded tool for the exact target, awaiting approval even in full-auto."""
    check(provider in PROVIDERS, "Unknown adapter")
    require_admitted(values, attachment, organization=organization, diagnostics=diagnostics)
    _kinds(values, PROPOSAL_FRAMES[provider], "The " + tool + " turn")
    proposal = values[-2].get("payload")
    prepared = proposal.get("preparedInvocation") if isinstance(proposal, dict) else None
    check(isinstance(prepared, dict) and prepared.get("toolName") == tool and prepared.get("requiresApproval") is True
          and proposal.get("requiresApproval") is True and str(proposal.get("status", "")).replace("-", "").lower() == "awaitingapproval"
          and prepared.get("executionLocation") == location and prepared.get("risk") == "reviewRequired"
          and (target is None or same_target(prepared.get("target"), target)) and values[-1].get("proposalId") == proposal.get("id"),
          "The " + tool + " proposal is not a reviewed " + location + " change of the expected target")
    owned = _owned(rows, provider, command_id)
    calls = [row for row in owned if row.get("kind") == provider + ".call"]
    check(tool in _offered(owned, provider) and len(calls) == 1 and calls[0]["data"].get("toolName") == tool,
          "The actual adapter was not offered " + tool + " or did not call it exactly once")
    return proposal


def require_refused(values, rows, *, provider, command_id, tool, attachment, refusal, organization=None, diagnostics=()):
    """Preparation refused with the module's own text; no review followed and nothing changed."""
    check(provider in PROVIDERS, "Unknown adapter")
    require_admitted(values, attachment, organization=organization, diagnostics=diagnostics)
    _kinds(values, REFUSED_FRAMES[provider], "The refused " + tool + " turn")
    completed = next(frame.get("payload") for frame in values if frame.get("kind") == "toolCallCompleted")
    result = completed.get("result") if isinstance(completed, dict) else None
    check(isinstance(completed, dict) and completed.get("toolName") == tool and completed.get("succeeded") is False
          and isinstance(result, dict) and result.get("succeeded") is False and result.get("payload") is None and result.get("receipt") is None,
          "The " + tool + " tool did not complete once, refused, with no payload or receipt")
    content = completed.get("summary")
    check(isinstance(content, str) and refusal in content, "Refused for another reason: " + str(content)[:300])
    owned = _owned(rows, provider, command_id)
    check(tool in _offered(owned, provider) and sum(row.get("kind") == provider + ".call" for row in owned) == 1
          and any(row.get("kind") == provider + OUTCOME_ROWS[provider] for row in owned),
          "The actual adapter did not call " + tool + " once and receive its refusal")
    return content[:400]


def require_unoffered(values, rows, *, provider, command_id, tool, attachment):
    check(provider in PROVIDERS, "Unknown adapter")
    require_admitted(values, attachment)
    _kinds(values, UNOFFERED_FRAMES[provider], "The unoffered " + tool + " turn")
    error = values[-1].get("error")
    check(isinstance(error, dict) and error.get("code") == UNOFFERED_ERROR[provider] and error.get("message") == UNOFFERED_MESSAGE,
          "The turn did not end because the actual adapter was never offered " + tool)
    owned = _owned(rows, provider, command_id)
    check(tool not in _offered(owned, provider) and not any(row.get("kind") == provider + ".call" for row in owned),
          "The actual adapter was offered or called " + tool)
    return {"offered": False}


def require_executed(response, proposal, *, tool, status="applied"):
    """The approved server write's own outcome: executed, with a server-persisted receipt bound to the reviewed target."""
    data = (response or {}).get("data") if isinstance(response, dict) else None
    result = (data or {}).get("toolResult") if isinstance(data, dict) else None
    prepared = proposal["preparedInvocation"]
    receipt = (result or {}).get("receipt") or {}
    payload = (result or {}).get("payload") or {}
    check(isinstance(data, dict) and data.get("executed") is True and data.get("status") == "executed" and isinstance(result, dict)
          and result.get("succeeded") is True and result.get("toolName") == tool and payload.get("typeId") == LIBRARY_RESULT
          and str(receipt.get("scope", "")).lower() in ("serverpersisted", "server-persisted") and receipt.get("invocationId") == prepared["invocationId"]
          and same_target(receipt.get("before"), prepared["target"]) and (payload.get("data") or {}).get("status") == status,
          "The approved " + tool + " did not execute through the library with a persisted receipt: " + json.dumps(data)[:400])
    return payload["data"]


def require_continuation(values, provider=None):
    """The originating turn continues once with the outcome and completes, with no new call or review (as lifecycle_journeys
    checks it), in the adapter's own pinned order when one is given."""
    values = _frames(values)
    if provider is not None:
        _kinds(values, EXECUTED_CONTINUATION_FRAMES[provider], "The executed write's continuation")
    check(values[0].get("kind") == "started" and values[-1].get("kind") == "completed" and sum(frame.get("kind") == "started" for frame in values) == 1
          and not any(frame.get("kind") in ("proposalCreated", "toolCallRequested", "toolCallStarted", "toolApprovalRequested", "error", "turnCancelled")
                      for frame in values), "The outcome did not continue and complete the originating native turn")
    return [frame.get("kind") for frame in values]


def require_executed_rows(rows, *, provider, command_id, tool):
    owned = [row for row in _owned(rows, provider, command_id) if row["kind"].split(".", 1)[1] in ("request", "call", "return", "continuation")]
    sequence = [row["kind"].split(".", 1)[1] for row in owned]
    check(sequence in [list(item) for item in EXECUTED_SEQUENCES[provider]], "The actual adapter sequence is " + ",".join(sequence))
    check(owned[1]["data"].get("toolName") == tool and not owned[-2]["data"].get("tools"),
          "The adapter called another tool or a continuation advertised callable tools again")
    return sequence


def require_dropped(log_lines, *, kind, operation_key, committed_id):
    """The seam logged exactly one lost response of this kind, for the frozen key, after the owner committed this identity."""
    entries = [json.loads(line) for line in log_lines if line.strip()]
    selected = [entry for entry in entries if entry.get("kind") == kind]
    check(len(selected) == 1 and selected[0] == {"kind": kind, "operationKey": operation_key, "committedId": committed_id},
          "The " + kind + " response was not lost exactly once after the owner committed the reported resource")
    return selected[0]


def frozen_drop(save, output, log_path, *, kind, operation_key, committed_id):
    """Checks the seam's lost-response log and retains a frozen copy of exactly the lines read now.

    The log is one shared, append-only file: a later drop appends to it, so a reference to the log itself would no longer
    match its hash (the 072001a53 witness failed on exactly that). The evidence is this observation's own copy.
    """
    lines = Path(log_path).read_text().splitlines()
    entry = require_dropped(lines, kind=kind, operation_key=operation_key, committed_id=committed_id)
    return entry, save(output, "dropped-responses-" + kind, {"entries": [json.loads(line) for line in lines if line.strip()]})


# ---- Native runner. ----

@contextmanager
def _bounded(runtime, name):
    check(threading.current_thread() is threading.main_thread() and hasattr(signal, "setitimer"),
          "Native library qualification requires an interruptible main-thread stage deadline")
    check(signal.getitimer(signal.ITIMER_REAL) == (0.0, 0.0), "Do not replace another qualification stage's active deadline")
    started = time.monotonic()
    stage = {"name": name, "status": "running", "boundSeconds": CASE_SECONDS}
    runtime.run.report[SECTION]["stages"].append(stage)
    runtime.run.save()
    def expired(_signum, _frame):
        raise TimeoutError("Native library stage expired without retry: " + name)
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


def _turn(runtime, name, http, attachments, tool, arguments, capability):
    """One native turn whose adapter calls exactly this tool with exactly these arguments; a fresh full-auto session."""
    transport = runtime.transport(name, http=http)
    offset = transport.trace_file.stat().st_size if transport.trace_file.exists() else 0
    created = transport.create_session({"conversationId": str(uuid.uuid4()), "mode": "build",
                                        "activeSurface": {"route": "/workflows/definitions"}, "autonomyMode": "full-auto"}).require_status(200)
    session = created.json()["data"]["sessionId"]
    command_id = str(uuid.uuid4())
    command = select_command(transport, command_id, tool, arguments)
    message = transport.message(session, {"content": "Owned workflow library qualification request", "mode": "build", "requestId": str(uuid.uuid4()),
        "requestedCapabilities": [capability], "contextAttachments": attachments}).require_status(200)
    accepted = message.json()["data"]["message"]
    check(all(any(isinstance(item, dict) and item.get("contentType") == attachment["contentType"] and same_target(item.get("target"), attachment["target"])
                  for item in accepted.get("context") or []) for attachment in attachments),
          "The accepted message does not carry every admitted page context, as data.message.context records it")
    stream = transport.collect_turn(session)
    trace = transport.protocol_trace(offset)
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the " + name + " turn")
    frames = _frames([frame.value for frame in stream.frames])
    return {"transport": transport, "session": session, "commandId": command_id, "frames": frames, "rows": trace["records"],
            "offset": offset, "evidence": [created.receipt, command, message.receipt, stream.receipt, trace["receipt"]]}


def _deny(turn, proposal, provider):
    denied = turn["transport"].deny(proposal["id"]).require_status(200)
    continuation = turn["transport"].collect_turn(turn["session"])
    _kinds(_frames([frame.value for frame in continuation.frames]), DENIED_CONTINUATION_FRAMES[provider], "The denied proposal's continuation")
    return [denied.receipt, continuation.receipt]


def _approve_execute(runtime, turn, proposal, tool, *, status="applied"):
    transport = turn["transport"]
    approved = transport.approve(proposal["id"], revision=proposal["baseRevision"]).require_status(200)
    executed = transport.execute(proposal["id"], revision=proposal["baseRevision"]).require_status(200)
    data = require_executed(executed.json(), proposal, tool=tool, status=status)
    continuation = transport.collect_turn(turn["session"])
    require_continuation([frame.value for frame in continuation.frames], runtime.provider)
    trace = transport.protocol_trace(turn["offset"])
    check(trace["incompleteTailBytes"] == 0, "An incomplete adapter trace cannot establish the executed " + tool)
    require_executed_rows(trace["records"], provider=runtime.provider, command_id=turn["commandId"], tool=tool)
    return data, [approved.receipt, executed.receipt, continuation.receipt, trace["receipt"]]


def _links(runtime, name):
    links, receipt = capability_links(runtime.backend, "elsa.api.workflow-design", runtime.output(name) / "links")
    check(all(key in links for key in ("workflow-tags", "workflow-views", "workflow-organization", "workflow-definitions-page", "workflow-templates", "workflow-template")),
          "The native library witness needs the library's tag, view, organization, page and template relations")
    return links, receipt


def _rows(runtime, client, name, links, seeds):
    """The native page's own rows and permission flags for this actor, restricted to the seeded definitions."""
    page, ref = runtime.native.json(client, name, owner_path(runtime.backend, links["workflow-definitions-page"]) + "?pageSize=100")
    ids = {seed["definitionId"] for seed in seeds}
    rows = [item for item in page["items"] if item["id"] in ids]
    check(len(rows) == len(ids), "The native page does not list every seeded definition to this actor")
    return rows, page["permissions"], ref["receipt"]


def _tags_named(runtime, client, name, links, tag_name):
    page, ref = runtime.native.json(client, name, owner_path(runtime.backend, links["workflow-tags"]) + "?search=" + quote(tag_name, safe=""))
    return [item for item in page["items"] if item["name"].upper() == tag_name.upper()], ref["receipt"]


def _views_named(runtime, client, name, links, view_name):
    page, ref = runtime.native.json(client, name, owner_path(runtime.backend, links["workflow-views"]))
    return [item for item in page["items"] if item["name"] == view_name], ref["receipt"]


def _mode(path, drop):
    private_json(path, {"drop": drop})


def _library_native(runtime, name, clients, links, seeds, seam):
    records, evidence = {}, []
    suffix = secrets.token_hex(4)
    existing = "Qualification critical " + suffix
    created, created_ref = runtime.native.json(runtime.backend, name + "-existing-tag", owner_path(runtime.backend, links["workflow-tags"]), {"name": existing}, method="POST")
    evidence.append(created_ref["receipt"])
    rows, permissions, ref = _rows(runtime, runtime.backend, name + "-admin-page", links, seeds)
    evidence.append(ref)
    available = {"status": "available", "reason": None}
    ids = [row["id"] for row in rows]

    # A lost tag-creation response, resolved by its key to exactly one tag; the assignment of both tags then proceeds.
    new_tag = "Qualification brand new " + suffix
    _mode(seam["mode"], "create-tag")
    list_attachment = library_attachment(rows, permissions=permissions)
    turn = _turn(runtime, name + "-tags-dropped", runtime.backend, [list_attachment], ASSIGN_TOOL,
                 {"definitionIds": ids, "add": [{"tag": existing}, {"tag": new_tag}]}, "workflow.library-organize")
    proposal = require_proposal(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=ASSIGN_TOOL,
                                location="server", target=None, attachment=list_attachment, organization=available)
    prepared = proposal["preparedInvocation"]
    preview = (prepared.get("preview") or {}).get("data") or {}
    creates = (prepared.get("arguments") or {}).get("creates") or []
    check(preview.get("createsTags") == 1 and len(creates) == 1 and creates[0].get("name") == new_tag
          and any(item.get("change") == "create-and-add" and item.get("tag") == new_tag for item in preview.get("changes") or []),
          "The review does not show the one tag creation explicitly")
    data, refs = _approve_execute(runtime, turn, proposal, ASSIGN_TOOL)
    tags, tags_ref = _tags_named(runtime, runtime.backend, name + "-tags-after", links, new_tag)
    check(len(tags) == 1, "The lost tag-creation response did not resolve to exactly one tag")
    dropped, dropped_ref = frozen_drop(runtime.save, turn["transport"].output, seam["log"], kind="create-tag",
                                       operation_key=creates[0]["operationKey"], committed_id=tags[0]["id"])
    check([item.get("status") for item in data.get("creations") or []] == ["applied"] and data["creations"][0].get("tagId") == tags[0]["id"]
          and sorted(item.get("definitionId") for item in data.get("targets") or []) == sorted(ids)
          and all(item.get("status") == "applied" for item in data["targets"]), "The tag change did not report every target applied")
    for seed in seeds:
        organization, org_ref = runtime.native.json(runtime.backend, name + "-organization-" + seed["definitionId"][:8],
                                                    owner_path(runtime.backend, links["workflow-organization"], definitionId=seed["definitionId"]))
        check({created["id"], tags[0]["id"]} <= {item["tagId"] for item in organization["tags"]}, "A target does not hold both tags")
        evidence.append(org_ref["receipt"])
    records["tags-dropped"] = {"control": "tags-dropped", "resolvedByKey": True, "tags": 1, "targets": len(ids)}
    evidence += [*turn["evidence"], *refs, tags_ref, dropped_ref]

    # A lost view-save response, resolved by its key to exactly one view.
    view_name = "Qualification view " + suffix
    _mode(seam["mode"], "save-view")
    list_attachment = library_attachment(rows, permissions=permissions, search="qualification")
    turn = _turn(runtime, name + "-view-dropped", runtime.backend, [list_attachment], VIEW_TOOL, {"name": view_name, "shared": False}, "workflow.library-organize")
    proposal = require_proposal(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=VIEW_TOOL,
                                location="server", target=None, attachment=list_attachment, organization=available)
    key = (proposal["preparedInvocation"].get("arguments") or {}).get("operationKey")
    check(isinstance(key, str) and key, "The view save was not frozen with its operation key")
    data, refs = _approve_execute(runtime, turn, proposal, VIEW_TOOL)
    views, views_ref = _views_named(runtime, runtime.backend, name + "-views-after", links, view_name)
    check(len(views) == 1 and views[0]["query"].get("searchTerm") == "qualification" and (data.get("view") or {}).get("id") == views[0]["id"],
          "The lost view-save response did not resolve to exactly one view of the visible query")
    _, dropped_ref = frozen_drop(runtime.save, turn["transport"].output, seam["log"], kind="save-view", operation_key=key, committed_id=views[0]["id"])
    records["view-dropped"] = {"control": "view-dropped", "resolvedByKey": True, "views": 1}
    evidence += [*turn["evidence"], *refs, views_ref, dropped_ref]
    _mode(seam["mode"], "none")

    # An idempotent favorite set, per target.
    list_attachment = library_attachment(rows, permissions=permissions)
    turn = _turn(runtime, name + "-favorite", runtime.backend, [list_attachment], FAVORITE_TOOL, {"definitionIds": [ids[0]], "favorite": True}, "workflow.library-organize")
    proposal = require_proposal(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=FAVORITE_TOOL,
                                location="server", target=None, attachment=list_attachment, organization=available)
    data, refs = _approve_execute(runtime, turn, proposal, FAVORITE_TOOL)
    check([(item.get("definitionId"), item.get("status")) for item in data.get("targets") or []] == [(ids[0], "applied")], "The favorite was not applied per target")
    records["favorite"] = {"control": "favorite", "targets": 1}
    evidence += [*turn["evidence"], *refs]

    # Ordinary list narrowing: a reviewed change to the page only.
    list_attachment = library_attachment(rows, permissions=permissions)
    turn = _turn(runtime, name + "-list-state", runtime.backend, [list_attachment], LIST_TOOL, {"search": "qualification"}, "workflow.library-organize")
    proposal = require_proposal(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=LIST_TOOL,
                                location="clientWorkingState", target=list_attachment["target"], attachment=list_attachment, organization=available)
    evidence += [*turn["evidence"], *_deny(turn, proposal, runtime.provider)]
    records["list-state"] = {"control": "list-state", "location": "clientWorkingState"}

    # Deleting definitions is never offered.
    list_attachment = library_attachment(rows, permissions=permissions)
    turn = _turn(runtime, name + "-delete-unoffered", runtime.backend, [list_attachment], DELETE_TOOL, {"definitionIds": ids}, "workflow.library-organize")
    records["delete-unoffered"] = {"control": "delete-unoffered", **require_unoffered(turn["frames"], turn["rows"], provider=runtime.provider,
                                                                                         command_id=turn["commandId"], tool=DELETE_TOOL, attachment=list_attachment)}
    evidence += turn["evidence"]

    # An actor with tag assignment but neither tag management nor view sharing.
    limited_rows, limited_permissions, ref = _rows(runtime, clients[LIBRARY_LIMITED], name + "-limited-page", links, seeds)
    check(limited_permissions.get("canAssignTags") is True and limited_permissions.get("canManageTags") is False and limited_permissions.get("canShareViews") is False,
          "The limited actor's native flags are not tag assignment without tag management or view sharing")
    evidence.append(ref)
    refused_tag = "Qualification refused " + suffix
    for control, tool, arguments in (("no-tag-management", ASSIGN_TOOL, {"definitionIds": [ids[0]], "add": [{"tag": refused_tag}]}),
                                     ("no-view-sharing", VIEW_TOOL, {"name": "Qualification shared " + suffix, "shared": True})):
        list_attachment = library_attachment(limited_rows, permissions=limited_permissions)
        turn = _turn(runtime, name + "-" + control, clients[LIBRARY_LIMITED], [list_attachment], tool, arguments, "workflow.library-organize")
        records[control] = {"control": control, "refusal": require_refused(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"],
                                                                          tool=tool, attachment=list_attachment, refusal=REFUSALS[control], organization=available)}
        evidence += turn["evidence"]
    tags, ref = _tags_named(runtime, runtime.backend, name + "-refused-tag-absent", links, refused_tag)
    check(tags == [], "A refused tag creation created a tag")
    views, view_ref = _views_named(runtime, clients[LIBRARY_LIMITED], name + "-shared-view-absent", links, "Qualification shared " + suffix)
    check(views == [], "A refused shared view was saved")
    evidence += [ref, view_ref]

    # Organization disabled for the backend: the read says why, organization tools refuse, ordinary list support remains.
    catalog, catalog_ref = runtime.native.json(runtime.backend, name + "-design-feature", "/modularity/features")
    original = next(item["configuration"] for item in catalog["features"] if item["id"] == DESIGN_FEATURE)
    evidence += [catalog_ref["receipt"], *runtime.apply_features(name + "-organization-off", {DESIGN_FEATURE: (True, {**original, "WorkflowOrganizationEnabled": False})})]
    try:
        disabled = {"status": "unavailable", "reason": DISABLED_REASON}
        list_attachment = library_attachment(rows, permissions={}, organization="disabled")
        turn = _turn(runtime, name + "-organization-disabled", runtime.backend, [list_attachment], ASSIGN_TOOL,
                     {"definitionIds": [ids[0]], "add": [{"tag": existing}]}, "workflow.library-organize")
        records["organization-disabled"] = {"control": "organization-disabled", "refusal": require_refused(turn["frames"], turn["rows"], provider=runtime.provider,
            command_id=turn["commandId"], tool=ASSIGN_TOOL, attachment=list_attachment, refusal=REFUSALS["organization-disabled"],
            organization=disabled, diagnostics=[UNAVAILABLE_DIAGNOSTIC])}
        evidence += turn["evidence"]
        list_attachment = library_attachment(rows, permissions={}, organization="disabled")
        turn = _turn(runtime, name + "-organization-disabled-list", runtime.backend, [list_attachment], LIST_TOOL, {"search": "qualification"}, "workflow.library-organize")
        proposal = require_proposal(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=LIST_TOOL,
                                    location="clientWorkingState", target=list_attachment["target"], attachment=list_attachment,
                                    organization=disabled, diagnostics=[UNAVAILABLE_DIAGNOSTIC])
        evidence += [*turn["evidence"], *_deny(turn, proposal, runtime.provider)]
        records["organization-disabled-list"] = {"control": "organization-disabled-list", "location": "clientWorkingState"}
    finally:
        evidence += runtime.apply_features(name + "-organization-on", {DESIGN_FEATURE: (True, original)})
    return records, evidence


def _seed_template(runtime, name, links, seed):
    """A Reusable Workflow Template holding the batch seed's draft, created through the template service itself."""
    definition, ref = runtime.native.json(runtime.backend, name + "-definition", seed["definitionPath"])
    draft = definition["draft"]
    body = {"name": "Qualification template " + secrets.token_hex(4), "description": "Owned spec 1193 template qualification.",
            "content": {"state": draft["state"], "layout": draft.get("layout") or [], "activityPresentation": draft.get("activityPresentation") or []}}
    created, created_ref = runtime.native.json(runtime.backend, name + "-template", owner_path(runtime.backend, links["workflow-templates"]), body, method="POST")
    check(isinstance(created.get("id"), str) and isinstance(created.get("revision"), str), "The template service did not create the template")
    return created, [ref["receipt"], created_ref["receipt"]]


def _template_native(runtime, name, links, seed, template):
    records, evidence = {}, []
    before, before_data = runtime.native.request(runtime.backend, name + "-definition-before", seed["definitionPath"])
    template_path = owner_path(runtime.backend, links["workflow-template"], templateId=template["id"])
    # One reviewed batch for the template editor's working state: its own resource, never a definition.
    attachment = template_snapshot(seed, template)
    turn = _turn(runtime, name + "-template-batch", runtime.backend, [attachment], BATCH_TOOL, {"operations": template_operations(seed)}, "workflow.propose-change")
    proposal = require_proposal(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=BATCH_TOOL,
                                location="clientWorkingState", target=attachment["target"], attachment=attachment)
    preview = (proposal["preparedInvocation"].get("preview") or {}).get("data") or {}
    check(preview.get("templateId") == template["id"] and preview.get("templateRevision") == template["revision"]
          and "workflowDefinitionId" not in preview and "draftId" not in preview, "The template batch's review names a definition or lost the template revision")
    evidence += [*turn["evidence"], *_deny(turn, proposal, runtime.provider)]
    records["template-batch"] = {"control": "template-batch", "resourceType": "workflow-template"}
    # The template list and a create dialog offering the template; a prefill chooses it.
    listed = [{"id": template["id"], "name": template["name"], "description": template.get("description"), "revision": template["revision"]}]
    templates, dialog = template_list_attachment(listed, template["id"]), dialog_attachment(listed)
    turn = _turn(runtime, name + "-template-prefill", runtime.backend, [templates, dialog], PREFILL_TOOL,
                 {"name": "From the qualification template", "templateId": template["id"]}, "workflow.library-organize")
    proposal = require_proposal(turn["frames"], turn["rows"], provider=runtime.provider, command_id=turn["commandId"], tool=PREFILL_TOOL,
                                location="clientWorkingState", target=dialog["target"], attachment=dialog)
    require_admitted(turn["frames"], templates)
    admitted = [item for frame in turn["frames"] if frame.get("kind") == "contextAdmitted" for item in frame["payload"]["attachments"]
                if isinstance(item, dict) and item.get("contentType") == TEMPLATE_LIST_KIND]
    check(all((item.get("content") or {}).get("templates") == [{**listed[0]}] and "workflowDefinitionId" not in json.dumps(item) for item in admitted),
          "The template list was not admitted by template identity and persisted revision only")
    evidence += [*turn["evidence"], *_deny(turn, proposal, runtime.provider)]
    records["template-prefill"] = {"control": "template-prefill", "templateId": template["id"]}
    after, after_data = runtime.native.request(runtime.backend, name + "-definition-after", seed["definitionPath"])
    check(before["status"] == after["status"] == 200 and json.loads(after_data)["draft"] == json.loads(before_data)["draft"], "A template turn changed a workflow definition")
    current, current_ref = runtime.native.json(runtime.backend, name + "-template-after", template_path)
    check(current["revision"] == template["revision"], "A denied template proposal changed the template")
    evidence += [before["receipt"], after["receipt"], current_ref["receipt"]]
    return records, evidence


def _browser(runtime, name, journey, workflow, extra):
    _, identity = runtime.sign_in()
    root = runtime.output(name)
    output = root / "observation"
    config = {"journey": journey, "provider": runtime.provider, "studioOrigin": runtime.studio_origin, "backendOrigin": runtime.backend_origin,
              "storageState": identity["storageState"], "commandFile": str(runtime.command_file), "output": str(output), "workflow": workflow, **extra}
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


BROWSER = {LIBRARY_BROWSER: (LIBRARY_OBSERVATIONS, {"narrow": LIST_TOOL, "prefill": PREFILL_TOOL}),
           TEMPLATE_BROWSER: (TEMPLATE_OBSERVATIONS, {"applied": BATCH_TOOL})}


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
        owned = _owned(rows, provider, command_id)
        calls = [row for row in owned if row.get("kind") == provider + ".call"]
        check(tool in _offered(owned, provider) and len(calls) == 1 and calls[0]["data"].get("toolName") == tool,
              "The actual adapter did not call " + tool + " exactly once for the " + step + " step")
    return {"journey": journey, "observations": observed["observations"], "commands": selected,
            "ownerBefore": observed["ownerBefore"], "ownerAfter": observed["ownerAfter"]}


def _library_case(runtime, provider):
    name = provider + "-workflow-library"
    evidence = list(runtime.select_provider(provider))
    _, admin = runtime.sign_in()
    clients = {}
    clients[LIBRARY_LIMITED], limited = runtime.sign_in(LIBRARY_LIMITED)
    # #3701: the packaged consumer stores every definition under the default scope, so every actor shares that tenant.
    check(limited["tenantId"] == admin["tenantId"], "The limited library actor must share the seeded workflows' tenant")
    seeds = [seed_workflow(runtime.backend, runtime.output(name + "-workflow-" + str(index)) / "workflow-seed") for index in (1, 2)]
    for seed in seeds: evidence.extend(seed["evidence"])
    links, links_ref = _links(runtime, name)
    evidence.append(links_ref)
    root = runtime.output(name + "-seam")
    seam = {"mode": root / "mode-private.json", "log": root / "dropped-private.log"}
    _mode(seam["mode"], "none")
    seam["log"].write_text("")
    seam["log"].chmod(0o600)
    evidence.extend(runtime.apply_features(name + "-select-seam", {SEAM_FEATURE: (True, {"ModeFile": str(seam["mode"]), "LogFile": str(seam["log"])})}))
    try:
        native, native_evidence = _library_native(runtime, name, clients, links, seeds, seam)
        evidence.extend(native_evidence)
    finally:
        evidence.extend(runtime.apply_features(name + "-deselect-seam", {SEAM_FEATURE: (False, {})}))
    first = seeds[0]
    workflow = {key: first[key] for key in ("definitionId", "draftId", "definitionPath", "draftPath", "validationPath", "activityId")}
    # seed_workflow names every seed "Weaver package witness <hex>", so this search narrows the list to the seeds.
    workflow.update(library={"search": "Weaver package witness", "pagePath": owner_path(runtime.backend, links["workflow-definitions-page"]),
                             "prefill": {"name": "Weaver prefilled " + secrets.token_hex(4)}})
    browser, browser_evidence = _browser(runtime, name + "-browser", LIBRARY_BROWSER, workflow, {})
    evidence.extend(browser_evidence)
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "native": native, "browser": browser, "evidence": evidence}


def _template_case(runtime, provider):
    name = provider + "-workflow-templates"
    evidence = list(runtime.select_provider(provider))
    runtime.sign_in()
    seed = seed_batch_workflow(runtime.backend, runtime.output(name + "-workflow") / "workflow-seed")
    evidence.extend(seed["evidence"])
    links, links_ref = _links(runtime, name)
    evidence.append(links_ref)
    template, refs = _seed_template(runtime, name, links, seed)
    evidence.extend(refs)
    native, native_evidence = _template_native(runtime, name, links, seed, template)
    evidence.extend(native_evidence)
    # A fresh template for the browser, so its persisted revision is the one the page opens.
    browser_template, refs = _seed_template(runtime, name + "-browser-seed", links, seed)
    evidence.extend(refs)
    workflow = {key: seed[key] for key in ("definitionId", "draftId", "definitionPath", "draftPath", "validationPath")}
    workflow["activityId"] = seed["nodes"]["a"]
    workflow.update(nodes=seed["nodes"], template={"id": browser_template["id"], "revision": browser_template["revision"],
                    "path": owner_path(runtime.backend, links["workflow-template"], templateId=browser_template["id"]),
                    "operations": template_operations(seed), "displayName": "Template greeting"})
    browser, browser_evidence = _browser(runtime, name + "-browser", TEMPLATE_BROWSER, workflow, {})
    evidence.extend(browser_evidence)
    return {"id": name, "status": "passed", "adapter": provider, "evidenceCategory": EVIDENCE_CATEGORY, "native": native, "browser": browser, "evidence": evidence}


def qualify_workflow_library(runtime):
    run = runtime.run
    check(SECTION not in run.report, "Never resume or overwrite a prior workflow library qualification")
    original_cases = copy.deepcopy(run.report["cases"])
    section = {"status": "running", "evidenceCategory": EVIDENCE_CATEGORY, "liveProvider": False, "source": run.source,
               "caseIDs": [], "records": [], "stages": [],
               "driver": {"repositoryPath": "tools/studio/Weaver.Conformance.Probe/library_journeys.py", "sha256": sha(TOOL / "library_journeys.py")}}
    run.report[SECTION] = section
    run.save()
    try:
        for provider in PROVIDERS:
            for suffix, case in (("-workflow-library", _library_case), ("-workflow-templates", _template_case)):
                name = provider + suffix
                section["activeCase"] = name
                with _bounded(runtime, name):
                    value = case(runtime, provider)
                    for ref in value["evidence"]:
                        path = Path(ref["path"])
                        check(path.resolve().is_relative_to(run.root.resolve()) and path.is_file() and sha(path) == ref["sha256"],
                              "Workflow library evidence is missing, changed or outside the owned run")
                    record = runtime.save(runtime.output(name + "-record"), "observation", value)
                    section["caseIDs"].append(name)
                    section["records"].append({"id": name, "status": "passed", "record": record})
        check(set(section["caseIDs"]) == EXPECTED_CASES and len(section["records"]) == len(EXPECTED_CASES),
              "Each workflow library case must be observed exactly once")
        check(same(original_cases, run.report["cases"]), "Workflow library qualification changed the original M1 case records")
        section.pop("activeCase", None)
        section["status"] = "passed"
    except BaseException as error:
        section.update(status="failed", failure={"type": type(error).__name__, "message": str(error)})
        raise
    finally:
        run.save()


def library_manifest_gaps(section, source=None):
    """Structural reasons a section cannot complete a witness; reads no files."""
    if not isinstance(section, dict):
        return ["The mandatory workflow library section is absent"]
    gaps = []
    names = section.get("caseIDs") if isinstance(section.get("caseIDs"), list) else []
    records = section.get("records") if isinstance(section.get("records"), list) else []
    if section.get("status") != "passed":
        gaps.append("Workflow library section status is " + repr(section.get("status")))
    if section.get("evidenceCategory") != EVIDENCE_CATEGORY or section.get("liveProvider") is not False:
        gaps.append("Unsupported workflow library provider evidence claim")
    if source is not None and section.get("source") != source:
        gaps.append("Workflow library section source differs from the witness source")
    if (sorted(names) != sorted(EXPECTED_CASES) or len(names) != len(EXPECTED_CASES)
            or [row.get("id") if isinstance(row, dict) else None for row in records] != names
            or not all(isinstance(row, dict) and row.get("status") == "passed" and isinstance(row.get("record"), dict) for row in records)):
        gaps.append("The workflow library cases were not each observed passed exactly once")
    return gaps


def require_workflow_library(section, owned_root=None, *, source=None):
    """Mandatory final gate: both adapters passed both cases, and each retained record still holds every native control and journey."""
    gaps = library_manifest_gaps(section, source)
    check(not gaps, "The workflow library inventory is mandatory on each actual adapter: " + "; ".join(gaps))
    if owned_root is not None:
        root = Path(owned_root).resolve()
        for row in section["records"]:
            path = Path(row["record"]["path"])
            check(path.is_absolute() and path.resolve().is_relative_to(root) and path.is_file() and sha(path) == row["record"]["sha256"],
                  "Workflow library record is missing, changed or outside the owned run: " + row["id"])
            observed = json.loads(path.read_text())
            native = observed.get("native") if isinstance(observed.get("native"), dict) else {}
            library = row["id"].endswith("-workflow-library")
            controls = LIBRARY_NATIVE if library else TEMPLATE_NATIVE
            check(observed.get("id") == row["id"] and observed.get("status") == "passed" and observed.get("adapter") == row["id"].split("-", 1)[0]
                  and sorted(native) == sorted(controls) and all((native.get(control) or {}).get("control") == control for control in controls)
                  and (not library or ((native.get("tags-dropped") or {}).get("tags") == 1 and (native.get("view-dropped") or {}).get("views") == 1
                                       and (native.get("delete-unoffered") or {}).get("offered") is False))
                  and same((observed.get("browser") or {}).get("observations"), LIBRARY_OBSERVATIONS if library else TEMPLATE_OBSERVATIONS),
                  "Workflow library record differs: " + row["id"])
    return section["records"]


def synthetic_native(case):
    """The minimal native record shape the final gate checks; for synthetic self-tests only, never native evidence."""
    if case.endswith("-workflow-library"):
        return {**{control: {"control": control} for control in LIBRARY_NATIVE}, "tags-dropped": {"control": "tags-dropped", "tags": 1},
                "view-dropped": {"control": "view-dropped", "views": 1}, "delete-unoffered": {"control": "delete-unoffered", "offered": False}}
    return {control: {"control": control} for control in TEMPLATE_NATIVE}


def synthetic_browser(case):
    """The minimal browser shape the final gate checks; for synthetic self-tests only."""
    library = case.endswith("-workflow-library")
    return {"journey": LIBRARY_BROWSER if library else TEMPLATE_BROWSER, "observations": copy.deepcopy(LIBRARY_OBSERVATIONS if library else TEMPLATE_OBSERVATIONS)}
