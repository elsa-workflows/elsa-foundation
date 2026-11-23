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
    template_contracts = []
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
        wire = dict(activityTypeKey='test.activity', contractVersion='1', schemaFingerprint='sha256:' + contract_pin['sha256'],
                    descriptorKind='test', descriptorPayload=dict(x=1), inputs={}, outcomes=[], activation=contract['activation'],
                    result=dict(type=contract['result']['Type'], isRequired=False, policy=contract['result']['Policy'], projections={}))
        for name in ['SupportsBackgroundExecution', 'AccessesUndeclaredVariables', 'AccessesWorkflowState']:
            if name in contract: wire[name[0].lower() + name[1:]] = True
        template_contracts.append((mask, wire))
        node_frame = 'node\nnode:test.activity:1:test:1:{"x":1e0}::::contract:sha256:' + contract_pin['sha256']
        behavior = dict(schemaVersion=2, rootNodeId='node', incidentStrategy=dict(alias='Fault', version='1'),
                        nodes=[node], inputContract=dict(version=1, inputs=[]), dependencies=[])
        vectors.append(dict(family='capability', mask=mask, contract=contract_pin, nodeFrame=node_frame,
                            nodeSha256=hashlib.sha256(node_frame.encode()).hexdigest(), **pin(behavior)))

    for key in ['accessesWorkflowState', 'runtime.mayWriteEveryVisibleVariable']:
        for text in [None, 'false', 'true']:
            metadata = {} if text is None else {key: text}
            node = dict(nodeId='node', activityType='test.activity', activityTypeVersion='1',
                        descriptor=dict(consumerKey='test', schemaVersion='1', payload=dict(x=1)),
                        structure=None, inputBindings=[], childSlots=[])
            node_frame = 'node\nnode:test.activity:1:test:1:{"x":1e0}:::'
            if metadata:
                node['behaviorMetadata'] = metadata
                node_frame += ':behaviorMetadata:' + pin(metadata)['canonical']
            behavior = dict(schemaVersion=2, rootNodeId='node', incidentStrategy=dict(alias='Fault', version='1'),
                            nodes=[node], inputContract=dict(version=1, inputs=[]), dependencies=[])
            vectors.append(dict(family='flag', key=key, value=text, nodeFrame=node_frame,
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

    cases = [
        ('effects-a', 'workflowStateEffects', '[ "outputs", "instanceName" ]'),
        ('effects-b', 'workflowStateEffects', '["outputs","instanceName"]'),
        ('effects-order', 'workflowStateEffects', '["instanceName","outputs"]'),
        ('effects-null', 'workflowStateEffects', 'null'),
        ('effects-missing', 'workflowStateEffects', None),
        ('receipts-a', 'workflowStateEffectReceipts', '[{"AttemptId":"Ren\\u00e9","Transition":"Completion","future":9007199254740993}]'),
        ('receipts-b', 'workflowStateEffectReceipts', '[ { "future":9007199254740993,"Transition":"Completion","AttemptId":"René"} ]'),
        ('receipts-number', 'workflowStateEffectReceipts', '[{"AttemptId":"René","Transition":"Completion","future":9007199254740992}]'),
        ('receipts-null', 'workflowStateEffectReceipts', '[{"AttemptId":null}]'),
        ('receipts-missing', 'workflowStateEffectReceipts', '[{}]'),
        ('ordinary-a', 'ordinary', '{}'), ('ordinary-b', 'ordinary', '{ }')]
    for name, key, raw in cases:
        metadata = {opaque('identity\ud800'): opaque('value\ud801')}
        if raw is not None:
            metadata[opaque(key)] = opaque(raw) if key == 'ordinary' else helper.load_json(raw)
        state = dict(execution={key: opaque(value) for key, value in dict(activityExecutionId='activity', workflowExecutionId='workflow',
                     executableNodeId='node', authoredActivityId='authored', activityType='test.activity', activityTypeVersion='1').items()},
                     status='Completed', subStatus=None, executionSequence=1, scheduledAt='1970-01-01T00:00:00+00:00', startedAt=None,
                     completedAt=None, schedulingActivityExecutionId=None, parentActivityExecutionId=None, branchId=None, iterationId=None,
                     provenance=dict(parentActivityExecutionId=None, schedulingActivityExecutionId=None, schedulingWorkflowExecutionId=None,
                                     branchId=None, iterationId=None, executionPathId=None, executionScopeId=None, schedulingCause=None, metadata={}, attempt=None),
                     callStackDepth=None, bookmarkIds=[], incidentIds=[], faultCount=0, aggregateFaultCount=0, metadata=metadata,
                     documentVersion=3, contractIdentity=None, inputSnapshot=None, attempts=None, privateState=None, completion=None, fault=None,
                     triggerRegistrations=None, triggerDeliveries=None, valueFlowCompatibility=None, executionScopeId=None, attempt=None,
                     supersededByActivityExecutionId=None, supersededAt=None, invocationId=opaque('activity'), enclosingExecutionScopeId=None,
                     variableFrame=None, iterationVariableFrame=None, iterationFrameRequest=None)
        changes = dict(workflowExecution=None, scheduler=None,
                       activityExecutions=[dict(stateId=opaque('activity'), operation='Upsert', state=state, metadata={})],
                       activityExecutionInspections=[], bookmarks=[], durableValues=[], incidents=[], operational=[], workflowDispatches=[],
                       workflowDispatchCancellations=[], activityScopeCleanups=[], consumedSchedulerWorkItems=[], alterationJobTerminalChange=None,
                       backgroundActivityWork=dict(admissions=[], acceptances=[], cancellations=[]), postCommitOutbox=[])
        commit = dict(commitId=opaque('commit'), checkpoint=dict(checkpointId=opaque('checkpoint'), name=opaque('test'),
                      workflowExecutionId=opaque('workflow'), occurredAt='1970-01-01T00:00:00+00:00', activityExecutionIds=[], metadata={}),
                      stateChanges=changes, postCommitIntents=[], metadata={})
        vectors.append(dict(family='activity-metadata', name=name, key=key, payload=raw, revision=pin(state), **pin(commit)))

    def template_root(metadata, payload=None, child=None, contract=None):
        parent = child is not None
        return dict(executableNodeId='parent' if parent else 'node', authoredActivityId='parent' if parent else 'authored',
                    activityType='test.container' if parent else 'test.activity', activityTypeVersion='1', descriptorType='test',
                    descriptorSchemaVersion='1', descriptorPayload={} if payload is None else payload, inputBindings={}, outputCaptures={},
                    metadata=metadata, childSlots=[] if child is None else [dict(name='body', activities=[child], operatorSchedulingCapability=None)],
                    structure=None, activityContract=contract, intrinsicKind=None, intrinsicVariable=None)

    def template_behavior(root):
        return dict(schemaVersion='1', root=root, resumeTargets=[], directDependencies=[], closedTemplates=[], runtimeRequirements=[],
                    storageDriverRequirements=[], providerFingerprint='provider', compatibilityMetadata=[])

    for mask, contract in template_contracts:
        root = template_root({}, payload=dict(x=1), contract=contract)
        vectors.append(dict(family='template-capability', mask=mask, **pin(template_behavior(root))))

    template_cases = [
        ('effects-a', 'workflowStateEffects', '{"writesInstanceName":true,"outputNames":["Ren\\u00e9"]}'),
        ('effects-b', 'workflowStateEffects', '{ "outputNames":["René"], "writesInstanceName":true }'),
        ('effects-changed', 'workflowStateEffects', '{"writesInstanceName":false,"outputNames":["René"]}'),
        ('effects-null', 'workflowStateEffects', 'null'), ('effects-missing', 'workflowStateEffects', None),
        ('variables-a', 'runtime.mayWriteVariables', '[{"key":"x","name":"X"}]'),
        ('variables-b', 'runtime.mayWriteVariables', '[ {"name":"X", "key":"x"} ]'),
        ('variables-order-a', 'runtime.mayWriteVariables', '[1,2]'), ('variables-order-b', 'runtime.mayWriteVariables', '[2,1]'),
        ('ordinary-a', 'ordinary', '{}'), ('ordinary-b', 'ordinary', '{ }')]
    for name, key, raw in template_cases:
        metadata = {} if raw is None else {key: raw if key == 'ordinary' else helper.load_json(raw)}
        for scope in ['root', 'child']:
            root = template_root(metadata)
            if scope == 'child': root = template_root({}, child=root)
            vectors.append(dict(family='template-metadata', name=name, key=key, payload=raw, scope=scope, **pin(template_behavior(root))))

    for text in ['{}', '{ }']:
        root = template_root({}, payload=dict(metadata=dict(workflowStateEffects=text)))
        vectors.append(dict(family='template-data-string', payload=text, **pin(template_behavior(root))))
    return vectors


if __name__ == '__main__':
    vectors = generate()
    Path(__file__).with_name('s13-integration-hash-vectors.json').write_text(json.dumps(vectors, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'Wrote {len(vectors)} independent S-13 integration vectors')
