"""Independent Publishing fixture recipes for absent ValueTypeDescriptor.Schema.

The old pins are reproduced before removing only default-constructed descriptor
schemas. No Elsa binary, serializer, observed hash or production hasher is loaded.
These pre-stable identities freeze at Elsa 4's first stable release.
"""
from pathlib import Path
import argparse
import base64
import copy
import hashlib
import importlib.util
import json

DIRECTORY = Path(__file__).resolve().parent
ROOT = DIRECTORY.parents[2]
FIXTURES = ROOT / 'essentials/tests/Workflows/Publishing/Api/Tests/Fixtures/CompilerGoldens'
spec = importlib.util.spec_from_file_location('canonical_fixture', DIRECTORY / 'generate-variable-hash-vectors.py')
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def canonical(value):
    return helper.canonical(helper.load_json(json.dumps(value, ensure_ascii=True, separators=(',', ':'))))


def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def descriptor(alias='String', old=False):
    result = dict(alias=alias, collectionKind='Single', schemaVersion=None)
    if old:
        result['schema'] = None
    return result


def policy(sensitive=False):
    return dict(lifecycle='Instance', storage='Inline', isSensitive=sensitive, requiresEncryption=sensitive,
                redactionMode=None, retentionPolicy=None, metadata={})


def plan(old=False):
    # ValueConversionPlan's dedicated fingerprint writer explicitly writes null
    # schemas in both cases; only its serialized source/target descriptors change.
    return dict(schemaVersion=1, mode='Auto', operation='Identity', sourceType=descriptor(old=old),
                targetType=descriptor(old=old), sourceRepresentation='TextValue', profile=None,
                limits=dict(maxCollectionItems=10000, maxDepth=64, maxPayloadBytes=1048576), options={},
                fingerprint='sha256:396d0ad07cbbf5b8074a8be3c6163e4db086b2b9445730c7d71794be59b22736')


def expression(text, result=None, parameters=None):
    return dict(language='JavaScript', expression=text, resultType=result, metadata={}, parameters=parameters or {},
                parameterConversionPlans={}, options={}, capabilityProfile='binding-readonly-v1')


def binding(name, source, old=False, **payload):
    result = dict(inputName=name, inputKey=name, targetType=descriptor(old=old), effectivePolicy=policy(),
                  source=source, literal=None, workflowRequest=None, variable=None, activityResult=None,
                  literalValue=None, expression=None, metadata={})
    result.update(payload)
    if source == 'Literal':
        value = result['literalValue']
        result['literal'] = dict(type=descriptor(old=old), presence='ExplicitNull' if value is None else 'Present',
                                 inlineValue=value, externalReference=None, policy=policy())
    return result


def write_binding(name, contract):
    payload = contract['literalValue']
    if contract['source'] == 'Expression':
        payload = {key: contract['expression'][key] for key in ['language', 'expression']}
    if contract['source'] == 'SecretRead':
        secret = contract['secret']
        payload = dict(secretName=secret['name'], secretTypeName=secret.get('typeName'), secretScope=secret.get('scope'))
    result = dict(name=name, contract=contract, source=contract['source'], payload=payload, metadata=contract['metadata'])
    if 'conversionPlan' in contract:
        result['conversionPlanFingerprint'] = contract['conversionPlan']['fingerprint']
    return result


