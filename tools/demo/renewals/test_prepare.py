"""Boundary checks for isolated demo bootstrap; never starts or stops a host."""
import importlib.util
import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


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

    def test_baseline_bootstrap_refuses_a_shared_update_without_touching_it(self):
        shared = self.prepare.shared_update_feed()
        shared.mkdir(parents=True)
        update = shared / 'Elsa.Samples.Nuplane.Renewals.1.1.0.nupkg'
        update.write_bytes(b'pending shared update')
        original_shells = (self.host / 'shells.json').read_bytes()

        with self.assertRaisesRegex(RuntimeError, 'refusing to expose them during baseline bootstrap'):
            self.prepare.bootstrap_host('a', initialize=True)

        self.assertEqual(update.read_bytes(), b'pending shared update')
        self.assertEqual((self.host / 'shells.json').read_bytes(), original_shells)
        self.assertFalse((self.host / 'ready-shells.json').exists())
        self.assertTrue((self.host / '.nuplane/store-state.json').exists())

    def test_both_hosts_watch_one_shared_update_feed_and_keep_private_baseline_feeds(self):
        feeds_a = self.prepare.nuplane_feeds('a')
        feeds_b = self.prepare.nuplane_feeds('b')
        update_a = next(feed for feed in feeds_a if feed['Name'] == 'renewal-demo')
        update_b = next(feed for feed in feeds_b if feed['Name'] == 'renewal-demo')
        baseline_a = next(feed for feed in feeds_a if feed['Name'] == 'renewal-demo-baseline')
        baseline_b = next(feed for feed in feeds_b if feed['Name'] == 'renewal-demo-baseline')

        self.assertEqual(update_a['DirectoryPath'], str(self.prepare.shared_update_feed()))
        self.assertEqual(update_a['DirectoryPath'], update_b['DirectoryPath'])
        self.assertTrue(update_a['Directory']['Watch'])
        self.assertTrue(update_b['Directory']['Watch'])
        self.assertNotEqual(baseline_a['DirectoryPath'], baseline_b['DirectoryPath'])
        # Nuplane's DesiredStateAggregator picks the alphabetically first SourceName
        # when both feeds contain the same package ID; keep shared updates first.
        self.assertLess(update_a['Name'].casefold(), baseline_a['Name'].casefold())
        self.assertLess(update_b['Name'].casefold(), baseline_b['Name'].casefold())

    def test_prepare_clears_only_owned_shared_update_archives(self):
        shared = self.prepare.shared_update_feed()
        shared.mkdir(parents=True)
        renewal_archive = shared / 'Elsa.Samples.Nuplane.Renewals.1.1.0.nupkg'
        renewal_partial = shared / 'Elsa.Samples.Nuplane.Renewals.Activities.1.1.0.nupkg.tmp'
        unrelated_archive = shared / 'Elsewhere.Package.2.0.0.nupkg'
        unrelated_partial = shared / 'Elsewhere.Package.2.0.0.nupkg.tmp'
        for item in (renewal_archive, renewal_partial, unrelated_archive, unrelated_partial):
            item.write_bytes(b'package')
        stopped_checks = []
        self.prepare.require_stopped = lambda: stopped_checks.append(True)

        self.prepare.clear_shared_updates()

        self.assertEqual(len(stopped_checks), 1)
        self.assertFalse(renewal_archive.exists())
        self.assertFalse(renewal_partial.exists())
        self.assertEqual(unrelated_archive.read_bytes(), b'package')
        self.assertEqual(unrelated_partial.read_bytes(), b'package')

    def stage_release(self, release):
        stage = self.prepare.DEMO / 'staging' / str(release)
        stage.mkdir(parents=True, exist_ok=True)
        for module, _ in self.prepare.MODULES:
            (stage / f'{module}.1.{release - 1}.0.nupkg').write_bytes(f'release {release}'.encode())
        return stage

    def test_release_two_publishes_both_packages_once_to_the_shared_feed(self):
        self.stage_release(2)

        self.prepare.publish('a', 2)

        shared = self.prepare.shared_update_feed()
        names = {path.name for path in shared.glob('*.nupkg')}
        self.assertEqual(names, {f'{module}.1.1.0.nupkg' for module, _ in self.prepare.MODULES})
        self.assertEqual({path.read_bytes() for path in shared.glob('*.nupkg')}, {b'release 2'})
        private_baseline = {f'{module}.1.0.0.nupkg' for module, _ in self.prepare.MODULES} | {'Elsa.Samples.Nuplane.Demo.Identity.0.1.0.nupkg'}
        host_a_feed = self.prepare.DEMO / 'hosts/a/feed'
        self.assertEqual({path.name for path in host_a_feed.glob('*.nupkg')}, private_baseline)
        for module, _ in self.prepare.MODULES:
            self.assertEqual((host_a_feed / f'{module}.1.0.0.nupkg').read_bytes(), b'baseline package')
        self.assertFalse(list((self.prepare.DEMO / 'hosts/b/feed').glob('*.nupkg')))
        self.assertFalse(list(shared.glob('*.tmp')))

    def test_release_one_publications_remain_host_private(self):
        self.stage_release(1)

        self.prepare.publish('a', 1)
        self.prepare.publish('b', 1)

        renewal_packages = {f'{module}.1.0.0.nupkg' for module, _ in self.prepare.MODULES}
        host_a_feed = self.prepare.DEMO / 'hosts/a/feed'
        self.assertEqual({path.name for path in host_a_feed.glob('*.nupkg')}, renewal_packages | {'Elsa.Samples.Nuplane.Demo.Identity.0.1.0.nupkg'})
        host_b_feed = self.prepare.DEMO / 'hosts/b/feed'
        self.assertEqual({path.name for path in host_b_feed.glob('*.nupkg')}, renewal_packages)
        for host_feed in (host_a_feed, host_b_feed):
            for module, _ in self.prepare.MODULES:
                self.assertEqual((host_feed / f'{module}.1.0.0.nupkg').read_bytes(), b'release 1')
        self.assertFalse(list(self.prepare.shared_update_feed().glob('*.nupkg')))

    def test_partial_setup_retry_keeps_the_complete_composition(self):
        self.prepare.bootstrap_host('a', initialize=True)
        self.prepare.write_json(self.host / 'shells.json', self.shells)
        self.prepare.bootstrap_host('a')
        self.assertEqual(json.loads((self.host / 'ready-shells.json').read_text()), self.shells)
        self.assertTrue((self.host / 'bootstrap-pending').exists())
        self.assertNotIn('RenewalsActivities', json.loads((self.host / 'shells.json').read_text())['CShells']['Shells']['default']['Features'])


class RehearsalModuleTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location('demo_rehearse', Path(__file__).with_name('rehearse.py'))
        self.rehearse = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = self.rehearse
        self.addCleanup(sys.modules.pop, spec.name, None)
        spec.loader.exec_module(self.rehearse)


class RehearsalInstallEvidenceTests(RehearsalModuleTests):
    def setUp(self):
        super().setUp()
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.rehearse.DEMO = Path(directory.name)
        self.state_path = self.rehearse.DEMO / 'hosts/a/.nuplane/store-state.json'

    def write_state(self, versions):
        self.state_path.parent.mkdir(parents=True, exist_ok=True)
        self.state_path.write_text(json.dumps({'activeVersionById': versions}))

    def test_installed_version_requires_both_packages_at_the_same_version(self):
        core, activities = self.rehearse.RENEWAL_PACKAGE_IDS
        self.write_state({core: '1.1.0', activities: '1.1.0'})

        version, evidence = self.rehearse.installed_version('a')

        self.assertEqual(version, '1.1.0')
        self.assertEqual(evidence['versions'], {core: '1.1.0', activities: '1.1.0'})

    def test_installed_version_reports_missing_or_mismatched_packages_as_incomplete(self):
        core, activities = self.rehearse.RENEWAL_PACKAGE_IDS
        self.write_state({core: '1.1.0'})

        version, missing = self.rehearse.installed_version('a')

        self.assertIsNone(version)
        self.assertEqual(missing['versions'], {core: '1.1.0', activities: None})
        self.assertIn('both renewal packages', missing['error'])

        self.write_state({core: '1.1.0', activities: '1.0.0'})
        version, mismatch = self.rehearse.installed_version('a')

        self.assertIsNone(version)
        self.assertEqual(mismatch['versions'], {core: '1.1.0', activities: '1.0.0'})
        self.assertIn('do not match', mismatch['error'])


