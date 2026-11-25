"""Independent pins for the Dashboard Weaver question/dataset fingerprint consumer.

The declared owner shapes below are authored from DashboardQueryModels; no production
serializer or hashing code is loaded. Raw JSON controls use the same independent canonical
fixture oracle as the other S-9 owner vectors.
"""
from pathlib import Path
import copy
import hashlib
import importlib.util
import json

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def generate():
    vectors = []

    def add(name, consumer, value=None, raw=None):
        raw = raw if raw is not None else json.dumps(value, ensure_ascii=False, separators=(',', ':'))
        canonical = helper.canonical(helper.load_json(raw))
        vectors.append(dict(name=name, consumer=consumer, json=raw, canonical=canonical,
                            sha256=hashlib.sha256(canonical.encode()).hexdigest()))

    for name, raw in [
        ('spelling-a', '{ "text":"\\u0061", "n":1.0 }'),
        ('spelling-b', '{"n":1e0,"text":"a"}'),
        ('empty', '{}'), ('present-null', '{"x":null}'),
        ('array-forward', '{"x":[1,2]}'), ('array-reverse', '{"x":[2,1]}'),
        ('large-a', '{"x":9007199254740993}'), ('large-b', '{"x":9007199254740992}'),
        ('verbatim-keys', '{"OwnerKey":"Ren\\u00e9","ownerKey":null}')
    ]:
        add(name, 'json', raw=raw)

    question = dict(query=dict(schemaVersion=1, dataset=dict(id='fixture.records', schemaRevision='1'),
        form='records', parameters=[], filters=None, fields=['id', 'amount'], groupBy=[], metrics=[],
        time=None, comparison=None, having=None, sort=[], limit=50),
        visualization=dict(kind='table', x=None, y=[], label=None), parameterValues={})
    add('question', 'question', question)
    changed = copy.deepcopy(question)
    changed['query']['fields'].reverse()
    add('question-field-order', 'question', changed)
    changed = copy.deepcopy(question)
    changed['parameterValues']['Amount'] = dict(type='decimal', literal='1.0', parameter=None)
    add('question-parameter-key', 'question', changed)
    changed['parameterValues']['amount'] = changed['parameterValues'].pop('Amount')
    add('question-other-parameter-key', 'question', changed)
    dataset = dict(id='fixture.records', ownerModuleId='Fixture', schemaRevision='1', title='Records',
        description='Dataset metadata', availability='ready', unavailableReason=None,
        fields=[], metrics=[], relationships=[], time=None, forms=['aggregate', 'records'],
        supportsDrill=False, supportsExport=False,
        coverageDescription='Collection and retention coverage is unknown.')
    add('dataset', 'dataset', dataset)
    dataset['schemaRevision'] = '2'
    add('dataset-other-revision', 'dataset', dataset)
    return vectors


if __name__ == '__main__':
    output = Path(__file__).with_name('dashboard-weaver-hash-vectors.json')
    output.write_text(json.dumps(generate(), ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
