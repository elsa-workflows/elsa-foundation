// Real, owned Studio journeys. This driver never registers a product contribution or fabricates a response.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { createRequire } from "node:module";

const require = createRequire(new URL("../../../Studio/package.json", import.meta.url));
const { chromium, expect } = require("@playwright/test");
const configPath = process.argv[2];
if (!configPath || !path.isAbsolute(configPath)) throw new Error("Supply an absolute private browser configuration path.");
const config = JSON.parse(fs.readFileSync(configPath, "utf8"));
const supported = new Set(["resource", "workflow", "duplicate-execute", "duplicate-ack", "deny-before-effect", "client-binding-removed", "malformed-result", "lost-confirmation", "renderer-unavailable", "consumed-intent-remount", "sdk-incompatible"]);
if (!supported.has(config.journey)) throw new Error("Unsupported browser journey: " + config.journey);
const witness = JSON.parse(fs.readFileSync(new URL("./witness.json", import.meta.url), "utf8"));
function need(condition, message) { if (!condition) throw new Error(message); }
function ownedOrigin(value) {
  const url = new URL(value);
  need(url.protocol === "http:" && url.hostname === "127.0.0.1" && url.port && !["5089", "5095", "7030", "7243"].includes(url.port)
    && !url.username && !url.password && url.pathname === "/" && !url.search && !url.hash, "An explicit owned loopback origin is required.");
  return url.origin;
}
const studio = ownedOrigin(config.studioOrigin), backend = ownedOrigin(config.backendOrigin);
need(studio !== backend, "Backend and Studio must have distinct owned origins.");
need(path.isAbsolute(config.output) && !fs.existsSync(config.output), "Evidence output must be a new absolute directory.");
need(path.isAbsolute(config.storageState) && path.isAbsolute(config.commandFile), "Private auth and protocol command paths are required.");
fs.mkdirSync(config.output, { recursive: true, mode: 0o700 });
const digest = bytes => crypto.createHash("sha256").update(bytes).digest("hex");
const ref = file => ({ path: file, sha256: digest(fs.readFileSync(file)) });
const secretKey = /^(authorization|proxy-authorization|password|apiKey|gitHubToken|runtimeConnectionToken|accessToken|refreshToken|clientSecret|cookie|set-cookie)$/i;
function redact(value) {
  if (Array.isArray(value)) return value.map(redact);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, secretKey.test(key) ? "[redacted]" : redact(item)]));
  return typeof value === "string" ? value.replace(/\bBearer\s+[^\s"\\]+/gi, "Bearer [redacted]")
    .replace(/\bsk-(?:proj-|ant-)?[A-Za-z0-9_-]{12,}/g, "[redacted]")
    .replace(/("(?:authorization|proxy-authorization|password|apiKey|gitHubToken|runtimeConnectionToken|accessToken|refreshToken|clientSecret|cookie|set-cookie)"\s*:\s*)"(?:\\.|[^"\\])*"/gi, '$1"[redacted]"')
    .replace(/\b((?:access[_-]?token|refresh[_-]?token|client[_-]?secret|password|api[_-]?key)=)[^\s&#"']+/gi, "$1[redacted]") : value;
}
function diagnosticUrl(value) {
  try { const url = new URL(value); return url.origin + url.pathname; }
  catch { return "[unavailable URL]"; }
}
function retain(name, value) {
  const file = path.join(config.output, name + ".json");
  fs.writeFileSync(file, JSON.stringify(redact(value), null, 2) + "\n", { mode: 0o600 });
  return ref(file);
}
const result = { journey: config.journey, provider: config.provider, evidenceCategory: "deterministic-provider-protocol", liveProvider: false,
  status: "running", observations: {}, evidence: [], startedAt: new Date().toISOString(), noFabricatedResponses: true };
const browser = await chromium.launch({ headless: true });
const context = await browser.newContext({ storageState: config.storageState, viewport: { width: 1920, height: 1080 } });
const page = await context.newPage();
const streamCapture = await captureNativeStreams(page);
page.setDefaultTimeout(30000);
const requests = [], owningWrites = [], pageErrors = [], consoleMessages = [], failedRequests = [];
const resourceInteractions = [], moduleOwnerReads = [], pendingOwnerReads = [];
await context.exposeBinding("qualificationObserveResourceInteraction", (_source, value) => resourceInteractions.push(value));
await context.addInitScript(observeResourceInteractions, { route: witness.route });
let owningDraftPath;
page.on("pageerror", error => pageErrors.push(error.message));
page.on("console", message => {
  const location = message.location();
  consoleMessages.push(redact({ type: message.type(), text: message.text(), location: {
    url: diagnosticUrl(location.url), lineNumber: location.lineNumber, columnNumber: location.columnNumber
  } }));
});
page.on("requestfailed", request => failedRequests.push(redact({
  url: diagnosticUrl(request.url()), method: request.method(), resourceType: request.resourceType(),
  errorText: request.failure()?.errorText
})));
page.on("request", request => {
  const url = new URL(request.url());
  if (url.origin === backend && owningDraftPath === url.pathname && ["PUT", "POST", "PATCH", "DELETE"].includes(request.method())) {
    owningWrites.push({ url: request.url(), method: request.method(), body: request.postDataJSON() });
  }
  if (url.origin !== backend || !url.pathname.startsWith("/_elsa/agent/") || request.method() !== "POST") return;
  requests.push({ url: request.url(), method: request.method(), body: request.postDataJSON() });
});
page.on("response", response => {
  const url = new URL(response.url());
  if (url.origin !== backend || url.pathname !== witness.ownerEndpoint || response.request().method() !== "GET") return;
  const observation = { url: diagnosticUrl(response.url()), status: response.status(), completed: false };
  moduleOwnerReads.push(observation);
  pendingOwnerReads.push(response.text().then(rawBody => Object.assign(observation, { rawBody, completed: true }))
    .catch(error => { observation.error = error.message; }));
});
const dock = () => page.getByRole("region", { name: "Weaver assistant", exact: true });
function observeResourceInteractions({ route }) {
  const labels = ["Read with Weaver", "Review visibility change", "Show Acme status card on Home"];
  let pressed = null, sequence = 0;
  function control(target) {
    if (!(target instanceof Element)) return null;
    const node = target.closest('button, input[type="checkbox"]');
    const label = node?.tagName === "BUTTON" ? node.textContent?.trim() : node?.closest("label")?.textContent?.trim();
    return labels.includes(label) ? { node, label } : null;
  }
  function identity(target) {
    return { tag: target instanceof Element ? target.tagName : null, label: control(target)?.label ?? null };
  }
  function rect(node) {
    const { x, y, width, height } = node.getBoundingClientRect();
    return { x, y, width, height };
  }
  function emit(type, event, active, phase) {
    // Only exact fixture control labels and geometry leave the page. Unknown
    // release/focus targets contribute their tag, never text or input values.
    const target = control(event?.target);
    const value = { type, phase, label: target?.label ?? null, isTrusted: event?.isTrusted,
      disabled: target?.node.disabled, checked: target?.node.tagName === "INPUT" ? target.node.checked : undefined,
      target: identity(event?.target), activeElement: identity(document.activeElement),
      clientX: event?.clientX, clientY: event?.clientY, pointerId: event?.pointerId,
      at: performance.now(), timeOrigin: performance.timeOrigin };
    if (active) Object.assign(value, { activationId: active.id, pressedLabel: active.label,
      pressedElementConnected: active.node.isConnected, pressedElementDisabled: active.node.disabled,
      originalRect: active.rect, currentRect: rect(active.node),
      targetInsidePressedElement: event?.target instanceof Node ? active.node.contains(event.target) : undefined,
      hitAtPressPoint: identity(document.elementFromPoint(active.x, active.y)) });
    void window.qualificationObserveResourceInteraction(value).catch(() => undefined);
  }
  for (const type of ["pointerdown", "mousedown", "pointerup", "mouseup", "click", "change", "pointercancel"])
    document.addEventListener(type, event => {
      if (location.pathname !== route) return;
      const target = control(event.target);
      if (type === "pointerdown") {
        pressed = target ? { ...target, id: ++sequence, rect: rect(target.node), x: event.clientX, y: event.clientY } : null;
        if (pressed) {
          const active = pressed;
          queueMicrotask(() => emit("activation-snapshot", null, active, "microtask"));
          requestAnimationFrame(() => emit("activation-snapshot", null, active, "animation-frame"));
        }
      }
      if (!target && !pressed) return;
      emit(type, event, pressed);
      if (type === "click" || type === "pointercancel") pressed = null;
    }, { capture: true, passive: true });
}
async function captureNativeStreams(target) {
  // Observe the existing browser request only. A clone/second fetch would change
  // stream consumption, and response.body() loses bytes when Studio aborts it.
  const cdp = await context.newCDPSession(target);
  const streams = new Map(), pending = [];
  cdp.on("Network.responseReceived", event => {
    const url = new URL(event.response.url);
    if (url.origin !== backend || !/^\/_elsa\/agent\/sessions\/[^/]+\/stream$/.test(url.pathname)) return;
    const contentType = Object.entries(event.response.headers).find(([key]) => key.toLowerCase() === "content-type")?.[1] ?? "";
    const stream = { url: diagnosticUrl(url.href), status: event.response.status, contentType,
      termination: "open", captureError: null, prefix: Buffer.alloc(0), chunks: [], streamedBytes: 0 };
    streams.set(event.requestId, stream);
    pending.push(cdp.send("Network.streamResourceContent", { requestId: event.requestId }).then(response => {
      // CDP bufferedData ends precisely where dataReceived.data starts. Events
      // can arrive before this promise resolves, so prepend this prefix once.
      stream.prefix = Buffer.from(response.bufferedData, "base64");
      if (stream.prefix.length + stream.streamedBytes > 8 * 1024 * 1024)
        stream.captureError = "Native SSE capture exceeded its bounded evidence size.";
    }).catch(error => { stream.captureError = error.message; }));
  });
  cdp.on("Network.dataReceived", event => {
    const stream = streams.get(event.requestId);
    if (!stream || !event.data || stream.captureError) return;
    const bytes = Buffer.from(event.data, "base64");
    stream.streamedBytes += bytes.length;
    if (stream.prefix.length + stream.streamedBytes > 8 * 1024 * 1024) {
      stream.captureError = "Native SSE capture exceeded its bounded evidence size.";
      return;
    }
    stream.chunks.push(bytes);
  });
  cdp.on("Network.loadingFinished", event => {
    const stream = streams.get(event.requestId);
    if (stream) stream.termination = "loadingFinished";
  });
  cdp.on("Network.loadingFailed", event => {
    const stream = streams.get(event.requestId);
    if (stream) Object.assign(stream, { termination: "loadingFailed", errorText: event.errorText, canceled: event.canceled === true });
  });
  await cdp.send("Network.enable");
  return async () => {
    for (let settled = 0; settled < pending.length;) {
      const batch = pending.slice(settled); settled = pending.length;
      await Promise.all(batch);
    }
    return [...streams.values()].map(({ prefix, chunks, ...stream }) => ({ ...stream, bufferedBytes: prefix.length,
      rawBody: Buffer.concat([prefix, ...chunks]).toString("utf8") }));
  };
}
function command(toolName, args) {
  const value = { commandId: crypto.randomUUID(), toolName, arguments: args };
  const temporary = config.commandFile + "." + value.commandId;
  fs.writeFileSync(temporary, JSON.stringify(value), { mode: 0o600 });
  fs.renameSync(temporary, config.commandFile);
  result.evidence.push(retain("command-" + value.commandId, value));
  return value;
}
async function owner(name, endpoint, headers = {}) {
  need(endpoint.startsWith("/") && !endpoint.startsWith("//"), "Owner endpoint must be local to the verified backend.");
  const response = await page.request.get(backend + endpoint, { headers });
  const rawBody = await response.text();
  result.evidence.push(retain(name + "-http", { url: diagnosticUrl(response.url()), status: response.status(), rawBody }));
  need(response.status() === 200, "Owning resource read failed: " + response.status() + " " + endpoint);
  const value = JSON.parse(rawBody);
  const receipt = retain(name, { url: response.url(), status: response.status(), value });
  result.evidence.push(receipt);
  return { value, receipt };
}
async function screenshot(name) {
  const file = path.join(config.output, name + ".png");
  await page.screenshot({ path: file, fullPage: true });
  result.evidence.push(ref(file));
}
async function failureDom() {
  const html = await page.locator("html").evaluate(root => {
    const copy = root.cloneNode(true);
    copy.querySelectorAll("script, style, noscript, template").forEach(element => element.remove());
    for (const element of [copy, ...copy.querySelectorAll("*")]) {
      for (const attribute of [...element.attributes]) {
        if (!/^(id|class|role|hidden|disabled|inert|open|type|title|name|tabindex|checked|aria-[\w-]+|data-testid|data-state|data-status|data-pending)$/.test(attribute.name))
          element.removeAttribute(attribute.name);
      }
      if (element.tagName === "TEXTAREA") element.textContent = "[redacted input]";
    }
    return copy.outerHTML;
  });
  result.evidence.push(retain("failure-dom", { url: diagnosticUrl(page.url()), html }));
}
async function nativeEnter(name, locator, control) {
  const interaction = { method: "keyboard", key: "Enter", control, completed: false };
  try { await locator.press("Enter"); interaction.completed = true; }
  finally { result.evidence.push(retain(name + "-interaction", interaction)); }
}
async function workingDraft(name) {
  const [download] = await Promise.all([
    page.waitForEvent("download"),
    (async () => {
      // The nonmodal dock overlaps the pointer target; native keyboard activation preserves its proposal UI.
      await nativeEnter(name + "-menu", page.getByRole("button", { name: "More workflow actions", exact: true }), "More workflow actions");
      const menu = page.getByRole("menu", { name: "More workflow actions", exact: true });
      await expect(menu.getByRole("menuitemcheckbox", { name: /^Autosave\b/ })).toBeFocused();
      result.evidence.push(retain(name + "-menu-focus", { control: "Autosave", initialFocusObserved: true }));
      await nativeEnter(name + "-export", page.getByRole("menuitem", { name: "Export JSON", exact: true }), "Export JSON");
    })()
  ]);
  const file = path.join(config.output, name + ".json");
  await download.saveAs(file);
  fs.chmodSync(file, 0o600);
  result.evidence.push(ref(file));
  const value = JSON.parse(fs.readFileSync(file, "utf8"));
  need(value.definitionId === config.workflow.definitionId && value.state && Array.isArray(value.layout), "Native working-state export does not match the selected workflow.");
  return value;
}
async function nativeValidation(name) {
  const panel = page.locator(".wf-validation");
  await expect(panel).toContainText("No validation errors");
  await expect(panel).not.toContainText("unavailable");
  result.evidence.push(retain(name, { text: await panel.innerText() }));
}
function localPath(href) {
  const url = new URL(href, backend + "/");
  need(url.origin === backend, "Discovered owner link escaped the verified backend.");
  return url.pathname + url.search;
}
function responseFor(suffix) {
  return page.waitForResponse(response => new URL(response.url()).origin === backend && response.url().endsWith(suffix) && response.request().method() === "POST");
}
async function accepted(responsePromise, name) {
  const response = await responsePromise;
  const rawBody = await response.text();
  result.evidence.push(retain(name + "-http", { url: diagnosticUrl(response.url()), status: response.status(), rawBody }));
  need(response.ok(), name + " failed HTTP " + response.status());
  const value = JSON.parse(rawBody);
  result.evidence.push(retain(name, { url: response.url(), status: response.status(), request: response.request().postDataJSON(), response: value }));
  return { response, value };
}
async function interceptAcknowledgement(mode) {
  const pattern = backend + "/_elsa/agent/sessions/*/client-invocations/*/acknowledgements";
  let resolveObservation;
  const completed = new Promise(resolve => { resolveObservation = resolve; });
  const timeout = setTimeout(() => resolveObservation({ error: new Error("No actual workflow ACK reached the interception boundary within 30 seconds.") }), 30000);
  let intercepted = false;
  const handler = async route => {
    if (intercepted || route.request().method() !== "POST") return route.continue();
    intercepted = true;
    const request = route.request();
    try {
      const originalText = request.postData(), original = request.postDataJSON();
      expect(original).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", schemaVersion: "1", data: { observed: true } } });
      result.evidence.push(retain(mode + "-original-ack", { url: request.url(), rawBody: originalText, request: original }));
      if (mode === "malformed-result") {
        const changed = structuredClone(original);
        changed.result.schemaVersion = "unsupported-witness-schema";
        const restored = structuredClone(changed); restored.result.schemaVersion = original.result.schemaVersion;
        expect(restored).toEqual(original);
        result.evidence.push(retain("malformed-result-forwarded-ack", { url: request.url(), request: changed, changedPaths: ["result.schemaVersion"] }));
        await route.continue({ postData: JSON.stringify(changed) });
        clearTimeout(timeout); resolveObservation({ url: request.url(), original, originalText, changed });
      } else {
        // Forward the actual browser ACK once, retaining the server's genuine response before losing delivery.
        const response = await route.fetch();
        const rawBody = await response.text();
        result.evidence.push(retain("lost-confirmation-accepted-http", { url: request.url(), status: response.status(), rawBody }));
        const value = JSON.parse(rawBody);
        need(response.status() === 200, "The ACK whose delivery is lost must actually be accepted by the native server.");
        expect(value.data).toMatchObject({ executed: true, status: "executed", toolResult: { succeeded: true, receipt: { scope: "clientWorkingState" } } });
        await route.abort("failed");
        clearTimeout(timeout); resolveObservation({ url: request.url(), original, originalText, value, responseLost: true });
      }
    } catch (error) {
      result.evidence.push(retain(mode + "-interception-failure", { type: error.name, message: error.message }));
      try { await route.abort("failed"); } catch { /* Preserve the first diagnostic. */ }
      clearTimeout(timeout); resolveObservation({ error });
    }
  };
  await page.route(pattern, handler);
  return { completed, dispose: () => { clearTimeout(timeout); return page.unroute(pattern, handler); } };
}
function observedLifecycle(ack, status, summary) {
  expect(ack.value).toMatchObject({ error: null, data: { executed: false, status, summary, toolResult: {
    succeeded: false, content: summary, payload: { typeId: "agent.operation-outcome", schemaVersion: "1", data: { status } }, receipt: null
  } } });
}
async function proposalFor(action, name) {
  const message = responseFor("/messages");
  await action();
  const sent = await accepted(message, name + "-message");
  const proposal = dock().locator(".agent-proposal").last();
  await expect(proposal).toHaveAttribute("data-status", "awaiting-approval");
  await expect(proposal.getByRole("button", { name: "Approve", exact: true })).toBeEnabled();
  result.evidence.push(retain(name + "-review", { text: await proposal.innerText(), status: await proposal.getAttribute("data-status") }));
  return { proposal, sent };
}
async function approve(proposal, name) {
  const response = responseFor("/approve");
  await proposal.getByRole("button", { name: "Approve", exact: true }).click();
  const decision = await accepted(response, name + "-approve");
  await expect(proposal).toHaveAttribute("data-status", "approved");
  return decision;
}
async function sendTool(toolName, args, name) {
  command(toolName, args);
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await dock().getByLabel("Ask Weaver", { exact: true }).fill(JSON.stringify({ toolName, arguments: args }));
  return proposalFor(() => dock().getByRole("button", { name: "Send message", exact: true }).click(), name);
}
function unrelated(value) {
  // Native revision/UpdatedAt are owner metadata. Compare the entire authored JSON,
  // retaining namespace/schema identity, while leaving that metadata in the receipts.
  const copy = { namespace: value.namespace, schemaVersion: value.schemaVersion, value: structuredClone(value.value) };
  need(copy.value && Array.isArray(copy.value.widgets), "Owner did not return the native Home layout document.");
  const widget = copy.value.widgets.filter(item => item.id === witness.widgetId);
  need(widget.length === 1, "Owner must contain one explicit witness widget.");
  // The owned widget visibility is the only authored field this operation changes.
  delete widget[0].visible;
  return copy;
}
function canonicalLayout(value) {
  return value.map(row => ({ ...row, width: row.width ?? null, height: row.height ?? null, additionalProperties: row.additionalProperties ?? null }));
}
async function resource() {
  await page.goto(studio + witness.route);
  await expect(page.getByTestId("acme-home-revision")).toBeVisible();
  const before = await owner("owner-before", witness.ownerEndpoint);
  const layoutBefore = await owner("layout-before", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId });
  result.ownerBefore = before.receipt;
  await page.goto(studio + "/dashboard");
  if (before.value.visible) await expect(page.getByTestId("acme-home-widget")).toBeVisible();
  else await expect(page.getByTestId("acme-home-widget")).toHaveCount(0);
  await screenshot("home-before");
  await page.goto(studio + witness.route);
  await expect(page.getByTestId("acme-home-revision")).toHaveText(before.value.revision);
  command(witness.readTool, {});
  const readMessage = responseFor("/messages");
  await page.getByRole("button", { name: "Read with Weaver", exact: true }).click();
  const read = await accepted(readMessage, "read-message");
  const readResult = dock().getByRole("region", { name: "Module results", exact: true }).getByTestId("acme-home-result");
  await expect(readResult).toContainText(`The owning resource reports ${before.value.visible ? "visible" : "hidden"}.`);
  await expect(readResult.getByTestId("acme-home-result-revision")).toHaveText(before.value.revision);
  result.evidence.push(retain("resource-read-rendering", { text: await readResult.innerText(),
    revision: await readResult.getByTestId("acme-home-result-revision").textContent(), owner: before.receipt }));
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  expect((await owner("owner-after-read", witness.ownerEndpoint)).value).toEqual(before.value);
  const next = !before.value.visible;
  command(witness.writeTool, { visible: next });
  await page.getByLabel("Show Acme status card on Home", { exact: true }).setChecked(next);
  const { proposal, sent } = await proposalFor(() => page.getByRole("button", { name: "Review visibility change", exact: true }).click(), "resource");
  const body = sent.response.request().postDataJSON();
  expect(body.contextAttachments).toEqual(expect.arrayContaining([expect.objectContaining({ moduleId: witness.moduleId, contentType: witness.contextType,
    target: expect.objectContaining({ resourceId: witness.widgetId, revision: before.value.revision }) })]));
  result.observations.context = true;
  result.observations.read = true;
  await expect(proposal.getByTestId("acme-home-result")).toContainText("only after approval and application");
  expect((await owner("owner-at-review", witness.ownerEndpoint)).value).toEqual(before.value);
  result.observations.review = true;
  if (config.journey === "deny-before-effect") {
    const deny = responseFor("/deny");
    await proposal.getByRole("button", { name: "Deny", exact: true }).click();
    await accepted(deny, "deny");
    await expect(proposal).toHaveAttribute("data-status", "denied");
    await expect(proposal.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
    const after = await owner("owner-after", witness.ownerEndpoint);
    expect(after.value).toEqual(before.value);
    result.ownerAfter = after.receipt;
    result.observations = { ownerObserved: true, noFalseSuccess: true, noReplay: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "review", diagnostic: "denied" };
    result.status = "failed"; // Expected refusal remains a failed child, never converted to a passed mutation.
    await screenshot("denied");
    return;
  }
  await approve(proposal, "resource");
  expect((await owner("owner-after-approval", witness.ownerEndpoint)).value).toEqual(before.value);
  const executed = responseFor("/execute");
  await proposal.getByRole("button", { name: "Apply", exact: true }).click();
  const execution = await accepted(executed, "resource-execute");
  const receipt = execution.value.data?.toolResult?.receipt;
  expect(receipt).toMatchObject({ scope: "serverPersisted", before: { resourceId: witness.widgetId, revision: before.value.revision }, after: { resourceId: witness.widgetId } });
  await expect(proposal).toHaveAttribute("data-status", "executed");
  await expect(proposal.getByTestId("acme-home-result")).toContainText("independently confirmed the saved visibility");
  const after = await owner("owner-after", witness.ownerEndpoint);
  expect(after.value.visible).toBe(next);
  expect(after.value.revision).not.toBe(before.value.revision);
  expect(after.value.revision).toBe(receipt.after.revision);
  const appliedResult = proposal.getByTestId("acme-home-result");
  await expect(appliedResult).toContainText(`to ${after.value.visible ? "visible" : "hidden"}.`);
  await expect(appliedResult.getByTestId("acme-home-result-revision")).toHaveText(after.value.revision);
  result.ownerAfter = after.receipt;
  const layoutAfter = await owner("layout-after", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId });
  expect(unrelated(layoutAfter.value)).toEqual(unrelated(layoutBefore.value));
  result.observations.ownerReadback = true;
  result.observations.unrelatedPreserved = true;
  if (config.journey === "duplicate-execute") {
    const repeated = await page.request.post(execution.response.url(), { data: execution.response.request().postDataJSON() });
    need(repeated.ok(), "Duplicate execute did not return its retained result.");
    const repeatedBody = await repeated.json();
    expect(repeatedBody.data.toolResult).toEqual(execution.value.data.toolResult);
    result.evidence.push(retain("duplicate-execute", { status: repeated.status(), response: repeatedBody }));
    expect((await owner("owner-after-duplicate", witness.ownerEndpoint)).value).toEqual(after.value);
    expect((await owner("layout-after-duplicate", "/_elsa/studio/preferences/home-layout", { "X-Elsa-Studio-Host-Id": witness.hostId })).value).toEqual(layoutAfter.value);
    result.observations = { retainedResult: true, oneMutation: true };
  }
  await screenshot("resource-applied");
  await page.goto(studio + "/dashboard");
  if (next) await expect(page.getByTestId("acme-home-widget")).toBeVisible();
  else await expect(page.getByTestId("acme-home-widget")).toHaveCount(0);
  expect((await owner("owner-after-home-reload", witness.ownerEndpoint)).value).toEqual(after.value);
  if (config.journey === "resource") result.observations.homeReload = true;
  await screenshot("home-reloaded");
  // actualAdapter is attached by the Python coordinator only after matching the protocol trace.
  result.readMessage = { url: read.response.url(), status: read.response.status() };
}
async function workflow() {
  const { definitionId, activityId, definitionPath } = config.workflow ?? {};
  need(definitionId && activityId && definitionPath, "Workflow journey needs exact owning IDs and discovered readback path.");
  await page.goto(studio + "/workflows/definitions?definition=" + encodeURIComponent(definitionId));
  // Attribute filtering avoids interpolating an arbitrary activity ID into a CSS selector.
  const activity = page.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(activityId) + "]");
  await expect(activity).toBeVisible();
  await expect(page.locator(".wf-editor-save-status")).not.toHaveAttribute("data-pending", "true");
  const before = await owner("owner-before", definitionPath);
  result.ownerBefore = before.receipt;
  const draftId = before.value.draft?.id;
  need(draftId, "The owning workflow must have an actual editable draft.");
  const capabilities = (await owner("workflow-capabilities", "/capabilities")).value;
  const links = capabilities.capabilities.filter(item => item.id === "elsa.api.workflow-design").flatMap(item => item.links);
  const draftLink = links.find(item => item.rel === "workflow-drafts")?.href;
  const validationLink = links.find(item => item.rel === "workflow-draft-validations")?.href;
  need(draftLink && validationLink, "Native draft Save and validation relations are required.");
  owningDraftPath = new URL(localPath(draftLink.replace("{draftId}", encodeURIComponent(draftId))), backend).pathname;
  const validationPath = localPath(validationLink.replace("{draftId}", encodeURIComponent(draftId)));
  const workingBefore = await workingDraft("working-before");
  await nativeValidation("validation-before");
  const position = await activity.getAttribute("style");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const args = { activityId, x: 480, y: 320 };
  need(!position?.includes("translate(480px, 320px)"), "Workflow baseline must differ from the requested position.");
  const { proposal, sent } = await sendTool("workflow_set_designer_position", args, "workflow");
  expect(sent.response.request().postDataJSON().contextAttachments).toEqual(expect.arrayContaining([expect.objectContaining({ contentType: "workflow.definition",
    target: expect.objectContaining({ resourceId: definitionId }), client: expect.objectContaining({ generationId: expect.any(String) }) })]));
  await approve(proposal, "workflow");
  if (config.journey === "client-binding-removed") {
    // Navigate with native SPA routing: the open assistant survives while the editor binding is disposed.
    await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Home", exact: true }).click();
    await expect(activity).toHaveCount(0);
    const execution = responseFor("/execute"), acknowledged = responseFor("/acknowledgements");
    await proposal.getByRole("button", { name: "Apply", exact: true }).click();
    const delivered = await accepted(execution, "removed-client-execute");
    need(delivered.response.status() === 202, "Removed-client control must reach the actual browser execution boundary.");
    const ack = await accepted(acknowledged, "removed-client-ack");
    const actualAck = ack.response.request().postDataJSON();
    expect(actualAck).toMatchObject({ outcome: "refused", summary: "The originating workflow editor is unavailable." });
    need(actualAck.result == null && actualAck.after == null, "Removed editor unexpectedly returned an applied observation.");
    observedLifecycle(ack, "refused", "The originating client refused the operation before application.");
    await expect(proposal).toHaveAttribute("data-status", "refused");
    await expect(proposal).toContainText(ack.value.data.summary);
    const after = await owner("owner-after", definitionPath);
    expect(after.value.draft).toEqual(before.value.draft);
    result.ownerAfter = after.receipt;
    expect(owningWrites).toEqual([]);
    expect(requests.filter(item => item.url.endsWith("/execute"))).toHaveLength(1);
    expect(requests.filter(item => item.url.endsWith("/acknowledgements"))).toHaveLength(1);
    result.observations = { ownerObserved: true, noFalseSuccess: true, noReplay: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "client-execution", diagnostic: actualAck.summary };
    result.status = "failed";
    return;
  }
  const special = ["malformed-result", "lost-confirmation"].includes(config.journey);
  const interception = special ? await interceptAcknowledgement(config.journey) : null;
  const executed = responseFor("/execute");
  const acknowledged = config.journey === "lost-confirmation" ? null : responseFor("/acknowledgements");
  await proposal.getByRole("button", { name: "Apply", exact: true }).click();
  const delivery = await accepted(executed, "workflow-execute");
  need(delivery.response.status() === 202, "Workflow application must be delivered to its originating browser.");
  let intercepted, ack;
  if (interception) {
    intercepted = await interception.completed;
    await interception.dispose();
    if (intercepted.error) throw intercepted.error;
    expect(intercepted.original).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", data: { ...args, observed: true } } });
  }
  if (acknowledged) ack = await accepted(acknowledged, "workflow-ack");
  if (config.journey === "malformed-result") {
    observedLifecycle(ack, "uncertain", "The reported client effect did not validate against the exact prepared operation. Its outcome is uncertain and will not be replayed.");
    await expect(proposal).toHaveAttribute("data-status", "uncertain");
    await expect(proposal).toContainText(ack.value.data.summary);
    await expect(proposal.getByRole("button", { name: "Retry confirmation", exact: true })).toHaveCount(0);
  } else if (config.journey === "lost-confirmation") {
    await expect(proposal).toHaveAttribute("data-status", "uncertain");
    await expect(proposal).toContainText("Confirmation was not received. Retry sends the same observed receipt without running the change again.");
    await expect(proposal.getByRole("button", { name: "Retry confirmation", exact: true })).toBeEnabled();
    result.evidence.push(retain("lost-confirmation-uncertain-ui", { status: await proposal.getAttribute("data-status"), text: await proposal.innerText() }));
  } else {
    expect(ack.response.request().postDataJSON()).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", data: { ...args, observed: true } } });
    expect(ack.value.data?.toolResult?.receipt?.scope).toBe("clientWorkingState");
    await expect(proposal).toHaveAttribute("data-status", "executed");
  }
  await expect(activity).toHaveAttribute("style", /translate\(480px, 320px\)/);
  const workingApplied = await workingDraft("working-applied");
  expect(workingApplied.state).toEqual(workingBefore.state);
  expect(workingApplied.activityPresentation).toEqual(workingBefore.activityPresentation);
  const appliedPosition = workingApplied.layout.find(item => item.nodeId === activityId);
  const priorPosition = workingBefore.layout.find(item => item.nodeId === activityId);
  need(priorPosition, "The workflow witness needs an explicit authored layout row.");
  expect(canonicalLayout([appliedPosition])).toEqual(canonicalLayout([{ ...priorPosition, x: args.x, y: args.y }]));
  expect(workingApplied.layout.filter(item => item.nodeId !== activityId)).toEqual(workingBefore.layout.filter(item => item.nodeId !== activityId));
  await nativeValidation("validation-after-local-apply");
  result.observations.workingStateChanged = true;
  expect((await owner("owner-after-local-apply", definitionPath)).value.draft).toEqual(before.value.draft);
  // Observe beyond the native autosave interval, retaining every attempted owning write.
  if (config.journey !== "lost-confirmation") await expect(dock()).toHaveAttribute("aria-busy", "false");
  await page.waitForTimeout(1600);
  expect((await owner("owner-after-autosave-window", definitionPath)).value.draft).toEqual(before.value.draft);
  expect(owningWrites).toEqual([]);
  result.observations.noImplicitSave = true;
  await screenshot("workflow-local");
  if (config.journey === "malformed-result") {
    const after = await owner("owner-after", definitionPath);
    expect(after.value.draft).toEqual(before.value.draft);
    expect(requests.filter(item => item.url.endsWith("/execute"))).toHaveLength(1);
    expect(requests.filter(item => item.url.endsWith("/acknowledgements"))).toHaveLength(1);
    result.ownerAfter = after.receipt;
    result.observations = { ...result.observations, ownerObserved: true, noFalseSuccess: true, noReplay: true, actualLocalApply: true, exactSchemaMutation: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "client-acknowledgement", diagnostic: ack.value.data.summary };
    result.status = "failed";
    return;
  }
  if (config.journey === "lost-confirmation") {
    const retried = responseFor("/acknowledgements");
    await proposal.getByRole("button", { name: "Retry confirmation", exact: true }).click();
    const confirmed = await accepted(retried, "confirmation-retry");
    expect(confirmed.response.url()).toBe(intercepted.url);
    expect(confirmed.response.request().postData()).toBe(intercepted.originalText);
    expect(confirmed.response.request().postDataJSON()).toEqual(intercepted.original);
    expect(confirmed.value).toEqual(intercepted.value);
    await expect(proposal).toHaveAttribute("data-status", "executed");
    expect(await workingDraft("working-after-confirmation-retry")).toEqual(workingApplied);
    await proposal.getByRole("button", { name: "Undo whole change", exact: true }).click();
    await expect(activity).toHaveAttribute("style", position);
    expect(await workingDraft("working-after-confirmation-undo")).toEqual(workingBefore);
    await nativeValidation("validation-after-confirmation-undo");
    const after = await owner("owner-after", definitionPath);
    expect(after.value.draft).toEqual(before.value.draft);
    expect(owningWrites).toEqual([]);
    expect(requests.filter(item => item.url.endsWith("/execute"))).toHaveLength(1);
    expect(requests.filter(item => item.url.endsWith("/acknowledgements"))).toHaveLength(2);
    result.ownerAfter = after.receipt;
    result.observations = { ...result.observations, ownerObserved: true, noFalseSuccess: true, noReplay: true, responseLost: true, uncertain: true,
      sameAcknowledgement: true, sameInvocation: true, retainedResult: true, wholeUndo: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "client-confirmation-delivery", diagnostic: "net::ERR_FAILED" };
    need(failedRequests.some(item => item.url === diagnosticUrl(intercepted.url) && item.errorText === result.actual.diagnostic), "The real accepted ACK response was not observed as lost browser delivery.");
    result.status = "failed";
    return;
  }
  if (config.journey === "duplicate-ack") {
    const duplicate = await page.request.post(ack.response.url(), { data: ack.response.request().postDataJSON() });
    need(duplicate.ok(), "Identical accepted ACK did not return its retained result.");
    const value = await duplicate.json();
    expect(value.data.toolResult).toEqual(ack.value.data.toolResult);
    result.evidence.push(retain("duplicate-ack", { status: duplicate.status(), response: value }));
    await expect(activity).toHaveAttribute("style", /translate\(480px, 320px\)/);
    expect((await owner("owner-after-duplicate", definitionPath)).value.draft).toEqual(before.value.draft);
    result.observations = { retainedResult: true, oneMutation: true };
    result.ownerAfter = (await owner("owner-after", definitionPath)).receipt;
    return;
  }
  await proposal.getByRole("button", { name: "Undo whole change", exact: true }).click();
  await expect(activity).toHaveAttribute("style", position);
  expect(await workingDraft("working-after-undo")).toEqual(workingBefore);
  await nativeValidation("validation-after-undo");
  expect((await owner("owner-after-undo", definitionPath)).value.draft).toEqual(before.value.draft);
  result.observations.wholeUndo = true;
  // Apply a separately reviewed second invocation before explicitly choosing the native Save action.
  const second = await sendTool("workflow_set_designer_position", args, "workflow-save");
  await approve(second.proposal, "workflow-save");
  const ackAgain = responseFor("/acknowledgements");
  await second.proposal.getByRole("button", { name: "Apply", exact: true }).click();
  await accepted(ackAgain, "workflow-save-ack");
  await expect(second.proposal).toHaveAttribute("data-status", "executed");
  expect(owningWrites).toEqual([]);
  const saved = page.waitForResponse(response => new URL(response.url()).origin === backend && response.request().method() === "PUT"
    && new URL(response.url()).pathname === owningDraftPath);
  await nativeEnter("explicit-save", page.getByRole("button", { name: "Save", exact: true }), "Save");
  const saveResponse = await saved;
  need(saveResponse.ok(), "Explicit native workflow Save failed.");
  result.evidence.push(retain("explicit-save", { status: saveResponse.status(), request: saveResponse.request().postDataJSON(), response: await saveResponse.json() }));
  const after = await owner("owner-after", definitionPath);
  need(JSON.stringify(after.value.draft) !== JSON.stringify(before.value.draft), "Explicit Save did not change the owning workflow draft.");
  expect(after.value.draft.state).toEqual(workingApplied.state);
  expect(canonicalLayout(after.value.draft.layout)).toEqual(canonicalLayout(workingApplied.layout));
  result.ownerAfter = after.receipt;
  await page.reload();
  await expect(activity).toHaveAttribute("style", /translate\(480px, 320px\)/);
  const validated = await owner("saved-draft-validations", validationPath);
  expect(validated.value).toMatchObject({ draftId, errors: [] });
  await nativeValidation("validation-after-saved-reload");
  result.observations.validated = true;
  result.observations.explicitSaveReadback = true;
  await screenshot("workflow-saved-reloaded");
}
async function rendererUnavailable() {
  const { definitionId, activityId, definitionPath, draftPath, htmlCanary } = config.workflow ?? {};
  need(definitionId && activityId && definitionPath && draftPath && typeof htmlCanary === "string" && htmlCanary.includes("<"),
    "Renderer fallback needs a real workflow with an authored markup-like activity display name.");
  await page.goto(studio + "/workflows/definitions?definition=" + encodeURIComponent(definitionId));
  await expect(page.locator(".react-flow__node").locator("xpath=self::*[@data-id=" + xpathString(activityId) + "]")).toBeVisible();
  owningDraftPath = new URL(localPath(draftPath), backend).pathname;
  const before = await owner("owner-before", definitionPath);
  result.ownerBefore = before.receipt;
  const workingBefore = await workingDraft("working-before");
  need(workingBefore.activityPresentation?.some(item => item.nodeId === activityId && item.displayName === htmlCanary),
    "The markup-like label must come from the actual owning workflow presentation.");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  const selected = command("workflow_read_graph", {});
  const message = responseFor("/messages");
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await dock().getByLabel("Ask Weaver", { exact: true }).fill("Read the open workflow graph.");
  await dock().getByRole("button", { name: "Send message", exact: true }).click();
  const sent = await accepted(message, "renderer-read-message");
  const article = dock().getByRole("region", { name: "Module results", exact: true }).locator('article[data-succeeded="true"]').last();
  const fallback = article.locator(":scope > pre");
  await expect(fallback).toBeVisible();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const text = await fallback.textContent();
  const payload = JSON.parse(text);
  expect(payload).toMatchObject({ typeId: "workflows.graph", schemaVersion: "1", data: { workflowDefinitionId: definitionId } });
  expect(payload.data.activities).toEqual(expect.arrayContaining([expect.objectContaining({ id: activityId, displayName: htmlCanary })]));
  await expect(fallback.locator("*")).toHaveCount(0);
  expect(await fallback.evaluate(element => element.childElementCount)).toBe(0);
  const composer = dock().getByLabel("Ask Weaver", { exact: true });
  await expect(composer).toBeEnabled();
  await composer.fill("Another explicit request remains available.");
  await expect(dock().getByRole("button", { name: "Send message", exact: true })).toBeEnabled();
  await composer.fill(""); // Checking the common controls does not submit a second intent.
  expect(await workingDraft("working-after")).toEqual(workingBefore);
  const after = await owner("owner-after", definitionPath);
  expect(after.value).toEqual(before.value);
  expect(owningWrites).toEqual([]);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith("/messages"))).toHaveLength(1);
  result.ownerAfter = after.receipt;
  result.evidence.push(retain("structured-fallback", { commandId: selected.commandId, toolName: selected.toolName,
    messageId: sent.value.data?.message?.id, payload, text, childElementCount: await fallback.evaluate(element => element.childElementCount),
    presentation: "native structured JSON fallback for workflows.graph" }));
  result.observations = { safeGenericPresentation: true, commonControls: true, ownerObserved: true };
  await screenshot("renderer-fallback");
}

