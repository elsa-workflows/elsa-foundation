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

function scrollingPage(root, geometry) {
  const captured = []; const elementCaptured = [];
  const image = options => { const bytes = Buffer.from("owned-test-image"); return bytes; };
  const selected = { count: async () => 1, ariaSnapshot: async () => '- button "Refresh connections"',
    locator: () => ({ evaluateAll: async () => [] }), evaluate: async () => geometry,
    screenshot: async options => { elementCaptured.push(options); return image(options); } };
  let evaluations = 0;
  return { captured, elementCaptured, url: () => origins[0] + "/security/external-sign-in?backend=owned", viewportSize: () => geometry.viewport,
    locator: selector => selector === "input[type=password]" ? { evaluateAll: async () => true } : selected,
    evaluate: async () => ++evaluations === 1 ? { tag: "body", id: "", role: null, label: "" } : true,
    screenshot: async options => { captured.push(options); return image(options); } };
}

test("records the visible owned region without auto-scrolling a clipped long form", async () => {
  const root = mkdtempSync(join(tmpdir(), "elsa-3579-ui-evidence-test-")); let privateRoot;
  try {
    const evidence = createUiEvidence({ root, origins, sourceSha: "0".repeat(40), dirty: false });
    privateRoot = readFileSync(join(root, ".ui-private-evidence"), "utf8");
    const page = scrollingPage(root, { target: { x: 236, y: -1100, width: 1176, height: 2200 }, viewport: { width: 1440, height: 1100 },
      scroll: { x: 0, y: 900 }, clips: [{ x: 236, y: 108, width: 1176, height: 992, clipX: true, clipY: true }] });
    await evidence.capture(page, "settled-save-failure");
    assert.equal(page.elementCaptured.length, 0, "Element screenshots would scroll and stitch invisible content.");
    assert.equal(page.captured.length, 1);
    assert.deepEqual(page.captured[0].clip, { x: 236, y: 108, width: 1176, height: 992 });
    const record = JSON.parse(readFileSync(join(root, "ui-evidence/001-settled-save-failure.json"), "utf8"));
    assert.equal(record.kind, "viewport-intersection-and-named-action");
    assert.deepEqual(record.captureClip, page.captured[0].clip);
  } finally { if (privateRoot) rmSync(privateRoot, { recursive: true }); rmSync(root, { recursive: true }); }
});

test("an offscreen owned region fails closed instead of moving the page to manufacture evidence", async () => {
  const root = mkdtempSync(join(tmpdir(), "elsa-3579-ui-evidence-test-")); let privateRoot;
  try {
    const evidence = createUiEvidence({ root, origins, sourceSha: "0".repeat(40), dirty: false });
    privateRoot = readFileSync(join(root, ".ui-private-evidence"), "utf8");
    const page = scrollingPage(root, { target: { x: 236, y: -2200, width: 1176, height: 1100 }, viewport: { width: 1440, height: 1100 }, scroll: { x: 0, y: 0 }, clips: [] });
    await assert.rejects(evidence.capture(page, "offscreen"), /visible/);
    assert.equal(page.captured.length, 0); assert.equal(page.elementCaptured.length, 0);
    assert.deepEqual(evidence.result().files, []);
  } finally { if (privateRoot) rmSync(privateRoot, { recursive: true }); rmSync(root, { recursive: true }); }
});
