"""Start one qualification workflow through node A's ordinary authenticated loopback API; prints its identities."""
import http.cookiejar, json, sys, urllib.parse, urllib.request

import os
BASE = os.environ.get('ELSA_EFFICIENCY_BASE', 'http://127.0.0.1:5095'); shape = sys.argv[1]; assert shape in ('delay', 'loop', 'status')
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))


def call(path, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(BASE + path, data=data, method='GET' if body is None else 'POST', headers={'Content-Type': 'application/json'})
    with opener.open(request, timeout=60) as response:
        text = response.read().decode()
        return json.loads(text) if text else None


def version(type_key):
    found = call('/design/activities/definitions?search=' + urllib.parse.quote(type_key))
    return next(x['definition']['headVersionId'] for x in found['items'] if x['definition']['activityTypeKey'] == type_key)


def literal(key, value): return {'referenceKey': key, 'value': {'value': value, 'expressionType': 'Literal'}}


call('/_elsa/identity/login', {'username': 'admin', 'password': 'Password123!'})
if shape == 'status':
    view = call('/runtime/workflows/instances/' + urllib.parse.quote(sys.argv[2]))
    print(json.dumps({'status': view['instance']['status'], 'incidentCount': view['instance'].get('incidentCount')})); sys.exit(0)
sequence, write = version('Elsa.Activities.Sequence.Activities.Sequence'), version('Elsa.Activities.Primitives.Activities.WriteLine')
before = {'nodeId': 'before', 'activityVersionId': write, 'inputs': [literal('text', 'before')], 'outputs': []}
if shape == 'delay':
    tail = {'nodeId': 'delay', 'activityVersionId': version('Elsa.Activities.Scheduling.Activities.Delay'), 'inputs': [literal('Duration', '00:30:00')], 'outputs': []}
else:
    body = {'nodeId': 'tick', 'activityVersionId': write, 'inputs': [literal('text', 't')], 'outputs': []}
    tail = {'nodeId': 'loop', 'activityVersionId': version('Elsa.Activities.While.Activities.While'), 'inputs': [literal('Condition', True)], 'outputs': [],
            'structure': {'kind': 'elsa.while.structure', 'schemaVersion': '1.0.0', 'payload': {'body': body}}}
root = {'nodeId': 'root', 'activityVersionId': sequence, 'inputs': [], 'outputs': [],
        'structure': {'kind': 'elsa.sequence.structure', 'schemaVersion': '1.0.0', 'payload': {'activities': [before, tail]}}}
submitted = call('/design/workflows/definitions/submit', {'name': 'eff-i68-cancel-' + shape, 'description': '', 'state': {'variables': [], 'inputs': [], 'outputs': [], 'rootActivity': root}})
published = call(f"/publishing/workflows/{submitted['version']['id']}/publish", {})
run = call(f"/runtime/workflows/executables/{published['artifactId']}/execute", {'sourceReferenceId': published['sourceReferenceId']})
print(json.dumps({'shape': shape, 'workflowExecutionId': run['workflowExecutionId'], 'artifactId': run.get('artifactId') or published['artifactId'],
                  'commandDispatchStatus': run.get('commandDispatchStatus'), 'agentProviderName': run.get('agentProviderName')}))
