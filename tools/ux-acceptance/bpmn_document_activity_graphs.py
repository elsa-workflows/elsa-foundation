"""Strict evidence inventory for actual source2 Activity Definition graph journeys."""
from __future__ import annotations

import json
from pathlib import Path
import xml.etree.ElementTree as ET

TITLE = 'activity-graph from {}: migration, shared Slot, history, source retention and publication guard'
CAPTURES = ('graph-opened-without-migration', 'graph-native-slot-shared-configuration',
            'graph-both-editors-and-history', 'graph-after-save-reload',
            'graph-standard-header-variables', 'graph-advanced-after-reload')
MODEL = '{http://www.omg.org/spec/BPMN/20100524/MODEL}'
VENDOR = '{urn:graph-retained}'


def expected_cases(projects):
    return {(project, TITLE.format(direction)): ('activity-graph', direction)
            for project in projects for direction in ('integrated', 'bpmn-js')}


def validate_case(project: str, direction: str, test: dict, output: Path, sha: str, attachment_bytes) -> dict:
    results = test.get('results', [])
    if (test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1
            or results[0].get('status') != 'passed' or results[0].get('retry') != 0
            or results[0].get('error') or results[0].get('errors')):
        raise ValueError(f'{project}/{direction}/activity-graph: no single complete passing execution')
    items = results[0].get('attachments', [])
    attachments = {item.get('name'): item for item in items}
    required = {'source', 'trace', 'activity-graph-receipt.json', 'graph-opened-draft.json', 'graph-noop-draft.json',
                'graph-first-edit-draft.json', 'graph-saved-draft.json', 'graph-reloaded-draft.json',
                'graph-expected-native.json', 'graph-complete-process.bpmn', 'graph-advanced-initial-draft.json',
                'graph-advanced-saved-draft.json', 'graph-advanced-facts.json', 'graph-advanced-process.bpmn',
                'graph-publication-guard.json'}
    required |= {f'{capture}.{suffix}' for capture in CAPTURES for suffix in ('png', 'aria.yml')}
    if len(items) != len(attachments) or not required <= attachments.keys():
        raise ValueError('Activity Graph evidence is missing or contains duplicate attachment names')
    for item in items:
        if item.get('path') or item.get('name') in required:
            attachment_bytes(item, output)
    read = lambda name: attachment_bytes(attachments[name], output)
    document = lambda name: json.loads(read(name))
    receipt = document('activity-graph-receipt.json')
    if (read('source').decode() != sha or receipt.get('source') != sha or receipt.get('kind') != 'activity-graph'
            or receipt.get('firstEditor') != direction or receipt.get('state') != 'passed-full-source-activity-graph'
            or receipt.get('browserErrors') != [] or receipt.get('unexpectedCanvasFailures') != []):
        raise ValueError('Activity Graph source, direction, completion or browser failure receipt differs')
    baseline = document('graph-opened-draft.json')
    no_op = document('graph-noop-draft.json')
    first = document('graph-first-edit-draft.json')
    saved = document('graph-saved-draft.json')
    reloaded = document('graph-reloaded-draft.json')
    advanced_initial = document('graph-advanced-initial-draft.json')
    advanced = document('graph-advanced-saved-draft.json')

    def root(draft):
        return draft.get('provider', {}).get('payload', {}).get('rootActivity', {})

    def payload(draft):
        return root(draft).get('structure', {}).get('payload', {})

    if (baseline != no_op or root(baseline).get('structure', {}).get('schemaVersion') != '1.0.0'
            or receipt.get('baselineRevision') != baseline.get('revision') or receipt.get('noOpRevision') != baseline.get('revision')):
        raise ValueError('Opening/switching an Activity Graph migrated or wrote the exact legacy draft')
    for draft in (first, saved, reloaded, advanced_initial, advanced):
        provider, base_provider = draft.get('provider', {}), baseline.get('provider', {})
        if (draft.get('draftId') != baseline.get('draftId') or draft.get('definitionId') != baseline.get('definitionId')
                or draft.get('draftId') != receipt.get('draftId') or draft.get('definitionId') != receipt.get('definitionId')
                or provider.get('providerKey') != 'elsa.activity-graph' or provider.get('schemaVersion') != base_provider.get('schemaVersion')
                or not provider.get('manifestFingerprint') or root(draft).get('nodeId') != root(baseline).get('nodeId')
                or root(draft).get('activityVersionId') != root(baseline).get('activityVersionId')
                or draft.get('contract') != baseline.get('contract')
                or provider.get('payload', {}).get('variables') != base_provider.get('payload', {}).get('variables')
                or provider.get('payload', {}).get('outputMappings') != base_provider.get('payload', {}).get('outputMappings')
                or root(draft).get('structure', {}).get('kind') != 'elsa.bpmn.structure'
                or root(draft).get('structure', {}).get('schemaVersion') != '2.0.0'
                or any(field in payload(draft) for field in ('elements', 'sequenceFlows', 'diagram', 'pools', 'lanes', 'messageFlows'))):
            raise ValueError('The same Activity Definition draft/provider/source identity or disjoint manifest did not survive')
    expected_native = document('graph-expected-native.json')
    if (payload(first).get('activities') != payload(baseline).get('activities')
            or any(payload(draft).get('activities') != expected_native for draft in (saved, reloaded, advanced_initial, advanced))
            or any(payload(draft).get('bindings') != payload(first).get('bindings') for draft in (saved, reloaded, advanced_initial, advanced))
            or payload(advanced).get('scopeHeaders') != payload(advanced_initial).get('scopeHeaders')):
        raise ValueError('Actual native configuration, bindings or standard scope headers were changed or lost')
    if (saved != reloaded or receipt.get('savedRevision') != saved.get('revision')
            or receipt.get('reloadedRevision') != saved.get('revision') or receipt.get('advancedRevision') != advanced.get('revision')
            or not first.get('revision', 0) > baseline.get('revision', 0)
            or not advanced.get('revision', 0) >= advanced_initial.get('revision', 0) > saved.get('revision', 0)):
        raise ValueError('Activity Graph reload/revision identity is not exact')
    for draft, filename, target, name in ((saved, 'graph-complete-process.bpmn', 'gateway', 'Graph complete source'),
                                        (advanced, 'graph-advanced-process.bpmn', 'GraphComplex', 'Graph advanced retained')):
        xml = payload(draft).get('documentXml')
        if not isinstance(xml, str) or read(filename).decode() != xml:
            raise ValueError('Actual exported XML differs from the saved Activity Graph source')
        tree = ET.fromstring(xml)
        element = next((item for item in tree.iter() if item.get('id') == target), None)
        if element is None or element.get('name') != name:
            raise ValueError('Authored Activity Graph source label was not actually persisted')
    facts = document('graph-advanced-facts.json')
    xml = payload(advanced)['documentXml']
    opaque = ET.fromstring(xml).find(f'.//{VENDOR}keep')
    if (opaque is None or opaque.get(VENDOR + 'flag') != 'qualified' or opaque.get('flag') != 'unqualified'
            or opaque.get('ref') != 'q:Thing' or len(opaque.findall(VENDOR + 'child')) != 1
            or '<![CDATA[raw < scope & retained]]>' not in xml or 'xmlns:q="urn:graph-qname"' not in xml
            or facts.get('qualified') != 'qualified' or facts.get('unqualified') != 'unqualified'
            or facts.get('qname') != 'urn:graph-qname' or facts.get('childCount') != 1 or facts.get('cdata') is not True
            or facts.get('complex') != 'GraphComplex'):
        raise ValueError('Advanced opaque XML/QName/CDATA preservation evidence is incomplete')
    guard = document('graph-publication-guard.json')
    preflight, status, rejection = guard.get('preflight', {}), guard.get('status'), guard.get('rejection', '')
    if (preflight.get('draftId') != baseline['draftId'] or preflight.get('draftRevision') != advanced['revision']
            or preflight.get('isPublishable') is not False or guard.get('versionCount') != 0
            or not any(marker in json.dumps(preflight.get('diagnostics')) for marker in ('GraphComplex', 'complexGateway'))
            or status not in (200, 201, 400, 409, 422)
            or not any(marker in rejection for marker in ('GraphComplex', 'complexGateway'))
            or status in (200, 201) and json.loads(rejection).get('status') not in ('Rejected', 'Failed')):
        raise ValueError('Advanced Activity Graph lacks an actual source-specific publication refusal')
    writes = receipt.get('writes')
    if not isinstance(writes, list) or not writes:
        raise ValueError('No real Activity Definition CAS writes were observed')
    previous = baseline['revision']
    for write in writes:
        if (write.get('error') or write.get('status') != 200 or write.get('expectedRevision') != previous
                or write.get('revision') != previous + 1):
            raise ValueError('Activity Definition draft writes are not one monotonic accepted CAS chain')
        previous = write['revision']
    if previous != advanced['revision']:
        raise ValueError('Activity Definition CAS receipt does not end at the saved advanced draft')
    return {'project': project, 'firstEditor': direction, 'kind': 'activity-graph',
            'definitionId': baseline['definitionId'], 'draftId': baseline['draftId'], 'revision': advanced['revision']}
