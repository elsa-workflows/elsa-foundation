"""Pure hosted-stage controls: no .NET, Docker, process launch or engine execution."""
import hashlib
import json
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

import postgres_source
import runner as entrypoint
import stages


class StageControls(unittest.TestCase):
    def test_platform_pin_keeps_digest_distinct_from_config_identity(self):
        image = {'Id': 'sha256:' + 'a' * 64, 'RepoDigests': ['postgres@sha256:' + 'b' * 64],
                 'Os': 'linux', 'Architecture': 'arm64'}
        result = postgres_source.image_identity(image, 'daemon-owned', 'arm64')
        self.assertNotEqual(result['expectedImageId'].split(':')[-1], result['image'].split(':')[-1])
        with self.assertRaises(ValueError):
            postgres_source.image_identity(image, 'daemon-owned', 'amd64')
        with self.assertRaises(ValueError):
            postgres_source.image_identity(dict(image, Id='sha256:bad'), 'daemon-owned', 'arm64')
        with self.assertRaises(ValueError):
            postgres_source.image_identity(dict(image, RepoDigests=image['RepoDigests'] * 2), 'daemon-owned', 'arm64')

    def test_native_platform_only(self):
        for machine, expected in [('x86_64', 'amd64'), ('aarch64', 'arm64'), ('arm64', 'arm64')]:
            with patch.object(postgres_source.platform, 'machine', return_value=machine):
                self.assertEqual(expected, postgres_source.host_architecture())
        with patch.object(postgres_source.platform, 'machine', return_value='unknown'):
            with self.assertRaises(ValueError):
                postgres_source.host_architecture()

    def test_source_pin_updates_controller_and_snapshot_fixture(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary)
            freezer, fixture = source / postgres_source.FREEZER, source / postgres_source.FIXTURE
            freezer.parent.mkdir(parents=True)
            fixture.parent.mkdir(parents=True)
            freezer.write_text("IMAGE = '" + postgres_source.OLD_DIGEST + "'\nIMAGE_ID = 'sha256:f4610795587492cd4fb887c2dc3de301b813278bb6f616992061141b5f15ac36'\nDAEMON_ID = '63366764-a397-4546-a171-598c97f3e765'\n")
            fixture.write_text('private const string ExpectedImage = "' + postgres_source.OLD_DIGEST + '";')
            identity = {'image': 'postgres@sha256:' + 'b' * 64, 'expectedImageId': 'sha256:' + 'a' * 64,
                        'expectedDaemonId': 'host-daemon'}
            changes = postgres_source.adapt(source, identity, True)
            self.assertEqual(2, len(changes))
            self.assertIn(identity['image'], freezer.read_text())
            self.assertIn(identity['expectedImageId'], freezer.read_text())
            self.assertIn(identity['image'], fixture.read_text())
            self.assertTrue(all(row['beforeSha256'] != row['afterSha256'] for row in changes))
            with self.assertRaises(ValueError):
                postgres_source.adapt(source, identity, True)

    def test_export_excludes_secrets_even_when_cleanup_failed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            private, evidence = root / 'private', root / 'evidence'
            private.mkdir()
            (private / 'postgres.env').write_text('POSTGRES_PASSWORD=secret')
            (private / 'snapshot-admin.connection').write_text('Password=secret;')
            (private / 'worker.failure.log').write_text('protected exception')
            (private / 'unsafe.log').write_text('Password=secret;')
            (private / 'controller.py').write_text("connection = 'Password=' + generated\n")
            original = b'{"status":"INCOMPLETE"}\n'
            (private / 'phase.json').write_bytes(original)
            destination = Path(stages.collect_private(SimpleNamespace(evidence=evidence), private, 'failed'))
            self.assertEqual(original, (destination / 'phase.json').read_bytes())
            self.assertTrue((destination / 'controller.py').exists())
            self.assertFalse((destination / 'postgres.env').exists())
            self.assertFalse((destination / 'snapshot-admin.connection').exists())
            self.assertFalse((destination / 'unsafe.log').exists())
            self.assertFalse((destination / 'worker.failure.log').exists())
            manifest = json.loads((destination / 'export.json').read_bytes())
            row = next(row for row in manifest['files'] if row['path'] == 'phase.json')
            self.assertEqual(hashlib.sha256(original).hexdigest(), row['sha256'])
            self.assertEqual(4, len(manifest['excludedPrivateFiles']))

    def test_failed_generator_is_retained_and_cannot_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            evidence, private = root / 'evidence', root / 'private'
            evidence.mkdir()
            private.mkdir()
            (private / 'partial.json').write_text('{}')
            fake = SimpleNamespace(evidence=evidence, state={}, save=lambda: None)
            with patch.object(entrypoint, 'command', side_effect=RuntimeError('synthetic freeze failure')):
                with self.assertRaises(RuntimeError):
                    stages.execute_packet(fake, private, 'launch_phase.py', ['generator'], 'failed', False)
            row = fake.state['controllers']['failed']
            self.assertEqual('FAILED', row['status'])
            self.assertTrue((Path(row['exportPath']) / 'partial.json').exists())

    def test_cleanup_failure_cannot_qualify_controller(self):
        for cleanup in (False, True):
            with self.subTest(cleanup=cleanup), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                private, evidence = root / 'private', root / 'evidence'
                private.mkdir()
                evidence.mkdir()
                fake = SimpleNamespace(evidence=evidence, state={}, save=lambda: None)

                def mock_command(argv, *_args, **_kwargs):
                    if argv[0] == 'generator':
                        (private / 'packet').mkdir()
                        (private / 'packet/manifest.json').write_text('{}')
                    else:
                        (private / 'output').mkdir()
                        (private / 'output/phase.json').write_text(json.dumps({'status': 'COMMANDS_PASSED',
                            'processQuiescent': True, 'containerRemoved': cleanup, 'secretRemoved': cleanup}))
                    return 0

                with patch.object(entrypoint, 'command', side_effect=mock_command):
                    if cleanup:
                        self.assertEqual('PASSED', stages.execute_packet(fake, private, 'postgres_phase.py', ['generator'], 'case', True)['status'])
                    else:
                        with self.assertRaises(RuntimeError):
                            stages.execute_packet(fake, private, 'postgres_phase.py', ['generator'], 'case', True)
                        self.assertEqual('FAILED', fake.state['controllers']['case']['status'])

    def test_stale_build_source_rejected_before_proof(self):
        with tempfile.TemporaryDirectory() as temporary:
            source = Path(temporary)
            path = source / 'manifest.json'
            path.write_text(json.dumps({'head': 'old', 'tree': 'oldtree'}))
            fake = SimpleNamespace(state={'variants': {'baseline': {'commit': 'new', 'tree': 'newtree',
                'buildStatus': 'PASSED', 'buildReceipt': {'manifestPath': str(path)}, 'binary': 'probe.dll'}}},
                refresh_source=lambda _variant: source)
            with self.assertRaises(ValueError):
                stages.selected_binary(fake, 'baseline')

    def test_absent_qualification_cannot_enable_measurements(self):
        fake = SimpleNamespace(state={'variants': {'runview': {}, 'snapshot': {}}})
        with self.assertRaises(ValueError):
            stages.require_measurement_gates(fake, 'Balanced')


if __name__ == '__main__':
    unittest.main()
