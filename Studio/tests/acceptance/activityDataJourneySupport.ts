import { expect, type Page, type TestInfo } from "@playwright/test";

export type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
export type Binding = { expressionType: string; value: unknown };
export type Node = { nodeId: string; activityVersionId: string; inputs: { referenceKey: string; value: Binding }[]; outputs: unknown[];
  structure?: { kind: string; schemaVersion: string; payload: { activities: Node[]; variables?: unknown[]; [key: string]: unknown } } };
export type State = { rootActivity: Node; variables: unknown[]; inputs: unknown[]; outputs: unknown[] };
export type Created = { definition: { id: string }; draftId: string };
type Draft = { state: State; layout: unknown; activityPresentation: unknown };
export type Context = { request: { definitionId: string; nodeId: string; inputKey: string; documentRevision: string; query?: { source?: Binding; memberPath?: string[] }; snapshot: State };
  response: { state: string; documentRevision: string; analysis?: { memberReadsEnabled?: boolean; sources: { binding: Binding | null; shape?: { state: string; kind: string } }[] } } };
type Execution = { activityExecutionId: string; authoredActivityId: string; status: string };

export function wireIdentity(value: unknown): string {
  return JSON.stringify(value, (_key, item) => item && typeof item === "object" && !Array.isArray(item)
    ? Object.fromEntries(Object.entries(item).sort(([a], [b]) => a.localeCompare(b))) : item);
}

/** Match the fresh admission call, never the post-edit provenance read that it triggers. */
export function findActivityDataAdmission(contexts: Context[], before: Context["request"], expected: Binding): Context | undefined {
  const memberPath = (expected.value as { memberPath?: string[] }).memberPath;
  return contexts.find(item => (item.response.state === "supported" || item.response.state === "empty") &&
    item.request.definitionId === before.definitionId && item.request.nodeId === before.nodeId && item.request.inputKey === before.inputKey &&
    item.request.documentRevision === before.documentRevision && item.response.documentRevision === before.documentRevision &&
    wireIdentity(item.request.snapshot) === wireIdentity(before.snapshot) && !!item.request.query?.source &&
    wireIdentity(item.request.query.memberPath) === wireIdentity(memberPath) &&
    !!item.response.analysis?.sources.some(source => wireIdentity(source.binding) === wireIdentity(expected)));
}

export type FeatureFact = { id: string; enabled: boolean; runtimeAvailable: boolean; dependencies: { id: string; optional: boolean }[] };
export const optionalBackendFeatures = ["HttpJavaScript", "JavaScriptActivities", "JavaScriptEndpoints", "JavaScriptExpressions",
  "JavaScriptJintEngine", "JavaScriptWorkflowsDesign", "Liquid"];

/** The live catalog's enabled bit is requested state. Dependencies come from resolved runtime descriptors. */
export function resolvedFeatureClosure(features: FeatureFact[]): string[] {
  const byId = new Map(features.map(feature => [feature.id, feature]));
  if (!features.length || byId.size !== features.length) throw new Error("Missing or ambiguous live feature catalog");
  for (const id of ["WorkflowsPublishingApi", "WorkflowDesignValidations"])
    if (!byId.get(id)?.enabled || !byId.get(id)?.runtimeAvailable) throw new Error(`Required rooted feature unavailable: ${id}`);
  const reached = new Set<string>();
  function visit(id: string) {
    if (reached.has(id)) return;
    const feature = byId.get(id);
    if (!feature?.runtimeAvailable || !Array.isArray(feature.dependencies)) throw new Error(`Required feature graph unresolved: ${id}`);
    reached.add(id);
    for (const dependency of feature.dependencies) {
      if (typeof dependency.id !== "string" || typeof dependency.optional !== "boolean") throw new Error("Malformed feature dependency");
      if (!dependency.optional) visit(dependency.id);
    }
  }
  for (const feature of features) if (feature.enabled) visit(feature.id);
  return [...reached].sort();
}

