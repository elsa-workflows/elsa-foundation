import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import threading
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

    def test_jobs_selection_requires_all_six_real_journeys_in_every_project_and_its_own_artifacts(self):
        model = self.root / 'model.json'
        model.write_text(json.dumps({'state': 'PASSED', 'cleanupErrors': [], 'cases': [{'kind': 'native', 'state': 'PASSED'}, {'kind': 'bpmn', 'state': 'PASSED'}]}))
        export_artifact = self.root / 'report-export.json'
        export_artifact.write_text('{"payload":{"value":{"total":9007199254740993}}}')
        report = {'suites': [{'specs': [
            {'title': journey, 'tests': [
                {'projectName': project, 'expectedStatus': 'passed', 'status': 'expected', 'results': [
                    {'status': 'passed', 'attachments': [{'name': name, 'path': str(model if name == 'authored-native-genuine-bpmn-jobs.json' else export_artifact if name == 'report-export.json' else self.artifact)} for name in
                        ['trace', 'source', 'console.json', 'network.json'] + (['authored-native-genuine-bpmn-jobs.json'] if journey == 'jobs native and BPMN continuation' else []) + (['report-export.json'] if journey == 'jobs standalone reports' else []) + [f'{capture}.{suffix}' for capture in captures for suffix in ('png', 'aria.yml')]]}]}
                for project in acceptance.PROJECTS]}
            for journey, captures in acceptance.JOBS_CAPTURES.items()]}]}
        self.assertEqual(24, acceptance.verdict(report, self.root, 'jobs'))
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'jobs', journeys=['jobs standalone reports'])
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'jobs', journeys=acceptance.JOBS_CAPTURES)
        with self.assertRaises(ValueError): acceptance.verdict(self.report, self.root, 'jobs')
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'baseline')
        for fault in ('missing-journey', 'retried', 'skipped', 'missing-network', 'duplicate'):
            with self.subTest(fault=fault):
                damaged = copy.deepcopy(report)
                row = damaged['suites'][0]['specs'][0]['tests'][0]
                if fault == 'missing-journey': damaged['suites'][0]['specs'].pop()
                if fault == 'retried': row['results'].append(copy.deepcopy(row['results'][0]))
                if fault == 'skipped': row['results'][0]['status'] = 'skipped'
                if fault == 'missing-network': row['results'][0]['attachments'] = [item for item in row['results'][0]['attachments'] if item['name'] != 'network.json']
                if fault == 'duplicate': damaged['suites'][0]['specs'][0]['tests'].append(copy.deepcopy(row))
                with self.assertRaises(ValueError): acceptance.verdict(damaged, self.root, 'jobs')
        model.write_text(json.dumps({'state': 'PASSED', 'cleanupErrors': [], 'cases': [{'kind': 'native', 'state': 'PASSED'}, {'kind': 'bpmn', 'state': 'NOT RUN'}]}))
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'jobs')
        model.write_text(json.dumps({'state': 'PASSED', 'cleanupErrors': [], 'cases': [{'kind': 'native', 'state': 'PASSED'}, {'kind': 'bpmn', 'state': 'PASSED'}]}))
        export_artifact.write_text('{"payload":{"value":{"total":9007199254740992}}}')
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'jobs')
        export_artifact.write_text('{"payload":{"value":{"total":9007199254740993}}}')
        with self.assertRaises(ValueError): acceptance.verdict(report, self.root, 'jobs', source_sha='0' * 40)

    def test_jobs_private_shell_has_only_fixture_owned_scopes_and_real_model_features(self):
        shells = {'CShells': {'Shells': {'default': {'Features': {'Jobs': {'TenantId': 'caller'}}}}}}
        acceptance.configure_jobs_shell(shells)
        features = shells['CShells']['Shells']['default']['Features']
        self.assertNotIn('Jobs', features)
        self.assertEqual('default', features['JobsApi']['PersistencePartition'])
        self.assertEqual('default', features['JobsWorkflowsRuntime']['TenantId'])
        self.assertTrue(features['ActivitiesBpmnHosting']['AdmitNewExecutions'])
        self.assertEqual('Data Source=jobs-ux.db;Pooling=False', features['JobsEntityFrameworkCore']['ConnectionString'])

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

    def test_control_start_and_restart_cancel_published_child_and_drain_handlers_before_owned_store_cleanup(self):
        # Actual host generator/lifecycle/control dispatch and handler bookkeeping, but no
        # socket bind, OS child, SDK or host: deterministic barriers replace those boundaries.
        from io import BytesIO
        from unittest.mock import MagicMock, Mock
        real_server = acceptance.OwnedControlServer
        for operation in ('start', 'restart'):
            with self.subTest(operation=operation):
                directory = self.root / operation
                directory.mkdir()
                stores = directory / 'stores'
                stores.mkdir()
                readiness_entered = threading.Event()
                child_stopped = threading.Event()
                readiness_returned = threading.Event()
                serve_stopped = threading.Event()
                cleanup_complete = threading.Event()
                spawned = []
                stopped = []
                handlers = []
                servers = []
                class Process:
                    def __init__(self, number):
                        self.pid = 100 + number
                        self.stopped = False
                    def poll(self):
                        return 0 if self.stopped else None
                def spawn(*args, **kwargs):
                    self.assertFalse(cleanup_complete.is_set(), 'No owned child may start after cleanup')
                    process = Process(len(spawned) + 1)
                    spawned.append(process)
                    kwargs['stdout'].write('Now listening on: http://localhost:1\n')
                    kwargs['stdout'].flush()
                    return process
                def stop(process):
                    self.assertTrue(stores.is_dir(), 'A child must stop before its owned store is removed')
                    stopped.append(process)
                    process.stopped = True
                    if len(spawned) > 1 and process is spawned[1]:
                        child_stopped.set()
                def read_ready(*args, **kwargs):
                    if len(spawned) == 2:
                        readiness_entered.set()
                        self.assertTrue(child_stopped.wait(2), 'Teardown must stop the published child before waiting for readiness')
                        self.assertTrue(stores.is_dir(), 'Readiness must drain before store deletion')
                        readiness_returned.set()
                    response = MagicMock()
                    response.__enter__.return_value = Mock(status=200)
                    return response
                def server_factory(_address, handler):
                    server = real_server.__new__(real_server)
                    server.requests_lock = threading.Lock()
                    server.requests = {}
                    server.threads = set()
                    server.accepting = True
                    server.server_port = 12345
                    server.serve_forever = lambda: serve_stopped.wait(5)
                    server.shutdown = serve_stopped.set
                    server.server_close = Mock()
                    server.shutdown_request = Mock()
                    server.handler = handler
                    servers.append(server)
                    return server
                def dispatch(_request, _address):
                    _request.handler.do_POST()
                with patch.object(acceptance.subprocess, 'Popen', side_effect=spawn), \
                     patch.object(acceptance, 'stop_group', side_effect=stop), \
                     patch.object(acceptance.urllib.request, 'urlopen', side_effect=read_ready), \
                     patch.object(acceptance, 'OwnedControlServer', side_effect=server_factory), \
                     patch.object(acceptance.ThreadingHTTPServer, 'process_request_thread', side_effect=dispatch):
                    backend = acceptance.RestartableHost(directory, 'host.dll', 'http://localhost:1', '/', directory / 'host.log', {})
                    with self.assertRaisesRegex(RuntimeError, 'Owned backend restart failed: InterruptedError'):
                        with backend:
                            if operation == 'start':
                                backend.stop()
                            with acceptance.restart_control(backend) as control:
                                server = servers[0]
                                def submit(path):
                                    handler = server.handler.__new__(server.handler)
                                    handler.path = path
                                    handler.headers = {'Authorization': 'Bearer ' + control['UX_RESTART_TOKEN']}
                                    handler.send_error = Mock()
                                    handler.send_response = Mock()
                                    handler.send_header = Mock()
                                    handler.end_headers = Mock()
                                    handler.wfile = BytesIO()
                                    handlers.append(handler)
                                    request = Mock(handler=handler)
                                    server.process_request(request, ('127.0.0.1', 1))
                                submit('/' + operation)
                                self.assertTrue(readiness_entered.wait(2))
                                self.assertIs(backend.child.process, spawned[1])
                                # This accepted operation waits behind startup's lifecycle lock.
                                # After cancellation it must drain without creating a third child.
                                submit('/start')
                    self.assertTrue(readiness_returned.is_set())
                    self.assertTrue(serve_stopped.is_set())
                    self.assertFalse(server.requests, 'All accepted handlers must drain before backend exit/store cleanup')
                    self.assertFalse(server.threads, 'Every accepted handler thread must be joined, including a just-finished handler')
                    self.assertIsNone(backend.context)
                    self.assertIsNone(backend.child)
                    self.assertEqual(2, len(spawned))
                    self.assertEqual(spawned, stopped, 'Each owned process group stops exactly once')
                    self.assertTrue(all(process.stopped for process in spawned))
                    for handler in handlers:
                        handler.send_response.assert_not_called()
                        handler.send_error.assert_called_once_with(503, 'Owned restart failed')
                    cleanup_complete.set()
                    stores.rmdir()
                    with self.assertRaises(InterruptedError):
                        backend.start()
                    self.assertEqual(2, len(spawned), 'A late control cannot restart a child after store removal')


if __name__ == '__main__':
    unittest.main()
