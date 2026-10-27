"""Negative controls for the checked observer; consumes an explicitly supplied, owned actual-host fixture.

These controls do not qualify host recovery. Test-BpmnBroadcastRecovery.ps1 supplies that separate proof.
"""
import argparse, base64, hashlib, importlib.util, json, pathlib, sqlite3, subprocess, tempfile, unittest
from contextlib import closing
ROOT = pathlib.Path(__file__).resolve().parents[2]
READER = DATABASE = MARKER = MEMBERSHIP = None
PUBLISHER_WORKFLOW = PUBLISHER_SOURCE = None
EXPECTED_RESUMES, EXPECTED_STARTS = 2, 1
spec=importlib.util.spec_from_file_location('recovery', ROOT/'e2e-tests/bpmn/inspect_broadcast_recovery.py'); recovery=importlib.util.module_from_spec(spec);spec.loader.exec_module(recovery)
def encode(s): return base64.b64encode(s.encode('utf-16le')).decode()
class CheckedRecoveryReaderTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  if READER is None:
   raise unittest.SkipTest('Supply an owned actual-host database, cut marker and built reader explicitly.')
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.db=pathlib.Path(self.tmp.name)/'snapshot.sqlite'
  with closing(sqlite3.connect(DATABASE.as_uri()+'?mode=ro',uri=True)) as c: recovery.copy_snapshot(c,self.db)
 def read(self, expected=None, identity=None):
  digest=hashlib.sha256(self.db.read_bytes()).hexdigest()
  r=subprocess.run(['dotnet',str(READER),str(self.db),expected or digest,MARKER['scope'],identity or MARKER['sourceOutboxItemId']],capture_output=True,text=True,timeout=30)
  return r.returncode,json.loads(r.stdout)
 def assert_refused(self, **kwargs):
  code,data=self.read(**kwargs);self.assertEqual(2,code);self.assertFalse(data['providerChecked']);self.assertEqual('ObservationUnavailable',data['status'])
 def mutate(self, operation):
  with closing(sqlite3.connect(self.db)) as c: operation(c);c.commit()
 def test_actual_source_and_generation_read_is_immutable(self):
  before=hashlib.sha256(self.db.read_bytes()).hexdigest();code,data=self.read();self.assertEqual(0,code);self.assertTrue(data['providerChecked']);self.assertEqual('Delivered',data['source']['status']);self.assertEqual(EXPECTED_RESUMES,len(data['source']['selectedResumes']));self.assertEqual(EXPECTED_STARTS,len(data['source']['selectedStarts']));self.assertEqual(before,hashlib.sha256(self.db.read_bytes()).hexdigest())
 def test_wrong_expected_hash_is_refused(self): self.assert_refused(expected='0'*64)
 def test_present_live_sidecar_is_refused(self):
  pathlib.Path(str(self.db)+'-wal').touch();self.assert_refused()
 def test_missing_exact_source_is_refused(self): self.assert_refused(identity='not-the-committed-source')
 def test_divergent_source_projection_is_refused(self):
  self.mutate(lambda c:c.execute('update elsa_runtime_post_commit_outbox set OutboxItemId=? where OutboxItemId=?',(encode('another-source'),encode(MARKER['sourceOutboxItemId']))));self.assert_refused()
 def test_changed_cohort_fingerprint_is_refused(self):
  def change(c):
   text=c.execute('select ContentJson from elsa_runtime_post_commit_outbox where OutboxItemId=?',(encode(MARKER['sourceOutboxItemId']),)).fetchone()[0]; item=json.loads(text);item['recipientSnapshot']['intentFingerprint']='changed';c.execute('update elsa_runtime_post_commit_outbox set ContentJson=? where OutboxItemId=?',(json.dumps(item),encode(MARKER['sourceOutboxItemId'])))
  self.mutate(change);self.assert_refused()
 def test_missing_publisher_is_refused(self):
  self.mutate(lambda c:c.execute('delete from elsa_runtime_workflow_execution_state where WorkflowExecutionId=?',(encode(MARKER['workflowExecutionId']),)));self.assert_refused()
 def test_pinned_wal_view_preserves_claim_despite_new_writer(self):
  # Snapshot mechanics control only; not recovery evidence. Another connection can commit after our read.
  with closing(sqlite3.connect(self.db)) as w:
   w.execute('pragma journal_mode=wal'); w.execute('create table observation_control (value integer)');w.execute('insert into observation_control values(1)');w.commit()
   with closing(sqlite3.connect(self.db.as_uri()+'?mode=ro',uri=True,isolation_level=None)) as r:
    r.execute('begin');self.assertEqual(1,r.execute('select value from observation_control').fetchone()[0]);w.execute('update observation_control set value=2');w.commit();copy=self.db.with_name('pinned.sqlite');recovery.copy_snapshot(r,copy);r.execute('rollback')
   with closing(sqlite3.connect(copy)) as v:self.assertEqual(1,v.execute('select value from observation_control').fetchone()[0])
class CheckedPublisherLookupTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  if PUBLISHER_WORKFLOW is None: raise unittest.SkipTest('Supply the exact owned publisher and source IDs explicitly.')
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.db=pathlib.Path(self.tmp.name)/'publisher.sqlite'
  with closing(sqlite3.connect(DATABASE.as_uri()+'?mode=ro',uri=True)) as c: recovery.copy_snapshot(c,self.db)
 def read(self, publisher=None):
  digest=hashlib.sha256(self.db.read_bytes()).hexdigest()
  r=subprocess.run(['dotnet',str(READER),'--find-publisher-source',str(self.db),digest,MARKER['scope'],publisher or PUBLISHER_WORKFLOW],capture_output=True,text=True,timeout=30)
  return r.returncode,json.loads(r.stdout)
 def refused(self, publisher=None):
  code,data=self.read(publisher);self.assertEqual(2,code);self.assertFalse(data['providerChecked']);self.assertEqual('ObservationUnavailable',data['status'])
 def test_exact_publisher_discovery_is_checked_and_immutable(self):
  before=hashlib.sha256(self.db.read_bytes()).hexdigest();code,data=self.read();self.assertEqual(0,code);self.assertEqual(PUBLISHER_SOURCE,data['source']['outboxItemId']);self.assertEqual(PUBLISHER_WORKFLOW,data['source']['workflowExecutionId']);self.assertTrue(data['providerChecked']);self.assertEqual(before,hashlib.sha256(self.db.read_bytes()).hexdigest())
 def test_unknown_publisher_is_refused(self): self.refused('not-the-owned-publisher')
 def test_divergent_publisher_projection_is_refused(self):
  with closing(sqlite3.connect(self.db)) as c:c.execute('update elsa_runtime_post_commit_outbox set WorkflowExecutionId=?,WorkflowExecutionIdHash=? where OutboxItemId=?',(encode('changed-projection'),hashlib.sha256('changed-projection'.encode('utf-16le')).hexdigest().upper(),encode(PUBLISHER_SOURCE)));c.commit()
  self.refused('changed-projection')
 def test_duplicate_candidate_projection_is_refused(self):
  with closing(sqlite3.connect(self.db)) as c:
   c.row_factory=sqlite3.Row;row=dict(c.execute('select * from elsa_runtime_post_commit_outbox where OutboxItemId=?',(encode(PUBLISHER_SOURCE),)).fetchone())
   row['Id']=encode('observer-duplicate-candidate');row['OutboxItemIdHash']='0'*64
   c.execute('insert into elsa_runtime_post_commit_outbox ('+','.join('"'+name+'"'for name in row)+') values ('+','.join('?'for _ in row)+')',list(row.values()));c.commit()
  self.refused()

class CheckedMembershipReaderTests(unittest.TestCase):
 @classmethod
 def setUpClass(cls):
  if MEMBERSHIP is None: raise unittest.SkipTest('Supply an owned actual-host membership database explicitly.')
 def setUp(self):
  self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.db=pathlib.Path(self.tmp.name)/'membership.sqlite'
  with closing(sqlite3.connect(MEMBERSHIP.as_uri()+'?mode=ro',uri=True)) as c: recovery.copy_snapshot(c,self.db)
 def read(self, expected=None):
  digest=hashlib.sha256(self.db.read_bytes()).hexdigest()
  r=subprocess.run(['dotnet',str(READER),'--inspect-membership',str(self.db),expected or digest],capture_output=True,text=True,timeout=30)
  return r.returncode,json.loads(r.stdout)
 def assert_refused(self, **kwargs):
  code,data=self.read(**kwargs);self.assertEqual(2,code);self.assertFalse(data['providerChecked']);self.assertEqual('ObservationUnavailable',data['status'])
 def test_actual_membership_read_is_immutable(self):
  before=hashlib.sha256(self.db.read_bytes()).hexdigest();code,data=self.read();self.assertEqual(0,code);self.assertTrue(data['providerChecked']);self.assertTrue(data['readOnly']);self.assertGreaterEqual(len(data['members']),2);self.assertTrue(all(m['isInterpretable'] for m in data['members']));self.assertEqual(before,hashlib.sha256(self.db.read_bytes()).hexdigest())
 def test_wrong_hash_is_refused(self): self.assert_refused(expected='0'*64)
 def test_live_sidecar_is_refused(self):
  pathlib.Path(str(self.db)+'-wal').touch();self.assert_refused()
 def test_uninterpretable_actual_row_is_refused(self):
  with closing(sqlite3.connect(self.db)) as c:c.execute("update elsa_cluster_members set SchemaVersion='unrecognized'");c.commit()
  self.assert_refused()
 def test_missing_membership_table_is_refused(self):
  with closing(sqlite3.connect(self.db)) as c:c.execute('drop table elsa_cluster_members');c.commit()
  self.assert_refused()

if __name__ == '__main__':
 parser = argparse.ArgumentParser(description=__doc__)
 parser.add_argument('--database', type=pathlib.Path, required=True)
 parser.add_argument('--marker', type=pathlib.Path, required=True)
 parser.add_argument('--reader', type=pathlib.Path, required=True)
 parser.add_argument('--expected-resumes', type=int, default=2)
 parser.add_argument('--expected-starts', type=int, default=1)
 parser.add_argument('--membership-database', type=pathlib.Path)
 parser.add_argument('--publisher-workflow')
 parser.add_argument('--publisher-source')
 options, remaining = parser.parse_known_args()
 EXPECTED_RESUMES, EXPECTED_STARTS = options.expected_resumes, options.expected_starts
 READER = options.reader.resolve(strict=True)
 DATABASE = options.database.resolve(strict=True)
 MARKER = json.loads(options.marker.read_text())
 MEMBERSHIP = options.membership_database.resolve(strict=True) if options.membership_database else None
 if bool(options.publisher_workflow) != bool(options.publisher_source): parser.error('Publisher workflow and source IDs must be supplied together.')
 PUBLISHER_WORKFLOW, PUBLISHER_SOURCE = options.publisher_workflow, options.publisher_source
 unittest.main(argv=[__file__, *remaining])
