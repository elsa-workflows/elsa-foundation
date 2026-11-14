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
