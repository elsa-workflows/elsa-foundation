"""Independent pins for saved-view JSON projections and recovery scan binding digests."""
from pathlib import Path
from datetime import datetime
import base64
import copy
import hashlib
import hmac
import importlib.util
import json

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def frame(kind, canonical):
    return ''.join(str(len(value.encode())) + ':' + value for value in ['elsa-design-material:v1', kind, '1', canonical])


def generate():
    query = dict(searchTerm='René <+>', state='active', folderId=None, unfiled=False, includeDescendants=False,
        tagFilters=[], favoritesOnly=False, sort='modified', pageSize=helper.Number('50'), continuationToken=None, publicationState=None)
    content = dict(query=query, columns=['name', 'tags'], schemaVersion=helper.Number('1'))
    views = []
    for name, title, columns, shared in [('base', 'View <é>', ['name', 'tags'], False),
        ('column-order', 'View <é>', ['tags', 'name'], False), ('shared', 'View <é>', ['name', 'tags'], True),
        ('text-a', '{}', ['name', 'tags'], False), ('text-b', '{ }', ['name', 'tags'], False)]:
        projected = copy.deepcopy(content)
        projected['columns'] = columns
        keyed = dict(name=title, content=projected, shared=shared)
        operation_key = hashlib.sha256(b'workflow-library-client-key\0key').hexdigest()
        request = dict(id=None, name=title, content=projected, shared=shared, revision=None, subjectId='alice', operationKey=operation_key)
        canonical_keyed, canonical_request = helper.canonical(keyed), helper.canonical(request)
        framed = frame('workflow.view.save.v1', canonical_request)
        views.append(dict(name=name, title=title, columns=columns, shared=shared,
            contentCanonical=helper.canonical(projected), keyedCanonical=canonical_keyed, requestCanonical=canonical_request,
            requestFrame=framed, keyedHash=hashlib.sha256(canonical_keyed.encode()).hexdigest(),
            requestHash='sha256:' + hashlib.sha256(framed.encode()).hexdigest()))
    delta = datetime(2026, 1, 15, 9, 30) - datetime(1, 1, 1)
    ticks = delta.days * 864000000000 + delta.seconds * 10000000
    base = dict(scope='tenant:tenant-a', nowUtcTicks=helper.Number(str(ticks)), leaseTimeoutTicks=helper.Number('3000000000'),
        heartbeatTimeoutTicks=helper.Number('600000000'), ownerId='owner <é>')
    recovery = []
    purpose = 'ef-runtime-recovery-liveness-v1'
    json_key = hmac.new(b'k' * 32, b'Elsa.RuntimeRecoveryContinuation.JsonHashInput.v1', hashlib.sha256).digest()
    changes = [('base', {}, 0, 1), ('same-UTC-offset', {}, 60, 1), ('different-page-limit', {}, 0, 2),
        ('null-owner', dict(ownerId=None), 0, 1), ('other-scope', dict(scope='tenant:tenant-b'), 0, 1),
        ('one-UTC-tick', dict(nowUtcTicks=helper.Number(str(ticks + 1))), 0, 1),
        ('one-lease-tick', dict(leaseTimeoutTicks=helper.Number('3000000001')), 0, 1),
        ('one-heartbeat-tick', dict(heartbeatTimeoutTicks=helper.Number('600000001')), 0, 1),
        ('other-owner', dict(ownerId='another owner'), 0, 1), ('owner-text-a', dict(ownerId='{}'), 0, 1), ('owner-text-b', dict(ownerId='{ }'), 0, 1)]
    for name, update, offset, limit in changes:
        payload = dict(base, **update)
        canonical = helper.canonical(payload)
        digest = hashlib.sha256(canonical.encode()).hexdigest()
        cursor = dict(Binding=digest, EligibleAtUtcTicks=helper.Number(str(ticks)), WorkflowExecutionId='workflow',
            OperationalStateId='liveness', EligibleAt='2026-01-15T09:30:00+00:00')
        cursor_canonical = helper.canonical(cursor)
        signature = hmac.new(json_key, b'json:' + purpose.encode() + b'.' + cursor_canonical.encode(), hashlib.sha256).digest()
        recovery.append(dict(name=name, scope=payload['scope'], utcTicks=str(payload['nowUtcTicks']),
            leaseTicks=str(payload['leaseTimeoutTicks']), heartbeatTicks=str(payload['heartbeatTimeoutTicks']),
            owner=payload['ownerId'], offsetMinutes=offset, limit=limit, canonical=canonical,
            sha256=digest, eligibleUtcTicks=str(ticks), cursorCanonical=cursor_canonical,
            cursorSignature=base64.urlsafe_b64encode(signature).decode().rstrip('=')))
    return dict(version=1, freeze="JSON hashes freeze at Elsa 4's first stable release; before then regenerate goldens and fixtures together, never add an old-hash verifier.",
        savedViews=views, recoveryBindings=recovery)


if __name__ == '__main__':
    Path(__file__).with_name('nested-binding-hash-vectors.json').write_text(json.dumps(generate(), indent=2, ensure_ascii=True) + '\n')
