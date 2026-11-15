"""Read-only, owner-bounded SQLite evidence for the genuine T038 writer generations.

These are fixture-store observations, not a writer, codec or substitute for native
old/current package processes. Opaque protected strings are retained unchanged;
the separately packaged inspector must prove their real authenticated decoding.
That inspector reads session records; the caller must separately validate actual
audit-reader decoding and process/package/source/HTTP receipt bindings.
"""
from __future__ import annotations

import hashlib
import math
from pathlib import Path
import re
import sqlite3
import time

from artifacts import check

TABLES = {
    "sessions": ("elsa_weaver_sessions", ("SessionId",), ("SessionId", "ActorId", "TenantId", "SchemaVersion", "Payload", "PausedTurnPayload", "PendingAuditsPayload")),
    "messages": ("elsa_weaver_messages", ("SessionId", "MessageId"), ("SessionId", "MessageId", "SchemaVersion", "Payload")),
    "snapshots": ("elsa_weaver_disclosure_snapshots", ("SessionId", "Digest"), ("SessionId", "Digest", "Size", "SchemaVersion", "Canonical")),
    "context": ("elsa_weaver_context", ("SessionId", "AttachmentKey"), ("SessionId", "AttachmentKey", "SchemaVersion", "Payload")),
    "proposals": ("elsa_weaver_proposals", ("ProposalId",), ("SessionId", "ProposalId", "SchemaVersion", "Payload")),
    "keys": ("elsa_weaver_call_keys", ("SessionId", "Kind", "KeyHash"), ("SessionId", "Kind", "KeyHash", "SchemaVersion", "Value")),
    "audit": ("elsa_weaver_audit", ("Sequence",), ("SessionId", "Sequence", "EventId", "ActorId", "SchemaVersion", "Payload")),
    "auditOwners": ("elsa_weaver_audit_owners", ("SessionId",), ("SessionId", "ActorId", "TenantId", "SchemaVersion")),
    "counter": ("elsa_weaver_audit_sequence", ("Id",), ("Id", "Last", "SchemaVersion")),
}
FORMAT = "weaver.upgrade.store-snapshot"
CONTENT_SECTIONS = {"session", "messages", "snapshots", "context", "pausedTurn", "proposals", "keys", "pendingAudits"}


def _digest(value):
    return isinstance(value, str) and re.fullmatch(r"[a-f0-9]{64}", value) is not None


def _need(value, message):
    check(value, "Upgrade store evidence: " + message)


def _owned_database(root, database):
    root, database = Path(root).resolve(), Path(database)
    _need(database.is_absolute() and database.is_relative_to(root) and database.resolve().is_relative_to(root)
          and database.is_file(), "database must be an existing file under the owned run root")
    _need(all(not parent.is_symlink() for parent in (database, *database.parents) if parent.is_relative_to(root)),
          "database path cannot contain a symlink")
    return database


def snapshot_store(root, database, session_ids):
    """Capture nine real table kinds in one read-only SQLite transaction; never read another module's tables."""
    database = _owned_database(root, database)
    _need(isinstance(session_ids, (list, tuple)) and 0 < len(session_ids) <= 256
          and all(isinstance(value, str) and value.strip() == value and value for value in session_ids)
          and len(set(session_ids)) == len(session_ids), "name bounded distinct owned sessions")
    session_ids = sorted(session_ids)
    original = database.stat()
    result = {"format": FORMAT, "version": 1, "observedAt": time.time(), "sessions": session_ids,
              "database": {"path": str(database), "device": original.st_dev, "inode": original.st_ino}, "tables": {}}
    with sqlite3.connect(database.as_uri() + "?mode=ro", uri=True, timeout=5) as connection:
        connection.row_factory = sqlite3.Row
        connection.execute("PRAGMA query_only=ON")
        connection.execute("BEGIN")
        for role, (table, keys, columns) in TABLES.items():
            fields = ",".join('"' + name + '"' for name in columns)
            order = ",".join('"' + name + '"' for name in keys)
            where = '"Id"=1' if role == "counter" else '"SessionId" IN (' + ",".join("?" for _ in session_ids) + ")"
            rows = [dict(row) for row in connection.execute('SELECT ' + fields + ' FROM "' + table + '" WHERE ' + where + " ORDER BY " + order,
                                                           () if role == "counter" else session_ids)]
            if role == "keys":
                for row in rows:
                    value = row.pop("Value")
                    _need(isinstance(value, str), "deduplication value is not text")
                    row["ValueSha256"] = hashlib.sha256(value.encode()).hexdigest()
            result["tables"][role] = rows
    current = database.stat()
    _need((original.st_dev, original.st_ino) == (current.st_dev, current.st_ino), "database was replaced during the snapshot")
    _require_snapshot(result)
    return result


