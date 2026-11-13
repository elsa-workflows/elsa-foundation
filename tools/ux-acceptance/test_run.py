from contextlib import closing, contextmanager
import copy
import importlib.util
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('acceptance', Path(__file__).with_name('run.py'))
acceptance = importlib.util.module_from_spec(spec)
spec.loader.exec_module(acceptance)


class VerdictTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.artifact = self.root / 'proof'
        self.artifact.write_text('proof')
        self.report = {'suites': [{'specs': [
            {'title': journey, 'tests': [
                {'projectName': project, 'expectedStatus': 'passed', 'status': 'expected', 'results': [
                    {'status': 'passed', 'attachments': [
                        {'name': name, 'path': str(self.artifact)} for name in
                        ['trace'] + [f'{capture}.{suffix}' for capture in acceptance.CAPTURES[journey] for suffix in ('png', 'aria.yml')]
                        + acceptance.EVIDENCE.get(journey, [])]}]}
                for project in sorted(acceptance.JOURNEY_PROJECTS[journey])]}
            for journey in sorted(acceptance.JOURNEYS)]}]}

    def test_complete_evidence(self):
        self.assertEqual(sum(len(projects) for projects in acceptance.JOURNEY_PROJECTS.values()), acceptance.verdict(self.report, self.root))

    def test_inventory_adds_bounded_supplementary_cases(self):
        standard = {journey for journey, projects in acceptance.JOURNEY_PROJECTS.items() if projects == acceptance.PROJECTS}
        supplements = acceptance.JOURNEYS - standard
        self.assertEqual(set(acceptance.SUPPLEMENT_PROJECTS), supplements)
        self.assertEqual(9, sum(len(acceptance.JOURNEY_PROJECTS[journey]) for journey in supplements))
        self.assertEqual({f'touch-{width}-{mode}' for width in (360, 390, 1440) for mode in ('light', 'dark')},
                         acceptance.JOURNEY_PROJECTS['coarse pointer grouped navigation'])
        # A supplementary project owns exactly one journey and never one of the standard projects.
        owners = [project for journey in supplements for project in acceptance.JOURNEY_PROJECTS[journey]]
        self.assertEqual(len(owners), len(set(owners)))
        self.assertFalse(set(owners) & acceptance.PROJECTS)
        self.assertEqual(70, sum(len(acceptance.CAPTURES[journey]) * len(acceptance.JOURNEY_PROJECTS[journey]) for journey in supplements))
        self.assertEqual(set(acceptance.EVIDENCE), supplements)

    def test_supplementary_journey_in_a_standard_project_is_not_the_inventory(self):
        report = copy.deepcopy(self.report)
        spec = next(row for row in report['suites'][0]['specs'] if row['title'] == 'runtime-only API composition and combined return')
        spec['tests'][0]['projectName'] = 'desktop-light'
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root)
        report = copy.deepcopy(self.report)
        spec = next(row for row in report['suites'][0]['specs'] if row['title'] == 'coarse pointer grouped navigation')
        spec['tests'].append(copy.deepcopy(spec['tests'][0]) | {'projectName': 'narrow-light'})
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root)

    def test_compact_inputs_preserve_inherited_named_journeys(self):
        required = {'collections and connection creation', 'agent authoring and navigation',
                    'executable designer parity',
                    'compact runtime Inputs retain exact captured values and accessible technical details'}
        self.assertTrue(required <= acceptance.JOURNEYS)
        self.assertEqual(['reference', 'compact-values', 'technical-details', 'nested-values', 'details-closed', 'technical-details-320', 'compact-values-320'],
                         acceptance.CAPTURES['compact runtime Inputs retain exact captured values and accessible technical details'])

    def test_scoped_run_requires_every_project_and_rejects_extra_or_unknown_journeys(self):
        journey = 'feature filtering and settings dialogs'
        report = copy.deepcopy(self.report)
        report['suites'][0]['specs'] = [row for row in report['suites'][0]['specs'] if row['title'] == journey]
        self.assertEqual(len(acceptance.PROJECTS), acceptance.verdict(report, self.root, [journey]))
        touch = copy.deepcopy(self.report)
        touch['suites'][0]['specs'] = [row for row in touch['suites'][0]['specs'] if row['title'] == 'coarse pointer grouped navigation']
        self.assertEqual(6, acceptance.verdict(touch, self.root, ['coarse pointer grouped navigation']))
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root)
        with self.assertRaises(ValueError): acceptance.verdict(self.report, self.root, [journey])
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, [])
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, ['unknown'])
        report['suites'][0]['specs'][0]['tests'].pop()
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, [journey])

    def test_missing_duplicate_empty_skipped_failed_or_retried_is_not_success(self):
        for change in ('missing', 'missing-journey', 'duplicate', 'empty', 'skipped', 'expected-failure', 'runner-error', 'retry'):
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                tests = report['suites'][0]['specs'][0]['tests']
                if change == 'missing': tests.pop()
                if change == 'missing-journey': report['suites'][0]['specs'].pop()
                if change == 'duplicate': tests.append(copy.deepcopy(tests[0]))
                if change == 'empty': report['suites'] = []
                if change == 'skipped': tests[0]['results'][0]['status'] = 'skipped'
                if change == 'expected-failure': tests[0]['expectedStatus'] = 'failed'
                if change == 'runner-error': report['errors'] = [{'message': 'host disappeared'}]
                if change == 'retry': tests[0]['results'].append(copy.deepcopy(tests[0]['results'][0]))
                with self.assertRaises(ValueError): acceptance.verdict(report, self.root)

    def test_missing_empty_or_external_artifact_cannot_pass(self):
        attachments = self.report['suites'][0]['specs'][0]['tests'][0]['results'][0]['attachments']
        attachments.pop()
        with self.assertRaises(ValueError): acceptance.verdict(self.report, self.root)
        self.setUp()
        self.artifact.write_text('')
        with self.assertRaises(ValueError): acceptance.verdict(self.report, self.root)
        self.artifact.write_text('proof')
        with self.assertRaises(ValueError): acceptance.verdict(self.report, self.root / 'different-run')

    def test_each_intermediate_capture_is_required(self):
        for spec in self.report['suites'][0]['specs']:
            for attachment in list(spec['tests'][0]['results'][0]['attachments']):
                with self.subTest(journey=spec['title'], capture=attachment['name']):
                    report = copy.deepcopy(self.report)
                    target = next(row for row in report['suites'][0]['specs'] if row['title'] == spec['title'])
                    target['tests'][0]['results'][0]['attachments'].remove(attachment)
                    with self.assertRaises(ValueError): acceptance.verdict(report, self.root)

    def test_stale_development_overlays_never_reach_owned_content_root(self):
        project = self.root / 'source/Host.csproj'
        project.parent.mkdir()
        output = project.parent / 'bin/Debug/net10.0'
        output.mkdir(parents=True)
        for name in ('appsettings.json', 'shells.json'):
            (project.parent / name).write_text('{}')
            (output / name).write_text('{}')
        for name in ('appsettings.Development.json', 'shells.Development.json'):
            (output / name).write_text('{"ConnectionString":"Data Source=/shared/developer/elsa.db"}')
        destination = self.root / 'private'
        acceptance.prepare_host(project, destination)
        self.assertEqual({'appsettings.json', 'shells.json'}, {file.name for file in destination.iterdir()})

    def test_exited_leader_does_not_hide_a_term_resistant_descendant(self):
        with patch.object(acceptance.os, 'killpg') as kill:
            from unittest.mock import Mock
            process = Mock(pid=44)
            process.poll.return_value = 0
            acceptance.stop_group(process)
            self.assertEqual([(44, acceptance.signal.SIGTERM), (44, acceptance.signal.SIGKILL)], [call.args for call in kill.call_args_list])

    def test_command_interrupt_stops_its_entire_group(self):
        with patch.object(acceptance.subprocess, 'Popen') as popen, patch.object(acceptance.os, 'killpg') as kill:
            process = popen.return_value
            process.pid = 43
            process.poll.return_value = None
            process.wait.side_effect = [InterruptedError('terminate'), 0, 0]
            with self.assertRaises(InterruptedError):
                acceptance.command(['browser-runner'], self.root / 'command.log')
            self.assertTrue(popen.call_args.kwargs['start_new_session'])
            self.assertEqual([(43, acceptance.signal.SIGTERM), (43, acceptance.signal.SIGKILL)], [call.args for call in kill.call_args_list])
            self.assertEqual(3, process.wait.call_count)

    def test_unrelated_healthy_listener_is_not_a_readiness_answer(self):
        with patch.object(acceptance.subprocess, 'Popen') as popen, patch.object(acceptance.urllib.request, 'urlopen') as request, patch.object(acceptance.time, 'sleep'), patch.object(acceptance.os, 'killpg'):
            popen.return_value.poll.side_effect = [None, 1]
            request.return_value.__enter__.return_value.status = 200
            with self.assertRaises(RuntimeError):
                with acceptance.host(self.root, 'missing.dll', 'http://localhost:1', '/', self.root / 'collision.log', {}):
                    self.fail('An unrelated listener was accepted')
            request.assert_not_called()

    def test_inherited_credentials_and_store_overrides_are_not_forwarded(self):
        with patch.dict(os.environ, {'ConnectionStrings__Elsa': 'private', 'OPENAI_API_KEY': 'private', 'PATH': '/bin'}, clear=True):
            env = acceptance.clean_environment()
        self.assertNotIn('OPENAI_API_KEY', env)
        self.assertNotIn('ConnectionStrings__Elsa', env)
        self.assertEqual('/bin', env['PATH'])

    def test_host_is_stopped_on_failed_readiness_and_body_exception(self):
        for fail_start in (True, False):
            with self.subTest(fail_start=fail_start):
                with patch.object(acceptance.subprocess, 'Popen') as popen, patch.object(acceptance.os, 'killpg') as kill:
                    process = popen.return_value
                    process.pid = 42
                    process.poll.return_value = 1 if fail_start else None
                    with patch.object(acceptance.urllib.request, 'urlopen') as request:
                        request.return_value.__enter__.return_value.status = 200
                        if not fail_start:
                            def started(*args, **kwargs):
                                kwargs['stdout'].write('Now listening on: http://localhost:1\n')
                                kwargs['stdout'].flush()
                                return process
                            popen.side_effect = started
                        with self.assertRaises(RuntimeError):
                            with acceptance.host(self.root, 'host.dll', 'http://localhost:1', '/', self.root / 'host.log', {}):
                                raise RuntimeError('journey failed')
                    if not fail_start:
                        self.assertEqual([(42, acceptance.signal.SIGTERM), (42, acceptance.signal.SIGKILL)], [call.args for call in kill.call_args_list])
                        self.assertEqual(2, process.wait.call_count)


