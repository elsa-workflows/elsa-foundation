"""Synthetic controls for the read-only store observer; never native writer/upgrade qualification."""
import copy
import hashlib
import json
from pathlib import Path
import secrets
import sqlite3
import tempfile
import unittest

from upgrade_store import require_identity_rows, require_retained_counter, snapshot_store


def digest(value):
    return hashlib.sha256(value.encode()).hexdigest()


class UpgradeStoreTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.database = self.root / "real-shape-unit-control.db"
        self.owner_marker = secrets.token_hex(24)
        self.canonical = digest("unit canonical text")
        self.approval = digest("original unit preparation")
        # Independent pinned schema shape: these SQL writes are test inputs, not an old-writer producer.
        with sqlite3.connect(self.database) as connection:
            connection.executescript('''
                CREATE TABLE elsa_weaver_sessions(SessionId TEXT,ActorId TEXT,TenantId TEXT,SchemaVersion TEXT,Payload TEXT,PausedTurnPayload TEXT,PendingAuditsPayload TEXT,OwnerToken TEXT);
                CREATE TABLE elsa_weaver_messages(SessionId TEXT,MessageId TEXT,SchemaVersion TEXT,Payload TEXT);
                CREATE TABLE elsa_weaver_disclosure_snapshots(SessionId TEXT,Digest TEXT,Size INTEGER,SchemaVersion TEXT,Canonical TEXT);
                CREATE TABLE elsa_weaver_context(SessionId TEXT,AttachmentKey TEXT,SchemaVersion TEXT,Payload TEXT);
                CREATE TABLE elsa_weaver_proposals(SessionId TEXT,ProposalId TEXT,SchemaVersion TEXT,Payload TEXT);
                CREATE TABLE elsa_weaver_call_keys(SessionId TEXT,Kind TEXT,KeyHash TEXT,SchemaVersion TEXT,Value TEXT);
                CREATE TABLE elsa_weaver_audit(SessionId TEXT,Sequence INTEGER,EventId TEXT,ActorId TEXT,SchemaVersion TEXT,Payload TEXT);
                CREATE TABLE elsa_weaver_audit_owners(SessionId TEXT,ActorId TEXT,TenantId TEXT,SchemaVersion TEXT);
                CREATE TABLE elsa_weaver_audit_sequence(Id INTEGER,Last INTEGER,SchemaVersion TEXT);
            ''')
            connection.execute("INSERT INTO elsa_weaver_sessions VALUES (?,?,?,?,?,?,?,?)",
                               ("own", "actor", "tenant", "1.0.0", "opaque-session", "opaque-paused", "opaque-owed", self.owner_marker))
            connection.execute("INSERT INTO elsa_weaver_sessions VALUES (?,?,?,?,?,?,?,?)",
                               ("foreign", "someone-else", "another-tenant", "1.0.0", "foreign", None, None, "not-selected"))
            connection.execute("INSERT INTO elsa_weaver_messages VALUES (?,?,?,?)", ("own", "message", "1.0.0", "opaque-message"))
            connection.execute("INSERT INTO elsa_weaver_disclosure_snapshots VALUES (?,?,?,?,?)", ("own", self.canonical, 19, "1.0.0", "opaque-canonical"))
            connection.execute("INSERT INTO elsa_weaver_context VALUES (?,?,?,?)", ("own", "attachment-key", "1.0.0", "opaque-context"))
            connection.execute("INSERT INTO elsa_weaver_proposals VALUES (?,?,?,?)", ("own", "proposal", "1.0.0", "opaque-proposal"))
            connection.execute("INSERT INTO elsa_weaver_call_keys VALUES (?,?,?,?,?)", ("own", "call", "call-key-hash", "1.0.0", self.owner_marker))
            connection.execute("INSERT INTO elsa_weaver_audit VALUES (?,?,?,?,?,?)", ("own", 1, "audit", "actor", "1.0.0", "opaque-audit"))
            connection.execute("INSERT INTO elsa_weaver_audit_owners VALUES (?,?,?,?)", ("own", "actor", "tenant", "1.0.0"))
            connection.execute("INSERT INTO elsa_weaver_audit_sequence VALUES (?,?,?)", (1, 1, "1.0.0"))
        self.before = snapshot_store(self.root, self.database, ["own"])
        self.after = copy.deepcopy(self.before)
        self.after["observedAt"] += 1
        for role, rows in self.after["tables"].items():
            if role != "counter":
                for row in rows: row["SchemaVersion"] = "1.1.0"
        self.decoded = {
            "evidenceKind": "native-real-store-decoding-without-engine-hydration", "sessionId": "own", "actorId": "actor", "tenantId": "tenant",
            "counts": {"messages": 1, "snapshots": 1, "context": 1, "proposals": 1, "keys": 1, "pendingAudits": 1},
            "content": {name: digest(name) for name in ("session", "messages", "snapshots", "context", "pausedTurn", "proposals", "keys", "pendingAudits")},
            "snapshots": [{"digest": self.canonical, "size": 19, "decodedCanonicalSha256": self.canonical}],
            "proposals": [{"id": "proposal", "status": "AwaitingApproval", "toolName": "unit_tool", "moduleId": "unit.module",
                           "approvalDigest": self.approval, "expiresAt": "2026-11-14T08:00:00+00:00", "preparedContentSha256": digest("frozen contents")}],
            "legacyDefaults": {"preparationsHaveNoClasses": True, "resultsHaveNoTypedErrorsOrModuleCodes": True, "noPerformedStatus": True}}

    def verify(self, before=None, after=None, decoded_before=None, decoded_after=None):
        return require_identity_rows(self.before if before is None else before, self.after if after is None else after,
                                     self.decoded if decoded_before is None else decoded_before, self.decoded if decoded_after is None else decoded_after,
                                     actor_id="actor", tenant_id="tenant")

    def test_complete_shapes_pass_without_claiming_native_qualification(self):
        observed = self.verify()
        self.assertEqual((9, 7, 1, "1.0.0"), (observed["rowKinds"], observed["chainProtectedColumns"], observed["canonicalProtectedColumn"], observed["counterStamp"]))
        self.assertNotIn("qualified", observed)

    def test_observer_is_read_only_owner_bounded_and_does_not_emit_lease_or_key_values(self):
        original = self.database.read_bytes()
        result = snapshot_store(self.root, self.database, ["own"])
        self.assertEqual(original, self.database.read_bytes())
        self.assertNotIn(self.owner_marker, json.dumps(result))
        self.assertNotIn("OwnerToken", result["tables"]["sessions"][0])
        self.assertNotIn("Value", result["tables"]["keys"][0])
        self.assertEqual(digest(self.owner_marker), result["tables"]["keys"][0]["ValueSha256"])
        self.assertEqual(["own"], [row["SessionId"] for row in result["tables"]["sessions"]])

    def test_foreign_missing_relative_or_symlinked_database_is_refused(self):
        foreign = tempfile.TemporaryDirectory()
        self.addCleanup(foreign.cleanup)
        outside = Path(foreign.name) / "foreign.db"
        outside.write_bytes(self.database.read_bytes())
        link = self.root / "linked.db"
        link.symlink_to(self.database)
        for database in (outside, self.root / "missing.db", Path("relative.db"), link):
            with self.subTest(database=database), self.assertRaises(RuntimeError):
                snapshot_store(self.root, database, ["own"])

    def test_absent_duplicate_or_unbounded_session_selection_cannot_report_empty_success(self):
        for identities in ([], ["missing"], ["own", "own"], [""], [1], [" own"], [str(index) for index in range(257)]):
            with self.subTest(identities=identities), self.assertRaises(RuntimeError):
                snapshot_store(self.root, self.database, identities)

    def test_missing_table_is_an_observation_failure(self):
        with sqlite3.connect(self.database) as connection:
            connection.execute("DROP TABLE elsa_weaver_audit_owners")
        with self.assertRaises(sqlite3.DatabaseError):
            snapshot_store(self.root, self.database, ["own"])

    def test_each_protected_column_and_canonical_must_remain_exact(self):
        fields = (("sessions", "Payload"), ("sessions", "PausedTurnPayload"), ("sessions", "PendingAuditsPayload"),
                  ("messages", "Payload"), ("context", "Payload"), ("proposals", "Payload"), ("audit", "Payload"), ("snapshots", "Canonical"))
        for role, column in fields:
            with self.subTest(role=role, column=column):
                changed = copy.deepcopy(self.after)
                changed["tables"][role][0][column] += "changed"
                with self.assertRaises(RuntimeError): self.verify(after=changed)
                missing = copy.deepcopy(self.before)
                missing["tables"][role][0][column] = None
                unchanged = copy.deepcopy(missing); unchanged["observedAt"] += 1
                with self.assertRaises(RuntimeError): self.verify(before=missing, after=unchanged)

    def test_missing_duplicate_foreign_rows_and_unknown_stamps_are_refused(self):
        changes = (
            lambda item: item["tables"].pop("keys"),
            lambda item: item["tables"]["auditOwners"].clear(),
            lambda item: item["tables"]["messages"].append(copy.deepcopy(item["tables"]["messages"][0])),
            lambda item: item["tables"]["messages"][0].update(SessionId="foreign"),
            lambda item: item["tables"]["context"][0].update(SchemaVersion="9.0.0"),
            lambda item: item["tables"]["sessions"][0].update(ActorId="foreign"),
            lambda item: item["database"].update(inode=item["database"]["inode"] + 1),
            lambda item: item.update(observedAt=float("nan")))
        for change in changes:
            with self.subTest(change=change):
                changed = copy.deepcopy(self.after); change(changed)
                with self.assertRaises(RuntimeError): self.verify(after=changed)

    def test_retained_counter_cannot_be_restamped_reset_or_advanced_by_an_inert_read(self):
        for change in ({"SchemaVersion": "1.1.0"}, {"Last": 0}, {"Last": 2}, {"Id": 2}):
            with self.subTest(change=change):
                changed = copy.deepcopy(self.after); changed["tables"]["counter"][0].update(change)
                with self.assertRaises(RuntimeError): self.verify(after=changed)
        advanced = copy.deepcopy(self.after); advanced["tables"]["counter"][0]["Last"] += 1
        require_retained_counter(self.before, advanced, advances=True)
        with self.assertRaises(RuntimeError): require_retained_counter(self.before, self.after, advances=True)

    def test_decoded_defaults_counts_digests_and_frozen_preparation_are_mandatory(self):
        changes = (
            lambda item: item.update(actorId="foreign"), lambda item: item.update(tenantId="foreign"),
            lambda item: item["legacyDefaults"].update(preparationsHaveNoClasses=False),
            lambda item: item["legacyDefaults"].update(resultsHaveNoTypedErrorsOrModuleCodes=False),
            lambda item: item["legacyDefaults"].update(noPerformedStatus=False),
            lambda item: item["counts"].update(context=0), lambda item: item["counts"].update(pendingAudits=0),
            lambda item: item.update(content=None), lambda item: item["content"].update(pausedTurn="not-a-digest"),
            lambda item: item.update(snapshots=[{}]), lambda item: item["snapshots"][0].update(size=0),
            lambda item: item["snapshots"][0].update(decodedCanonicalSha256=digest("other")),
            lambda item: item["proposals"][0].update(approvalDigest=digest("changed")),
            lambda item: item["proposals"][0].update(expiresAt="2026-11-14T08:00:01+00:00"),
            lambda item: item["proposals"][0].update(preparedContentSha256=digest("changed")))
        for change in changes:
            with self.subTest(change=change):
                changed = copy.deepcopy(self.decoded); change(changed)
                with self.assertRaises(RuntimeError): self.verify(decoded_after=changed)


if __name__ == "__main__":
    unittest.main()
