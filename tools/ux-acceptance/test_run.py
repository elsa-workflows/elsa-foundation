import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
import zipfile
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
        self.trace = self.root / 'trace.zip'
        with zipfile.ZipFile(self.trace, 'w') as archive:
            for actor in ('owner', 'reader'):
                archive.writestr(actor + '.trace', json.dumps({'type': 'context-options', 'origin': 'library', 'contextId': actor}) + '\n')
        self.report = {'suites': [{'specs': [
            {'title': journey, 'tests': [
                {'projectName': project, 'expectedStatus': 'passed', 'status': 'expected', 'results': [
                    {'status': 'passed', 'attachments': [
                        {'name': name, 'path': str(self.trace if name == 'trace' else self.artifact)} for name in
                        ['trace'] + sorted(acceptance.ADDITIONAL_ARTIFACTS.get(journey, set())) + [f'{capture}.{suffix}' for capture in acceptance.CAPTURES[journey] for suffix in ('png', 'aria.yml')]]}]}
                for project in acceptance.PROJECTS]}
            for journey in acceptance.JOURNEYS]}]}

    def test_native_reader_must_be_recorded_in_the_combined_trace(self):
        with zipfile.ZipFile(self.trace, 'w') as archive:
            archive.writestr('owner.trace', json.dumps({'type': 'context-options', 'origin': 'library', 'contextId': 'owner'}) + '\n')
        with self.assertRaisesRegex(ValueError, 'independent owner and reader'):
            acceptance.verdict(self.report, self.root)

    def test_complete_evidence(self):
        self.assertEqual(len(acceptance.PROJECTS) * len(acceptance.JOURNEYS), acceptance.verdict(self.report, self.root))

    def test_compact_inputs_preserve_inherited_named_journeys(self):
        required = {'collections and connection creation', 'agent authoring and navigation',
                    'executable designer parity',
                    'compact runtime Inputs retain exact captured values and accessible technical details'}
        self.assertTrue(required <= acceptance.JOURNEYS)
        self.assertEqual(['reference', 'compact-values', 'technical-details', 'nested-values', 'details-closed', 'technical-details-320', 'compact-values-320'],
                         acceptance.CAPTURES['compact runtime Inputs retain exact captured values and accessible technical details'])

    def test_scoped_run_requires_every_project_and_rejects_extra_or_unknown_journeys(self):
        journey = sorted(acceptance.JOURNEYS)[0]
        report = copy.deepcopy(self.report)
        report['suites'][0]['specs'] = [row for row in report['suites'][0]['specs'] if row['title'] == journey]
        self.assertEqual(len(acceptance.PROJECTS), acceptance.verdict(report, self.root, [journey]))
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
        self.trace = self.root / 'trace.zip'
        with zipfile.ZipFile(self.trace, 'w') as archive:
            for actor in ('owner', 'reader'):
                archive.writestr(actor + '.trace', json.dumps({'type': 'context-options', 'origin': 'library', 'contextId': actor}) + '\n')
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

    def test_native_qualification_has_exact_private_grants_and_no_inference(self):
        settings = {}
        shells = {'CShells': {'Shells': {'default': {'Features': {'WeaverGitHubCopilot': {'Enabled': True}}}}}}
        acceptance.configure_dashboard_qualification(settings, shells)
        qualification = settings['Qualification']
        self.assertEqual(1, len(qualification['Principals']))
        self.assertEqual('default', qualification['Principals'][0]['tenant'])
        profiles = qualification['GrantProfiles']
        self.assertEqual({'dashboard-native-reader', 'dashboard-native-no-source'}, set(profiles))
        self.assertEqual({'workflows/dashboard-queries:read', 'workflows/activity-executions:inspect'},
                         set(profiles['dashboard-native-reader']) - set(profiles['dashboard-native-no-source']))
        self.assertFalse(any('*' in grant or 'agent' in grant or grant.endswith(':manage') or grant.endswith(':share')
                             for grant in qualification['Principals'][0]['exactDirectPermissions']))
        self.assertFalse(shells['CShells']['Shells']['default']['Features']['WeaverGitHubCopilot']['Enabled'])

    def test_exited_leader_does_not_hide_a_term_resistant_descendant(self):
        with patch.object(acceptance.os, 'killpg') as kill:
            from unittest.mock import Mock
            process = Mock(pid=44)
            process.poll.return_value = 0
            acceptance.stop_group(process)
            self.assertEqual([(44, acceptance.signal.SIGTERM), (44, acceptance.signal.SIGKILL)], [call.args for call in kill.call_args_list])

    def test_retired_group_permission_denial_does_not_fail_a_successful_command(self):
        from unittest.mock import Mock
        process = Mock(pid=44); process.poll.return_value = 0
        # A zombie is already dead; a different user's group must never be touched.
        members = f'{os.getuid()} 44 Z\n{os.getuid() + 1} 44 S\n'
        with patch.object(acceptance.os, 'killpg', side_effect=PermissionError('retired group')) as kill, \
             patch.object(acceptance.subprocess, 'check_output', return_value=members):
            acceptance.stop_group(process)
        self.assertEqual(2, kill.call_count)
        self.assertEqual(2, process.wait.call_count)

    def test_permission_denial_cannot_hide_a_live_owned_process(self):
        from unittest.mock import Mock
        for leader in (None, 0):
            process = Mock(pid=44); process.poll.return_value = leader
            with patch.object(acceptance.os, 'killpg', side_effect=PermissionError('live process')), \
                 patch.object(acceptance.subprocess, 'check_output', return_value=f'{os.getuid()} 44 S\n'):
                with self.assertRaises(PermissionError): acceptance.stop_group(process)

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