class CompositionTests(unittest.TestCase):
    def setUp(self):
        self.shells = json.loads((acceptance.ROOT / 'Essentials/apps/Elsa.Workbench/shells.json').read_text(encoding='utf-8-sig'))
        self.features = self.shells['CShells']['Shells']['default']['Features']

    def test_subset_lists_name_only_currently_configured_features(self):
        # A feature rename must fail here and in setup, never silently widen a subset.
        for name, item in acceptance.SUBSET_COMPOSITIONS.items():
            with self.subTest(name=name):
                self.assertTrue(item['remove'])
                self.assertEqual([], [feature for feature in item['remove'] if feature not in self.features])
                self.assertEqual(len(item['remove']), len(set(item['remove'])))
                self.assertTrue(item['journeys'] <= acceptance.JOURNEYS)
        # Design-only is deferred to #4160; only the runtime-only subset is composed.
        self.assertEqual({'runtime-api-subset'}, set(acceptance.SUBSET_COMPOSITIONS))
        self.assertTrue({'WorkflowsDesignApi', 'ActivitiesDesignApi', 'ConnectionsApi', 'SecretsApi'} <= set(acceptance.SUBSET_COMPOSITIONS['runtime-api-subset']['remove']))
        self.assertNotIn('WorkflowsRuntimeApi', acceptance.SUBSET_COMPOSITIONS['runtime-api-subset']['remove'])
        # The ordinary admin seed that every other journey and cleanup signs in with stays committed.
        self.assertEqual('admin', self.features[acceptance.IDENTITY_SEED_FEATURE]['SeedAdminUserName'])

    def test_compose_shells_removes_routes_and_seeds_a_private_copy(self):
        original = copy.deepcopy(self.shells)
        remove = acceptance.SUBSET_COMPOSITIONS['runtime-api-subset']['remove']
        composed = acceptance.compose_shells(self.shells, 'http://localhost:9', remove)
        features = composed['CShells']['Shells']['default']['Features']
        self.assertEqual(original, self.shells, 'The source composition is never mutated')
        self.assertEqual(set(self.features) - set(remove), set(features))
        for name in features:
            self.assertEqual(self.features[name], features[name]) if name != 'FoundationIdentityAspNetCoreIdentity' else None
        self.assertEqual(['http://localhost:9'], features['FoundationIdentityAspNetCoreIdentity']['AllowedReturnUrlOrigins'])
        self.assertEqual('admin', features[acceptance.IDENTITY_SEED_FEATURE]['SeedAdminUserName'])
        seeded = acceptance.compose_shells(self.shells, 'http://localhost:9', seed={'user': 'reader', 'password': 'Secret-1a', 'role': 'reader-seed'})
        seed = seeded['CShells']['Shells']['default']['Features'][acceptance.IDENTITY_SEED_FEATURE]
        self.assertEqual(('reader', 'Secret-1a', 'reader-seed', 'reader@example.invalid'),
                         (seed['SeedAdminUserName'], seed['SeedAdminPassword'], seed['SeedAdminRoleName'], seed['SeedAdminEmail']))
        with self.assertRaises(ValueError):
            acceptance.compose_shells(self.shells, 'http://localhost:9', ['NoSuchRenamedFeature'])


class RestrictedIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.store = self.root / 'elsa.db'
        # Stored the way the real converters write them: every text column through IdentityEntityFrameworkUtf16Codec,
        # set columns as an IdentityApplicationSetCodec payload that is itself a text column.
        encode, text = acceptance.encode_identity_set, acceptance.encode_identity_text
        with closing(sqlite3.connect(self.store)) as db, db:
            db.execute('create table identity_users (Id text primary key, TenantId text, UserId text, UserName text, RoleIdsJson text, DirectPermissionsJson text, PasswordHash text)')
            db.execute('create table identity_roles (Id text primary key, TenantId text, RoleId text, Name text, PermissionsJson text)')
            db.execute('create table identity_tenant_memberships (Id text primary key, TenantId text, UserId text, Status integer, RoleIdsJson text, DirectPermissionsJson text)')
            db.execute('insert into identity_users values (?, ?, ?, ?, ?, ?, ?)', ('u-key', text('default'), text('reader-id'), text(acceptance.RESTRICTED_USER), encode(['role-id']), encode([]), text('AQAAAA-real-hash')))
            db.execute('insert into identity_roles values (?, ?, ?, ?, ?)', ('r-key', text('default'), text('role-id'), text(acceptance.RESTRICTED_SEED_ROLE), encode(['*', 'identity/providers:read'])))
            db.execute('insert into identity_tenant_memberships values (?, ?, ?, ?, ?, ?)', ('m-key', text('default'), text('reader-id'), 1, encode(['role-id']), encode([])))
        (self.root / 'other.db').write_bytes(b'')
        (self.root / 'not-sqlite.db').write_bytes(b'not a database file at all, just bytes' * 4)

    def rows(self):
        with closing(sqlite3.connect(self.store)) as db:
            return db.execute('select u.RoleIdsJson, u.DirectPermissionsJson, u.PasswordHash, m.RoleIdsJson, m.DirectPermissionsJson, m.Status, r.PermissionsJson '
                              'from identity_users u, identity_tenant_memberships m, identity_roles r').fetchone()

    def test_set_codec_matches_the_identity_application_set_format(self):
        self.assertEqual('[]', acceptance.identity_set_payload([]))
        self.assertEqual('["YQA="]', acceptance.identity_set_payload(['a']))
        # System.Text.Json's default encoder writes '+' as \u002B; values are ordinal-sorted and de-duplicated.
        self.assertEqual('["YQA=","\\u002BwA="]', acceptance.identity_set_payload(['\u00fb', 'a', 'a']))
        self.assertEqual(['a', '\u00fb'], acceptance.decode_identity_set(acceptance.encode_identity_set(['\u00fb', 'a'])))

    def test_text_columns_match_a_real_workbench_store(self):
        # Values read from a Workbench store the real seeder wrote: UserName 'admin', TenantId 'default', and the
        # opening of a RoleIdsJson column whose payload is a JSON array of encoded role ids.
        self.assertEqual('YQBkAG0AaQBuAA==', acceptance.encode_identity_text('admin'))
        self.assertEqual('ZABlAGYAYQB1AGwAdAA=', acceptance.encode_identity_text('default'))
        self.assertEqual('admin', acceptance.decode_identity_text('YQBkAG0AaQBuAA=='))
        self.assertTrue(acceptance.encode_identity_set(['seed-role-1']).startswith('WwAiAGMAdwBCAGwAQQBHAFUAQQBaAEEAQQB0AEEA'))
        self.assertNotEqual(acceptance.identity_set_payload([]), acceptance.encode_identity_set([]), 'A set column is not its bare payload')

    def test_reader_keeps_its_seeded_account_and_holds_exactly_the_three_grants(self):
        before = self.rows()
        receipt = acceptance.restrict_seeded_reader(self.root)
        users, direct, password, member_roles, member_direct, status, role_permissions = self.rows()
        self.assertEqual([], acceptance.decode_identity_set(users))
        self.assertEqual(sorted(acceptance.RESTRICTED_PERMISSIONS), acceptance.decode_identity_set(direct))
        self.assertEqual([], acceptance.decode_identity_set(member_roles))
        self.assertEqual([], acceptance.decode_identity_set(member_direct))
        self.assertEqual([], acceptance.decode_identity_set(role_permissions))
        self.assertEqual((before[2], before[5]), (password, status), 'Password hash and membership status are untouched')
        self.assertEqual({'tenant': 'default', 'subject': 'reader-id', 'userName': acceptance.RESTRICTED_USER}, {key: receipt[key] for key in ('tenant', 'subject', 'userName')})
        self.assertFalse(receipt['passwordInReceipt'])
        self.assertNotIn('AQAAAA', json.dumps(receipt))

    def test_unexpected_store_shapes_fail_setup_without_writing(self):
        cases = {
            'second account': f"insert into identity_users values ('a-key', '{acceptance.encode_identity_text('default')}', '{acceptance.encode_identity_text('admin-id')}', '{acceptance.encode_identity_text('admin')}', '{acceptance.encode_identity_set([])}', '{acceptance.encode_identity_set([])}', null)",
            'missing seed role': 'delete from identity_roles',
            'unexpected role grant': f"update identity_users set RoleIdsJson = '{acceptance.encode_identity_set([])}'",
            'existing direct grant': f"update identity_users set DirectPermissionsJson = '{acceptance.encode_identity_set(['*'])}'",
            'missing membership': 'delete from identity_tenant_memberships',
        }
        for name, change in cases.items():
            with self.subTest(name=name):
                self.setUp()
                with closing(sqlite3.connect(self.store)) as db, db:
                    db.execute(change)
                before = self.rows()
                with self.assertRaises(RuntimeError):
                    acceptance.restrict_seeded_reader(self.root)
                self.assertEqual(before, self.rows())

    def test_identity_store_must_be_unique(self):
        with closing(sqlite3.connect(self.root / 'copy.db')) as db, db:
            db.execute('create table identity_users (Id text)')
        with self.assertRaises(RuntimeError):
            acceptance.restrict_seeded_reader(self.root)


