"""Independent pins for the Elsa 3 import ledger's JSON content; identity lookup hashes are separate."""
from pathlib import Path
import hashlib
import importlib.util
import json
import struct

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def identity(kind, *fields):
    material = b''.join(struct.pack('>I', len(field.encode())) + field.encode() for field in [kind, *fields])
    return 'elsa3-' + kind + '-' + hashlib.sha256(material).hexdigest()


def wire(value):
    if isinstance(value, helper.Number):
        return str(value)
    if value is None or isinstance(value, (str, bool)):
        return json.dumps(value, ensure_ascii=True)
    if isinstance(value, list):
        return '[' + ','.join(wire(item) for item in value) + ']'
    return '{' + ','.join(json.dumps(key) + ':' + wire(item) for key, item in value.items()) + '}'


def generate():
    scope = dict(tenantId='tenant-a', userId='user-a', tenantScope='tenant:tenant-a')
    collection = dict(collection=dict(handle='handle <é>', accessScope=scope, createdAt='2026-01-15T09:30:00+00:00',
        expiresAt='2026-01-15T10:30:00+00:00', contentLength=helper.Number('1'), collection=dict(collectionId='source <é>', definitions=[])))
    receipt = dict(receipt=dict(receiptId=identity('receipt', scope['tenantScope'], scope['userId'], 'key'), collectionHandle='handle <é>',
        planId='plan', idempotencyKey='key', selectionFingerprint='sha256:selection', accessScope=scope, status=helper.Number('0'),
        completedAt='2026-01-15T09:30:00+00:00', sources=[]))
    cases = [('spelling-a', '{ "OrderID":9007199254740993, "amount":1.0, "text":"\\u0061", "nil":null }'),
        ('spelling-b', '{"nil":null,"text":"a","amount":1e0,"OrderID":9007199254740993}'),
        ('missing', '{}'), ('null', '{"x":null}'), ('forward', '[1,2]'), ('reverse', '[2,1]'),
        ('large-a', '9007199254740993'), ('large-b', '9007199254740992'), ('text-a', '"{}"'), ('text-b', '" { }"')]
    vectors = []
    for kind, payload in [('collection', collection), ('receipt', receipt)]:
        for name, raw in [('writer', None), *cases]:
            material = wire(payload)
            if raw is not None:
                material = material[:-1] + ',"extra":' + raw + '}'
            canonical = helper.canonical(helper.load_json(material))
            vectors.append(dict(name=kind + '-' + name, kind=kind, writer=raw is None, material=material,
                canonical=canonical, sha256=hashlib.sha256(canonical.encode()).hexdigest()))
    return dict(version=1, owner='Elsa3ImportRecordCodec collection/receipt content',
        freeze='FR-021: frozen from Elsa 4 first stable; later recipe changes require the agreed compatibility process.', vectors=vectors)


if __name__ == '__main__':
    Path(__file__).with_name('elsa3-import-content-hash-vectors.json').write_text(json.dumps(generate(), indent=2, ensure_ascii=True) + '\n')
