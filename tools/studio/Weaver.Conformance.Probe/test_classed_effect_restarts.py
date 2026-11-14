"""Synthetic deep-reader/driver controls only. Authored under the execution hold; native qualification is separate."""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

from case_records import retained
from consumers import DURABLE_EFFECTS_FEATURE, DURABLE_STORE_FEATURE, DURABLE_STORE_DEPENDENCY
import effects_journeys as effects
import restart_journeys as restarts
from test_effects_journeys import check_fixture, refusal_fixture


ORIGIN = "http://127.0.0.1:49100"
SOURCE = "a" * 40
EPOCH = restarts.instant("2026-11-14T10:00:00Z")
OWNER = {"subject": "actor-one", "tenantId": "tenant-one"}


class RecoveryFixture:
    """Actual wire shapes in disposable files, deliberately no runtime or product calls."""
    def __init__(self, root, provider="copilot", tool=effects.CHECK_TOOL, removed=False):
        self.root, self.sequence = Path(root), 0
        self.witness = json.loads((effects.TOOL / "witness.json").read_text())
        paths = {key: str(self.root / (key + ".jsonl")) for key in ("execution", "checks")}
        self.config = {"StudioHostId": effects.HOST, "ExecutionObservationFile": paths["execution"], "CheckObservationFile": paths["checks"]}
        features = [{"id": name, "enabled": True, "runtimeAvailable": True,
                     "configuration": self.config if name == "AcmeHomeWidget" else {}}
                    for name in (DURABLE_STORE_FEATURE, DURABLE_STORE_DEPENDENCY, DURABLE_EFFECTS_FEATURE, "AcmeHomeWidget")]
        self.catalog = {"revision": "feature-revision", "features": features}
        self.generation = {"generation": "before", "featureConfiguration": {"rootGuardUses": 0},
                           "tools": [{"name": name, "moduleId": effects.MODULE, "contractVersion": "1"}
                                     for name in (*effects.TOOLS, self.witness["readTool"], self.witness["writeTool"], self.witness["sizeTool"])],
                           "contextSources": [{"id": name, "moduleId": effects.MODULE} for name in ("acme.home.widget-context", "acme.home.effects-context")]}
        recovered_generation = dict(self.generation, generation="recovered")
        if removed: recovered_generation.update(tools=[], contextSources=[])
        before_process = {"pid": 1, "origin": ORIGIN, "exitCode": 0, "runtimeCacheFiles": {"packages": [], "http": []},
                          "inputsUnchangedAtCloseout": True, "assemblySha256": "b" * 64, "publishedDirectory": str(self.root),
                          "contentRoot": str(self.root), "contentRootIdentity": {"device": 1, "inode": 1}, "command": ["dotnet", "consumer.dll"]}
        restart = {"phase": restarts.GRACEFUL, "kind": "graceful", "name": "backend", "origin": ORIGIN,
                   "fromGeneration": 1, "toGeneration": 2, "stoppedAt": EPOCH + 20, "startedAt": EPOCH + 30,
                   "stoppedProcess": before_process, "startedProcess": dict(before_process, pid=2)}
        refs = {"catalog": self.native("/modularity/features", self.catalog), "restart": self.save(restart)}
        for name in ("identityBefore", "identityRecovered", "identityAfter"):
            refs[name] = self.native("/_elsa/identity/session", dict(OWNER, status="authenticated"))
        for name, enabled in (("policyBefore", True), ("policyRecovered", removed), ("policyRestored", True)):
            refs[name] = self.http("GET", "/_elsa/weaver/bootstrap", {"data": {"privileges": dict.fromkeys(effects.CLASSES, enabled),
                                         "providerStatus": "available", "recovery": "durable"}})
        for name, generation in (("generationBefore", self.generation), ("generationRecovered", recovered_generation),
                                 ("generationAfter", dict(self.generation, generation="after"))):
            refs[name] = self.native("/__qualification/generation", generation)
        self.value = {"id": provider + "/" + ("removed-owner-pending" if removed else tool + ":recovery-disabled"),
                      "provider": provider, "adapter": provider, "tool": tool, "status": "passed", "source": SOURCE,
                      "bindings": effects.effect_restart_bindings(), "evidenceCategory": effects.EVIDENCE_CATEGORY,
                      "liveProvider": False, "qualified": False, "independentPackageWitness": False, "backendOrigin": ORIGIN,
                      "witness": self.witness, "journalPaths": paths, "refs": refs, "fromGeneration": 1, "toGeneration": 2,
                      "snapshots": {}, "branches": {}}
        for name, at in (("before", EPOCH), ("recovered", EPOCH + 45), ("after", EPOCH + 70), ("repeated", EPOCH + 90)):
            self.value["snapshots"][name] = self.snapshot(paths, at, available=not (removed and name == "recovered"))
        for name in (("pending",) if removed else ("pending", "approved")):
            self.value["branches"][name] = self.branch(provider, tool, name, removed)
        if removed:
            archive = self.root / "feed.nupkg"; archive.write_bytes(b"independent archive")
            original = self.root / "author.nupkg"; original.write_bytes(archive.read_bytes())
            package = {"id": self.witness["backendPackage"], "version": "1.0.0", "sha256": hashlib.sha256(archive.read_bytes()).hexdigest()}
            self.value["package"] = package
            self.value["archiveDelta"] = {"added": [{"identity": package["id"] + "/1.0.0", "path": str(archive),
                                                     "sourceArchive": str(original), "sha256": package["sha256"]}], "baseline": {}}
            packages = [{"packageId": package["id"], "version": package["version"], "installPath": str(self.root / "installed")},
                        {"packageId": "Unrelated", "version": "2.0.0", "installPath": str(self.root / "unrelated")}]
            for name in ("installed", "disabled", "removed", "removedRecovered", "restored"):
                refs[name] = self.native("/__qualification/packages", {"snapshotAtUtc": "now", "persistedAtUtc": "now", "correlationId": name,
                    "packages": packages[1:] if name in ("removed", "removedRecovered") else packages})
            refs["generationRemoved"] = self.native("/__qualification/generation", dict(recovered_generation, generation="removed"))
            refs["disableTransition"] = self.transition(False)
            refs["restoreTransition"] = self.transition(True)
            for name in ("removeReconcile", "restoreReconcile"):
                refs[name] = self.native("/__qualification/reconcile", {"outcomeCode": 0, "runResult": {"isDegraded": False, "failedPackages": []}}, method="POST")

    def save(self, value):
        self.sequence += 1
        path = self.root / (str(self.sequence) + ".json")
        path.write_text(json.dumps(value))
        return retained(path)

    def change(self, reference, change):
        value = json.loads(Path(reference["path"]).read_text())
        change(value)
        return self.save(value)

    def native(self, path, value, method="GET"):
        return self.save({"origin": ORIGIN, "method": method, "path": path, "status": 200, "body": self.save(copy.deepcopy(value))})

    def http(self, method, path, value, status=200, body=None):
        raw = json.dumps(value)
        return self.save({"origin": ORIGIN, "method": method, "path": path, "status": status, "requestBody": body,
                          "rawBody": raw, "rawBodySha256": hashlib.sha256(raw.encode()).hexdigest()})

    def stream(self, session, frames):
        raw = "".join("data: " + json.dumps(frame) + "\n\n" for frame in frames)
        return self.save({"origin": ORIGIN, "method": "GET", "path": "/_elsa/weaver/sessions/" + session + "/stream", "status": 200,
                          "headers": {"content-type": "text/event-stream"}, "rawBody": raw, "frames": [{"value": frame} for frame in frames]})

    def snapshot(self, paths, at, available=True):
        fixture = check_fixture()
        widget, layout, checks = fixture[2], fixture[4], fixture[6]
        entries = {**{key: widget[key] for key in ("widgetId", "hostId", "revision")}, "entryCount": 0, "entries": []}
        result = {"layout": self.native(effects.HOME_PREFERENCES_PATH, layout), "journals": {}}
        if available:
            result.update(widget=self.native(effects.OWNER, widget), checks=self.native(effects.OWNER + "/checks", checks),
                          entries=self.native(effects.EXECUTION_ENDPOINT, entries))
        for kind, path in paths.items():
            raw = effects.EXECUTION_HEADER + "\n" if kind == "execution" else ""
            result["journals"][kind] = self.save({"sourcePath": path, "status": "observed", "observedAt": at, "device": 1,
                "inode": 1 if kind == "execution" else 2, "mode": 0o600, "parentMode": 0o700, "failedMarker": False, "probeExists": False,
                "size": len(raw.encode()), "rawBody": raw, "bytesSha256": hashlib.sha256(raw.encode()).hexdigest()})
        return result

    def transition(self, enabled):
        before, after = dict(self.generation, generation="transition-before"), dict(self.generation, generation="transition-after")
        selected = copy.deepcopy(self.catalog)
        selected["features"][-1]["enabled"] = enabled
        applied = {"reloadedShellCount": 1, "catalog": selected}
        evidence = [self.native("/__qualification/generation", before), self.native("/modularity/features", self.catalog),
                    self.native("/modularity/features/apply", applied, method="POST"), self.native("/__qualification/generation", after)]
        return self.save({"before": before, "after": after, "applied": applied, "evidence": evidence})

    def branch(self, provider, tool, name, removed):
        args, _ = refusal_fixture(provider, True)
        session, command, proposal_id, turn, call = (name + "-" + key for key in ("session", "command", "proposal", "turn", "call"))
        if provider == "anthropic": call = command
        arguments = {"expectedVisible": True} if tool == effects.CHECK_TOOL else {"visible": False}
        code, status = ("weaver.proposal.executor_unavailable", 409) if removed else (effects.POLICY_CODE, 403)
        substitutions = {"command-one": command, "session-one": session, "proposal-one": proposal_id, "turn-one": turn,
                         "native-call-one": call, "continuation-turn": name + "-continuation", effects.CHECK_TOOL: tool, "module.code.unsupported": code}
        def rewrite(value):
            if isinstance(value, dict):
                if value == {"expectedVisible": True}: return arguments
                result = {key: rewrite(item) for key, item in value.items()}
                if result.get("code") == code: result["statusCode"] = status
                return result
            if isinstance(value, list): return [rewrite(item) for item in value]
            if isinstance(value, str):
                if value in substitutions: return substitutions[value]
                if value.startswith("{"): return json.dumps(rewrite(json.loads(value)))
            return value
        trace, _, _, _, _, _, initial, terminal, _, reopened = rewrite(args)
        prepared = copy.deepcopy(check_fixture()[0])
        prepared.update(toolName=tool, sessionId=session, turnId=turn, invocationId=name + "-invocation", toolCallId=call,
                        actorId=OWNER["subject"], tenantId=OWNER["tenantId"], approvalDigest="digest-original", arguments=arguments,
                        requiredPrivilegeClasses=list(effects.TOOLS[tool]), risk="reviewRequired" if tool == "acme_home_activation_visibility" else "admin",
                        expiresAt="2026-11-14T10:05:00Z")
        if tool != effects.CHECK_TOOL:
            prepared["preview"] = {"typeId": "acme.home.widget-visibility", "schemaVersion": "1", "data": {
                "widgetId": effects.WIDGET, "hostId": effects.HOST, "revision": "revision-one", "beforeVisible": True, "visible": False}}
        proposal = {"id": proposal_id, "sessionId": session, "status": "awaitingApproval", "baseRevision": "revision-one", "preparedInvocation": prepared}
        initial[2]["payload"] = copy.deepcopy(proposal)
        session_path, proposal_path = "/_elsa/weaver/sessions/" + session, "/_elsa/weaver/proposals/" + proposal_id
        refs = {"initialStream": self.stream(session, initial), "command": self.save({"commandId": command, "toolName": tool, "arguments": arguments}),
                "recoveredSession": self.http("GET", session_path, {"data": {"sessionId": session, "messages": []}}),
                "recoveredProposals": self.http("GET", session_path + "/proposals", {"data": [dict(proposal, status="approved" if name == "approved" else "awaitingApproval")]}),
                "terminalProposals": self.http("GET", session_path + "/proposals", {"data": [dict(proposal, status="failed")]}),
                "continuation": self.stream(session, terminal), "session": self.http("GET", session_path, {"data": reopened}),
                "trace": self.save({"records": trace, "incompleteTailBytes": 0, "sourcePath": str(self.root / "protocol.jsonl"), "offset": 0}),
                "audit": self.http("GET", "/_elsa/weaver/audit?take=100&sessionId=" + session,
                    {"data": [{"sessionId": session, "kind": "proposalFailed", "metadata": {"proposalId": proposal_id}}]})}
        error = json.loads(reopened["messages"][0]["content"])["error"]
        for action in ("execute", "repeatExecute"):
            refs[action] = self.http("POST", proposal_path + "/execute", {"data": None, "error": error}, status, {"revision": "revision-one"})
        decision = {"data": dict(proposal, status="approved")} if removed or name == "approved" else {"data": None, "error": error}
        refs["approvedBefore" if name == "approved" else "decision"] = self.http("POST", proposal_path + "/approve", decision,
            200 if removed or name == "approved" else status, {"revision": "revision-one", "comment": None})
        return {"sessionId": session, "commandId": command, "proposalId": proposal_id, "prepared": prepared, "refs": refs,
                "times": {"prepared": EPOCH + 10, "recovered": EPOCH + 40, "execute": EPOCH + 60, "repeated": EPOCH + 80}}

    def require(self):
        return effects.require_effect_restart_case(self.value, self.root, source=SOURCE)