class OrchestrationTests(unittest.TestCase):
    def write_gallery_fixture(self, args, env):
        self.assertEqual(['pnpm', 'exec', 'playwright', 'test', '--config', 'tests/browser/playwright.outlined-fields.config.ts'], args)
        gallery = Path(env['OUTLINED_EVIDENCE'])
        gallery.mkdir(parents=True)
        screenshot = gallery / 'states.png'
        screenshot.write_bytes(b'isolated-gallery-fixture')
        report = {'config': {'metadata': {'outlinedCases': 1}}, 'suites': [{'specs': [
            {'title': 'Outlined field states', 'tests': [{'expectedStatus': 'passed', 'status': 'expected', 'results': [
                {'status': 'passed', 'attachments': [{'contentType': 'image/png', 'path': str(screenshot)}]}]}]}]}]}
        (gallery / 'playwright.json').write_text(json.dumps(report))

    def test_full_run_seeds_reader_before_owning_every_composition_host(self):
        events = []
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'out'

            @contextmanager
            def fake_host(directory, dll, url, ready, log, env):
                seed = None
                if dll == 'Elsa.Workbench.dll':
                    shells = json.loads((directory / 'shells.json').read_text())
                    seed = shells['CShells']['Shells']['default']['Features'][acceptance.IDENTITY_SEED_FEATURE]['SeedAdminUserName']
                events.append(('start', directory.name, dll, url, log.name, seed, env.get('Studio__BackendBaseUrl')))
                try:
                    yield
                finally:
                    events.append(('stop', directory.name, dll))

            def fake_command(args, log, *, cwd=acceptance.ROOT, env=None):
                if log.name == 'outlined-gallery.log':
                    self.write_gallery_fixture(args, env)
                    return
                if args[:2] == ['pnpm', 'exec']:
                    events.append(('browser', {key: env.get(key) for key in env if key.startswith(('UX_', 'ELSA_'))}))
                    (output / 'playwright.json').write_text('{}')

            def fake_prepare(project, destination):
                destination.mkdir(parents=True)

            def fake_restrict(root):
                events.append(('restrict', root.name))
                return {'subject': 'reader-id'}

            with patch.object(acceptance, 'command', fake_command), patch.object(acceptance, 'host', fake_host), \
                    patch.object(acceptance, 'prepare_host', fake_prepare), patch.object(acceptance, 'restrict_seeded_reader', fake_restrict), \
                    patch.object(acceptance, 'verdict', return_value=70), \
                    patch.object(acceptance.subprocess, 'check_output', side_effect=lambda args, **_: 'abc' if 'rev-parse' in args else ''), \
                    patch('builtins.print'):
                acceptance.run(output)
            report = json.loads((output / 'report.json').read_text())
        self.assertEqual('passed', report['status'])
        self.assertEqual({'executed': 1, 'sourceSha': 'abc', 'kind': 'isolated-shared-components'}, report['isolatedFieldGallery'])
        starts = [event for event in events if event[0] == 'start']
        seed, owned = starts[0], starts[1:]
        self.assertEqual(('workbench', 'Elsa.Workbench.dll', 'backend-restricted-seed.log', acceptance.RESTRICTED_USER), (seed[1], seed[2], seed[4], seed[5]))
        self.assertEqual(('stop', 'workbench', 'Elsa.Workbench.dll'), events[1], 'The seed boot stops before any acceptance host starts')
        self.assertEqual(('restrict', 'workbench'), events[2])
        self.assertEqual(['workbench', 'studio', 'runtime-api-subset-workbench', 'runtime-api-subset-studio'],
                         [event[1] for event in owned])
        self.assertEqual(seed[3], owned[0][3], 'The seed boot uses the combined host origin it later serves')
        self.assertTrue(all(event[5] in ('admin', None) for event in owned), 'Acceptance boots seed only the ordinary admin')
        self.assertEqual(owned[0][3], owned[1][6])
        self.assertEqual(owned[2][3], owned[3][6])
        browser = next(event[1] for event in events if event[0] == 'browser')
        self.assertEqual((owned[3][3], owned[2][3]), (browser['UX_RUNTIME_STUDIO_URL'], browser['UX_RUNTIME_BACKEND_URL']))
        self.assertNotIn('UX_DESIGN_STUDIO_URL', browser)
        self.assertEqual((acceptance.RESTRICTED_USER, 'reader-id'), (browser['UX_RESTRICTED_USER'], browser['UX_RESTRICTED_SUBJECT']))
        self.assertTrue(browser['UX_RESTRICTED_PASSWORD'])
        self.assertNotIn(browser['UX_RESTRICTED_PASSWORD'], json.dumps(report))
        stops = [event[1] for event in events if event[0] == 'stop'][1:]
        self.assertEqual(list(reversed([event[1] for event in owned])), stops, 'Every owned host stops, newest first')
        self.assertEqual({'combined', 'runtime-api-subset'}, set(report['compositions']))
        self.assertEqual(acceptance.SUBSET_COMPOSITIONS['runtime-api-subset']['remove'], report['compositions']['runtime-api-subset']['removedFeatures'])

    def test_scoped_ordinary_journey_starts_only_the_combined_hosts(self):
        starts = []
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'out'

            @contextmanager
            def fake_host(directory, dll, url, ready, log, env):
                starts.append(directory.name)
                yield

            def fake_command(args, log, *, cwd=acceptance.ROOT, env=None):
                if log.name == 'outlined-gallery.log':
                    self.write_gallery_fixture(args, env)
                    return
                if args[:2] == ['pnpm', 'exec']:
                    self.assertEqual(['--grep', 'save template toast'], args[-2:])
                    (output / 'playwright.json').write_text('{}')

            with patch.object(acceptance, 'command', fake_command), patch.object(acceptance, 'host', fake_host), \
                    patch.object(acceptance, 'prepare_host', lambda project, destination: destination.mkdir(parents=True)), \
                    patch.object(acceptance, 'restrict_seeded_reader', side_effect=AssertionError('no reader seed')), \
                    patch.object(acceptance, 'verdict', return_value=6), \
                    patch.object(acceptance.subprocess, 'check_output', side_effect=lambda args, **_: 'abc' if 'rev-parse' in args else ''), \
                    patch('builtins.print'):
                acceptance.run(output, 'save template toast')
            report = json.loads((output / 'report.json').read_text())
        self.assertEqual(['workbench', 'studio'], starts)
        self.assertEqual({'executed': 1, 'sourceSha': 'abc', 'kind': 'isolated-shared-components'}, report['isolatedFieldGallery'])


