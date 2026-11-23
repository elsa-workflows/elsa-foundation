"""Independent owner preimages for S-13/S-9 integration. No Elsa implementation is invoked.

These hash recipes freeze at Elsa 4's first stable release. Regeneration is evidence,
not permission to change identity without an explicit migration decision.
"""
from pathlib import Path
import base64
import hashlib
import importlib.util
import json

spec = importlib.util.spec_from_file_location('canonical_fixture', Path(__file__).with_name('generate-variable-hash-vectors.py'))
helper = importlib.util.module_from_spec(spec)
spec.loader.exec_module(helper)


def opaque(text):
    return base64.b64encode(text.encode('utf-16-le', errors='surrogatepass')).decode()


def pin(value):
    def numbers(item):
        if isinstance(item, bool): return item
        if isinstance(item, int): return helper.Number(str(item))
        if isinstance(item, list): return [numbers(child) for child in item]
        if isinstance(item, dict): return {key: numbers(child) for key, child in item.items()}
        return item
    canonical = helper.canonical(numbers(value))
    return dict(canonical=canonical, sha256=hashlib.sha256(canonical.encode()).hexdigest())


def generate():
    vectors = []
    for mask in range(8):
        contract = dict(activityTypeKey='test.activity', contractVersion='1', descriptorKind='test',
                        descriptorPayload=dict(x=1), inputs=[], outcomes=[],
                        activation=dict(descriptorKind='test', capability='execute'),
                        result=dict(Type=dict(alias='Int32', collectionKind='Single', schema=None, schemaVersion=None),
                                    IsRequired=False, Policy=dict(isPersistable=True, isSensitive=False, requiresEncryption=False,
                                    redactionMode=None, lifecycle='Instance', storage='Inline', storageProfile=None, retentionPolicy=None), projections=[]))
        node = dict(nodeId='node', activityType='test.activity', activityTypeVersion='1', sideEffectProfile='External',
                    descriptor=dict(consumerKey='test', schemaVersion='1', payload=dict(x=1)),
                    structure=None, inputBindings=[], childSlots=[])
        for bit, name in enumerate(['SupportsBackgroundExecution', 'AccessesUndeclaredVariables', 'AccessesWorkflowState']):
            if mask & (1 << bit):
                contract[name] = True
                node[name[0].lower() + name[1:]] = True
        contract_pin = pin(contract)
        node_frame = 'node\nnode:test.activity:1:test:1:{"x":1e0}::::contract:sha256:' + contract_pin['sha256']
        behavior = dict(schemaVersion=2, rootNodeId='node', incidentStrategy=dict(alias='Fault', version='1'),
                        nodes=[node], inputContract=dict(version=1, inputs=[]), dependencies=[])
        vectors.append(dict(family='capability', mask=mask, contract=contract_pin, nodeFrame=node_frame,
                            nodeSha256=hashlib.sha256(node_frame.encode()).hexdigest(), **pin(behavior)))

    cases = [('spelling-a', '{ "total":1.0,"name":"Ren\\u00e9" }'),
             ('spelling-b', '{"name":"René","total":1e0}'),
             ('empty', '{}'), ('present-null', '{"x":null}'),
             ('array-forward', '{"x":[1,2]}'), ('array-reverse', '{"x":[2,1]}'),
             ('large-a', '{"x":9007199254740993}'), ('large-b', '{"x":9007199254740992}'),
             ('inline-null', 'null'), ('none', None), ('external', None),
             ('opaque-a', 'null'), ('opaque-b', 'null')]
    for name, raw in cases:
        external = name == 'external'
        metadata = {opaque('tenant\ud800'): opaque('value\ud800' if name == 'opaque-a' else 'value\ud801')} if name.startswith('opaque-') else {}
        value = dict(durableValueId=opaque('durable'), workflowExecutionId=opaque('workflow'), valueId=opaque('output:Value'),
                     type=dict(kind=opaque('Any'), id=opaque('Single'), schema=None),
                     lifecycle='None' if name == 'none' else 'Instance',
                     storage='None' if name == 'none' else 'External' if external else 'Inline',
                     inlineValue=None if raw is None else helper.load_json(raw),
                     externalReference=dict(storageProfile=opaque('store'), locator=opaque('locator'), metadata={opaque('region'): opaque('west')}) if external else None,
                     sourceActivityExecutionId=None, capturedAt='1970-01-01T00:00:00+00:00', metadata=metadata)
        changes = dict(workflowExecution=None, scheduler=None, activityExecutions=[], activityExecutionInspections=[], bookmarks=[],
                       durableValues=[dict(stateId=opaque('durable'), operation='Upsert', state=value, metadata={})],
                       incidents=[], operational=[], workflowDispatches=[], workflowDispatchCancellations=[], activityScopeCleanups=[],
                       consumedSchedulerWorkItems=[], alterationJobTerminalChange=None,
                       backgroundActivityWork=dict(admissions=[], acceptances=[], cancellations=[]), postCommitOutbox=[])
        commit = dict(commitId=opaque('commit'), checkpoint=dict(checkpointId=opaque('checkpoint'), name=opaque('test'),
                      workflowExecutionId=opaque('workflow'), occurredAt='1970-01-01T00:00:00+00:00', activityExecutionIds=[], metadata={}),
                      stateChanges=changes, postCommitIntents=[], metadata={})
        vectors.append(dict(family='durable-checkpoint', name=name, payload=raw, **pin(commit)))
    Path(__file__).with_name('s13-integration-hash-vectors.json').write_text(json.dumps(vectors, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Wrote {len(vectors)} independent S-13 integration vectors')


if __name__ == '__main__':
    generate()
