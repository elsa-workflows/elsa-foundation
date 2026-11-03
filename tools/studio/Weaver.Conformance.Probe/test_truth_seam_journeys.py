"""Integrity checks for the workflow-truth-seams driver; synthetic records are not native evidence."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

from artifacts import sha
from journey_runtime import BASE_GRANTS
from truth_seam_journeys import (ACTORS, EXPECTED_CASES, LAYOUT_TOOL, MANAGER, READ_TOOL, READER, SECTION, TENANT_MANAGER,
                                 TENANT_ONLY_VERSION, require_catalog, require_permissions, require_truth_turn,
                                 require_workflow_truth_seams, synthetic_actors, truth_principals, truth_seam_manifest_gaps)

TARGET = {"moduleId": "Elsa.Studio.Workflows", "resourceType": "workflow-definition", "resourceId": "wf-1", "revision": "unsaved-1"}
COMMAND = "command-1"


def owner_entries(tenant_only=True):
    rows = [{"activityVersionId": "flowchart-v1", "available": True, "version": "1.0.0"},
            {"activityVersionId": "write-line-v1", "available": True, "version": "1.0.0"},
            {"activityVersionId": "excluded-v2", "available": False, "version": "2.0.0"}]
    return ([{"activityVersionId": TENANT_ONLY_VERSION, "available": True, "version": "1.0.0"}] if tenant_only else []) + rows


def catalog_content(entries, included=None):
    included = len(entries) if included is None else included
    return {"status": "available", "reason": None, "totalCount": len(entries), "includedCount": included,
            "truncated": included < len(entries), "entries": copy.deepcopy(entries[:included])}


def permissions_content(manage):
    return {"workflowDefinitionId": "wf-1", "revision": "unsaved-1", "read": "allowed", "manage": "allowed" if manage else "denied",
            "capabilities": ["workflow.explain", "workflow.propose-change"] if manage else ["workflow.explain"],
            "tools": [READ_TOOL, LAYOUT_TOOL] if manage else [READ_TOOL]}


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


def rows(provider="copilot", tools=(READ_TOOL, LAYOUT_TOOL)):
    return [{"kind": provider + ".request", "data": {"commandId": COMMAND, "tools": [{"name": name} for name in tools] + [{"name": "acme_other"}]}},
            {"kind": provider + ".call", "data": {"commandId": COMMAND, "toolName": READ_TOOL}},
            {"kind": provider + ".return", "data": {"commandId": COMMAND}}]


class TurnTests(unittest.TestCase):
    def check(self, value=None, trace=None, provider="copilot"):
        return require_truth_turn(frames() if value is None else value, rows(provider) if trace is None else trace,
                                  provider=provider, command_id=COMMAND, target=TARGET)

    def test_a_completed_read_turn_yields_its_server_reads_and_the_offered_workflow_tools(self):
        for provider in ("copilot", "anthropic"):
            with self.subTest(provider=provider):
                observed = self.check(provider=provider)
                self.assertEqual(sorted([READ_TOOL, LAYOUT_TOOL]), observed["advertised"])
                self.assertEqual("allowed", observed["permissions"]["manage"])
                self.assertEqual(len(owner_entries()), observed["catalog"]["totalCount"])

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
        self.assertEqual({"totalCount": 4, "includedCount": 4, "truncated": False, "tenantOnly": True},
                         require_catalog(catalog_content(owner_entries()), owner_entries(), tenant_only=True))
        self.assertEqual({"totalCount": 4, "includedCount": 2, "truncated": True, "tenantOnly": True},
                         require_catalog(catalog_content(owner_entries(), 2), owner_entries(), tenant_only=True))
        other = owner_entries(tenant_only=False)
        self.assertFalse(require_catalog(catalog_content(other), other, tenant_only=False)["tenantOnly"])

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
        }
        for name, (value, entries, tenant_only) in cases.items():
            with self.subTest(change=name):
                with self.assertRaises(RuntimeError): require_catalog(value, entries, tenant_only=tenant_only)
        with self.assertRaises(RuntimeError): require_catalog(catalog_content(owner), [], tenant_only=True)


class PermissionTests(unittest.TestCase):
    def test_permissions_follow_the_owners_answers_and_the_offered_tools_follow_them(self):
        self.assertEqual("allowed", require_permissions(permissions_content(True), manage=True, read_status=200, manage_status=200,
                                                        advertised=sorted([READ_TOOL, LAYOUT_TOOL]), target=TARGET)["manage"])
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
        self.assertEqual([("witness-reader", "default"), ("witness-manager", "default"), ("witness-tenant-manager", "other")],
                         [(item["prefix"], item["tenant"]) for item in made])
        self.assertEqual((5, 6, 7), (READER, MANAGER, TENANT_MANAGER))
        self.assertTrue(set(BASE_GRANTS) <= set(made[0]["grants"]) and "workflows/design:manage" not in made[0]["grants"])
        self.assertTrue(all({"workflows/design:manage", "activities/design:read"} <= set(item["grants"]) for item in made[1:]))
        with self.assertRaises(RuntimeError): truth_principals(original[:4], lambda *args: {})
        with self.assertRaises(RuntimeError): truth_principals([{"tenant": "default"}] * 5, lambda *args: {})


class GateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def section(self, actors=None):
        records = []
        for name in sorted(EXPECTED_CASES):
            path = self.root / (name + ".json")
            path.write_text(json.dumps({"id": name, "status": "passed", "adapter": name.split("-", 1)[0], "actors": actors or synthetic_actors()}))
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
        with self.assertRaises(RuntimeError): require_workflow_truth_seams(changed, self.root, source="a" * 40)
        for role, value in (("reader", {"permissions": {"manage": "allowed"}, "catalog": {"tenantOnly": True}}),
                            ("tenant-manager", {"permissions": {"manage": "allowed"}, "catalog": {"tenantOnly": True}})):
            with self.subTest(actor=role):
                with self.assertRaises(RuntimeError):
                    require_workflow_truth_seams(self.section({**synthetic_actors(), role: value}), self.root, source="a" * 40)
        self.assertEqual("workflowTruthSeamQualification", SECTION)


if __name__ == "__main__":
    unittest.main()
