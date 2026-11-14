"""Failure controls for the normal-host inventory evidence gate. These do not qualify product UX."""
import copy
import json
from pathlib import Path
import tempfile
import unittest

import weaver_inventory as inventory


def receipt(phase='baseline'):
    states = ['enabled'] if phase == 'enabled' else ['baseline', 'deselected', 'unknown']
    observations = []
    for state in states:
        logs = state == 'baseline'
        def feature(identity, enabled): return {'id': identity, 'enabled': enabled, 'runtimeAvailable': True}
        observations.append({'kind': 'inventory', 'state': state, 'route': '/qualification-unknown-module' if state == 'unknown' else '/observed',
            'registry': {'complete': True, 'modules': [{'id': 'structured-logs', 'status': 'available' if logs else 'disabled'},
                {'id': 'extension-builder', 'status': 'available' if phase == 'enabled' else 'disabled'}]},
            'support': {'support': {'completeness': 'complete'}},
            'studioFeatures': [feature('StructuredLogsStudio', logs), feature('ExtensionBuilderStudio', phase == 'enabled')],
            'backendFeatures': [feature('DiagnosticsStructuredLogs', logs), feature('DiagnosticsStructuredLogsEntityFrameworkCore', logs)],
            'provider': {'enabled': False, 'providerStatus': 'unavailable'},
            'question': {'text': 'What support is installed?', 'status': 'unanswered-provider-unavailable'},
            'readiness': {'status': 'ready', 'shell': 'default', 'generation': 1},
            'disclosure': {'status': 'not-observed', 'reason': 'provider-unavailable'}})
    selections = [('studio', {'ExtensionBuilderStudio': True})] if phase == 'enabled' else [
        ('backend', {'DiagnosticsStructuredLogsEntityFrameworkCore': False, 'DiagnosticsStructuredLogs': False}),
        ('studio', {'StructuredLogsStudio': False})]
    for side, states in selections:
        observations.append({'kind': 'selection', 'side': side, 'states': states,
                             'retainedEnabledIds': ['unrelated'], 'submittedIds': ['unrelated', *states], 'retainedConfigurationMatched': True})
    observations.append({'kind': 'root-extension', 'enabled': phase == 'enabled', 'status': 401 if phase == 'enabled' else 404})
    return {'source': 'a' * 40, 'phase': phase, 'completed': True, 'failedRequests': [],
            'providerEvidence': 'not-observed', 'observations': observations}


class InventoryVerdictTests(unittest.TestCase):
    def validate(self, value, phase='baseline'):
        return inventory.inventory_verdict(value, source='a' * 40, phase=phase)

    def test_both_native_phase_receipts_preserve_unanswered_provider_boundary(self):
        self.assertEqual(3, self.validate(receipt()))
        self.assertEqual(1, self.validate(receipt('enabled'), 'enabled'))

    def test_failed_missing_stale_and_incomplete_observations_cannot_pass(self):
        for mutation in ['failure', 'request', 'source', 'missing-state', 'duplicate-state', 'unknown-discovery',
                         'ef-still-selected', 'studio-module-missing', 'answer-claimed', 'provider-claimed',
                         'admission-claimed', 'missing-generation', 'omitted-enabled-feature', 'root-not-restarted']:
            with self.subTest(mutation=mutation):
                value = receipt()
                row = value['observations'][0]
                if mutation == 'failure': value['completed'] = False
                if mutation == 'request': value['failedRequests'] = ['/weaver/bootstrap']
                if mutation == 'source': value['source'] = 'b' * 40
                if mutation == 'missing-state': value['observations'].pop(0)
                if mutation == 'duplicate-state': value['observations'].append(copy.deepcopy(row))
                if mutation == 'unknown-discovery': row['registry']['complete'] = None
                if mutation == 'ef-still-selected': row['backendFeatures'][1]['enabled'] = False
                if mutation == 'studio-module-missing': row['registry']['modules'].pop()
                if mutation == 'answer-claimed': row['question']['status'] = 'answered'
                if mutation == 'provider-claimed': row['provider']['enabled'] = True
                if mutation == 'admission-claimed': row['disclosure'] = {'status': 'admitted'}
                if mutation == 'missing-generation': row['readiness'].pop('generation')
                if mutation == 'omitted-enabled-feature': value['observations'][-2]['submittedIds'] = []
                if mutation == 'root-not-restarted': value['observations'][-1]['status'] = 401
                with self.assertRaises(ValueError): self.validate(value)

    def test_shipped_health_failure_keeps_only_bounded_native_cause(self):
        value = inventory.readiness_fact(503, json.dumps({'status': 'failed', 'code': 'shell_activation_failed',
            'message': 'Bearer secret /Users/private.json', 'generation': 2}).encode())
        self.assertEqual({'httpStatus': 503, 'status': 'failed', 'code': 'shell_activation_failed', 'generation': 2}, value)
        self.assertEqual({'httpStatus': 503, 'status': 'unclassified'}, inventory.readiness_fact(503, b'Bearer secret'))

    def test_build_failure_keeps_exit_and_codes_without_log_body(self):
        import subprocess
        with tempfile.TemporaryDirectory() as temporary:
            log = Path(temporary) / 'private.log'
            log.write_text('NU1101 secret /Users/private.json\nCS0006 Bearer secret\n')
            value = inventory.failure_fact(subprocess.CalledProcessError(1, ['private-command']), 'build-workbench', log)
            self.assertEqual({'stage': 'build-workbench', 'category': 'process-failed', 'exitCode': 1, 'buildCodes': ['CS0006', 'NU1101']}, value)


