import base64
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest

from publication_snapshot import TABLES, readonly_database, snapshot


class PublicationSnapshotTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.database = self.root / 'owned.db'
        self.connection = sqlite3.connect(self.database)
        self.addCleanup(self.connection.close)
        self.connection.execute('PRAGMA journal_mode=WAL')
        self.owner = base64.b64encode('owned'.encode('utf-16-le')).decode()
        for table, column in TABLES.items():
            self.connection.execute(f'CREATE TABLE "{table}" (Id TEXT, "{column}" TEXT, Revision INTEGER)')
            value = json.dumps({'rootActivity': {'authoredActivityId': base64.b64encode('unique-root'.encode('utf-16-le')).decode()}}) if column == 'ContentJson' else self.owner
            self.connection.execute(f'INSERT INTO "{table}" VALUES (?, ?, ?)', ('row', value, 1))
            unrelated = json.dumps({'rootActivity': {'authoredActivityId': base64.b64encode('other-root'.encode('utf-16-le')).decode()}}) if column == 'ContentJson' else 'unrelated'
            self.connection.execute(f'INSERT INTO "{table}" VALUES (?, ?, ?)', ('other', unrelated, 1))
        self.connection.commit()

    def read(self):
        return snapshot(self.root, 'owned', 'unique-root', require_published=True)

    def test_positive_control_scopes_all_four_tables(self):
        result = self.read()
        self.assertEqual(set(TABLES), set(result))
        self.assertTrue(all(item['count'] == 1 for item in result.values()))

    def test_committed_wal_update_changes_fingerprint_without_count_change(self):
        before = self.read()
        self.connection.execute('UPDATE elsa_publication_records SET Revision=2 WHERE Id="row"')
        self.connection.commit()
        after = self.read()
        self.assertEqual(before['elsa_publication_records']['count'], after['elsa_publication_records']['count'])
        self.assertNotEqual(before, after)

    def test_unrelated_rows_do_not_change_owned_snapshot(self):
        before = self.read()
        self.connection.execute('UPDATE elsa_publication_records SET Revision=2 WHERE Id="other"')
        self.connection.commit()
        self.assertEqual(before, self.read())

    def test_orphan_artifact_is_visible_without_source_reference(self):
        before = self.read()
        self.connection.execute('INSERT INTO elsa_runtime_workflow_executable VALUES (?, ?, ?)',
                                ('orphan', json.dumps({'rootActivity': {'authoredActivityId': base64.b64encode('unique-root'.encode('utf-16-le')).decode()}}), 1))
        self.connection.commit()
        self.assertNotEqual(before, self.read())

    def test_missing_table_and_empty_positive_control_fail_closed(self):
        with self.assertRaisesRegex(ValueError, 'positive control'):
            snapshot(self.root, 'missing', 'missing', require_published=True)
        self.connection.execute('DROP TABLE elsa_publication_records')
        self.connection.commit()
        with self.assertRaisesRegex(ValueError, 'Missing expected'):
            self.read()

    def test_query_only_connection_refuses_writes(self):
        with readonly_database(self.database) as connection:
            with self.assertRaises(sqlite3.OperationalError):
                connection.execute('DELETE FROM elsa_publication_records')
        self.assertEqual(1, self.read()['elsa_publication_records']['count'])


if __name__ == '__main__':
    unittest.main()
