"""Pure owned-container lifecycle controls; no Docker, process, socket or database is used."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location('owned_model_store', Path(__file__).with_name('owned_model_store.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class OwnedCreationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.owner = module.OwnedModelStores(Path(self.temporary.name), 'ack-loss-control')
        self.identity = 'a' * 64
        self.present = False
        self.foreign = False
        self.inventory_error = False
        self.removed = []

    def docker(self, arguments, timeout=45):
        if arguments[0] == 'run':
            self.present = True
            raise subprocess.TimeoutExpired('docker run', timeout)
        if arguments[0] == 'ps':
            if self.inventory_error:
                raise subprocess.CalledProcessError(1, ['docker', 'ps'])
            return self.identity if self.present else ''
        if arguments[0] == 'inspect':
            if arguments[2] == '{{.Name}}':
                return '/' + self.owner.container_name
            if self.foreign == 'null':
                return 'null'
            labels = dict(self.owner.labels)
            if self.foreign:
                labels['elsa.qualification.run'] = 'foreign-run'
            return json.dumps(labels)
        if arguments[0] == 'rm':
            self.assertEqual(self.identity, arguments[-1])
            self.removed.append(arguments[-1])
            self.present = False
            return self.identity
        self.fail('Unexpected fake Docker command: ' + str(arguments))

    def lose_create_ack(self):
        with self.assertRaises(subprocess.TimeoutExpired):
            self.owner.create()
        self.assertTrue(self.owner.creation_attempted)
        self.assertIsNone(self.owner.container)
        self.assertFalse((self.owner.evidence / 'owned-postgres.env').exists())

    @staticmethod
    def retained_export(arguments, **options):
        output = options['stdout']
        output.write(b'PGDMP-FAKE-CONTROL' if 'pg_dump' in arguments else 'FAKE OWNED LOG\n')
        return subprocess.CompletedProcess(arguments, 0)

    def test_lost_create_ack_recovers_only_exact_owned_id_before_removal_and_confirms_absence(self):
        with patch.object(self.owner, 'docker', side_effect=self.docker), patch.object(module.subprocess, 'run', side_effect=self.retained_export):
            self.lose_create_ack()
            self.assertTrue(self.owner.close())
        self.assertEqual([self.identity], self.removed)
        self.assertEqual('EXACT_OWNED_CONTAINER_RECOVERED', self.owner.proof['creationReconciliation'])
        self.assertEqual('PASSED', self.owner.proof['cleanupState'])
        self.assertTrue(self.owner.proof['exactContainerAbsent'])

    def test_uncertain_create_never_adopts_foreign_labels(self):
        self.foreign = True
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.lose_create_ack()
            self.assertFalse(self.owner.close())
        self.assertTrue(self.present)
        self.assertEqual([], self.removed)
        self.assertIsNone(self.owner.container)
        self.assertEqual('FAILED', self.owner.proof['cleanupState'])
        self.assertNotIn('exactContainerAbsent', self.owner.proof)

    def test_foreign_name_reservation_failure_never_grants_deletion_authority(self):
        self.foreign = self.present = True
        def reservation(arguments, timeout=45):
            if arguments[0] == 'run':
                raise subprocess.CalledProcessError(125, ['docker', 'run'])
            return self.docker(arguments, timeout)
        with patch.object(self.owner, 'docker', side_effect=reservation):
            with self.assertRaises(subprocess.CalledProcessError):
                self.owner.create()
            self.assertFalse(self.owner.close())
        self.assertEqual([], self.removed)
        self.assertTrue(self.present)
        self.assertIsNone(self.owner.container)
        self.assertEqual('FAILED', self.owner.proof['cleanupState'])

    def test_failed_daemon_query_cannot_be_promoted_to_absence(self):
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.lose_create_ack()
            self.inventory_error = True
            self.assertFalse(self.owner.close())
        self.assertEqual([], self.removed)
        self.assertEqual('UNRESOLVED', self.owner.proof['creationReconciliation'])
        self.assertEqual('FAILED', self.owner.proof['cleanupState'])

    def test_missing_labels_refuse_reconciliation_without_removing_the_named_container(self):
        self.foreign = 'null'
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.lose_create_ack()
            self.assertFalse(self.owner.close())
        self.assertTrue(self.present)
        self.assertEqual([], self.removed)
        self.assertEqual('FAILED', self.owner.proof['cleanupState'])

    def test_successful_empty_exact_name_query_confirms_failed_creation_left_no_container(self):
        with patch.object(self.owner, 'docker', side_effect=self.docker):
            self.lose_create_ack()
            self.present = False
            self.assertTrue(self.owner.close())
        self.assertEqual([], self.removed)
        self.assertEqual('CONFIRMED_ABSENT', self.owner.proof['creationReconciliation'])
        self.assertTrue(self.owner.proof['exactContainerAbsent'])


if __name__ == '__main__':
    unittest.main()