def compiler_material(name, old=False):
    # CanonicalSnapshot intentionally omits typed binding contracts. Reconstruct
    # those from the corpus constructors, catalog type String and compiler rules.
    snapshot = json.loads((FIXTURES / (name + '.json')).read_text())
    nodes = []

    def visit(source):
        structure = copy.deepcopy(source['structure'])
        if structure and name == 'sequence-with-container-variables':
            # Sequence authored variable and its literal/identity-plan descriptors
            # were constructed without schema. Their raw compiled structure keeps
            # its numeric enums; only the five schema fields are omitted.
            variable = structure['payload']['variables'][0]
            initial = variable['initialBinding']
            for item in [variable['type'], initial['targetType'], initial['literal']['type'],
                         initial['conversionPlan']['sourceType'], initial['conversionPlan']['targetType']]:
                assert item.get('schema') is None
                if old:
                    item['schema'] = None
                else:
                    item.pop('schema', None)
        node = dict(nodeId=source['executableNodeId'], activityType=source['activityType'],
                    activityTypeVersion=source['activityTypeVersion'], sideEffectProfile='External',
                    descriptor=source['descriptor'], structure=structure, inputBindings=[], childSlots=[])
        # StructuralActivity declares IActivityResult<ActivityUnit>; Sequence is ReplaySafe.
        if source['activityType'] == 'Elsa.Activities.Sequence.Activities.Sequence':
            node['sideEffectProfile'] = 'ReplaySafe'
        for entry in source['inputBindings']:
            if entry['source'] == 'Literal':
                contract = binding(entry['key'], 'Literal', old, literalValue=entry['literalValue'])
                # Authored literals use an identity TextValue plan; omitted Text does not.
                if entry['literalValue'] is not None:
                    contract['conversionPlan'] = plan(old)
                else:
                    # Unbound optional Text is Absent, not an authored null/default.
                    contract['literal']['presence'] = 'Absent'
            else:
                assert entry['source'] == 'Expression'
                details = entry['expression']
                contract = binding(entry['key'], 'Expression', old,
                    expression=expression(details['expression'], dict(kind='alias', id='String', schema=None)))
            contract['metadata'] = {item['Key']: item['Value'] for item in entry['metadata']}
            node['inputBindings'].append(write_binding(entry['key'], contract))
        for slot in source['childSlots']:
            node['childSlots'].append(dict(name=slot['name'], nodeIds=sorted(child['executableNodeId'] for child in slot['activities'])))
            for child in slot['activities']:
                visit(child)
        nodes.append(node)

    visit(snapshot['rootActivity'])
    contract = snapshot['inputContract'] or dict(version=1, inputs=[])
    inputs = []
    for item in contract['inputs']:
        entry = dict(name=item['name'], typeAlias=item['type']['alias'], collectionKind=item['type']['collectionKind'],
                     isRequired=item['isRequired'], hasDefaultValue=item['defaultValue'] is not None,
                     defaultValue=item['defaultValue'])
        if not item.get('isNullable', False):
            entry['isNullable'] = False
        inputs.append(entry)
    return dict(schemaVersion=3 if inputs else 2, rootNodeId=snapshot['rootActivity']['executableNodeId'],
                incidentStrategy=dict(alias='Fault', version='1'), nodes=sorted(nodes, key=lambda n:n['nodeId']),
                inputContract=dict(version=contract['version'], inputs=inputs), dependencies=snapshot['dependencies'])


def whole_bindings(old=False):
    result = dict(
        literal=binding('literal', 'Literal', old, literalValue='hello'),
        request=binding('request', 'WorkflowRequest', old, workflowRequest=dict(memberKey='customer', path='customer.id')),
        variable=binding('variable', 'VariableRead', old, variable=dict(variableKey='customer', declaringScopeId='workflow')),
        result=binding('result', 'ActivityResult', old, activityResult=dict(producerExecutableNodeId='producer', projectionKey='customer', isOptional=False), conversionPlan=plan(old)),
        expression=binding('expression', 'Expression', old, expression=expression('args.p', parameters={
            'p':dict(kind='ActivityResult', producerNodeId='producer', projectionKey='$result', isOptional=False)})),
        secret=binding('secret', 'SecretRead', old, secret=dict(name='api-key', typeName='text'), effectivePolicy=policy(True)))
    result['scoped-result'] = binding('scoped-result', 'ActivityResult', old, activityResult=dict(
        producerExecutableNodeId='producer', projectionKey='$result', producerScopeId='scope:root', isOptional=True))
    return result


def template_material(old=False, scoped=False):
    bindings = whole_bindings(old)
    if scoped:
        contract = binding('customer-id', 'ActivityResult', old, activityResult=dict(
            producerExecutableNodeId='node-fetch', projectionKey='customer-id', producerScopeId='scope-root', isOptional=True))
        contract['targetType'] = descriptor('System.String', old)
        bindings = {'customer-id': contract}
    node = dict(executableNodeId='node-consumer', authoredActivityId='consumer', activityType='Tests.Consumer',
                activityTypeVersion='1.0.0', descriptorType='clr', descriptorSchemaVersion='1',
                descriptorPayload=dict(typeAlias='Tests.Consumer'), inputBindings=bindings, outputCaptures={},
                metadata={}, childSlots=[], structure=None, activityContract=None, intrinsicKind=None, intrinsicVariable=None)
    return dict(schemaVersion='1', root=node, resumeTargets=[], directDependencies=[], closedTemplates=[],
                runtimeRequirements=[], storageDriverRequirements=[], providerFingerprint='provider', compatibilityMetadata=[])


def whole_behavior(old=False):
    node = dict(nodeId='node-consumer', activityType='Tests.Consumer', activityTypeVersion='1.0.0',
                descriptor=dict(consumerKey='clr', schemaVersion='1', payload=dict(typeAlias='Tests.Consumer')),
                structure=None, inputBindings=[write_binding(key,value) for key,value in sorted(whole_bindings(old).items())], childSlots=[])
    return dict(schemaVersion=2, rootNodeId='node-consumer', incidentStrategy=dict(alias='Fault', version='1'),
                nodes=[node], inputContract=dict(version=1, inputs=[]), dependencies=[])


