"""One-time, offline S-9 migration of the eight spec1006 captures' owned protocol keys.

This changes key tokens only. JSON data, string/number spelling, queue identities,
business state and every other database column are retained byte for byte.
Run from any directory with Python 3; an optional argument selects the checkout.
This is a fixture migration, never a production compatibility reader.
"""
from pathlib import Path
import gzip
import hashlib
import json
import sqlite3
import sys

ROOT = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path(__file__).resolve().parents[3]
DIRECTORY = ROOT / 'specs/1006-bpmn-library-adoption/evidence/legacy'
DECODER = json.JSONDecoder()
TABLES = ('elsa_runtime_post_commit_outbox', 'elsa_runtime_scheduler_work_item')


def digest(data):
    return hashlib.sha256(data).hexdigest()


class Node:
    def __init__(self, value, members=None, items=None):
        self.value, self.members, self.items = value, members, items

    def get(self, key):
        return next(v for name, _, _, v in self.members if name in (key, key[0].upper() + key[1:]))


def parse(text, start=0):
    while text[start].isspace():
        start += 1
    if text[start] == '{':
        members, pos = [], start + 1
        while True:
            while text[pos].isspace():
                pos += 1
            if text[pos] == '}':
                return Node(None, members=members), pos + 1
            name, end = DECODER.raw_decode(text, pos)
            key_start, key_end, pos = pos, end, end
            while text[pos].isspace():
                pos += 1
            assert text[pos] == ':'
            value, pos = parse(text, pos + 1)
            members.append((name, key_start, key_end, value))
            while text[pos].isspace():
                pos += 1
            if text[pos] == ',':
                pos += 1
            else:
                assert text[pos] == '}'
                return Node(None, members=members), pos + 1
    if text[start] == '[':
        items, pos = [], start + 1
        while True:
            while text[pos].isspace():
                pos += 1
            if text[pos] == ']':
                return Node(None, items=items), pos + 1
            item, pos = parse(text, pos)
            items.append(item)
            while text[pos].isspace():
                pos += 1
            if text[pos] == ',':
                pos += 1
            else:
                assert text[pos] == ']'
                return Node(None, items=items), pos + 1
    value, end = DECODER.raw_decode(text, start)
    return Node(value), end


WORK = 'WorkItemId WorkflowExecutionId CommandId CommandKind EnvelopeId IdempotencyKey EnqueuedAt RecordedAt Sequence Payload CommandMetadata EnvelopeMetadata ExecutionScopeId Attempt'.split()
PIN = 'ArtifactId DefinitionId DefinitionVersionId ArtifactVersion ArtifactHash'.split()
ATTEMPT = 'AttemptNumber FirstAttemptActivityExecutionId PreviousAttemptActivityExecutionId'.split()
PROVENANCE = 'ParentActivityExecutionId SchedulingActivityExecutionId SchedulingWorkflowExecutionId BranchId IterationId ExecutionPathId ExecutionScopeId SchedulingCause Metadata Attempt CausedBy'.split()
COMMANDS = {
    'StartActivity': 'PinnedExecutable ExecutableNodeId ActivityExecutionId Reason',
    'InvokeActivity': 'PinnedExecutable ExecutableNodeId ActivityExecutionId Reason',
    'CompleteActivity': 'PinnedExecutable ExecutableNodeId ActivityExecutionId ParentActivityExecutionId BranchId OutcomeNames Reason CompletionKind CompletedChildActivityExecutionId ParentOutcome',
    'ScheduleActivity': 'PinnedExecutable ExecutableNodeId Reason ActivityExecutionId SchedulingActivityExecutionId ParentActivityExecutionId SchedulingProvenance IterationFrame InputWarnings',
    'CreateBookmark': 'PinnedExecutable BookmarkId ActivityExecutionId ExecutableNodeId ResumeTargetId StimulusType StimulusHash Payload ExpiresAt Reason Metadata ValueSnapshots DurableValueChanges',
}
KINDS = {7: 'ScheduleActivity', 8: 'CompleteActivity', 10: 'CreateBookmark', 12: 'StartActivity', 13: 'InvokeActivity'}


