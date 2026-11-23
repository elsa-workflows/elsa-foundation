"""Independent, hand-declared RuntimeHistoryIdentity recipes. No production code is loaded.

Opaque identities are framed as UTF-16LE Base64; payloads remain exact JSON values.
The first stable release freezes these recipes and their complete owned DTO contracts.
"""
from pathlib import Path
import base64
import hashlib
import importlib.util
import json

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def opaque(value):
    return None if value is None else base64.b64encode(value.encode('utf-16-le', errors='surrogatepass')).decode()


def pin(value):
    canonical = helper.canonical(numbers(value))
    return canonical, hashlib.sha256(canonical.encode()).hexdigest()


def numbers(value):
    if isinstance(value, bool):
        return value
    if isinstance(value, int):
        return helper.Number(str(value))
    if isinstance(value, dict):
        return {key: numbers(item) for key, item in value.items()}
    if isinstance(value, list):
        return list(map(numbers, value))
    return value


def event(payload=None, has_payload=False, ancestry=None, occurrence=None, bookmark=False):
    value = {key: opaque(text) for key, text in dict(
        historyEventId='event', workflowExecutionId='run', name='BookmarkConsumed' if bookmark else 'ActivityStarted',
        activityExecutionId='activity', executableNodeId='node', checkpointId='checkpoint', incidentId=None,
        bookmarkId='bookmark' if bookmark else None, attemptId=None, parentActivityExecutionId=None,
        executionScopeId=None, iterationId=None).items()}
    value.update(category='BookmarkLifecycle' if bookmark else 'ActivityLifecycle',
                 occurredAt='1970-01-01T00:00:00+00:00', checkpointOrdinal=0, schemaVersion=1,
                 authority='CommittedTransition', causation=dict(historyEventId=opaque('cause'), workflowExecutionId=None,
                 deliveryId=None, commandId=None, schedulerWorkItemId=None),
                 hasPayload=has_payload, payload=payload, metadata=[dict(key=opaque('OwnerKey'), value=opaque('René'))])
    stages = []
    for version, known in [('ancestryVersion', ancestry), ('occurrenceVersion', occurrence)]:
        if known is not None:
            stages.append(value)
            value = {version: 1, 'event': opaque(pin(value)[1]), 'known': known}
    return value, stages


def generate():
    vectors = []

    def add(name, family, value, **inputs):
        canonical, digest = pin(value)
        vectors.append(dict(name=name, family=family, **inputs, canonical=canonical, sha256=digest))

    for name, fields in [('simple', ['checkpoint', 'name', 'subject']), ('framing-a', ['a\0b', 'c', 'd']),
                         ('framing-b', ['a', 'b\0c', 'd']), ('surrogate-a', ['a', 'b', '\ud800']),
                         ('surrogate-b', ['a', 'b', '\ud801'])]:
        encoded = list(map(opaque, fields))
        add(name, 'id', dict(zip(['logicalCheckpointId', 'name', 'subject'], encoded)), fields=encoded)
    for name, fields in [('null', [None]), ('empty', ['']), ('order-a', ['a', 'b']), ('order-b', ['b', 'a']),
                         ('surrogate-a', ['\ud800']), ('surrogate-b', ['\ud801'])]:
        encoded = list(map(opaque, fields))
        add(name, 'binding', encoded, fields=encoded)
    scenarios = [('missing', None, False), ('null', 'null', True), ('spelling-a', '{"n":1.0,"OwnerKey":"\\u0061"}', True),
                 ('spelling-b', '{"OwnerKey":"a","n":1e0}', True), ('large-a', '{"n":9007199254740993}', True),
                 ('large-b', '{"n":9007199254740992}', True), ('order-a', '[1,2]', True), ('order-b', '[2,1]', True)]
    for name, raw, present in scenarios:
        # Preserve numeric tokens through the independent canonical parser.
        value, _ = event()
        value['hasPayload'] = present
        value['payload'] = helper.load_json(raw) if present else None
        # Decimal tokens are emitted by the canonical oracle, not Python's float encoder.
        canonical = helper.canonical(numbers(value))
        vectors.append(dict(name=name, family='event', payload=raw, ancestry=None, occurrence=None,
                            canonical=canonical, sha256=hashlib.sha256(canonical.encode()).hexdigest()))
    for name, ancestry, occurrence in [('ancestry-false', False, None), ('ancestry-true', True, None),
                                       ('occurrence-false', None, False), ('both-known', True, True)]:
        value, stages = event(ancestry=ancestry, occurrence=occurrence)
        add(name, 'event', value, payload=None, ancestry=ancestry, occurrence=occurrence,
            stages=[dict(canonical=pin(stage)[0], sha256=pin(stage)[1]) for stage in stages])
    for status in ['Consumed', 'Removed']:
        value, _ = event(bookmark=True)
        mutation = dict(bookmark=dict(identity=dict(bookmarkId=opaque('bookmark'), workflowExecutionId=opaque('run'),
            activityExecutionId=opaque('activity'), resumeTargetId=opaque('resume')), stimulusType=opaque('Signal'),
            stimulusHash=opaque('hash'), createdAt='1970-01-01T00:00:00+00:00', expiresAt=None),
            status=status, reason=opaque('selected'), selectedBookmarkId=None)
        add(status.lower(), 'candidate', dict(event=opaque(pin(value)[1]), bookmarkMutation=mutation), status=status)
    return vectors


if __name__ == '__main__':
    target = Path(__file__).with_name('history-hash-vectors.json')
    target.write_text(json.dumps(generate(), ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