def semantic_case(phase='baseline'):
    """Synthetic boundary records, not source-shape assertions or a product qualification."""
    value = receipt(phase)
    if phase == 'baseline':
        extra = copy.deepcopy(value['observations'][1]); extra['state'] = 'deselected-list'
        value['observations'].insert(1, extra)
    value.update(turns=[], messagePosts=[], studioSource='a' * 40)
    peers = []
    for index, native in enumerate([item for item in value['observations'] if item['kind'] == 'inventory']):
        native['provider'] = {'enabled': True, 'providerStatus': 'available'}
        native['support']['policyScope'] = 'hostDefault'
        state = native['state']
        nonce, session = f'synthetic-nonce-{index}', f'synthetic-session-{index}'
        question = f'What support is installed for modules structured-logs, extension-builder and absent-module?{" What is known about the current route?" if state == "unknown" else ""} nonce={nonce}'
        studio = {'side': 'studio', 'clientAsserted': True, 'completeness': 'complete', 'registrationCompleteness': 'complete',
                  'modules': copy.deepcopy(native['registry']['modules']), 'loader': [], 'registrations': []}
        backend = {'side': 'backend', 'policyScope': 'session', 'support': {'completeness': 'complete', 'tools': [], 'contexts': []}}
        context = [{'source': side, 'moduleId': 'Elsa.Weaver', 'contentType': 'weaver.host-support', 'schemaVersion': '1',
                    'origin': origin, 'content': content, 'target': None, 'weaverScopes': []}
                   for side, origin, content in [('studio', 'clientSnapshot', studio), ('backend', 'authoritativeRead', backend)]]
        request = {'requestId': f'client-request-{index}', 'message': question, 'content': question,
                   'activeSurface': {'route': native['route']}, 'contextAttachments': [copy.deepcopy(context[0])]}
        if state == 'unknown': studio['activeRoute'] = native['route']
        message = {'id': f'accepted-{index}', 'requestId': request['requestId'], 'sessionId': session, 'content': question, 'context': context}
        response = f'Synthetic derived response for {nonce}'
        recorded = {'id': f'assistant-{index}', 'role': 'assistant', 'status': 'completed', 'content': response}
        turn = {'state': state, 'route': native['route'], 'nonce': nonce, 'question': question, 'sessionId': session,
                'request': request, 'accepted': {'message': message, 'warnings': []}, 'assistantId': recorded['id'],
                'rendered': response, 'recorded': recorded, 'before': copy.deepcopy(native['readiness']), 'after': copy.deepcopy(native['readiness'])}
        value['turns'].append(turn)
        value['messagePosts'].append({'path': f'/_elsa/weaver/sessions/{session}/messages', 'body': copy.deepcopy(request)})
        facts = {'studioCompleteness': 'complete', 'registrationCompleteness': 'complete', 'backendCompleteness': 'complete',
                 'authority': 'unchecked', 'modules': [{'id': row['id'], 'status': row['status'], 'weaverContributions': 'none', 'registrations': []}
                    for row in studio['modules']] + [{'id': 'absent-module', 'status': 'absent', 'weaverContributions': 'none', 'registrations': []}],
                 'backendTools': [], 'backendContexts': []}
        if state == 'unknown': facts['activeRoute'] = studio['activeRoute']
        peers.append({'nonce': nonce, 'sessionId': session, 'requestId': index + 1, 'messageId': f'sdk-message-{index}',
            'metadata': [{'moduleId': 'Elsa.Weaver', 'contentType': 'weaver.host-support', 'schemaVersion': '1', 'target': None, 'origin': origin}
                         for origin in ('ClientSnapshot', 'AuthoritativeRead')], 'facts': facts, 'response': response,
            'promptHash': inventory.text_hash('prompt-' + nonce), 'requestHash': inventory.text_hash('request-' + nonce), 'userRequestHash': inventory.text_hash(question),
            'responseHash': inventory.text_hash(response), 'wireComplete': True})
        value['observations'].append({'kind': 'native-packages', 'modules': [{'packageId': 'unrelated.package', 'version': '1.0', 'status': 'available'}]})
    return value, peers