class ClassedRecoveryReaderTests(unittest.TestCase):
    def test_fourteen_shapes_keep_pending_and_approved_branches_and_separate_owner_absence(self):
        self.assertEqual(14, len(effects.CLASSED_RESTART_CASES))
        self.assertEqual(30, len(effects.RESTART_REQUIREMENTS))
        for provider in effects.PROVIDERS:
            for tool in effects.TOOLS:
                with tempfile.TemporaryDirectory() as root, self.subTest(provider=provider, tool=tool):
                    RecoveryFixture(root, provider, tool).require()
            with tempfile.TemporaryDirectory() as root, self.subTest(provider=provider, removed=True):
                RecoveryFixture(root, provider, removed=True).require()
        self.assertTrue(effects.restart_manifest_gaps({"status": "passed"}, SOURCE))

    def test_summary_status_source_adapter_digest_deadline_and_branch_mutations_fail_closed(self):
        mutations = {
            "wrong-source": lambda x: x.update(source="b" * 40), "self-promoted": lambda x: x.update(qualified=True),
            "live-provider": lambda x: x.update(liveProvider=True), "wrong-adapter": lambda x: x.update(adapter="anthropic"),
            "missing-approved": lambda x: x["branches"].pop("approved"),
            "changed-digest": lambda x: x["branches"]["pending"]["prepared"].update(approvalDigest="changed"),
            "changed-deadline": lambda x: x["branches"]["approved"]["prepared"].update(expiresAt="2026-11-14T11:05:00Z"),
            "late-recovery": lambda x: x["branches"]["pending"]["times"].update(recovered=EPOCH + 400),
            "foreign-journal-path": lambda x: x["journalPaths"].update(execution="/foreign/entries.jsonl"),
            "stale-snapshot": lambda x: x["snapshots"].update(recovered=x["snapshots"]["before"]),
        }
        for name, change in mutations.items():
            with tempfile.TemporaryDirectory() as root, self.subTest(name=name):
                fixture = RecoveryFixture(root); change(fixture.value)
                with self.assertRaises((RuntimeError, ValueError, KeyError)): fixture.require()

    def test_changed_retained_native_receipts_cannot_be_rehashed_into_success(self):
        mutations = {
            "wrong-policy": ("policyRecovered", lambda x: x.update(status=200, rawBody='{"data":{}}')),
            "same-generation": ("restart", lambda x: x.update(toGeneration=1)),
            "different-binary": ("restart", lambda x: x["startedProcess"].update(assemblySha256="c" * 64)),
            "nonzero-exit": ("restart", lambda x: x["stoppedProcess"].update(exitCode=1)),
        }
        for name, (key, change) in mutations.items():
            with tempfile.TemporaryDirectory() as root, self.subTest(name=name):
                fixture = RecoveryFixture(root)
                fixture.value["refs"][key] = fixture.change(fixture.value["refs"][key], change)
                with self.assertRaises((RuntimeError, ValueError, KeyError)): fixture.require()
        for provider in effects.PROVIDERS:
            for branch in ("pending", "approved"):
                for key in ("trace", "continuation", "session", "recoveredProposals", "terminalProposals", "execute", "repeatExecute"):
                    with tempfile.TemporaryDirectory() as root, self.subTest(provider=provider, branch=branch, ref=key):
                        fixture = RecoveryFixture(root, provider)
                        refs = fixture.value["branches"][branch]["refs"]
                        refs[key] = fixture.change(refs[key], lambda x: x.clear())
                        with self.assertRaises((RuntimeError, ValueError, KeyError, TypeError)): fixture.require()

    def test_missing_native_arming_changed_owner_and_historical_adapter_results_fail(self):
        for label, key in (("before", "entries"), ("after", "widget"), ("after", "layout"), ("repeated", "checks")):
            with tempfile.TemporaryDirectory() as root, self.subTest(snapshot=label, kind=key):
                fixture = RecoveryFixture(root)
                snapshot = fixture.value["snapshots"][label]
                wire = json.loads(Path(snapshot[key]["path"]).read_text())
                def change(body):
                    if key == "widget": body["visible"] = False
                    elif key == "layout": body["value"]["unrelated"] = []
                    elif key == "checks": body["ranCount"] = 1
                    else: body.clear()
                wire["body"] = fixture.change(wire["body"], change)
                snapshot[key] = fixture.save(wire)
                with self.assertRaises((RuntimeError, ValueError, KeyError)): fixture.require()
        for provider in effects.PROVIDERS:
            for mutation in ("duplicate", "historical", "incomplete"):
                with tempfile.TemporaryDirectory() as root, self.subTest(provider=provider, mutation=mutation):
                    fixture = RecoveryFixture(root, provider)
                    refs = fixture.value["branches"]["pending"]["refs"]
                    def change(trace):
                        if mutation == "duplicate": trace["records"].append(copy.deepcopy(trace["records"][-1]))
                        elif mutation == "historical": trace["records"][-1]["data"]["commandId"] = "old-command"
                        else: trace["incompleteTailBytes"] = 1
                    refs["trace"] = fixture.change(refs["trace"], change)
                    with self.assertRaises(RuntimeError): fixture.require()

    def test_group_rechecks_immutable_records_and_never_accepts_a_fourteen_case_summary(self):
        with tempfile.TemporaryDirectory() as root:
            records = [{"id": name, "status": "passed"} for name in effects.CLASSED_RESTART_CASES]
            with self.assertRaises(RuntimeError): effects.require_classed_effect_restarts(records, root, source=SOURCE)
            records[0]["id"] = records[1]["id"]
            with self.assertRaises(RuntimeError): effects.require_classed_effect_restarts(records, root, source=SOURCE)
        with tempfile.TemporaryDirectory() as root:
            fixture = RecoveryFixture(root)
            with self.assertRaises(RuntimeError):
                effects.require_effect_restart_case(fixture.value, root, source=SOURCE, bindings={"forged-driver": "b" * 64})

    def test_unarmed_poisoned_replaced_and_one_entry_despite_unchanged_resource_never_prove_zero(self):
        def one_entry(value):
            row = {"Actor": OWNER["subject"], "Tenant": OWNER["tenantId"], "Host": effects.HOST, "RequestedActor": OWNER["subject"],
                   "RequestedTenant": OWNER["tenantId"], "ToolName": effects.CHECK_TOOL, "RequestedToolName": effects.CHECK_TOOL,
                   "SessionId": "pending-session", "TurnId": "pending-turn", "InvocationId": "pending-invocation", "ToolCallId": "pending-call", "EntryNumber": 1}
            raw = effects.EXECUTION_HEADER + "\n" + json.dumps(row) + "\n"
            value.update(rawBody=raw, size=len(raw.encode()), bytesSha256=hashlib.sha256(raw.encode()).hexdigest())
        mutations = {
            "unknown": lambda x: x.update(status="unknown"), "unwritable": lambda x: x.update(mode=0o400),
            "public": lambda x: x.update(parentMode=0o755), "failed-marker": lambda x: x.update(failedMarker=True),
            "probe-left": lambda x: x.update(probeExists=True), "replacement": lambda x: x.update(inode=17),
            "empty": lambda x: x.update(rawBody="", size=0, bytesSha256=hashlib.sha256(b"").hexdigest()),
            "wrong-header": lambda x: x.update(rawBody=effects.EXECUTION_HEADER.replace("2", "1")),
            "forged-shape": lambda x: x.update(rawBody=effects.EXECUTION_HEADER + '\n{"EntryNumber":1}\n'),
            "one-real-entry-with-no-business-run": one_entry,
        }
        for name, change in mutations.items():
            with tempfile.TemporaryDirectory() as root, self.subTest(name=name):
                fixture = RecoveryFixture(root)
                journals = fixture.value["snapshots"]["recovered"]["journals"]
                journals["execution"] = fixture.change(journals["execution"], change)
                with self.assertRaises((RuntimeError, ValueError, KeyError)): fixture.require()

    def test_removed_feature_only_wrong_cause_failed_reconcile_and_restored_archive_are_rejected(self):
        for key in ("removed", "removedRecovered", "generationRemoved", "disableTransition", "restoreTransition", "removeReconcile", "restoreReconcile"):
            with tempfile.TemporaryDirectory() as root, self.subTest(ref=key):
                fixture = RecoveryFixture(root, removed=True)
                fixture.value["refs"][key] = fixture.value["refs"]["installed"]
                with self.assertRaises((RuntimeError, ValueError, KeyError, TypeError)): fixture.require()
        with tempfile.TemporaryDirectory() as root:
            fixture = RecoveryFixture(root, removed=True)
            Path(fixture.value["archiveDelta"]["added"][0]["path"]).write_bytes(b"different archive")
            with self.assertRaises(RuntimeError): fixture.require()
        for removed, wrong in ((True, "module.owner.unavailable"), (False, "weaver.proposal.expired")):
            with tempfile.TemporaryDirectory() as root:
                fixture = RecoveryFixture(root, removed=removed)
                ref = fixture.value["branches"]["pending"]["refs"]["execute"]
                def change(value):
                    wire = json.loads(value["rawBody"]); wire["error"]["code"] = wrong
                    value["rawBody"] = json.dumps(wire); value["rawBodySha256"] = hashlib.sha256(value["rawBody"].encode()).hexdigest()
                fixture.value["branches"]["pending"]["refs"]["execute"] = fixture.change(ref, change)
                with self.assertRaises(RuntimeError): fixture.require()


