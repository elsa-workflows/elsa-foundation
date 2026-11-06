import { existsSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { randomUUID } from "node:crypto";
import { expect, type APIResponse, type Page, type TestInfo } from "@playwright/test";
import type { ChangeResult, DeliveryView, InputMapping, SubscriptionView, TriggerCommand, TriggerView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

export type Authored = { definition: { id: string }; draftId: string; version: { id: string } };
export type Published = { artifactId: string; sourceReferenceId: string };
export type DispatchBoundary = {
  processId: number; nodeIdentity: string; bootId: string; sourceRevision: string; deliveryId: string; recipientId: string;
  plannedExecutionId: string; capturedTarget: { artifactId: string; artifactVersion: string; definitionId: string; definitionVersionId: string; artifactHash: string };
  sourceFingerprint: string; selectionFingerprint: string; sealFingerprint: string; phase: string; recordedAt: string;
  claimExpiresAt?: string; claimOwnerId?: string; claimFencingToken?: number; blockAfterFirstSeal: boolean;
  acceptanceReceipt?: { kind: string; evidenceId: string; sealFingerprint: string };
  cohortRecipients: { recipientId: string; subscriptionId: string; plannedExecutionId: string; capturedTarget: Record<string, unknown>; phase: string }[];
};
type HostReceipt = {
  action: string; previousProcessId: number | null; processId: number | null; generation: number; sourceSha: string; storePreserved: boolean;
  setup?: { sourceOwnedTriggerId: string };
  boundary?: DispatchBoundary | { previous: DispatchBoundary | null; peer: DispatchBoundary | null };
  recovery?: { previousNodeIdentity: string; nodeIdentity: string; killedAt: string; stopSignal: string; naturalLeaseWaitSeconds: number; leaseMutated: boolean; clockChanged: boolean; previousBoundary: DispatchBoundary; peerBoundary?: DispatchBoundary | null };
  scopePeer?: { url: string; processId: number; runtimeAndDesignStoresIsolated: boolean; identityStoreSharedWithOwnedPrimary: boolean; sourceSha: string };
  concurrent?: { primaryProcessId: number | null; peerProcessId: number | null; primaryAlive: boolean; peerAlive: boolean; peerUrl: string | null;
    sourceSha: string; runtimeAndDesignStoresShared: boolean; primaryBoundary: DispatchBoundary | null; peerBoundary: DispatchBoundary | null };
  fault?: TriggerFault;
};
export type TriggerFault = { processId: number; nodeIdentity: string; bootId: string; sourceRevision: string; triggerId: string;
  faultMode: "SourceWriteRefused" | "DispatchShed" | "ResponseWriteFailed"; controlledPublicPortFault: boolean; physicalSourcePresent: boolean;
  deliveryId: string; sourceFingerprint: string; recipientId?: string; subscriptionId?: string; plannedExecutionId?: string; sealFingerprint?: string;
  recordedAt: string; claimExpiresAt?: string; cohortRecipients: { recipientId: string; plannedExecutionId: string; capturedTarget: Record<string, unknown> }[] };
export type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; intrinsic?: unknown; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
export type WorkflowState = { rootActivity: Node; inputs: ReturnType<typeof input>[]; outputs: unknown[]; variables: unknown[] };
export const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
export const input = (name: string, alias = "String", required = true) => ({ referenceKey: name, name, displayName: name, type: { alias, collectionKind: "Single" }, isNullable: !required, isRequired: required, storageDriverType: null });
export const configured = (inputName: string, value: unknown): InputMapping => ({ inputName, binding: { kind: "Literal", value: value as never } });
export const source = (inputName: string, path: string[]): InputMapping => ({ inputName, binding: { kind: "SourceField", path } });
export const triggerPath = (id: string) => `/workflows/triggers/${encodeURIComponent(id)}?backend=legacy`;
export const definitionPath = (id: string) => `/workflows/definitions?definition=${encodeURIComponent(id)}&backend=legacy`;
const apiRoot = "/runtime/standalone-triggers";

function safe(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(safe);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value).map(([key, item]) => [key,
    /^(authorization|cookie|set-cookie|password|accessToken|refreshToken|token|secret)$/i.test(key) ? "[redacted]" : safe(item)]));
  return value;
}

