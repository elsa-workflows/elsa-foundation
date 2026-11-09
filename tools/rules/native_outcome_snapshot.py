"""Capture read-only SQLite evidence for packaged native Rules outcome journeys."""

from __future__ import annotations

import argparse
import base64
import hashlib
import json
from pathlib import Path
import sqlite3
from typing import Any


NATIVE_TABLES = (
    "elsa_runtime_workflow_execution_state",
    "elsa_runtime_activity_execution_state",
    "elsa_runtime_post_commit_outbox",
)
RULES_TABLES = (
    "elsa_rules_runtime_runs",
    "elsa_rules_runtime_receipts",
    "elsa_rules_runtime_facts",
)
OUTBOX_STATUSES = ("Pending", "Delivering", "Delivered", "FailedRetryable", "FailedFinal", "Cancelled")
RUN_STATUSES = ("Running", "Succeeded", "Faulted", "Cancelled", "Refused")


def canonical(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False)


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def utf16_projection(value: Any) -> str | None:
    """Decode an EF UTF-16LE/base64 string projection, preserving lone surrogates."""
    if value is None:
        return None
    try:
        raw = base64.b64decode(value, validate=True)
        if len(raw) % 2:
            raise ValueError("incomplete UTF-16 code unit")
        return raw.decode("utf-16-le", errors="surrogatepass")
    except (ValueError, TypeError, base64.binascii.Error) as error:
        raise ValueError("The native database contains an invalid UTF-16 identity projection.") from error


def json_safe(value: Any) -> Any:
    if isinstance(value, bytes):
        return {"blobBase64": base64.b64encode(value).decode("ascii")}
    return value


def rows(connection: sqlite3.Connection, table: str) -> list[dict[str, Any]]:
    cursor = connection.execute(f'SELECT * FROM "{table}"')
    columns = [column[0] for column in cursor.description or ()]
    result = [dict(zip(columns, (json_safe(value) for value in row))) for row in cursor.fetchall()]
    return sorted(result, key=canonical)


def open_read_only(path_text: str, label: str) -> sqlite3.Connection:
    path = Path(path_text).expanduser().resolve()
    if not path.is_file():
        raise ValueError(f"The {label} database does not exist as a file: {path}")
    connection = sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=5)
    connection.row_factory = sqlite3.Row
    connection.execute("PRAGMA query_only = ON")
    connection.execute("BEGIN")
    return connection


def schema(connection: sqlite3.Connection) -> list[dict[str, Any]]:
    return [dict(row) for row in connection.execute(
        "SELECT type, name, tbl_name, sql FROM sqlite_master "
        "WHERE sql IS NOT NULL ORDER BY type, name"
    ).fetchall()]


def table_names(connection: sqlite3.Connection) -> set[str]:
    return {row[0] for row in connection.execute(
        "SELECT name FROM sqlite_master WHERE type = 'table'"
    ).fetchall()}


def outbox_item_summary(row: dict[str, Any]) -> dict[str, Any]:
    try:
        item = json.loads(row["ContentJson"])
        intent = item["intent"]
        payload = intent.get("payload")
        request = payload.get("request") if isinstance(payload, dict) else None
        destination = payload.get("destination") if isinstance(payload, dict) else None
    except (KeyError, TypeError, json.JSONDecodeError) as error:
        raise ValueError("A native outbox row could not be read as a persisted RuntimePostCommitOutboxItem.") from error

    status_number = row["Status"]
    if not isinstance(status_number, int) or not 0 <= status_number < len(OUTBOX_STATUSES):
        raise ValueError("A native outbox row has an unknown status projection.")
    if not isinstance(request, dict) or not isinstance(destination, dict):
        request = None
        destination = None

    return {
        "outboxItemId": utf16_projection(item.get("outboxItemId")),
        "intentId": utf16_projection(intent.get("intentId")),
        "workflowExecutionId": utf16_projection(row["WorkflowExecutionId"]),
        "activityExecutionId": utf16_projection(intent.get("activityExecutionId")),
        "intentKind": row["IntentKind"],
        "status": OUTBOX_STATUSES[status_number],
        "deliveryAttemptCount": item.get("deliveryAttemptCount"),
        "revision": row["Revision"],
        "request": request,
        "destination": destination,
    }


def run_summary(row: dict[str, Any]) -> dict[str, Any]:
    try:
        invocation = json.loads(row["InvocationJson"])
        identity = invocation["identity"]
        implementation = invocation["implementation"]
    except (KeyError, TypeError, json.JSONDecodeError) as error:
        raise ValueError("A Rules run row could not be read as its canonical invocation envelope.") from error
    status_number = row["Status"]
    if not isinstance(status_number, int) or not 0 <= status_number < len(RUN_STATUSES):
        raise ValueError("A Rules run row has an unknown status projection.")
    return {
        "runId": identity.get("invocation"),
        "workflowExecutionId": (invocation.get("nativeDestination") or {}).get("workflowExecutionId"),
        "activityExecutionId": (invocation.get("nativeDestination") or {}).get("activityExecutionId"),
        "status": RUN_STATUSES[status_number],
        "admitted": row["Admitted"],
        "modelRevision": row["ModelRevision"],
        "committedRounds": row["CommittedRounds"],
        "pinHash": row["PinHash64"],
        "implementation": implementation,
        "terminalKind": None if row["TerminalJson"] is None else json.loads(row["TerminalJson"]).get("kind"),
        "deliveryAcknowledged": row["DeliveryAcknowledged"],
    }


