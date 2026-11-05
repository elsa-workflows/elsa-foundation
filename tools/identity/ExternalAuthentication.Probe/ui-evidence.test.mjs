import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, readdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { checkedRoute, createUiEvidence, safeAria } from "./ui-evidence.mjs";

const origins = ["http://127.0.0.1:12345"];
test("only owned application routes and a blank chooser route are recordable", () => {
  assert.equal(checkedRoute(origins[0] + "/security/user-access?backend=owned", origins), "/security/user-access");
  assert.equal(checkedRoute(origins[0] + "/backend/_elsa/identity/login?returnUrl=%2Fbackend%2Fresource", origins, true), "/backend/_elsa/identity/login");
  for (const value of [origins[0] + "/authorize?code=sentinel", origins[0] + "/identity/external/preview/callback",
    origins[0] + "/security/user-access?externalPreviewHandle=sentinel", origins[0] + "/security/user-access#sentinel",
    origins[0] + "/security/user-access?backend=sentinel", "http://127.0.0.1:9999/security/user-access",
    "http://fixture:sentinel@127.0.0.1:12345/security/user-access"])
    assert.throws(() => checkedRoute(value, origins));
});
test("field values outside the closed public fixture vocabulary are omitted", () => {
  const snapshot = safeAria('- textbox "Issuer URL": http://127.0.0.1:7777/issuer\n- textbox "External subject": unrecognized-subject\n- checkbox "Reader" [checked]');
  assert.equal(snapshot.includes("unrecognized-subject"), false); assert.equal(snapshot.includes(":7777"), false);
  assert.equal(snapshot.includes('checkbox "Reader" [checked]'), true);
});
test("public fixture draft values make before/after retention inspectable", () => {
  assert.equal(safeAria('- textbox "External subject": unattached-retained-proof').includes("unattached-retained-proof"), true);
  assert.throws(() => safeAria('- textbox "Display name": Studio company', ["Studio company"]));
});
test("known credential/control sentinels and authentication material fail closed without echo", () => {
  for (const value of ["control-key-sentinel", "access_token: sentinel", "-----BEGIN PRIVATE KEY-----", "Password123!",
    "eyJabcdefghijklmnopqrstuvwxyz0123456789.payload.signature"])
    assert.throws(() => safeAria(value, ["control-key-sentinel"]), error => !error.message.includes(value));
});
test("snapshot length is bounded", () => assert.throws(() => safeAria("x".repeat(65_537))));
test("blocked callback writes no public snapshot and never screenshots", async () => {
  const root = mkdtempSync(join(tmpdir(), "elsa-3579-ui-evidence-test-")); let privateRoot;
  try {
    const evidence = createUiEvidence({ root, origins, sourceSha: "0".repeat(40), dirty: false });
    privateRoot = readFileSync(join(root, ".ui-private-evidence"), "utf8");
    await assert.rejects(evidence.capture({ url: () => origins[0] + "/callback?code=sentinel" }, "blocked"));
    assert.deepEqual(readdirSync(join(root, "ui-evidence")), []); assert.deepEqual(evidence.result().files, []);
  } finally { if (privateRoot) rmSync(privateRoot, { recursive: true }); rmSync(root, { recursive: true }); }
});