class ApiRequestTimeoutTests(RehearsalModuleTests):
    class RecordingOpener:
        def __init__(self):
            self.timeouts = []

        def open(self, _request, *, timeout):
            self.timeouts.append(timeout)

            class ResponseContext:
                status = 200

                def __enter__(self):
                    return self

                def __exit__(self, *_args):
                    return False

                @staticmethod
                def read():
                    return b'{}'

            return ResponseContext()

    def setUp(self):
        super().setUp()
        self.api = self.rehearse.Api('test-management-key')
        self.opener = self.RecordingOpener()
        self.api.opener = self.opener

    def test_request_uses_separate_bounded_defaults_for_api_and_management_calls(self):
        self.api.request('a', '/health/ready', expected={200})
        self.api.request('a', '/_module-management/reload', method='POST', management=True, expected={200})

        self.assertEqual(self.opener.timeouts, [30, 600])

    def test_explicit_request_timeout_overrides_each_default(self):
        self.api.request('a', '/health/ready', expected={200}, timeout=7)
        self.api.request('a', '/_module-management/reload', method='POST', management=True, expected={200}, timeout=725)

        self.assertEqual(self.opener.timeouts, [7, 725])


class SharedInstallWaitTests(RehearsalModuleTests):
    def test_one_bounded_wait_confirms_both_independent_stores(self):
        counts = {'a': 0, 'b': 0}
        current_time = [0.0]

        def read_store(host):
            counts[host] += 1
            version = '1.1.0' if host == 'a' or counts[host] > 1 else '1.0.0'
            return version, {'installedVersion': version}

        with patch.object(self.rehearse, 'installed_version', side_effect=read_store), \
                patch.object(self.rehearse.time, 'monotonic', side_effect=lambda: current_time[0]), \
                patch.object(self.rehearse.time, 'sleep', side_effect=lambda seconds: current_time.__setitem__(0, current_time[0] + seconds)):
            installed = self.rehearse.wait_for_shared_installation('1.1.0')

        self.assertEqual(installed['a'][0], '1.1.0')
        self.assertEqual(installed['b'][0], '1.1.0')
        self.assertEqual(counts, {'a': 2, 'b': 2})
        self.assertEqual(current_time[0], 2)

    def test_shared_install_timeout_is_fifteen_minutes_and_retains_both_store_observations(self):
        current_time = [0.0]
        reads = []

        def read_store(host):
            reads.append(host)
            return '1.0.0', {'installedVersion': '1.0.0', 'host': host}

        with patch.object(self.rehearse, 'installed_version', side_effect=read_store), \
                patch.object(self.rehearse.time, 'monotonic', side_effect=lambda: current_time[0]), \
                patch.object(self.rehearse.time, 'sleep', side_effect=lambda seconds: current_time.__setitem__(0, current_time[0] + seconds)):
            with self.assertRaises(self.rehearse.RehearsalFailure) as caught:
                self.rehearse.wait_for_shared_installation('1.1.0')

        self.assertEqual(self.rehearse.SHARED_INSTALL_TIMEOUT, 900)
        self.assertEqual(current_time[0], 900)
        self.assertEqual(len(reads), 900)
        self.assertEqual(caught.exception.evidence['a'][1]['installedVersion'], '1.0.0')
        self.assertEqual(caught.exception.evidence['b'][1]['installedVersion'], '1.0.0')


if __name__ == '__main__':
    unittest.main()