class SourcePairTests(unittest.TestCase):
    def test_dirty_studio_checkout_is_refused_even_when_backend_source_is_clean(self):
        with tempfile.TemporaryDirectory() as directory:
            studio = Path(directory).resolve() / "studio-source"
            output = Path(directory) / "evidence"
            state = lambda root: ("frontend-head", " M component.tsx") if root == studio else ("backend-head", "")
            with patch.object(acceptance, "source_state", side_effect=state), patch.object(acceptance, "command") as command:
                with self.assertRaisesRegex(ValueError, "dirty run"):
                    acceptance.run(output, 'existing home widgets and layout defaults', studio)
            command.assert_not_called()
            report = json.loads((output / "report.json").read_text())
            self.assertTrue(report["studioDirty"])
            self.assertEqual("frontend-head", report["studioSourceSha"])
            self.assertEqual("failed", report["status"])

    def test_unrelated_claimed_pr_head_is_refused_before_build(self):
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "evidence"
            with patch.dict(os.environ, {"UX_PR_HEAD_SHA": "f" * 40}), patch.object(acceptance, "source_state", return_value=("a" * 40, "")), patch.object(acceptance, "command") as command:
                with self.assertRaisesRegex(ValueError, "PR head"):
                    acceptance.run(output, 'existing home widgets and layout defaults')
            command.assert_not_called()
            self.assertEqual("failed", json.loads((output / "report.json").read_text())["status"])

    def test_ci_merge_accepts_only_its_reviewed_second_parent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args):
                return subprocess.check_output(['git', '-c', 'user.name=Owned test', '-c', 'user.email=owned@example.invalid', *args], cwd=root, text=True).strip()
            git('init', '-q'); (root / 'code.txt').write_text('base'); git('add', '.'); git('commit', '-qm', 'Base')
            base = git('rev-parse', 'HEAD'); git('checkout', '-qb', 'reviewed')
            (root / 'code.txt').write_text('reviewed'); git('commit', '-qam', 'Reviewed'); reviewed = git('rev-parse', 'HEAD')
            git('checkout', '-q', '--detach', base); git('merge', '--no-ff', '-m', 'Owned CI merge', 'reviewed')
            merged = git('rev-parse', 'HEAD')
            self.assertTrue(acceptance.supplies_pr_head(root, merged, reviewed))
            self.assertTrue(acceptance.supplies_pr_head(root, merged, merged))
            self.assertFalse(acceptance.supplies_pr_head(root, merged, base))
            self.assertFalse(acceptance.supplies_pr_head(root, reviewed, base))
            shallow = root / 'shallow-checkout'
            subprocess.run(['git', 'clone', '-q', '--depth', '1', root.resolve().as_uri(), str(shallow)], check=True, capture_output=True)
            self.assertTrue((shallow / '.git/shallow').is_file())
            self.assertTrue(acceptance.supplies_pr_head(shallow, merged, reviewed))
            self.assertFalse(acceptance.supplies_pr_head(shallow, merged, base))

    def test_reported_studio_source_is_the_checkout_actually_built(self):
        with tempfile.TemporaryDirectory() as directory:
            studio = Path(directory).resolve() / "studio-source"; output = Path(directory) / "evidence"; calls = []
            def command(argv, *_args, **kwargs):
                calls.append((argv, kwargs.get("cwd")))
                if "tests/browser/playwright.outlined-fields.config.ts" in argv:
                    self.assertTrue(1024 <= int(kwargs["env"]["OUTLINED_PORT"]) <= 65535)
                    gallery = Path(kwargs["env"]["OUTLINED_EVIDENCE"]); gallery.mkdir()
                    image = gallery / "unit-fixture.png"; image.write_bytes(b"explicit unit artifact fixture")
                    test = {"expectedStatus": "passed", "status": "expected", "results": [{"status": "passed", "attachments": [{"contentType": "image/png", "path": str(image)}]}]}
                    (gallery / "playwright.json").write_text(json.dumps({"config": {"metadata": {"outlinedCases": 1}}, "suites": [{"specs": [{"title": "Unit fixture", "tests": [test]}]}]}))
                if "studio-build.log" in str(_args): raise RuntimeError("Stop after verifying build selection")
            state = lambda root: ("frontend-head", "") if root == studio else ("backend-head", "")
            with patch.object(acceptance, "source_state", side_effect=state), patch.object(acceptance, "command", side_effect=command):
                with self.assertRaisesRegex(RuntimeError, "build selection"):
                    acceptance.run(output, 'existing home widgets and layout defaults', studio)
            self.assertEqual(studio / "Studio", calls[1][1])
            self.assertEqual(studio / "Studio", calls[2][1])
            self.assertIn("tests/browser/playwright.outlined-fields.config.ts", calls[2][0])
            self.assertIn(str(studio / "Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj"), calls[3][0])
            report = json.loads((output / "report.json").read_text())
            self.assertEqual("backend-head", report["sourceSha"])
            self.assertEqual("frontend-head", report["studioSourceSha"])
            self.assertEqual("frontend-head", report["isolatedFieldGallery"]["sourceSha"])
            self.assertFalse(report["isolatedFieldGallery"]["portOverrideConfirmed"])
            self.assertNotIn("url", report["isolatedFieldGallery"])


if __name__ == '__main__':
    unittest.main()


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
            self.assertEqual(1, acceptance.field_gallery_verdict(report, root, 4567))
            report['config']['metadata']['outlinedBaseURL'] = 'http://127.0.0.1:4567'
            self.assertEqual(1, acceptance.field_gallery_verdict(report, root, 4567))
            with self.assertRaises(ValueError): acceptance.field_gallery_verdict(report, root, 7654)
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
