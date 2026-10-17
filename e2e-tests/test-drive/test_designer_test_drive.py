#!/usr/bin/env python3
"""REST acceptance journey for unpublished designer Test Drive. Python 3 stdlib; starts/stops no processes."""
import argparse
import hashlib
import http.cookiejar
import json
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path


class Elsa:
    def __init__(self, base_url, auth_mode='bearer'):
        self.base = base_url.rstrip('/')
        self.headers = {}
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.call('/_elsa/identity/login', {'username': 'admin', 'password': 'Password123!'})
        if auth_mode == 'bearer':
            token = self.call('/_elsa/identity/token')['accessToken']
            self.opener = urllib.request.build_opener()
            self.headers['Authorization'] = 'Bearer ' + token

    def exchange(self, path, body=None, method=None, headers=None):
        content = body.encode() if isinstance(body, str) else json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(self.base + path, content,
                                         {'Content-Type': 'application/json', **self.headers, **(headers or {})}, method=method)
        try:
            response = self.opener.open(request, timeout=60)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            return response.status, response.read().decode(), dict(response.headers)

    def call(self, path, body=None, method=None):
        status, content, _ = self.exchange(path, body, method)
        if not 200 <= status < 300:
            raise AssertionError(f'{method or ("POST" if body is not None else "GET")} {path}: HTTP {status}: {content[:500]}')
        return json.loads(content) if content else None

    def activity(self, name):
        result = self.call('/design/activities/definitions?search=' + urllib.parse.quote(name))
        return next(item['definition']['headVersionId'] for item in result['items']
                    if item['definition']['activityTypeKey'].endswith('.' + name))

    def instance(self, execution_id, expected, timeout=30):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            result = self.call('/runtime/workflows/instances/' + execution_id)
            waiting = expected == 'Waiting' and any(
                activity['status'] == 'Suspended' and activity.get('bookmarkCount', 0) > 0
                for activity in result['activities'])
            if result['instance']['status'] == expected or waiting:
                assert not result.get('incidents'), result.get('incidents')
                return result
            if result.get('incidents') or result['instance']['status'] == 'Faulted':
                raise AssertionError(f'Unexpected execution fault: {result}')
            time.sleep(.2)
        raise AssertionError(f'Execution {execution_id} did not become {expected}: {result}')


def literal(key, value):
    return {'referenceKey': key, 'value': {'value': value, 'expressionType': 'Literal'}}


def node(api, node_id, activity, **inputs):
    return {'nodeId': node_id, 'activityVersionId': api.activity(activity),
            'inputs': [literal(key, value) for key, value in inputs.items()], 'outputs': []}


def state(api, wait=False):
    http = node(api, 'http-start', 'HttpEndpoint', Path='orders/{orderId}', SupportedMethods=['POST'],
                CanStartWorkflow=True, ResponseMode='Sync', RequestSizeLimit=4096)
    event = node(api, 'event-start', 'Event', EventName='OrderImported', CanStartWorkflow=True)
    timer = node(api, 'timer-start', 'Timer', Interval='PT5M')
    cron = node(api, 'cron-start', 'Cron', Expression='0 9 * * *')
    response = node(api, 'response', 'WriteHttpResponse', StatusCode=200, ContentType='application/json',
                    Body='{"accepted":true,"demo":"test-drive"}')
    approval = node(api, 'approval', 'Event', EventName='ApprovalReceived', CanStartWorkflow=False)
    end = node(api, 'done', 'WriteLine', text='Order test completed')
    starts = [http, event, timer, cron]
    target = approval if wait else end
    activities = starts + [response, target] + ([end] if wait else [])
    edges = [('http-start', 'response'), ('response', target['nodeId'])] + [(start['nodeId'], target['nodeId']) for start in starts[1:]]
    if wait:
        edges.append(('approval', 'done'))
    root = node(api, 'root', 'Flowchart')
    root['structure'] = {'kind': 'elsa.flowchart.structure', 'schemaVersion': '1.0.0',
                         'payload': {'startNodeId': 'http-start', 'activities': activities, 'variables': [],
                                     'connections': [{'source': {'nodeId': source, 'port': 'Done'}, 'target': {'nodeId': target, 'port': 'Done'}} for source, target in edges]}}
    return {'rootActivity': root, 'variables': [], 'outputs': [], 'workflowActivityOptions': None,
            'strategyOptions': {'persistenceMode': 'Durable'},
            'inputs': [{'referenceKey': 'customerId', 'name': 'customerId', 'displayName': 'Customer ID',
                        'type': {'alias': 'String', 'collectionKind': 'single'}, 'storageDriverType': None,
                        'category': None, 'isNullable': False, 'isRequired': True}]}


