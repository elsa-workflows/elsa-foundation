"""Read-only, WAL-aware fingerprints of one owned workflow's publication boundary."""
import argparse
import base64
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import sqlite3

TABLES = {
    "elsa_runtime_workflow_executable": "ContentJson",
    "elsa_runtime_workflow_executable_source_reference": "DefinitionId",
    "elsa_publication_records": "WorkflowDefinitionId",
    "elsa_runtime_workflow_activation_slot": "WorkflowDefinitionId",
}


@contextmanager
def readonly_database(path):
    # Normal SQLite read transactions include committed WAL content; immutable=1 would ignore it.
    connection = sqlite3.connect(path.resolve().as_uri() + "?mode=ro", uri=True)
    try:
        connection.execute("PRAGMA query_only=ON")
        connection.execute("BEGIN")
        yield connection
    finally:
        connection.rollback()
        connection.close()


def snapshot(root, definition_id, root_node_id, require_published=False):
    encoded = base64.b64encode(definition_id.encode("utf-16-le")).decode("ascii")
    encoded_root = base64.b64encode(root_node_id.encode("utf-16-le")).decode("ascii")
    result = {}
    for database in sorted(root.rglob("*.db")):
        with readonly_database(database) as connection:
            names = {row[0] for row in connection.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            for table, owner_column in TABLES.items():
                if table not in names:
                    continue
                if table in result:
                    raise ValueError(f"Ambiguous owned store for {table}")
                cursor = connection.execute(f'SELECT * FROM "{table}"')
                columns = [column[0] for column in cursor.description]
                if owner_column not in columns:
                    raise ValueError(f"Missing owner column {table}.{owner_column}")
                rows = []
                for values in cursor:
                    row = dict(zip(columns, values))
                    owned = (json.loads(row[owner_column]).get("rootActivity", {}).get("authoredActivityId") == encoded_root
                             if owner_column == "ContentJson" else row[owner_column] == encoded)
                    if owned:
                        rows.append(json.dumps(row, sort_keys=True, separators=(",", ":"),
                                               default=lambda value: {"bytes": value.hex()}))
                rows.sort()
                result[table] = {"store": str(database.relative_to(root)), "columns": columns, "count": len(rows),
                                 "sha256": hashlib.sha256("\n".join(rows).encode()).hexdigest()}
    if set(result) != set(TABLES):
        raise ValueError(f"Missing expected publication stores/tables: {sorted(set(TABLES) - set(result))}")
    if require_published and any(table["count"] == 0 for table in result.values()):
        raise ValueError("Published positive control must expose owned artifact, reference, publication and activation rows")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("definition_id")
    parser.add_argument("root_node_id")
    parser.add_argument("--require-published", action="store_true")
    args = parser.parse_args()
    print(json.dumps(snapshot(args.root, args.definition_id, args.root_node_id, args.require_published), sort_keys=True))