async function incompatibleModule() {
  const expected = config.incompatibleModule;
  need(expected?.moduleId === witness.moduleId && typeof expected.packageId === "string" && expected.packageId
    && typeof expected.version === "string" && expected.version && expected.requiredSdkVersion === "999.0.0",
  "The incompatible journey requires the actually installed incompatible archive identity.");
  need(config.ownerPath === "/_elsa/studio/preferences/home-layout",
    "Observe the normal persisted owner while the resource backend extension is absent.");
  await page.goto(studio + "/diagnostics/modules");
  await expect(page.getByRole("heading", { name: "Module load status", exact: true })).toBeVisible();
  const before = await owner("owner-before", config.ownerPath, { "X-Elsa-Studio-Host-Id": witness.hostId });
  result.ownerBefore = before.receipt;
  const diagnostic = page.locator(".diagnostic-row").filter({ has: page.locator("strong", { hasText: expected.moduleId }) })
    .filter({ has: page.locator('[data-status="incompatible"]') });
  await expect(diagnostic.first()).toBeVisible();
  await expect(diagnostic.first()).toContainText(expected.requiredSdkVersion);
  const observedStatus = await diagnostic.first().locator('[data-status="incompatible"]').getAttribute("data-status");
  result.evidence.push(retain("incompatible-module-diagnostic", { expectedArchive: expected,
    url: diagnosticUrl(page.url()), status: observedStatus, text: await diagnostic.allTextContents() }));
  await screenshot("incompatible-diagnostic-before-removal");
  await page.goto(studio + witness.route);
  await expect(page.locator(".empty-state")).toContainText("No Studio route is registered for " + witness.route);
  await expect(page.getByRole("link", { name: "Acme status card", exact: true })).toHaveCount(0);
  await expect(page.locator(".acme-home-settings, .acme-home-card, .acme-home-result")).toHaveCount(0);
  await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Home", exact: true }).click();
  await expect(page.locator(".topbar h1")).toHaveText("Home");
  await expect(page.getByTestId("acme-home-widget")).toHaveCount(0);
  await page.getByRole("navigation", { name: "Main", exact: true }).getByRole("link", { name: "Workflows", exact: true }).click();
  await expect(page.locator(".topbar h1")).toContainText("Workflow");
  await expect(page.locator("main")).not.toContainText("No Studio route is registered");
  await page.getByRole("button", { name: /^Weaver/ }).click();
  await expect(dock().getByTestId("weaver-provider-status")).toBeVisible();
  await expect(dock().getByRole("button", { name: "Close assistant", exact: true })).toBeEnabled();
  await expect(dock().locator(".agent-context-chip").filter({ hasText: "Acme status card" })).toHaveCount(0);
  await expect(dock().getByTestId("acme-home-result")).toHaveCount(0);
  result.evidence.push(retain("common-studio-usable", { url: diagnosticUrl(page.url()), title: await page.locator(".topbar h1").innerText(),
    providerStatus: await dock().getByTestId("weaver-provider-status").innerText(), noPromptSubmitted: requests.length === 0 }));
  await screenshot("incompatible-common-studio-usable");
  await dock().getByRole("button", { name: "Close assistant", exact: true }).click();
  const after = await owner("owner-after", config.ownerPath, { "X-Elsa-Studio-Host-Id": witness.hostId });
  expect(after.value).toEqual(before.value);
  expect(requests).toEqual([]);
  result.ownerAfter = after.receipt;
  result.observations = { ownerObserved: true, noFalseSuccess: true, noReplay: true,
    browserContributionsAbsent: true, commonStudioUsable: true };
  result.status = "failed";
  result.expectedFailureObserved = true;
  result.actual = { stage: "module-admission", diagnostic: observedStatus };
}

