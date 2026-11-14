import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { URL, fileURLToPath } from "node:url";
import process from "node:process";

const require = createRequire(new URL("../../Studio/package.json", import.meta.url));
const ts = require("typescript");
const sourcePath = process.env.CLEANUP_RECEIPT_SOURCE ?? fileURLToPath(new URL("../../Studio/tests/acceptance/journeys.spec.ts", import.meta.url));
const toastControls = {};
const toastSource = readFileSync(new URL("../../Studio/tests/acceptance/toastPlacementControl.ts", import.meta.url), "utf8");
new Function("exports", ts.transpileModule(toastSource, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS }
}).outputText)(toastControls);
const dashboardSourcePath = new URL("../../Studio/tests/acceptance/dashboardJourneySupport.ts", import.meta.url);
const dashboardSource = ts.createSourceFile(String(dashboardSourcePath), readFileSync(dashboardSourcePath, "utf8"), ts.ScriptTarget.Latest, true);
const dashboardParts = dashboardSource.statements.filter(node =>
  ts.isVariableStatement(node) && node.declarationList.declarations.some(item => item.name.getText(dashboardSource) === "pendingDashboardCleanup") ||
  ts.isFunctionDeclaration(node) && ["registerDashboardCleanup", "cleanupDashboardJourney"].includes(node.name?.text)
).map(node => node.getText(dashboardSource)).join("\n");
const dashboardControls = {};
new Function("exports", ts.transpileModule(dashboardParts, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS }
}).outputText)(dashboardControls);
const source = ts.createSourceFile(sourcePath, readFileSync(sourcePath, "utf8"), ts.ScriptTarget.Latest, true);
assert.equal(source.parseDiagnostics.length, 0, "The actual acceptance source parses");
const hooks = [];
function visit(node) {
  if (ts.isCallExpression(node) && node.expression.getText(source) === "test.afterEach") hooks.push(node.arguments[0]);
  ts.forEachChild(node, visit);
}
visit(source);
assert.equal(hooks.length, 1, "Exercise the actual sole cleanup hook");
const compiled = ts.transpileModule(`exports.cleanup = ${hooks[0].getText(source)};`, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.CommonJS }, reportDiagnostics: true
});
assert.equal(compiled.diagnostics?.length ?? 0, 0);

const cookieMarker = "FAKE_COOKIE_MUST_NOT_ENTER_RECEIPT_3866";
const authMarker = "FAKE_AUTH_MUST_NOT_ENTER_RECEIPT_3866";
function sensitiveError() {
  const error = new Error(`request failed\nCall log:\nCookie: ${cookieMarker}\nAuthorization: ${authMarker}`);
  error.name = "TimeoutError";
  error.stack = `Error: ${authMarker}\nCookie: ${cookieMarker}`;
  error.cause = { headers: { Cookie: cookieMarker, Authorization: authMarker } };
  return error;
}

async function exercise({ contextError, deleteError, deleteStatus = 204, cleanups = [], toastReleaseError, dashboardCleanupError } = {}) {
  const page = { context: () => ({ async storageState() { return { cookies: [] }; } }) };
  const createdWorkflows = new WeakMap([[page, [{ id: "metadata-owned", url: "http://owned.invalid/templates", authorization: authMarker }]]]);
  const designerDefinitions = new WeakMap([[page, ["frame-owned", "sequence-owned"]]]);
  const ownedCleanups = new WeakMap([[page, cleanups]]);
  const browserErrors = new WeakMap([[page, []]]);
  const assertions = (actual, message) => ({ toEqual: expected => assert.deepEqual(actual, expected, message) });
  const exports = {};
  const hook = new Function("createdWorkflows", "designerDefinitions", "ownedCleanups", "browserErrors", "expect", "process", "exports", "releaseToastPlacementHolds", "cleanupDashboardJourney",
    `${compiled.outputText}\nreturn exports.cleanup;`)(createdWorkflows, designerDefinitions, ownedCleanups, browserErrors, assertions,
      { env: { ELSA_WORKBENCH_URL: "http://owned.invalid" } }, exports, async page => {
        await toastControls.releaseToastPlacementHolds(page);
        if (toastReleaseError !== undefined) throw toastReleaseError;
      }, async page => {
        await dashboardControls.cleanupDashboardJourney(page);
        if (dashboardCleanupError !== undefined) throw dashboardCleanupError;
      });
  const attempts = [];
  let attachment;
  let failure;
  let disposed = false;
  const playwright = { request: { async newContext(options) {
    assert.equal(options.timeout, 5_000);
    assert.deepEqual(options.storageState.cookies, []);
    if (contextError !== undefined) throw contextError;
    return {
      async delete(url, requestOptions) {
        assert.equal(requestOptions.timeout, 5_000);
        const id = url.split("/").at(-1);
        attempts.push(id);
        if (id === "metadata-owned") assert.equal(requestOptions.headers.Authorization, authMarker);
        if (id === "frame-owned" && deleteError !== undefined) throw deleteError;
        return { status: () => id === "frame-owned" ? deleteStatus : 204 };
      },
      async dispose() { disposed = true; }
    };
  } } };
  try {
    await hook({ page, playwright }, { async attach(name, value) {
      assert.equal(name, "owned-definition-cleanup");
      assert.equal(value.contentType, "application/json");
      attachment = value.body;
    } });
  } catch (error) { failure = error; }
  const expectedIds = ["frame-owned", "metadata-owned", "sequence-owned"];
  if (contextError === undefined) assert.deepEqual(attempts.sort(), expectedIds, "Every exact owned ID receives a cleanup attempt");
  assert.equal(disposed, contextError === undefined, "A created request context is disposed");
  assert.equal(typeof attachment, "string", "Cleanup retains a receipt even when requests fail");
  assert.ok(!attachment.includes(cookieMarker) && !attachment.includes(authMarker), "Receipt excludes fake credential markers");
  const receipt = JSON.parse(attachment);
  assert.deepEqual(Object.keys(receipt).sort(), ["ownedIds", "receipts"]);
  assert.deepEqual(receipt.ownedIds.sort(), expectedIds);
  for (const entry of receipt.receipts) {
    assert.ok(expectedIds.includes(entry.id));
    assert.ok(Object.keys(entry).every(key => ["id", "status", "error"].includes(key)), "Only allowlisted receipt fields");
    if ("error" in entry) assert.equal(entry.error, "request-failed", "Only the fixed error category is retained");
    if ("status" in entry) assert.equal(typeof entry.status, "number");
  }
  return { receipt, failure, disposed };
}