def opaque(text):
    return base64.b64encode(text.encode('utf-16-le')).decode()


def whole_fingerprint(old=False):
    # Mirror declared CLR types, not JSON text: raw inlineValue and polymorphic
    # discriminator stay JSON; CLR strings and dictionary keys are opaque UTF-16.
    def project(value, key=None):
        if isinstance(value, str):
            return value if key in ['collectionKind','lifecycle','storage','source','presence','kind'] else opaque(value)
        if isinstance(value, list):
            return [project(item) for item in value]
        if isinstance(value, dict):
            result = {}
            for name, item in value.items():
                if name in ['inlineValue', 'options']:
                    result[name] = item
                elif name in ['metadata','parameters','parameterConversionPlans'] and isinstance(item,dict):
                    result[name] = {opaque(k):project(v,k) for k,v in item.items()}
                else:
                    result[name] = project(item,name)
            return result
        return value
    bindings = []
    for key, value in sorted(whole_bindings(old).items()):
        entry = {name:value[name] for name in ['targetType','effectivePolicy','source','literal','workflowRequest','variable','activityResult','expression']}
        entry['key'] = key
        entry['metadata'] = []
        bindings.append(project(entry))
    return dict(bindings=bindings, secrets=[dict(key=opaque('secret'), secret=project(dict(name='api-key',typeName='text')))], selections=[])


def activity_contract(old=False, activity_type='Elsa.Workflows.Publishing.Api.Tests.TestWriteLineActivity'):
    clr = 'Elsa.Primitives.Models.ClrActivityDescriptor'
    value_policy = dict(isPersistable=True, isSensitive=False, requiresEncryption=False, redactionMode=None,
                        lifecycle='Instance', storage='Inline', storageProfile=None, retentionPolicy=None)
    result_policy = dict(value_policy, lifecycle='Result')
    return dict(activityTypeKey=activity_type, contractVersion='1.0.0', descriptorKind=clr,
                descriptorPayload=dict(typeAlias='Elsa.Workflows.Publishing.Api.Tests.TestWriteLineActivity'),
                inputs=[dict(Key='Text', Type=descriptor(old=old), IsRequired=False, IsNullable=True,
                             HasDefault=False, DefaultValue=None, Policy=value_policy)],
                result=dict(Type=descriptor('Elsa.Activities.Runtime.Core.Models.ActivityUnit',old),
                            IsRequired=True, Policy=result_policy, projections=[], SourceRepresentation='TypedValue'),
                outcomes=['Done'], activation=dict(descriptorKind=clr, capability='Elsa.Workflows.Publishing.Api.Tests.TestWriteLineActivity'))


def fixture_behavior(old=False, literal=False):
    # SecretBindingCompilerFixture's reflected CLR catalog names the activity by
    # its full alias, unlike the compiler corpus catalog's Test.WriteLine key.
    model = compiler_material('literal-input',old)
    node = model['nodes'][0]
    model['rootNodeId'] = node['nodeId'] = 'secret-node'
    node['activityType'] = 'Elsa.Workflows.Publishing.Api.Tests.TestWriteLineActivity'
    contract = node['inputBindings'][0]['contract']
    if literal:
        contract['literalValue'] = contract['literal']['inlineValue'] = 'hello'
    else:
        contract['literalValue'] = contract['literal']['inlineValue'] = None
        contract['literal']['presence'] = 'Absent'
        contract.pop('conversionPlan')
    node['inputBindings'] = [write_binding('Text',contract)]
    return model


def legacy_frame(old=False):
    # FormatNode's existing string frame includes the pinned activity contract
    # even though full behavioral hashes write its side-effect declaration only.
    fingerprint = digest(activity_contract(old, 'Test.WriteLine'))
    binding_frame = 'Text:String:Single:::Instance:Inline:False:False::[]=Present:String:Single:::Instance:Inline:False:False::[]:"Hello World!"[referenceKey=Text]:conversion=sha256:396d0ad07cbbf5b8074a8be3c6163e4db086b2b9445730c7d71794be59b22736'
    return 'write-one\nwrite-one:Test.WriteLine:1.0.0:elsa.clr-activity:1:{"typeAlias":"Elsa.Workflows.Publishing.Api.Tests.TestWriteLineActivity"}::' + binding_frame + '::contract:sha256:' + fingerprint