async function consumedIntentRemount() {
  need(path.isAbsolute(config.traceFile ?? "") && fs.existsSync(config.traceFile), "Remount proof needs the actual selected provider trace.");
  const offset = fs.statSync(config.traceFile).size;
  const callsFor = commandId => {
    const bytes = fs.readFileSync(config.traceFile).subarray(offset);
    need(bytes.length === 0 || bytes.at(-1) === 10, "A partial provider trace cannot prove no replay.");
    return bytes.toString("utf8").split("\n").filter(Boolean).map(line => JSON.parse(line))
      .filter(row => row.kind === config.provider + ".call" && row.data.commandId === commandId)
      .map(row => ({ kind: row.kind, commandId: row.data.commandId, toolName: row.data.toolName }));
  };
  await page.goto(studio + witness.route);
  await expect(page.getByTestId("acme-home-revision")).toBeVisible();
  const before = await owner("owner-before", witness.ownerEndpoint);
  result.ownerBefore = before.receipt;
  const selected = command(witness.readTool, {});
  const message = responseFor("/messages");
  await page.getByRole("button", { name: "Read with Weaver", exact: true }).click();
  const sent = await accepted(message, "consumed-intent");
  const original = sent.response.request().postDataJSON();
  need(typeof original.requestId === "string" && original.requestId && original.source?.moduleId === witness.moduleId,
    "The module action must deliver a correlated shell intent.");
  await expect(dock().getByTestId("acme-home-result")).toContainText("The owning resource reports");
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  expect(callsFor(selected.commandId)).toHaveLength(1);
  const sessionPath = new URL(sent.response.url()).pathname.replace(/\/messages$/, "");
  const sessionBefore = await owner("session-before-remount", sessionPath);
  // Use native SPA navigation so the App retaining the consumed shell intent survives.
  await page.getByLabel("Search modules", { exact: true }).fill("Features");
  await page.getByRole("link", { name: "Features", exact: true }).click();
  await expect(page).toHaveURL(studio + "/features");
  await page.getByLabel("Search modules", { exact: true }).fill("");
  await page.getByRole("tab", { name: "Studio", exact: true }).click();
  const tokenResponse = await page.request.get(backend + "/_elsa/identity/token");
  need(tokenResponse.status() === 200, "Normal cookie-backed Studio management token exchange failed.");
  const token = (await tokenResponse.json()).accessToken;
  need(typeof token === "string" && token, "Normal token exchange returned no access token.");
  const hostRead = async endpoint => {
    const response = await page.request.get(studio + endpoint, { headers: { Authorization: "Bearer " + token } });
    need(response.status() === 200, "Native Studio observation failed HTTP " + response.status());
    return response.json();
  };
  const generationBefore = await hostRead("/__qualification/generation");
  const catalog = await hostRead("/modularity/features");
  const target = catalog.features.find(feature => feature.id === witness.studioFeature);
  need(target?.enabled === true, "The installed resource Studio feature must be enabled before its real remount transition.");
  const oldDock = await dock().elementHandle();
  need(oldDock, "The original assistant must exist before native feature application.");
  const timeOrigin = await page.evaluate(() => performance.timeOrigin);
  const moduleEvents = [];
  await page.exposeFunction("observeQualificationModulesChanged", event => moduleEvents.push(event));
  await page.evaluate(() => window.addEventListener("elsa-studio:modules-changed", event =>
    void window.observeQualificationModulesChanged({ type: event.type, timeStamp: event.timeStamp })));
  await page.getByLabel("Filter features", { exact: true }).fill(target.id);
  await nativeEnter("remount-disable-feature", page.getByRole("switch", { name: "Disable " + (target.displayName || target.id), exact: true }), "Disable resource Studio feature");
  await nativeEnter("remount-review", page.getByRole("button", { name: "Review changes", exact: true }), "Review changes");
  const dialog = page.locator('[role="dialog"][aria-label="Feature impact review"]');
  await expect(dialog).toBeVisible();
  const applied = page.waitForResponse(response => new URL(response.url()).origin === studio
    && new URL(response.url()).pathname === "/modularity/features/apply" && response.request().method() === "POST");
  const bootstrap = page.waitForResponse(response => new URL(response.url()).origin === backend
    && new URL(response.url()).pathname === "/_elsa/agent/bootstrap");
  await nativeEnter("remount-apply", dialog.getByRole("button", { name: "Apply", exact: true }), "Apply reviewed Studio feature change");
  const appliedResponse = await applied;
  need(appliedResponse.status() === 200, "The native Studio feature apply was refused.");
  const appliedValue = await appliedResponse.json();
  need(appliedValue.reloadedShellCount > 0 && appliedValue.catalog.revision !== catalog.revision,
    "Native feature apply did not report a new configuration and shell generation.");
  expect(appliedValue.catalog.features.find(feature => feature.id === target.id)?.enabled).toBe(false);
  await expect.poll(() => moduleEvents.length).toBe(1);
  await expect.poll(() => oldDock.evaluate(element => element.isConnected)).toBe(false);
  await bootstrap;
  await expect(dock().getByLabel("Ask Weaver", { exact: true })).toBeEnabled();
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  const windowStartedAt = Date.now();
  await page.waitForTimeout(1000);
  const generationAfter = await hostRead("/__qualification/generation");
  need(generationBefore.generation && generationAfter.generation !== generationBefore.generation,
    "The actual Studio serving generation did not change.");
  expect(await page.evaluate(() => performance.timeOrigin)).toBe(timeOrigin);
  expect(requests.filter(request => new URL(request.url).pathname.endsWith("/messages"))).toHaveLength(1);
  expect(callsFor(selected.commandId)).toHaveLength(1);
  const sessionAfter = await owner("session-after-remount", sessionPath);
  const userMessages = session => session.value.data.messages.filter(message => String(message.role).toLowerCase() === "user")
    .map(message => ({ id: message.id, requestId: message.requestId, source: message.source, content: message.content }));
  expect(userMessages(sessionAfter)).toEqual(userMessages(sessionBefore));
  const after = await owner("owner-after", witness.ownerEndpoint);
  expect(after.value).toEqual(before.value);
  result.ownerAfter = after.receipt;
  result.evidence.push(retain("native-intent-remount", { nativeOperation: "Studio feature apply", featureId: target.id,
    enabledBefore: target.enabled, enabledAfter: false, catalogRevisionBefore: catalog.revision,
    catalogRevisionAfter: appliedValue.catalog.revision, reloadedShellCount: appliedValue.reloadedShellCount,
    generationBefore, generationAfter, moduleEvents, originalRequestId: original.requestId, originalSource: original.source,
    commandId: selected.commandId, providerCalls: callsFor(selected.commandId), observationWindowMilliseconds: Date.now() - windowStartedAt }));
  result.observations = { nativeReconciliation: true, noResubmission: true };
  await screenshot("native-remount-without-replay");
}