test("successful cleanup keeps owned IDs/status without request headers", async () => {
  const result = await exercise();
  assert.equal(result.failure, undefined);
  assert.equal(result.receipt.receipts.length, 3);
  assert.equal(result.disposed, true);
});

test("request timeout strips message calllog stack and cause while cleanup remains failed", async () => {
  const result = await exercise({ deleteError: sensitiveError() });
  assert.ok(result.failure, "Cleanup failure cannot become success");
  assert.deepEqual(result.receipt.receipts.find(item => item.id === "frame-owned"), { id: "frame-owned", error: "request-failed" });
});

test("request-context failure is sanitized and retained cleanup callbacks still run", async () => {
  let cleanupCalled = false;
  const result = await exercise({ contextError: sensitiveError(), cleanups: [async () => { cleanupCalled = true; }] });
  assert.ok(result.failure);
  assert.equal(cleanupCalled, true);
  assert.deepEqual(result.receipt.receipts.map(item => item.error), ["request-failed", "request-failed", "request-failed"]);
});

test("HTTP delete failures retain their statuses and fail teardown", async () => {
  const result = await exercise({ deleteStatus: 503 });
  assert.ok(result.failure);
  assert.equal(result.receipt.receipts.find(item => item.id === "frame-owned").status, 503);
});

test("non-Error transport failures cannot stringify arbitrary credential-bearing values", async () => {
  const value = { message: cookieMarker, cause: authMarker, toString() { return `${cookieMarker} ${authMarker}`; } };
  const result = await exercise({ deleteError: value });
  assert.ok(result.failure);
  assert.deepEqual(result.receipt.receipts.find(item => item.id === "frame-owned"), { id: "frame-owned", error: "request-failed" });
});

test("registered journey cleanup runs all callbacks and sanitizes failures", async () => {
  const calls = [];
  const result = await exercise({ cleanups: [
    async () => { calls.push("failed"); throw sensitiveError(); },
    async () => { calls.push("succeeded"); }
  ] });
  assert.deepEqual(calls, ["failed", "succeeded"]);
  assert.ok(result.failure);
  assert.ok(!result.failure.message.includes(cookieMarker) && !result.failure.message.includes(authMarker));
});

test("a failed toast release still attempts every owned record and keeps teardown failed", async () => {
  let cleanupCalled = false;
  const result = await exercise({ toastReleaseError: sensitiveError(), cleanups: [async () => { cleanupCalled = true; }] });
  assert.equal(cleanupCalled, true);
  assert.equal(result.receipt.receipts.length, 3);
  assert.ok(result.failure);
  assert.match(result.failure.message, /toast-release-failed/);
  assert.ok(!result.failure.message.includes(cookieMarker) && !result.failure.message.includes(authMarker));
});

 test("Dashboard cleanup failure stays failed while all owned definitions are attempted", async () => {
  const result = await exercise({ dashboardCleanupError: sensitiveError() });
  assert.ok(result.failure);
  assert.match(String(result.failure), /dashboard-cleanup-failed/);
  assert.ok(!String(result.failure).includes(cookieMarker) && !String(result.failure).includes(authMarker));
});
