"""Independent pins for RuntimeModelMaterial's opaque-identity hash projection."""
from pathlib import Path
import base64
import hashlib
import importlib.util
import json

helper_path = Path(__file__).with_name('generate-variable-hash-vectors.py')
spec = importlib.util.spec_from_file_location('canonical_fixture', helper_path)
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def opaque(value):
    return base64.b64encode(value.encode('utf-16-le')).decode()


def generate():
    cases = [
        ('spelling-a', '{ "total":1.0, "name":"Ren\\u00e9" }'),
        ('spelling-b', '{"name":"René","total":1e0}'),
        ('empty', '{}'), ('present-null', '{"x":null}'),
        ('array-forward', '{"x":[1,2]}'), ('array-reverse', '{"x":[2,1]}'),
        ('large-a', '{"x":9007199254740993}'), ('large-b', '{"x":9007199254740992}'),
    ]
    vectors = []
    for name, raw in cases:
        projection = dict(domain=opaque('elsa.runtime.model-material/v1'), formatVersion=helper.Number('1'),
                          protocol=dict(key=opaque('test.semantic-model'), version=helper.Number('1')),
                          profile=opaque('test/1'), definition=json.loads(raw, parse_int=helper.Number, parse_float=helper.Number),
                          inputContract=dict(version=helper.Number('1'), inputs=[]), outputs=[], dependencies=[], runtimeRequirements=[])
        preimage = helper.canonical(projection)
        vectors.append(dict(name=name, definition=raw, canonical=preimage,
                            sha256=hashlib.sha256(preimage.encode()).hexdigest()))
    Path(__file__).with_name('model-material-hash-vectors.json').write_text(json.dumps(vectors, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Wrote {len(vectors)} independent model material vectors')


if __name__ == '__main__':
    generate()
