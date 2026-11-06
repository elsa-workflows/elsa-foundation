import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import * as fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { test } from "node:test";
import { classifyLocation, limits, observeEditor } from "./diagnostics.mjs";

const origin = "http://localhost:14000";
const secret = "PRIVATE_DO_NOT_RETAIN_58a2";
const editorUrl = `${origin}/workflows/definitions?definition=${secret}&token=${secret}#${secret}`;
const visibleStage = "activity-picker-trigger-visible";
const clickStage = "activity-picker-trigger-click";
const png = Buffer.from("89504e470d0a1a0a", "hex");
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
const runSource = await fs.readFile(new URL("./run.mjs", import.meta.url), "utf8");

function deferred() {
  let resolve;
  const promise = new Promise(done => { resolve = done; });
  return { promise, resolve };
}

function editorState(overrides = {}) {
  return {
    url: editorUrl, sensitiveForms: 0, frames: 0, authFallback: 0, visibleDialogs: 1,
    busyElements: 2, editorCanvases: 1, viewport: { width: 1440, height: 1000 },
    ...overrides
  };
}

function buttonState(overrides = {}) {
  return {
    visible: true, enabled: true, centerUnobstructed: true,
    coveringTag: "BUTTON", display: "inline-flex", visibility: "visible",
    pointerEvents: "auto", opacity: 1, bounds: { x: 10, y: 20, width: 40, height: 50 },
    ...overrides
  };
}

class FakePage extends EventEmitter {
  constructor({ states = [editorState()], buttons = [buttonState()], screenshot } = {}) {
    super();
    this.states = states;
    this.buttons = buttons;
    this.frame = {};
    this.calls = [];
    this.screenshotImpl = screenshot ?? (async () => png);
  }

  mainFrame() { return this.frame; }

  async evaluate(callback) {
    this.calls.push(["evaluate", callback]);
    if (this.inspectImpl) return this.inspectImpl(callback);
    this.currentState = this.states.length > 1 ? this.states.shift() : this.states[0];
    const state = { ...this.currentState };
    // Authentication fallback is discovered through the real Playwright locator,
    // not through inspectPage's DOM evaluation result.
    delete state.authFallback;
    return state;
  }

  getByRole(role, options) {
    this.calls.push(["getByRole", role, options]);
    return {
      count: async () => this.buttons.length,
      evaluateAll: async (_callback, cap) => {
        this.calls.push(["evaluateAll", cap]);
        // Return excess entries deliberately: the receipt must enforce its own cap.
        return this.buttons;
      }
    };
  }

  locator(selector) {
    this.calls.push(["locator", selector]);
    if (selector.includes('input[type="password"]')) return { count: async () => this.currentState.sensitiveForms };
    if (selector === "iframe, frame") return { count: async () => this.currentState.frames };
    if (selector === ".studio-auth-signing-in") return { count: async () => this.currentState.authFallback };
    return { maskSelector: selector };
  }

  async screenshot(options) {
    this.calls.push(["screenshot", options]);
    return this.screenshotImpl(options);
  }
}

async function fixture(t, { page = new FakePage(), captureMs, observe = observeEditor } = {}) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "pair-diagnostics-test-"));
  const outputDir = path.join(root, "diagnostics");
  const observer = observe(page, { studioOrigin: origin, outputDir, captureMs });
  t.after(async () => {
    observer.dispose();
    await fs.rm(root, { recursive: true, force: true });
  });
  return {
    root, outputDir, page, observer,
    async receipt() {
      const raw = await fs.readFile(path.join(outputDir, "diagnostic.json"), "utf8");
      assert.ok(Buffer.byteLength(raw) <= 16 * 1024);
      assert.ok(!raw.includes(secret), "receipt retained an arbitrary secret");
      return JSON.parse(raw);
    },
    async files() { return fs.readdir(outputDir).catch(error => error.code === "ENOENT" ? [] : Promise.reject(error)); }
  };
}

