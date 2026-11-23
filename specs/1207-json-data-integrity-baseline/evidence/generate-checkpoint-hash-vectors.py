"""Independent JSON and component-framed checkpoint input/intent preimages."""
from pathlib import Path
import base64
import datetime
import hashlib
import importlib.util
import json
import struct

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def opaque(value):
    return base64.b64encode(value.encode('utf-16-le', errors='surrogatepass')).decode()


def frame_component(value):
    return bytes([0 if value is None else 1]) + struct.pack('>i', 0 if value is None else len(value)) + (value or b'')


def generate():
    vectors = []
    cases = [('spelling-a', '{ "total":1.0,"name":"Ren\\u00e9" }'),
             ('spelling-b', '{"name":"René","total":1e0}'),
             ('empty', '{}'), ('present-null', '{"x":null}'),
             ('array-forward', '{"x":[1,2]}'), ('array-reverse', '{"x":[2,1]}'),
             ('large-a', '{"x":9007199254740993}'), ('large-b', '{"x":9007199254740992}')]
    elapsed = datetime.datetime(2026, 10, 26) - datetime.datetime(1, 1, 1)
    ticks = elapsed.days * 86400 * 10_000_000
    for name, raw in [*cases, ('absent', None), ('explicit-null', 'null')]:
        value = None if raw is None else helper.load_json(raw)
        strings = ['elsa.post-commit.intent.v1', 'send', 'publisher', 'publish', str(ticks), 'activity', 'key', None, None, None]
        frame = b''.join(frame_component(None if item is None else item.encode('utf-16-le')) for item in strings)
        frame += frame_component(None if raw is None else helper.canonical(value).encode('utf-8'))
        frame += b''.join(frame_component(item.encode('utf-16-le')) for item in ['1', 'routing', 'saved'])
        vectors.append(dict(family='intent', name=name, payload=raw, frameHex=frame.hex(),
                            sha256=hashlib.sha256(frame).hexdigest()))
        if raw in (None, 'null'):
            continue
        pin = {key: opaque(text) for key, text in dict(artifactId='artifact', definitionId='definition',
               definitionVersionId='version', artifactVersion='1.0.0', artifactHash='hash').items()}
        command = dict(executable=pin, materialIncarnationId=opaque('incarnation'), inputId=opaque('input'),
                       inputKind=opaque('start'), payload=value, tenantId=opaque('tenant-a'),
                       authority=dict(systemIdentity=opaque('system'), rootInitiator=opaque('initiator'),
                                      metadata={opaque('tenant'): opaque('tenant-a')}))
        preimage = helper.canonical(dict(format=opaque('elsa.model.input.v1'), command=command))
        vectors.append(dict(family='model-input', name=name, payload=raw, canonical=preimage,
                            sha256=hashlib.sha256(preimage.encode()).hexdigest()))
    Path(__file__).with_name('checkpoint-hash-vectors.json').write_text(json.dumps(vectors, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Wrote {len(vectors)} independent checkpoint vectors')


if __name__ == '__main__':
    generate()
