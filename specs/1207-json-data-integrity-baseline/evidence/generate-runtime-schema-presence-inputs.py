"""Independent Runtime fixture preimages after ValueTypeDescriptor.Schema preserves presence.

Only fixture inputs and Python hashlib are used; no CLR serializer or production hasher runs.
The recipes freeze at the first stable Elsa 4 release and then require an identity/migration decision.
"""
from pathlib import Path
import base64
import copy
import hashlib
import importlib.util
import json

DIRECTORY = Path(__file__).resolve().parent
ROOT = DIRECTORY.parents[2]
spec = importlib.util.spec_from_file_location('canonical_fixture', DIRECTORY / 'generate-variable-hash-vectors.py')
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def canonical(value):
    return helper.canonical(helper.load_json(json.dumps(value, ensure_ascii=True, separators=(',', ':'))))


def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def legacy_fixture_preimage(explicit_schema):
    # LegacyWorkflowHashFixture.Load keeps the literal schema:null fields in its source file.
    # The former nullable reader lost them in FormatType; the corrected descriptor reader retains them.
    fixture = json.loads((ROOT / 'essentials/tests/Workflows/Fixtures/legacy-set-v1.json').read_text())
    binding = copy.deepcopy(fixture['rootActivity']['inputBindings']['value'])
    binding['targetType']['alias'] = 'Any'  # ValueTypeDescriptor's Elsa.Any read alias.
    policy = 'Instance:Inline:False:False::[]'
    variable_type = 'Int32:Single::' + ('null' if explicit_schema else '')
    return dict(schemaVersion=2, rootNodeId='set-counter', incidentStrategy=dict(alias='Fault', version='1'),
        workflowVariables=[dict(variableKey='counter', name='Counter', type=variable_type, policy=policy,
            initialBinding='Present:' + variable_type + ':' + policy + ':0')],
        nodes=[dict(nodeId='set-counter', activityType='elsa.intrinsic.set', activityTypeVersion='1.0.0',
            descriptor=dict(consumerKey='intrinsic', schemaVersion='1', payload=dict(kind='Set', schemaVersion='1.0.0')),
            structure=None, inputBindings=[dict(name='value', contract=binding, source='Expression',
                payload=dict(language='JavaScript', expression='7'), metadata=dict(referenceKey='value'))], childSlots=[])],
        inputContract=dict(version=1, inputs=[]), dependencies=[])


def member_binding_preimage(include_absent_schema):
    # MemberReadMaterializationTests.Member(variable, address/street) and the materializer's three
    # projection arrays. Only CLR strings receive its opaque UTF-16 encoding; enum names stay names.
    def opaque(text):
        return base64.b64encode(text.encode('utf-16-le')).decode()
    target = dict(alias=opaque('String'), collectionKind='Single', schemaVersion=None)
    if include_absent_schema:
        target['schema'] = None
    selection = '7#message=' + ':'.join(['sel', '1', 'DeferredFit', '2', '7#address6#street', '-', '', '', '', '-'])
    return dict(bindings=[dict(key=opaque('message'), targetType=target,
        effectivePolicy=dict(lifecycle='Instance', storage='Inline', isSensitive=False, requiresEncryption=False,
            redactionMode=None, retentionPolicy=None, metadata={}), source='VariableMember', literal=None,
        workflowRequest=None, variable=dict(variableKey=opaque('customer'), declaringScopeId=opaque('workflow')),
        activityResult=None, expression=None, metadata=[])], secrets=[], selections=[opaque(selection)])


def generate():
    # Reproducing the previous pins demonstrates the specific old input interpretation rather than
    # treating a newly observed C# hash as an expected value.
    assert digest(legacy_fixture_preimage(False)) == '90c8cd4b830e94d9b305b6cca2477aada5dc673d0d46fcf5cfb7878b9a9ff1fb'
    assert digest(member_binding_preimage(True)) == '4c09af9c24f5035c24ba7c9fa9c083147d0cafb664e6d2c087c8af564bb39bd2'
    return [
        dict(name='runtime-schema-presence/legacy-set-current', json=canonical(legacy_fixture_preimage(True))),
        dict(name='runtime-schema-presence/member-binding', json=canonical(member_binding_preimage(False))),
    ]


if __name__ == '__main__':
    path = DIRECTORY / 'compiler-hash-inputs.json'
    rows = json.loads(path.read_text())
    # The compiler constructs these four default String descriptors with absent Schema. A plan's
    # fingerprint writer still explicitly writes schema:null, so its fingerprint is left unchanged.
    declared = next(row for row in rows if row['name'] == 'declared-input-contract')
    material = helper.load_json(declared['json'])
    contract = material['nodes'][0]['inputBindings'][0]['contract']
    for descriptor in [contract['targetType'], contract['literal']['type'],
                       contract['conversionPlan']['sourceType'], contract['conversionPlan']['targetType']]:
        assert descriptor.get('schema') is None
        descriptor.pop('schema', None)
    declared['json'] = helper.canonical(material)
    rows = [row for row in rows if not row['name'].startswith('runtime-schema-presence/')]
    rows.extend(generate())
    path.write_text(json.dumps(rows, ensure_ascii=True, indent=2) + '\n', encoding='utf-8')
    print('Prepared two Runtime presence preimages; corrected four absent compiler descriptor fields.')