class SemanticVerdictTests(unittest.TestCase):
    def validate(self, value, peers, phase='baseline'):
        return inventory.semantic_verdict(value, peers, source='a' * 40, phase=phase)

    def test_exact_four_plus_one_turns_join_actual_boundaries_without_exporting_raw_values(self):
        for phase, count in [('baseline', 4), ('enabled', 1)]:
            with self.subTest(phase=phase):
                value, peers = semantic_case(phase)
                joined = self.validate(value, peers, phase)
                self.assertEqual(count, len(joined))
                public = json.dumps(joined)
                for turn in value['turns']:
                    for key in ('nonce', 'question', 'sessionId', 'rendered', 'assistantId'):
                        self.assertNotIn(turn[key], public)

    def test_correlation_and_native_causes_fail_independently(self):
        cases = {'missing-peer': 'Missing, duplicate or retried', 'extra-post': 'Missing, duplicate or retried',
                 'duplicate-nonce': 'Duplicate browser nonce', 'foreign-session': 'Incomplete or foreign SDK delivery',
                 'foreign-question': 'Incomplete or foreign SDK delivery', 'wire-incomplete': 'Incomplete or foreign SDK delivery',
                 'wrong-admitted-origin': 'SDK metadata differs', 'wrong-derived-module': 'SDK derived facts differ',
                 'stale-studio-module': 'Current submitted Studio facts', 'wrong-rendered-answer': 'Derived SDK response',
                 'wrong-response-hash': 'Derived SDK response', 'stale-assistant': 'Stale rendered assistant',
                 'generation-changed': 'Native generation changed', 'resource-on-unknown': 'Unknown route acquired resource',
                 'missing-route': 'Missing actual admitted current route', 'route-in-question': 'Question supplied route',
                 'backend-authority-inferred': 'SDK derived facts differ', 'inventory-lost': 'Unrelated package inventory changed',
                 'native-selection-stale': 'Missing independent discovered/selected feature'}
        for cause, assertion in cases.items():
            with self.subTest(cause=cause):
                value, peers = semantic_case()
                turn = value['turns'][0]
                if cause == 'missing-peer': peers.pop()
                if cause == 'extra-post': value['messagePosts'].append(copy.deepcopy(value['messagePosts'][0]))
                if cause == 'duplicate-nonce': value['turns'][1]['nonce'] = turn['nonce']
                if cause == 'foreign-session': peers[0]['sessionId'] = 'foreign'
                if cause == 'foreign-question': peers[0]['userRequestHash'] = 'c' * 64
                if cause == 'wire-incomplete': peers[0]['wireComplete'] = False
                if cause == 'wrong-admitted-origin': peers[0]['metadata'][0]['origin'] = 'AuthoritativeRead'
                if cause == 'wrong-derived-module': peers[0]['facts']['modules'][0]['status'] = 'disabled'
                if cause == 'stale-studio-module': value['turns'][1]['accepted']['message']['context'][0]['content']['modules'][0]['status'] = 'available'
                if cause == 'wrong-rendered-answer': turn['rendered'] = 'Unrelated answer'
                if cause == 'wrong-response-hash': peers[0]['responseHash'] = 'c' * 64
                if cause == 'stale-assistant': value['turns'][1]['assistantId'] = turn['assistantId']
                if cause == 'generation-changed': turn['after']['generation'] += 1
                if cause == 'resource-on-unknown':
                    value['turns'][-1]['request']['activeSurface']['resourceId'] = 'guessed'
                    value['messagePosts'][-1]['body']['activeSurface']['resourceId'] = 'guessed'
                if cause == 'missing-route': value['turns'][-1]['accepted']['message']['context'][0]['content'].pop('activeRoute')
                if cause == 'route-in-question': turn['question'] += ' /expected/path'
                if cause == 'backend-authority-inferred': peers[0]['facts']['authority'] = 'granted'
                if cause == 'inventory-lost': value['observations'][-1]['modules'] = []
                if cause == 'native-selection-stale': value['observations'][1]['backendFeatures'][1]['enabled'] = True
                with self.assertRaisesRegex(ValueError, assertion): self.validate(value, peers)

    def test_json_order_and_optional_null_are_not_mistaken_for_a_transport_mismatch(self):
        value, peers = semantic_case()
        peers[0]['metadata'].reverse()
        peers[0]['facts']['modules'].reverse()
        for row in peers[0]['metadata']: row.pop('target')
        self.assertEqual(4, len(self.validate(value, peers)))

    def test_connection_scoped_rpc_ids_may_repeat_with_distinct_physical_messages(self):
        value, peers = semantic_case()
        for peer in peers: peer['requestId'] = 4
        self.assertEqual(4, len(self.validate(value, peers)))


