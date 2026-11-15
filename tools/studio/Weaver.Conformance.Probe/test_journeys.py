"""Fail-closed coordinator boundaries; these tests are not runtime witness evidence."""
from pathlib import Path
import copy
import hashlib
import json
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from weaver_transport import Response, WeaverTransportError
from journey_runtime import expect_error, same, BASE_GRANTS, browser_inputs, JourneyRuntime
from contextlib import contextmanager
from unittest.mock import DEFAULT
from evidence import REQUIRED_CASES
from effects_journeys import (CLASSED_RESTART_CASES, EVIDENCE_CATEGORY, RESTART_REQUIREMENTS,
                              RESTART_SECTION as EFFECT_RESTART_SECTION)
from upgrade_journeys import LEGACY_RESTART_CASES
from journeys import (_unchanged, _seed_principals, qualify_distributions, _semantic_refusal, _remove, _incompatible, _restore,
                      _autonomy_preflight)

ORIGINAL_SEQUENCE = ("_bootstrap", "freeze_consumer_baseline", "_module_missing", "_install", "_positives", "_active_resource",
                     "_context_controls", "_resource_absence", "_authority_controls", "_stale", "_deny", "_cancel", "_redaction",
                     "_concurrent_cas", "_remove", "_incompatible", "_restore", "qualify_context_disclosure")


