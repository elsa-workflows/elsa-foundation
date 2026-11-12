"""Read-only corrupt controls over actual signed-in role and tenant HTTP probes."""
from contextlib import closing
import gzip,hashlib,importlib.util,json,shutil,sqlite3,tempfile,unittest
from pathlib import Path
_spec=importlib.util.spec_from_file_location('retry_auth',Path(__file__).with_name('verify_held_service_retry.py'))
v=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(v)
EVIDENCE=v.q.REPO/'specs/1017-direct-bpmn-hosting/evidence/pipeline/retry-authorization'


class ActualRetryAuthorizationControls(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='elsa-retry-auth-control-');self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)/'evidence';shutil.copytree(EVIDENCE,self.root)

    def rehash(self,case,name):
        p=self.root/'manifest.json';m=json.loads(p.read_text());item=next(c for c in m['cases'] if c['name']==case)
        next(a for a in item['assets'] if a['path']==case+'/'+name)['sha256']=v.q.digest(self.root/case/name);v.q.write_json(p,m)

    def change(self,case,name,action):
        p=self.root/case/name;value=json.loads(p.read_text());action(value);v.q.write_json(p,value);self.rehash(case,name)

    def store(self,case,action):
        p=self.root/case/'finished-elsa.sqlite.gz';raw=Path(self.temp.name)/'changed.sqlite';raw.write_bytes(gzip.decompress(p.read_bytes()))
        with closing(sqlite3.connect(raw)) as c:action(c);c.commit()
        p.write_bytes(gzip.compress(raw.read_bytes()));self.rehash(case,p.name)
        def observer(value):
            s=value['finishedStores']['elsa'];s['databaseSha256']=hashlib.sha256(raw.read_bytes()).hexdigest();s['gzipSha256']=v.q.digest(p);s['observer']['snapshotSha256']=s['databaseSha256']
        self.change(case,'process.json',observer)

    def verify(self):return v.verify(self.root/'manifest.json')

    def test_actual_permission_and_tenant_boundaries_hold_before_replay_and_after_restart(self):
        r=self.verify();self.assertEqual(5,len(r['actualHeldRetryJourneys']));self.assertEqual(45,r['wholeStoresVerified']);self.assertEqual(355,r['actualAuthorizationHttpProbes'])
        self.assertTrue(r['authorizationBeforeReplayProved']);self.assertTrue(r['tenantIsolationBeforeReplayProved']);self.assertTrue(r['oneAuthorizedPhysicalRetryProved'])
        self.assertFalse(r['obsoleteResultControlsProved']);self.assertFalse(r['finalUpgradeAndStudioProved'])

    def test_authorization_cannot_be_silently_removed_from_the_manifest(self):
        p=self.root/'manifest.json';m=json.loads(p.read_text());m['authorizationControls']=False;v.q.write_json(p,m)
        with self.assertRaisesRegex(ValueError,'exact raw assets'):self.verify()

    def test_a_read_only_signed_in_account_cannot_acquire_an_extra_permission(self):
        self.change('direct','auth-after.json',lambda x:next(p for p in x['principals'] if p['profile']=='reader')['session']['permissions'].append('*'))
        with self.assertRaisesRegex(ValueError,'stored scoped Identity grant'):self.verify()

    def test_a_signed_in_subject_must_be_the_actual_normal_identity_account(self):
        self.change('root','auth-retained.json',lambda x:next(p for p in x['principals'] if p['profile']=='retrier')['session'].update(subject='other-subject'))
        with self.assertRaisesRegex(ValueError,'stored scoped Identity grant'):self.verify()

    def test_the_foreign_principal_cannot_be_relabelled_as_local(self):
        self.change('sequence','auth-before.json',lambda x:next(p for p in x['principals'] if p['profile']=='foreign')['session'].update(tenantId='default'))
        with self.assertRaisesRegex(ValueError,'stored scoped Identity grant'):self.verify()

    def test_read_permission_cannot_be_used_to_replay_a_retry(self):
        def change(x):
            row=next(o for o in x['outcomes'] if o['profile']=='reader' and o['operation']=='retry-replay');row.update(httpStatus=200,returnedOutcome=True,permittedHttpStatuses=[200])
        self.change('direct','auth-after.json',change)
        with self.assertRaisesRegex(ValueError,'permission/tenant boundary'):self.verify()

    def test_a_foreign_tenant_denial_cannot_disclose_the_retained_result(self):
        self.change('flowchart','auth-retained.json',lambda x:next(o for o in x['outcomes'] if o['profile']=='foreign' and o['operation']=='read-command').update(returnedOutcome=True))
        with self.assertRaisesRegex(ValueError,'disclosed an outcome'):self.verify()

    def test_the_cold_host_authorization_stage_cannot_be_omitted(self):
        p=self.root/'manifest.json';m=json.loads(p.read_text());m['cases'][0]['assets']=[a for a in m['cases'][0]['assets'] if not a['path'].endswith('/auth-retained.json')];v.q.write_json(p,m)
        with self.assertRaisesRegex(ValueError,'exact raw assets'):self.verify()

    def test_denials_cannot_distinguish_existing_and_missing_commands(self):
        self.change('direct','auth-after.json',lambda x:next(o for o in x['outcomes'] if o['profile']=='nobody' and o['operation']=='read-unknown-command').update(httpStatus=403))
        with self.assertRaisesRegex(ValueError,'resource-existence distinction'):self.verify()

    def test_a_physical_role_cannot_gain_wildcard_permission(self):
        setup=json.loads((self.root/'direct/auth-probe-users.json').read_text());user=next(u for u in setup['users'] if u['userName']=='bpmn-probe-reader')
        self.store('direct',lambda c:c.execute('UPDATE identity_roles SET PermissionsJson=? WHERE RoleId=?',(v.v.key(json.dumps([v.v.key('*')])),v.v.key(user['roleId']))))
        with self.assertRaisesRegex(ValueError,'physical Identity role'):self.verify()

    def test_the_actual_stored_role_tenant_cannot_be_changed(self):
        setup=json.loads((self.root/'root/auth-probe-users.json').read_text());user=next(u for u in setup['users'] if u['userName']=='bpmn-probe-foreign')
        self.store('root',lambda c:c.execute('UPDATE identity_roles SET TenantId=? WHERE RoleId=?',(v.v.key('default'),v.v.key(user['roleId']))))
        with self.assertRaisesRegex(ValueError,'physical Identity role'):self.verify()

    def test_the_actual_framework_membership_cannot_be_removed(self):
        setup=json.loads((self.root/'sequence/auth-probe-users.json').read_text());user=setup['users'][0]
        self.store('sequence',lambda c:c.execute('DELETE FROM identity_user_roles WHERE UserId=?',(v.v.key(user['userId']),)))
        with self.assertRaisesRegex(ValueError,'framework user-role membership'):self.verify()

    def test_identity_setup_cannot_be_labelled_as_execution_state_seeding(self):
        self.change('direct','auth-probe-users.json',lambda x:x.update(executionStateSeeded=True))
        with self.assertRaisesRegex(ValueError,'normal Identity setup'):self.verify()

    def test_the_identity_sdk_must_match_the_executing_source(self):
        self.change('compensationscope','auth-probe-users.json',lambda x:x['products'][0].update(informationalVersion='1.0.0+'+'0'*40))
        with self.assertRaisesRegex(ValueError,'normal Identity setup'):self.verify()

    def test_a_real_password_backed_sign_in_requires_its_actual_stored_credential(self):
        setup=json.loads((self.root/'direct/auth-probe-users.json').read_text());user=setup['users'][0]
        self.store('direct',lambda c:c.execute('UPDATE identity_users SET PasswordHash=NULL WHERE UserId=?',(v.v.key(user['userId']),)))
        with self.assertRaisesRegex(ValueError,'stored credential/role relationship'):self.verify()

    def test_public_observations_cannot_include_login_credentials(self):
        self.change('direct','auth-probe-users.json',lambda x:x['users'][0].update(password='not-a-real-password'))
        with self.assertRaisesRegex(ValueError,'credential or cookie field'):self.verify()
