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
const supported = new Set(["resource", "workflow", "duplicate-execute", "duplicate-ack", "deny-before-effect", "client-binding-removed"]);
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
page.setDefaultTimeout(30000);
const requests = [], owningWrites = [], pageErrors = [], consoleMessages = [], failedRequests = [];
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
const dock = () => page.getByRole("region", { name: "Weaver assistant", exact: true });
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
  need(response.ok(), name + " failed HTTP " + response.status());
  const value = await response.json();
  result.evidence.push(retain(name, { url: response.url(), status: response.status(), request: response.request().postDataJSON(), response: value }));
  return { response, value };
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
  command(witness.readTool, {});
  const readMessage = responseFor("/messages");
  await page.getByRole("button", { name: "Read with Weaver", exact: true }).click();
  const read = await accepted(readMessage, "read-message");
  await expect(dock().getByRole("region", { name: "Module results", exact: true }).getByTestId("acme-home-result")).toContainText("The owning resource reports");
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
    const execution = responseFor("/execute");
    await proposal.getByRole("button", { name: "Apply", exact: true }).click();
    const delivered = await execution;
    result.evidence.push(retain("removed-client-execute", { status: delivered.status(), response: await delivered.json() }));
    await expect(proposal).not.toHaveAttribute("data-status", "executed");
    await expect(proposal).toContainText(/originating|unavailable|refused/i);
    const after = await owner("owner-after", definitionPath);
    expect(after.value.draft).toEqual(before.value.draft);
    result.ownerAfter = after.receipt;
    result.observations = { ownerObserved: true, noFalseSuccess: true, noReplay: true };
    result.expectedFailureObserved = true;
    result.actual = { stage: "client-execution", diagnostic: "originating-client-unavailable" };
    result.status = "failed";
    return;
  }
  const executed = responseFor("/execute"), acknowledged = responseFor("/acknowledgements");
  await proposal.getByRole("button", { name: "Apply", exact: true }).click();
  const delivery = await accepted(executed, "workflow-execute");
  need(delivery.response.status() === 202, "Workflow application must be delivered to its originating browser.");
  const ack = await accepted(acknowledged, "workflow-ack");
  expect(ack.response.request().postDataJSON()).toMatchObject({ outcome: "applied", result: { typeId: "workflows.layout-change", data: { ...args, observed: true } } });
  expect(ack.value.data?.toolResult?.receipt?.scope).toBe("clientWorkingState");
  await expect(proposal).toHaveAttribute("data-status", "executed");
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
  await expect(dock()).toHaveAttribute("aria-busy", "false");
  await page.waitForTimeout(1600);
  expect((await owner("owner-after-autosave-window", definitionPath)).value.draft).toEqual(before.value.draft);
  expect(owningWrites).toEqual([]);
  result.observations.noImplicitSave = true;
  await screenshot("workflow-local");
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
function xpathString(value) {
  need(typeof value === "string" && !value.includes("'"), "Workflow ID must be a supported native identifier.");
  return "'" + value + "'";
}
try {
  if (["workflow", "duplicate-ack", "client-binding-removed"].includes(config.journey)) await workflow();
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