/** Real composed-host support. Setup uses REST; every authored selection is made through Studio, from an observed offer. */
export async function activityDataSupport(page: Page, info: TestInfo, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned Workbench URL").toBeTruthy();
  const composition = process.env.UX_ACTIVITY_DATA_COMPOSITION ?? "normal";
  const contexts: Context[] = [], pending: Promise<void>[] = [], scripts: string[] = [], definitions: string[] = [];
  const selections: { definitionId: string; binding: Binding; offered: Binding; admission: Context; before: Context["request"] }[] = [];
  const runtime: { definitionId: string; workflowExecutionId: string; expected: string; committed: string; consumer: Execution; producers: Execution[] }[] = [];
  const observations: string[] = [];
  let compositionObservation: { features: FeatureFact[]; mandatoryClosure: string[]; expressionTypes: string[]; syntaxChoices: string[] } | undefined;
  let writes = 0;
  page.on("response", response => {
    if (response.url().includes("/design/workflows/activity-data/context") && response.ok()) {
      pending.push((async () => { contexts.push({ request: response.request().postDataJSON(), response: await response.json() }); })());
    }
  });
  page.on("request", request => {
    if (request.resourceType() === "script") scripts.push(request.url());
    if (request.method() === "PUT" && /\/design\/workflows\/drafts\//.test(request.url())) writes++;
  });
  async function api<T>(path: string, method = "GET", data?: unknown, timeout?: number): Promise<T> {
    const response = await page.request.fetch(base + path, { method, data, timeout });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  // Registered before setup: failures and stock-timeout exits still attach partial observations and delete owned definitions.
  registerCleanup(async () => {
    const failures: unknown[] = [], cleanup: { id: string; status: string }[] = [];
    try { await Promise.all(pending); } catch (error) { failures.push(error); }
    for (const id of definitions) {
      try { await api(`/design/workflows/definitions/${id}`, "DELETE", undefined, 15_000); cleanup.push({ id, status: "deleted" }); }
      catch (error) { failures.push(error); cleanup.push({ id, status: "failed" }); }
    }
    await info.attach("activity-data-proof.json", { body: JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA,
      composition, contexts, selections, runtime, observations, scripts, cleanup, compositionObservation }, null, 2), contentType: "application/json" });
    if (failures.length) throw new AggregateError(failures, "Owned activity-data cleanup or evidence collection failed");
  });
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(key: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const entry = result.items.find(item => item.definition.activityTypeKey === key);
    expect(entry, key).toBeTruthy(); return entry!.definition.headVersionId;
  }
  const [sequenceVersion, flowchartVersion, lineVersion] = await Promise.all([
    version("Elsa.Activities.Sequence.Activities.Sequence"), version("Elsa.Activities.Flowchart.Activities.Flowchart"),
    version("Elsa.Activities.Primitives.Activities.WriteLine")
  ]);
  const line = (): Node => ({ nodeId: "consumer", activityVersionId: lineVersion, inputs: [], outputs: [] });
  const root = (activities: Node[], kind = "sequence", variables: unknown[] = []): Node => ({ nodeId: "root",
    activityVersionId: kind === "sequence" ? sequenceVersion : flowchartVersion, inputs: [], outputs: [],
    structure: { kind: `elsa.${kind}.structure`, schemaVersion: "1.0.0", payload: kind === "sequence" ? { activities, variables } : {
      activities, startNodeId: activities[0].nodeId, nodeMetadata: {}, connectionMetadata: {},
      connections: activities.slice(1).map((activity, index) => ({ id: `edge-${index}`, source: { nodeId: activities[index].nodeId, port: "Done" }, target: { nodeId: activity.nodeId } }))
    } } });
  async function seed(name: string, rootActivity: Node, inputs: unknown[] = [], variables: unknown[] = []) {
    const created = await api<Created>("/design/workflows/definitions/submit", "POST", {
      name: `Activity data ${name} ${info.project.name} ${Date.now()}`, state: { rootActivity, inputs, variables, outputs: [] }
    }); definitions.push(created.definition.id); return created;
  }
  const saved = (created: Created) => api<Draft>(`/design/workflows/drafts/${created.draftId}`);
  const binding = (draft: Draft): Binding | null => draft.state.rootActivity.structure!.payload.activities.find(node => node.nodeId === "consumer")!.inputs.find(input => input.referenceKey === "text")?.value ?? null;
  async function stored(created: Created) { return binding(await saved(created)); }
  async function canvas() {
    if (page.viewportSize()!.width <= 600) {
      const collapse = page.getByRole("button", { name: "Collapse inspector panel", exact: true });
      if (await collapse.isVisible()) await collapse.click();
    }
  }
  async function select(nodeId = "consumer") {
    await canvas();
    await page.locator(`.wf-sequence-step[data-id="${nodeId}"],.react-flow__node[data-id="${nodeId}"]`).first().press("Enter");
    if (page.viewportSize()!.width <= 600) {
      const expand = page.getByRole("button", { name: "Expand inspector panel", exact: true });
      if (await expand.isVisible()) await expand.click();
    }
    if (nodeId === "consumer") await expect(page.locator(".wf-inspector-context h3")).toHaveText("Write Line");
  }
  async function open(created: Created) {
    await page.goto(`/workflows/definitions?definition=${created.definition.id}&backend=legacy`);
    await expect(page.locator('.wf-sequence-step[data-id="consumer"],.react-flow__node[data-id="consumer"]').first()).toBeAttached();
    await select(); await expect(page.getByText("Autosaving...", { exact: true })).toHaveCount(0, { timeout: 30_000 });
  }
  const chooser = () => page.getByRole("dialog", { name: "Choose data for Text", exact: true });
  const trigger = () => page.getByRole("button", { name: "Choose data for Text", exact: true });
  const replace = () => page.getByRole("button", { name: "Replace data for Text", exact: true });
  const reference = () => page.locator(".wf-data-reference-source");
  const memberName = () => chooser().getByRole("textbox", { name: "Member name", exact: true });
  const path = () => chooser().getByRole("navigation", { name: "Member path", exact: true });
  async function commit(created: Created, expected: Binding, action: () => Promise<void>) {
    await Promise.all(pending);
    const oldBinding = await stored(created);
    const before = [...contexts].reverse().find(item => item.request.definitionId === created.definition.id && item.request.nodeId === "consumer" && item.request.inputKey === "text" &&
      wireIdentity(item.request.snapshot.rootActivity.structure!.payload.activities.find(node => node.nodeId === "consumer")!.inputs.find(input => input.referenceKey === "text")?.value ?? null) === wireIdentity(oldBinding));
    expect(before, "Current pre-edit snapshot before selection").toBeTruthy();
    const boundary = contexts.length;
    await action(); await expect(chooser()).toHaveCount(0); await Promise.all(pending);
    const admission = findActivityDataAdmission(contexts.slice(boundary), before!.request, expected);
    expect(admission, "Fresh supported pre-edit admission, not a post-edit provenance read").toBeTruthy();
    const offer = admission!.response.analysis!.sources.find(source => wireIdentity(source.binding) === wireIdentity(expected))!.binding!;
    await expect.poll(() => stored(created)).toEqual(offer);
    selections.push({ definitionId: created.definition.id, binding: (await stored(created))!, offered: offer, admission: admission!, before: before!.request });
  }
  async function history(created: Created, before: Binding | null, after: Binding | null) {
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await expect.poll(() => stored(created)).toEqual(before);
    await page.getByRole("button", { name: "Redo", exact: true }).click();
    await expect.poll(() => stored(created)).toEqual(after); await select();
  }
  async function codeApply(created: Created) {
    const before = await stored(created); await canvas();
    await page.getByRole("tab", { name: "Code", exact: true }).click();
    const code = page.getByRole("textbox", { name: "Workflow JSON", exact: true });
    await expect(code).toBeVisible(); await code.press("ControlOrMeta+End"); await code.press("Enter");
    await page.locator(".wf-code-view").getByRole("button", { name: "Apply", exact: true }).click();
    await page.getByRole("tab", { name: "Designer", exact: true }).click();
    await expect.poll(() => stored(created)).toEqual(before); await open(created);
  }
  async function publishRun(created: Created, expected: string, inputs: unknown = {}, producer = false) {
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Publish workflow", exact: true });
    await expect(dialog.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
    await dialog.getByRole("button", { name: "Publish", exact: true }).click();
    const published = page.getByRole("dialog", { name: "Published", exact: true });
    await expect(published).toBeVisible({ timeout: 60_000 }); await published.getByRole("button", { name: "Close", exact: true }).click();
    const slots = await api<{ items: { slotName: string; activeActivationId: string }[] }>(`/runtime/workflows/activation-slots/${created.definition.id}`);
    const activation = slots.items.find(slot => slot.slotName === "default")?.activeActivationId; expect(activation).toBeTruthy();
    const publication = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/publications/${activation}`);
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${publication.artifactId}/execute`, "POST", { inputs, sourceReferenceId: publication.sourceReferenceId });
    const instance = `/runtime/workflows/instances/${run.workflowExecutionId}`;
    await expect.poll(async () => (await api<{ instance: { status: string } }>(instance)).instance.status, { timeout: 60_000 }).toBe("Completed");
    const executions = await api<{ items: { execution: Execution }[] }>(`${instance}/activity-executions`);
    const producers = executions.items.filter(item => item.execution.authoredActivityId === "producer").map(item => item.execution);
    if (producer) { expect(producers).toHaveLength(1); expect(producers[0].status).toBe("Completed"); }
    const consumer = executions.items.find(item => item.execution.authoredActivityId === "consumer")!.execution;
    expect(consumer.status).toBe("Completed");
    const detail = await api<{ valueSnapshots: { inputKey: string; evidenceId: string }[] }>(`${instance}/activity-executions/${consumer.activityExecutionId}`);
    const evidence = detail.valueSnapshots.find(item => item.inputKey === "text"); expect(evidence).toBeTruthy();
    const payload = await api<{ payload: { preview: string } }>(`${instance}/activity-executions/${consumer.activityExecutionId}/value-evidence/${evidence!.evidenceId}/payload`);
    expect(payload.payload.preview).toBe(expected);
    runtime.push({ definitionId: created.definition.id, workflowExecutionId: run.workflowExecutionId, expected, committed: payload.payload.preview, consumer, producers });
  }
  async function assertComposition() {
    await Promise.all(pending);
    expect(contexts.some(item => item.response.analysis?.memberReadsEnabled === (composition !== "default-off")), "Measured member capability").toBe(true);
    if (composition === "minimal") {
      const catalog = await api<{ features: FeatureFact[] }>("/modularity/features");
      const features = catalog.features.map(({ id, enabled, runtimeAvailable, dependencies }) => ({ id, enabled, runtimeAvailable, dependencies }));
      const mandatoryClosure = resolvedFeatureClosure(features);
      expect(mandatoryClosure.filter(id => optionalBackendFeatures.includes(id)), "Optional backend features absent from actual resolved dependency closure").toEqual([]);
      const descriptors = await api<{ items: { typeName: string }[] }>("/expressions/descriptors");
      const expressionTypes = descriptors.items.map(item => item.typeName);
      expect(expressionTypes).toContain("Literal"); expect(expressionTypes.filter(name => /JavaScript|Liquid/i.test(name))).toEqual([]);
      await page.getByRole("button", { name: /^Text expression syntax/ }).click();
      const options = page.getByRole("listbox").getByRole("option"); await expect(options.first()).toBeVisible();
      const syntaxChoices = await options.allTextContents();
      expect(syntaxChoices.length).toBeGreaterThan(0); expect(syntaxChoices.some(text => /Activity output/i.test(text))).toBe(true);
      expect(syntaxChoices.some(text => /JavaScript|Liquid/i.test(text))).toBe(false); await page.keyboard.press("Escape");
      expect(scripts.filter(url => /expressioneditors?\.(javascript|liquid)|javascript.expression|liquid.expression/i.test(url))).toEqual([]);
      compositionObservation = { features, mandatoryClosure, expressionTypes, syntaxChoices };
      observations.push("minimal installed syntax menu and requested scripts exclude optional JavaScript/Liquid editors");
    }
  }
  return { api, version, line, root, seed, saved, stored, binding, canvas, select, open, chooser, trigger, replace, reference, memberName, path,
    commit, history, codeApply, publishRun, assertComposition, contexts, observations, selections, writes: () => writes };
}
