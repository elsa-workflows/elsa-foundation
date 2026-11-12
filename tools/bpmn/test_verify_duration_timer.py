"""Read-only corrupt-copy controls for the actual wall-clock duration journeys."""
from contextlib import closing
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('duration_verifier',Path(__file__).with_name('verify_duration_timer.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/races/human-duration'

class ActualDurationTimerControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-duration-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)
    def rehash(self,case,name):
        p=self.root/'manifest.json';m=json.loads(p.read_text());row=next(c for c in m['cases'] if c['name']==case)
        next(a for a in row['assets'] if a['path']==case+'/'+name)['sha256']=v.q.digest(self.root/case/name);v.q.write_json(p,m)
    def change(self,case,name,action):
        p=self.root/case/name;s=json.loads(p.read_text());action(s);v.q.write_json(p,s);self.rehash(case,name)
    def store(self,case,phase,action):
        p=self.root/case/(phase+'-runtime.sqlite.gz');raw=Path(self.temp.name)/'db';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def update(s):
            snap=next(snap for snap in s['snapshots'] if snap['file']==p.name);snap['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();snap['gzipSha256']=v.q.digest(p)
        self.change(case,'process.json',update)
    def verify(self):return v.verify(self.root/'manifest.json')
    def test_actual_both_winners_keep_late_inputs_obsolete_after_real_process_loss(self):
        r=self.verify();self.assertEqual(8,r['wholeStoresVerified']);self.assertTrue(r['actualBothHumanTimerOrdersProved'])
        self.assertTrue(r['noNativeCarrierOrDesignStorage']);self.assertFalse(r['actualWorkerTimerOrdersProved']);self.assertFalse(r['finalUpgradeAndStudioProved'])
    def test_a_first_winner_order_cannot_be_omitted(self):
        p=self.root/'manifest.json';s=json.loads(p.read_text());s['cases'].pop();v.q.write_json(p,s)
        with self.assertRaisesRegex(ValueError,'both unique'):self.verify()
    def test_a_graceful_exit_cannot_replace_actual_process_loss(self):
        self.change('human-first','process.json',lambda s:s['exits'].__setitem__(0,0))
        with self.assertRaisesRegex(ValueError,'process-loss exits'):self.verify()
    def test_an_old_listener_cannot_be_reassigned_to_another_scope(self):
        self.change('timer-first','before-checked.json',lambda s:next(w['timer'] for w in s['model']['payload']['work'] if w.get('timer'))['scope'].update(value='foreign'))
        with self.assertRaisesRegex(ValueError,'accepted winner changed|deterministic address'):self.verify()
    def test_an_elapsed_receipt_cannot_precede_its_real_deadline(self):
        for name in ('finished-checked.json','retained-checked.json'):
            self.change('timer-first',name,lambda s:next(r for r in s['inputs'] if r['inputKind']=='timer-elapsed').update(acceptedAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError,'eligibility'):self.verify()
    def test_late_human_input_cannot_be_relabelled_as_a_second_winner(self):
        for name in ('finished-checked.json','retained-checked.json'):
            self.change('timer-first',name,lambda s:next(r for r in s['inputs'] if r['inputKind']=='complete-human')['result'].update(status='Accepted',code=None))
        with self.assertRaisesRegex(ValueError,'eligibility'):self.verify()
    def test_the_original_delayed_source_must_retain_not_before(self):
        self.change('human-first','before-checked.json',lambda s:s['sources'][0]['intent'].update(notBefore=None))
        with self.assertRaisesRegex(ValueError,'thirty-second deadline'):self.verify()
    def test_source_ack_cannot_precede_recipient_consumption(self):
        for name in ('finished-checked.json','retained-checked.json'):
            self.change('human-first',name,lambda s:s['sources'][0].update(deliveredAt='2000-01-01T00:00:00Z'))
        with self.assertRaisesRegex(ValueError,'acknowledged before'):self.verify()
    def test_the_actual_checked_observer_must_match_source(self):
        self.change('human-first','process.json',lambda s:s['observer'].update(productVersion='0.0.0+'+'0'*40))
        with self.assertRaisesRegex(ValueError,'checked observer'):self.verify()
    def test_a_cold_host_cannot_change_the_model_winner(self):
        self.change('human-first','retained-checked.json',lambda s:s['model'].update(revision=3))
        with self.assertRaisesRegex(ValueError,'accepted winner changed'):self.verify()
    def test_physical_material_cannot_drop_its_timer_binding(self):
        def change(c):
            artifact,raw=c.execute('SELECT ArtifactId,ContentJson FROM elsa_runtime_workflow_executable').fetchone();s=json.loads(raw);s['material']['definition']['timerBindings']=[]
            c.execute('UPDATE elsa_runtime_workflow_executable SET ContentJson=? WHERE ArtifactId=?',(json.dumps(s),artifact))
        self.store('human-first','before',change)
        with self.assertRaisesRegex(ValueError,'physical duration material'):self.verify()
    def test_physical_receipt_fingerprint_cannot_change(self):
        def change(c):
            key,raw=c.execute('SELECT InputId,ContentJson FROM elsa_runtime_model_input LIMIT 1').fetchone();s=json.loads(raw);s['contentFingerprint']=v.key('other')
            c.execute('UPDATE elsa_runtime_model_input SET ContentJson=? WHERE InputId=?',(json.dumps(s),key))
        self.store('timer-first','finished',change)
        with self.assertRaisesRegex(ValueError,'physical duration receipt'):self.verify()
    def test_a_physical_native_carrier_cannot_be_hidden_by_zero_observer_counts(self):
        original=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/retry-authorization/direct/before-elsa.sqlite.gz'
        source=Path(self.temp.name)/'native';source.write_bytes(gzip.decompress(original.read_bytes()))
        def change(c):
            c.execute('ATTACH DATABASE ? AS native',(str(source),));table='elsa_runtime_workflow_execution_state'
            names=','.join('"'+r[1]+'"' for r in c.execute('PRAGMA table_info('+table+')'))
            c.execute('INSERT INTO '+table+' ('+names+') SELECT '+names+' FROM native.'+table+' LIMIT 1')
        self.store('human-first','before',change)
        with self.assertRaisesRegex(ValueError,'native workflow/activity/bookmark carrier'):self.verify()
