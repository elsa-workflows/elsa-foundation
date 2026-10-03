"""Boundary checks for isolated demo bootstrap; never starts or stops a host."""
import importlib.util
import json
import os
import tempfile
import unittest
from pathlib import Path


class BootstrapTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('demo_prepare', Path(__file__).with_name('prepare.py'))
        self.prepare = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.prepare)
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.prepare.DEMO = Path(directory.name)
        self.prepare.HOSTS = {'a': 0}
        self.host = self.prepare.DEMO / 'hosts/a'
        (self.host / 'feed').mkdir(parents=True)
        (self.host / '.nuplane').mkdir()
        (self.host / '.nuplane/store-state.json').write_text('retained until safely stopped')
        self.shells = json.loads((self.prepare.ROOT / 'tools/demo/renewals/shells.template.json').read_text())
        self.prepare.write_json(self.host / 'shells.json', self.shells)
        for module, _ in self.prepare.MODULES:
            (self.host / 'feed' / (module + '.1.0.0.nupkg')).write_bytes(b'baseline package')
        (self.host / 'feed/Elsa.Samples.Nuplane.Demo.Identity.0.1.0.nupkg').write_bytes(b'platform package')
        (self.prepare.DEMO / 'renewals.db').write_bytes(b'existing renewal data')
        (self.prepare.DEMO / 'signing-key.pk8').write_bytes(b'existing key')

    def test_platform_bootstrap_preserves_data_keys_and_unrelated_packages(self):
        self.prepare.bootstrap_host('a', initialize=True)
        self.assertEqual((self.prepare.DEMO / 'renewals.db').read_bytes(), b'existing renewal data')
        self.assertEqual((self.prepare.DEMO / 'signing-key.pk8').read_bytes(), b'existing key')
        self.assertTrue((self.host / 'feed/Elsa.Samples.Nuplane.Demo.Identity.0.1.0.nupkg').is_file())
        self.assertFalse((self.host / '.nuplane').exists())
        ready = json.loads((self.host / 'ready-shells.json').read_text())
        self.assertEqual(ready, self.shells)
        self.assertEqual((self.host / 'ready-shells.json').stat().st_mode & 0o777, 0o600)
        self.assertNotIn('Renewals', json.loads((self.host / 'shells.json').read_text())['CShells']['Shells']['default']['Features'])

    def test_running_owned_pid_refuses_before_any_state_changes(self):
        original = (self.host / 'shells.json').read_bytes()
        (self.host / 'cockpit.pid').write_text(str(os.getpid()))
        with self.assertRaisesRegex(RuntimeError, 'still running'):
            self.prepare.bootstrap_host('a', initialize=True)
        self.assertEqual((self.host / 'shells.json').read_bytes(), original)
        self.assertTrue((self.host / '.nuplane/store-state.json').exists())
        self.assertFalse((self.host / 'ready-shells.json').exists())

    def test_retry_requires_a_pending_baseline_marker(self):
        with self.assertRaisesRegex(RuntimeError, 'no pending baseline bootstrap'):
            self.prepare.bootstrap_host('a')
        self.assertTrue((self.host / '.nuplane/store-state.json').exists())

    def test_partial_setup_retry_keeps_the_complete_composition(self):
        self.prepare.bootstrap_host('a', initialize=True)
        self.prepare.write_json(self.host / 'shells.json', self.shells)
        self.prepare.bootstrap_host('a')
        self.assertEqual(json.loads((self.host / 'ready-shells.json').read_text()), self.shells)
        self.assertTrue((self.host / 'bootstrap-pending').exists())
        self.assertNotIn('RenewalsActivities', json.loads((self.host / 'shells.json').read_text())['CShells']['Shells']['default']['Features'])


if __name__ == '__main__':
    unittest.main()
