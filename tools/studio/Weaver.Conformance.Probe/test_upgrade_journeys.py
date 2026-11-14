"""Authored T038 reader controls, all UNRUN under the source-only execution hold.

Disposable native-shaped receipts exercise refusal/custody boundaries. These
controls never build/start a host, write a real Weaver database or qualify a
native old writer. No synthetic record is a package-witness substitute.
"""
import copy
import hashlib
import json
from pathlib import Path
import tempfile
import unittest

from case_records import retained
from consumers import DURABLE_EFFECTS_FEATURE, DURABLE_STORE_FEATURE
from effects_journeys import HOST, RESTART_REQUIREMENTS
from runtime import private_json
import upgrade_journeys as upgrade

ORIGIN = "http://127.0.0.1:49101"
OWNER = {"subject": "native-owner", "tenantId": "native-tenant"}


def journal(rows=()):
    raw = upgrade.JOURNAL_HEADER + "\n" + "".join(json.dumps(row) + "\n" for row in rows)
    return {"status": "observed", "sourcePath": "/owned/observation.jsonl", "device": 10, "inode": 20,
            "mode": 0o600, "parentMode": 0o700, "observedAt": 10.0, "failedMarker": False, "probeExists": False,
            "rawBody": raw, "size": len(raw.encode()), "bytesSha256": hashlib.sha256(raw.encode()).hexdigest()}


def entry(**changes):
    return {"Actor": OWNER["subject"], "Tenant": OWNER["tenantId"], "Host": HOST, "RequestedActor": OWNER["subject"],
            "RequestedTenant": OWNER["tenantId"], "ToolName": upgrade.RESOURCE_TOOLS["write"], "RequestedToolName": upgrade.RESOURCE_TOOLS["write"],
            "SessionId": "actual-session", "TurnId": "actual-turn", "InvocationId": "actual-invocation", "ToolCallId": "actual-call",
            "EntryNumber": 1, **changes}


class JournalControls(unittest.TestCase):
    def read(self, value):
        return upgrade._journal(value, "/owned/observation.jsonl", OWNER)

    def test_armed_empty_header_establishes_zero_and_real_entry_is_correlated(self):
        self.assertEqual([], self.read(journal()))
        actual = entry()
        self.assertEqual([actual], self.read(journal([actual])))

    def test_failed_unknown_or_poisoned_observer_never_becomes_zero(self):
        for changes in ({"status": "unknown"}, {"failedMarker": True}, {"probeExists": True}, {"mode": 0o644},
                        {"parentMode": 0o755}, {"sourcePath": "/foreign/journal"}, {"device": True}, {"observedAt": float("nan")}):
            with self.subTest(changes=changes), self.assertRaises(RuntimeError): self.read({**journal(), **changes})

    def test_plain_summary_or_changed_raw_bytes_cannot_replace_observed_journal(self):
        for value in ({"entryCount": 0}, {**journal(), "rawBody": ""}, {**journal(), "bytesSha256": "0" * 64},
                      {**journal(), "size": 0}):
            with self.subTest(value=value), self.assertRaises(RuntimeError): self.read(value)

    def test_wrong_tool_requested_owner_or_sequence_is_refused(self):
        for changes in ({"RequestedActor": "other"}, {"RequestedTenant": "other"}, {"RequestedToolName": upgrade.RESOURCE_TOOLS["read"]},
                        {"EntryNumber": 2}, {"ToolName": "acme_home_check_widget"}, {"SessionId": ""}):
            with self.subTest(changes=changes), self.assertRaises(RuntimeError): self.read(journal([entry(**changes)]))

    def test_other_native_owner_entries_do_not_count_for_this_owner(self):
        other = entry(Actor="other", RequestedActor="other")
        self.assertEqual([], self.read(journal([other])))
        self.assertEqual([entry()], self.read(journal([other, entry()])))

    def test_duplicate_native_invocation_entry_cannot_hide_by_deduplicating_rows(self):
        rows = [entry(), entry(EntryNumber=2)]
        self.assertEqual(2, len(self.read(journal(rows))))