class SemanticExportTests(unittest.TestCase):
    def export_case(self, root, *, failed=False, canary=None):
        private = root / 'private'; private.mkdir()
        destination = root / 'safe'
        value, peers = semantic_case()
        if failed: value['completed'] = False
        # Dynamic session paths are private, unlike the fixed public protocol path templates.
        value['network'] = [{'side': 'backend', 'path': '/_elsa/weaver/sessions/synthetic-session-0/messages', 'method': 'POST', 'status': 200}]
        files = {'weaver-semantic.json': json.dumps(value).encode(), 'trace': b'PRIVATE RAW TRACE Cookie: synthetic'}
        for capture in inventory.SEMANTIC_CAPTURES[inventory.SEMANTIC_BASELINE]:
            files[capture + '.png'] = b'\x89PNG\r\n\x1a\n' + capture.encode()
            files[capture + '.aria.yml'] = b'- heading "Native surface; assistant closed"'
        if canary: files['reference.aria.yml'] += canary
        attachments = []
        for index, (name, data) in enumerate(files.items()):
            path = private / str(index); path.write_bytes(data)
            attachments.append({'name': name, 'path': str(path)})
        report = {'suites': [{'specs': [{'title': inventory.SEMANTIC_BASELINE, 'tests': [{'projectName': 'desktop-light',
            'expectedStatus': 'passed', 'status': 'unexpected' if failed else 'expected',
            'results': [{'status': 'failed' if failed else 'passed', 'attachments': attachments}]}]}]}]}
        path = private / 'playwright.json'; path.write_text(json.dumps(report))
        return path, private, destination, peers

    def test_success_and_failure_never_export_prompt_response_nonce_or_raw_session_path(self):
        for failed in (False, True):
            with self.subTest(failed=failed), tempfile.TemporaryDirectory() as directory:
                report, private, destination, peers = self.export_case(Path(directory), failed=failed)
                inventory.safe_export(report, private, destination, source='a' * 40, project='desktop-light',
                    title=inventory.SEMANTIC_BASELINE, phase='baseline', peer_records=peers)
                public = b''.join(path.read_bytes() for path in destination.iterdir())
                for secret in (b'synthetic-nonce-', b'synthetic-session-', b'What support is installed', b'Synthetic derived response', b'PRIVATE RAW TRACE'):
                    self.assertNotIn(secret, public)
                receipt = json.loads((destination / 'weaver-semantic.json').read_text())
                self.assertEqual(not failed, receipt['correlationComplete'])
                self.assertEqual(0 if failed else 4, len(receipt['correlation']))
                if not failed:
                    inventory.export_manifest_verdict(destination / 'safe-export.json', destination, project='desktop-light', semantic=True)
                    with self.assertRaisesRegex(ValueError, 'Incomplete or duplicate safe export manifest'):
                        inventory.export_manifest_verdict(destination / 'safe-export.json', destination, project='desktop-light')

    def test_raw_conversation_and_auth_canaries_refuse_all_writes_on_success_and_failure(self):
        for failed in (False, True):
            for marker in (b'synthetic-nonce-0', b'Synthetic derived response for synthetic-nonce-0',
                           b'What support is installed for modules structured-logs, extension-builder and absent-module? nonce=synthetic-nonce-0',
                           b'Bearer synthetic-secret', b'/Users/private/config.json'):
                with self.subTest(failed=failed, marker=marker), tempfile.TemporaryDirectory() as directory:
                    report, private, destination, peers = self.export_case(Path(directory), failed=failed, canary=marker)
                    with self.assertRaisesRegex(ValueError, 'Safe export refused'):
                        inventory.safe_export(report, private, destination, source='a' * 40, project='desktop-light',
                            title=inventory.SEMANTIC_BASELINE, phase='baseline', peer_records=peers)
                    self.assertFalse(destination.exists())
                    self.assertFalse(destination.with_name(destination.name + '.exporting').exists())