class JourneyBoundariesTests(unittest.TestCase):
    def restore_runtime(self, root):
        deltas = {}
        for name in ("backend", "studio"):
            original, staged = root / (name + "-author.nupkg"), root / (name + "-feed.nupkg")
            data = ("synthetic retained " + name + " archive bytes").encode()
            original.write_bytes(data); staged.write_bytes(data)
            deltas[name] = {"added": [{"sourceArchive": str(original), "path": str(staged), "sha256": hashlib.sha256(data).hexdigest()}]}
        runtime = SimpleNamespace(run=SimpleNamespace(root=root), backend=object(), studio=object(),
            witness={"hostId": "test-host", "backendFeature": "backend", "studioFeature": "studio"},
            cas_control=root / "control", cas_receipt=root / "receipt", record=Mock(),
            snapshot=Mock(return_value=({"revision": "r1", "value": {"unchanged": True}}, {}, {})),
            native=SimpleNamespace(reconcile=Mock(return_value=({}, {"receipt": {}})), feature=Mock(return_value={"evidence": []})))
        return runtime, json.loads(json.dumps(deltas))

    def test_restore_identity_guard_reads_actual_files_from_serialized_delta_paths(self):
        with tempfile.TemporaryDirectory() as temporary:
            runtime, deltas = self.restore_runtime(Path(temporary))
            self.assertIsInstance(deltas["backend"]["added"][0]["path"], str)
            self.assertIsInstance(deltas["backend"]["added"][0]["sourceArchive"], str)
            with patch("journeys.restore_resource_archives") as restore, patch("journeys._active_resource", return_value=[]) as active:
                _restore(runtime, object(), deltas)
            self.assertEqual(2, restore.call_count); self.assertEqual(2, runtime.native.reconcile.call_count)
            active.assert_called_once(); runtime.record.assert_called_once()

    def test_restore_refuses_changed_copy_original_expected_digest_or_owner(self):
        for kind in ("copy", "original", "expected-sha", "both-archives", "owner"):
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as temporary:
                runtime, deltas = self.restore_runtime(Path(temporary)); item = deltas["studio"]["added"][0]
                if kind in ("copy", "both-archives"): Path(item["path"]).write_bytes(b"changed bytes")
                if kind in ("original", "both-archives"): Path(item["sourceArchive"]).write_bytes(b"changed bytes")
                if kind == "expected-sha": item["sha256"] = "f" * 64
                if kind == "owner": runtime.snapshot.side_effect = [({"value": True}, {}, {}), ({"value": False}, {}, {})]
                with patch("journeys.restore_resource_archives"), patch("journeys._active_resource", return_value=[]):
                    with self.assertRaisesRegex(RuntimeError, "Restor"):
                        _restore(runtime, object(), deltas)
                runtime.record.assert_not_called()

    class BrowserObserved(Exception):
        pass

    def incompatible_runtime(self):
        package = {"id": "Resource.Studio", "version": "1.0.1-incompatible", "sha256": "a" * 64}
        manifest = {"id": "Resource", "version": package["version"], "requiredSdkVersion": "999.0.0", "requiredHostVersion": "*",
                    "entry": "/module.js", "styles": ["/module.css"], "capabilities": ["test-resource"]}
        # This is the actual slim management DTO shape: availability is server
        # discovery and requirements belong only to the full bootstrap manifest.
        module = {"id": manifest["id"], "version": package["version"], "status": "available", "compatibility": "incompatible",
                  "manifest": {key: manifest[key] for key in ("entry", "styles", "capabilities")}}
        registry, bootstrap = {"modules": [module]}, {"sdkVersion": "1.0.0", "hostVersion": "1.0.0", "modules": [manifest]}
        native = SimpleNamespace(reconcile=Mock(return_value=({}, {"receipt": {"synthetic": "reconcile"}})),
            feature=Mock(return_value={"evidence": []}), membership=Mock(return_value=(
                {package["id"].lower(): {"id": package["id"], "version": package["version"], "installPath": "/synthetic/native"}}, {"receipt": {"synthetic": "membership"}})))
        def read(_client, _name, path):
            return copy.deepcopy(registry if path == "/_elsa/module-management/registry" else bootstrap), {"receipt": {"syntheticPath": path}}
        native.json = Mock(side_effect=read)
        runtime = SimpleNamespace(native=native, studio=object(), witness={"moduleId": "Resource", "studioFeature": "ResourceFeature"},
            snapshot=Mock(return_value=({"revision": "r1", "value": {"unchanged": True}}, {}, {})),
            run=SimpleNamespace(report={"authors": {"incompatibleStudio": {"requiredSdkVersion": "999.0.0", "packed": {"package": package}},
                "studio": {"packed": {"package": {"version": "1.0.0"}}}}}, save=Mock()), output=Mock(return_value=Path("/synthetic")), save=Mock(return_value={}))
        records = SimpleNamespace(add=Mock())
        def browser(sink, name, journey, *, extra):
            sink.add(name, {"status": "failed", "expectedFailureObserved": True, "actual": {"stage": "module-admission", "diagnostic": "incompatible"},
                "observations": {"browserContributionsAbsent": True, "commonStudioUsable": True}, "evidence": []})
            raise self.BrowserObserved()  # Stop before cleanup; no unit fixture represents a native archive.
        runtime.browser = Mock(side_effect=browser)
        return runtime, records, registry, bootstrap

    def test_incompatible_uses_full_manifest_and_keeps_slim_discovery_status_distinct(self):
        runtime, records, registry, bootstrap = self.incompatible_runtime()
        with patch("journeys.select_native_archives", return_value=[]), patch("journeys.stage_native_archives"):
            with self.assertRaises(self.BrowserObserved): _incompatible(runtime, records)
        self.assertEqual(["/_elsa/module-management/registry", "/_elsa/studio/modules"], [call.args[2] for call in runtime.native.json.call_args_list])
        observed = records.add.call_args.args[1]
        self.assertEqual("999.0.0", observed["nativeIncompatibleModule"]["requiredSdkVersion"])
        self.assertEqual("*", observed["nativeIncompatibleModule"]["requiredHostVersion"])
        self.assertEqual("available", observed["nativeIncompatibleModule"]["discoveryStatus"])
        self.assertEqual("incompatible", observed["nativeIncompatibleModule"]["compatibility"])
        self.assertIn({"syntheticPath": "/_elsa/studio/modules"}, observed["evidence"])
        self.assertEqual(1, runtime.native.feature.call_count)  # Browser must run before feature disable/removal.

    def test_incompatible_refuses_wrong_declaration_identity_or_compatibility_before_browser(self):
        for kind in ("missing-manifest", "duplicate-manifest", "version", "sdk", "actual-sdk-supported", "missing-sdk-declaration",
                     "host", "compatible", "missing-registry", "entry"):
            with self.subTest(kind=kind):
                runtime, records, registry, bootstrap = self.incompatible_runtime()
                if kind == "missing-manifest": bootstrap["modules"] = []
                elif kind == "duplicate-manifest": bootstrap["modules"].append(copy.deepcopy(bootstrap["modules"][0]))
                elif kind == "version": bootstrap["modules"][0]["version"] = "other-version"
                elif kind == "sdk": bootstrap["modules"][0]["requiredSdkVersion"] = bootstrap["sdkVersion"]
                elif kind == "actual-sdk-supported": bootstrap["sdkVersion"] = "999.0.0"
                elif kind == "missing-sdk-declaration": del bootstrap["modules"][0]["requiredSdkVersion"]
                elif kind == "host": bootstrap["modules"][0]["requiredHostVersion"] = "999.0.0"
                elif kind == "compatible": registry["modules"][0]["compatibility"] = "compatible"
                elif kind == "missing-registry": registry["modules"] = []
                else: bootstrap["modules"][0]["entry"] = "/different-module.js"
                with patch("journeys.select_native_archives", return_value=[]), patch("journeys.stage_native_archives"):
                    with self.assertRaises(RuntimeError): _incompatible(runtime, records)
                runtime.browser.assert_not_called(); records.add.assert_not_called()

    def test_changed_disable_and_reconcile_generations_cannot_mask_package_still_installed(self):
        package = {"id": "Resource.Package", "version": "1.2.3", "sha256": "a" * 64}
        inventory = {"resource.package": {"id": package["id"], "version": package["version"], "installPath": "/owned/native/resource"}}
        class Native:
            def __init__(self, removal_generation): self.removal_generation = removal_generation
            def membership(self, *args, **kwargs): return inventory, {"receipt": {}, "body": {}}
            def generation(self, client, name):
                return {"generation": self.removal_generation if name.endswith("removed-generation") else "before-disable", "tools": [], "contextSources": []}, {"receipt": {}}
            def feature(self, *args, **kwargs): return {"after": {"generation": "after-disable"}, "evidence": []}
            def reconcile(self, *args, **kwargs): return {}, {"receipt": {}}
        runtime = SimpleNamespace(snapshot=lambda name: ({"unchanged": True}, {}, {}),
            prepare=lambda name: SimpleNamespace(evidence=[]), approve=lambda turn: None,
            backend=object(), studio=object(), witness={"backendFeature": "resource-feature"},
            run=SimpleNamespace(report={"authors": {"backend": {"packed": {"package": package}}}}))
        for generation in ("after-remove", "after-disable"):
            runtime.native = Native(generation)
            with self.subTest(removalGeneration=generation), patch("journeys.remove_resource_archives") as remove:
                # The same strict membership failure must be reached whether removal
                # produces another serving generation or keeps the already-disabled one.
                with self.assertRaisesRegex(RuntimeError, "left the resource package installed"):
                    _remove(runtime, SimpleNamespace(), {"backend": {}})
                remove.assert_called_once()

    def test_controls_get_a_fresh_private_parent_without_changing_protocol_directory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            protocol = root / "prepared-protocol"
            protocol.mkdir(mode=0o755)
            protocol.chmod(0o755)
            command = protocol / "command.json"
            run = SimpleNamespace(root=root, profiles={"backend": {"commandFile": command}})
            runtime = JourneyRuntime(run, "http://127.0.0.1:51001", "http://127.0.0.1:51002", {}, [])
            self.assertEqual(root / "journeys" / "controls", runtime.controls)
            self.assertEqual(0o700, runtime.controls.stat().st_mode & 0o777)
            self.assertEqual(0o755, protocol.stat().st_mode & 0o777)
            self.assertEqual(command, runtime.command_file)
            for control in (runtime.gate_file, runtime.cas_control, runtime.cas_receipt):
                self.assertEqual(runtime.controls, control.parent)
                self.assertFalse(control.is_symlink())
            self.assertEqual(0o600, runtime.gate_file.stat().st_mode & 0o777)
            self.assertEqual("release", json.loads(runtime.gate_file.read_text())["state"])
            self.assertFalse(runtime.cas_control.exists()); self.assertFalse(runtime.cas_receipt.exists())

    def test_independent_runtime_can_retain_a_separate_journey_namespace_without_replacing_prior_evidence(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            run = SimpleNamespace(root=root, profiles={"backend": {"commandFile": root / "command.json"}})
            normal = JourneyRuntime(run, "http://127.0.0.1:51001", "http://127.0.0.1:51002", {}, [])
            original = normal.gate_file.read_bytes()
            upgrade = JourneyRuntime(run, "http://127.0.0.1:51003", "http://127.0.0.1:51004", {}, [], output_namespace="upgrade-copilot")
            self.assertEqual(root / "upgrade-copilot", upgrade.root)
            self.assertNotEqual(normal.controls, upgrade.controls)
            self.assertEqual(original, normal.gate_file.read_bytes())
            with self.assertRaises(FileExistsError):
                JourneyRuntime(run, "http://127.0.0.1:51003", "http://127.0.0.1:51004", {}, [], output_namespace="upgrade-copilot")
            for name in (None, "", "../escape", "/absolute", ".", "..", "x" * 97):
                with self.subTest(name=name), self.assertRaisesRegex(RuntimeError, "namespace"):
                    JourneyRuntime(run, "http://127.0.0.1:51003", "http://127.0.0.1:51004", {}, [], output_namespace=name)

    def test_browser_export_summary_cannot_copy_private_auth_or_config_paths(self):
        config = {"journey": "workflow", "provider": "copilot", "studioOrigin": "http://127.0.0.1:51001", "backendOrigin": "http://127.0.0.1:51002",
                  "storageState": "/private/auth-state.json", "bearer": "PRIVATE_CREDENTIAL", "commandFile": "/private/command.json",
                  "traceFile": "/private/trace.jsonl", "workflow": {"definitionId": "definition", "activityId": "activity", "draftId": "draft",
                  "definitionPath": "/design/workflows/definitions/definition", "draftPath": "/design/workflows/drafts/draft",
                  "validationPath": "/design/workflows/drafts/draft/validations", "privateAuth": "/private/nested-auth.json", "evidence": []}}
        summary = browser_inputs(config)
        self.assertEqual({"journey", "provider", "studioOrigin", "backendOrigin", "driverSha256", "workflow"}, set(summary))
        self.assertEqual({"definitionId", "activityId", "draftId", "definitionPath", "draftPath", "validationPath"}, set(summary["workflow"]))
        self.assertNotIn("/private/", json.dumps(summary)); self.assertNotIn("PRIVATE_CREDENTIAL", json.dumps(summary))
        config["workflow"]["draftPath"] += "?accessToken=PRIVATE_CREDENTIAL"
        with self.assertRaises(RuntimeError): browser_inputs(config)

    def test_browser_export_summary_exports_only_the_fields_a_journey_addresses(self):
        base = {"journey": "workflow-version-history", "provider": "copilot", "studioOrigin": "http://127.0.0.1:51001",
                "backendOrigin": "http://127.0.0.1:51002"}
        # Witness f052ff221: the version-history journey addresses a definition and its draft path, no draft id or activity.
        version = {"definitionId": "definition", "definitionPath": "/design/workflows/definitions/definition",
                   "draftPath": "/design/workflows/drafts/draft", "version": {"id": "v2", "path": "/design/workflows/versions/v2"}}
        self.assertEqual({"definitionId", "definitionPath", "draftPath"}, set(browser_inputs({**base, "workflow": version})["workflow"]))
        executable = {"executable": {"artifactId": "artifact", "artifactHash": "sha256:abc", "path": "/runtime/workflows/executables/artifact"}}
        self.assertEqual({"executable"}, set(browser_inputs({**base, "workflow": executable})["workflow"]))
        # Every present field is still validated, and some identity is required.
        for key, value in (("activityId", ""), ("activityId", None), ("draftId", 7), ("definitionPath", "relative/path"),
                           ("draftPath", "/x?accessToken=PRIVATE")):
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                browser_inputs({**base, "workflow": {**version, key: value}})
        with self.assertRaises(RuntimeError): browser_inputs({**base, "workflow": {"definitionPath": "/design/workflows/definitions/d"}})
        with self.assertRaises(RuntimeError):
            browser_inputs({**base, "workflow": {"executable": {"artifactId": "a", "artifactHash": "h", "path": "/x?token=1"}}})
        # A present identity is never null or a non-object: it would otherwise pass as "some identity" and export nothing.
        for key, value in (("executable", None), ("run", None), ("executable", "artifact"), ("run", [])):
            with self.subTest(key=key, value=value), self.assertRaises(RuntimeError):
                browser_inputs({**base, "workflow": {key: value}})
            with self.subTest(key=key, value=value, beside="definition"), self.assertRaises(RuntimeError):
                browser_inputs({**base, "workflow": {**version, key: value}})
        run = {"run": {"workflowExecutionId": "run-1", "path": "/runtime/workflows/instances/run-1"}}
        self.assertEqual({"run"}, set(browser_inputs({**base, "workflow": run})["workflow"]))

    def test_semantic_refusal_requires_native_failure_and_correlated_completion(self):
        result = {"toolCallId": "native-call", "toolName": "declared_tool", "succeeded": False,
                  "content": "The actual declared operation was refused.", "payload": None, "receipt": None}
        events = [{"kind": "toolCallStarted", "payload": {"toolCallId": "native-call", "toolName": "declared_tool"}},
                  {"kind": "toolCallCompleted", "payload": {"toolCallId": "native-call", "toolName": "declared_tool", "succeeded": False,
                   "summary": result["content"], "result": result}}, {"kind": "contributedResult", "payload": result}]
        turn = lambda rows: SimpleNamespace(stream=SimpleNamespace(frames=[SimpleNamespace(value=row) for row in rows]))
        sdk = {"error": {"code": "declared.refused", "message": result["content"]}}
        _semantic_refusal(turn(events), sdk, "declared_tool")
        for invalid in ([], events[:2], events[1:], [events[0], events[2]]):
            with self.subTest(events=invalid), self.assertRaises(RuntimeError): _semantic_refusal(turn(invalid), sdk, "declared_tool")
        for key, value in (("toolCallId", "unrelated-sdk-call"), ("succeeded", True), ("summary", "SDK transport completed.")):
            changed = json.loads(json.dumps(events)); changed[1]["payload"][key] = value
            with self.subTest(field=key), self.assertRaises(RuntimeError): _semantic_refusal(turn(changed), sdk, "declared_tool")

    def test_owner_comparison_does_not_coerce_json_boolean_to_integer(self):
        with self.assertRaises(RuntimeError):
            _unchanged({"value": {"visible": True}}, {"value": {"visible": 1}})
        self.assertFalse(same({"a": False}, {"a": 0}))

    def test_owner_comparison_keeps_unknown_nested_values(self):
        before = {"revision": "r1", "value": {"widgets": [{"id": "other", "options": {"unknown": [True, None, {"x": 2}]}}]}}
        after = json.loads(json.dumps(before))
        self.assertTrue(all(_unchanged(before, after).values()))
        after["value"]["widgets"][0]["options"]["unknown"].append("lost-field")
        with self.assertRaises(RuntimeError): _unchanged(before, after)

    def test_identical_http_failure_status_cannot_mask_wrong_cause(self):
        def response(code, status=403):
            return Response("POST", "/_elsa/weaver/proposals/p/execute", status, {},
                            json.dumps({"error": {"code": code, "message": "observed native refusal"}}).encode(), {})
        self.assertEqual("weaver.proposal.forbidden", expect_error(response("weaver.proposal.forbidden"), 403, "weaver.proposal.forbidden")["error"]["code"])
        with self.assertRaises(RuntimeError): expect_error(response("weaver.proposal.expired"), 403, "weaver.proposal.forbidden")
        with self.assertRaises(WeaverTransportError): expect_error(response("weaver.proposal.forbidden", 409), 403, "weaver.proposal.forbidden")

    def test_private_principal_recipe_is_concrete_and_isolates_tenant_axis(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "appsettings.json").write_text(json.dumps({"Qualification": {"StudioOrigin": "http://127.0.0.1:51001"}, "unrelated": {"preserve": True}}))
            run = SimpleNamespace(profiles={"backend": {"published": root}})
            principals = _seed_principals(run)
            settings = json.loads((root / "appsettings.json").read_text())
            self.assertEqual({"preserve": True}, settings["unrelated"])
            self.assertEqual(principals[0]["subject"], principals[2]["subject"])
            self.assertNotEqual(principals[0]["tenant"], principals[2]["tenant"])
            self.assertNotEqual(principals[0]["subject"], principals[1]["subject"])
            # Spec 1193 T012: the four workflow actors are appended; the original indexes keep their meaning. T036 appends the
            # library actor: tag assignment in the admin's tenant, without tag management or view sharing.
            # T045 appends the Run actor: runtime read and structure inspection in the admin's tenant, without value inspection.
            self.assertEqual(11, len(principals))
            reader, manager, tenant_reader, catalog_denied, library_limited, run_limited = principals[5:]
            self.assertEqual(principals[0]["tenant"], run_limited["tenant"])
            self.assertTrue({"workflows/runtime:read", "workflows/activity-executions:inspect"} <= set(run_limited["exactDirectPermissions"]))
            self.assertFalse({"workflows/activity-executions:inspect-values", "workflows/activity-executions:resolve-value-payloads",
                              "workflows/runtime:manage"} & set(run_limited["exactDirectPermissions"]))
            self.assertEqual(principals[0]["tenant"], library_limited["tenant"])
            self.assertIn("workflows/design/tag-assignments:manage", library_limited["exactDirectPermissions"])
            self.assertFalse({"workflows/design/tags:manage", "workflows/design/views:share", "workflows/design:manage"} & set(library_limited["exactDirectPermissions"]))
            self.assertNotIn("activities/design:read", catalog_denied["exactDirectPermissions"])
            # Every dedicated actor discovers its owner links through GET /capabilities.
            self.assertTrue(all("api-capabilities:read" in item["exactDirectPermissions"] for item in principals[5:]))
            self.assertEqual((principals[0]["tenant"], principals[0]["tenant"], principals[2]["tenant"]),
                             (reader["tenant"], manager["tenant"], tenant_reader["tenant"]))
            self.assertIn("workflows/design:read", reader["exactDirectPermissions"])
            self.assertNotIn("workflows/design:manage", reader["exactDirectPermissions"])
            self.assertTrue({"workflows/design:manage", "activities/design:read"} <= set(manager["exactDirectPermissions"]))
            # The second tenant's actor only reads the catalog: the host's one persistence scope would refuse its workflows.
            self.assertEqual({"workflows/design:read", "activities/design:read"},
                             {key for key in tenant_reader["exactDirectPermissions"] if key.startswith(("workflows/", "activities/"))})
            self.assertEqual(len(principals), len({(item["tenant"], item["subject"]) for item in principals}))
            self.assertEqual(principals, settings["Qualification"]["Principals"])
            self.assertEqual(set(BASE_GRANTS), set(settings["Qualification"]["GrantProfiles"]["full"]))
            self.assertNotIn("studio/preferences:read", settings["Qualification"]["GrantProfiles"]["without-read"])
            self.assertNotIn("studio/preferences:write", settings["Qualification"]["GrantProfiles"]["without-write"])
            self.assertTrue(all("*" not in key for profile in settings["Qualification"]["GrantProfiles"].values() for key in profile))
            self.assertEqual(0o600, (root / "appsettings.json").stat().st_mode & 0o777)

    def test_failed_or_partial_preparation_never_reaches_a_host(self):
        for status in ("failed", "running", "passed"):
            run = SimpleNamespace(report={"status": status})
            with self.subTest(status=status), patch("journeys.server") as launch:
                with self.assertRaises(RuntimeError): qualify_distributions(run)
                launch.assert_not_called()
        run = SimpleNamespace(source="a" * 40, report={"status": "preparing", "authors": {"protocol": {"status": "building"}}, "consumers": {}, "producer": {"source": "a" * 40}})
        with patch("journeys.server") as launch, patch("journeys.build_authors") as authors:
            with self.assertRaises(RuntimeError): qualify_distributions(run)
            authors.assert_not_called(); launch.assert_not_called()



class CompleteWitnessOrchestrationTests(unittest.TestCase):
    """Synthetic coordinator order/isolation; native producers and deep readers are ports, not witness evidence."""
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "shells.json").write_text(json.dumps({"CShells": {"Shells": {"Authoring": {"Features": {"AcmeCopilotProtocolFixture": {}}}}}}))
        self.run = SimpleNamespace(report={"status": "distributions-prepared", "cases": []}, source="a" * 40, root=self.root,
                                   profiles={"backend": {"published": self.root}}, assert_immutable_inputs=Mock(), save=Mock())
        self.hosts_open = {"backend": False, "studio": False}
        self.backend_host = SimpleNamespace(restarts=[{"reason": "synthetic"}])
        self.classed_records = self.restart_records(CLASSED_RESTART_CASES)
        self.legacy_records = self.restart_records(LEGACY_RESTART_CASES)

    def restart_records(self, identities):
        # Only the index shape is real here. Both deep native readers are mocked below;
        # these synthetic payloads cannot establish native qualification.
        return [{"id": identity, "status": "passed", "source": self.run.source, "evidenceCategory": EVIDENCE_CATEGORY,
                 "liveProvider": False, "qualified": False, "independentPackageWitness": False,
                 "record": {"synthetic": identity}} for identity in identities]

    def qualify(self, **overrides):
        self.assertEqual("distributions-prepared", self.run.report["status"])
        self.assertEqual([], self.run.report["cases"])
        self.assertNotIn(EFFECT_RESTART_SECTION, self.run.report)
        names = (*ORIGINAL_SEQUENCE, "owned_origin", "configure_runtime", "_seed_principals", "JourneyRuntime", "private_json", "CaseRecords",
                 "server", "RestartableServer", "qualify_workflow_authorization", "qualify_workflow_truth_seams", "qualify_workflow_batch", "qualify_workflow_explain", "qualify_workflow_library", "qualify_workflow_inspection", "qualify_workflow_run", "_autonomy_preflight", "qualify_witness_autonomy", "qualify_witness_restarts",
                 "qualify_effects", "qualify_classed_effect_restarts", "qualify_legacy_effect_restarts",
                 "capture_consumer_after", "validate_complete_witness")
        order = []
        self.saved_states = []
        self.run.save.side_effect = lambda: self.saved_states.append(
            (self.run.report["status"], self.run.report["independentPackageWitness"], tuple(order)))
        sections = {"qualify_workflow_authorization": "workflowAuthorizationQualification",
                    "qualify_workflow_truth_seams": "workflowTruthSeamQualification",
                    "qualify_workflow_batch": "workflowBatchQualification", "qualify_workflow_explain": "workflowExplainQualification",
                    "qualify_workflow_library": "workflowLibraryQualification", "qualify_workflow_inspection": "workflowInspectionQualification",
                    "qualify_workflow_run": "workflowRunQualification", "qualify_witness_autonomy": "autonomyLifecycleQualification",
                    "qualify_effects": "moduleEffectsQualification", "qualify_witness_restarts": "restartRecoveryQualification"}

        @contextmanager
        def host(name):
            self.hosts_open[name] = True
            try:
                yield self.backend_host if name == "backend" else None
            finally:
                self.hosts_open[name] = False
                order.append(name + "-closed")

        def recorder(name):
            def call(*args, **kwargs):
                order.append(name)
                self.assertFalse(self.run.report["independentPackageWitness"])
                self.assertNotEqual("passed", self.run.report["status"])
                if name == "qualify_context_disclosure":
                    self.run.report["cases"] = [{"id": case, "synthetic": {"original": index}}
                                                for index, case in enumerate(sorted(REQUIRED_CASES))]
                    self.run.report["contextDisclosureQualification"] = {"status": "passed"}
                if name == "qualify_classed_effect_restarts":
                    self.assertTrue(all(self.hosts_open.values()))
                    self.assertIs(args[0], self.runtime); self.assertIs(args[1], self.backend_host)
                    self.earlier_records = copy.deepcopy({key: value for key, value in self.run.report.items()
                        if key in ("cases", "contextDisclosureQualification", *sections.values())})
                if name == "qualify_legacy_effect_restarts":
                    self.assertFalse(any(self.hosts_open.values()))
                    self.assertIs(args[0], self.runtime)
                    self.assertEqual("verifying", self.run.report["status"])
                    self.assertIn(self.run.report[EFFECT_RESTART_SECTION]["status"], ("running", "failed"))
                    self.assertEqual(self.classed_records, self.run.report[EFFECT_RESTART_SECTION]["records"])
                if name in ("require_classed_effect_restarts", "require_legacy_effect_restarts"):
                    self.assertFalse(any(self.hosts_open.values()))
                    self.assertEqual((self.root, self.run.source), (args[1], kwargs["source"]))
                    self.assertEqual(self.classed_records if name == "require_classed_effect_restarts" else self.legacy_records, args[0])
                if name == "validate_complete_witness":
                    self.assertFalse(any(self.hosts_open.values()))
                    self.assertEqual("verifying", self.run.report["status"])
                    self.assertEqual("passed", self.run.report[EFFECT_RESTART_SECTION]["status"])
                    self.assertEqual(1, order.count("require_classed_effect_restarts"))
                    self.assertEqual(1, order.count("require_legacy_effect_restarts"))
                if name in overrides: return overrides[name](*args, **kwargs)
                if name == "server": return host("studio")
                if name == "RestartableServer": return host("backend")
                if name in sections:
                    self.run.report[sections[name]] = {"status": "passed", "synthetic": {"producer": name}}
                if name == "JourneyRuntime":
                    self.runtime = SimpleNamespace(gate_file=self.root / "gate.json", root=self.root, select_provider=Mock(), browser=Mock(),
                                                   native=SimpleNamespace(feature=Mock()), studio=object(), witness={"studioFeature": "resource"})
                    return self.runtime
                if name == "qualify_classed_effect_restarts": return copy.deepcopy(self.classed_records)
                if name == "qualify_legacy_effect_restarts": return copy.deepcopy(self.legacy_records)
                if name == "_install": return {}
                if name == "_autonomy_preflight": return {"gateReleased": True, "evidence": []}
                return DEFAULT
            return call
        # Keep append_effect_restarts and its manifest/source-binding gate real. Only
        # the external native record producers/readers stop at synthetic test ports.
        with patch.multiple("journeys", **{name: Mock(side_effect=recorder(name)) for name in names}), \
                patch("effects_journeys.require_classed_effect_restarts", side_effect=recorder("require_classed_effect_restarts")), \
                patch("upgrade_journeys.require_legacy_effect_restarts", side_effect=recorder("require_legacy_effect_restarts")):
            try:
                qualify_distributions(self.run)
            finally:
                self.order = order
        return order

    def test_inventory_runs_once_after_the_unchanged_original_sequence_and_before_cleanup_capture(self):
        order = self.qualify()
        sequence = [name for name in order if name in ORIGINAL_SEQUENCE]
        self.assertEqual([*ORIGINAL_SEQUENCE[:-1], "_positives", "qualify_context_disclosure"], sequence)
        tail = order[order.index("qualify_context_disclosure") + 1:]
        self.assertEqual(["qualify_workflow_authorization", "qualify_workflow_truth_seams", "qualify_workflow_batch", "qualify_workflow_explain", "qualify_workflow_library", "qualify_workflow_inspection", "qualify_workflow_run", "_autonomy_preflight", "qualify_witness_autonomy", "qualify_effects", "qualify_witness_restarts",
                          "qualify_classed_effect_restarts", "capture_consumer_after", "studio-closed", "backend-closed",
                          "qualify_legacy_effect_restarts", "require_classed_effect_restarts", "require_legacy_effect_restarts",
                          "validate_complete_witness"], tail)
        self.assertEqual(1, order.count("qualify_workflow_authorization"))
        self.assertEqual(1, order.count("qualify_workflow_truth_seams"))
        self.assertEqual(1, order.count("qualify_workflow_batch"))
        self.assertEqual(1, order.count("qualify_workflow_explain"))
        self.assertEqual(1, order.count("qualify_workflow_library"))
        self.assertEqual(1, order.count("qualify_workflow_inspection"))
        self.assertEqual(1, order.count("qualify_workflow_run"))
        self.assertEqual(1, order.count("qualify_witness_autonomy"))
        self.assertEqual(1, order.count("qualify_effects"))
        self.assertEqual(1, order.count("qualify_witness_restarts"))
        for name in ("qualify_classed_effect_restarts", "qualify_legacy_effect_restarts", "require_classed_effect_restarts",
                     "require_legacy_effect_restarts", "validate_complete_witness"):
            self.assertEqual(1, order.count(name))
        self.assertEqual(1, order.count("RestartableServer"))
        self.assertEqual([{"reason": "synthetic"}], self.run.report["hostRestarts"])
        self.assertEqual(("passed", True), (self.run.report["status"], self.run.report["independentPackageWitness"]))
        self.assertEqual(self.earlier_records, {key: self.run.report[key] for key in self.earlier_records})
        section = self.run.report[EFFECT_RESTART_SECTION]
        self.assertEqual("passed", section["status"])
        self.assertEqual(list(RESTART_REQUIREMENTS), [row["id"] for row in section["records"]])
        self.assertEqual((14, 16, 30), (len(self.classed_records), len(self.legacy_records), len(section["records"])))
        self.assertTrue(all(status != "passed" and not independent for status, independent, _ in self.saved_states[:-1]))
        self.assertEqual(("passed", True, tuple(order)), self.saved_states[-1])
        self.assertFalse(hasattr(self.runtime, "run"))
        manifest = self.run.report["witnessManifest"]
        self.assertEqual(107 + len(manifest["moduleEffectsCases"]) + len(manifest["story1RestartRequirements"]), manifest["expectedCaseCount"])
        self.assertEqual((200, 30, 337), (len(manifest["moduleEffectsCases"]), len(manifest["story1RestartRequirements"]), manifest["expectedCaseCount"]))
        self.assertNotIn("stageCount", json.dumps(self.run.report["witnessManifest"]))

    def test_failed_matrix_changed_original_records_or_failed_gate_never_promote(self):
        def failed(_runtime): raise RuntimeError("anthropic-cancel-before-effect: lifecycle.gate")
        def relabels(_runtime): self.run.report["cases"][0]["status"] = "relabelled"
        def rejects(*_args): raise ValueError("Incomplete complete witness")
        def restart_failed(_runtime, _host): raise RuntimeError("copilot-dispatched-without-receipt: restart.dispatch_not_held")
        def restart_relabels(_runtime, _host): self.run.report["autonomyLifecycleQualification"]["status"] = "relabelled"
        for name, override, cause in (
                ("qualify_witness_autonomy", failed, "anthropic-cancel-before-effect: lifecycle.gate"),
                ("qualify_witness_autonomy", relabels, "The appended autonomy matrix changed the original or current-authority records"),
                ("validate_complete_witness", rejects, "Incomplete complete witness"),
                ("qualify_witness_restarts", restart_failed, "copilot-dispatched-without-receipt: restart.dispatch_not_held"),
                ("qualify_witness_restarts", restart_relabels, "The appended restart inventory changed an earlier witness record")):
            with self.subTest(override=override.__name__):
                self.setUp()
                with self.assertRaisesRegex(ValueError if override is rejects else RuntimeError, cause):
                    self.qualify(**{name: override})
                self.assert_not_promoted()
                self.assertEqual(1, self.order.count(name))
                if name in ("qualify_witness_autonomy", "qualify_witness_restarts"): self.assertNotIn("capture_consumer_after", self.order)

    def assert_not_promoted(self):
        self.assertFalse(self.run.report["independentPackageWitness"])
        self.assertNotEqual("passed", self.run.report["status"])
        self.assertTrue(all(status != "passed" and not independent for status, independent, _ in self.saved_states))

    def test_new_native_producer_failures_stop_before_final_promotion(self):
        def fails(*_args): raise RuntimeError("synthetic native producer failed")
        for name in ("qualify_classed_effect_restarts", "qualify_legacy_effect_restarts"):
            with self.subTest(producer=name):
                self.setUp()
                with self.assertRaisesRegex(RuntimeError, "synthetic native producer failed"):
                    self.qualify(**{name: fails})
                self.assert_not_promoted()
                self.assertEqual(1, self.order.count(name))
                self.assertFalse(any(self.hosts_open.values()))
                self.assertNotIn("validate_complete_witness", self.order)
                self.assertNotIn("require_classed_effect_restarts", self.order)
                if name == "qualify_classed_effect_restarts":
                    self.assertNotIn("capture_consumer_after", self.order)
                    self.assertNotIn("qualify_legacy_effect_restarts", self.order)
                else:
                    self.assertIn("capture_consumer_after", self.order)
                    self.assertEqual(self.classed_records, self.run.report[EFFECT_RESTART_SECTION]["records"])

    def test_new_restart_producers_cannot_change_original_effect_or_restart_records(self):
        for producer, records in (("qualify_classed_effect_restarts", "classed_records"),
                                  ("qualify_legacy_effect_restarts", "legacy_records")):
            for section in ("cases", "moduleEffectsQualification", "restartRecoveryQualification"):
                with self.subTest(producer=producer, changed=section):
                    self.setUp()
                    def relabels(*_args):
                        value = self.run.report[section]
                        if section == "cases": value[0]["synthetic"]["original"] = "relabelled"
                        else: value["synthetic"]["producer"] = "relabelled"
                        return copy.deepcopy(getattr(self, records))
                    cause = ("Classed recovery changed earlier native witness records" if producer == "qualify_classed_effect_restarts"
                             else "Upgrade qualification changed earlier witness records")
                    with self.assertRaisesRegex(RuntimeError, cause):
                        self.qualify(**{producer: relabels})
                    self.assert_not_promoted()
                    self.assertEqual(1, self.order.count(producer))
                    self.assertNotIn("validate_complete_witness", self.order)
                    if producer == "qualify_classed_effect_restarts":
                        self.assertNotIn("capture_consumer_after", self.order)
                    else:
                        self.assertEqual(1, self.order.count("require_classed_effect_restarts"))
                        self.assertEqual(1, self.order.count("require_legacy_effect_restarts"))

    def test_real_restart_index_gate_refuses_incomplete_duplicate_failed_or_foreign_records(self):
        for lane in ("classed_records", "legacy_records"):
            for kind in ("missing", "duplicate", "failed", "foreign-source", "foreign-id"):
                with self.subTest(lane=lane, kind=kind):
                    self.setUp()
                    rows = getattr(self, lane)
                    if kind == "missing": rows.pop()
                    elif kind == "duplicate": rows.append(copy.deepcopy(rows[0]))
                    elif kind == "failed": rows[0]["status"] = "failed"
                    elif kind == "foreign-source": rows[0]["source"] = "b" * 40
                    else: rows[0]["id"] = "synthetic-foreign-case"
                    cause = ("duplicate effects restart observations" if kind == "duplicate" else
                             "foreign effects restart record" if kind == "foreign-id" else
                             "incomplete effects upgrade/recovery matrix")
                    with self.assertRaisesRegex(RuntimeError, cause): self.qualify()
                    self.assert_not_promoted()
                    producer = "qualify_classed_effect_restarts" if lane == "classed_records" else "qualify_legacy_effect_restarts"
                    self.assertEqual(1, self.order.count(producer))
                    if kind not in ("duplicate", "foreign-id"):
                        self.assertEqual(1, self.order.count("qualify_legacy_effect_restarts"))
                        self.assertEqual("Module effects: " + cause, self.run.report[EFFECT_RESTART_SECTION]["failure"]["message"])
                    elif lane == "classed_records":
                        self.assertNotIn("qualify_legacy_effect_restarts", self.order)
                        self.assertNotIn("capture_consumer_after", self.order)
                    self.assertNotIn("validate_complete_witness", self.order)
                    self.assertNotIn("require_classed_effect_restarts", self.order)
                    self.assertNotIn("require_legacy_effect_restarts", self.order)

    def test_failed_native_reader_keeps_complete_index_failed_and_never_reaches_final_gate(self):
        def rejects(*_args, **_kwargs): raise RuntimeError("synthetic native reader refused")
        for name in ("require_classed_effect_restarts", "require_legacy_effect_restarts"):
            with self.subTest(reader=name):
                self.setUp()
                with self.assertRaisesRegex(RuntimeError, "synthetic native reader refused"):
                    self.qualify(**{name: rejects})
                self.assert_not_promoted()
                self.assertEqual(1, self.order.count(name))
                self.assertLess(self.order.index("qualify_legacy_effect_restarts"), self.order.index(name))
                section = self.run.report[EFFECT_RESTART_SECTION]
                self.assertEqual("failed", section["status"])
                self.assertEqual("RuntimeError", section["failure"]["type"])
                self.assertEqual("synthetic native reader refused", section["failure"]["message"])
                self.assertEqual(list(RESTART_REQUIREMENTS), [row["id"] for row in section["records"]])
                self.assertNotIn("validate_complete_witness", self.order)
                if name == "require_classed_effect_restarts": self.assertNotIn("require_legacy_effect_restarts", self.order)

    def preflight_runtime(self):
        bootstrap = Mock(); bootstrap.require_status.return_value = bootstrap; bootstrap.receipt = {"synthetic": "bootstrap"}
        bootstrap.json.return_value = {"data": {"providerStatus": "available"}}
        return SimpleNamespace(cas_control=self.root / "cas.json", gate_file=self.root / "gate.json", sign_in=Mock(),
                               transport=Mock(return_value=SimpleNamespace(bootstrap=Mock(return_value=bootstrap)))), bootstrap

    def test_preflight_releases_the_held_gate_and_reobserves_the_restored_size_contribution(self):
        runtime, _ = self.preflight_runtime()
        runtime.gate_file.write_text(json.dumps({"commandId": "original-cancel", "state": "hold"}))
        with patch("journeys._active_resource", return_value=[{"synthetic": "generation"}]) as active:
            value = _autonomy_preflight(runtime)
        active.assert_called_once_with(runtime, "autonomy-preflight")
        self.assertEqual({"gateReleased": True, "evidence": [{"synthetic": "generation"}, {"synthetic": "bootstrap"}]}, value)
        gate = json.loads(runtime.gate_file.read_text())
        self.assertEqual(("not-an-active-command", "release"), (gate["commandId"], gate["state"]))

    def test_preflight_refuses_an_armed_cas_rendezvous_or_unavailable_provider(self):
        runtime, _ = self.preflight_runtime()
        runtime.cas_control.write_text("{}")
        with patch("journeys._active_resource") as active, self.assertRaisesRegex(RuntimeError, "CAS"):
            _autonomy_preflight(runtime)
        active.assert_not_called(); self.assertFalse(runtime.gate_file.exists())
        runtime.cas_control.unlink()
        runtime, bootstrap = self.preflight_runtime()
        bootstrap.json.return_value = {"data": {"providerStatus": "unavailable"}}
        with patch("journeys._active_resource", return_value=[]), self.assertRaisesRegex(RuntimeError, "provider"):
            _autonomy_preflight(runtime)


if __name__ == "__main__": unittest.main()
