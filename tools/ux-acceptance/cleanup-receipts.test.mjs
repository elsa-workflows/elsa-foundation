import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { URL, fileURLToPath } from "node:url";
import process from "node:process";

const require = createRequire(new URL("../../Studio/package.json", import.meta.url));
const ts = require("typescript");
const sourcePath = process.env.CLEANUP_RECEIPT_SOURCE ?? fileURLToPath(new URL("../../Studio/tests/acceptance/journeys.spec.ts", import.meta.url));
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

async function exercise({ loginError, loginStatus = 200, deleteError, deleteStatus = 204 } = {}) {
  const page = {};
  const createdWorkflows = new WeakMap([[page, [{ id: "metadata-owned", url: "http://owned.invalid/templates", authorization: authMarker }]]]);
  const designerDefinitions = new WeakMap([[page, ["frame-owned", "sequence-owned"]]]);
  const browserErrors = new WeakMap([[page, []]]);
  const assertions = (actual, message) => ({ toEqual: expected => assert.deepEqual(actual, expected, message) });
  const exports = {};
  const hook = new Function("createdWorkflows", "designerDefinitions", "browserErrors", "expect", "process", "exports",
    `${compiled.outputText}\nreturn exports.cleanup;`)(createdWorkflows, designerDefinitions, browserErrors, assertions,
      { env: { ELSA_WORKBENCH_URL: "http://owned.invalid" } }, exports);
  const attempts = [];
  let attachment;
  let failure;
  const request = {
    async post(url, options) {
      assert.equal(url, "http://owned.invalid/_elsa/identity/login");
      assert.equal(options.timeout, 5_000);
      if (loginError !== undefined) throw loginError;
      return { ok: () => loginStatus >= 200 && loginStatus < 300, status: () => loginStatus };
    },
    async delete(url, options) {
      assert.equal(options.timeout, 5_000);
      attempts.push(url.split("/").at(-1));
      if (url.endsWith("/frame-owned") && deleteError !== undefined) throw deleteError;
      return { status: () => url.endsWith("/frame-owned") ? deleteStatus : 204 };
    }
  };
  try {
    await hook({ page, request }, { async attach(name, value) {
      assert.equal(name, "owned-definition-cleanup");
      assert.equal(value.contentType, "application/json");
      attachment = value.body;
    } });
  } catch (error) { failure = error; }
  assert.deepEqual(attempts.sort(), ["frame-owned", "metadata-owned", "sequence-owned"], "Every exact owned ID receives a cleanup attempt");
  assert.equal(typeof attachment, "string", "Cleanup retains a receipt even when requests fail");
  assert.ok(!attachment.includes(cookieMarker) && !attachment.includes(authMarker), "Receipt excludes fake credential markers");
  const receipt = JSON.parse(attachment);
  assert.deepEqual(Object.keys(receipt).sort(), ["ownedIds", "receipts"]);
  assert.deepEqual(receipt.ownedIds.sort(), ["frame-owned", "metadata-owned", "sequence-owned"]);
  for (const entry of receipt.receipts) {
    assert.ok(["frame-owned", "metadata-owned", "sequence-owned", "cleanup-login"].includes(entry.id));
    assert.ok(Object.keys(entry).every(key => ["id", "status", "error"].includes(key)), "Only allowlisted receipt fields");
    if ("error" in entry) assert.equal(entry.error, "request-failed", "Only the fixed error category is retained");
    if ("status" in entry) assert.equal(typeof entry.status, "number");
  }
  return { receipt, failure };
}

test("successful cleanup keeps owned IDs/status without request headers", async () => {
  const result = await exercise();
  assert.equal(result.failure, undefined);
  assert.equal(result.receipt.receipts.length, 3);
});

test("request timeout strips message calllog stack and cause while cleanup remains failed", async () => {
  const result = await exercise({ deleteError: sensitiveError() });
  assert.ok(result.failure, "Cleanup failure cannot become success");
  assert.deepEqual(result.receipt.receipts.find(item => item.id === "frame-owned"), { id: "frame-owned", error: "request-failed" });
});

test("login transport failure is sanitized and every deletion is still attempted", async () => {
  const result = await exercise({ loginError: sensitiveError() });
  assert.ok(result.failure);
  assert.deepEqual(result.receipt.receipts.find(item => item.id === "cleanup-login"), { id: "cleanup-login", error: "request-failed" });
});

test("HTTP login and delete failures retain their statuses and fail teardown", async () => {
  const result = await exercise({ loginStatus: 401, deleteStatus: 503 });
  assert.ok(result.failure);
  assert.equal(result.receipt.receipts.find(item => item.id === "cleanup-login").status, 401);
  assert.equal(result.receipt.receipts.find(item => item.id === "frame-owned").status, 503);
});

test("non-Error transport failures cannot stringify arbitrary credential-bearing values", async () => {
  const value = { message: cookieMarker, cause: authMarker, toString() { return `${cookieMarker} ${authMarker}`; } };
  const result = await exercise({ deleteError: value });
  assert.ok(result.failure);
  assert.deepEqual(result.receipt.receipts.find(item => item.id === "frame-owned"), { id: "frame-owned", error: "request-failed" });
});
