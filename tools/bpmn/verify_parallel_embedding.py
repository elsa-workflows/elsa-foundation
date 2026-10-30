#!/usr/bin/env python3
"""Verify actual paired direct/native partial-fork recovery, including physical stores and one permitted normalization."""
from copy import deepcopy
import argparse
import importlib.util
import json
from pathlib import Path
import re

spec = importlib.util.spec_from_file_location('runbpmn_recovery', Path(__file__).with_name('verify_runbpmn_recovery.py'))
v = importlib.util.module_from_spec(spec); spec.loader.exec_module(v); q = v.q
PARENTS = ('Root', 'Sequence', 'Flowchart', 'CompensationScope')
PHASES = ('bothWaiting', 'partial', 'recovered', 'joined', 'final', 'retained')


def require(condition, message):
    if not condition: raise ValueError(message)


def semantic_state(model, case):
    state = deepcopy(model['payload']['state']); identity = model['executionId']
    known = [*case['initial'][identity]]
    if identity in case['afterJoin']: known.append(case['afterJoin'][identity])
    handles = {w['invocationId']: '$human:' + json.dumps([w[k] for k in ('elementId', 'bindingId', 'tokenId', 'iterationId')], separators=(',', ':')) for w in known}
    require(len(handles) == len(known), 'The observed local human-work ledger repeats an invocation.')
    for token in state['tokens']:
        handle = token['producingWorkHandle']
        if handle == identity: token['producingWorkHandle'] = '$model'
        elif handle in handles: token['producingWorkHandle'] = handles[handle]
    return state