class ClassedRecoveryDriverTests(unittest.TestCase):
    def test_unknown_or_poisoned_storage_is_retained_externally_and_never_recreated_or_cleared(self):
        for poisoned in (False, True):
            with tempfile.TemporaryDirectory() as directory, self.subTest(poisoned=poisoned):
                root = Path(directory)
                journal = root / "execution.jsonl"
                marker = root / "execution.jsonl.failed"
                if poisoned:
                    journal.write_text(effects.EXECUTION_HEADER + "\n"); journal.chmod(0o600)
                    marker.write_text("failure remains"); marker.chmod(0o600)
                def output(name):
                    path = root / name; path.mkdir(mode=0o700)
                    return path
                def save(path, name, value):
                    target = path / (name + ".json"); target.write_text(json.dumps(value))
                    return retained(target)
                runtime = SimpleNamespace(run=SimpleNamespace(root=root), active_identity=OWNER, output=output, save=save)
                with self.assertRaises(restarts.RestartCaseFailure) as failure:
                    restarts._effect_journal_snapshot(runtime, {"execution": str(journal)}, "unknown")
                evidence = failure.exception.observation["failure"]
                observed = json.loads(Path(evidence["path"]).read_text())
                self.assertTrue(observed["requiresFreshObservationPath"])
                self.assertEqual(poisoned, journal.exists())
                self.assertEqual(poisoned, marker.exists())

    def test_wrong_first_execute_is_never_continued_or_retried_after_restoration(self):
        for removed in (False, True):
            t = Mock()
            turn = SimpleNamespace(command_id="command", session="session", proposal={"id": "proposal", "baseRevision": "revision"})
            branch = {"prepared": {"toolName": effects.CHECK_TOOL, "arguments": {}}, "refs": {}, "times": {}}
            runtime = SimpleNamespace(run=SimpleNamespace(root=Path("/owned")), backend_origin=ORIGIN)
            with patch.object(effects, "_restart_failure", side_effect=RuntimeError("wrong cause")):
                with self.assertRaises(RuntimeError): restarts._effect_restart_refuse(runtime, turn, branch, t, "approved", removed=removed)
            t.execute.assert_called_once_with("proposal", revision="revision")
            t.collect_turn.assert_not_called()

    def test_setup_failure_returns_all_fourteen_failed_and_does_not_attempt_a_tool_or_restart(self):
        with tempfile.TemporaryDirectory() as root:
            runtime = SimpleNamespace(run=SimpleNamespace(root=Path(root), source=SOURCE, report={"independentPackageWitness": False}),
                provider="copilot", backend_origin=ORIGIN, backend=object(), witness=json.loads((effects.TOOL / "witness.json").read_text()), native=Mock())
            runtime.native.json.side_effect = RuntimeError("durable composition absent")
            runtime.output = lambda name: Path(root) / name
            runtime.save = lambda output, name, value: {"path": str(output / name), "sha256": "b" * 64}
            host = SimpleNamespace(current=object(), restart=Mock())
            records = restarts.qualify_classed_effect_restarts(runtime, host)
            self.assertEqual(list(effects.CLASSED_RESTART_CASES), [row["id"] for row in records])
            self.assertTrue(all(row["status"] == "failed" and row["attempted"] is False for row in records))
            host.restart.assert_not_called()


if __name__ == "__main__":
    unittest.main()