def _require_snapshot(value):
    _need(isinstance(value, dict) and value.get("format") == FORMAT and value.get("version") == 1,
          "missing actual logical snapshot format")
    _need(type(value.get("observedAt")) in (int, float) and math.isfinite(value["observedAt"]), "invalid observation time")
    sessions = value.get("sessions")
    _need(isinstance(sessions, list) and sessions and all(isinstance(item, str) and item for item in sessions)
          and sessions == sorted(set(sessions)), "invalid selected session identities")
    database = value.get("database")
    _need(isinstance(database, dict) and isinstance(database.get("path"), str) and Path(database["path"]).is_absolute()
          and all(type(database.get(key)) is int and database[key] >= 0 for key in ("device", "inode")), "missing database identity")
    tables = value.get("tables")
    _need(isinstance(tables, dict) and set(tables) == set(TABLES), "all nine table kinds must be observed, including empty results")
    for role, (_, keys, columns) in TABLES.items():
        rows = tables[role]
        _need(isinstance(rows, list), "table result must be a list")
        expected = (set(columns) - {"Value"}) | {"ValueSha256"} if role == "keys" else set(columns)
        identities = []
        for row in rows:
            _need(isinstance(row, dict) and set(row) == expected, "table projection differs from its pinned source schema")
            _need(row.get("SchemaVersion") in ("1.0.0", "1.1.0"), "unknown physical Weaver schema stamp")
            _need(role == "counter" or row.get("SessionId") in sessions, "snapshot contains a foreign session")
            _need(all(type(row.get(key)) is int and row[key] >= 0 if key in ("Sequence", "Id") else isinstance(row.get(key), str) and bool(row[key]) for key in keys),
                  "invalid row identity")
            if role == "keys": _need(_digest(row["ValueSha256"]), "invalid deduplication value hash")
            if role == "snapshots":
                _need(_digest(row["Digest"]) and type(row["Size"]) is int and row["Size"] > 0, "invalid canonical disclosure identity/size")
            identities.append(tuple(row[key] for key in keys))
        _need(len(identities) == len(set(identities)), "duplicate physical row identity")
    _need(sorted(row["SessionId"] for row in tables["sessions"]) == sessions, "a selected session was not observed")
    counter = tables["counter"]
    _need(len(counter) == 1 and counter[0]["Id"] == 1 and type(counter[0]["Last"]) is int and counter[0]["Last"] >= 0,
          "missing real shared audit sequence")
    return value


def require_retained_counter(before, after, *, advances):
    _require_snapshot(before); _require_snapshot(after)
    _need(before["database"] == after["database"], "counter came from a replaced/foreign database")
    left, right = before["tables"]["counter"][0], after["tables"]["counter"][0]
    _need(left["SchemaVersion"] == right["SchemaVersion"] == "1.0.0", "retained audit counter was restamped")
    _need(right["Last"] > left["Last"] if advances else right["Last"] == left["Last"], "audit counter did not preserve the required append boundary")