test("location classification drops query values, fragments, foreign origins and arbitrary routes", () => {
  assert.deepEqual(classifyLocation(editorUrl, origin), {
    location: `${origin}/workflows/definitions?definition=[present]`, editor: true
  });
  for (const [url, location] of [
    [`${origin}/workflows/definitions?token=${secret}#${secret}`, `${origin}/workflows/definitions`],
    [`${origin}/workflows/definitions/${secret}?definition=${secret}`, "other-studio-route"],
    [`${origin}/_elsa/identity/login?returnUrl=${secret}`, "other-studio-route"],
    [`https://${secret}.example/workflows/definitions?definition=${secret}`, "other-origin"],
    [`data:text/html,${secret}`, "other-origin"],
    [`not-a-url-${secret}`, "invalid-url"]
  ]) {
    assert.deepEqual(classifyLocation(url, origin), { location, editor: false });
  }
});

test("event projections retain categories without touching raw text, args, errors, headers or payloads", async t => {
  const f = await fixture(t);
  let rawReads = 0;
  const forbidden = () => { rawReads++; throw new Error(secret); };
  const poison = fields => Object.defineProperties({}, Object.fromEntries(fields.map(field => [field, { get: forbidden }])));
  f.page.emit("console", Object.assign(poison(["text", "args", "location"]), { type: () => "warning" }));
  f.page.emit("console", Object.assign(poison(["text", "args"]), { type: () => "error" }));
  f.page.emit("console", Object.assign(poison(["text", "args"]), { type: () => "log" }));
  f.page.emit("pageerror", Object.assign(poison(["message", "stack", "cause"]), { name: "TypeError" }));
  f.page.emit("pageerror", Object.assign(poison(["message", "stack"]), { name: secret }));
  f.page.emit("requestfailed", Object.assign(poison(["failure", "headers", "allHeaders", "postData", "postDataJSON"]), {
    url: () => `${origin}/_content/${secret}.js?access_token=${secret}#${secret}`, resourceType: () => "script"
  }));
  f.page.emit("requestfailed", Object.assign(poison(["failure", "headers", "postData"]), {
    url: () => `https://${secret}.example/${secret}`, resourceType: () => secret
  }));
  f.page.emit("response", Object.assign(poison(["headers", "allHeaders", "body", "text", "json", "request"]), {
    status: () => 401, url: () => `http://localhost:13000/_elsa/${secret}?token=${secret}`
  }));
  for (const status of [200, 399, 600, 401.5, NaN]) {
    f.page.emit("response", { status: () => status, url: forbidden });
  }
  assert.equal(await f.observer.captureFailure(visibleStage, "timeout"), "retained");
  assert.equal(rawReads, 0);
  assert.deepEqual((await f.receipt()).events, [
    { kind: "console-warning" }, { kind: "console-error" },
    { kind: "page-error", category: "TypeError" }, { kind: "page-error", category: "other" },
    { kind: "request-failed", origin: "studio", route: "asset", resource: "script" },
    { kind: "request-failed", origin: "other", route: "other", resource: "other" },
    { kind: "http-error", origin: "workbench", route: "api", status: 401 }
  ]);
});

test("safe capture bounds events, buttons, numbers and JSON and requests a masked viewport PNG", async t => {
  const page = new FakePage({
    states: [editorState({ visibleDialogs: Infinity, busyElements: -8, editorCanvases: 1e8, viewport: { width: 1e9, height: NaN }, extra: secret })],
    buttons: Array.from({ length: 30 }, () => buttonState({
      visible: secret, coveringTag: secret, display: secret, opacity: 5,
      bounds: { x: -1e9, y: Infinity, width: 1e9, height: -2 }, text: secret, html: secret
    }))
  });
  const f = await fixture(t, { page });
  for (let index = 0; index < 50; index++) page.emit("response", { status: () => 400 + index, url: () => `${origin}/${secret}` });
  assert.equal(await f.observer.captureFailure(clickStage, secret), "retained");
  const receipt = await f.receipt();
  assert.equal(receipt.stage, clickStage);
  assert.equal(receipt.failure, "other");
  assert.equal(receipt.events.length, 20);
  assert.equal(receipt.events[0].status, 430, "old events must be evicted");
  assert.equal(receipt.events.at(-1).status, 449);
  assert.equal(receipt.buttonCount, 30);
  assert.equal(receipt.buttons.length, 3);
  assert.deepEqual(receipt.buttons[0], {
    visible: false, enabled: true, centerUnobstructed: true, coveringTag: "other",
    display: "other", visibility: "visible", pointerEvents: "auto", opacity: 1,
    bounds: { x: -10000, y: null, width: 10000, height: 0 }
  });
  assert.deepEqual(receipt.state, { visibleDialogs: null, busyElements: 0, editorCanvases: 1000, viewport: { width: 4000, height: null } });
  assert.deepEqual(page.calls.find(call => call[0] === "getByRole").slice(1), ["button", { name: "Add activity", exact: true, includeHidden: true }]);
  assert.deepEqual(page.calls.find(call => call[0] === "screenshot")[1], {
    type: "png", fullPage: false, timeout: 1000,
    mask: [{ maskSelector: 'input, textarea, [contenteditable]:not([contenteditable="false"])' }], maskColor: "#000000"
  });
  assert.deepEqual(await fs.readFile(path.join(f.outputDir, "viewport.png")), png);
  assert.equal((await fs.stat(f.outputDir)).mode & 0o777, 0o700);
  assert.equal((await fs.stat(path.join(f.outputDir, "diagnostic.json"))).mode & 0o777, 0o600);
  assert.equal((await fs.stat(path.join(f.outputDir, "viewport.png"))).mode & 0o777, 0o600);
  assert.deepEqual(limits, { captureMs: 3000, events: 20, buttons: 3, jsonBytes: 16384, pngBytes: 2097152 });
});