class ProtocolProjectionTests(unittest.TestCase):
    def receipt(self):
        turns = []
        for index in range(5):
            turns.append({**{key: inventory.text_hash(f'{key}-{index}') for key in
                ('sessionHash', 'nonceHash', 'promptHash', 'requestHash', 'userRequestHash', 'responseHash')},
                'requestId': 4, 'wireComplete': True, 'moduleCount': 3, 'admittedMetadataCount': 2})
        return {'kind': 'external-copilot-protocol-observation', 'targetSdkVersion': '1.0.4', 'protocolVersion': 3,
                'connections': 10, 'requests': 50, 'errors': [], 'closed': True, 'turns': turns,
                'close': {'kind': 'owned-copilot-peer-close', 'complete': True, 'liveThreads': 0,
                          'openConnections': 0, 'connections': 10, 'turns': 5, 'errors': []}}

    def test_only_closed_five_turn_safe_snapshot_fields_are_selected(self):
        value = self.receipt()
        value['prompt'] = 'private-prompt-canary'
        value['turns'][0]['response'] = 'private-response-canary'
        value['close']['token'] = 'private-token-canary'
        self.assertNotIn('canary', json.dumps(inventory.protocol_projection(value)))

    def test_errors_partial_wire_duplicate_turn_and_cleanup_failure_cannot_qualify(self):
        for cause, assertion in [('errors', 'Peer observed protocol errors'), ('partial', 'Incomplete safe protocol turn'),
                                 ('duplicate', 'Duplicate protocol nonce'), ('cleanup', 'Owned peer did not close'),
                                 ('zero', 'exactly five physical')]:
            with self.subTest(cause=cause):
                value = self.receipt()
                if cause == 'errors': value['errors'] = ['protocol-error']
                if cause == 'partial': value['turns'][0]['wireComplete'] = False
                if cause == 'duplicate': value['turns'][1] = copy.deepcopy(value['turns'][0])
                if cause == 'cleanup': value['close']['liveThreads'] = 1
                if cause == 'zero': value['turns'] = []
                with self.assertRaisesRegex(ValueError, assertion): inventory.protocol_projection(value)


