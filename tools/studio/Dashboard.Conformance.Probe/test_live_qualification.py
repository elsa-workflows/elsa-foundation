import argparse
import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import xml.etree.ElementTree as ET
import zipfile

spec = importlib.util.spec_from_file_location('live_qualification', Path(__file__).with_name('live_qualify.py'))
live = importlib.util.module_from_spec(spec)
spec.loader.exec_module(live)


class ProfileTests(unittest.TestCase):
    def test_default_preparation_does_not_access_credentials_start_hosts_or_build(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'proof'
            options = argparse.Namespace(output=output, mode='prepare', model='explicit-model', account_access_restored=False, credential_environment_variable=None)
            with patch.object(live, 'credential_environment', side_effect=AssertionError('credential read')), patch.object(live, 'build', side_effect=AssertionError('build')), patch.object(live, 'execute_live', side_effect=AssertionError('provider')), patch.object(live, 'source_state', return_value={'sha': 'test', 'dirty': 'preliminary'}):
                live.run(options)
            report = json.loads((output / 'report.json').read_text())
            self.assertEqual('prepared', report['status'])
            self.assertEqual('not run', report['providerCalls'])
            self.assertFalse(report['liveQualified'])

    def test_no_environment_lookup_before_explicit_live_restored_gate(self):
        for mode, restored in [('prepare', False), ('build', True), ('live', False)]:
            with self.subTest(mode=mode, restored=restored), patch.object(live.os.environ, 'get', side_effect=AssertionError('secret lookup')):
                with self.assertRaises(ValueError):
                    live.credential_environment(mode, restored, 'APPROVED_TOKEN', None)

    def test_live_without_ack_fails_before_output_materialization(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / 'proof'
            options = argparse.Namespace(output=output, mode='live', model='pinned', account_access_restored=False)
            with self.assertRaises(ValueError): live.run(options)
            self.assertFalse(output.exists())

    def test_auto_and_nonowned_outputs_refused(self):
        with tempfile.TemporaryDirectory() as temporary:
            for output, model in [(Path(temporary) / 'unused', 'auto'), (live.ROOT / 'unsafe', 'pinned'), (Path('relative'), 'pinned')]:
                with self.subTest(output=output, model=model):
                    with self.assertRaises(ValueError): live.run(argparse.Namespace(output=output, model=model, mode='prepare', account_access_restored=False))

    def test_live_profile_uses_real_provider_normal_iam_and_current_category_profiles(self):
        native = live.native_helpers()
        settings = {'Cors': {}}
        shells = {'CShells': {'Shells': {'default': {'Features': {'WeaverGitHubCopilot': {}, 'FoundationIdentityAspNetCoreIdentity': {}}}}}}
        live.profile(settings, shells, 'http://localhost:12345', 'pinned-model', native)
        features = shells['CShells']['Shells']['default']['Features']
        actual = features['WeaverGitHubCopilot']
        self.assertEqual(actual, features['DashboardLiveQualification'])
        self.assertTrue(actual['Enabled'])
        self.assertFalse(actual['UseLoggedInUser'])
        self.assertNotIn('GitHubToken', actual)
        self.assertNotIn('RuntimeUrl', actual)
        self.assertEqual(live.MODEL_ENVIRONMENT, actual['GitHubTokenEnvironmentVariable'])
        profiles = settings['Qualification']['GrantProfiles']
        self.assertEqual({'acme/processing/category:read'}, set(profiles['dashboard-live-reader']) - set(profiles['dashboard-live-no-category']))
        self.assertFalse(any('*' in grant or 'agent' in grant for grant in profiles['dashboard-live-reader']))

    def test_conditional_dependency_patch_cannot_silently_return(self):
        template = live.reviewed_host_template()
        project = ET.fromstring(template)
        refs = list(project.iter('ProjectReference'))
        self.assertGreater(len(refs), 100)
        self.assertEqual(1, sum('Acme.Processing/Module/Acme.Processing.csproj' in item.attrib['Include'] for item in refs))
        self.assertTrue(all(not item.get('Condition') for item in refs))
        self.assertNotIn('ElsaDashboardLiveQualification', (live.ROOT / 'Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj').read_text())

    def test_observer_graph_paths_exist_and_native_nuget_case_is_preserved(self):
        tree = ET.fromstring((live.HERE / 'live/Observer.Tests.csproj.in').read_text())
        for item in tree.iter('ProjectReference'):
            self.assertTrue(Path(item.get('Include').replace('__SOURCE_ROOT__', str(live.ROOT))).is_file())
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            live.materialize(root)
            self.assertEqual((live.ROOT / 'NuGet.config').read_bytes(), (root / 'host/NuGet.Config').read_bytes())

    def test_materialized_host_owns_graph_outputs_locks_and_checks_source_hooks(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            project, hook, seeded = live.materialize(root)
            self.assertTrue(project.is_relative_to(root))
            self.assertTrue(all(Path(name).is_relative_to(root / 'locks') for name in seeded))
            self.assertIn('$(MSBuildProjectName).packages.lock.json', hook.read_text())
            self.assertIn(str(root / 'artifacts'), ' '.join(live.build_options(root, hook)))
            program = (project.parent / 'Program.cs').read_text()
            self.assertEqual(1, program.count('new DashboardLiveCounters()'))
            self.assertEqual(1, program.count('DashboardLiveQualificationRoutes.Map'))
            self.assertIn('typeof(Acme.Processing.ProcessingFeature).Assembly', program)
            with self.assertRaises(ValueError): live.replace_once('missing', 'hook', 'new')
            with self.assertRaises(ValueError): live.replace_once('hook hook', 'hook', 'new')


class PreflightTests(unittest.TestCase):
    def test_actual_preflight_performs_one_normal_iam_bootstrap_attempt(self):
        with patch.object(live, 'http_json', side_effect=[(200, {}), (200, {'accessToken': 'synthetic-test-only'}), (403, {})]) as http:
            result = live.live_preflight('http://localhost:12345')
        self.assertFalse(result['available'])
        self.assertEqual(3, http.call_count)
        self.assertEqual(['/_elsa/identity/login', '/_elsa/identity/token', '/_elsa/weaver/bootstrap'], [call.args[2] for call in http.call_args_list])

    def test_requested_live_unavailable_exits_nonzero(self):
        with patch('sys.argv', ['live_qualify.py', '--output', '/unused-owned-proof', '--model', 'pinned', '--mode', 'live', '--account-access-restored']), patch.object(live, 'run', side_effect=live.LiveUnavailable('unavailable')):
            with self.assertRaises(SystemExit) as failure:
                live.main()
        self.assertEqual(2, failure.exception.code)

    def test_403_or_unavailable_is_single_attempt_without_retry_or_body_leakage(self):
        for status, envelope in [(403, {'error': {'message': 'secret sentinel'}}), (200, {'data': {'enabled': False, 'provider': {'isAvailable': False, 'status': 'secret sentinel'}}})]:
            with self.subTest(status=status):
                result = live.classify_preflight(status, envelope)
                self.assertFalse(result['available'])
                self.assertEqual(1, result['attempts'])
                self.assertFalse(result['automaticRetry'])
                self.assertNotIn('secret sentinel', json.dumps(result))

    def test_available_preflight_retains_only_public_declared_model_and_sdk_metadata(self):
        result = live.classify_preflight(200, {'data': {'enabled': True, 'provider': {'isAvailable': True, 'metadata': {'sdkPackage': 'GitHub.Copilot.SDK', 'sdkVersion': '1', 'models': 'pinned,other', 'credential': 'sentinel'}}}})
        self.assertTrue(result['available'])
        self.assertEqual(['pinned', 'other'], result['declaredModels'])
        self.assertNotIn('sentinel', json.dumps(result))


class VerdictTests(unittest.TestCase):
    @staticmethod
    def evidence():
        snapshot = lambda sends: {'category': 'actual-sdk-forwarding', 'counts': {'session.send': sends, 'event.Completed': sends,
                                  'tool.return.dashboard_request_clarification': 1 if sends >= 2 else 0,
                                  'tool.return.dashboard_preview_question': sends - 1 if sends >= 2 else 0}, 'privacyFailures': 0}
        phases = {'initial-native-preview', 'refined-native-preview', 'use-unsaved-draft', 'explicit-save', 'reopen-saved-definition', 'reopened-native-refresh', 'saved-native-appearances', 'native-new-run-and-refresh', 'native-saved-drill', 'native-share', 'native-independent-viewer-refresh', 'native-reauthenticated-field-revocation', 'native-dashboard-arrange', 'native-dashboard-share'}
        return {'turns': {'before': snapshot(0), 'afterClarification': snapshot(2)}, 'refinement': {'before': snapshot(2), 'after': snapshot(3)}, 'after': snapshot(3),
                'nativePhases': [{'phase': phase, 'before': 2 if phase == 'initial-native-preview' else 3,
                                 'after': 2 if phase == 'initial-native-preview' else 3, 'delta': 0} for phase in phases]}

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.artifact = self.root / 'proof'
        self.artifact.write_text('proof')
        self.trace = self.root / 'trace.zip'
        with zipfile.ZipFile(self.trace, 'w') as archive:
            for number, role in enumerate(('admin', 'dashboard-reader')):
                archive.writestr(f'{number}-trace.trace', '\n'.join(json.dumps(value) for value in [
                    {'type': 'context-options', 'version': 8},
                    {'type': 'before', 'method': 'fill', 'params': {'selector': 'internal:label=username', 'value': role}},
                    {'type': 'before', 'method': 'click', 'params': {'selector': 'View matching records for result 1'}},
                    {'type': 'frame-snapshot'}]))
        self.boundary = self.root / 'boundary.json'
        self.boundary.write_text(json.dumps(self.evidence()))
        self.authority = self.root / 'authority.json'
        self.authority.write_text(json.dumps({'readerReauthenticated': True, 'executeStatus': 403}))
        self.refinement = self.root / 'refinement.json'
        self.refinement.write_text(json.dumps({'groupingChanged': True, 'filtersChanged': True, 'actualSdkRefinement': True}))
        self.retained = self.root / 'retained.json'
        self.retained.write_text(json.dumps({'questionId': 'question', 'dashboardId': 'dashboard', 'tileIds': ['first', 'second'], 'deferredToOwnedRestart': True}))
        def required(title):
            return live.JSON_ARTIFACTS | {'trace', 'source'} | ({'live-authority.json'} if title == live.ACME else {'live-query-refinement.json', 'live-retained-definitions.json'}) | {f'{capture}.{suffix}' for capture in live.CAPTURES[title] for suffix in ('png', 'aria.yml')}
        paths = {'trace': self.trace, 'live-counters.json': self.boundary, 'live-authority.json': self.authority, 'live-query-refinement.json': self.refinement, 'live-retained-definitions.json': self.retained}
        self.report = {'suites': [{'specs': [{'title': title, 'tests': [{'projectName': 'live-desktop', 'expectedStatus': 'passed', 'status': 'expected', 'results': [{'status': 'passed', 'attachments': [{'name': name, 'path': str(paths.get(name, self.artifact))} for name in required(title)]}]}]} for title in live.TITLES]}]}

    def test_presentation_only_or_unretained_definitions_cannot_qualify(self):
        self.refinement.write_text(json.dumps({'groupingChanged': False, 'filtersChanged': False, 'actualSdkRefinement': True}))
        with self.assertRaises(ValueError): live.verdict(self.report, self.root)
        self.refinement.write_text(json.dumps({'groupingChanged': True, 'filtersChanged': True, 'actualSdkRefinement': True}))
        self.retained.write_text(json.dumps({'questionId': 'question', 'dashboardId': 'dashboard', 'tileIds': ['duplicate', 'duplicate'], 'deferredToOwnedRestart': True}))
        with self.assertRaises(ValueError): live.verdict(self.report, self.root)

    def test_reload_recreated_stores_failed_restart_or_provider_send_cannot_qualify(self):
        proof = self.root / 'restart.json'
        value = {'providerDisabled': True, 'sameQuestion': True, 'sameDashboard': True, 'sameTileOrderAndSizes': True, 'sdkSendsBefore': 0, 'sdkSendsAfter': 0}
        proof.write_text(json.dumps(value))
        required = {'source', 'trace', 'restart-proof.json'} | {f'{name}.{suffix}' for name in live.RESTART_CAPTURES for suffix in ('png', 'aria.yml')}
        report = {'suites': [{'specs': [{'title': live.RESTART_TITLE, 'tests': [{'projectName': 'live-desktop', 'expectedStatus': 'passed', 'status': 'expected', 'results': [{'status': 'passed', 'attachments': [{'name': name, 'path': str(proof if name == 'restart-proof.json' else self.trace if name == 'trace' else self.artifact)} for name in required]}]}]}]}]}
        hosts = [{'backendPid': 11, 'studioPid': 12, 'backendExit': 0, 'studioExit': 0}, {'backendPid': 21, 'studioPid': 22, 'backendExit': 0, 'studioExit': 0, 'sameOwnedStores': True}]
        self.assertEqual(1, live.restart_verdict(report, self.root, 'proof', hosts))
        for change in ('reload', 'recreated-stores', 'failed-stop', 'provider-send', 'changed-definition', 'missing-execution'):
            with self.subTest(change=change):
                altered = copy.deepcopy(hosts); browser = copy.deepcopy(report); evidence = dict(value)
                if change == 'reload': altered = hosts[:1]
                if change == 'recreated-stores': altered[1]['sameOwnedStores'] = False
                if change == 'failed-stop': altered[0]['backendExit'] = -15
                if change == 'provider-send': evidence['sdkSendsAfter'] = 1
                if change == 'changed-definition': evidence['sameQuestion'] = False
                if change == 'missing-execution': browser['suites'][0]['specs'].clear()
                proof.write_text(json.dumps(evidence))
                with self.assertRaises(ValueError): live.restart_verdict(browser, self.root, 'proof', altered)

    def test_keyboard_enter_drill_qualifies_but_other_keys_do_not(self):
        original = self.trace.read_bytes()
        for name in ('Enter', 'Tab', 'Escape'):
            with self.subTest(name=name):
                with zipfile.ZipFile(self.trace, 'w') as archive:
                    for number, role in enumerate(('admin', 'dashboard-reader')):
                        archive.writestr(f'{number}.trace', '\n'.join(json.dumps(value) for value in [
                            {'type': 'before', 'method': 'fill', 'params': {'selector': 'username', 'value': role}},
                            {'type': 'before', 'method': 'press', 'params': {'selector': 'View matching records for result 1', 'key': name}},
                            {'type': 'frame-snapshot'}]))
                if name == 'Enter': self.assertEqual(2, len(live.trace_context_evidence(self.trace, owner_drill=True)))
                else:
                    with self.assertRaises(ValueError): live.trace_context_evidence(self.trace, owner_drill=True)
        self.trace.write_bytes(original)

    def test_pointer_or_programmatic_focus_or_absent_real_keys_cannot_qualify(self):
        path = self.root / 'keyboard.json'
        test = self.report['suites'][0]['specs'][0]
        title = test['title']
        test['tests'][0]['results'][0]['attachments'].append({'name': 'keyboard-proof.json', 'path': str(path)})
        original = {'pointerEvents': [], 'directFocusCalls': 0, 'tabs': 2, 'focusedTargets': 1, 'keys': {'Enter': 1}}
        path.write_text(json.dumps(original))
        live.require_keyboard_artifact(self.report, self.root, title, 'keyboard-proof.json')
        for key, value in [('pointerEvents', ['mousedown']), ('directFocusCalls', 1), ('tabs', 0), ('focusedTargets', 0), ('keys', {})]:
            with self.subTest(key=key):
                path.write_text(json.dumps({**original, key: value}))
                with self.assertRaises(ValueError): live.require_keyboard_artifact(self.report, self.root, title, 'keyboard-proof.json')

    def test_a_reader_screenshot_without_recorded_actions_or_dom_cannot_qualify(self):
        with zipfile.ZipFile(self.trace, 'w') as archive:
            archive.writestr('0-trace.trace', '\n'.join(json.dumps(value) for value in [
                {'type': 'before', 'method': 'fill', 'params': {'selector': 'internal:label=username', 'value': 'admin'}},
                {'type': 'before', 'method': 'click', 'params': {'selector': 'View matching records for result 1'}},
                {'type': 'frame-snapshot'}]))
        with self.assertRaises(ValueError): live.verdict(self.report, self.root)

    def test_missing_fresh_principal_or_denial_cannot_qualify(self):
        for evidence in ({'executeStatus': 403}, {'readerReauthenticated': True, 'executeStatus': 200}):
            self.authority.write_text(json.dumps(evidence))
            with self.assertRaises(ValueError): live.verdict(self.report, self.root)

    def test_complete_actual_sdk_boundary_evidence(self):
        self.assertEqual(2, live.verdict(self.report, self.root))

    def test_missing_skipped_retried_extra_or_fake_provider_cannot_qualify(self):
        for change in ('missing', 'skipped', 'retried', 'duplicate', 'artifact', 'fake-provider', 'native-send', 'hidden-send', 'no-authoring', 'missing-completion', 'missing-clarification', 'missing-proposal', 'privacy'):
            with self.subTest(change=change):
                report = copy.deepcopy(self.report)
                row = report['suites'][0]['specs'][0]
                result = row['tests'][0]['results'][0]
                evidence = self.evidence()
                if change == 'missing': report['suites'][0]['specs'].pop()
                if change == 'skipped': result['status'] = 'skipped'
                if change == 'retried': row['tests'][0]['results'].append(copy.deepcopy(result))
                if change == 'duplicate': report['suites'][0]['specs'].append(copy.deepcopy(row))
                if change == 'artifact': result['attachments'].pop()
                if change == 'fake-provider': evidence['after']['category'] = 'deterministic-provider-protocol'
                if change == 'native-send': evidence['nativePhases'][0]['delta'] = 1
                if change == 'hidden-send': evidence['after']['counts']['session.send'] = 4
                if change == 'no-authoring': evidence['refinement']['after']['counts']['session.send'] = 0
                if change == 'missing-completion': evidence['refinement']['after']['counts']['event.Completed'] = 0
                if change == 'missing-clarification': evidence['turns']['afterClarification']['counts']['tool.return.dashboard_request_clarification'] = 0
                if change == 'missing-proposal': evidence['refinement']['after']['counts']['tool.return.dashboard_preview_question'] = 0
                if change == 'privacy': evidence['after']['privacyFailures'] = 1
                self.boundary.write_text(json.dumps(evidence))
                with self.assertRaises(ValueError): live.verdict(report, self.root)

    def test_external_or_empty_artifact_cannot_qualify(self):
        self.artifact.write_text('')
        with self.assertRaises(ValueError): live.verdict(self.report, self.root)
        self.artifact.write_text('proof')
        with self.assertRaises(ValueError): live.verdict(self.report, self.root / 'other')

    def test_browser_source_attachment_must_match_current_committed_revision(self):
        with self.assertRaises(ValueError): live.verdict(self.report, self.root, 'different-sha')



class NativeEdgeVerdictTests(unittest.TestCase):
    def test_nonzero_sdk_send_or_unperformed_repair_cannot_qualify_native_edges(self):
        for mutation in ('sdkSendsAfter','explicitRepairSavedNewRevision','oldHistoryWithheld','boundedCostRetrySucceeded'):
            with self.subTest(mutation=mutation),tempfile.TemporaryDirectory() as directory:
                root=Path(directory);report=self.fixture(root)
                proof_path=root/'native-edge-proof.json';proof=json.loads(proof_path.read_text())
                proof[mutation]=1 if mutation=='sdkSendsAfter' else False;proof_path.write_text(json.dumps(proof))
                with self.assertRaises(ValueError):live.native_edge_verdict(report,root,'fixture-sha')

    def test_missing_or_skipped_native_experience_cannot_qualify(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);report=self.fixture(root)
            (root/'edge-module-history-withheld.aria.yml').unlink()
            with self.assertRaises(ValueError):live.native_edge_verdict(report,root,'fixture-sha')
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory);report=self.fixture(root)
            report['suites'][0]['specs'][0]['tests'][0]['results'][0]['status']='skipped'
            with self.assertRaises(ValueError):live.native_edge_verdict(report,root,'fixture-sha')

    def fixture(self,root):
        proof={key:True for key in ('actualModuleDisabledAndRestored','explicitRepairSavedNewRevision','sameQuestion','oldHistoryWithheld','newHistoryAvailable','noEligibleDenominator','noEligibleValues','priorResultGeneratedAtRetained','boundedCostRetrySucceeded','failedValuesWithheld','transportRetrySucceeded')}
        proof.update(staleReason='definition changed',emptyBaselineCoverage='unknown',boundedCostError='QUERY_LIMIT',failureKind='owned browser transport offline',sdkSendsBefore=0,sdkSendsAfter=0)
        attachments=[]
        for name in {'source','native-edge-proof.json','edge-native-weaver-boundary.json'}|{f'{capture}.{suffix}' for capture in live.EDGE_CAPTURES for suffix in ('png','aria.yml')}:
            path=root/name;path.write_text('fixture-sha' if name=='source' else json.dumps(proof) if name=='native-edge-proof.json' else '{"genericWeaverMessagePosts":0}' if name=='edge-native-weaver-boundary.json' else 'synthetic refusal control')
            attachments.append({'name':name,'path':str(path)})
        path=root/'trace.zip'
        with zipfile.ZipFile(path,'w') as archive:archive.writestr('0-trace.trace','{"type":"frame-snapshot","snapshot":{"html":"Replace unavailable query"}}\n')
        attachments.append({'name':'trace','path':str(path)})
        report={'suites':[{'specs':[{'title':'native Dashboard module repair and edge states','tests':[{'projectName':'live-desktop','expectedStatus':'passed','status':'expected','results':[{'status':'passed','attachments':attachments}]}]}]}]}
        self.assertEqual(1,live.native_edge_verdict(report,root,'fixture-sha'))
        return report

class NativeSourceFenceTests(unittest.TestCase):
    def test_changed_source_invalidates_native_qualification_flag(self):
        with tempfile.TemporaryDirectory() as directory:
            output=Path(directory)/'proof'
            options=argparse.Namespace(output=output,mode='native-edges',model='pinned',account_access_restored=False)
            def success(_output,_options,_native,report):report.update(status='passed',stage='complete',nativeEdgesQualified=True)
            with patch.object(live,'materialize',return_value=(Path('project'),Path('hook'),[])),patch.object(live,'build'),patch.object(live,'execute_live',side_effect=success),patch.object(live,'source_state',side_effect=[{'sha':'before','dirty':''},{'sha':'after','dirty':''}]):
                with self.assertRaises(ValueError):live.run(options)
            report=json.loads((output/'report.json').read_text())
            self.assertEqual('failed',report['status'])
            self.assertFalse(report['nativeEdgesQualified'])
            self.assertFalse(report['liveQualified'])


class UnsupportedVerdictTests(unittest.TestCase):
    def test_fabrication_substitution_or_missing_actual_provider_cannot_qualify(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            evidence={'before':{'category':'actual-sdk-forwarding','counts':{},'privacyFailures':0},'after':{'category':'actual-sdk-forwarding','counts':{'session.send':1,'event.Completed':1},'privacyFailures':0},'originalRequestPreserved':True,'limitationTextObserved':True,'proposedQuestions':0,'nativeQueryExecutions':0,'resourceWrites':0,'savedResourcesUnchanged':True,'selectedDatasetStillEmpty':True,'structuredMetricSubstitution':False,'explanation':'Acme does not expose revenue or cost, so net profit cannot be calculated. Supply an authorized financial dataset to continue.','proseSemanticAcceptance':'requires independent review'}
            names={'source','trace','unsupported-proof.json'}|{f'{name}.{suffix}' for name in live.UNSUPPORTED_CAPTURES for suffix in ('png','aria.yml')}
            for name in names:
                if name=='trace':continue
                (root/name).write_text('fixture-sha' if name=='source' else json.dumps(evidence) if name=='unsupported-proof.json' else 'synthetic refusal control')
            trace=root/'trace.zip'
            with zipfile.ZipFile(trace,'w') as archive:archive.writestr('0-trace.trace','{"type":"frame-snapshot","snapshot":{"html":"Your question"}}\n')
            report={'suites':[{'specs':[{'title':live.UNSUPPORTED_TITLE,'tests':[{'projectName':'live-desktop','expectedStatus':'passed','status':'expected','results':[{'status':'passed','attachments':[{'name':name,'path':str(trace if name=='trace' else root/name)} for name in names]}]}]}]}]}
            self.assertEqual(1,live.unsupported_verdict(report,root,'fixture-sha'))
            for change in ('proposedQuestions','nativeQueryExecutions','resourceWrites','structuredMetricSubstitution','originalRequestPreserved','savedResourcesUnchanged','provider'):
                altered=copy.deepcopy(evidence)
                if change=='provider':altered['after']['counts']['session.send']=0
                elif change=='structuredMetricSubstitution':altered[change]=True
                elif change in {'originalRequestPreserved','savedResourcesUnchanged'}:altered[change]=False
                else:altered[change]=1
                (root/'unsupported-proof.json').write_text(json.dumps(altered))
                with self.subTest(change=change),self.assertRaises(ValueError):live.unsupported_verdict(report,root,'fixture-sha')


class UnsupportedExplanationTests(unittest.TestCase):
    def test_mixed_refusal_and_fabricated_financial_answer_cannot_qualify(self):
        for explanation in ('Profit is unavailable, but your net profit is $100.', 'Revenue is missing, but net profit equals 100.', 'Cost is unsupported; margin is 20%.'):
            with self.subTest(explanation=explanation),self.assertRaises(ValueError):live.require_unsupported_explanation(explanation)
        live.require_unsupported_explanation('The dataset does not expose revenue or cost. Net profit cannot be calculated from processing duration or record counts. Supply an authorized financial dataset or revise the question.')


if __name__ == '__main__': unittest.main()