for (const [name, state] of [
  ["password or login form", editorState({ sensitiveForms: 1 })],
  ["embedded frame", editorState({ frames: 1 })],
  ["authentication fallback", editorState({ authFallback: 1 })],
  ["unknown origin", editorState({ url: `https://other.example/${secret}?definition=${secret}` })],
  ["login route", editorState({ url: `${origin}/_elsa/identity/login?returnUrl=${secret}` })],
  ["arbitrary Studio route", editorState({ url: `${origin}/${secret}?definition=${secret}` })],
  ["definitions list", editorState({ url: `${origin}/workflows/definitions` })]
]) {
  test(`capture suppresses editor inspection and screenshot for ${name}`, async t => {
    const f = await fixture(t, { page: new FakePage({ states: [state] }) });
    assert.equal(await f.observer.captureFailure(visibleStage, "timeout"), "suppressed-unsafe-page");
    const receipt = await f.receipt();
    assert.equal(receipt.screenshot, "suppressed");
    assert.equal(receipt.location, undefined);
    assert.equal(receipt.state, undefined);
    assert.equal(receipt.buttons, undefined);
    assert.ok(!f.page.calls.some(call => ["screenshot", "getByRole"].includes(call[0])));
    assert.deepEqual(await f.files(), ["diagnostic.json"]);
  });
}

for (const [name, mutate] of [
  ["navigation event even if the URL returns", page => page.emit("framenavigated", page.mainFrame())],
  ["URL change", page => { page.states = [editorState({ url: `${origin}/_elsa/identity/login?token=${secret}` })]; }],
  ["new password form", page => { page.states = [editorState({ sensitiveForms: 1 })]; }],
  ["new frame", page => { page.states = [editorState({ frames: 1 })]; }],
  ["authentication fallback", page => { page.states = [editorState({ authFallback: 1 })]; }]
]) {
  test(`screenshot bytes are discarded after ${name} during capture`, async t => {
    const page = new FakePage();
    page.screenshotImpl = async () => { mutate(page); return png; };
    const f = await fixture(t, { page });
    assert.equal(await f.observer.captureFailure(clickStage, "timeout"), "suppressed-navigation-or-page-change");
    assert.equal((await f.receipt()).screenshot, "suppressed");
    assert.deepEqual(await f.files(), ["diagnostic.json"]);
  });
}

test("a page change before the image prevents screenshot invocation", async t => {
  const f = await fixture(t, { page: new FakePage({ states: [editorState(), editorState({ frames: 1 })] }) });
  assert.equal(await f.observer.captureFailure(clickStage, "error"), "suppressed-navigation-or-page-change");
  assert.ok(!f.page.calls.some(call => call[0] === "screenshot"));
});

test("authentication fallback appearing before the image prevents screenshot invocation", async t => {
  const f = await fixture(t, { page: new FakePage({ states: [editorState(), editorState({ authFallback: 1 })] }) });
  assert.equal(await f.observer.captureFailure(clickStage, "error"), "suppressed-navigation-or-page-change");
  assert.ok(!f.page.calls.some(call => call[0] === "screenshot"));
  assert.equal((await f.receipt()).screenshot, "suppressed");
  assert.deepEqual(await f.files(), ["diagnostic.json"]);
});