function xpathString(value) {
  need(typeof value === "string" && !value.includes("'"), "Workflow ID must be a supported native identifier.");
  return "'" + value + "'";
}
try {
  if (config.journey === "sdk-incompatible") await incompatibleModule();
  else if (config.journey === "renderer-unavailable") await rendererUnavailable();
  else if (config.journey === "consumed-intent-remount") await consumedIntentRemount();
  else if (["workflow", "duplicate-ack", "client-binding-removed", "malformed-result", "lost-confirmation"].includes(config.journey)) await workflow();
  else await resource();
  need(pageErrors.length === 0, "Studio raised browser errors: " + pageErrors.join("; "));
  if (!result.expectedFailureObserved) result.status = "passed";
} catch (error) {
  result.status = "failed";
  result.failure = redact({ type: error.name, message: error.message });
  try { await failureDom(); } catch (diagnosticError) {
    result.evidence.push(retain("failure-dom-unavailable", { type: diagnosticError.name, message: diagnosticError.message }));
  }
  try { await screenshot("failure"); } catch { /* Retain the original failure if the browser has gone away. */ }
} finally {
  result.completedAt = new Date().toISOString();
  result.evidence.push(retain("agent-streams", await streamCapture()));
  await Promise.all(pendingOwnerReads);
  result.evidence.push(retain("resource-dispatch-boundaries", { resourceInteractions, moduleOwnerReads }));
  result.evidence.push(retain("agent-requests", requests));
  result.evidence.push(retain("owning-write-attempts", owningWrites));
  result.evidence.push(retain("browser-diagnostics", { consoleMessages, failedRequests }));
  result.pageErrors = redact(pageErrors);
  retain("observation", result);
  await context.close();
  await browser.close();
}
console.log(JSON.stringify({ status: result.status, journey: result.journey, expectedFailureObserved: !!result.expectedFailureObserved,
  observations: result.observations, failure: result.failure, evidence: path.join(config.output, "observation.json") }));
if (result.failure || result.status !== "passed" && !result.expectedFailureObserved) process.exitCode = 1;
