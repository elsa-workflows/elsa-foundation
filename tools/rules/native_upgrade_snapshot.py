"""Read-only SQLite evidence for adding Rules to an existing native runtime database."""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import sqlite3


STATE_TABLES = (
    'elsa_runtime_workflow_execution_state',
    'elsa_runtime_activity_execution_state',
    'elsa_runtime_bookmark_state',
    'elsa_runtime_durable_value_state',
    'elsa_runtime_workflow_executable',
)
RULES_TABLES = {'elsa_rules_runtime_runs', 'elsa_rules_runtime_facts', 'elsa_rules_runtime_receipts'}


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False)


def snapshot(database):
    # mode=ro must fail for a missing database, never create an empty success-shaped fixture.
    with sqlite3.connect(database.resolve().as_uri() + '?mode=ro', uri=True) as connection:
        connection.execute('BEGIN')
        schema = [list(row) for row in connection.execute(
            "SELECT type, name, tbl_name, sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY type, name").fetchall()]
        tables = {row[1] for row in schema if row[0] == 'table'}
        if not set(STATE_TABLES).issubset(tables):
            raise ValueError('The database is missing required native state tables.')
        state = {}
        for table in STATE_TABLES:
            cursor = connection.execute(f'SELECT * FROM "{table}"')
            columns = [item[0] for item in cursor.description]
            rows = [dict(zip(columns, [
                {'blobBase64': base64.b64encode(value).decode()} if isinstance(value, bytes) else value
                for value in row])) for row in cursor.fetchall()]
            state[table] = sorted(rows, key=canonical)
        # This fixture owns a fresh database with exactly one waiting workflow.
        if len(state[STATE_TABLES[0]]) != 1 or len(state[STATE_TABLES[2]]) != 1:
            raise ValueError('Expected one retained workflow and one live bookmark.')
        if len(state[STATE_TABLES[1]]) < 3 or not state[STATE_TABLES[4]]:
            raise ValueError('Expected persisted activities and an executable artifact.')
        return {'schema': schema, 'state': state, 'rulesTables': sorted(tables & RULES_TABLES),
                'rulesRunCount': (connection.execute('SELECT count(*) FROM elsa_rules_runtime_runs').fetchone()[0]
                                  if RULES_TABLES.issubset(tables) else None),
                'stateSha256': hashlib.sha256(canonical(state).encode()).hexdigest()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('database', type=Path)
    parser.add_argument('output', type=Path)
    parser.add_argument('--before', type=Path)
    args = parser.parse_args()
    result = snapshot(args.database)
    if args.before:
        before = json.loads(args.before.read_text())
        if before['rulesTables'] or set(result['rulesTables']) != RULES_TABLES or result['rulesRunCount'] != 0:
            raise ValueError('Expected Rules tables to be newly added, with no Rules runs yet.')
        # Preserve every prior schema object, including non-native modules sharing this database.
        current_schema = {row[1]: row for row in result['schema']}
        if any(current_schema.get(row[1]) != row for row in before['schema']):
            raise ValueError('Adding Rules changed a pre-existing schema object.')
        if result['state'] != before['state']:
            raise ValueError('Adding Rules changed retained native state, values, bookmark or artifact.')
        result['preservedNativeStateAndSchema'] = True
        result['addedSchemaObjects'] = sorted(set(current_schema) - {row[1] for row in before['schema']})
    elif result['rulesTables']:
        raise ValueError('Rules schema already exists before the installation phase.')
    args.output.write_text(json.dumps(result, indent=2) + '\n')


if __name__ == '__main__':
    main()
