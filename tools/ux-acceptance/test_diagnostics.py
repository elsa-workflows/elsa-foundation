import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('diagnostics', Path(__file__).with_name('diagnostics.py'))
diagnostics = importlib.util.module_from_spec(spec)
spec.loader.exec_module(diagnostics)


class DiagnosticsBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='elsa-history-diagnostics-')
        self.addCleanup(self.temporary.cleanup)
        self.data = Path(self.temporary.name).resolve()
        self.root = self.data / 'workbench-owned'; self.root.mkdir()
        self.database = self.root / 'elsa.db'; self.database.write_bytes(b'owned existing database')
        self.marker = dict(schemaVersion=1, nonce='owned-nonce', sourceSha='a' * 40, controllerProcessId=os.getpid(), root=str(self.root), backendPid=999999)
        (self.root / 'owned-history.json').write_text(json.dumps(self.marker))

    def test_preparation_accepts_only_this_controller_root_nonce_source_and_unlinked_store(self):
        self.assertEqual(self.marker, diagnostics.validate_owner(self.root, 'owned-nonce', 'a' * 40))
        before = diagnostics.store_hashes(self.root)
        for nonce, sha in [('other', 'a' * 40), ('owned-nonce', 'b' * 40)]:
            with self.subTest(nonce=nonce, sha=sha), self.assertRaises(ValueError):
                diagnostics.validate_owner(self.root, nonce, sha)
        self.marker['controllerProcessId'] = -1
        (self.root / 'owned-history.json').write_text(json.dumps(self.marker))
        with self.assertRaises(ValueError): diagnostics.validate_owner(self.root, 'owned-nonce', 'a' * 40)
        self.assertEqual(before, diagnostics.store_hashes(self.root))

    def test_preparation_refuses_linked_or_unowned_store_before_any_native_client_call(self):
        original = self.data / 'external.db'; original.write_bytes(b'preserve external store')
        self.database.unlink(); self.database.symlink_to(original)
        with self.assertRaises(ValueError): diagnostics.validate_owner(self.root, 'owned-nonce', 'a' * 40)
        self.assertEqual(b'preserve external store', original.read_bytes())
        with tempfile.TemporaryDirectory(prefix='unowned-') as external:
            with self.assertRaises(ValueError): diagnostics.validate_owner(Path(external).resolve(), 'owned-nonce', 'a' * 40)

    def report(self):
        artifact = self.data / 'artifact'; artifact.write_text('retained evidence')
        required = ['trace', 'native-preparation', 'diagnostic-facts', 'disabled-profile'] + [f'{capture}.{suffix}' for capture in diagnostics.CAPTURES for suffix in ('png', 'aria.yml')]
        return {'suites': [{'specs': [{'title': diagnostics.JOURNEY, 'tests': [
            {'projectName': project, 'expectedStatus': 'passed', 'status': 'expected', 'results': [{'status': 'passed',
                'attachments': [{'name': name, 'path': str(artifact)} for name in required]}]} for project in diagnostics.PROJECTS]}]}]}

    def test_exact_two_diagnostics_cases_and_every_named_native_visual_receipt_are_required(self):
        self.assertEqual(2, diagnostics.verdict(self.report(), self.data))
        for mutation in ('missing', 'duplicate', 'skip', 'retry', 'missing-capture', 'external-artifact', 'runner-error'):
            with self.subTest(mutation=mutation):
                report = self.report(); tests = report['suites'][0]['specs'][0]['tests']
                if mutation == 'missing': tests.pop()
                if mutation == 'duplicate': tests.append(tests[0])
                if mutation == 'skip': tests[0]['results'][0]['status'] = 'skipped'
                if mutation == 'retry': tests[0]['results'].append(tests[0]['results'][0])
                if mutation == 'missing-capture': tests[0]['results'][0]['attachments'].pop()
                if mutation == 'external-artifact': tests[0]['results'][0]['attachments'][0]['path'] = str(self.data.parent / 'external-proof')
                if mutation == 'runner-error': report['errors'] = [{'message': 'owned host stopped'}]
                with self.assertRaises(ValueError): diagnostics.verdict(report, self.data)

    def test_disabled_and_same_store_restart_operations_cannot_be_reordered_or_repeated(self):
        requests = self.data / 'requests'
        # No subprocess/host is necessary to reject an out-of-order private request.
        supervisor = diagnostics.DiagnosticsSupervisor(source_root=self.data, output=self.data, data=self.data,
            source_sha='a' * 40, tool=self.data / 'tool.dll', command=lambda *args, **kwargs: self.fail('native client must not run'),
            host=lambda *args: self.fail('host must not start'), prepare_host=lambda *args: self.fail('host copy must not run'),
            clean_environment=lambda: {}, free_port=lambda: 1)
        for operation in ('prepare', 'disabled'):
            (requests / 'request.json').write_text(json.dumps({'id': '12345678-1234-1234-1234-123456789012', 'project': 'diagnostics-desktop-light', 'operation': operation}))
            with self.subTest(operation=operation), self.assertRaises(ValueError): supervisor.supervise()
        supervisor.started.add('diagnostics-desktop-light')
        (requests / 'request.json').write_text(json.dumps({'id': '12345678-1234-1234-1234-123456789012', 'project': 'diagnostics-desktop-light', 'operation': 'start'}))
        with self.assertRaises(ValueError): supervisor.supervise()


if __name__ == '__main__':
    unittest.main()
