import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";
import { randomBytes, randomUUID } from "node:crypto";
import { closeSync, mkdirSync, mkdtempSync, openSync, readFileSync, writeFileSync } from "node:fs";
import { createServer as httpServer, request as httpRequest } from "node:http";
import { createServer } from "node:net";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const repo = resolve(import.meta.dirname, "../../..");
const { request } = await import(pathToFileURL(join(repo, "Studio/node_modules/@playwright/test/index.mjs")));
const { ExternalAuthenticationClient, connectionChanges } = await import(pathToFileURL(join(repo,
  "Extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/wwwroot/studio/sdk/external-authentication/sdk.js")));
const sqlite = process.argv.includes("--sqlite"); const managed = !process.argv.includes("--baseline");
const root = mkdtempSync(join(tmpdir(), `elsa-3579-nodes-${sqlite ? "sqlite" : "postgres"}-${managed ? "managed" : "baseline"}-`));
const owner = randomUUID(); const marker = "elsa-external-authentication-probe/v1";
writeFileSync(join(root, ".elsa-3579-owned-probe"), marker);
console.log(`Owned separate-process qualification: ${root}`);
const dll = join(repo, "tools/identity/ExternalAuthentication.Probe/bin/Debug/net10.0/ExternalAuthentication.Probe.dll");
const children = []; const contexts = []; const targets = {}; const routing = { a: 0, b: 0 }; let active = "a";
const docker = (args, options = {}) => execFileSync("docker", args, { encoding: "utf8", timeout: 60_000, ...options })?.trim() ?? "";
let container; let dbUser; let serverVersion; const dumps = []; let result;
async function port() {
  const listener = createServer();
  await new Promise((done, reject) => { listener.once("error", reject); listener.listen(0, "127.0.0.1", done); });
  const value = listener.address().port; await new Promise(done => listener.close(done)); return value;
}
const proxy = httpServer((incoming, outgoing) => {
  const selected = incoming.headers["x-owned-node"] ?? active;
  if (!(selected in routing) || !targets[selected]) { outgoing.writeHead(503); outgoing.end(); return; }
  routing[selected]++;
  const target = new URL(incoming.url, targets[selected]); const headers = { ...incoming.headers, host: target.host };
  delete headers["x-owned-node"];
  const forwarded = httpRequest(target, { method: incoming.method, headers }, response => {
    outgoing.writeHead(response.statusCode, response.headers); response.pipe(outgoing);
  });
  forwarded.on("error", () => { if (!outgoing.headersSent) outgoing.writeHead(503); outgoing.end(); }); incoming.pipe(forwarded);
});
await new Promise((done, reject) => { proxy.once("error", reject); proxy.listen(0, "127.0.0.1", done); });
const base = `http://127.0.0.1:${proxy.address().port}/backend`;
const nodePorts = { a: await port(), b: await port() };
for (const name of ["a", "b"]) targets[name] = `http://127.0.0.1:${nodePorts[name]}`;
async function stop(child) {
  if (child.process.exitCode === null && child.process.signalCode === null) {
    const stopped = new Promise(done => child.process.once("exit", done)); child.process.kill("SIGTERM");
    await Promise.race([stopped, new Promise(done => setTimeout(done, 5000))]);
    if (child.process.exitCode === null && child.process.signalCode === null) { child.process.kill("SIGKILL"); await stopped; }
  }
  if (child.log !== null) { closeSync(child.log); child.log = null; }
}
async function launch(name, directory, args, metadataFile) {
  const instance = randomUUID(); const log = openSync(join(root, `${name}-${instance}.log`), "w", 0o600);
  const process = spawn("dotnet", [dll, "--owned-root", directory, "--instance", instance, ...args], { cwd: repo, stdio: ["ignore", log, log] });
  const child = { process, log, instance, name }; children.push(child);
  const deadline = Date.now() + 90_000;
  while (true) {
    try { const metadata = JSON.parse(readFileSync(metadataFile, "utf8")); if (metadata.Instance === instance) { child.metadata = metadata; return child; } } catch { /* partial/old metadata is not readiness */ }
    if (process.exitCode !== null || process.signalCode !== null || Date.now() >= deadline) throw new Error(`Owned ${name} did not become ready; inspect its retained log.`);
    await new Promise(done => setTimeout(done, 100));
  }
}
async function startNode(name) { return launch(name, root, ["--listen", targets[name]], join(root, "probe.json")); }
const options = node => ({ maxRedirects: 0, headers: { "X-Owned-Node": node } });
async function context(storageState) { const ctx = await request.newContext(storageState ? { storageState } : {}); contexts.push(ctx); return ctx; }
async function json(ctx, path, node = active) {
  const response = await ctx.get(base + path, options(node)); assert.equal(response.status(), 200, `GET ${path.split("?")[0]}`); return response.json();
}
async function csrf(ctx, node) { return json(ctx, "/identity/external/antiforgery", node); }
async function loginAdministrator(ctx, node) {
  const token = await csrf(ctx, node);
  const response = await ctx.post(base + "/_elsa/identity/login", { ...options(node),
    form: { username: "admin", password: "Password123!", returnUrl: "/backend/resource", [token.formFieldName]: token.requestToken } });
  assert.equal(response.status(), 302, "ordinary local login"); assert.equal((await json(ctx, "/_elsa/identity/session", node)).status, "authenticated");
}
async function challenge(ctx, key, node) {
  const token = await csrf(ctx, node);
  const started = await ctx.post(base + "/identity/external/signin/" + key, { ...options(node), form: { returnUrl: "/backend/resource", [token.formFieldName]: token.requestToken } });
  assert.equal(started.status(), 302, "normal external challenge");
  const authorized = await ctx.get(started.headers().location, { maxRedirects: 0 }); assert.equal(authorized.status(), 302, "actual provider authorization");
  const callback = authorized.headers().location; assert.equal(new URL(callback).origin, new URL(base).origin); return callback;
}
async function complete(ctx, callback, node) {
  const response = await ctx.get(callback, options(node)); assert.equal(response.status(), 302, "one normal completion");
  assert.equal(new URL(response.headers().location, base).pathname, "/backend/resource");
  const session = await json(ctx, "/_elsa/identity/session", node); assert.equal(session.status, "authenticated"); assert.deepEqual(session.roles, ["reader"]); return session;
}
function client(ctx, node) {
  async function send(path, init = {}, body) {
    const extra = { ...options(node), headers: { ...options(node).headers, ...init.headers } };
    const response = body === undefined ? await ctx.get(base + path, extra) : await ctx.post(base + path, { ...extra, data: body });
    const payload = await response.json(); if (!response.ok()) throw { status: response.status(), payload }; return payload;
  }
  return new ExternalAuthenticationClient({ getJson: (path, init) => send(path, init), postJson: (path, body, init) => send(path, init, body) }, base);
}
async function outcome(operation) { try { return { ok: true, value: await operation() }; } catch (error) { return { ok: false, status: error.status, failure: error.payload?.error ?? error.payload?.failure }; } }
async function subject(providers, provider, value, controlKey) {
  const response = await fetch(providers.ControlUrl + "/__probe/subject", { method: "POST", headers: { "Content-Type": "application/json", "X-Owned-Probe-Control": controlKey }, body: JSON.stringify({ provider, subject: value }) });
  assert.equal(response.status, 200, "owned upstream account selection");
}
try {
  const settings = { databaseProvider: sqlite ? "Sqlite" : "PostgreSql", databaseManagement: managed, provisioning: true, publicBaseUrl: base };
  if (!sqlite) {
    const image = "postgres@sha256:0cefaabd882e42bcb2a2f3dc56d3ce4770560ba62fd7f197f1a2e008fd5731ab";
    docker(["image", "inspect", image], { stdio: "ignore" });
    dbUser = "elsa3579_" + owner.replaceAll("-", ""); const password = randomBytes(32).toString("base64url");
    const envFile = join(root, "postgres.env"); writeFileSync(envFile, `POSTGRES_USER=${dbUser}\nPOSTGRES_PASSWORD=${password}\nPOSTGRES_DB=elsa3579_iam\n`, { mode: 0o600 });
    container = docker(["run", "--detach", "--name", `elsa-3579-postgres-${owner}`, "--label", `elsa.qualification.owner=${owner}`,
      "--cpus", "1", "--memory", "768m", "--publish", "127.0.0.1::5432", "--env-file", envFile, image]);
    const deadline = Date.now() + 60_000;
    while (true) {
      try { docker(["exec", container, "pg_isready", "-h", "127.0.0.1", "-U", dbUser, "-d", "elsa3579_iam"], { stdio: "ignore" }); break; } catch { /* owned server startup */ }
      if (Date.now() >= deadline) throw new Error("Owned PostgreSQL did not become ready."); await new Promise(done => setTimeout(done, 250));
    }
    docker(["exec", container, "psql", "-U", dbUser, "-d", "elsa3579_iam", "-c", "CREATE DATABASE elsa3579_tokens;"]);
    serverVersion = docker(["exec", container, "psql", "-At", "-U", dbUser, "-d", "elsa3579_iam", "-c", "SHOW server_version;"]);
    assert.match(serverVersion, /^16\.15(?:\s|$)/);
    const bindings = JSON.parse(docker(["inspect", "--format", "{{json .NetworkSettings.Ports}}", container]));
    for (const [database, file] of [["elsa3579_iam", "iam.connection"], ["elsa3579_tokens", "tokens.connection"]])
      writeFileSync(join(root, file), `Host=127.0.0.1;Port=${bindings["5432/tcp"][0].HostPort};Database=${database};Username=${dbUser};Password=${password};Pooling=False;Include Error Detail=False`, { mode: 0o600 });
    settings.connectionStringFile = "iam.connection"; settings.tokenConnectionStringFile = "tokens.connection";
  }
  const providerRoot = join(root, "providers"); mkdirSync(providerRoot); writeFileSync(join(providerRoot, ".elsa-3579-owned-probe"), marker);
  const controlKey = randomBytes(32).toString("base64url"); writeFileSync(join(providerRoot, "control-key.txt"), controlKey, { mode: 0o600 });
  const keeper = await launch("providers", providerRoot, ["--providers-only", "--public-backend", base], join(providerRoot, "providers.json"));
  Object.assign(settings, { companyIssuer: keeper.metadata.CompanyIssuer, partnerIssuer: keeper.metadata.PartnerIssuer });
  writeFileSync(join(root, "probe-settings.json"), JSON.stringify(settings), { mode: 0o600 });
  let a = await startNode("a");
  const admin = await context(); await loginAdministrator(admin, "a"); const adminId = (await json(admin, "/_elsa/identity/session", "a")).subject;
  const company = await context(); const pending = await challenge(company, "company", "a");
  await stop(a); let b = await startNode("b");
  const companySession = await complete(company, pending, "b"); assert.notEqual(companySession.subject, adminId);
  assert.equal((await company.get(pending, options("b"))).status(), 400, "restart replay is refused");
  a = await startNode("a");
  const sameProcessRestart = await context(); const pendingRestart = await challenge(sameProcessRestart, "company", "a");
  await stop(a); a = await startNode("a"); await complete(sameProcessRestart, pendingRestart, "a");
  const partner = await context(); const partnerSession = await complete(partner, await challenge(partner, "partner", "a"), "b");
  assert.notEqual(partnerSession.subject, companySession.subject, "equal subjects in separate issuers remain separate users");
  const companyPair = await json(company, "/_elsa/identity/token", "b"); const partnerPair = await json(partner, "/_elsa/identity/token", "a");
  for (const name of ["a", "b"]) {
    assert.equal((await json(company, "/_elsa/identity/session", name)).subject, companySession.subject);
    assert.equal((await company.get(base + "/resource", { ...options(name), headers: { ...options(name).headers, Authorization: `Bearer ${companyPair.accessToken}` } })).status(), 200);
  }
  const apiA = client(admin, "a"); const apiB = client(admin, "b");
  const id = JSON.parse(Buffer.from(companyPair.accessToken.split(".")[1], "base64url").toString())["elsa.identity.session_id"];
  const selectedSession = (await apiA.sessions(companySession.subject)).items.find(item => item.id === id); assert.ok(selectedSession);
  const revokedCookieState = await company.storageState();
  assert.equal((await apiA.revokeSession(companySession.subject, selectedSession)).revoked, true);
  for (const name of ["a", "b"]) {
    const cookieProbe = await context(revokedCookieState);
    assert.equal((await json(cookieProbe, "/_elsa/identity/session", name)).status, "anonymous");
    assert.equal((await company.get(base + "/resource", { ...options(name), headers: { ...options(name).headers, Authorization: `Bearer ${companyPair.accessToken}` } })).status(), 401);
    assert.equal((await company.post(base + "/_elsa/identity/refresh", { ...options(name), data: { refreshToken: companyPair.refreshToken } })).status(), 401);
    assert.equal((await json(partner, "/_elsa/identity/session", name)).subject, partnerSession.subject);
    assert.equal((await partner.get(base + "/resource", { ...options(name), headers: { ...options(name).headers, Authorization: `Bearer ${partnerPair.accessToken}` } })).status(), 200);
  }
  const siblingRefresh = await partner.post(base + "/_elsa/identity/refresh", { ...options("b"), data: { refreshToken: partnerPair.refreshToken } });
  assert.equal(siblingRefresh.status(), 200, "unrelated refresh family survives on the other node");
  await subject(keeper.metadata, "company", "simultaneous-subject", controlKey);
  const concurrent = await context(); const concurrentCallback = await challenge(concurrent, "company", "a");
  const beforeConcurrent = await json(admin, "/__probe/state", "a");
  const completions = await Promise.all([concurrent.get(concurrentCallback, options("a")), concurrent.get(concurrentCallback, options("b"))]);
  assert.deepEqual(completions.map(value => value.status()).sort(), [302, 400]);
  const afterConcurrent = await json(admin, "/__probe/state", "b");
  for (const field of ["users", "links", "sessions"]) assert.equal(afterConcurrent[field], beforeConcurrent[field] + 1, "single concurrent completion: " + field);
  assert.equal(afterConcurrent.tokens, beforeConcurrent.tokens);
  await subject(keeper.metadata, "company", "linked-recovery-subject", controlKey);
  await subject(keeper.metadata, "partner", "linked-recovery-subject", controlKey);
  const linkedCompany = await context(); const linkedSession = await complete(linkedCompany, await challenge(linkedCompany, "company", "a"), "b");
  const beforeAttach = await apiA.links(linkedSession.subject);
  const choice = beforeAttach.attachChoices.find(item => item.key === "partner"); assert.ok(choice);
  assert.equal((await apiA.attach(linkedSession.subject, { connectionRecordId: choice.id, subject: "linked-recovery-subject",
    expectedUserRevision: beforeAttach.userRevision, expectedCatalogRevision: beforeAttach.catalogRevision })).attached, true);
  const linkedPartner = await context(); assert.equal((await complete(linkedPartner, await challenge(linkedPartner, "partner", "b"), "a")).subject, linkedSession.subject);
  const linkedCredentials = [];
  for (const [provider, ctx] of [["company", linkedCompany], ["partner", linkedPartner]])
    linkedCredentials.push({ provider, storage: await ctx.storageState(), pair: await json(ctx, "/_elsa/identity/token", "a") });
  const links = await apiA.links(linkedSession.subject); assert.equal(links.items.length, 2); assert.ok(links.items.every(item => item.canRemove));
  const removals = await Promise.all(links.items.map((link, index) => outcome(() => (index ? apiB : apiA).removeLink(linkedSession.subject, {
    provider: link.provider, issuer: link.issuer, subject: link.subject, expectedLinkRevision: link.revision,
    expectedUserRevision: links.userRevision, expectedCatalogRevision: links.catalogRevision }))));
  assert.equal(removals.filter(item => item.ok && item.value.removed).length, 1, "two nodes cannot remove both final user paths");
  const remaining = await apiB.links(linkedSession.subject); assert.equal(remaining.items.length, 1);
  const finalLink = remaining.items[0]; const refused = await outcome(() => apiB.removeLink(linkedSession.subject, {
    provider: finalLink.provider, issuer: finalLink.issuer, subject: finalLink.subject, expectedLinkRevision: finalLink.revision,
    expectedUserRevision: remaining.userRevision, expectedCatalogRevision: remaining.catalogRevision }));
  assert.equal(refused.ok && refused.value.removed, false); assert.equal(refused.failure ?? refused.value?.failure, "final-login-path");
  for (const credential of linkedCredentials) for (const name of ["a", "b"]) {
    const probe = await context(credential.storage); const retained = credential.provider === finalLink.provider;
    assert.equal((await json(probe, "/_elsa/identity/session", name)).status, retained ? "authenticated" : "anonymous", "exact-link cookie validation on " + name);
    assert.equal((await probe.get(base + "/resource", { ...options(name), headers: { ...options(name).headers, Authorization: `Bearer ${credential.pair.accessToken}` } })).status(), retained ? 200 : 401);
  }
  const catalog = await apiA.list(); assert.equal(catalog.databaseManagement, managed); assert.equal(catalog.canManageConnections, true);
  let catalogRace = null;
  if (managed) {
    const original = catalog.connections.find(item => item.document.key === "company").document;
    const changes = await Promise.all([apiA, apiB].map((api, index) => outcome(() => api.apply({ change: connectionChanges.create,
      expectedRevision: catalog.revision, record: { ...original, id: "", key: "race-" + index, displayName: "Owned race " + index, enabled: false, admission: { type: "reject", defaultRoleIds: [] } } }))));
    assert.equal(changes.filter(item => item.ok && item.value.saved).length, 1, "catalog CAS across processes"); catalogRace = true;
    await subject(keeper.metadata, "company", "pending-source-change-subject", controlKey);
    const pendingSource = await challenge(linkedCompany, "company", "a"); const beforeSource = await json(admin, "/__probe/state", "a");
    const current = await apiB.list();
    const override = await apiB.apply({ change: connectionChanges.createOverride, expectedRevision: current.revision, record: { ...original, id: "", enabled: false, overridesDeployment: true } });
    assert.equal(override.saved, true); assert.equal((await linkedCompany.get(pendingSource, options("b"))).status(), 400, "source change invalidates pending state on another process");
    const afterSource = await json(admin, "/__probe/state", "b"); for (const field of ["users", "links", "sessions", "tokens"]) assert.equal(afterSource[field], beforeSource[field]);
    for (const change of [connectionChanges.archive, connectionChanges.restore, connectionChanges.remove]) {
      const state = await apiA.list(); const view = state.connections.find(item => item.document.id === override.recordId); assert.ok(view);
      assert.equal((await apiA.apply({ change, expectedRevision: state.revision, record: view.document })).saved, true);
      const observed = await apiB.list(); const baseline = observed.connections.find(item => item.source === 0 && item.document.key === "company");
      assert.equal(baseline.effective, change !== connectionChanges.restore, "source lifecycle observed by the other node");
      if (baseline.effective) assert.equal((await linkedCompany.get(pendingSource, options("a"))).status(), 400, "restoring source material cannot revive rejected state");
    }
  } else {
    const deployed = catalog.connections.find(item => item.document.key === "company").document;
    const deniedWrite = await outcome(() => apiA.apply({ change: connectionChanges.create, expectedRevision: catalog.revision,
      record: { ...deployed, id: "", key: "refused-baseline-write", enabled: false } }));
    assert.equal(deniedWrite.ok && deniedWrite.value.saved, false);
    assert.equal(deniedWrite.failure ?? deniedWrite.value?.failure, "database-management-disabled");
    await subject(keeper.metadata, "company", "pending-source-change-subject", controlKey);
    const pendingSource = await challenge(linkedCompany, "company", "a"); const beforeSource = await json(admin, "/__probe/state", "a");
    writeFileSync(join(root, "probe-settings.json"), JSON.stringify({ ...settings, provisioning: false }), { mode: 0o600 });
    await stop(b); b = await startNode("b");
    assert.equal((await linkedCompany.get(pendingSource, options("b"))).status(), 400, "changed deployment material invalidates pending state");
    const afterSource = await json(admin, "/__probe/state", "b"); for (const field of ["users", "links", "sessions", "tokens"]) assert.equal(afterSource[field], beforeSource[field]);
    writeFileSync(join(root, "probe-settings.json"), JSON.stringify(settings), { mode: 0o600 });
    await stop(b); b = await startNode("b");
    assert.equal((await linkedCompany.get(pendingSource, options("b"))).status(), 400, "deployment rollback cannot revive rejected state");
  }
  result = { passed: true, ownedRoot: root, store: sqlite ? "native SQLite, same machine" : "PostgreSQL " + serverVersion,
    mode: managed ? "database-managed" : "deployment-only without provider-configuration store", driver: "actual Kestrel/OIDC HTTP with isolated cookie jars",
    restartCompletion: true, sameNodeRestartCompletion: true, crossNodeCompletion: true, replayRefused: true, equalSubjectIsolation: true,
    sharedCookieAndBearerValidation: true, selectedRevocationAcrossNodes: true, concurrentCallbackSingleCommit: true,
    finalUserLinkRace: true, catalogRace, sourceChanges: true, catalogLifecycle: managed ? true : null, nodeRequests: routing,
    processesStarted: children.length, limitations: ["HTTP process/store qualification; browser/product/Studio profiles are recorded separately"] };
} finally {
  for (const ctx of contexts) await ctx.dispose();
  await new Promise(done => proxy.close(done));
  for (const child of children.reverse()) await stop(child);
  if (container) {
    if (docker(["inspect", "--format", '{{index .Config.Labels "elsa.qualification.owner"}}', container]) !== owner) throw new Error("Refusing to stop an unowned database container.");
    try {
      for (const database of ["elsa3579_iam", "elsa3579_tokens"]) {
        const destination = join(root, database + ".dump"); const descriptor = openSync(destination, "w", 0o600);
        try { execFileSync("docker", ["exec", container, "pg_dump", "-Fc", "-U", dbUser, "-d", database], { stdio: ["ignore", descriptor, "pipe"], timeout: 60_000 }); dumps.push(database); }
        finally { closeSync(descriptor); }
      }
    } finally { docker(["stop", "--time", "10", container]); docker(["rm", "--volumes", container]); }
  }
}
result.frozenDatabaseDumps = dumps; writeFileSync(join(root, "node-receipt.json"), JSON.stringify(result, null, 2)); console.log(JSON.stringify(result));
