'use strict';
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto'), assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process'), { createRequire } = require('node:module');
const repo = path.resolve(process.env.DECISIONS_REPOSITORY || path.join(__dirname, '../..'));
const out = process.env.DECISIONS_PERMISSION_OUT, ownedRoot = process.env.DECISIONS_OWNED_STORE_ROOT, database = process.env.DECISIONS_IAM_DATABASE;
const base = process.env.ELSA_WORKBENCH_URL, phase = process.env.DECISIONS_PERMISSION_PHASE || 'prepare';
const templates = process.env.DECISIONS_QUALIFICATION_TEMPLATES || path.join(repo, 'specs/1195-business-decisions/evidence/identity-qualification');
if (process.env.DECISIONS_OWNED_HOSTS !== '1' || !out || !ownedRoot || !database || !base || !process.env.DECISIONS_HOST_DIRECTORY) throw Error('Explicit owned host, store and output paths required');
if (!['localhost', '127.0.0.1', '[::1]'].includes(new URL(base).hostname)) throw Error('Owned loopback host required');
const relative = path.relative(fs.realpathSync(ownedRoot), fs.realpathSync(database));
if (relative.startsWith('..') || path.isAbsolute(relative)) throw Error('Only the existing owned host store is permitted');
const { request } = createRequire(path.join(repo, 'Studio/package.json'))('@playwright/test');
fs.mkdirSync(out, { recursive: true });
const privatePath = path.join(out, 'actors.private.json'), reportPath = path.join(out, 'permissions.json');
let state = fs.existsSync(privatePath) ? JSON.parse(fs.readFileSync(privatePath)) : null;
let report = fs.existsSync(reportPath) ? JSON.parse(fs.readFileSync(reportPath)) : { status: 'running', sourceSha: process.env.DECISIONS_SOURCE_SHA, cases: [], limitations: [] };
const save = () => fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
function privateSave() { fs.writeFileSync(privatePath, JSON.stringify(state, null, 2), { mode: 0o600 }); }
function provision(command, actors) {
  const project = path.join(out, 'provisioner'); fs.mkdirSync(project, { recursive: true });
  fs.writeFileSync(path.join(project, 'Program.cs'), fs.readFileSync(path.join(templates, 'Program.cs.template')));
  fs.writeFileSync(path.join(project, 'Provisioner.csproj'), fs.readFileSync(path.join(templates, 'Provisioner.csproj.template'), 'utf8').replaceAll('@@HOST@@', path.resolve(process.env.DECISIONS_HOST_DIRECTORY)).replaceAll('@@NATIVE_RID@@', `${({ darwin: 'osx', linux: 'linux', win32: 'win' })[process.platform]}-${process.arch}`));
  const lock = path.join(templates, 'packages.lock.json.template');
  if (fs.existsSync(lock)) fs.copyFileSync(lock, path.join(project, 'packages.lock.json'));
  const build = spawnSync('dotnet', ['build', path.join(project, 'Provisioner.csproj'), '-p:BuildProjectReferences=false', ...(fs.existsSync(lock) ? ['-p:RestoreLockedMode=true'] : []), '-m:1', '/nr:false', '-v:q'], { cwd: repo, env: process.env, encoding: 'utf8' });
  fs.writeFileSync(path.join(out, 'provisioner-build.log'), build.stdout + build.stderr);
  if (build.status !== 0) throw Error('Private IAM provisioner build failed; inspect provisioner-build.log');
  const input = path.join(out, 'provision-request.private.json');
  fs.writeFileSync(input, JSON.stringify({ command, runId: state.runId, storeRoot: ownedRoot, databasePath: database, actors }), { mode: 0o600 });
  const result = spawnSync('dotnet', [path.join(project, 'bin/Debug/net10.0/Provisioner.dll'), input], { cwd: repo, env: process.env, encoding: 'utf8' });
  fs.unlinkSync(input);
  if (result.status !== 0) { fs.writeFileSync(path.join(out, 'provisioner-failure.log'), result.stderr); throw Error('Normal provider IAM operation failed; inspect provisioner-failure.log'); }
  return JSON.parse(result.stdout.trim());
}
const wfGrants = ['workflows/runtime:read', 'workflows/activity-executions:inspect', 'workflows/activity-executions:inspect-values', 'workflows/activity-executions:resolve-value-payloads', 'workflows/publishing:read', 'bpmn:read'];
const domainGrants = ['decisions:read', 'decisions:manage', 'decisions:publish', 'decisions:evaluate', 'decisions:test', 'decisions:history', 'decisions:source', 'decisions:sensitive-values'];
async function raw(c, method, url, data) { const response = await c.fetch(url, { method, maxRedirects: 0, ...(data === undefined ? {} : { data }) }); const text = await response.text(); let body; try { body = text ? JSON.parse(text) : null; } catch { body = text; } return { status: response.status(), body, text, location: response.headers().location || null }; }
async function api(c, method, url, data) { const r = await raw(c, method, url, data); assert.ok(r.status >= 200 && r.status < 300, `${method} ${url} status ${r.status}`); return r.body; }
async function status(c, label, method, url, expected, data) { const r = await raw(c, method, url, data); report.cases.push({ label, expectedStatus: expected, actualStatus: r.status, location: r.location }); save(); assert.equal(r.status, expected, label); return r; }
async function actorContext(actor) { const c = await request.newContext({ baseURL: base }); const session = await api(c, 'POST', '/_elsa/identity/login', { username: actor.userName, password: actor.password, tenantId: actor.tenantId }); assert.equal(session.tenantId, actor.tenantId); assert.ok(session.permissions.every(grant => !grant.includes('*')), 'No wildcard actor grants'); assert.deepEqual([...session.permissions].sort(), [...actor.grants].sort(), 'Cookie session contains exactly the provisioned role grants'); return c; }
const literal = (referenceKey, value, expressionType = 'Literal') => ({ referenceKey, value: { value, expressionType }, autoEvaluate: null, evaluatorType: null, storageDriverType: null, isSensitive: null });
const node = (nodeId, activityVersionId, inputs = [], structure) => ({ nodeId, activityVersionId, inputs, outputs: [], ...(structure ? { structure } : {}) });
async function wait(admin, id, suspended = false) { for (let n = 0; n < 180; n++) { const v = await api(admin, 'GET', `/runtime/workflows/instances/${id}`); if (suspended && v.activities.some(activity => activity.status === 'Suspended')) return v; if (['Completed', 'Faulted', 'Cancelled', 'Finished'].includes(v.instance.status)) return v; await new Promise(resolve => setTimeout(resolve, 500)); } throw Error('Owned execution did not settle'); }
(async () => {
 const contexts = [];
 try {
  if (phase === 'provision') {
   assert.equal(state, null, 'Use a fresh qualification output');
   state = { runId: 'decisions-iam-' + crypto.randomUUID(), actors: [] };
   const specs = { workflow: wfGrants, reader: [...wfGrants, 'decisions:read'], source: [...wfGrants, 'decisions:read', 'decisions:source'], evaluator: ['decisions:evaluate'], history: ['decisions:read', 'decisions:history'], foreign: domainGrants, revocable: [...wfGrants, 'workflows/runtime:execute', 'decisions:read', 'decisions:evaluate'] };
   for (const [key, grants] of Object.entries(specs)) state.actors.push({ key, tenantId: key === 'foreign' ? state.runId : 'default', userName: `${state.runId}-${key}`, password: 'Aa1!' + crypto.randomBytes(18).toString('base64url'), grants });
   privateSave(); const created = provision('provision', state.actors);
   for (const actor of state.actors) Object.assign(actor, created.find(item => item.key === actor.key));
   privateSave(); report.actors = created; report.status = 'provisioned'; save();
  } else if (phase === 'revoke') {
   const actor = state.actors.find(item => item.key === 'revocable');
   report.revocation = provision('revoke-evaluate', [actor]); actor.grants = report.revocation[0].grants; privateSave(); report.status = 'revoked'; save();
  } else {
   assert.ok(state, 'Provision actors first');
   const admin = await request.newContext({ baseURL: base }); contexts.push(admin);
   await api(admin, 'POST', '/_elsa/identity/login', { username: process.env.DECISIONS_ADMIN_USER || 'admin', password: process.env.DECISIONS_ADMIN_PASSWORD || 'Password123!' });
   const actors = {}; for (const actor of state.actors) { actors[actor.key] = await actorContext(actor); contexts.push(actors[actor.key]); }
   const witness = JSON.parse(fs.readFileSync(path.join(process.env.DECISIONS_EXECUTION_OUT, 'execution.json')));
   if (phase === 'prepare') {
    const id = witness.asset.id, version = witness.version.id, inputs = { amount: { kind: 'number', value: '9007199254740993' } };
    const anonymous = await request.newContext({ baseURL: base }); contexts.push(anonymous);
    await status(anonymous, 'Anonymous Decisions inventory is401 without login redirect', 'GET', '/decisions', 401);
    await status(anonymous, 'Anonymous Decisions evaluation is401 without login redirect', 'POST', `/decisions/${id}/evaluate`, 401, { versionId: version, inputs });
    const read = await status(actors.reader, 'Read grants metadata', 'GET', `/decisions/${id}`, 200); assert.equal(read.body.xml, null);
    await status(actors.reader, 'Read does not grant Evaluate', 'POST', `/decisions/${id}/evaluate`, 403, { versionId: version, inputs });
    await status(actors.reader, 'Read does not grant Source', 'GET', `/decisions/${id}/export`, 403);
    await status(actors.reader, 'Read does not grant History', 'GET', `/decisions/${id}/runs`, 403);
    const evaluated = await status(actors.evaluator, 'Evaluate does not need Read or Source', 'POST', `/decisions/${id}/evaluate`, 200, { versionId: version, inputs }); assert.equal(evaluated.body.contentHash, witness.direct.contentHash);
    await status(actors.evaluator, 'Evaluate does not grant Read', 'GET', `/decisions/${id}`, 403);
    await status(actors.evaluator, 'Evaluate does not grant Source', 'GET', `/decisions/${id}/export`, 403);
    await status(actors.evaluator, 'Evaluate does not grant History', 'GET', `/decisions/${id}/runs`, 403);
    const exactSource = await status(actors.source, 'Source with Read exports exact published XML', 'GET', `/decisions/${id}/export?versionId=${version}`, 200); assert.equal(exactSource.text, witness.asset.xml);
    await status(actors.source, 'Source does not grant Evaluate', 'POST', `/decisions/${id}/evaluate`, 403, { versionId: version, inputs });
    await status(actors.source, 'Source does not grant History', 'GET', `/decisions/${id}/runs`, 403);
    const savedCase = await api(admin, 'POST', `/decisions/${id}/cases`, { operationId: crypto.randomUUID(), name: state.runId, inputs, expectedStatus: 'Success', expectedResult: witness.direct.result });
    const receipt = await api(admin, 'POST', `/decisions/${id}/test`, { operationId: crypto.randomUUID(), candidateHash: witness.version.sourceHash, versionId: version, cases: [{ id: savedCase.id, revision: savedCase.revision }] }); assert.equal(receipt.passed, true);
    const history = await status(actors.history, 'History with Read retrieves completed summary receipt', 'GET', `/decisions/${id}/runs/${receipt.id}`, 200); assert.equal(history.body.hasDetailedPayload, false); assert.ok(history.body.comparisons.every(item => item.actual == null && item.expected == null));
    await status(actors.history, 'History with Read lists receipts', 'GET', `/decisions/${id}/runs`, 200);
    await status(actors.history, 'History does not grant Source', 'GET', `/decisions/${id}/export`, 403);
    await status(actors.foreign, 'Real foreign tenant cannot read asset', 'GET', `/decisions/${id}`, 404);
    await status(actors.foreign, 'Real foreign tenant cannot evaluate version', 'POST', `/decisions/${id}/evaluate`, 404, { versionId: version, inputs });
    const inventory = await status(actors.foreign, 'Real foreign tenant inventory is empty', 'GET', '/decisions', 200); assert.deepEqual(inventory.body.items, []);
    const protectedAsset = await api(admin, 'POST', '/decisions', { operationId: crypto.randomUUID(), name: state.runId + '-protected', xml: witness.asset.xml, isSensitive: true });
    const protectedVersion = await api(admin, 'POST', `/decisions/${protectedAsset.id}/publish`, { operationId: crypto.randomUUID(), expectedRevision: protectedAsset.revision });
    await status(actors.source, 'Source without SensitiveValues cannot export protected literals', 'GET', `/decisions/${protectedAsset.id}/export`, 403);
    await status(actors.evaluator, 'Evaluate without SensitiveValues cannot disclose protected output', 'POST', `/decisions/${protectedAsset.id}/evaluate`, 403, { versionId: protectedVersion.id, inputs });
    for (const row of witness.cases.filter(item => ['native', 'flowchart', 'bpmn'].includes(item.name))) {
     const url = `/runtime/workflows/instances/${row.run.workflowExecutionId}`;
     const denied = await status(actors.workflow, `${row.name} workflow-only value read is redacted`, 'GET', url, 200); assert.equal(denied.body.outputs.decisionResult.isRedacted, true); assert.ok(denied.body.outputs.decisionResult.value == null);
     const allowed = await status(actors.reader, `${row.name} domain Read discloses output`, 'GET', url, 200); assert.equal(allowed.body.outputs.decisionResult.isRedacted, false); assert.ok(allowed.body.outputs.decisionResult.value);
     const decisionActivity = allowed.body.activities.find(activity => activity.executableNodeId === 'decision'); assert.ok(decisionActivity);
     const activityUrl = `${url}/activity-executions/${decisionActivity.activityExecutionId}`;
     const inspected = await status(actors.reader, `${row.name} decision activity structure readable`, 'GET', activityUrl, 200);
     const evidence = inspected.body.valueSnapshots.find(item => item.accessState === 'resolutionAvailable' && /result/i.test(item.subject + item.name));
     if (evidence) {
      const payloadUrl = `${activityUrl}/value-evidence/${encodeURIComponent(evidence.evidenceId)}/payload`;
      await status(actors.workflow, `${row.name} workflow-only decision evidence payload denied`, 'GET', payloadUrl, 403);
      await status(actors.reader, `${row.name} domain Read decision evidence payload readable`, 'GET', payloadUrl, 200);
     } else report.limitations.push(`${row.name} decision activity result has no captured resolvable evidence; workflow output disclosure is covered.`);
     await status(actors.workflow, `${row.name} workflow-only source export denied`, 'GET', `/publishing/workflows/${row.definition.version.id}/executable-export`, 403);
     const exported = await status(actors.source, `${row.name} domain Source permits exact closure export`, 'GET', `/publishing/workflows/${row.definition.version.id}/executable-export`, 200); assert.ok(exported.body.artifacts); assert.ok(JSON.stringify(exported.body).includes(witness.direct.contentHash), 'Export contains the frozen decision content identity');
    }
    const model = process.env.DECISIONS_BPMN_EXECUTION_ID;
    if (model) { await status(actors.workflow, 'Hosted BPMN workflow-only read denied', 'GET', `/bpmn/executions/${model}/inspection`, 403); await status(actors.reader, 'Hosted BPMN domain Read permitted', 'GET', `/bpmn/executions/${model}/inspection`, 200); }
    else report.limitations.push('No hosted-model BPMN execution supplied; native BPMN activity disclosure is covered, hosted-model HTTP not claimed.');
    report.limitations.push('External provider identity unsupported handling remains covered by integration tests; no external provider is configured in this owned host.');
    state.event = state.runId + '-resume'; const catalog = witness.catalog;
    const capture = { nodeId: 'capture', activityVersionId: 'elsa.intrinsic.set-output@1', outputs: [], intrinsic: { kind: 'setOutput', valueType: { alias: 'Elsa.Any', collectionKind: 'single' } }, inputs: [literal('name', 'decisionResult'), literal('value', { producerNodeId: 'decision', producerScopeId: 'root', projectionKey: '$result' }, 'ActivityResult')] };
    const activities = [node('wait', catalog.event, [literal('EventName', state.event), literal('CanStartWorkflow', false)]), node('decision', catalog.decision, [literal('Decision', `${id}@${version}`), literal('Inputs', inputs, 'Object'), literal('OperationBudget', 1000000)]), capture];
    const draft = await api(admin, 'POST', '/design/workflows/definitions/submit', { name: state.runId + '-revocation', state: { variables: [], inputs: [], outputs: [], workflowActivityOptions: null, strategyOptions: { persistenceMode: 'Durable' }, rootActivity: node('root', catalog.sequence, [], { kind: 'elsa.sequence.structure', schemaVersion: '1.0.0', payload: { activities } }) } });
    const published = await api(admin, 'POST', `/publishing/workflows/${draft.version.id}/publish`, {});
    state.revocationRun = await api(actors.revocable, 'POST', `/runtime/workflows/executables/${published.artifactId}/execute`, { sourceReferenceId: published.sourceReferenceId });
    const waiting = await wait(admin, state.revocationRun.workflowExecutionId, true); assert.ok(waiting.activities.some(activity => activity.status === 'Suspended')); assert.ok(!waiting.activities.some(activity => activity.executableNodeId === 'decision')); privateSave();
    report.revocationRun = state.revocationRun; report.status = 'waiting-for-revocation'; save();
   } else if (phase === 'verify') {
    assert.equal(report.status, 'revoked');
    await api(admin, 'POST', '/runtime/workflows/stimuli', { stimulusType: 'Event', stimulusHash: 'sha256:' + crypto.createHash('sha256').update(state.event).digest('hex'), mode: 'ResumeOnly', input: {} });
    const final = await wait(admin, state.revocationRun.workflowExecutionId);
    assert.equal(final.instance.status, 'Faulted', 'Revoked persisted user Evaluate must refuse resumed execution');
    assert.ok(final.incidents.some(incident => JSON.stringify(incident).includes('The execution subject cannot evaluate this decision.')), 'Fault must identify the current-authority decision refusal');
    assert.ok(!final.outputs.decisionResult || final.outputs.decisionResult.isRedacted || final.outputs.decisionResult.value === null, 'No revoked decision result');
    report.cases.push({ label: 'Current persisted Evaluate revocation refuses resumed run', expectedLifecycle: 'Faulted', actualLifecycle: final.instance.status });
    report.status = 'passed'; save();
   } else throw Error('Unknown permission qualification phase');
  }
  console.log(JSON.stringify({ status: report.status, phase, out }));
 } catch (error) { report.status = 'failed'; report.error = String(error); save(); throw error; }
 finally { await Promise.all(contexts.map(context => context.dispose())); }
})();