test("an oversized or invalid screenshot is excluded from disk", async t => {
  for (const image of [Buffer.alloc(2 * 1024 * 1024 + 1), secret]) {
    await t.test(Buffer.isBuffer(image) ? "oversized" : "invalid", async child => {
      const f = await fixture(child, { page: new FakePage({ screenshot: async () => image }) });
      assert.equal(await f.observer.captureFailure(clickStage, "error"), "screenshot-over-limit");
      assert.equal((await f.receipt()).screenshot, "suppressed");
      assert.deepEqual(await f.files(), ["diagnostic.json"]);
    });
  }
});

test("filesystem and page errors become fixed results without escaping or retaining exception text", async t => {
  await t.test("existing directory is not overwritten", async child => {
    const f = await fixture(child);
    await fs.mkdir(f.outputDir);
    await fs.writeFile(path.join(f.outputDir, "diagnostic.json"), "existing");
    assert.equal(await f.observer.captureFailure(visibleStage, "error"), "directory-unavailable");
    assert.equal(await fs.readFile(path.join(f.outputDir, "diagnostic.json"), "utf8"), "existing");
    assert.equal(f.page.calls.length, 0);
  });
  await t.test("detached page", async child => {
    const page = new FakePage();
    page.inspectImpl = async () => { throw new Error(secret); };
    const f = await fixture(child, { page });
    assert.equal(await f.observer.captureFailure(visibleStage, "error"), "capture-unavailable");
    assert.deepEqual(await f.files(), []);
  });
  await t.test("screenshot rejected", async child => {
    const f = await fixture(child, { page: new FakePage({ screenshot: async () => { throw new Error(secret); } }) });
    assert.equal(await f.observer.captureFailure(clickStage, "error"), "screenshot-unavailable");
    assert.equal((await f.receipt()).screenshot, "suppressed");
    assert.deepEqual(await f.files(), ["diagnostic.json"]);
  });
});

test("only the two trigger failures capture and disposal detaches all event listeners", async t => {
  const f = await fixture(t);
  assert.equal(await f.observer.captureFailure("browser-login", "timeout"), "not-applicable");
  assert.equal(await f.observer.captureFailure("activity-picker-open", "timeout"), "not-applicable");
  assert.equal(f.page.calls.length, 0);
  assert.deepEqual(await f.files(), []);
  assert.ok(f.page.eventNames().length > 0);
  f.observer.dispose();
  f.observer.dispose();
  assert.deepEqual(f.page.eventNames(), []);
});

test("malformed event objects cannot interrupt the journey or add raw exception details", async t => {
  const f = await fixture(t);
  const fail = () => { throw new Error(secret); };
  for (const [event, value] of [
    ["console", { type: fail }],
    ["pageerror", Object.defineProperty({}, "name", { get: fail })],
    ["requestfailed", { resourceType: () => "fetch", url: fail }],
    ["response", { status: fail }]
  ]) assert.doesNotThrow(() => f.page.emit(event, value));
  assert.equal(await f.observer.captureFailure(clickStage, "error"), "retained");
  assert.deepEqual((await f.receipt()).events, []);
});

test("a hanging page capture returns within its deadline and cannot write after it resolves", async t => {
  const gate = deferred();
  const f = await fixture(t, { captureMs: 20, page: new FakePage({ screenshot: () => gate.promise }) });
  const started = Date.now();
  assert.equal(await f.observer.captureFailure(clickStage, "timeout"), "capture-timeout");
  assert.ok(Date.now() - started < 1000, "capture did not enforce its deadline");
  gate.resolve(png);
  await delay(30);
  assert.deepEqual(await f.files(), []);
});

// Load the actual module with only its filesystem imports injected. This lets an
// in-flight write finish after the deadline, which a fake page alone cannot model.
async function observerWithFilesystem(overrides) {
  const source = await fs.readFile(new URL("./diagnostics.mjs", import.meta.url), "utf8");
  const executable = source
    .replace(/import \{([^}]+)\} from "node:fs\/promises";/, "const {$1} = injectedFs;")
    .replace('import path from "node:path";', "")
    .replace(/\bexport /g, "");
  return new AsyncFunction("injectedFs", "path", `${executable}\nreturn observeEditor;`)({ ...fs, ...overrides }, path);
}

