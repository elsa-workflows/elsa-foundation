const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto');
const { createRequire } = require('node:module');
const repo = path.resolve(__dirname, '../..');
const { request, expect } = createRequire(path.join(repo, 'Studio/package.json'))('@playwright/test');
const base = process.env.ELSA_WORKBENCH_URL, out = process.env.DECISIONS_EXECUTION_OUT, phase = process.env.DECISIONS_PHASE || 'prepare';
if (process.env.DECISIONS_OWNED_HOSTS !== '1' || !base || !out)
    throw Error('Explicit owned host/output required');
if (!['localhost', '127.0.0.1', '[::1]'].includes(new URL(base).hostname))
    throw Error('Owned loopback host required');
const reportPath = path.join(out, 'execution.json');
fs.mkdirSync(out, { recursive: true });
let report = phase === 'prepare' ? { status: 'running', phase, sourceSha: process.env.DECISIONS_SOURCE_SHA, cases: [] } : JSON.parse(fs.readFileSync(reportPath));
const save = () => fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
const literal = (referenceKey, value, expressionType = 'Literal') => ({ referenceKey, value: { value, expressionType }, autoEvaluate: null, evaluatorType: null, storageDriverType: null, isSensitive: null });
const node = (nodeId, activityVersionId, inputs = [], structure) => ({ nodeId, activityVersionId, inputs, outputs: [], ...(structure ? { structure } : {}) });
const sequence = activities => ({ kind: 'elsa.sequence.structure', schemaVersion: '1.0.0', payload: { activities } });
const capture = (scope) => ({ nodeId: 'capture', activityVersionId: 'elsa.intrinsic.set-output@1', outputs: [], intrinsic: { kind: 'setOutput', valueType: { alias: 'Elsa.Any', collectionKind: 'single' } }, inputs: [literal('name', 'decisionResult'), literal('value', { producerNodeId: 'decision', producerScopeId: scope, projectionKey: '$result' }, 'ActivityResult')] });
function snapshot(s) { if (!s || typeof s !== 'object')
    return s; if (s.truncated)
    throw Error('Truncated result'); if (s.kind === 'object')
    return Object.fromEntries(s.properties.map(x => [x.name, snapshot(x.value)])); if (s.kind === 'array')
    return s.items.map(snapshot); if (s.kind === 'string')
    return s.preview; if (s.kind === 'null')
    return null; return s.value ?? s; }