def require_identity_rows(before, after, decoded_before, decoded_after, *, actor_id, tenant_id):
    """The inert all-row case: real authenticated decoding, unchanged protected bytes and only identity upcast stamps."""
    _require_snapshot(before); _require_snapshot(after)
    _need(before["database"] == after["database"] and before["sessions"] == after["sessions"]
          and len(before["sessions"]) == 1 and before["observedAt"] < after["observedAt"], "wrong store/session/observation order")
    _need(isinstance(actor_id, str) and actor_id and isinstance(tenant_id, str) and tenant_id, "missing authenticated owner binding")
    for role, (_, keys, _) in TABLES.items():
        left, right = before["tables"][role], after["tables"][role]
        _need(left and right and all(row["SchemaVersion"] == "1.0.0" for row in left), "old writer did not create every required row kind at1.0")
        left_map = {tuple(row[key] for key in keys): {key: item for key, item in row.items() if key != "SchemaVersion"} for row in left}
        right_map = {tuple(row[key] for key in keys): {key: item for key, item in row.items() if key != "SchemaVersion"} for row in right}
        _need(left_map == right_map, "identity upgrade changed old physical identity/content in " + role)
        if role in ("sessions", "auditOwners"):
            _need(all(row["ActorId"] == actor_id and row["TenantId"] == tenant_id for row in left), "old row owner differs from current native identity")
        protected = ("Payload", "PausedTurnPayload", "PendingAuditsPayload") if role == "sessions" else ("Payload",) if role in ("messages", "context", "proposals", "audit") else ("Canonical",) if role == "snapshots" else ()
        for column in protected:
            _need(any(isinstance(row[column], str) and bool(row[column]) for row in left), "missing actual protected string " + role + "." + column)
    for decoded in (decoded_before, decoded_after):
        _need(isinstance(decoded, dict) and decoded.get("evidenceKind") == "native-real-store-decoding-without-engine-hydration"
              and decoded.get("sessionId") == before["sessions"][0] and decoded.get("actorId") == actor_id and decoded.get("tenantId") == tenant_id,
              "missing independently authorized real-store decoding")
        _need(decoded.get("legacyDefaults") == {"preparationsHaveNoClasses": True, "resultsHaveNoTypedErrorsOrModuleCodes": True, "noPerformedStatus": True},
              "new model defaults were not observed after authenticated decoding")
        _need(isinstance(decoded.get("content"), dict) and set(decoded["content"]) == CONTENT_SECTIONS
              and all(_digest(item) for item in decoded["content"].values()), "missing complete decoded content fingerprints")
        expected_counts = {name: len(before["tables"][name]) for name in ("messages", "snapshots", "context", "proposals", "keys")}
        _need(isinstance(decoded.get("counts"), dict) and set(decoded["counts"]) == set(expected_counts) | {"pendingAudits"}
              and all(type(decoded["counts"].get(name)) is int and decoded["counts"][name] == count for name, count in expected_counts.items())
              and type(decoded["counts"]["pendingAudits"]) is int and decoded["counts"]["pendingAudits"] > 0, "decoded row coverage differs from physical rows")
        _need(isinstance(decoded.get("snapshots"), list) and decoded["snapshots"]
              and all(isinstance(item, dict) and set(item) == {"digest", "size", "decodedCanonicalSha256"}
                      and _digest(item["digest"]) and item["digest"] == item["decodedCanonicalSha256"]
                      and type(item["size"]) is int and item["size"] > 0 for item in decoded["snapshots"]),
              "decoded canonical text differs from its original disclosure digest")
        canonical = {item["digest"]: item["size"] for item in decoded["snapshots"]}
        _need(len(canonical) == len(decoded["snapshots"])
              and canonical == {row["Digest"]: row["Size"] for row in before["tables"]["snapshots"]}, "decoded canonical identities differ from physical snapshots")
        proposals = decoded.get("proposals")
        _need(isinstance(proposals, list) and all(isinstance(item, dict)
              and set(item) == {"id", "status", "toolName", "moduleId", "approvalDigest", "expiresAt", "preparedContentSha256"}
              and all(isinstance(item.get(name), str) and item[name] for name in ("id", "status", "toolName", "moduleId", "expiresAt"))
              and _digest(item["approvalDigest"]) and _digest(item["preparedContentSha256"]) for item in proposals)
              and sorted(item["id"] for item in proposals) == sorted(row["ProposalId"] for row in before["tables"]["proposals"]),
              "missing exact safe preparation/digest/deadline observations")
    _need(decoded_before.get("content") == decoded_after.get("content") and decoded_before.get("counts") == decoded_after.get("counts")
          and decoded_before.get("proposals") == decoded_after.get("proposals"), "real decoder changed legacy semantic content or preparation")
    require_retained_counter(before, after, advances=False)
    return {"rowKinds": len(TABLES), "chainProtectedColumns": 7, "canonicalProtectedColumn": 1,
            "legacyDefaults": True, "counterStamp": "1.0.0", "evidenceKind": "real-writer-identity-upgrade-content"}