for (const delayedFile of ["viewport.png", "diagnostic.json"]) {
  test(`an in-flight ${delayedFile} write cannot publish an artifact after capture timeout`, { timeout: 2000 }, async t => {
    const entered = deferred();
    const release = deferred();
    const finished = deferred();
    const observe = await observerWithFilesystem({
      async writeFile(filename, ...args) {
        if ([delayedFile, `${delayedFile}.pending`].includes(path.basename(filename))) {
          entered.resolve();
          await release.promise;
          // Model an OS write that completes despite best-effort cancellation.
          const [contents, options] = args;
          try { return await fs.writeFile(filename, contents, { ...options, signal: undefined }); } finally { finished.resolve(); }
        }
        return fs.writeFile(filename, ...args);
      }
    });
    const f = await fixture(t, { observe, captureMs: 30 });
    const capture = f.observer.captureFailure(clickStage, "timeout");
    await entered.promise;
    assert.equal(await capture, "capture-timeout");
    const beforeRelease = await f.files();
    release.resolve();
    await finished.promise;
    await delay(30);
    assert.deepEqual(await f.files(), beforeRelease, "a timed-out capture published late artifacts");
  });
}

// Execute run.mjs's real outer try/catch/finally with fakes, without importing
// Playwright or invoking its Docker/HTTP/browser entrypoint.
async function runOuterTail({ failure, capture, dispose, cleanupFailure, browserFailure } = {}) {
  const start = runSource.lastIndexOf("\ntry {\n  await main();");
  assert.ok(start > 0, "could not locate the runner entrypoint");
  const classifierStart = runSource.indexOf("function safeFailureClass(");
  const classifierEnd = runSource.indexOf("\nfunction commandOutput(", classifierStart);
  assert.ok(classifierStart > 0 && classifierEnd > classifierStart);
  const calls = [];
  const errors = [];
  const logs = [];
  const fakeProcess = {};
  const injected = {
    stage: clickStage, process: fakeProcess,
    console: { error: message => errors.push(message) },
    log: message => logs.push(message),
    main: async () => { calls.push("main"); if (failure) throw failure; },
    editorDiagnostics: {
      captureFailure: async (...args) => { calls.push(["capture", ...args]); return capture?.(...args) ?? "capture-timeout"; },
      dispose: () => { calls.push("dispose"); return dispose?.(); }
    },
    browser: { close: async () => { calls.push("browser"); if (browserFailure) throw browserFailure; } },
    cleanupOwnedResources: async () => { calls.push("resources"); if (cleanupFailure) throw cleanupFailure; },
    ownershipApproved: true,
    tempDir: "/fake-owned-temp",
    rm: async (...args) => { calls.push(["rm", ...args]); }
  };
  await new AsyncFunction("injected", `
    const { stage, process, console, log, main, editorDiagnostics, browser,
      cleanupOwnedResources, ownershipApproved, tempDir, rm } = injected;
    let cleanupError;
    ${runSource.slice(classifierStart, classifierEnd)}
    ${runSource.slice(start)}
  `)(injected);
  return { calls, errors, logs, exitCode: fakeProcess.exitCode };
}

for (const [name, failure, classification] of [
  ["timeout", Object.assign(new Error(secret), { name: "TimeoutError" }), "timeout"],
  ["assertion", new assert.AssertionError({ message: secret }), "assertion"],
  ["ambiguous locator", new Error(`strict mode violation ${secret}`), "ambiguous-locator"],
  ["interrupted navigation", new Error(`interrupted by another navigation ${secret}`), "navigation-interrupted"]
]) {
  test(`runner preserves the original ${name} classification when diagnostics throws`, async () => {
    const result = await runOuterTail({ failure, capture: () => { throw new Error(secret); }, browserFailure: new Error(secret) });
    assert.equal(result.exitCode, 1);
    assert.deepEqual(result.errors, [`[published-pair] failed during ${clickStage} (${classification})`]);
    assert.deepEqual(result.calls, ["main", ["capture", clickStage, classification], "dispose", "browser", "resources", ["rm", "/fake-owned-temp", { recursive: true, force: true }]]);
    assert.ok(!JSON.stringify(result).includes(secret));
  });
}

