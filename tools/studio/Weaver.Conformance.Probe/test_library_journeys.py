"""Integrity checks for the workflow-library driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from artifacts import sha
from batch_journeys import OUTCOME_ROWS, PROPOSAL_FRAMES, REFUSED_FRAMES, UNOFFERED_ERROR, UNOFFERED_FRAMES, UNOFFERED_MESSAGE
from library_journeys import (frozen_drop, ASSIGN_TOOL, DELETE_TOOL, DISABLED_REASON, EXPECTED_CASES, LIBRARY_BROWSER, LIBRARY_LIMITED, LIBRARY_LIMITED_GRANTS,
                              LIBRARY_NATIVE, LIBRARY_OBSERVATIONS, LIST_TOOL, ORGANIZATION_KIND, REFUSALS, TEMPLATE_BROWSER, TEMPLATE_KIND,
                              TEMPLATE_NATIVE, TEMPLATE_OBSERVATIONS, UNAVAILABLE_DIAGNOSTIC, VIEW_TOOL, dialog_attachment, library_attachment,
                              library_manifest_gaps, library_principal, require_admitted, require_browser, require_continuation, require_dropped,
                              require_executed, require_executed_rows, require_proposal, require_refused, require_unoffered, require_workflow_library,
                              synthetic_browser, synthetic_native, template_list_attachment, template_operations, template_snapshot)

SEED = {"definitionId": "wf-1", "draftId": "draft-1",
        "nodes": {"root": "batch-flow", "a": "batch-a", "b": "batch-b", "nested": "batch-nested", "inner": "batch-inner"},
        "writeLine": {"activityTypeKey": "Elsa.Activities.Primitives.Activities.WriteLine", "version": "1.0.0", "activityVersionId": "write-v1", "displayName": "Write Line"},
        "flowchart": {"activityTypeKey": "Elsa.Activities.Flowchart.Activities.Flowchart", "version": "1.0.0", "activityVersionId": "flow-v1",
                      "displayName": "Flowchart", "slot": "elsa.flowchart.structure:activities", "mode": "flowchart"}}
ROWS = [{"id": "wf-1", "name": "Collect payment"}, {"id": "wf-2", "name": "Refund"}]
PERMISSIONS = {"canAssignTags": True, "canManageFolders": True, "canManageTags": True, "canShareViews": True}
AVAILABLE = {"status": "available", "reason": None}
COMMAND = "command-1"


def admitted(attachment, organization=AVAILABLE, diagnostics=()):
    items = [{**copy.deepcopy(attachment), "target": {**attachment["target"], "displayName": None, "route": None}}]
    if organization is not None:
        items.append({"contentType": ORGANIZATION_KIND, "origin": "authoritativeRead",
                      "content": {"status": organization["status"], "reason": organization.get("reason"), "refused": ["delete", "permanent-delete"]}})
    return {"kind": "contextAdmitted", "payload": {"diagnostics": [{"code": code, "required": False} for code in diagnostics], "attachments": items}}


def proposal(tool, location, target, preview=None):
    return {"kind": "proposalCreated", "payload": {"id": "proposal-1", "status": "awaiting-approval", "requiresApproval": True, "baseRevision": "1",
            "preparedInvocation": {"invocationId": "invocation-1", "toolName": tool, "requiresApproval": True, "executionLocation": location,
                                   "risk": "reviewRequired", "target": target, "arguments": {}, "preview": {"data": preview or {}}}}}


def frames(kinds, attachment, **named):
    def frame(kind):
        if kind in named: return copy.deepcopy(named[kind])
        if kind == "contextAdmitted": return admitted(attachment, named.get("organization", AVAILABLE), named.get("diagnostics", ()))
        return {"kind": kind}
    return [frame(kind) for kind in kinds]


def rows(provider, tool, offered=True, called=True, returned=True, command=COMMAND):
    value = [{"kind": provider + ".request", "data": {"commandId": command, "tools": [{"name": LIST_TOOL}] + ([{"name": tool}] if offered else [])}}]
    if called: value.append({"kind": provider + ".call", "data": {"commandId": command, "toolName": tool}})
    if returned: value.append({"kind": provider + OUTCOME_ROWS[provider], "data": {"commandId": command}})
    return value


def refused(tool, text, provider="copilot", **named):
    completed = {"kind": "toolCallCompleted", "payload": {"toolName": tool, "succeeded": False, "summary": text,
                 "result": {"toolName": tool, "succeeded": False, "content": text, "payload": None, "receipt": None}}}
    return frames(REFUSED_FRAMES[provider], named.pop("attachment"), toolCallCompleted=completed, **named)


class AttachmentTests(unittest.TestCase):
    def test_page_contexts_are_the_pages_own_strict_shapes(self):
        value = library_attachment(ROWS, permissions=PERMISSIONS, selection=["wf-1"])
        self.assertEqual(("workflows.library", "workflow-library", "library"), (value["contentType"], value["target"]["resourceType"], value["target"]["resourceId"]))
        self.assertEqual(value["target"]["revision"], value["content"]["revision"])
        self.assertEqual(["wf-1"], value["content"]["selection"])
        self.assertEqual({"revision", "query", "view", "columns", "selection", "items", "totalCount", "permissions", "organization"}, set(value["content"]))
        # Stays well inside the host's 32 KiB per-attachment budget (#3710) and the module's 16 KiB bound.
        self.assertLess(len(json.dumps(value["content"])), 16 * 1024)
        with self.assertRaises(RuntimeError):
            library_attachment([{"id": "wf-%d" % index, "name": "n" * 256} for index in range(50)], permissions=PERMISSIONS)
        dialog = dialog_attachment([{"id": "template-1", "name": "Approval", "revision": "t-r3"}])
        self.assertEqual("workflow-create-dialog", dialog["target"]["resourceType"])
        listed = template_list_attachment([{"id": "template-1", "name": "Approval", "revision": "t-r3"}], "template-1")
        self.assertNotIn("workflowDefinitionId", json.dumps(listed))

    def test_the_template_snapshot_is_its_own_resource_with_its_persisted_revision(self):
        value = template_snapshot(SEED, {"id": "template-1", "revision": "t-r3"})
        self.assertEqual((TEMPLATE_KIND, "workflow-template", "template-1"), (value["contentType"], value["target"]["resourceType"], value["target"]["resourceId"]))
        self.assertEqual(("template-1", "t-r3"), (value["content"]["templateId"], value["content"]["templateRevision"]))
        self.assertNotIn("workflowDefinitionId", value["content"]); self.assertNotIn("draftId", value["content"])
        self.assertEqual({"update", "position"}, {item["kind"] for item in template_operations(SEED)})

    def test_the_library_actor_extends_the_nine_earlier_principals(self):
        principals = [{"tenant": "default"}] * LIBRARY_LIMITED
        [actor] = library_principal(principals, lambda prefix, tenant, grants: {"tenant": tenant, "grants": grants})
        self.assertEqual("default", actor["tenant"])
        self.assertIn("api-capabilities:read", actor["grants"])
        self.assertIn("workflows/design/tag-assignments:manage", LIBRARY_LIMITED_GRANTS)
        self.assertFalse({"workflows/design/tags:manage", "workflows/design/views:share"} & set(LIBRARY_LIMITED_GRANTS))
        with self.assertRaises(RuntimeError): library_principal(principals[:5], lambda *args: {})


class TurnTests(unittest.TestCase):
    def test_a_server_write_is_a_reviewed_proposal_in_each_adapters_pinned_order(self):
        attachment = library_attachment(ROWS, permissions=PERMISSIONS)
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                values = frames(PROPOSAL_FRAMES[provider], attachment, proposalCreated=proposal(ASSIGN_TOOL, "server", None),
                                toolApprovalRequested={"kind": "toolApprovalRequested", "proposalId": "proposal-1"})
                self.assertEqual("proposal-1", require_proposal(values, rows(provider, ASSIGN_TOOL), provider=provider, command_id=COMMAND, tool=ASSIGN_TOOL,
                                                                location="server", target=None, attachment=attachment, organization=AVAILABLE)["id"])
                with self.assertRaises(RuntimeError):
                    require_proposal(values, rows(provider, ASSIGN_TOOL, offered=False), provider=provider, command_id=COMMAND, tool=ASSIGN_TOOL,
                                     location="server", target=None, attachment=attachment)
                with self.assertRaises(RuntimeError):
                    require_proposal(values[:-1], rows(provider, ASSIGN_TOOL), provider=provider, command_id=COMMAND, tool=ASSIGN_TOOL,
                                     location="server", target=None, attachment=attachment)
                # A direct application (not awaiting review) is refused even when the frames line up.
                direct = copy.deepcopy(values); direct[-2]["payload"]["preparedInvocation"]["requiresApproval"] = False
                with self.assertRaises(RuntimeError):
                    require_proposal(direct, rows(provider, ASSIGN_TOOL), provider=provider, command_id=COMMAND, tool=ASSIGN_TOOL,
                                     location="server", target=None, attachment=attachment)

    def test_admission_compares_targets_by_identity_and_names_the_organization_read(self):
        attachment = library_attachment(ROWS, permissions=PERMISSIONS)
        require_admitted([admitted(attachment)], attachment, organization=AVAILABLE)
        other = copy.deepcopy(attachment); other["target"]["revision"] = "other"
        with self.assertRaises(RuntimeError): require_admitted([admitted(other)], attachment)
        with self.assertRaises(RuntimeError): require_admitted([admitted(attachment, {"status": "unavailable", "reason": DISABLED_REASON})], attachment, organization=AVAILABLE)
        # Only the expected non-required diagnostics.
        with self.assertRaises(RuntimeError): require_admitted([admitted(attachment, diagnostics=["other"])], attachment)
        require_admitted([admitted(attachment, diagnostics=[UNAVAILABLE_DIAGNOSTIC])], attachment, diagnostics=[UNAVAILABLE_DIAGNOSTIC])

    def test_a_refusal_carries_the_modules_own_text_and_nothing_else(self):
        attachment = library_attachment(ROWS, permissions=PERMISSIONS)
        for provider in ("copilot", "anthropic"):
            for control, tool in (("no-tag-management", ASSIGN_TOOL), ("no-view-sharing", VIEW_TOOL)):
                with self.subTest(provider=provider, control=control):
                    text = "Refused: " + REFUSALS[control]
                    self.assertEqual(text, require_refused(refused(tool, text, provider, attachment=attachment), rows(provider, tool), provider=provider,
                                                           command_id=COMMAND, tool=tool, attachment=attachment, refusal=REFUSALS[control], organization=AVAILABLE))
                    with self.assertRaises(RuntimeError):
                        require_refused(refused(tool, "other", provider, attachment=attachment), rows(provider, tool), provider=provider,
                                        command_id=COMMAND, tool=tool, attachment=attachment, refusal=REFUSALS[control])
        disabled = {"status": "unavailable", "reason": DISABLED_REASON}
        values = refused(ASSIGN_TOOL, DISABLED_REASON, attachment=attachment, organization=disabled, diagnostics=[UNAVAILABLE_DIAGNOSTIC])
        require_refused(values, rows("copilot", ASSIGN_TOOL), provider="copilot", command_id=COMMAND, tool=ASSIGN_TOOL, attachment=attachment,
                        refusal=REFUSALS["organization-disabled"], organization=disabled, diagnostics=[UNAVAILABLE_DIAGNOSTIC])

    def test_deletion_is_never_offered(self):
        attachment = library_attachment(ROWS, permissions=PERMISSIONS)
        for provider in ("copilot", "anthropic"):
            values = frames(UNOFFERED_FRAMES[provider], attachment, error={"kind": "error", "error": {"code": UNOFFERED_ERROR[provider], "message": UNOFFERED_MESSAGE}})
            self.assertEqual({"offered": False}, require_unoffered(values, rows(provider, DELETE_TOOL, offered=False, called=False, returned=False),
                                                                   provider=provider, command_id=COMMAND, tool=DELETE_TOOL, attachment=attachment))
            with self.assertRaises(RuntimeError):
                require_unoffered(values, rows(provider, DELETE_TOOL, called=False, returned=False), provider=provider, command_id=COMMAND,
                                  tool=DELETE_TOOL, attachment=attachment)

    def test_an_approved_write_executes_with_a_persisted_receipt_and_continues_once(self):
        prepared = proposal(ASSIGN_TOOL, "server", {"moduleId": "Elsa.Studio.Workflows", "resourceType": "workflow-library", "resourceId": "library", "revision": "sha256:a"})["payload"]
        response = {"data": {"executed": True, "status": "executed", "toolResult": {"succeeded": True, "toolName": ASSIGN_TOOL,
                    "payload": {"typeId": "workflows.library-result", "data": {"status": "applied", "targets": []}},
                    "receipt": {"scope": "serverPersisted", "invocationId": "invocation-1", "before": prepared["preparedInvocation"]["target"]}}}}
        self.assertEqual("applied", require_executed(response, prepared, tool=ASSIGN_TOOL)["status"])
        partial = copy.deepcopy(response); partial["data"]["toolResult"]["payload"]["data"]["status"] = "partial"
        with self.assertRaises(RuntimeError): require_executed(partial, prepared, tool=ASSIGN_TOOL)
        self.assertEqual("partial", require_executed(partial, prepared, tool=ASSIGN_TOOL, status="partial")["status"])
        unbound = copy.deepcopy(response); unbound["data"]["toolResult"]["receipt"]["before"]["revision"] = "other"
        with self.assertRaises(RuntimeError): require_executed(unbound, prepared, tool=ASSIGN_TOOL)
        require_continuation([{"kind": "started"}, {"kind": "contextAdmitted"}, {"kind": "messageDelta"}, {"kind": "completed"}])
        # Pinned per adapter: Copilot's as the c58c9f149 witness observed it.
        require_continuation([{"kind": "started"}, {"kind": "contextAdmitted"}, {"kind": "messageDelta"}, {"kind": "completed"}], "copilot")
        with self.assertRaises(RuntimeError):
            require_continuation([{"kind": "started"}, {"kind": "contextAdmitted"}, {"kind": "messageDelta"}, {"kind": "completed"}], "anthropic")
        with self.assertRaises(RuntimeError): require_continuation([{"kind": "started"}, {"kind": "toolCallStarted"}, {"kind": "completed"}])
        base = [{"kind": "copilot.request", "data": {"commandId": COMMAND, "tools": [{"name": ASSIGN_TOOL}]}},
                {"kind": "copilot.call", "data": {"commandId": COMMAND, "toolName": ASSIGN_TOOL}},
                {"kind": "copilot.return", "data": {"commandId": COMMAND}},
                {"kind": "copilot.request", "data": {"commandId": COMMAND, "tools": []}},
                {"kind": "copilot.continuation", "data": {"commandId": COMMAND}}]
        self.assertEqual(5, len(require_executed_rows(base, provider="copilot", command_id=COMMAND, tool=ASSIGN_TOOL)))
        with self.assertRaises(RuntimeError): require_executed_rows(base[:2], provider="copilot", command_id=COMMAND, tool=ASSIGN_TOOL)
        retooled = copy.deepcopy(base); retooled[3]["data"]["tools"] = [{"name": ASSIGN_TOOL}]
        with self.assertRaises(RuntimeError): require_executed_rows(retooled, provider="copilot", command_id=COMMAND, tool=ASSIGN_TOOL)

    def test_a_lost_response_is_logged_once_for_the_frozen_key_and_the_committed_identity(self):
        lines = [json.dumps({"kind": "create-tag", "operationKey": "k1", "committedId": "tag-1"})]
        require_dropped(lines, kind="create-tag", operation_key="k1", committed_id="tag-1")
        for kind, key, committed in (("create-tag", "k2", "tag-1"), ("create-tag", "k1", "tag-2"), ("save-view", "k1", "tag-1")):
            with self.assertRaises(RuntimeError): require_dropped(lines, kind=kind, operation_key=key, committed_id=committed)
        with self.assertRaises(RuntimeError): require_dropped(lines * 2, kind="create-tag", operation_key="k1", committed_id="tag-1")


class FrozenEvidenceTests(unittest.TestCase):
    def test_a_lost_response_is_retained_as_its_own_frozen_copy_never_the_shared_append_only_log(self):
        # The 072001a53 witness: the first drop's evidence referenced the seam's log, which the second drop then appended to,
        # so the case's final evidence check found a changed file. Each observation now keeps its own copy.
        with tempfile.TemporaryDirectory() as root:
            root = Path(root)
            log = root / "dropped-private.log"
            log.write_text(json.dumps({"kind": "create-tag", "operationKey": "k1", "committedId": "tag-1"}) + "\n")
            def save(output, name, value):
                path = Path(output) / (name + ".json")
                self.assertFalse(path.exists())
                path.write_text(json.dumps(value))
                return {"path": str(path), "sha256": sha(path)}
            first = root / "turn-1"; first.mkdir()
            entry, tag_ref = frozen_drop(save, first, log, kind="create-tag", operation_key="k1", committed_id="tag-1")
            self.assertEqual("tag-1", entry["committedId"])
            stale = {"path": str(log), "sha256": sha(log)}
            with log.open("a") as handle: handle.write(json.dumps({"kind": "save-view", "operationKey": "k2", "committedId": "view-1"}) + "\n")
            second = root / "turn-2"; second.mkdir()
            _, view_ref = frozen_drop(save, second, log, kind="save-view", operation_key="k2", committed_id="view-1")
            # The shared log changed under the first reference; each frozen copy still matches its own hash.
            self.assertNotEqual(stale["sha256"], sha(log))
            for ref in (tag_ref, view_ref):
                self.assertEqual(ref["sha256"], sha(Path(ref["path"])))
            self.assertEqual(1, len(json.loads(Path(tag_ref["path"]).read_text())["entries"]))
            with self.assertRaises(RuntimeError):
                frozen_drop(save, root, log, kind="create-tag", operation_key="other", committed_id="tag-1")


class SettledReviewTests(unittest.TestCase):
    def test_each_library_review_waits_until_the_page_state_it_binds_is_settled(self):
        # The 6231b4434 witness: the list was reviewed before its permissions were read, the read moved the page revision and
        # the page refused the approved change as stale. Each review must wait on the page's own settled signal first.
        source = Path(__file__).with_name("browser.mjs").read_text()
        journey = source[source.index("async function workflowLibrary()"):source.index("async function workflowTemplates()")]
        waits = [('getByRole("table", { name: "Workflow definitions", exact: true })).toHaveAttribute("aria-busy", "false"',
                  'reviewedTool("workflow_library_set_list_state"'),
                 ('getByRole("combobox", { name: "Starting point", exact: true })).toHaveAttribute("aria-busy", "false"',
                  'reviewedTool("workflow_create_dialog_prefill"')]
        previous = 0
        for wait, review in waits:
            self.assertIn(wait, journey)
            self.assertLess(previous, journey.index(wait))
            self.assertLess(journey.index(wait), journey.index(review))
            # Nothing between the wait and the review changes the page state the review binds.
            between = journey[journey.index(wait):journey.index(review)]
            self.assertNotIn("nativeEnter(", between.replace('nativeEnter("open-weaver"', ""))
            previous = journey.index(review)


class BrowserAndGateTests(unittest.TestCase):
    def observed(self, journey):
        library = journey == LIBRARY_BROWSER
        commands = {"narrow": "c1", "prefill": "c2"} if library else {"applied": "c1"}
        return {"journey": journey, "status": "passed", "provider": "copilot", "pageErrors": [], "observations": copy.deepcopy(LIBRARY_OBSERVATIONS if library else TEMPLATE_OBSERVATIONS),
                "ownerBefore": {"path": "a"}, "ownerAfter": {"path": "b"}, "evidence": [{"path": "c"}], "commands": commands}

    def test_the_browser_journeys_are_checked_against_the_adapter_trace(self):
        library = self.observed(LIBRARY_BROWSER)
        commands = [{"commandId": "c1", "toolName": LIST_TOOL, "arguments": {"search": "x"}}, {"commandId": "c2", "toolName": "workflow_create_dialog_prefill", "arguments": {"name": "n"}}]
        trace = rows("copilot", LIST_TOOL, command="c1", returned=False) + rows("copilot", "workflow_create_dialog_prefill", command="c2", returned=False)
        self.assertEqual(LIBRARY_OBSERVATIONS, require_browser(library, commands, trace, provider="copilot", journey=LIBRARY_BROWSER)["observations"])
        changed = copy.deepcopy(library); changed["observations"]["createNotPressed"] = False
        with self.assertRaises(RuntimeError): require_browser(changed, commands, trace, provider="copilot", journey=LIBRARY_BROWSER)
        with self.assertRaises(RuntimeError): require_browser(library, commands[:1], trace, provider="copilot", journey=LIBRARY_BROWSER)
        swapped = [dict(commands[0], toolName="workflow_create_dialog_prefill"), commands[1]]
        with self.assertRaises(RuntimeError): require_browser(library, swapped, trace, provider="copilot", journey=LIBRARY_BROWSER)
        template = self.observed(TEMPLATE_BROWSER)
        trace = rows("copilot", "workflow_apply_operation_batch", command="c1", returned=False)
        require_browser(template, [{"commandId": "c1", "toolName": "workflow_apply_operation_batch", "arguments": {"operations": []}}], trace,
                        provider="copilot", journey=TEMPLATE_BROWSER)

    def section(self, root, native=None):
        records = []
        for name in sorted(EXPECTED_CASES):
            path = Path(root) / (name + ".json")
            path.write_text(json.dumps({"id": name, "status": "passed", "adapter": name.split("-", 1)[0],
                                        "native": (native or {}).get(name, synthetic_native(name)), "browser": synthetic_browser(name)}))
            records.append({"id": name, "status": "passed", "record": {"path": str(path), "sha256": sha(path)}})
        return {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False, "source": "s",
                "caseIDs": sorted(EXPECTED_CASES), "records": records, "stages": []}

    def test_the_final_gate_requires_every_case_control_and_journey(self):
        with tempfile.TemporaryDirectory() as root:
            section = self.section(root)
            self.assertEqual([], library_manifest_gaps(section, "s"))
            self.assertEqual(4, len(require_workflow_library(section, root, source="s")))
            self.assertEqual(set(LIBRARY_NATIVE), set(synthetic_native("copilot-workflow-library")))
            self.assertEqual(set(TEMPLATE_NATIVE), set(synthetic_native("copilot-workflow-templates")))
            self.assertTrue(library_manifest_gaps(None))
            self.assertTrue(library_manifest_gaps({**section, "liveProvider": True}, "s"))
            self.assertTrue(library_manifest_gaps({**section, "caseIDs": section["caseIDs"][:3], "records": section["records"][:3]}, "s"))
            two_tags = {**synthetic_native("copilot-workflow-library"), "tags-dropped": {"control": "tags-dropped", "tags": 2}}
            with self.assertRaises(RuntimeError):
                require_workflow_library(self.section(root, {"copilot-workflow-library": two_tags}), root, source="s")


if __name__ == "__main__":
    unittest.main()
