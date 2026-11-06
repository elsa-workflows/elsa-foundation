#!/usr/bin/env python3
"""Qualify source2 Workflow authoring/runtime and advanced retention on disposable real hosts."""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from xml.dom import minidom

from bpmn_document_activity_graphs import expected_cases, validate_case
from pathlib import Path
import signal


def helpers():
    path = Path(__file__).with_name('run-bpmn-js-prototype.py')
    spec = importlib.util.spec_from_file_location('bpmn_owned_host_helpers', path)
    if spec is None or spec.loader is None:
        raise RuntimeError('Cannot load the existing owned-host lifecycle')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def verdict(report: dict, output: Path, sha: str) -> list[dict]:
    original = helpers()
    names = [project.get('name') for project in report.get('config', {}).get('projects', [])]
    expected_projects = {'desktop-light', 'desktop-dark', 'narrow-desktop-light'}
    if len(names) != 3 or set(names) != expected_projects:
        raise ValueError('The exact Light/Dark/1100px desktop project matrix is incomplete')
    if any(project.get('retries') != 0 or project.get('repeatEach') != 1 for project in report['config']['projects']):
        raise ValueError('Source2 qualification requires one attempt per case without retries')
    runtime_title = 'full-source from {}: migration, shared inspector, history, two published routes and settled inspection'
    advanced_title = 'advanced-source from {}: full drafts survive both editors and block execution'
    expected = {(project, title.format(direction)): (kind, direction)
                for project in names for direction in ('integrated', 'bpmn-js')
                for kind, title in [('runtime', runtime_title), ('advanced', advanced_title)]}
    expected.update(expected_cases(names))
    rows = list(original.test_rows(report.get('suites', [])))
    actual = [(project, title) for project, title, _ in rows]
    if len(actual) != len(expected) or set(actual) != set(expected):
        raise ValueError('Actual journeys differ from the eighteen required source2 Workflow and Activity Graph cases')
    stats = report.get('stats', {})
    if report.get('errors') or stats.get('expected') != len(expected) or any(stats.get(key) != 0 for key in ('skipped', 'unexpected', 'flaky')):
        raise ValueError('Failed, skipped, flaky or incomplete journeys remain unqualified')
    captures = {
        'runtime': ('opened-without-migration', 'native-pool-create-before-shared-undo', 'native-sequence-through-shared-inspector',
                    'native-after-shared-configuration', 'integrated-keyboard-layout-and-properties', 'native-after-save-reload',
                    'actual-run-inspection-high', 'actual-run-inspection-low'),
        'advanced': ('advanced-first-editor', 'advanced-boundary-loop-collaboration', 'advanced-independent-process-context',
                     'advanced-event-subprocess-context', 'advanced-after-save-reload')
    }
    accepted = []
    for project, title, test in rows:
        kind, direction = expected[(project, title)]
        if kind == 'activity-graph':
            accepted.append(validate_case(project, direction, test, output, sha, original.attachment_bytes))
            continue
        results = test.get('results', [])
        if (test.get('expectedStatus') != 'passed' or test.get('status') != 'expected' or len(results) != 1
                or results[0].get('status') != 'passed' or results[0].get('retry') != 0
                or results[0].get('error') or results[0].get('errors')):
            raise ValueError(f'{project}/{direction}/{kind}: no single complete passing execution')
        items = results[0].get('attachments', [])
        attachments = {item.get('name'): item for item in items}
        required = {'source', 'trace', 'document-receipt.json', 'saved-draft.json', 'expected-native-activities.json'}
        required |= {f'{capture}.{suffix}' for capture in captures[kind] for suffix in ('png', 'aria.yml')}
        required |= {'complete-process.bpmn', 'before-active-drag.bpmn', 'before-pool.bpmn', 'after-pool-undo.bpmn', 'runtime-High.json', 'runtime-Low.json'} if kind == 'runtime' else {'advanced-process.bpmn', 'advanced-facts.json', 'advanced-initial-draft.json'}
        if len(items) != len(attachments) or not required <= attachments.keys():
            raise ValueError(f'{project}/{direction}/{kind}: missing or duplicate required evidence')
        for item in items:
            if item.get('path') or item.get('name') in required:
                original.attachment_bytes(item, output)
        read = lambda name: original.attachment_bytes(attachments[name], output)
        if read('source').decode() != sha:
            raise ValueError('Journey source attachment differs from the clean checkout')
        receipt = json.loads(read('document-receipt.json'))
        if (receipt.get('source') != sha or receipt.get('kind') != kind or receipt.get('firstEditor') != direction
                or receipt.get('browserErrors') != [] or receipt.get('unexpectedCanvasFailures') != []):
            raise ValueError('Source, direction, runtime kind or actual browser error receipt differs')
        saved = json.loads(read('saved-draft.json'))
        root = saved.get('state', {}).get('rootActivity', {})
        structure = root.get('structure', {})
        payload = structure.get('payload', {})
        if (saved.get('id') != receipt.get('draftId') or saved.get('definitionId') != receipt.get('definitionId')
                or structure.get('kind') != 'elsa.bpmn.structure' or structure.get('schemaVersion') != '2.0.0'
                or any(field in payload for field in ('elements', 'sequenceFlows', 'diagram', 'pools', 'lanes', 'messageFlows', 'isTransaction'))
                or payload.get('activities') != json.loads(read('expected-native-activities.json'))):
            raise ValueError('Saved source2/native configuration evidence is incomplete or has competing authorities')
        xml_name = 'complete-process.bpmn' if kind == 'runtime' else 'advanced-process.bpmn'
        if read(xml_name).decode() != payload.get('documentXml'):
            raise ValueError('Downloaded complete XML differs from the actual saved source')
        if kind == 'runtime':
            drag = receipt.get('activeDrag', {})
            before, after = drag.get('before', {}), drag.get('after', {})
            positions = [value.get(axis) for value in (before, after) for axis in ('x', 'y')]
            if (drag.get('savedBeforeMouseup') is not True or any(type(value) not in (int, float) or not math.isfinite(value) for value in positions)
                    or before == after):
                raise ValueError('Actual Save did not capture a finite changed DI position before native pointer release')
            original.validate_receipt(receipt, attachments, output, sha, direction,
                                      branches={'High': ('Edited high', 'high'), 'Low': ('Nested low', 'low-inline')},
                                      passed_state='passed-full-source-runtime')
            if read('before-pool.bpmn') != read('after-pool-undo.bpmn'):
                raise ValueError('Shared Undo did not restore exact source after native pool creation')
        else:
            if (receipt.get('state') != 'passed-full-source-advanced'
                    or receipt.get('publicationStatus', receipt.get('promotionStatus')) not in (400, 409, 422)
                    or not isinstance(receipt.get('executionRejection'), str) or not receipt['executionRejection']):
                raise ValueError('Advanced source has no actual publication/execution refusal receipt')
            initial = json.loads(read('advanced-initial-draft.json'))
            initial_payload = initial.get('state', {}).get('rootActivity', {}).get('structure', {}).get('payload', {})
            def opaque_subtree(xml):
                values = minidom.parseString(xml).getElementsByTagNameNS('urn:retained-vendor', 'keep')
                if len(values) != 1:
                    raise ValueError('Expected one retained opaque source subtree')
                return values[0].toxml()
            if (initial.get('id') != saved.get('id') or initial.get('definitionId') != saved.get('definitionId')
                    or initial_payload.get('activities') != payload.get('activities')
                    or initial_payload.get('scopeHeaders') != payload.get('scopeHeaders')
                    or opaque_subtree(initial_payload.get('documentXml')) != opaque_subtree(payload.get('documentXml'))):
                raise ValueError('Original advanced draft identity, native headers or opaque source subtree changed')
            facts = json.loads(read('advanced-facts.json'))
            if (facts.get('advanced') != 'complexGateway' or facts.get('sequential') != 'true'
                    or facts.get('timer') != 'PT5M' or facts.get('interrupting') != 'false'
                    or facts.get('opaqueQName') != 'urn:retained-qname'
                    or facts.get('opaqueRef') != 'q:Uninterpreted' or facts.get('opaqueLiteral') != 'native-high'
                    or facts.get('opaqueChildren') != 1 or facts.get('opaqueCdata') is not True
                    or not isinstance(facts.get('opaqueXml'), str) or '<![CDATA[raw < text & native-high]]>' not in facts['opaqueXml']
                    or facts.get('script') != 'return "kept < script & value";'):
                raise ValueError('Actual advanced syntax/configuration preservation facts differ')
        accepted.append({'project': project, 'firstEditor': direction, 'kind': kind, 'definitionId': receipt['definitionId'], 'draftId': receipt['draftId']})
    if not (output / 'html/index.html').is_file():
        raise ValueError('Downloadable HTML report is missing')
    return accepted


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    original = helpers()
    signal.signal(signal.SIGTERM, original.terminate)
    signal.signal(signal.SIGINT, original.terminate)
    original.run(args.output, playwright_config='tests/acceptance/playwright.bpmn-document.config.ts',
                 verdict_fn=verdict, kind='composed-source2-workflow-and-activity-graph')


if __name__ == '__main__':
    main()
