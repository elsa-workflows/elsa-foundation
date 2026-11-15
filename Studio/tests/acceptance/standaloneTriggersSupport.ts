import { existsSync, readFileSync, renameSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { randomUUID } from "node:crypto";
import { expect, type APIResponse, type Page, type Response, type TestInfo } from "@playwright/test";
import type { ChangeResult, DeliveryView, InputMapping, SubscriptionView, TriggerCommand, TriggerView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/standalone-triggers/types";

export type Authored = { definition: { id: string; name: string }; draftId: string; version: { id: string } };
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
  setup?: { sourceOwnedTriggerId: string; composition?: { processId: number; sourceSha: string; shape: string; runtimeProcessor: boolean; ingressFactory: boolean; deliveryStore: boolean; designStore: boolean; services: string[] }; departure?: { creator: string; membershipStatus: string; triggerId: string; triggerRevision: number; grantRevision: number; grantState: string; resourceUnchanged: boolean } };
  boundary?: DispatchBoundary | { previous: DispatchBoundary | null; peer: DispatchBoundary | null };
  recovery?: { previousNodeIdentity: string; nodeIdentity: string; killedAt: string; stopSignal: string; naturalLeaseWaitSeconds: number; leaseMutated: boolean; clockChanged: boolean; previousBoundary: DispatchBoundary; peerBoundary?: DispatchBoundary | null };
  scopePeer?: { url: string; processId: number; runtimeAndDesignStoresIsolated: boolean; identityStoreSharedWithOwnedPrimary: boolean; sourceSha: string };
  concurrent?: { primaryProcessId: number | null; peerProcessId: number | null; primaryAlive: boolean; peerAlive: boolean; peerUrl: string | null;
    sourceSha: string; runtimeAndDesignStoresShared: boolean; primaryBoundary: DispatchBoundary | null; peerBoundary: DispatchBoundary | null };
  fault?: TriggerFault;
};
export type TriggerFault = { processId: number; nodeIdentity: string; bootId: string; sourceRevision: string; triggerId: string;
  faultMode: "SourceWriteRefused" | "DispatchShed" | "ResponseWriteFailed"; controlledPublicPortFault: boolean; physicalSourcePresent: boolean;
  deliveryId: string; sourceFingerprint: string; sourceKey?: string; recipientId?: string; subscriptionId?: string; plannedExecutionId?: string; sealFingerprint?: string;
  recordedAt: string; claimExpiresAt?: string; cohortRecipients: { recipientId: string; plannedExecutionId: string; capturedTarget: Record<string, unknown> }[] };
export type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; intrinsic?: unknown; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
export type WorkflowState = { rootActivity: Node; inputs: ReturnType<typeof input>[]; outputs: unknown[]; variables: unknown[] };
export const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
export const input = (name: string, alias = "String", required = true) => ({ referenceKey: name, name, displayName: name, type: { alias, collectionKind: "Single" }, isNullable: !required, isRequired: required, storageDriverType: null });
export const configured = (inputName: string, value: unknown): InputMapping => ({ inputName, binding: { kind: "Literal", value: value as never } });
export const source = (inputName: string, path: string[]): InputMapping => ({ inputName, binding: { kind: "SourceField", path } });
export const triggerPath = (id: string) => `/workflows/triggers/${encodeURIComponent(id)}?backend=acceptance-primary`;
export const definitionPath = (id: string) => `/workflows/definitions?definition=${encodeURIComponent(id)}&backend=acceptance-primary`;
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
  readonly profileLimitations: { state: string; reason: string; qualificationScope: string; evidence: string[] }[] = [];
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
    await expect(this.page.getByText("Loading Studio modules…", { exact: true })).toHaveCount(0);
    if (name === "workflow-live-triggers" || name === "bpmn-workflow-live-triggers") {
      const associations = this.page.getByRole("region", { name: "Workflow subscriptions", exact: true });
      await associations.scrollIntoViewIfNeeded();
      const row = associations.getByRole("row").nth(1);
      await expect(row.getByRole("button", { name: "Edit", exact: true })).toBeInViewport();
      await expect(row.getByRole("button", { name: "Remove", exact: true })).toBeInViewport();
    }
    if (name === "subscription-runtime-targets" || name === "trigger-runtime-only-composition") {
      const selector = this.page.getByLabel("Workflow and publication slot", { exact: true });
      await selector.scrollIntoViewIfNeeded(); await expect(selector).toBeInViewport();
    }
    if (name === "subscription-workflow-search") {
      const results = this.page.getByRole("listbox", { name: "Workflow search results", exact: true });
      await results.scrollIntoViewIfNeeded(); await expect(results).toBeInViewport();
    }
    if (name === "trigger-details") {
      const edit = this.page.getByRole("button", { name: "Edit configuration", exact: true });
      await expect(edit).toBeVisible(); await edit.scrollIntoViewIfNeeded();
    }
    if (name === "trigger-no-subscribers") {
      const empty = this.page.getByRole("heading", { name: "No live associations", exact: true });
      await empty.scrollIntoViewIfNeeded(); await expect(empty).toBeInViewport();
      await expect(this.page.getByRole("button", { name: "Add workflow", exact: true })).toBeInViewport();
    }
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
        appearance: identityPage ? "host-controlled" : document.documentElement.dataset.themeAppearance,
        workspaceStyle: document.documentElement.dataset.workspaceStyle };
    }) });
  }
  gap(state: string, reason: string) { this.unverified.push({ state, reason }); }
  async host(action: "pause" | "resume" | "stop" | "start" | "restart" | "setup-access" | "cleanup-access" | "depart-creator" | "design-only" | "runtime-only" | "combined-probe" | "without-schedule-module" | "without-trigger-capability" | "unsupported-trigger-contract" | "restore-features" | "without-optional-editor" | "restore-studio-features" | "arm-dispatch-boundary" | "await-dispatch-boundary" | "recover-on-peer" | "inspect-dispatch-boundary" | "start-scope-peer" | "stop-scope-peer" | "arm-trigger-fault" | "inspect-trigger-fault" | "release-trigger-fault" | "start-concurrent-node" | "inspect-concurrent-node" | "release-dispatch-boundary" | "stop-concurrent-node", options?: { triggerId?: string; faultMode?: "SourceWriteRefused" | "DispatchShed" | "ResponseWriteFailed"; subscriptionId?: string; requestPath?: string }) {
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
      journey: this.info.title, project: this.info.project.name, requests: this.requests, captures: this.captures, observed: this.observed, unverified: this.unverified, profileLimitations: this.profileLimitations,
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
      intrinsic: { kind: "setOutput", valueType: { alias: declaration.type.alias, collectionKind: declaration.type.collectionKind.toLowerCase() } },
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
    await this.page.goto("/workflows/triggers/new?backend=acceptance-primary");
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
  async openWorkflowTriggers() {
    // The workspace also has a Triggers tab. Wait for the actual editor and select its contributed inspector tab.
    const inspector = this.page.getByRole("complementary", { name: "Inspector panel", exact: true });
    await expect(inspector).toBeVisible({ timeout: 45_000 });
    const expand = inspector.getByRole("button", { name: "Expand inspector panel", exact: true });
    if (await expand.isVisible()) await expand.click();
    const tab = inspector.getByRole("tablist", { name: "Inspector panel tabs", exact: true }).getByRole("tab", { name: "Triggers", exact: true });
    await tab.click(); await expect(tab).toHaveAttribute("aria-selected", "true");
    await expect(inspector.getByRole("region", { name: "Live workflow subscriptions", exact: true })).toBeVisible();
  }
  async chooseTarget(definitionId: string, viaRuntime = false, capture?: string) {
    if (viaRuntime) {
      const matchesPage = (response: Response, after?: string) => {
        const url = new URL(response.url());
        return response.request().method() === "GET" && url.origin === new URL(this.base).origin &&
          url.pathname === "/runtime/standalone-trigger-targets" && url.searchParams.get("search") === definitionId &&
          url.searchParams.get("after") === (after ?? null);
      };
      await this.page.getByText("Browse published Runtime slots", { exact: true }).click();
      await this.page.getByLabel("Search subscription targets", { exact: true }).fill(definitionId);
      // The mount's unfiltered request may still be in flight. Only this exact submitted query can satisfy the waiter.
      const targetPage = this.page.waitForResponse(response => matchesPage(response));
      await this.page.getByRole("button", { name: "Search workflows", exact: true }).click();
      let response = await targetPage;
      const selector = this.page.getByLabel("Workflow and publication slot", { exact: true });
      const desired = JSON.stringify({ definitionId, slotName: "default" });
      let pages = 0;
      while (true) {
        expect(response.ok(), `Published target discovery: ${response.status()} ${await response.text()}`).toBe(true);
        const current = await response.json() as { items: { definitionId: string; slotName: string }[]; next?: string };
        const values = [JSON.stringify({ definitionId: "", slotName: "default" }),
          ...current.items.map(item => JSON.stringify({ definitionId: item.definitionId, slotName: item.slotName }))];
        await expect.poll(() => selector.locator("option").evaluateAll(options => options.map(option => (option as HTMLOptionElement).value))).toEqual(values);
        pages++;
        if (values.includes(desired)) break;
        expect(current.next, "The selected published workflow must remain reachable through the visible target pages").toBeTruthy();
        expect(pages, "Target selection stays a bounded actual UI traversal").toBeLessThan(50);
        const nextPage = this.page.waitForResponse(candidate => matchesPage(candidate, current.next));
        await this.page.getByRole("button", { name: "Next workflow targets", exact: true }).click();
        response = await nextPage;
      }
      await selector.selectOption(desired, { timeout: 5000 });
      this.requests.push({ kind: "actual-target-picker-pagination", definitionId, slotName: "default", pages });
      return;
    }
    const details = await this.api<{ definition: { id: string; name: string } }>(`/design/workflows/definitions/${encodeURIComponent(definitionId)}`);
    expect(details.definition.id).toBe(definitionId);
    const name = details.definition.name.trim(); expect(name).not.toBe("");
    const searchResponse = this.page.waitForResponse(response => response.request().method() === "GET" &&
      new URL(response.url()).origin === new URL(this.base).origin && new URL(response.url()).searchParams.get("searchTerm") === name);
    await this.page.getByRole("combobox", { name: "Workflow", exact: true }).fill(name);
    const response = await searchResponse; expect(response.ok(), await response.text()).toBe(true);
    const results = this.page.getByRole("listbox", { name: "Workflow search results", exact: true });
    let pages = 1;
    while (true) {
      await expect(results).toHaveAttribute("aria-busy", "false");
      const option = results.getByRole("option").filter({ hasText: definitionId });
      if (await option.count()) { await expect(option).toHaveCount(1); if (capture) await this.capture(capture); await option.click(); break; }
      const next = this.page.getByRole("button", { name: "Next workflow definitions", exact: true });
      await expect(next, "The selected authorized workflow is reachable through Design name search").toBeVisible();
      expect(pages++).toBeLessThan(50); await next.click();
    }
    await expect(this.page.getByRole("combobox", { name: "Workflow", exact: true })).toHaveValue(name);
    await expect(this.page.getByLabel(`Workflow definition ID: ${definitionId}`, { exact: true })).toBeVisible();
    this.requests.push({ kind: "actual-workflow-name-selection", definitionId, name, pages, slotName: "default", ordinaryDesignAuthorization: true });
  }
  async add(id: string, definitionId: string, mappings: InputMapping[], fromWorkflow = false, capture?: string) {
    const previousIds = new Set((await this.subscriptions(id)).map(item => item.id));
    if (fromWorkflow) {
      await this.page.goto(definitionPath(definitionId));
      await this.openWorkflowTriggers();
      await this.page.getByRole("button", { name: "Add trigger", exact: true }).click();
      const trigger = await this.read(id);
      await this.page.getByRole("region", { name: "Choose standalone trigger", exact: true }).getByRole("button", { name: trigger.name, exact: true }).click();
    } else {
      await this.open(id);
      await this.page.getByRole("button", { name: "Add workflow", exact: true }).click();
      await this.chooseTarget(definitionId);
    }
    await expect(this.page.getByRole("heading", { name: "Declared inputs", exact: true })).toBeVisible();
    for (const mapping of mappings) {
      await this.page.getByLabel(`Value source for ${mapping.inputName}`, { exact: true }).selectOption(mapping.binding.kind === "Literal" && mapping.binding.value === null ? "Null" : mapping.binding.kind);
      if (mapping.binding.kind === "Literal" && mapping.binding.value !== null) {
        const field = this.page.getByLabel(`Configured value for ${mapping.inputName}`, { exact: true });
        const description = await field.locator("xpath=ancestor::fieldset[contains(@class, 'wf-trigger-mapping')][1]").locator(":scope > p").first().textContent({ timeout: 15_000 });
        const declaredType = (description ?? "").split(" · ")[0].trim();
        const text = typeof mapping.binding.value === "string" && /^(system\.)?string$/i.test(declaredType)
          ? mapping.binding.value : JSON.stringify(mapping.binding.value);
        await field.fill(text);
      }
      if (mapping.binding.kind === "SourceField") {
        const selector = this.page.getByLabel(`Source field for ${mapping.inputName}`, { exact: true });
        const selectedPath = JSON.stringify(mapping.binding.path);
        const predefined = await selector.locator("option").evaluateAll(options => options.map(option => option.getAttribute("value")));
        if (predefined.includes(selectedPath)) await selector.selectOption(selectedPath);
        else {
          await selector.selectOption("custom");
          const pointer = mapping.binding.path.map(part => part.replace(/~/g, "~0").replace(/\//g, "~1")).join("/");
          await this.page.getByLabel(`Source path for ${mapping.inputName}`, { exact: true }).fill(`/${pointer}`);
        }
      }
    }
    for (const name of ["Authorize this shared trigger to invoke the workflow", "Enable this subscription"]) {
      const checkbox = this.page.getByRole("checkbox", { name, exact: true });
      const before = await checkbox.boundingBox(); expect(before).not.toBeNull();
      await checkbox.check(); await expect(checkbox).toBeChecked();
      const focused = await checkbox.boundingBox(); expect(focused).not.toBeNull();
      expect(focused!.width, "Focus keeps the pointer target's width stable").toBeCloseTo(before!.width, 1);
      expect(focused!.height, "Focus keeps the pointer target's height stable").toBeCloseTo(before!.height, 1);
      await checkbox.press("Space"); await expect(checkbox).not.toBeChecked();
      await checkbox.press("Space"); await expect(checkbox).toBeChecked();
    }
    if (capture) await this.capture(capture);
    await this.page.getByRole("button", { name: "Review subscription", exact: true }).press("Enter");
    const saved = await this.review(); expect(saved.disposition).toBe("Applied");
    const added = (await this.subscriptions(id)).filter(item => !previousIds.has(item.id) && item.target.definitionId === definitionId && item.target.slotName === "default");
    expect(added, "The reviewed association is persisted and readable through its ordinary API").toHaveLength(1);
    expect(added[0].enabled).toBe(true); expect(added[0].invocationGrant.state).toBe("Granted");
    return added[0];
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
  async inspectDelivery(delivery: DeliveryView, capture?: string) {
    await this.page.goto(`/workflows/triggers/deliveries/${encodeURIComponent(delivery.id)}?backend=acceptance-primary`);
    await expect(this.page.getByRole("heading", { name: "Trigger delivery", exact: true })).toBeVisible();
    await expect(this.page.getByLabel(`Delivery ID: ${delivery.id}`, { exact: true })).toBeVisible();
    await this.page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: new URL(this.page.url()).origin });
    await this.page.evaluate(() => navigator.clipboard.writeText("before-delivery-copy"));
    await this.page.getByRole("button", { name: "Copy Delivery ID", exact: true }).click();
    await expect.poll(() => this.page.evaluate(() => navigator.clipboard.readText())).toBe(delivery.id);
    this.requests.push({ kind: "delivery-exact-identity-copy", deliveryId: delivery.id, studioUrl: this.page.url() });
    if (capture) await this.capture(capture);
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
