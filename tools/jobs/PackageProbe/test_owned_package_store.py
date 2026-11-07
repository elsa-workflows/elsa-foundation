"""Pure package-witness cleanup controls; no Docker, host, SDK or database is used."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('jobs_package_witness', Path(__file__).with_name('run_witness.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class OwnedPackageCreationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.owner = module.Witness(Path('/unused/Jobs.PackageProbe.dll'), Path(temporary.name), 'postgresql')
        self.identity = 'b' * 64
        self.present = False
        self.foreign_label = None
        self.foreign_name = False
        self.inventory_error = False
        self.inventory_identity = self.identity
        self.removed = []

    def docker(self, arguments, timeout=60):
        if arguments[0] == 'run':
            proof = json.loads((self.owner.evidence / 'owned-container.json').read_text())
            self.assertTrue(proof['creationAttempted'])
            self.assertEqual(self.owner.container_name, proof['name'])
            self.assertEqual(self.owner.container_labels, proof['labels'])
            self.present = True
            raise subprocess.TimeoutExpired('docker run', timeout)
        if arguments[0] == 'ps':
            if self.inventory_error:
                raise subprocess.CalledProcessError(1, ['docker', 'ps'])
            if '--filter' in arguments:
                self.assertIn('name=^/' + self.owner.container_name + '$', arguments)
            return self.inventory_identity if self.present else ''
        if arguments[0] == 'inspect':
            self.assertEqual(self.identity, arguments[-1])
            if arguments[2] == '{{.Name}}':
                return '/foreign-name' if self.foreign_name else '/' + self.owner.container_name
            if self.foreign_label == 'missing':
                return 'null'
            labels = dict(self.owner.container_labels)
            if self.foreign_label:
                labels[self.foreign_label] = 'foreign'
            return json.dumps(labels)
        if arguments[0] == 'rm':
            self.assertEqual(self.identity, arguments[-1])
            self.removed.append(arguments[-1])
            self.present = False
            return self.identity
        self.fail('Unexpected fake Docker command: ' + str(arguments))

    def lose_create_ack(self):
        with self.assertRaises(subprocess.TimeoutExpired):
            self.owner.initialize_store()
        self.assertTrue(self.owner.container_creation_attempted)
        self.assertIsNone(self.owner.container)
        self.assertFalse((self.owner.evidence / 'owned-postgres.env').exists())

    @staticmethod
    def fake_logs(arguments, **options):
        options['stdout'].write('FAKE OWNED CONTAINER LOG\n')
        return subprocess.CompletedProcess(arguments, 0)

    def refused_cleanup(self):
        with self.assertRaises(RuntimeError):
            self.owner.close()
        self.assertEqual([], self.removed)
        self.assertEqual('FAILED', self.owner.container_proof['cleanupState'])
        self.assertNotIn('exactContainerAbsent', self.owner.container_proof)

    def test_lost_create_reply_recovers_exact_owned_id_and_confirms_removal(self):
        with patch.object(self.owner, 'docker', side_effect=self.docker), patch.object(module.subprocess, 'run', side_effect=self.fake_logs):
            self.lose_create_ack()
            self.owner.close()
        self.assertEqual([self.identity], self.removed)
        self.assertEqual('EXACT_OWNED_CONTAINER_RECOVERED', self.owner.container_proof['creationReconciliation'])
        self.assertEqual('PASSED', self.owner.container_proof['cleanupState'])
        self.assertTrue(self.owner.container_proof['exactContainerAbsent'])

    def test_every_original_label_and_missing_labels_are_required_before_adoption(self):
        for label in (*self.owner.container_labels, 'missing'):
            with self.subTest(label=label), patch.object(self.owner, 'docker', side_effect=self.docker):
                self.foreign_label = label
                self.lose_create_ack()
                self.refused_cleanup()
                self.assertIsNone(self.owner.container)

    def test_foreign_exact_name_is_never_adopted_or_removed(self):
        self.foreign_name = True
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.lose_create_ack()
            self.refused_cleanup()
        self.assertIsNone(self.owner.container)

    def test_foreign_name_reservation_failure_never_grants_deletion_authority(self):
        self.present = True
        self.foreign_label = 'elsa.qualification.run'

        def reservation(arguments, timeout=60):
            if arguments[0] == 'run':
                raise subprocess.CalledProcessError(125, ['docker', 'run'])
            return self.docker(arguments, timeout)

        with patch.object(self.owner, 'docker', side_effect=reservation):
            with self.assertRaises(subprocess.CalledProcessError):
                self.owner.initialize_store()
            self.refused_cleanup()
        self.assertTrue(self.present)
        self.assertIsNone(self.owner.container)

    def test_failed_inventory_cannot_be_promoted_to_absence(self):
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.lose_create_ack()
            self.inventory_error = True
            self.refused_cleanup()
        self.assertEqual('UNRESOLVED', self.owner.container_proof['creationReconciliation'])

    def test_successful_empty_exact_name_inventory_confirms_no_created_container(self):
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.lose_create_ack()
            self.present = False
            self.owner.close()
        self.assertEqual([], self.removed)
        self.assertEqual('CONFIRMED_ABSENT', self.owner.container_proof['creationReconciliation'])
        self.assertTrue(self.owner.container_proof['exactContainerAbsent'])

    def test_short_or_multiple_inventory_ids_never_grant_removal_authority(self):
        for identity in ('abcdef', self.identity + '\n' + 'c' * 64):
            with self.subTest(identity=identity), patch.object(self.owner, 'docker', side_effect=self.docker):
                self.inventory_identity = identity
                self.lose_create_ack()
                self.refused_cleanup()
                self.assertIsNone(self.owner.container)

    def test_known_id_with_foreign_labels_is_not_removed(self):
        self.owner.container_creation_attempted = True
        self.owner.container = self.identity
        self.present = True
        self.foreign_label = 'elsa.qualification.run'
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.refused_cleanup()


if __name__ == '__main__':
    unittest.main()