def rewrite(text, outbox):
    root, end = parse(text)
    assert not text[end:].strip()
    work = root.get('intent').get('payload') if outbox else root
    if work.members is None:
        return text, 0
    replacements = []

    def keys(node, names):
        assert node.members is not None
        allowed = {name: name[0].lower() + name[1:] for name in names}
        for name, start, end, _ in node.members:
            assert name in allowed or name in allowed.values(), ('Unexpected owned field', name)
            if name in allowed:
                replacements.append((start, end, json.dumps(allowed[name])))

    keys(work, WORK)
    if work.get('attempt').members is not None:
        keys(work.get('attempt'), ATTEMPT)
    else:
        assert work.get('attempt').value is None
    kind = work.get('commandKind').value
    kind = KINDS.get(kind, kind)
    assert kind in COMMANDS, kind
    command = work.get('payload')
    keys(command, COMMANDS[kind].split())
    keys(command.get('pinnedExecutable'), PIN)
    for name, _, _, value in command.members:
        canonical = name[0].lower() + name[1:]
        if canonical == 'schedulingProvenance':
            keys(value, PROVENANCE)
            if value.get('attempt').members is not None:
                keys(value.get('attempt'), ATTEMPT)
            else:
                assert value.get('attempt').value is None
        elif canonical in ('valueSnapshots', 'durableValueChanges', 'inputWarnings'):
            assert value.items == [] or (value.items is None and value.value is None), canonical
        elif canonical in ('iterationFrame', 'parentOutcome'):
            assert value.value is None and value.members is None, canonical
        # Payload is carried JSON. Metadata dictionaries, outcome arrays and all leaf tokens stay raw.
    for start, end, value in sorted(replacements, reverse=True):
        text = text[:start] + value + text[end:]
    return text, len(replacements)


def other_columns(connection):
    result = []
    for (table,) in connection.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name").fetchall():
        query = connection.execute('SELECT * FROM "' + table.replace('"', '""') + '" ORDER BY rowid')
        names = [x[0] for x in query.description]
        for row in query:
            result.append((table, tuple((name, value) for name, value in zip(names, row)
                                       if not (table in TABLES and name == 'ContentJson'))))
    return result


for path in sorted(DIRECTORY.glob('*.sqlite.gz')):
    receipt_path = path.with_suffix('').with_suffix('.json')
    receipt = json.loads(receipt_path.read_text(encoding='utf-8'))
    if 's9ProtocolRegeneration' in receipt:
        assert digest(path.read_bytes()) == receipt['compressedSha256']
        assert digest(gzip.decompress(path.read_bytes())) == receipt['databaseSha256']
        print(path.name, 'already migrated and digest verified')
        continue
    original = path.read_bytes()
    data = gzip.decompress(original)
    assert digest(original) == receipt['compressedSha256']
    assert digest(data) == receipt['databaseSha256']
    scratch = ROOT / '.agent-prefs' / ('s9-protocol-' + path.stem + '.sqlite')
    scratch.write_bytes(data)
    connection = sqlite3.connect(scratch)
    before = other_columns(connection)
    changes = []
    for table in TABLES:
        for row_id, text in connection.execute('SELECT rowid, ContentJson FROM ' + table).fetchall():
            updated, count = rewrite(text, table == TABLES[0])
            if count:
                connection.execute('UPDATE ' + table + ' SET ContentJson=? WHERE rowid=?', (updated, row_id))
                changes.append(dict(table=table, rowId=row_id, renamedKeyTokens=count,
                                    inputSha256=digest(text.encode()), outputSha256=digest(updated.encode())))
    assert other_columns(connection) == before, 'Unrelated persisted state changed'
    connection.commit()
    connection.execute('PRAGMA wal_checkpoint(TRUNCATE)')
    connection.close()
    updated_data = scratch.read_bytes()
    updated = gzip.compress(updated_data, compresslevel=9, mtime=0)
    receipt['s9ProtocolRegeneration'] = dict(spec=1207, inputCompressedSha256=digest(original),
        inputDatabaseSha256=digest(data), changedColumns=[table + '.ContentJson' for table in TABLES],
        changes=changes, preserved='Only declared RuntimeSchedulerWorkItem/typed command/identity/provenance key tokens change; every other JSON token and database column is unchanged. Historical old-host proof applies to the original capture.')
    receipt['databaseSha256'], receipt['compressedSha256'] = digest(updated_data), digest(updated)
    path.write_bytes(updated)
    receipt_path.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(path.name, len(changes), 'rows;', sum(x['renamedKeyTokens'] for x in changes), 'key tokens')
