import copy
import importlib.util
import json
import os
from pathlib import Path
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
                        ['trace'] + [f'{capture}.{suffix}' for capture in acceptance.CAPTURES[journey] for suffix in ('png', 'aria.yml')]]}]}
                for project in acceptance.PROJECTS]}
            for journey in acceptance.JOURNEYS]}]}

    def test_complete_evidence(self):
        self.assertEqual(len(acceptance.PROJECTS) * len(acceptance.JOURNEYS), acceptance.verdict(self.report, self.root))

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

    def trigger_report(self):
        specs = []
        for journey, captures in acceptance.TRIGGER_CAPTURES.items():
            tests = []
            for project in acceptance.TRIGGER_PROJECTS:
                mode = project.removeprefix('desktop-').removeprefix('narrow-').removeprefix('compact-')
                width = 320 if project == 'compact-light' else 360 if project.startswith('narrow-') else 1440
                evidence = self.root / f'{len(specs)}-{project}.json'
                evidence.write_text(json.dumps({'kind': 'composed-source', 'sourceSha': 'candidate', 'journey': journey,
                    'project': project, 'requests': [{'method': 'GET', 'path': '/runtime/standalone-triggers', 'status': 200}],
                    'observed': ['actual API result inspected'], 'unverified': [],
                    'captures': [{'name': name, 'viewport': {'width': width}, 'mode': mode, 'palette': 'meridian'} for name in captures]}))
                attachments = [{'name': name, 'path': str(self.artifact)} for name in
                    ['trace'] + [f'{capture}.{suffix}' for capture in captures for suffix in ('png', 'aria.yml')]]
                attachments.append({'name': 'standalone-evidence.json', 'path': str(evidence)})
                tests.append({'projectName': project, 'expectedStatus': 'passed', 'status': 'expected',
                              'results': [{'status': 'passed', 'attachments': attachments}]})
            specs.append({'title': journey, 'tests': tests})
        return {'suites': [{'specs': specs}]}

    def test_trigger_inventory_is_finite_and_separate_from_existing(self):
        report = self.trigger_report()
        self.assertEqual(49, acceptance.verdict(report, self.root, 'standalone-triggers', 'candidate'))
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'all', 'candidate')
        with self.assertRaises(ValueError): acceptance.verdict(self.report, self.root, 'standalone-triggers', 'candidate')

    def test_missing_trigger_boundary_stays_incomplete_even_when_browser_passes(self):
        for change in ('gap', 'capture', 'capture-metadata'):
            with self.subTest(change=change):
                report = self.trigger_report()
                attachments = report['suites'][0]['specs'][0]['tests'][0]['results'][0]['attachments']
                evidence = Path(attachments[-1]['path'])
                state = json.loads(evidence.read_text())
                if change == 'gap': state['unverified'] = [{'state': 'after-seal restart', 'reason': 'No actual process restart occurred'}]
                if change == 'capture':
                    attachments.pop(1)
                    state['captures'].pop(0)
                if change == 'capture-metadata': state['captures'].pop()
                evidence.write_text(json.dumps(state))
                with self.assertRaises(acceptance.IncompleteAcceptanceError):
                    acceptance.verdict(report, self.root, 'standalone-triggers', 'candidate')

    def test_trigger_evidence_rejects_stale_source_and_unexplained_or_mismatched_states(self):
        for change in ('source', 'unexplained', 'viewport', 'mode', 'missing-unverified', 'no-requests'):
            with self.subTest(change=change):
                report = self.trigger_report()
                evidence = Path(report['suites'][0]['specs'][0]['tests'][0]['results'][0]['attachments'][-1]['path'])
                state = json.loads(evidence.read_text())
                if change == 'source': state['sourceSha'] = 'stale'
                if change == 'unexplained': state['unverified'] = [{'state': 'missing'}]
                if change == 'viewport': state['captures'][0]['viewport']['width'] = 3600
                if change == 'mode': state['captures'][0]['mode'] = 'unrecorded'
                if change == 'missing-unverified': del state['unverified']
                if change == 'no-requests': state['requests'] = []
                evidence.write_text(json.dumps(state))
                with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'standalone-triggers', 'candidate')

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


if __name__ == '__main__':
    unittest.main()