test("runner keeps a failure failed after diagnostic timeout and attempts all cleanup", async () => {
  const result = await runOuterTail({ failure: Object.assign(new Error(secret), { name: "TimeoutError" }), capture: async () => "capture-timeout", cleanupFailure: new Error(secret) });
  assert.equal(result.exitCode, 1);
  assert.deepEqual(result.errors, [
    `[published-pair] failed during ${clickStage} (timeout)`,
    "[published-pair] owned cleanup failed (error)"
  ]);
  assert.ok(result.calls.includes("browser") && result.calls.includes("resources"));
  assert.ok(result.calls.some(call => Array.isArray(call) && call[0] === "rm"));
  assert.ok(!JSON.stringify(result).includes(secret));
});

test("a throwing diagnostic disposer cannot mask the original failure or skip owned cleanup", async () => {
  const result = await runOuterTail({ failure: Object.assign(new Error(secret), { name: "TimeoutError" }), dispose: () => { throw new Error(secret); } });
  assert.equal(result.exitCode, 1);
  assert.deepEqual(result.errors, [`[published-pair] failed during ${clickStage} (timeout)`]);
  assert.ok(result.calls.includes("browser"));
  assert.ok(result.calls.includes("resources"));
  assert.ok(result.calls.some(call => Array.isArray(call) && call[0] === "rm"));
});

test("runner success does not capture diagnostics, and cleanup failure prevents a pass", async () => {
  const passed = await runOuterTail();
  assert.equal(passed.exitCode, undefined);
  assert.deepEqual(passed.errors, []);
  assert.ok(!passed.calls.some(call => Array.isArray(call) && call[0] === "capture"));
  assert.ok(passed.calls.includes("resources"));
  const failed = await runOuterTail({ cleanupFailure: new Error(secret) });
  assert.equal(failed.exitCode, 1);
  assert.deepEqual(failed.errors, ["[published-pair] owned cleanup failed (error)"]);
});

test("the journey keeps its strict selector, visibility wait, original click and 30-second timeout", async () => {
  const journeyStart = runSource.indexOf("async function runBrowserJourney() {");
  const preambleStart = runSource.indexOf("{", journeyStart) + 1;
  const preambleEnd = runSource.indexOf('  stage = "browser-login";', preambleStart);
  assert.ok(journeyStart > 0 && preambleEnd > preambleStart, "could not locate the journey preamble");
  const observedTimeouts = [];
  const timeoutPage = { setDefaultTimeout: milliseconds => observedTimeouts.push(milliseconds) };
  const chromium = { launch: async () => ({
    newContext: async () => ({ newPage: async () => timeoutPage })
  }) };
  await new AsyncFunction("chromium", `let browser; ${runSource.slice(preambleStart, preambleEnd)}`)(chromium);
  assert.deepEqual(observedTimeouts, [30000], "the actual journey page must use the original timeout");
  const start = runSource.indexOf('  stage = "activity-picker-trigger-visible";');
  const end = runSource.indexOf('  stage = "activity-picker-open";', start);
  assert.ok(start > 0 && end > start);
  const calls = [];
  const page = { getByRole(role, options) {
    calls.push(["selector", role, options]);
    return {
      waitFor: async options => calls.push(["wait", options, page.readStage()]),
      click: async (...args) => calls.push(["click", ...args, page.readStage()])
    };
  } };
  const finalStage = await new AsyncFunction("page", `let stage; page.readStage = () => stage; ${runSource.slice(start, end)} return stage;`)(page);
  assert.equal(finalStage, clickStage);
  assert.deepEqual(calls, [
    ["selector", "button", { name: "Add activity", exact: true }],
    ["wait", { state: "visible" }, visibleStage], ["click", clickStage]
  ]);
  // Execute the same block with the visibility wait failing: the click must not run.
  calls.length = 0;
  page.getByRole = () => ({ waitFor: async () => { throw new Error("wait failed"); }, click: async () => calls.push("click") });
  await assert.rejects(new AsyncFunction("page", `let stage; ${runSource.slice(start, end)}`)(page), /wait failed/);
  assert.deepEqual(calls, []);
});