class FieldGalleryVerdictTests(unittest.TestCase):
    def test_gallery_requires_complete_single_passes_and_images(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            screenshot = root / 'states.png'
            screenshot.write_bytes(b'png-witness')
            test = {'expectedStatus': 'passed', 'status': 'expected', 'results': [{'status': 'passed', 'attachments': [
                {'contentType': 'image/png', 'path': str(screenshot)}]}]}
            report = {'config': {'metadata': {'outlinedCases': 1}}, 'suites': [{'specs': [{'title': 'Theme states', 'tests': [test]}]}]}
            self.assertEqual(1, acceptance.field_gallery_verdict(report, root))
            for change in ('empty', 'missing', 'duplicate', 'skip', 'retry', 'no-image', 'inline-only', 'external-image'):
                with self.subTest(change=change):
                    broken = copy.deepcopy(report)
                    row = broken['suites'][0]['specs'][0]['tests'][0]
                    if change == 'empty': broken['suites'] = []
                    if change == 'missing': broken['config']['metadata']['outlinedCases'] = 2
                    if change == 'duplicate': broken['suites'][0]['specs'].append(copy.deepcopy(broken['suites'][0]['specs'][0]))
                    if change == 'skip': row['results'][0]['status'] = 'skipped'
                    if change == 'retry': row['results'].append(copy.deepcopy(row['results'][0]))
                    if change == 'no-image': row['results'][0]['attachments'] = []
                    if change == 'inline-only': row['results'][0]['attachments'] = [{'contentType': 'image/png', 'body': 'inline-only'}]
                    if change == 'external-image': row['results'][0]['attachments'][0]['path'] = str(root.parent / 'outside.png')
                    with self.assertRaises(ValueError): acceptance.field_gallery_verdict(broken, root)


if __name__ == '__main__':
    unittest.main()