def draft(api, name, waiting=False):
    authored = state(api, waiting)
    layout = [{'nodeId': item, 'x': x, 'y': y} for item, x, y in [
        ('http-start', 80, 60), ('event-start', 80, 280), ('timer-start', 80, 500), ('cron-start', 80, 720),
        ('response', 460, 60), ('approval', 830, 280), ('done', 1180, 280)]]
    response = api.call('/design/workflows/definitions', {'name': name, 'description': 'Unpublished modular Test Drive demo.',
        'initialState': authored, 'layout': layout,
        'activityPresentation': [{'nodeId': item, 'displayName': label} for item, label in [
            ('http-start', 'Receive order'), ('event-start', 'Import order'), ('timer-start', 'Every five minutes'),
            ('cron-start', 'Daily at nine UTC'), ('response', 'Acknowledge order'), ('approval', 'Wait for approval'), ('done', 'Order completed')]]})
    definition_id = response['definition']['id']
    return definition_id, authored


def request(definition_id, authored):
    return {'definitionId': definition_id, 'snapshotId': 'snapshot-' + uuid.uuid4().hex, 'state': authored,
            'inputs': {'customerId': 'cust-1042'}, 'acknowledgeUnavailableExpressionValidation': True}


def run_journey(api, output_dir):
    evidence = {}
    definition_id, authored = draft(api, 'Order intake · Test Drive', waiting=True)
    req = request(definition_id, authored)
    discovered = api.call('/publishing/workflows/drafts/test-run-options', req)
    options = {item['driverId']: item for item in discovered['options'] if item['isAvailable']}
    assert set(options) == {'Elsa.HttpEndpoint', 'Elsa.Event', 'Elsa.Timer', 'Elsa.Cron'}, discovered
    evidence['options'] = discovered
    evidence['definitionId'] = definition_id
    output_dir.mkdir(parents=True, exist_ok=True)
    (output_dir / 'recording-workflow.json').write_text(json.dumps({'definitionId': definition_id, 'state': authored}, indent=2))

    invalid = dict(req, inputs={})
    rejected = api.call('/publishing/workflows/drafts/test-runs', invalid)
    assert rejected['status'] == 'Rejected' and rejected['workflowExecutionId'] is None, rejected
    evidence['invalidInput'] = rejected

    event = options['Elsa.Event']
    event_request = dict(req, start={'kind': 'trigger', 'optionId': event['optionId'], 'modeId': 'event', 'data': {'payload': {'orderId': 'ord-7821'}}})
    run = api.call('/publishing/workflows/drafts/test-runs', event_request)
    assert run['workflowExecutionId'], run
    api.instance(run['workflowExecutionId'], 'Waiting')
    waits = api.call(f"/publishing/workflow-test-runs/{run['testRunId']}/waits")['waits']
    assert len(waits) == 1 and waits[0]['driverId'] == 'Elsa.Event', waits
    invalid_delivery = api.call(f"/publishing/workflow-test-runs/{run['testRunId']}/events",
        {'waitId': '', 'deliveryId': str(uuid.uuid4()), 'modeId': 'event'})
    assert invalid_delivery['status'] == 'Rejected', invalid_delivery
    global_result = api.call('/runtime/workflows/stimuli', {'stimulusType': 'Event', 'stimulusHash': 'sha256:' + hashlib.sha256(b'ApprovalReceived').hexdigest(), 'mode': 'ResumeOnly'})
    assert global_result['resumedCount'] == 0, global_result
    api.instance(run['workflowExecutionId'], 'Waiting')
    delivery = {'waitId': waits[0]['waitId'], 'deliveryId': str(uuid.uuid4()), 'modeId': 'event', 'data': {'payload': {'eventName': 'ApprovalReceived'}}}
    delivered = api.call(f"/publishing/workflow-test-runs/{run['testRunId']}/events", delivery)
    assert delivered['status'] == 'Delivered', delivered
    api.instance(run['workflowExecutionId'], 'Completed')
    repeated = api.call(f"/publishing/workflow-test-runs/{run['testRunId']}/events", delivery)
    assert repeated['status'] == 'AlreadyDelivered', repeated
    changed = dict(delivery, data={'payload': {'approved': False}})
    conflict = api.call(f"/publishing/workflow-test-runs/{run['testRunId']}/events", changed)
    assert conflict['status'] == 'Rejected', conflict
    evidence['event'] = {'run': run, 'delivery': delivered, 'retry': repeated, 'conflict': conflict, 'globalIngress': global_result}

    http = options['Elsa.HttpEndpoint']
    http_request = dict(req, start={'kind': 'trigger', 'optionId': http['optionId'], 'modeId': 'http-request', 'data': {'routeValues': {'orderId': 'ord-7821'}, 'query': {}, 'headers': {}, 'body': '{"priority":"normal"}'}})
    prepared = api.call('/publishing/workflows/drafts/test-runs', http_request)
    assert prepared['status'] == 'Prepared' and prepared['workflowExecutionId'] is None, prepared
    action = prepared['driverAction']
    wrong = api.exchange(action['relativeUrl'], method='GET')
    assert wrong[0] == 405, wrong
    still_prepared = api.call(f"/publishing/workflow-test-runs/{prepared['testRunId']}")
    assert still_prepared['workflowExecutionId'] is None, still_prepared
    actual = api.exchange(action['relativeUrl'], '{"priority":"normal"}', action['method'])
    assert actual[0] == 200 and json.loads(actual[1])['accepted'], actual
    refreshed = api.call(f"/publishing/workflow-test-runs/{prepared['testRunId']}")
    api.instance(refreshed['workflowExecutionId'], 'Waiting')
    evidence['http'] = {'prepared': prepared, 'exchange': {'status': actual[0], 'body': actual[1]}, 'run': refreshed}
    stopped = api.call(f"/publishing/workflow-test-runs/{prepared['testRunId']}/stop", {})
    api.instance(refreshed['workflowExecutionId'], 'Cancelled')
    expired = api.exchange(action['relativeUrl'], '{}', action['method'])
    assert expired[0] in [404, 410], expired
    evidence['stop'] = {'view': stopped, 'laterHttpStatus': expired[0]}

    for driver_id in ['Elsa.Timer', 'Elsa.Cron']:
        option = options[driver_id]
        config = option['modes'][0]['configuration']
        assert config['timezone'] == 'UTC' and config['nextOccurrences'], config
        scheduled = api.call('/publishing/workflows/drafts/test-runs', dict(req, start={'kind': 'trigger', 'optionId': option['optionId'], 'modeId': 'fire-now'}))
        api.instance(scheduled['workflowExecutionId'], 'Waiting')
        api.call(f"/publishing/workflow-test-runs/{scheduled['testRunId']}/stop", {})
        evidence[driver_id] = {'configuration': config, 'run': scheduled}
    (output_dir / 'test-drive-evidence.json').write_text(json.dumps(evidence, indent=2))
    print('PASS: unpublished inputs, selected Event/HTTP/Timer/Cron, real HTTP 200, scoped wait, retry/conflict, global isolation, actual Cancelled root.')
    print('Recording workflow:', definition_id)
    return evidence


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base-url', default='http://localhost:5095')
    parser.add_argument('--auth-mode', choices=['bearer', 'cookie'], default='bearer')
    parser.add_argument('--output-dir', type=Path, required=True)
    args = parser.parse_args()
    run_journey(Elsa(args.base_url, args.auth_mode), args.output_dir)