class ReceiptControls(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.sequence = 0

    def save(self, value):
        self.sequence += 1
        path = self.root / (str(self.sequence) + ".json")
        private_json(path, value)
        return retained(path)

    def native(self, path, body, *, origin=ORIGIN, method="GET", status=200):
        return self.save({"origin": origin, "path": path, "method": method, "status": status, "body": self.save(body)})

    def http(self, path, value):
        raw = json.dumps(value)
        return self.save({"origin": ORIGIN, "path": path, "method": "GET", "status": 200, "rawBody": raw,
                          "rawBodySha256": hashlib.sha256(raw.encode()).hexdigest()})

    def stream(self, session, frames):
        raw = "".join("data: " + json.dumps(frame) + "\n\n" for frame in frames)
        return self.save({"origin": ORIGIN, "method": "GET", "path": "/_elsa/weaver/sessions/" + session + "/stream", "status": 200,
                          "headers": {"content-type": "text/event-stream"}, "rawBody": raw, "frames": [{"value": frame} for frame in frames]})

    def initial(self, *, classes=None, digest=None, expiry=None, preview_visible=True, terminal="toolApprovalRequested"):
        session = "real-shape-session"
        frozen_expiry = "2026-11-14T10:05:00Z"
        native_owner = {"widgetId": "acme.home:status", "hostId": HOST, "revision": "native-reviewed-revision", "visible": False}
        prepared = {"invocationId": "invocation", "toolCallId": "native-call", "turnId": "native-turn", "sessionId": session,
            "toolName": upgrade.RESOURCE_TOOLS["write"], "moduleId": upgrade.RESOURCE_MODULE, "toolContractVersion": "1",
            "executionLocation": "server", "client": None, "requiresApproval": True, "risk": "reviewRequired",
            "actorId": OWNER["subject"], "tenantId": OWNER["tenantId"], "requiredPrivilegeClasses": classes,
            "approvalDigest": digest or "d" * 64, "expiresAt": expiry or frozen_expiry,
            "target": {"moduleId": upgrade.RESOURCE_MODULE, "resourceType": "home-widget", "resourceId": "acme.home:status", "revision": native_owner["revision"]},
            "arguments": {"visible": True}, "disclosureSourceId": "acme.upgrade.widget-context",
            "preview": {"typeId": "acme.upgrade.widget-result", "schemaVersion": "1", "data": {
                **native_owner, "beforeVisible": False, "visible": preview_visible}}}
        proposal = {"id": "proposal", "sessionId": session, "status": "awaitingApproval", "baseRevision": native_owner["revision"], "preparedInvocation": prepared}
        frames = [{"id": "1", "kind": "started", "payload": {"turnId": prepared["turnId"]}},
                  {"id": "2", "kind": "toolCallStarted", "payload": {"toolCallId": prepared["toolCallId"], "toolName": prepared["toolName"]}},
                  {"id": "3", "kind": "proposalCreated", "payload": proposal}, {"id": "4", "kind": terminal, "payload": {}}]
        before = {"owner": self.native(upgrade.RESOURCE_OWNER, native_owner),
                  "layout": self.native(upgrade.HOME_PREFERENCES_PATH, {"revision": native_owner["revision"], "value": {"widgets": [{"id": "acme.home:status", "visible": False}]}}),
                  "entries": self.native(upgrade.RESOURCE_OWNER + "/execution-entries", {"entries": []}), "journal": self.save(journal())}
        inspector = {"actorId": OWNER["subject"], "tenantId": OWNER["tenantId"], "sessionId": session,
                     "legacyDefaults": {"preparationsHaveNoClasses": True}, "proposals": [{"id": "proposal", "status": "AwaitingApproval", "approvalDigest": "d" * 64, "expiresAt": frozen_expiry}]}
        branch = {"sessionId": session, "proposalId": "proposal", "commandId": "command", "prepared": prepared, "approved": False,
                  "snapshots": {"before": before}, "refs": {"initialStream": self.stream(session, frames),
                  "identityBefore": self.native("/_elsa/identity/session", {"status": "authenticated", **OWNER}),
                  "command": self.save({"commandId": "command", "toolName": prepared["toolName"], "arguments": prepared["arguments"]}),
                  "oldInspector": self.native(upgrade.INSPECTOR + session, inspector)}}
        return {"backendOrigin": ORIGIN, "store": {"executionObservationFile": "/owned/observation.jsonl"}}, branch

    def test_legacy_preparation_uses_native_preview_and_original_digest_deadline(self):
        row, branch = self.initial()
        self.assertEqual("d" * 64, upgrade._read_initial(self.root, row, branch)[2]["approvalDigest"])

    def test_rehashing_or_extending_an_old_preparation_is_refused(self):
        for arguments in ({"digest": "e" * 64}, {"expiry": "2026-11-14T10:10:00Z"}):
            row, branch = self.initial(**arguments)
            with self.subTest(arguments=arguments), self.assertRaises(RuntimeError): upgrade._read_initial(self.root, row, branch)

    def test_classed_data_cannot_be_fabricated_as_a_pre_upgrade_preparation(self):
        row, branch = self.initial(classes=["administrative"])
        with self.assertRaises(RuntimeError): upgrade._read_initial(self.root, row, branch)

    def test_foreign_preview_or_aborted_initial_turn_cannot_pass_as_pending(self):
        for arguments in ({"preview_visible": False}, {"terminal": "error"}):
            row, branch = self.initial(**arguments)
            with self.subTest(arguments=arguments), self.assertRaises(RuntimeError): upgrade._read_initial(self.root, row, branch)

    def availability(self, held):
        features = [{"id": name, "enabled": True, "runtimeAvailable": True, "availability": {"status": "available", "reasons": []}}
                    for name in ("Weaver", "WeaverApi", DURABLE_STORE_FEATURE, upgrade.RESOURCE_FEATURE, DURABLE_EFFECTS_FEATURE)]
        if held:
            features[-1]["availability"] = {"status": "dormant", "reasons": [{"family": "Weaver", "version": "1.1.0", "kind": "Held"}]}
        item = {"id": "feature-dormant:" + DURABLE_EFFECTS_FEATURE}
        attention = {"contributors": [{"contributorId": "modularity", "status": "ready", "isTruncated": False, "items": [item] if held else []}]}
        capabilities = {"capabilities": [{"id": "elsa.api.weaver", "contractVersion": "1"},
            {"id": "elsa.api.weaver.effects", "contractVersion": "1", **({"status": "dormant", "reason": "Weaver1.1 is not finalized"} if held else {})}]}
        row = {"backendOrigin": ORIGIN, "observations": {"catalog": self.native("/modularity/features", {"features": features}),
            "attention": self.native(upgrade.ATTENTION, attention), "capabilities": self.native("/capabilities", capabilities),
            "generation": self.native("/__qualification/generation", {"generation": "same-current-process-generation"})}}
        return row, features, attention, capabilities

    def test_catalog_attention_and_real_capability_must_agree_both_before_and_after_release(self):
        for held in (True, False):
            row, *_ = self.availability(held)
            self.assertEqual("same-current-process-generation", upgrade._read_availability(self.root, row, held=held)["generation"])

    def test_missing_effects_capability_cannot_pass_on_legacy_capability_alone(self):
        row, _, _, capabilities = self.availability(True)
        capabilities["capabilities"].pop()
        row["observations"]["capabilities"] = self.native("/capabilities", capabilities)
        with self.assertRaises(RuntimeError): upgrade._read_availability(self.root, row, held=True)

    def test_catalog_dormancy_cannot_be_replaced_by_available_capability_or_wrong_version(self):
        for changes in ({"status": None}, {"contractVersion": "2"}, {"reason": ""}):
            row, _, _, capabilities = self.availability(True)
            capabilities["capabilities"][-1].update(changes)
            row["observations"]["capabilities"] = self.native("/capabilities", capabilities)
            with self.subTest(changes=changes), self.assertRaises(RuntimeError): upgrade._read_availability(self.root, row, held=True)

    def test_unavailable_or_truncated_attention_does_not_prove_absence(self):
        for changes in ({"status": "unavailable"}, {"isTruncated": True}, {"contributorId": "unrelated"}):
            row, _, attention, _ = self.availability(False)
            attention["contributors"][0].update(changes)
            row["observations"]["attention"] = self.native(upgrade.ATTENTION, attention)
            with self.subTest(changes=changes), self.assertRaises(RuntimeError): upgrade._read_availability(self.root, row, held=False)

    def test_legacy_weaver_api_must_stay_available_while_effects_dormant(self):
        row, features, _, _ = self.availability(True)
        features[1]["availability"]["status"] = "dormant"
        row["observations"]["catalog"] = self.native("/modularity/features", {"features": features})
        with self.assertRaises(RuntimeError): upgrade._read_availability(self.root, row, held=True)

    def test_wrong_family_or_version_cannot_satisfy_effects_requirement(self):
        for changes in ({"family": "Agent"}, {"version": "1.0.0"}):
            row, features, _, _ = self.availability(True)
            features[-1]["availability"]["reasons"][0].update(changes)
            row["observations"]["catalog"] = self.native("/modularity/features", {"features": features})
            with self.subTest(changes=changes), self.assertRaises(RuntimeError): upgrade._read_availability(self.root, row, held=True)

    def test_foreign_http_origin_wrong_endpoint_or_changed_body_hash_is_refused(self):
        for changes in ({"origin": "http://127.0.0.1:49102"}, {"path": "/other"}, {"status": 503}):
            row, features, _, _ = self.availability(True)
            receipt = {"origin": ORIGIN, "path": "/modularity/features", "method": "GET", "status": 200,
                       "body": self.save({"features": features}), **changes}
            row["observations"]["catalog"] = self.save(receipt)
            with self.subTest(changes=changes), self.assertRaises(RuntimeError): upgrade._read_availability(self.root, row, held=True)
        row, *_ = self.availability(True)
        path = Path(row["observations"]["catalog"]["path"])
        path.write_text("{}")
        with self.assertRaises(RuntimeError): upgrade._read_availability(self.root, row, held=True)

    def restart(self):
        old = {"pid": 11, "publishedDirectory": "/old", "origin": ORIGIN}
        new = {"pid": 12, "publishedDirectory": "/current", "origin": ORIGIN}
        receipt = {"name": "backend", "kind": "graceful", "origin": ORIGIN, "fromGeneration": 1, "toGeneration": 2,
                   "stoppedAt": 10, "startedAt": 20, "readyAt": 30, "stoppedProcess": copy.deepcopy(old), "startedProcess": copy.deepcopy(new)}
        return receipt, {11: old, 12: new}

    def test_restart_must_join_exact_actual_retained_processes(self):
        receipt, processes = self.restart()
        self.assertEqual(receipt, upgrade._read_restart(self.root, self.save(receipt), {"origin": ORIGIN}, processes))
        for changes in ({"pid": 13}, {"publishedDirectory": "/different"}, {"origin": "http://127.0.0.1:49102"}):
            changed = copy.deepcopy(receipt)
            changed["startedProcess"].update(changes)
            with self.subTest(changes=changes), self.assertRaises(RuntimeError):
                upgrade._read_restart(self.root, self.save(changed), {"origin": ORIGIN}, processes)

    def test_same_generation_abrupt_or_reversed_time_is_not_a_graceful_native_upgrade(self):
        receipt, processes = self.restart()
        for changes in ({"toGeneration": 1}, {"kind": "abrupt"}, {"startedAt": 5}, {"readyAt": 15}):
            with self.subTest(changes=changes), self.assertRaises(RuntimeError):
                upgrade._read_restart(self.root, self.save({**receipt, **changes}), {"origin": ORIGIN}, processes)

    def test_operator_cannot_substitute_another_generation_even_with_valid_claimed_result(self):
        row = {"generations": {"old": {"operator": {"source": "actual-old-inputs"}}}, "store": {"database": str(self.root / "state/weaver.db")}}
        ref = self.save({"operator": {"source": "different-current-inputs"}, "exitCode": 0})
        with self.assertRaises(RuntimeError): upgrade._bound_operator(self.root, row, ref, "old", "hold")

    def preparation(self, held):
        request = {"sessionId": "owned-session", "expectedVisible": False}
        response = {"evidenceKind": "native-module-public-invoker-preparation", "sessionId": request["sessionId"],
                    "toolName": "acme_home_check_widget", "turnId": "owned-module-turn", "toolCallId": "owned-module-call",
                    "outcome": "Denied" if held else "ProposalCreated", "result": None,
                    "error": {"code": "schema-version-not-finalized", "statusCode": 409, "ownerUnavailable": False} if held else None,
                    "proposal": None if held else {"id": "owned-proposal", "status": "AwaitingApproval", "requiresApproval": True}}
        prepared = {"sessionId": request["sessionId"], "toolName": response["toolName"], "turnId": response["turnId"],
                    "toolCallId": response["toolCallId"], "actorId": OWNER["subject"], "tenantId": OWNER["tenantId"],
                    "requiredPrivilegeClasses": ["administrative"], "requiresApproval": True, "arguments": {"expectedVisible": False}}
        rows = [] if held else [{"id": "owned-proposal", "status": "awaitingApproval", "preparedInvocation": prepared}]
        row = {"backendOrigin": ORIGIN, "preparation": {"request": self.save(request),
               "response": self.native(upgrade.PREPARATION_ENDPOINT, response, method="POST", status=409 if held else 200),
               "proposals": self.http("/_elsa/weaver/sessions/owned-session/proposals", {"data": rows})}}
        return row, response, rows

    def test_real_public_invoker_preparation_boundary_is_required_beside_discovery_proof(self):
        for held in (True, False):
            row, response, _ = self.preparation(held)
            self.assertEqual(response, upgrade._read_preparation_probe(self.root, row, OWNER, held=held))

    def test_held_preparation_must_name_exact_schema_cause_and_have_no_proposal(self):
        for mutate in (lambda body: body["error"].update(code="weaver.tool.not_found"),
                       lambda body: body.update(proposal={"id": "unexpected"}), lambda body: body.update(outcome="ProposalCreated")):
            row, response, _ = self.preparation(True)
            mutate(response)
            row["preparation"]["response"] = self.native(upgrade.PREPARATION_ENDPOINT, response, method="POST", status=409)
            with self.assertRaises(RuntimeError): upgrade._read_preparation_probe(self.root, row, OWNER, held=True)

    def test_released_preparation_must_be_unapproved_current_native_owned_and_classed(self):
        for changes in ({"actorId": "other"}, {"requiredPrivilegeClasses": None}, {"turnId": "substituted"}, {"requiresApproval": False}):
            row, _, rows = self.preparation(False)
            rows[0]["preparedInvocation"].update(changes)
            row["preparation"]["proposals"] = self.http("/_elsa/weaver/sessions/owned-session/proposals", {"data": rows})
            with self.subTest(changes=changes), self.assertRaises(RuntimeError): upgrade._read_preparation_probe(self.root, row, OWNER, held=False)

    def protocol(self, at):
        content = self.root / "content"
        protocol = content / "protocol"
        protocol.mkdir(parents=True)
        source, window = protocol / "copilot.jsonl", self.root / "retained-window.jsonl"
        events = [{"evidenceKind": upgrade.EVIDENCE_CATEGORY, "kind": "copilot.call", "at": at,
                   "data": {"commandId": "command", "toolName": upgrade.RESOURCE_TOOLS["write"]}}]
        raw = (json.dumps(events[0]) + "\n").encode()
        source.write_bytes(raw); window.write_bytes(raw)
        metadata = {"sourcePath": str(source), "offset": 0, "nextOffset": len(raw), "incompleteTailBytes": 0,
                    "observedBytesSha256": hashlib.sha256(raw).hexdigest(), "records": events}
        actual = source.stat()
        trace = self.save({**metadata, "commandId": "command", "rawTrace": self.save(metadata), "window": retained(window),
            "capturedAt": 50, "sourceSizeAtObservation": len(raw), "sourceIdentity": {"device": actual.st_dev, "inode": actual.st_ino}})
        old = {"pid": 1, "publishedDirectory": str(self.root / "old"), "startedAt": 0, "stoppedAt": 20}
        current = {"pid": 2, "publishedDirectory": str(self.root / "current"), "startedAt": 21, "stoppedAt": 40}
        row = {"provider": "copilot", "store": {"contentRoot": str(content)}, "refs": {
            "laneCloseout": self.save({"processes": [self.save(old), self.save(current)]})}, "generations": {
            "old": {"consumerBinaryProvenance": {"deps": {"path": str(self.root / "old/Backend.deps.json")}}}}}
        return row, trace, source

    def test_actual_protocol_bytes_must_belong_to_the_old_writer_lifetime(self):
        row, trace, _ = self.protocol("1970-01-01T00:00:10Z")
        self.assertEqual(1, len(upgrade._bind_protocol(self.root, row, trace, "command", old_call=True)))
        with self.assertRaises(RuntimeError): upgrade._bind_protocol(self.root, row, trace, "command", old_call=False)

    def test_current_or_between_process_events_cannot_be_presented_as_old_writer_calls(self):
        for at in ("1970-01-01T00:00:20.5Z", "1970-01-01T00:00:30Z"):
            with self.subTest(at=at):
                temporary = tempfile.TemporaryDirectory()
                self.addCleanup(temporary.cleanup)
                self.root = Path(temporary.name).resolve()
                row, trace, _ = self.protocol(at)
                with self.assertRaises(RuntimeError): upgrade._bind_protocol(self.root, row, trace, "command", old_call=True)

    def test_replacing_raw_protocol_source_cannot_pass_with_unchanged_parsed_summary(self):
        row, trace, source = self.protocol("1970-01-01T00:00:10Z")
        source.write_bytes(source.read_bytes().replace(b"copilot.call", b"copilot.fake"))
        with self.assertRaises(RuntimeError): upgrade._bind_protocol(self.root, row, trace, "command", old_call=True)

    def test_manifest_summary_cannot_promote_or_replace_sixteen_full_native_records(self):
        self.assertEqual(16, len(upgrade.LEGACY_RESTART_CASES))
        self.assertTrue(set(upgrade.LEGACY_RESTART_CASES).issubset(RESTART_REQUIREMENTS))
        for rows in ([], [{"id": name, "status": "passed"} for name in upgrade.LEGACY_RESTART_CASES],
                     [{"id": name} for name in reversed(upgrade.LEGACY_RESTART_CASES)]):
            with self.subTest(rows=len(rows)), self.assertRaises(RuntimeError):
                upgrade.require_legacy_effect_restarts(rows, self.root, source="a" * 40)


if __name__ == "__main__":
    unittest.main()