def verify(path):
    manifest = json.loads(path.read_text()); root = path.parent; source = manifest.get('sourceRevision', '')
    require(manifest.get('schemaVersion') == 1 and manifest.get('kind') == 'actual-direct-embedded-partial-fork-recovery' and re.fullmatch('[0-9a-f]{40}', source),
        'The actual partial-fork manifest lacks its declared kind or exact source.')
    names = {'case.json'} | {phase + '-checked.json' for phase in PHASES} | {phase + '-' + store + '.sqlite.gz' for phase in ('partial', 'final', 'retained') for store in ('runtime', 'design')}
    expected = {'parallel-' + parent.lower() + '/' + name for parent in PARENTS for name in names}
    assets = manifest['assets']; declared = {a['path'] for a in assets}
    require(len(assets) == len(declared) and declared == expected, 'The actual partial-fork bundle lacks its complete four-parent assets.')
    for asset in assets: require(q.digest(q.evidence_file(root, asset['path'])) == asset['sha256'], 'An actual partial-fork asset differs from its declared hash.')
    for parent in PARENTS:
        directory = 'parallel-' + parent.lower()
        read = lambda name: json.loads(q.evidence_file(root, directory + '/' + name).read_text())
        case = read('case.json'); stages = {phase: read(phase + '-checked.json') for phase in PHASES}
        require(case['schemaVersion'] == 1 and case['kind'] == manifest['kind'] and case['sourceRevision'] == source and case['parent'] == parent and
            len(set(case['processes'])) == len(case['processes']) == 3 and case['exits'] == [137, 137, 137] and
            case['termination'] == '.NET Process.Kill(entireProcessTree: true)' and case['executionStateEdited'] is case['claimEdited'] is case['clockEdited'] is False,
            'The actual partial fork lacks its three owned process losses or unchanged-state provenance.')
        owned = case['ownedProcesses']
        require(len(owned) == 3 and [p['processId'] for p in owned] == case['processes'] and
            len({p['controllerProcessId'] for p in owned}) == len({p['contentRoot'] for p in owned}) == 1 and
            all(p['role'] == 'standard' and Path(p['executable']).name == 'dotnet' and p['controllerProcessId'] > 0 and v.timestamp(p['startedAtUtc']) for p in owned),
            'The actual partial-fork processes lost their original owner, start, executable or configured root.')
        products = case['binaries']
        require(len(products) == 11 and {p['file'] for p in products} == v.PRODUCTS and
            all(p['productVersion'].endswith('+' + source) for p in products if p['file'].startswith('Elsa.')) and
            all(re.fullmatch('[0-9a-f]{64}', p['sha256']) for p in products) and
            all(p['productVersion'].startswith('0.5.0+') for p in products if p['file'].startswith('Bpmn.')),
            'The partial-fork host/publishing/library products lack their exact-source identities.')
        first = stages['bothWaiting']; original = first['model']; direct = first['directCounterpart']
        require(original['executionId'] == case['modelExecutionId'] and direct['executionId'] == case['directCounterpartId'] and
            original['executionId'] != direct['executionId'] and original['executable'] == direct['executable'] == case['published']['executable'] and
            original['materialIncarnationId'] == direct['materialIncarnationId'] == case['published']['incarnationId'],
            'The direct and embedded partial forks did not execute the same immutable model material.')
        origin = v.invocation(original['payload']['nativeParent']['invocation'])
        require(origin['executionId'] == original['executionId'] and origin['nativeExecutionId'] == case['nativeExecutionId'] and
            origin['nativeExecutable'] == v.pin(case['publication']) == first['nativeParent']['pinnedExecutable'],
            'The original native parent lost its exact model invocation or complete publication pin.')
        for phase, checked in stages.items():
            model = checked['model']; counterpart = checked['directCounterpart']
            require(checked['readOnly'] is True and model['executionId'] == original['executionId'] and counterpart['executionId'] == direct['executionId'] and
                model['executable'] == counterpart['executable'] == original['executable'] and
                model['materialIncarnationId'] == counterpart['materialIncarnationId'] == original['materialIncarnationId'] and
                model['payload']['nativeParent'] == original['payload']['nativeParent'] and
                checked['nativeParent']['pinnedExecutable'] == first['nativeParent']['pinnedExecutable'],
                'A partial-fork stage changed its actual model/native identity, incarnation or parent address.')
            require(semantic_state(model, case) == semantic_state(counterpart, case), 'The actual direct/embedded released-interpreter traces differ after the permitted model-local producer-handle normalization.')
            revision, elements = (1, {'left', 'right'}) if phase == 'bothWaiting' else (2, {'right'}) if phase in ('partial', 'recovered') else (3, {'after-join'}) if phase == 'joined' else (5, set())
            require(model['revision'] == revision and counterpart['revision'] == (4 if phase in ('final', 'retained') else revision) and
                {w['elementId'] for w in model['payload']['work']} == {w['elementId'] for w in counterpart['payload']['work']} == elements and
                len(model['payload']['work']) == len(counterpart['payload']['work']) == len(elements),
                'The actual partial fork lost its accepted branch, exact single join or expected semantic revision.')
            require(len(checked['inputs']) == revision and sorted(x['acceptedRevision'] for x in checked['inputs']) == list(range(1, revision + 1)),
                'The actual partial fork lost an exact accepted-input receipt or repeated a logical decision.')
        require(stages['partial']['model'] == stages['recovered']['model'] and stages['partial']['directCounterpart'] == stages['recovered']['directCounterpart'],
            'The fresh process changed an already accepted partial branch.')
        require(stages['final'] == stages['retained'], 'A further cold process changed the settled parallel models or native return.')
        final = stages['final']; settled = next(i for i in final['inputs'] if i['inputId'] == origin['settledInputId'])
        require(final['model']['lifecycle'] == final['directCounterpart']['lifecycle'] == 3 and
            final['model']['terminalResult'] == final['directCounterpart']['terminalResult'] == {'status': 'Completed', 'outcome': 'Done'} and
            final['nativeParent']['status'] == 3 and not final['bookmarks'] and len(final['activities']) == (1 if parent == 'Root' else 3) and
            all(a['status'] == 4 for a in final['activities']) and len(final['sources']) == 2 and all(s['status'] == 2 for s in final['sources']) and
            settled['acceptedRevision'] == 5 and settled['invocationId'] == origin['nativeInvocationId'] and settled['attemptId'] == origin['nativeAttemptId'],
            'The actual native parallel parent did not consume and settle its exact result once.')
        source_row = next(s for s in final['sources'] if s['intent']['kind'] == v.RESULT)
        require(v.timestamp(source_row['deliveredAt']) >= v.timestamp(settled['acceptedAt']), 'The parent result was acknowledged before exact model settlement.')
        for identity in (original['executionId'], direct['executionId']):
            for stage in ('left-replay', 'right-replay', 'late-right-replay'):
                command = case['commands'][identity + ':' + stage]
                original_command = case['commands'][identity + ':' + ('left' if stage == 'left-replay' else 'right')]
                require(command['replay'] is True and command['pending'] is False and command['executionId'] == identity and
                    command['inputId'] == original_command['inputId'] and command['result'] == original_command['result'],
                    'An actual repeated branch command did not replay its original consumed outcome.')
            for stage, revision in (('partial', 2), ('joined', 3), ('final', 5 if identity == original['executionId'] else 4)):
                require(case['views'][stage][identity]['revision'] == revision, 'A branch replay changed the retained public model revision.')
        snapshots = case['snapshots']; require(len(snapshots) == 6 and {s['file'] for s in snapshots} == {n for n in names if n.endswith('.sqlite.gz')},
            'The partial-fork receipt lacks its six whole-store snapshots.')
        for snapshot in snapshots:
            phase = snapshot['file'].split('-', 1)[0]
            with q.checked_upgrade_snapshot(root, directory, snapshot) as connection:
                if snapshot['file'].endswith('-runtime.sqlite.gz'):
                    checked = stages[phase]
                    v.physical(connection, checked, {'cancel': False}, 'waiting' if phase == 'partial' else 'completed', origin)
                    expected_type = v.ACTIVITY if parent == 'Root' else 'Elsa.Activities.' + ('Compensation' if parent == 'CompensationScope' else parent) + '.Activities.' + parent
                    native_material = json.loads(connection.execute('SELECT ContentJson FROM elsa_runtime_workflow_executable WHERE ArtifactId = ?', (v.key(checked['nativeParent']['pinnedExecutable']['artifactId']),)).fetchone()[0])
                    require(v.text(native_material['rootActivity']['activityType']) == expected_type, 'The actual native parent kind was relabelled.')
                    for model in (checked['model'], checked['directCounterpart']):
                        require(connection.execute('SELECT COUNT(*) FROM elsa_runtime_model_input WHERE ExecutionId = ?', (v.key(model['executionId']),)).fetchone()[0] == model['revision'],
                            'A physical partial-fork input receipt is absent or duplicated.')
                        identity = model['executionId']; observed = {w['elementId']: w for w in case['initial'][identity]}
                        observed['after-join'] = case['afterJoin'][identity]
                        for step, element in [('left', 'left')] + ([] if phase == 'partial' else [('right', 'right'), ('finish', 'after-join')]):
                            command = case['commands'][identity + ':' + step]; work = observed[element]
                            row = connection.execute('SELECT ContentJson FROM elsa_runtime_model_input WHERE ExecutionId = ? AND InputId = ?', (v.key(identity), v.key(command['inputId']))).fetchone()
                            receipt = json.loads(row[0]) if row else {}
                            require(receipt.get('invocationId') == v.key(work['invocationId']) and receipt.get('attemptId') == v.key(work['attemptId']),
                                'The observed human-work ledger is not bound to its physical invocation/attempt receipt.')
                    require(connection.execute('SELECT COUNT(*) FROM elsa_runtime_activity_execution_state').fetchone()[0] == (1 if parent == 'Root' else 3),
                        'The physical native continuation ran more than once.')
    return {'sourceRevision': source, 'actualParentPairs': 4, 'modelExecutionsCompleted': 8, 'wholeStoresVerified': 24,
        'partialBranchesPreservedAcrossProcessLoss': True, 'singleLogicalJoins': 8, 'directEmbeddedTracesEqual': True,
        'normalization': 'Only tokens.producingWorkHandle: owning model ID or observed local human invocation mapped by exact element/binding/token/iteration.',
        'etlOrStudioAcceptanceProved': False}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--manifest', required=True, type=Path)
    print(json.dumps(verify(parser.parse_args().manifest.resolve(strict=True)), indent=2))