class SafeExportTests(unittest.TestCase):
    def export_case(self, root, *, failed=False, canary=None, canary_name='reference.aria.yml'):
        private = root / 'private-originals'; private.mkdir()
        destination = root / 'exported'
        value = receipt()
        if failed: value['completed'] = False
        files = {'weaver-inventory.json': json.dumps(value).encode(), 'trace': b'RAW PRIVATE ORIGINAL: Cookie: session=never-export',
                 'unselected-auth-state.json': b'RAW PRIVATE ORIGINAL: Password123!'}
        for name in inventory.CAPTURES[inventory.BASELINE]:
            files[name + '.png'] = b'\x89PNG\r\n\x1a\n' + name.encode()
            files[name + '.aria.yml'] = b'- heading "Workflow definitions"'
        if canary is not None: files[canary_name] += canary
        attachments = []
        for index, (name, data) in enumerate(files.items()):
            path = private / str(index); path.write_bytes(data)
            attachments.append({'name': name, 'path': str(path)})
        report = {'suites': [{'specs': [{'title': inventory.BASELINE, 'tests': [{'projectName': 'desktop-light',
            'status': 'unexpected' if failed else 'expected', 'expectedStatus': 'passed',
            'results': [{'status': 'failed' if failed else 'passed', 'attachments': attachments,
                         'error': {'message': 'RAW PRIVATE ORIGINAL: Password123!'}}]}]}]}]}
        path = private / 'playwright.json'; path.write_text(json.dumps(report))
        return path, private, destination

    def test_success_and_failure_export_only_allowlisted_safe_projections(self):
        for failed in [False, True]:
            with self.subTest(failed=failed), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                report, private, destination = self.export_case(root, failed=failed)
                exported = inventory.safe_export(report, private, destination, source='a' * 40, project='desktop-light',
                    title=inventory.BASELINE, phase='baseline')
                self.assertFalse((destination / 'trace').exists())
                self.assertFalse((destination / 'unselected-auth-state.json').exists())
                for path in destination.iterdir():
                    self.assertNotIn(b'RAW PRIVATE ORIGINAL', path.read_bytes())
                    self.assertNotIn(b'Password123!', path.read_bytes())
                    self.assertNotIn(str(private).encode(), path.read_bytes())
                interaction = json.loads((destination / 'weaver-interaction.json').read_text())
                self.assertEqual('safe-weaver-interaction-projection', interaction['kind'])
                self.assertFalse(interaction['rawTraceInspectedOrShared'])
                self.assertEqual('failed' if failed else 'passed', exported['suites'][0]['specs'][0]['tests'][0]['results'][0]['status'])
                if failed:
                    with self.assertRaises(ValueError): inventory.export_manifest_verdict(destination / 'safe-export.json', destination, project='desktop-light')
                else:
                    self.assertGreater(inventory.export_manifest_verdict(destination / 'safe-export.json', destination, project='desktop-light'), 0)
                    (destination / 'reference.aria.yml').write_text('tampered after export')
                    with self.assertRaisesRegex(ValueError, 'bytes changed'):
                        inventory.export_manifest_verdict(destination / 'safe-export.json', destination, project='desktop-light')

    def test_credential_jwt_private_file_and_auth_surface_bytes_fail_before_any_export_write(self):
        for failed in [False, True]:
            for canary in [b' Password123!', b' Authorization: confidential', b' Cookie: confidential',
                           b' eyJhbGciOiJIUzI1NiJ9.payload.signature', b' /Users/owner/private-auth.json',
                           b' - textbox "Password"']:
                with self.subTest(failed=failed, canary=canary), tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    report, private, destination = self.export_case(root, failed=failed, canary=canary)
                    with self.assertRaises(ValueError):
                        inventory.safe_export(report, private, destination, source='a' * 40, project='desktop-light',
                            title=inventory.BASELINE, phase='baseline')
                    self.assertFalse(destination.exists())
                    self.assertFalse(destination.with_name(destination.name + '.exporting').exists())


if __name__ == '__main__':
    unittest.main()
