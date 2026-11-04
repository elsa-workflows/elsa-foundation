"""Integrity checks for the workflow-truth-seams driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from artifacts import sha
from journey_runtime import BASE_GRANTS
from truth_seam_journeys import (ACTORS, BATCH_TOOL, BROWSER_JOURNEY, BROWSER_OBSERVATIONS, CATALOG_DENIED, require_read_only_browser, synthetic_browser, EXPECTED_CASES, LAYOUT_TOOL, LEGACY_PLACEHOLDERS, MANAGER, READ_TOOL, READER,
                                 SECTION, TENANT_READER, TENANT_ONLY_VERSION, require_catalog, require_no_placeholders,
                                 require_permissions, require_truth_turn, same_target, admitted_context, require_workflow_truth_seams, synthetic_actors,
                                 synthetic_registrations, truth_principals, truth_seam_manifest_gaps)

TARGET = {"moduleId": "Elsa.Studio.Workflows", "resourceType": "workflow-definition", "resourceId": "wf-1", "revision": "unsaved-1"}
COMMAND = "command-1"


def entry(version_id, available=True, version="1.0.0", inputs=()):
    return {"activityVersionId": version_id, "available": available, "version": version,
            "availabilityReason": None if available else "Excluded by the effective activity availability policy.",
            "inputs": [{"referenceKey": item[0], "name": item[0], "type": "String", "isRequired": False, "isSensitive": item[1],
                        "isCredential": item[1], "uiHint": item[2] if len(item) > 2 else None} for item in inputs]}


def owner_entries(tenant_only=True):
    rows = [entry("flowchart-v1"), entry("write-line-v1", inputs=(("text", False), ("apiKey", True), ("passphrase", False, "password"))),
            entry("excluded-v2", False, "2.0.0")]
    return ([entry(TENANT_ONLY_VERSION)] if tenant_only else []) + rows


def catalog_content(entries, included=None):
    included = len(entries) if included is None else included
    truncated = included < len(entries)
    return {"status": "available", "reason": None, "totalCount": len(entries), "includedCount": included, "truncated": truncated,
            "budgetBytes": 1000, "includedBytes": 900 if truncated else 400, "nextEntryBytes": 200 if truncated else None,
            "entries": [{**item, "inputs": [{**field, "isMasked": field["isSensitive"] or field["isCredential"] or field.get("uiHint") == "password"}
                                            for field in item["inputs"]]} for item in copy.deepcopy(entries[:included])]}


DENIED = {"status": "denied", "reason": "The actor may not read the activity authoring catalog.", "totalCount": 0, "includedCount": 0,
          "truncated": False, "budgetBytes": 1000, "includedBytes": 0, "nextEntryBytes": None, "entries": []}


def permissions_content(manage):
    return {"workflowDefinitionId": "wf-1", "revision": "unsaved-1", "read": "allowed", "manage": "allowed" if manage else "denied",
            "capabilities": ["workflow.explain", "workflow.propose-change"] if manage else ["workflow.explain"],
            "tools": [READ_TOOL, LAYOUT_TOOL, BATCH_TOOL] if manage else [READ_TOOL]}


def admitted(manage=True):
    return {"kind": "contextAdmitted", "payload": {"diagnostics": [], "attachments": [
        {"contentType": "workflow.definition", "origin": "clientSnapshot", "target": TARGET},
        {"contentType": "workflow.catalog", "origin": "authoritativeRead", "target": None, "content": catalog_content(owner_entries())},
        {"contentType": "workflow.permissions", "origin": "authoritativeRead", "target": TARGET, "content": permissions_content(manage)},
        {"contentType": "workflow.guidance", "origin": "installedGuidance"}]}}


def frames(manage=True):
    return [{"kind": "started", "payload": {"turnId": "turn-1"}}, admitted(manage), admitted(manage),
            {"kind": "toolCallStarted", "payload": {"toolName": READ_TOOL}},
            {"kind": "toolCallCompleted", "payload": {"toolName": READ_TOOL, "succeeded": True}},
            {"kind": "completed", "payload": {}}]


def rows(provider="copilot", tools=(READ_TOOL, LAYOUT_TOOL, BATCH_TOOL)):
    return [{"kind": provider + ".request", "data": {"commandId": COMMAND, "tools": [{"name": name} for name in tools] + [{"name": "acme_other"}]}},
            {"kind": provider + ".call", "data": {"commandId": COMMAND, "toolName": READ_TOOL}},
            {"kind": provider + ".return", "data": {"commandId": COMMAND}}]


# The exact envelope target both adapters' native streams carried in the 499853897 witness: the engine serializes the
# optional presentation members of AgentResourceTarget as null.
WIRE_TARGET = {**TARGET, "displayName": None, "route": None}


def wire_frames(manage=True):
    value = frames(manage)
    for frame in value:
        for item in (frame.get("payload") or {}).get("attachments", []) if frame["kind"] == "contextAdmitted" else []:
            if item.get("target") is not None: item["target"] = dict(WIRE_TARGET)
    return value


class TurnTests(unittest.TestCase):
    def check(self, value=None, trace=None, provider="copilot"):
        return require_truth_turn(frames() if value is None else value, rows(provider) if trace is None else trace,
                                  provider=provider, command_id=COMMAND, target=TARGET)

    def test_a_completed_read_turn_yields_its_server_reads_and_the_offered_workflow_tools(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                observed = self.check(provider=provider)
                self.assertEqual(sorted([READ_TOOL, LAYOUT_TOOL, BATCH_TOOL]), observed["advertised"])
                self.assertEqual("allowed", observed["permissions"]["manage"])
                self.assertEqual(len(owner_entries()), observed["catalog"]["totalCount"])

    def test_the_native_wire_target_with_null_presentation_members_is_the_same_target(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                observed = require_truth_turn(wire_frames(), rows(provider), provider=provider, command_id=COMMAND, target=TARGET)
                self.assertEqual("allowed", observed["permissions"]["manage"])
        self.assertTrue(same_target({**TARGET, "displayName": "Workflow", "route": "/workflows"}, TARGET))
        for name, target in (("other-revision", {**WIRE_TARGET, "revision": "persisted"}), ("other-id", {**WIRE_TARGET, "resourceId": "wf-2"}),
                             ("other-module", {**WIRE_TARGET, "moduleId": "Acme"}), ("unknown-member", {**WIRE_TARGET, "tenantId": "t"}),
                             ("missing-revision", {key: value for key, value in WIRE_TARGET.items() if key != "revision"}), ("none", None)):
            with self.subTest(target=name):
                self.assertFalse(same_target(target, TARGET))
                value = wire_frames()
                value[1]["payload"]["attachments"][0]["target"] = target
                with self.assertRaises(RuntimeError): require_truth_turn(value, rows(), provider="copilot", command_id=COMMAND, target=TARGET)

    def test_a_missing_drifting_forged_or_incomplete_turn_never_qualifies(self):
        def attachment(value, kind, index=1):
            return next(item for item in [f for f in value if f["kind"] == "contextAdmitted"][index - 1]["payload"]["attachments"]
                        if item["contentType"] == kind)
        changes = {
            "not-completed": lambda v: v.__setitem__(-1, {"kind": "error", "error": {}}),
            "no-context": lambda v: [v.remove(f) for f in [f for f in v if f["kind"] == "contextAdmitted"]],
            "diagnostic": lambda v: v[1]["payload"].update(diagnostics=[{"code": "agent.workflow.catalog_unavailable", "required": False}]),
            "no-catalog": lambda v: v[1]["payload"].update(attachments=[i for i in v[1]["payload"]["attachments"] if i["contentType"] != "workflow.catalog"]),
            "no-permissions": lambda v: v[2]["payload"].update(attachments=[i for i in v[2]["payload"]["attachments"] if i["contentType"] != "workflow.permissions"]),
            "client-catalog": lambda v: attachment(v, "workflow.catalog").update(origin="clientSnapshot"),
            "targeted-catalog": lambda v: attachment(v, "workflow.catalog").update(target=TARGET),
            "foreign-permissions-target": lambda v: attachment(v, "workflow.permissions").update(target={**TARGET, "resourceId": "wf-2"}),
            "lost-definition-target": lambda v: attachment(v, "workflow.definition", 2).update(target={**TARGET, "revision": "other"}),
            "drifting-catalog": lambda v: attachment(v, "workflow.catalog", 2)["content"].update(totalCount=99),
            "drifting-permissions": lambda v: attachment(v, "workflow.permissions", 2)["content"].update(manage="denied"),
            "tool-failed": lambda v: v[4]["payload"].update(succeeded=False),
            "other-tool": lambda v: v[4]["payload"].update(toolName=LAYOUT_TOOL),
        }
        for name, change in changes.items():
            with self.subTest(change=name):
                value = frames(); change(value)
                with self.assertRaises(RuntimeError): self.check(value)
        for name, trace in (("no-call", rows()[:1]), ("other-call", [rows()[0], {"kind": "copilot.call", "data": {"commandId": COMMAND, "toolName": LAYOUT_TOOL}}]),
                            ("no-advertisement", rows()[1:]), ("foreign-command", [{**row, "data": {**row["data"], "commandId": "other"}} for row in rows()])):
            with self.subTest(trace=name):
                with self.assertRaises(RuntimeError): self.check(trace=trace)


class CatalogTests(unittest.TestCase):
    def test_the_catalog_is_the_designers_answer_or_its_marked_prefix_with_the_tenant_activity_in_its_own_tenant(self):
        self.assertEqual({"status": "available", "totalCount": 4, "includedCount": 4, "truncated": False, "tenantOnly": True},
                         require_catalog(catalog_content(owner_entries()), 200, owner_entries(), tenant_only=True))
        self.assertEqual({"status": "available", "totalCount": 4, "includedCount": 2, "truncated": True, "tenantOnly": True},
                         require_catalog(catalog_content(owner_entries(), 2), 200, owner_entries(), tenant_only=True))
        other = owner_entries(tenant_only=False)
        self.assertFalse(require_catalog(catalog_content(other), 200, other, tenant_only=False)["tenantOnly"])
        self.assertEqual("denied", require_catalog(DENIED, 403, None, tenant_only=None)["status"])

    def test_a_narrowed_reordered_invented_unmarked_or_cross_tenant_catalog_never_qualifies(self):
        owner = owner_entries()
        def content(change):
            value = catalog_content(owner); change(value); return value
        cases = {
            "denied": (content(lambda v: v.update(status="denied")), owner, True),
            "unavailable": (content(lambda v: v.update(status="unavailable", entries=[])), owner, True),
            "narrowed": (content(lambda v: v.update(entries=v["entries"][:-1], includedCount=3)), owner, True),
            "unmarked-prefix": (content(lambda v: v.update(entries=v["entries"][:2], includedCount=2)), owner, True),
            "reordered": (content(lambda v: v["entries"].reverse()), owner, True),
            "availability": (content(lambda v: v["entries"][3].update(available=True)), owner, True),
            "version": (content(lambda v: v["entries"][1].update(version="9.9.9")), owner, True),
            "invented": (content(lambda v: (v["entries"].append({"activityVersionId": "x", "available": True, "version": "1"}), v.update(includedCount=5))), owner, True),
            "wrong-total": (content(lambda v: v.update(totalCount=3)), owner, True),
            "tenant-activity-missing": (catalog_content(owner_entries(False)), owner_entries(False), True),
            "tenant-activity-leaked": (catalog_content(owner), owner, False),
            "credential-dropped": (content(lambda v: v["entries"][2]["inputs"][1].update(isCredential=False)), owner, True),
            "input-missing": (content(lambda v: v["entries"][2]["inputs"].pop()), owner, True),
            "hint-dropped": (content(lambda v: v["entries"][2]["inputs"][2].update(uiHint=None)), owner, True),
            "masked-dropped": (content(lambda v: v["entries"][2]["inputs"][2].update(isMasked=False)), owner, True),
            "reason-changed": (content(lambda v: v["entries"][3].update(availabilityReason=None)), owner, True),
            "prefix-not-maximal": (catalog_content(owner, 2) | {"includedBytes": 500}, owner, True),
            "over-budget": (catalog_content(owner) | {"includedBytes": 1001}, owner, True),
            "no-budget": ({k: v for k, v in catalog_content(owner).items() if k != "budgetBytes"}, owner, True),
            "untruncated-left-out": (catalog_content(owner) | {"nextEntryBytes": 10}, owner, True),
        }
        for name, (value, entries, tenant_only) in cases.items():
            with self.subTest(change=name):
                with self.assertRaises(RuntimeError): require_catalog(value, 200, entries, tenant_only=tenant_only)
        with self.assertRaises(RuntimeError): require_catalog(catalog_content(owner), 200, [], tenant_only=True)
        with self.assertRaises(RuntimeError): require_catalog(catalog_content(owner), 403, owner, tenant_only=True)
        for status, value in ((200, DENIED), (403, catalog_content(owner)), (403, DENIED | {"entries": [entry("x")], "totalCount": 1})):
            with self.subTest(denied=status):
                with self.assertRaises(RuntimeError): require_catalog(value, status, None, tenant_only=None)


class PermissionTests(unittest.TestCase):
    def test_permissions_follow_the_owners_answers_and_the_offered_tools_follow_them(self):
        self.assertEqual("allowed", require_permissions(permissions_content(True), manage=True, read_status=200, manage_status=200,
                                                        advertised=sorted([READ_TOOL, LAYOUT_TOOL, BATCH_TOOL]), target=TARGET)["manage"])
        self.assertEqual("denied", require_permissions(permissions_content(False), manage=False, read_status=200, manage_status=403,
                                                       advertised=[READ_TOOL], target=TARGET)["manage"])

    def test_any_disagreement_with_the_owner_or_the_offered_tools_never_qualifies(self):
        reader = dict(manage=False, read_status=200, manage_status=403, advertised=[READ_TOOL], target=TARGET)
        cases = {
            "reader-offered-layout": (permissions_content(False), {**reader, "advertised": sorted([READ_TOOL, LAYOUT_TOOL])}),
            "reader-reported-manage": (permissions_content(True), reader),
            "owner-allows-manage": (permissions_content(False), {**reader, "manage_status": 200}),
            "owner-refuses-read": (permissions_content(False), {**reader, "read_status": 403}),
            "manager-not-offered-layout": (permissions_content(True), {**reader, "manage": True, "manage_status": 200}),
            "unavailable-claimed-as-denied": ({**permissions_content(False), "manage": "unavailable"}, reader),
            "hardcoded-capabilities": ({**permissions_content(False), "capabilities": ["workflow.explain", "workflow.propose-change"]}, reader),
            "other-target": ({**permissions_content(False), "workflowDefinitionId": "wf-2"}, reader),
            "other-revision": ({**permissions_content(False), "revision": "rev-persisted"}, reader),
        }
        for name, (content, arguments) in cases.items():
            with self.subTest(change=name):
                with self.assertRaises(RuntimeError): require_permissions(content, **arguments)


class PrincipalTests(unittest.TestCase):
    def test_the_three_actors_extend_the_original_five_with_their_own_grants_and_tenants(self):
        original = [{"tenant": "default"}, {"tenant": "default"}, {"tenant": "other"}, {"tenant": "default"}, {"tenant": "default"}]
        made = truth_principals(original, lambda prefix, tenant, grants: {"prefix": prefix, "tenant": tenant, "grants": grants})
        self.assertEqual([("witness-reader", "default"), ("witness-manager", "default"), ("witness-tenant-reader", "other"),
                          ("witness-catalog-denied", "default")], [(item["prefix"], item["tenant"]) for item in made])
        self.assertEqual((5, 6, 7, 8), (READER, MANAGER, TENANT_READER, CATALOG_DENIED))
        self.assertTrue(set(BASE_GRANTS) <= set(made[0]["grants"]) and "workflows/design:manage" not in made[0]["grants"])
        self.assertTrue({"workflows/design:manage", "activities/design:read"} <= set(made[1]["grants"]))
        # Exactly what each journey reaches beyond BASE_GRANTS, nothing broader: every actor discovers owner links through
        # GET /capabilities; only the manager preflights and lays out; the second tenant's reader only reads the catalog
        # over a target-free request; only the reader's Studio editor reads expressions; the catalog-denied actor never
        # holds activities/design:read.
        extra = [sorted(key for key in item["grants"] if key not in BASE_GRANTS) for item in made]
        self.assertEqual([["activities/design:read", "api-capabilities:read", "expressions:read", "workflows/design:read"],
                          ["activities/design:read", "api-capabilities:read", "workflows/design:manage"],
                          ["activities/design:read", "api-capabilities:read", "workflows/design:read"],
                          ["api-capabilities:read", "workflows/design:read"]], extra)
        self.assertTrue(all(len(item["grants"]) == len(set(item["grants"])) and all("*" not in key for key in item["grants"]) for item in made))
        with self.assertRaises(RuntimeError): truth_principals(original[:4], lambda *args: {})
        with self.assertRaises(RuntimeError): truth_principals([{"tenant": "default"}] * 5, lambda *args: {})


class BrowserTests(unittest.TestCase):
    READ, LAYOUT = "command-read", "command-layout"

    def observed(self, provider="copilot"):
        return {"journey": BROWSER_JOURNEY, "provider": provider, "status": "passed", "pageErrors": [],
                "observations": copy.deepcopy(BROWSER_OBSERVATIONS), "ownerBefore": {"path": "/owned/before.json", "sha256": "a" * 64},
                "ownerAfter": {"path": "/owned/after.json", "sha256": "b" * 64}, "evidence": [{"path": "/owned/x.json", "sha256": "c" * 64}]}

    def commands(self):
        return [{"commandId": self.READ, "toolName": READ_TOOL, "arguments": {}},
                {"commandId": self.LAYOUT, "toolName": LAYOUT_TOOL, "arguments": {"activityId": "a", "x": 480, "y": 320}}]

    def rows(self, provider="copilot", offered=(READ_TOOL,)):
        tools = [{"name": name} for name in offered] + [{"name": "acme_other"}]
        return [{"kind": provider + ".request", "data": {"commandId": self.READ, "tools": tools}},
                {"kind": provider + ".call", "data": {"commandId": self.READ, "toolName": READ_TOOL}},
                {"kind": provider + ".request", "data": {"commandId": self.LAYOUT, "tools": tools}}]

    def test_the_reader_explains_and_is_never_offered_the_layout_change_on_either_adapter(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                value = require_read_only_browser(self.observed(provider), self.commands(), self.rows(provider), provider=provider)
                self.assertEqual({READ_TOOL: self.READ, LAYOUT_TOOL: self.LAYOUT}, value["commands"])
                self.assertEqual({READ_TOOL: [READ_TOOL], LAYOUT_TOOL: [READ_TOOL]}, value["advertised"])
                self.assertFalse(value["observations"]["layoutOffered"])

    def test_an_offered_called_unretained_or_otherwise_observed_layout_never_qualifies(self):
        def observation(change):
            value = self.observed(); change(value); return value
        observed_changes = {
            "failed": lambda v: v.update(status="failed"), "failure": lambda v: v.update(failure={"type": "Error"}),
            "page-error": lambda v: v.update(pageErrors=["boom"]), "other-journey": lambda v: v.update(journey="workflow"),
            "other-adapter": lambda v: v.update(provider="anthropic"), "no-owner": lambda v: v.pop("ownerAfter"),
            "no-evidence": lambda v: v.update(evidence=[]),
            "proposal": lambda v: v["observations"].update(proposalCount=1), "moved": lambda v: v["observations"].update(nodeMoved=True),
            "draft-changed": lambda v: v["observations"].update(draftUnchanged=False), "write": lambda v: v["observations"].update(owningWrites=1),
            "offered": lambda v: v["observations"].update(layoutOffered=True),
            "manage-reported": lambda v: v["observations"]["permissionsDisclosed"].update(manage="allowed"),
            "no-explain": lambda v: v["observations"].update(explainCompleted=False),
        }
        for name, change in observed_changes.items():
            with self.subTest(observed=name):
                with self.assertRaises(RuntimeError): require_read_only_browser(observation(change), self.commands(), self.rows(), provider="copilot")
        layout_called = self.rows() + [{"kind": "copilot.call", "data": {"commandId": self.LAYOUT, "toolName": LAYOUT_TOOL}}]
        traces = {
            "layout-offered": self.rows(offered=(READ_TOOL, LAYOUT_TOOL)),
            "layout-called": layout_called,
            "read-not-called": [row for row in self.rows() if row["kind"] != "copilot.call"],
            "layout-not-requested": self.rows()[:2],
            "foreign-command": [{**row, "data": {**row["data"], "commandId": "other"}} for row in self.rows()],
        }
        for name, rows in traces.items():
            with self.subTest(trace=name):
                with self.assertRaises(RuntimeError): require_read_only_browser(self.observed(), self.commands(), rows, provider="copilot")
        for name, commands in (("one-command", self.commands()[:1]), ("duplicate", self.commands() + self.commands()[:1]),
                               ("foreign-tool", self.commands() + [{"commandId": "x", "toolName": "acme_other"}])):
            with self.subTest(commands=name):
                with self.assertRaises(RuntimeError): require_read_only_browser(self.observed(), commands, self.rows(), provider="copilot")


class PlaceholderTests(unittest.TestCase):
    def generation(self, **changes):
        value = {"generation": "g-1", "workflowAgent": {"assembly": "Elsa.Agent.Workflows", "activityAuthoringCatalogs": ["HttpActivityAuthoringCatalog"],
                 "types": ["DefaultWorkflowAgentContextProvider", "FoundationWorkflowsAgentFeature", "WorkflowActivityCatalog", "WorkflowAgentGraph"]}}
        value["workflowAgent"].update(changes)
        return value

    def test_the_packaged_consumer_ships_and_composes_no_placeholder(self):
        self.assertEqual([], require_no_placeholders(self.generation())["placeholders"])

    def test_a_shipped_placeholder_a_foreign_report_or_another_catalog_never_qualifies(self):
        for name in sorted(LEGACY_PLACEHOLDERS):
            with self.subTest(placeholder=name):
                with self.assertRaises(RuntimeError):
                    require_no_placeholders(self.generation(types=["DefaultWorkflowAgentContextProvider", "WorkflowActivityCatalog", name]))
        for name, value in (("absent", {"generation": "g-1"}), ("empty", self.generation(types=[])),
                            ("foreign", self.generation(types=["SomethingElse"])),
                            ("no-catalog", self.generation(activityAuthoringCatalogs=[])),
                            ("two-catalogs", self.generation(activityAuthoringCatalogs=["HttpActivityAuthoringCatalog", "Other"])),
                            ("unresolvable", self.generation(activityAuthoringCatalogs=["unresolvable:InvalidOperationException"]))):
            with self.subTest(change=name):
                with self.assertRaises(RuntimeError): require_no_placeholders(value)


class GateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def section(self, actors=None, browser=None):
        records = []
        for name in sorted(EXPECTED_CASES):
            path = self.root / (name + ".json")
            path.write_text(json.dumps({"id": name, "status": "passed", "adapter": name.split("-", 1)[0], "actors": actors or synthetic_actors(),
                                        "registrations": synthetic_registrations(), "browser": synthetic_browser() if browser is None else browser}))
            records.append({"id": name, "status": "passed", "record": {"path": str(path), "sha256": sha(path)}})
        return {"status": "passed", "evidenceCategory": "deterministic-provider-protocol", "liveProvider": False, "source": "a" * 40,
                "caseIDs": sorted(EXPECTED_CASES), "records": records, "stages": []}

    def test_both_passed_records_with_every_actor_complete_the_inventory(self):
        section = self.section()
        self.assertEqual([], truth_seam_manifest_gaps(section, "a" * 40))
        self.assertEqual(2, len(require_workflow_truth_seams(section, self.root, source="a" * 40)))
        self.assertEqual(sorted(role for role, *_ in ACTORS), sorted(synthetic_actors()))

    def test_a_missing_failed_foreign_or_changed_record_never_completes(self):
        self.assertTrue(truth_seam_manifest_gaps(None))
        for name, change in {"failed": lambda s: s.update(status="failed"), "live": lambda s: s.update(liveProvider=True),
                             "foreign-source": lambda s: s.update(source="b" * 40),
                             "missing": lambda s: (s["caseIDs"].pop(), s["records"].pop())}.items():
            with self.subTest(change=name):
                section = self.section(); change(section)
                self.assertTrue(truth_seam_manifest_gaps(section, "a" * 40))
                with self.assertRaises(RuntimeError): require_workflow_truth_seams(section, self.root, source="a" * 40)
        changed = self.section(); changed["records"][0]["record"]["sha256"] = "0" * 64
        offered = synthetic_browser(); offered["observations"]["layoutOffered"] = True
        with self.assertRaises(RuntimeError): require_workflow_truth_seams(self.section(browser=offered), self.root, source="a" * 40)
        with self.assertRaises(RuntimeError): require_workflow_truth_seams(self.section(browser={}), self.root, source="a" * 40)
        with self.assertRaises(RuntimeError): require_workflow_truth_seams(changed, self.root, source="a" * 40)
        for role, value in (("reader", {"permissions": {"manage": "allowed"}, "catalog": {"tenantOnly": True, "status": "available"}}),
                            ("tenant-reader", {"permissions": None, "catalog": {"tenantOnly": True, "status": "available"}}),
                            ("tenant-reader", {"permissions": {"manage": "allowed"}, "catalog": {"tenantOnly": False, "status": "available"}}),
                            ("catalog-denied", {"permissions": {"manage": "denied"}, "catalog": {"tenantOnly": None, "status": "available"}})):
            with self.subTest(actor=role):
                with self.assertRaises(RuntimeError):
                    require_workflow_truth_seams(self.section({**synthetic_actors(), role: value}), self.root, source="a" * 40)
        self.assertEqual("workflowTruthSeamQualification", SECTION)


class AdmittedContextTests(unittest.TestCase):
    # The accepted-message wire the native API returned in the f32dd0a8f witness for the second tenant's request: the
    # admitted context lives at data.message.context, beside data.warnings, as Studio's agent client reads it.
    ACCEPTED = {"data": {"message": {"id": "m-1", "role": "user", "status": "pending", "context": [
        {"contentType": "workflow.request", "origin": "clientSnapshot", "target": None},
        {"contentType": "workflow.catalog", "origin": "authoritativeRead", "target": None, "content": {"status": "available"}},
        {"contentType": "workflow.guidance", "origin": "installedGuidance", "target": None}]}, "warnings": []}}

    def test_the_admitted_context_is_read_where_the_api_puts_it(self):
        self.assertEqual(["workflow.request", "workflow.catalog", "workflow.guidance"],
                         [item["contentType"] for item in admitted_context(copy.deepcopy(self.ACCEPTED))])

    def test_another_shape_is_refused_rather_than_read_as_empty(self):
        for value in ({"data": {"contextAttachments": []}}, {"data": {"message": {}}}, {"data": None}, {}, None,
                      {"data": {"message": {"context": "not-a-list"}}}):
            with self.subTest(value=value):
                with self.assertRaises(RuntimeError): admitted_context(value)


class FailureExcerptTests(unittest.TestCase):
    def test_an_unexpected_error_body_names_its_cause_without_secrets(self):
        from runtime import failure_excerpt
        body = json.dumps({"errors": {"generalErrors": ["The requested workflow definition is not readable."]},
                           "detail": "Retry with Bearer private-bearer or access_token=private-token-value",
                           "accessToken": "private-token-value"}).encode()
        excerpt = failure_excerpt(403, body)
        self.assertIn("not readable", excerpt)
        self.assertNotIn("private-token-value", excerpt)
        self.assertNotIn("private-bearer", excerpt)
        self.assertEqual("", failure_excerpt(200, b"{}"))
        self.assertEqual("", failure_excerpt(403, b""))

    def test_server_errors_and_non_json_bodies_contribute_nothing(self):
        from runtime import failure_excerpt
        page = b"System.InvalidOperationException: private store connection string Host=db;Password=x\n   at Private.Frame()"
        self.assertEqual("", failure_excerpt(500, page))
        self.assertEqual("", failure_excerpt(503, json.dumps({"detail": "private cause"}).encode()))
        self.assertEqual("", failure_excerpt(403, page))
        self.assertEqual("", failure_excerpt(403, b"[\"not an object\"]"))

    def test_a_secret_crossing_the_bound_is_redacted_before_truncation(self):
        from runtime import failure_excerpt
        detail = "x" * 390 + " Bearer " + "s" * 200
        excerpt = failure_excerpt(400, json.dumps({"detail": detail}).encode())
        self.assertNotIn("sss", excerpt)
        self.assertLessEqual(len(excerpt), len("; body: ") + 400)


if __name__ == "__main__":
    unittest.main()