function result(view) { const o = view.outputs.decisionResult; expect(o.isRedacted).toBe(false); return snapshot(o.value); }
(async () => {
    const c = await request.newContext({ baseURL: base });
    async function api(method, url, data) { const r = await c.fetch(url, { method, ...(data === undefined ? {} : { data }) }); const t = await r.text(); if (!r.ok())
        throw Error(`${method} ${url}: ${r.status()} ${t}`); return t ? JSON.parse(t) : null; }
    async function wait(id, status = 'Completed') { for (let n = 0; n < 180; n++) {
        const v = await api('GET', `/runtime/workflows/instances/${id}`);
        if (status === 'Suspended' && v.activities.some(x => x.status === 'Suspended'))
            return v;
        if (['Completed', 'Faulted', 'Cancelled', 'Finished'].includes(v.instance.status)) {
            expect(v.instance.status, JSON.stringify(v.incidents)).toBe(status);
            return v;
        }
        await new Promise(r => setTimeout(r, 500));
    } throw Error(`Timeout ${id} ${status}`); }
    try {
        await api('POST', '/_elsa/identity/login', { username: 'admin', password: 'Password123!' });
        if (phase === 'prepare') {
            const xml = `<definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/" id="defs" name="Approval" namespace="urn:approval"><decision id="approval" name="Approval"><decisionTable id="table" hitPolicy="UNIQUE"><input id="amount"><inputExpression id="amountExpression" typeRef="number"><text>amount</text></inputExpression></input><output id="level" name="level" typeRef="string"/><rule id="automatic"><inputEntry id="automaticInput"><text>&lt;= 1000</text></inputEntry><outputEntry id="automaticOutput"><text>"Automatic"</text></outputEntry></rule><rule id="manager"><inputEntry id="managerInput"><text>&gt; 1000</text></inputEntry><outputEntry id="managerOutput"><text>"Manager"</text></outputEntry></rule></decisionTable></decision></definitions>`;
            const asset = await api('POST', '/decisions', { operationId: crypto.randomUUID(), name: 'Execution witness', xml });
            const version = await api('POST', `/decisions/${asset.id}/publish`, { operationId: crypto.randomUUID(), expectedRevision: asset.revision });
            report.asset = asset;
            report.version = version;
            const inputs = { amount: { kind: 'number', value: '9007199254740993' } };
            report.direct = await api('POST', `/decisions/${asset.id}/evaluate`, { versionId: version.id, inputs });
            expect(report.direct.status).toBe('Success');
            expect(report.direct.result).toEqual({ kind: 'string', value: 'Manager' });
            expect(report.direct.trace.filter(row => row.selected).map(row => row.ruleId)).toEqual(['manager']);
            const types = { decision: 'Elsa.Decisions.Workflows.Runtime.Activities.EvaluateDecision', sequence: 'Elsa.Activities.Sequence.Activities.Sequence', flowchart: 'Elsa.Activities.Flowchart.Activities.Flowchart', bpmn: 'Elsa.Activities.Bpmn.Activities.BpmnProcess', event: 'Elsa.Activities.Primitives.Activities.Event' };
            const ids = {};
            for (const [key, t] of Object.entries(types)) {
                const list = await api('GET', `/design/activities/definitions?search=${encodeURIComponent(t)}`);
                const a = list.items.find(x => x.definition.activityTypeKey === t);
                if (!a)
                    throw Error(`Missing catalog ${t}`);
                ids[key] = a.definition.headVersionId;
            }
            report.catalog = ids;
            const decision = () => node('decision', ids.decision, [literal('Decision', `${asset.id}@latest`), literal('Inputs', inputs, 'Object'), literal('OperationBudget', 1000000)]);
            async function workflow(name, root, held = false, workflowInputs = [], requestInputs = {}) {
                const definition = await api('POST', '/design/workflows/definitions/submit', {
                    name: `Decisions-${name}-${crypto.randomUUID()}`,
                    description: 'Owned shared-runtime acceptance',
                    state: { variables: [], inputs: workflowInputs, outputs: [], workflowActivityOptions: null,
                        strategyOptions: { persistenceMode: 'Durable' }, rootActivity: root }
                });
                const publication = await api('POST', `/publishing/workflows/${definition.version.id}/publish`, {});
                const run = await api('POST', `/runtime/workflows/executables/${publication.artifactId}/execute`, {
                    sourceReferenceId: publication.sourceReferenceId, inputs: requestInputs
                });
                const view = await wait(run.workflowExecutionId, held ? 'Suspended' : 'Completed');
                const row = { name, definition, publication, run, before: view };
                report.cases.push(row);
                save();
                if (!held) {
                    const value = result(view);
                    expect(value.contentHash).toBe(report.direct.contentHash);
                    expect(value.status).toBe('Success');
                    expect(value.result).toEqual(report.direct.result);
                    expect(value.trace).toEqual(report.direct.trace);
                    row.result = value;
                    save();
                }
                return row;
            }
            report.publicationRefusals = [];
            for (const selection of ["invalid-selection", `${asset.id}@missing-version`]) {
                const invalid = node('decision', ids.decision, [literal('Decision', selection), literal('Inputs', inputs, 'Object')]);
                const definition = await api('POST', '/design/workflows/definitions/submit', {
                    name: `Decision-refusal-${crypto.randomUUID()}`,
                    state: { variables: [], inputs: [], outputs: [], workflowActivityOptions: null, strategyOptions: null, rootActivity: invalid }
                });
                const denied = await c.post(`/publishing/workflows/${definition.version.id}/publish`, { data: {} });
                expect(denied.status()).toBe(400);
                report.publicationRefusals.push({ selection, status: denied.status(), problem: await denied.json() });
                save();
            }
            await workflow('native', node('root', ids.sequence, [], sequence([decision(), capture('root')])));
            await workflow('flowchart', node('root', ids.flowchart, [], {
                kind: 'elsa.flowchart.structure', schemaVersion: '1.0.0', payload: {
                    startNodeId: 'decision', activities: [decision(), capture('scope:root')],
                    connections: [{ id: 'decision-capture', source: { nodeId: 'decision', port: 'Done' }, target: { nodeId: 'capture' } }],
                    nodeMetadata: {}, connectionMetadata: {}
                }
            }));
            const dynamicDecision = decision();
            dynamicDecision.inputs = dynamicDecision.inputs.map(binding => binding.referenceKey === 'Inputs'
                ? literal('Inputs', { memberKey: 'decisionInputs' }, 'WorkflowRequest') : binding);
            await workflow('dynamic-input', node('root', ids.sequence, [], sequence([dynamicDecision, capture('root')])), false,
                [{ referenceKey: 'decisionInputs', name: 'decisionInputs', type: { alias: 'Object', collectionKind: 'single' },
                    storageDriverType: null, displayName: 'Decision inputs', category: null, isNullable: false }],
                { decisionInputs: inputs });

            const process = node('process', ids.bpmn, [], { kind: 'elsa.bpmn.structure', schemaVersion: '1.0.0', payload: { activities: [decision(), capture('process')], elements: [{ elementId: 'start', elementType: 'startEvent' }, { elementId: 'task', elementType: 'task', childNodeId: 'decision' }, { elementId: 'capture-task', elementType: 'task', childNodeId: 'capture' }, { elementId: 'end', elementType: 'endEvent' }], sequenceFlows: [{ flowId: 'f1', sourceRef: 'start', targetRef: 'task' }, { flowId: 'f2', sourceRef: 'task', targetRef: 'capture-task' }, { flowId: 'f3', sourceRef: 'capture-task', targetRef: 'end' }] } });
            await workflow('bpmn', process);
            report.event = `decision-resume-${crypto.randomUUID()}`;
            const held = node('wait', ids.event, [literal('EventName', report.event), literal('CanStartWorkflow', false)]);
            await workflow('restart', node('root', ids.sequence, [], sequence([held, decision(), capture('root')])), true);
            const latest = await api('GET', `/decisions/${asset.id}`);
            const changed = await api('PUT', `/decisions/${asset.id}`, { operationId: crypto.randomUUID(), expectedRevision: latest.revision, name: latest.name, xml: xml.replace('Manager', 'Changed policy') });
            report.newVersion = await api('POST', `/decisions/${asset.id}/publish`, { operationId: crypto.randomUUID(), expectedRevision: changed.revision });
            expect(report.newVersion.contentHash).not.toBe(version.contentHash);
            report.status = 'waiting-for-owned-restart';
            save();
        }
        else {
            const unavailable = await c.get('/decisions');
            expect(unavailable.status()).toBe(404);
            report.designApiStatus = unavailable.status();
            const row = report.cases.find(x => x.name === 'restart');
            const before = await wait(row.run.workflowExecutionId, 'Suspended');
            expect(before.activities.filter(x => x.executableNodeId === 'decision')).toHaveLength(0);
            report.resume = await api('POST', '/runtime/workflows/stimuli', { stimulusType: 'Event', stimulusHash: 'sha256:' + crypto.createHash('sha256').update(report.event).digest('hex'), mode: 'ResumeOnly', input: { resumed: true } });
            expect(report.resume.resumedCount).toBeGreaterThan(0);
            row.after = await wait(row.run.workflowExecutionId);
            row.result = result(row.after);
            expect(row.result.ContentHash ?? row.result.contentHash).toBe(report.direct.contentHash);
            expect(row.result.status).toBe('Success');
            expect(row.result.Result ?? row.result.result).toEqual(report.direct.result);
            expect(row.result.trace).toEqual(report.direct.trace);
            report.status = 'passed';
            save();
        }
        console.log(JSON.stringify({ status: report.status, out }));
    }
    catch (e) {
        report.error = String(e);
        report.status = 'failed';
        save();
        throw e;
    }
    finally {
        await c.dispose();
    }
})();
