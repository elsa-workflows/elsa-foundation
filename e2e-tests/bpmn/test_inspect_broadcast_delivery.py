"""Real checked-provider inventory controls; not host execution/recovery evidence."""
import base64
from datetime import date
import gzip
import hashlib
import json
from pathlib import Path
import sqlite3
import tempfile
import subprocess
import unittest

import inspect_broadcast_delivery as preflight

PUBLISH_KIND = "Elsa.Activities.Primitives.PublishStimulus"
STATUSES = ["Pending", "Delivering", "Delivered", "FailedRetryable", "FailedFinal", "Cancelled"]
NOW = "2026-10-25T00:00:00.0000000+00:00"
TICKS = (date(2026, 10, 25) - date(1, 1, 1)).days * 864000000000
MAX_TICKS = 3155378975999999999


def utf16(text):
    return text.encode("utf-16-le", errors="surrogatepass")


def encode(text):
    return base64.b64encode(utf16(text)).decode()


def hash_identity(text):
    return hashlib.sha256(utf16(text)).hexdigest().upper()


def row_identity(scope, identity):
    return hash_identity(f"{len(utf16(scope)) // 2}:{scope}{len(utf16(identity)) // 2}:{identity}")


def workflow_order(identity):
    prefix = utf16(identity)[:64]
    # Provider's fixed-width UTF-16BE prefix, length terminator and identity hash (all fixture IDs are short).
    units = [prefix[i:i + 2][::-1] for i in range(0, len(prefix), 2)]
    raw = b"".join(units).ljust(64, b"\0") + (len(prefix) // 2).to_bytes(2, "big")
    return raw.hex().upper() + hash_identity(identity)


class BroadcastPreflightTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "source.sqlite"
        self.connection = sqlite3.connect(self.path)
        self.addCleanup(self.connection.close)
        self.connection.execute('''CREATE TABLE elsa_runtime_post_commit_outbox (
            Id TEXT PRIMARY KEY, ScopeKey TEXT, ScopeKeyHash TEXT, OutboxItemId TEXT,
            OutboxItemIdHash TEXT, OutboxItemIdOrderKey TEXT, WorkflowExecutionId TEXT,
            WorkflowExecutionIdHash TEXT, WorkflowExecutionIdOrderKey TEXT, IntentKind TEXT,
            IntentKindHash TEXT, Status INTEGER, RecordedAtUtcTicks INTEGER, DeliverableAtUtcTicks INTEGER,
            ClaimableAtUtcTicks INTEGER, ClaimableIsEligible INTEGER, ContentJson TEXT, SchemaVersion TEXT, Revision INTEGER)''')
        self.connection.commit()

    def add(self, status=0, attempts=0, family="1.0.0", kind=PUBLISH_KIND, content_kind=None, cohort=None,
            row_id="row-1", physical_id=None, change_item=None):
        logical = "source-DO-NOT-EMIT-" + row_id
        scope = "scope-DO-NOT-EMIT"
        workflow = "workflow"
        item = {"outboxItemId": encode(logical),
                "intent": {"intentId": encode("intent"), "workflowExecutionId": encode(workflow),
                           "kind": encode(content_kind or kind), "recordedAt": NOW,
                           "metadata": {}, "payload": {"private": "sensitive-payload"}},
                "recordedAt": NOW, "availableAt": None, "retryPolicy": None,
                "status": STATUSES[status], "deliveryAttemptCount": attempts, "deliveryFencingToken": 0,
                "deliveringOwnerId": encode("owner") if status == 1 else None,
                "deliveryStartedAt": NOW if status == 1 else None,
                "deliveredAt": NOW if status == 2 else None, "deliveryVisibleAfter": None,
                "metadata": {encode("runtime.delivery.firstAttemptedAt"): encode(NOW)}}
        if cohort is not None:
            item["recipientSnapshot"] = cohort
        if change_item:
            change_item(item)
        eligible = status == 0  # RetryPolicy.None means retryable items are exhausted.
        values = (physical_id if physical_id is not None else row_identity(scope, logical),
                  encode(scope), hash_identity(scope), encode(logical), hash_identity(logical),
                  utf16(logical).decode("utf-16-le").encode("utf-16-be").hex().upper(),
                  encode(workflow), hash_identity(workflow), workflow_order(workflow), kind, hash_identity(kind),
                  status, TICKS, 0 if eligible else None, 0 if eligible else MAX_TICKS, int(eligible),
                  json.dumps(item), family, 1)
        self.connection.execute('INSERT INTO elsa_runtime_post_commit_outbox VALUES (' + ','.join('?' for _ in values) + ')', values)
        self.connection.commit()

    def test_zero_attempt_legacy_source_still_requires_recovery(self):
        self.add()
        result = preflight.inspect_database(self.path)
        self.assertEqual("RecoveryRequired", result["status"])
        self.assertTrue(result["providerChecked"])
        self.assertEqual("LegacyRecoveryRequired", result["publishes"][0]["classification"])
        self.assertTrue(result["publishes"][0]["firstAttemptRecorded"])

    def test_only_valid_delivered_counts_as_settled(self):
        for status in range(6):
            with self.subTest(status=status):
                self.connection.execute('DELETE FROM elsa_runtime_post_commit_outbox')
                self.connection.commit()
                self.add(status=status)
                result = preflight.inspect_database(self.path)
                self.assertTrue(result["providerChecked"], result)
                self.assertEqual("NoLegacyPublishResponsibilityOwed" if status == 2 else "RecoveryRequired", result["status"])

    def test_receipt_omits_payload_and_raw_identity_scope(self):
        self.add()
        wire = json.dumps(preflight.inspect_database(self.path))
        for private in ["source-DO-NOT-EMIT", "scope-DO-NOT-EMIT", "sensitive-payload"]:
            self.assertNotIn(private, wire)

    def test_projection_cannot_hide_a_publish_behind_another_kind(self):
        self.add(kind="other-kind", content_kind=PUBLISH_KIND)
        self.assertEqual("InventoryUnavailable", preflight.inspect_database(self.path)["status"])

    def test_unsupported_family_is_not_treated_as_no_publish_work(self):
        self.add(kind="other-kind", family="999.0.0")
        self.assertEqual("InventoryUnavailable", preflight.inspect_database(self.path)["status"])

    def test_declared_current_family_can_preserve_legacy_absence(self):
        self.add(kind="other-kind", family="2.0.0")
        result = preflight.inspect_database(self.path)
        self.assertTrue(result["providerChecked"])
        self.assertEqual("NoLegacyPublishResponsibilityOwed", result["status"])

    def test_legacy_family_cannot_claim_a_protected_cohort(self):
        self.add(cohort={"version": 1, "starts": [], "resumes": []})
        self.assertEqual("InventoryUnavailable", preflight.inspect_database(self.path)["status"])

    def test_empty_physical_id_cannot_hide_an_unresolved_publish(self):
        self.add(physical_id="")
        self.assertEqual("InventoryUnavailable", preflight.inspect_database(self.path)["status"])

    def test_null_physical_id_cannot_hide_an_unresolved_publish(self):
        self.add()
        self.connection.execute('UPDATE elsa_runtime_post_commit_outbox SET Id=NULL')
        self.connection.commit()
        self.assertEqual("InventoryUnavailable", preflight.inspect_database(self.path)["status"])

    def test_native_reader_refuses_a_live_wal_database_instead_of_ignoring_it(self):
        self.connection.execute('PRAGMA journal_mode=WAL')
        self.add(status=2)
        result = subprocess.run(["dotnet", str(preflight.VALIDATOR), str(self.path),
                                 preflight.digest(self.path.read_bytes())], capture_output=True, text=True)
        self.assertEqual(2, result.returncode)
        self.assertEqual("InventoryUnavailable", json.loads(result.stdout)["status"])

    def test_malformed_settlement_or_source_is_never_clean(self):
        for field, value in [("deliveredAt", None), ("deliveringOwnerId", encode("stale-owner")),
                             ("deliveryStartedAt", NOW), ("deliveryVisibleAfter", NOW),
                             ("deliveryFencingToken", -1), ("deliveredAt", "invalid-time")]:
            with self.subTest(field=field):
                self.connection.execute('DELETE FROM elsa_runtime_post_commit_outbox')
                self.connection.commit()
                self.add(status=2, change_item=lambda item: item.__setitem__(field, value))
                self.assertEqual("InventoryUnavailable", preflight.inspect_database(self.path)["status"])

    def test_forged_physical_projection_cannot_redirect_to_a_valid_row(self):
        self.add(status=2)
        # The second row points at the first row's logical identity. Finding that valid row must not bless this one.
        self.connection.execute('INSERT INTO elsa_runtime_post_commit_outbox SELECT ?, ScopeKey, ScopeKeyHash, OutboxItemId, OutboxItemIdHash, OutboxItemIdOrderKey, WorkflowExecutionId, WorkflowExecutionIdHash, WorkflowExecutionIdOrderKey, IntentKind, IntentKindHash, Status, RecordedAtUtcTicks, DeliverableAtUtcTicks, ClaimableAtUtcTicks, ClaimableIsEligible, ContentJson, SchemaVersion, Revision FROM elsa_runtime_post_commit_outbox', ("forged",))
        self.connection.commit()
        self.assertEqual("InventoryUnavailable", preflight.inspect_database(self.path)["status"])

    def test_live_backup_includes_committed_wal_and_omits_uncommitted_work(self):
        self.connection.execute('PRAGMA journal_mode=WAL')
        self.add(status=2)
        self.connection.execute('UPDATE elsa_runtime_post_commit_outbox SET ContentJson=?', ("invalid-json",))
        result = preflight.inspect_database(self.path)
        self.assertTrue(result["providerChecked"], result)
        self.assertEqual("sqlite-backup-including-wal", result["sourceKind"])
        self.assertEqual(1, result["outboxRows"])
        self.assertEqual("LegacyDelivered", result["publishes"][0]["classification"])
        self.assertEqual("invalid-json", self.connection.execute('SELECT ContentJson FROM elsa_runtime_post_commit_outbox').fetchone()[0])

    def test_compressed_closed_capture_binds_both_hashes(self):
        self.add(status=2)
        self.connection.close()
        raw = self.path.read_bytes()
        compressed = Path(self.directory.name) / "closed.sqlite.gz"
        compressed.write_bytes(gzip.compress(raw))
        result = preflight.inspect_database(compressed)
        self.assertTrue(result["providerChecked"], result)
        self.assertEqual(preflight.digest(raw), result["databaseSha256"])
        self.assertEqual(preflight.digest(compressed.read_bytes()), result["compressedSha256"])
        self.assertEqual(raw, self.path.read_bytes())

    def test_inventory_reads_beyond_its_first_page(self):
        for index in range(300):
            self.add(status=2, row_id=f"row-{index:04}")
        result = preflight.inspect_database(self.path)
        self.assertTrue(result["providerChecked"], result)
        self.assertEqual(300, result["outboxRows"])
        self.assertEqual(300, result["publishRows"])


if __name__ == "__main__":
    unittest.main()