/** A helper for these journeys, using the existing runner's real owned hosts and ordinary APIs only. */
export class TriggerJourney {
  readonly base = process.env.ELSA_WORKBENCH_URL!;
  readonly stamp: string;
  readonly definitions: string[] = [];
  readonly triggers: string[] = [];
  readonly requests: unknown[] = [];
  readonly captures: unknown[] = [];
  readonly observed: string[] = [];
  readonly unverified: { state: string; reason: string }[] = [];
  readonly hostLifecycle: HostReceipt[] = [];
  private pendingResponses: Promise<void>[] = [];
  constructor(readonly page: Page, readonly info: TestInfo) {
    if (!this.base || process.env.UX_ACCEPTANCE_OWNED !== "1" || !process.env.UX_SOURCE_SHA) throw new Error("Use the owned committed-source runner");
    this.stamp = `${info.project.name}-${randomUUID().slice(0, 8)}`;
    page.on("response", response => {
      const path = new URL(response.url()).pathname;
      if (!path.includes("/standalone-trigger")) return;
      this.pendingResponses.push((async () => {
        let result: unknown;
        try { result = safe(await response.json()); } catch { result = { body: "Non-JSON response omitted" }; }
        this.requests.push({ kind: "ui", method: response.request().method(), path, status: response.status(), result });
      })());
    });
  }
  async api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await this.page.request.fetch(`${this.base}${path}`, { method, data });
    const text = await response.text();
    let result: unknown;
    try { result = text ? JSON.parse(text) : null; } catch { result = { body: "Non-JSON response omitted" }; }
    if (!path.includes("identity/login")) this.requests.push({ kind: "setup-or-inspection", method, path, status: response.status(), request: safe(data), result: safe(result) });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text.slice(0, 1500)}`).toBe(true);
    return result as T;
  }
  async capture(name: string) {
    await expect.poll(() => this.page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), { message: `Page overflow in ${name}` }).toBe(true);
    const path = this.info.outputPath(`${name}.png`);
    await this.page.screenshot({ path, fullPage: true, animations: "disabled" });
    await this.info.attach(`${name}.png`, { path, contentType: "image/png" });
    const aria = this.info.outputPath(`${name}.aria.yml`);
    writeFileSync(aria, await this.page.locator("body").ariaSnapshot());
    await this.info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
    this.captures.push({ name, url: this.page.url(), ...await this.page.evaluate(() => {
      const identityPage = !!document.querySelector('form[action="/_elsa/identity/login"]');
      return { viewport: { width: innerWidth, height: innerHeight }, appearanceScope: identityPage ? "identity-login" : "studio",
        palette: identityPage ? "identity-login" : document.documentElement.dataset.theme,
        mode: identityPage ? "host-controlled" : document.documentElement.dataset.themeMode,
        workspaceStyle: document.documentElement.dataset.workspaceStyle };
    }) });
  }
  gap(state: string, reason: string) { this.unverified.push({ state, reason }); }
  async host(action: "pause" | "resume" | "stop" | "start" | "restart" | "setup-access" | "cleanup-access" | "without-schedule-module" | "without-trigger-capability" | "unsupported-trigger-contract" | "restore-features" | "without-optional-editor" | "restore-studio-features" | "arm-dispatch-boundary" | "await-dispatch-boundary" | "recover-on-peer" | "inspect-dispatch-boundary" | "start-scope-peer" | "stop-scope-peer" | "independent-backend-auth" | "arm-trigger-fault" | "inspect-trigger-fault" | "release-trigger-fault" | "start-concurrent-node" | "inspect-concurrent-node" | "release-dispatch-boundary" | "stop-concurrent-node", options?: { triggerId?: string; faultMode?: "SourceWriteRefused" | "DispatchShed" | "ResponseWriteFailed"; subscriptionId?: string; requestPath?: string }) {
    const directory = process.env.UX_HOST_CONTROL, token = process.env.UX_HOST_CONTROL_TOKEN;
    if (!directory || !token) throw new Error("The owned runner must provide host lifecycle control");
    const key = randomUUID(), request = join(directory, `${key}.request.json`), response = join(directory, `${key}.response.json`);
    writeFileSync(`${request}.tmp`, JSON.stringify({ token, action, ...options })); renameSync(`${request}.tmp`, request);
    await expect.poll(() => existsSync(response), { timeout: 190_000, message: `Owned Workbench ${action} completed` }).toBe(true);
    const result = JSON.parse(readFileSync(response, "utf8"));
    expect(result.ok, result.error ?? "Owned process operation succeeded").toBe(true);
    expect(result.receipt.sourceSha).toBe(process.env.UX_SOURCE_SHA);
    this.hostLifecycle.push(result.receipt); this.requests.push({ kind: "actual-owned-host-lifecycle", ...result.receipt });
    return result.receipt as typeof this.hostLifecycle[number];
  }
  async recoveryPeerReady() {
    const directory = process.env.UX_HOST_CONTROL;
    if (!directory) throw new Error("Use the owned host lifecycle runner");
    const path = join(directory, "peer-ready.json");
    await expect.poll(() => existsSync(path), { timeout: 45_000, message: "Actual peer process is ready on the preserved store" }).toBe(true);
    const receipt = JSON.parse(readFileSync(path, "utf8")) as { previousProcessId: number; processId: number; previousBootId: string; nodeIdentity: string; sourceSha: string; state: string; storePreserved: boolean };
    expect(receipt.sourceSha).toBe(process.env.UX_SOURCE_SHA); expect(receipt.processId).not.toBe(receipt.previousProcessId);
    expect(receipt.storePreserved).toBe(true); this.requests.push({ kind: "actual-peer-ready-during-natural-lease-wait", ...receipt });
    return receipt;
  }
  async finish() {
    await Promise.allSettled(this.pendingResponses);
    const path = this.info.outputPath("standalone-evidence.json");
    writeFileSync(path, JSON.stringify({ kind: "composed-source", sourceSha: process.env.UX_SOURCE_SHA, headSha: process.env.UX_PR_HEAD_SHA ?? process.env.UX_SOURCE_SHA,
      journey: this.info.title, project: this.info.project.name, requests: this.requests, captures: this.captures, observed: this.observed, unverified: this.unverified,
      visualApproval: "requires independent inspection", hostLifecycle: this.hostLifecycle,
      controlledFaults: this.hostLifecycle.some(item => item.action === "pause" || item.action === "stop" || item.action === "recover-on-peer" || item.action === "arm-trigger-fault"),
      processRestart: this.hostLifecycle.some(item => item.previousProcessId && item.processId && item.previousProcessId !== item.processId),
      sameStorePeerRecovery: this.hostLifecycle.some(item => item.action === "recover-on-peer"),
      multiNode: this.hostLifecycle.some(item => item.concurrent?.primaryAlive && item.concurrent.peerAlive && item.concurrent.runtimeAndDesignStoresShared),
      multiNodeScope: "Concurrent normal source-queue workers over shared SQLite; distributed placement is outside this witness" }, null, 2));
    await this.info.attach("standalone-evidence.json", { path, contentType: "application/json" });
  }
  async cleanup() {
    for (const id of this.triggers.reverse()) {
      const trigger = await this.read(id);
      if (trigger.desiredState !== "Deleted") await this.change(trigger, { kind: "DeleteTrigger" });
    }
    for (const id of this.definitions.reverse()) await this.api(`/design/workflows/definitions/${encodeURIComponent(id)}`, "DELETE");
  }
  async version(type: string) {
    const catalog = await this.api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const found = catalog.items.find(item => item.definition.activityTypeKey === type);
    expect(found, `Real catalog contains ${type}`).toBeTruthy(); return found!.definition.headVersionId;
  }
  async submit(name: string, state: WorkflowState): Promise<Authored> {
    const authored = await this.api<Authored>("/design/workflows/definitions/submit", "POST", { name: `${name} ${this.stamp}`, state });
    this.definitions.push(authored.definition.id); return authored;
  }
  async publish(authored: Authored): Promise<Published> { return this.api(`/publishing/workflows/${authored.version.id}/publish`, "POST", {}); }
  async echo(name: string, declarations = [input("message"), input("count", "Int32")]) {
    const activities: Node[] = declarations.map(declaration => ({ nodeId: `echo-${declaration.name}`, activityVersionId: "elsa.intrinsic.set-output@1", outputs: [],
      intrinsic: { kind: "setOutput", valueType: { alias: declaration.type.alias, collectionKind: "single" } },
      inputs: [literal("name", declaration.name), { referenceKey: "value", value: { expressionType: "WorkflowRequest", value: { memberKey: declaration.name } } }] }));
    const state: WorkflowState = { inputs: declarations, outputs: [], variables: [], rootActivity: { nodeId: "root", activityVersionId: await this.version("Elsa.Activities.Sequence.Activities.Sequence"), inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities } } } };
    const authored = await this.submit(name, state); return { authored, published: await this.publish(authored), state };
  }
  async read(id: string): Promise<TriggerView> { return this.api(`${apiRoot}/${encodeURIComponent(id)}`); }
  async subscriptions(triggerId: string): Promise<SubscriptionView[]> { return (await this.api<{ items: SubscriptionView[] }>(`/runtime/standalone-trigger-subscriptions?triggerId=${encodeURIComponent(triggerId)}&limit=100`)).items; }
  async deliveries(triggerId: string): Promise<DeliveryView[]> { return (await this.api<{ items: DeliveryView[] }>(`/runtime/standalone-trigger-deliveries?triggerId=${encodeURIComponent(triggerId)}&limit=100`)).items; }
  async change(trigger: TriggerView, command: TriggerCommand) {
    const preview = await this.api<{ previewId: string; canApply: boolean }>(`${apiRoot}/${trigger.id}/changes/preview`, "POST", { expectedRevision: trigger.revision, command });
    expect(preview.canApply, `Ordinary server impact review permits ${command.kind}`).toBe(true);
    return this.api<ChangeResult>(`${apiRoot}/${trigger.id}/changes/apply`, "POST", { requestId: randomUUID(), previewId: preview.previewId, expectedRevision: trigger.revision, command });
  }
  async review(capture?: string) {
    const apply = this.page.getByRole("button", { name: "Apply reviewed change", exact: true });
    await expect(apply).toBeEnabled();
    if (capture) await this.capture(capture);
    const response = this.page.waitForResponse(result => result.request().method() === "POST" && new URL(result.url()).pathname.endsWith("/changes/apply"));
    await apply.press("Enter"); const saved = await response;
    expect(saved.ok(), await saved.text()).toBe(true); await expect(apply).toHaveCount(0);
    return await saved.json() as ChangeResult;
  }
  async create(family: "event" | "timer" | "cron" | "http", name: string, options: { mode?: "WorkflowResponse" | "AsyncFanOut"; policy?: "RequireAll" | "AcceptAvailable"; route?: string; interval?: string } = {}) {
    await this.page.goto("/workflows/triggers/new?backend=legacy");
    await this.page.getByLabel("Trigger type", { exact: true }).selectOption(`${family}:1`);
    await this.page.getByLabel("Trigger name", { exact: true }).fill(`${name} ${this.stamp}`);
    if (family === "event") await this.page.getByLabel("Event name", { exact: true }).fill(`${name}.${this.stamp}`);
    if (family === "timer") await this.page.getByLabel("Interval", { exact: true }).fill(options.interval ?? "PT5S");
    if (family === "cron") { await this.page.getByLabel("Cron expression", { exact: true }).fill("*/10 * * * * *"); await this.page.getByLabel("Timezone", { exact: true }).fill("UTC"); }
    if (family === "http") {
      await this.page.getByLabel("Route", { exact: true }).fill(options.route ?? `acceptance/${this.stamp}/${this.triggers.length}`);
      await this.page.getByLabel("Source access", { exact: true }).selectOption("Anonymous");
      await this.page.getByLabel("HTTP response mode", { exact: true }).selectOption(options.mode ?? "AsyncFanOut");
      if (options.mode !== "WorkflowResponse") await this.page.getByLabel("HTTP admission policy", { exact: true }).selectOption(options.policy ?? "RequireAll");
    }
    const result = this.page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === apiRoot);
    await this.page.getByRole("button", { name: "Create trigger", exact: true }).press("Enter");
    const response = await result; expect(response.status(), await response.text()).toBe(201);
    const trigger = await response.json() as TriggerView; this.triggers.push(trigger.id);
    await this.page.goto(triggerPath(trigger.id)); await expect(this.page.getByRole("heading", { name: trigger.name, exact: true })).toBeVisible();
    return trigger;
  }
  async open(id: string) { await this.page.goto(triggerPath(id)); await expect(this.page.getByRole("button", { name: "Edit configuration", exact: true })).toBeVisible(); }
  async add(id: string, definitionId: string, mappings: InputMapping[], fromWorkflow = false, capture?: string) {
    if (fromWorkflow) {
      await this.page.goto(definitionPath(definitionId));
      await this.page.getByRole("tab", { name: "Triggers", exact: true }).click();
      await this.page.getByRole("button", { name: "Add trigger", exact: true }).click();
      const trigger = await this.read(id);
      await this.page.getByRole("region", { name: "Choose standalone trigger", exact: true }).getByRole("button", { name: trigger.name, exact: true }).click();
    } else {
      await this.open(id); await this.page.getByRole("button", { name: "Add workflow", exact: true }).click();
      await this.page.getByLabel("Workflow and publication slot", { exact: true }).selectOption(JSON.stringify({ definitionId, slotName: "default" }));
    }
    await expect(this.page.getByRole("heading", { name: "Declared inputs", exact: true })).toBeVisible();
    for (const mapping of mappings) {
      await this.page.getByLabel(`Value source for ${mapping.inputName}`, { exact: true }).selectOption(mapping.binding.kind === "Literal" && mapping.binding.value === null ? "Null" : mapping.binding.kind);
      if (mapping.binding.kind === "Literal" && mapping.binding.value !== null) await this.page.getByLabel(`Configured value for ${mapping.inputName}`, { exact: true }).fill(typeof mapping.binding.value === "string" ? mapping.binding.value : JSON.stringify(mapping.binding.value));
      if (mapping.binding.kind === "SourceField") await this.page.getByLabel(`Source field for ${mapping.inputName}`, { exact: true }).selectOption(JSON.stringify(mapping.binding.path));
    }
    await this.page.getByRole("checkbox", { name: "Authorize this shared trigger to invoke the workflow", exact: true }).check();
    await this.page.getByRole("checkbox", { name: "Enable this subscription", exact: true }).check();
    if (capture) await this.capture(capture);
    await this.page.getByRole("button", { name: "Review subscription", exact: true }).press("Enter");
    const saved = await this.review(); expect(saved.subscription?.target.definitionId).toBe(definitionId);
    return saved.subscription!;
  }
  async enable(id: string) { await this.open(id); await this.page.getByRole("button", { name: "Enable trigger", exact: true }).press("Enter"); await this.review(); await expect.poll(async () => (await this.read(id)).desiredState).toBe("Enabled"); }
  async event(name: string, payload: unknown = { message: "actual Event payload", count: 7 }) {
    const authored = await this.submit("Actual PublishEvent producer", { inputs: [], outputs: [], variables: [], rootActivity: { nodeId: "publish", activityVersionId: await this.version("Elsa.Activities.Primitives.Activities.PublishEvent"), outputs: [], inputs: [literal("EventName", name), literal("Payload", payload), literal("IsLocalEvent", false)] } });
    const published = await this.publish(authored);
    return this.api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
  }
  async waitDelivery(id: string, after = new Set<string>(), accepted = 1): Promise<DeliveryView> {
    let delivery: DeliveryView | undefined;
    await expect.poll(async () => { delivery = (await this.deliveries(id)).find(item => !after.has(item.id) && item.acceptedCount >= accepted); return !!delivery; }, { timeout: 45_000, message: "Actual source has a retained accepted delivery" }).toBe(true);
    return delivery!;
  }
  async inspectDelivery(delivery: DeliveryView, capture: string) {
    await this.page.goto(`/workflows/triggers/deliveries/${encodeURIComponent(delivery.id)}?backend=legacy`);
    await expect(this.page.getByRole("heading", { name: "Trigger delivery", exact: true })).toBeVisible();
    await expect(this.page.getByText(delivery.id, { exact: true }).first()).toBeVisible(); await this.capture(capture);
  }
  async outputs(executionId: string, values: Record<string, unknown>) {
    let instance: { instance: { status: string }; outputs: Record<string, { value?: { preview?: unknown; value?: unknown } }> } | undefined;
    await expect.poll(async () => { instance = await this.api(`/runtime/workflows/instances/${encodeURIComponent(executionId)}`); return instance!.instance.status; }, { timeout: 45_000 }).toBe("Completed");
    for (const [name, expected] of Object.entries(values)) {
      const output = instance!.outputs[name]?.value;
      expect(output, `Actual ${executionId} consumed ${name}`).toBeTruthy();
      expect(output!.preview ?? output!.value).toEqual(expected);
    }
    return instance!;
  }
  async started(delivery: DeliveryView) {
    let current = delivery;
    await expect.poll(async () => { current = await this.api(`/runtime/standalone-trigger-deliveries/${delivery.id}`); return current.recipients.filter(recipient => recipient.admission === "Accepted" && recipient.executionReference).length; }, { timeout: 45_000 }).toBe(delivery.acceptedCount);
    return current;
  }
  async sourceRequest(trigger: TriggerView, data: unknown, expectedStatus?: number, headers: Record<string, string> = {}): Promise<{ response: APIResponse; body: Record<string, unknown> }> {
    const route = String(trigger.configuration.path).replace(/^\/+/, "");
    const path = `/workflows/http/${route}`;
    const sourceKey = randomUUID();
    const response = await this.page.request.fetch(`${this.base}${path}`, { method: String(trigger.configuration.method), data, headers: { "Idempotency-Key": sourceKey, ...headers } });
    let body: Record<string, unknown>; try { body = await response.json(); } catch { body = { text: await response.text() }; }
    this.requests.push({ kind: "actual-http-source", method: trigger.configuration.method, path, sourceKey, request: safe(data), status: response.status(), result: safe(body) });
    if (expectedStatus !== undefined) expect(response.status(), JSON.stringify(body)).toBe(expectedStatus);
    return { response, body };
  }
}