def generate():
    result = []
    def add(name, recipe, previous):
        before = recipe(True)
        actual = digest(before)
        assert actual == previous.removeprefix('sha256:'), (name, 'old-pin', actual, previous)
        current = canonical(recipe(False))
        result.append(dict(name=name, previousSha256=previous.removeprefix('sha256:'),
                           canonical=current, sha256=hashlib.sha256(current.encode()).hexdigest()))
    for name, previous in {
        'literal-input':'e61bec8961bdad4547982aeed74f8a2989a2a72329385762ec072292cc4ad581',
        'declared-input-contract':'4948a61b62baa38edb917a7c5eb4f35c75b85a08f1ba58c732af3d06e7b35e56',
        'dependency-set':'4597e710e822dd42f747817f26a81a0d135fd3c3df1bf4d72f4e55bc6ce9f8c9',
        'javascript-expression':'926d48f598690df2b746157df3c37cdd7aadcbde98c17f8f87a3215ff0f19c04',
        'nested-child-slots':'d2317674ac5fd069897e35fc34c56eae766b3f1d81dffdb25ec43c992ea0f74a',
        'sequence-with-container-variables':'a1d202673959f4189e4a3b2d9a653e837662cd40202411d55dccdad1183d1035',
    }.items():
        add(name, lambda old:compiler_material(name,old), previous)
    add('whole-executable',whole_behavior,'7d92bfc3137b805dc75dcc434370eb2023b9c0de3421e178e15aea959da07e18')
    add('whole-template',template_material,'fa20530c5493f4159134c46b584ff782ae66fbfca47c81a088aa055272a9e993')
    add('scoped-template',lambda old:template_material(old,True),'5ecbae548b89c178896e9a3d6d15bc4d638b89893ededed38803bfa2a3ec536e')
    add('whole-binding',whole_fingerprint,'7dc805658de8eec17062f5fde590a520010694e86a71d84f14653ae22ec3489e')
    add('reflected-activity-contract',activity_contract,'f224a242d3e87a95da6890a5a32cc6bfe00830f19225becfb6d4d531d3145838')
    add('reflected-unbound',fixture_behavior,'74adb9b8800e57c21cd8e70a18649246cc87a488773d0b65131f59060d0f926c')
    add('reflected-literal',lambda old:fixture_behavior(old,True),'0ebcb066839829c092c8cbedcca35debcf6661eb92b260ff11c98a81645f8fd7')
    previous = '4d925e771b736a04daa45efcc6488182d8c6c855b61a2221cfe0d335a89f6e0c'
    assert hashlib.sha256(legacy_frame(True).encode()).hexdigest() == previous
    frame = legacy_frame(False)
    result.append(dict(name='legacy-null-compatibility',previousSha256=previous,frame=frame,
                       sha256=hashlib.sha256(frame.encode()).hexdigest()))
    return result


def install(rows):
    by_name = {row['name']:row for row in rows}
    for name in ['literal-input','declared-input-contract','dependency-set','javascript-expression',
                 'nested-child-slots','sequence-with-container-variables','legacy-null-compatibility']:
        path = FIXTURES / (name + '.json')
        text = path.read_text(encoding='utf-8')
        row = by_name[name]
        text = text.replace('sha256:' + row['previousSha256'], 'sha256:' + row['sha256'])
        text = text.replace('artifact-' + row['previousSha256'][:12], 'artifact-' + row['sha256'][:12])
        if name == 'sequence-with-container-variables':
            # These five lines are exactly the constructed descriptors listed in
            # compiler_material; preserve every other raw structure field/byte.
            lines = text.splitlines(keepends=True)
            omitted = [line for line in lines if line.strip() == '"schema": null,']
            assert len(omitted) in [0,5]
            text = ''.join(line for line in lines if line.strip() != '"schema": null,')
        path.write_text(text, encoding='utf-8', newline='\n')
    pins = {
        'WholeValueBindingHashTests.cs':['whole-executable','whole-template','whole-binding'],
        'ScopedActivityResultHashTests.cs':['scoped-template'],
        'WorkflowExecutableCompilerTests.cs':['reflected-activity-contract','reflected-unbound','reflected-literal'],
    }
    for file,names in pins.items():
        path = FIXTURES.parents[1] / file
        text = path.read_text(encoding='utf-8')
        for name in names:
            row = by_name[name]
            old, current = 'sha256:' + row['previousSha256'], 'sha256:' + row['sha256']
            assert text.count(old) == 1 or text.count(current) == 1, (file,name)
            text = text.replace(old,current)
        path.write_text(text, encoding='utf-8', newline='\n')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--install',action='store_true',help='Install independently derived fixture identities and C# pins.')
    args = parser.parse_args()
    rows = generate()
    (DIRECTORY / 'publishing-schema-presence-vectors.json').write_text(
        json.dumps(rows,ensure_ascii=True,indent=2) + '\n', encoding='utf-8')
    if args.install:
        install(rows)
    print('Reproduced 14 prior pins; independently derived 14 Publishing presence vectors.')