def activity_summary(row: dict[str, Any]) -> dict[str, Any]:
    try:
        state = json.loads(row["ContentJson"])
        execution = state["execution"]
    except (KeyError, TypeError, json.JSONDecodeError) as error:
        raise ValueError("A native activity row could not be read as an ActivityExecutionState document.") from error
    attempts = state.get("attempts") or []
    return {
        "activityExecutionId": utf16_projection(row["ActivityExecutionId"]),
        "status": row["Status"],
        "revision": row["Revision"],
        "executableNodeId": utf16_projection(execution.get("executableNodeId")),
        "attemptCountInternal": len(attempts),
        "attempts": [{
            "attemptId": utf16_projection(attempt.get("attemptId")),
            "ordinal": attempt.get("ordinal"),
            "reason": attempt.get("reason"),
            "transitionKind": attempt.get("transitionKind"),
            "startedAt": attempt.get("startedAt"),
            "endedAt": attempt.get("endedAt"),
        } for attempt in attempts],
        "contentJson": row["ContentJson"],
    }


def snapshot(native_path: str, rules_path: str, phase: str, expected_workflows: list[str]) -> dict[str, Any]:
    native: sqlite3.Connection | None = None
    rules: sqlite3.Connection | None = None
    try:
        native = open_read_only(native_path, "native")
        rules = open_read_only(rules_path, "Rules")
        native_schema = schema(native)
        rules_schema = schema(rules)
        native_tables = table_names(native)
        rules_tables = table_names(rules)
        missing_native = set(NATIVE_TABLES) - native_tables
        missing_rules = set(RULES_TABLES) - rules_tables
        if missing_native:
            raise ValueError(f"The native database is missing required tables: {', '.join(sorted(missing_native))}")
        if missing_rules:
            raise ValueError(f"The Rules database is missing required tables: {', '.join(sorted(missing_rules))}")

        native_rows = {table: rows(native, table) for table in NATIVE_TABLES}
        rules_rows = {table: rows(rules, table) for table in RULES_TABLES}
        workflow_rows = native_rows[NATIVE_TABLES[0]]
        activity_rows = native_rows[NATIVE_TABLES[1]]
        outbox_rows = native_rows[NATIVE_TABLES[2]]
        expected = set(expected_workflows)
        workflow_summaries = []
        for workflow_id in sorted(expected):
            workflow_matches = [row for row in workflow_rows
                                if utf16_projection(row["WorkflowExecutionId"]) == workflow_id]
            if len(workflow_matches) != 1:
                raise ValueError(f"Expected exactly one native workflow row for {workflow_id}; found {len(workflow_matches)}.")
            activities = [row for row in activity_rows
                          if utf16_projection(row["WorkflowExecutionId"]) == workflow_id]
            intents = [outbox_item_summary(row) for row in outbox_rows
                       if utf16_projection(row["WorkflowExecutionId"]) == workflow_id]
            workflow_summaries.append({
                "workflowExecutionId": workflow_id,
                "workflowStatusProjection": workflow_matches[0]["Status"],
                "workflowRevision": workflow_matches[0]["Revision"],
                "activities": [activity_summary(row) for row in activities],
                "intents": intents,
            })

        all_intents = [outbox_item_summary(row) for row in outbox_rows]
        all_runs = [run_summary(row) for row in rules_rows["elsa_rules_runtime_runs"]]
        correlated_run_ids = {
            intent["request"].get("runId")
            for intent in all_intents
            if intent["request"] is not None and intent["workflowExecutionId"] in expected
        }
        matching_runs = [run for run in all_runs if run["runId"] in correlated_run_ids]
        content = {
            "schemaVersion": 1,
            "phase": phase,
            "native": {
                "schema": native_schema,
                "tableRows": native_rows,
                "workflowSummaries": workflow_summaries,
                "outboxIntents": all_intents,
            },
            "rules": {
                "schema": rules_schema,
                "tableRows": rules_rows,
                "runSummaries": all_runs,
                "matchingRunSummaries": matching_runs,
            },
        }
        content["snapshotSha256"] = sha256(canonical(content).encode("utf-8", errors="surrogatepass"))
        return content
    finally:
        if rules is not None:
            try:
                rules.rollback()
            finally:
                rules.close()
        if native is not None:
            try:
                native.rollback()
            finally:
                native.close()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-db", required=True)
    parser.add_argument("--rules-db", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--phase", required=True)
    parser.add_argument("--workflow-id", action="append", default=[])
    args = parser.parse_args()
    if not args.workflow_id:
        parser.error("at least one --workflow-id is required")
    try:
        result = snapshot(args.native_db, args.rules_db, args.phase, args.workflow_id)
        output = Path(args.output).expanduser().resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(result, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
        print(json.dumps({"passed": True, "snapshotSha256": result["snapshotSha256"], "output": str(output)}))
        return 0
    except (OSError, sqlite3.Error, ValueError) as error:
        print(json.dumps({"passed": False, "error": str(error)}))
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
