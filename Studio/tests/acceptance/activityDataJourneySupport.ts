import { expect, type Page, type TestInfo } from "@playwright/test";

export type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
export type Binding = { expressionType: string; value: unknown };
export type Node = { nodeId: string; activityVersionId: string; inputs: { referenceKey: string; value: Binding }[]; outputs: unknown[];
  structure?: { kind: string; schemaVersion: string; payload: { activities: Node[]; variables?: unknown[]; [key: string]: unknown } } };
export type State = { rootActivity: Node; variables: unknown[]; inputs: unknown[]; outputs: unknown[] };
export type Created = { definition: { id: string }; draftId: string };
type Draft = { state: State; layout: unknown; activityPresentation: unknown };
type Context = { request: { definitionId: string; nodeId: string; inputKey: string; documentRevision: string; query?: { source?: Binding; memberPath?: string[] }; snapshot: State };
  response: { state: string; documentRevision: string; analysis?: { memberReadsEnabled?: boolean; sources: { binding: Binding | null; shape?: { state: string; kind: string } }[] } } };
type Execution = { activityExecutionId: string; authoredActivityId: string; status: string };

/** Real composed-host support. Setup uses REST; every authored selection is made through Studio, from an observed offer. */
export async function activityDataSupport(page: Page, info: TestInfo, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned Workbench URL").toBeTruthy();
  const composition = process.env.UX_ACTIVITY_DATA_COMPOSITION ?? "normal";
  const contexts: Context[] = [], pending: Promise<void>[] = [], scripts: string[] = [], definitions: string[] = [];
  const selections: { definitionId: string; binding: Binding; offered: Binding; documentRevision: string }[] = [];
  const runtime: { definitionId: string; workflowExecutionId: string; expected: string; committed: string; consumer: Execution; producers: Execution[] }[] = [];
  const observations: string[] = [];
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
      composition, contexts, selections, runtime, observations, scripts, cleanup }, null, 2), contentType: "application/json" });
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
    const before = contexts.length;
    await action(); await expect(chooser()).toHaveCount(0); await Promise.all(pending);
    // Scalar clicks and "Use this value" request fresh admission. Capture that request's offer,
    // rather than a later provenance refresh or a locally fabricated binding.
    const answer = contexts.slice(before).find(item => item.request.definitionId === created.definition.id && item.request.nodeId === "consumer" && item.request.inputKey === "text" &&
      item.response.analysis?.sources.some(source => source.binding?.expressionType === expected.expressionType && JSON.stringify(source.binding.value) === JSON.stringify(expected.value)));
    const offer = answer?.response.analysis?.sources.find(source => source.binding?.expressionType === expected.expressionType && JSON.stringify(source.binding.value) === JSON.stringify(expected.value))?.binding;
    expect(offer, "Exact real server offer belonging to the committing request").toEqual(expected);
    expect(answer!.response.documentRevision).toBe(answer!.request.documentRevision);
    await expect.poll(() => stored(created)).toEqual(offer);
    selections.push({ definitionId: created.definition.id, binding: (await stored(created))!, offered: offer!, documentRevision: answer!.request.documentRevision });
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
      await page.getByRole("button", { name: /^Text expression syntax/ }).click();
      const choices = await page.getByRole("listbox").getByRole("option").allTextContents();
      expect(choices.some(text => /JavaScript|Liquid/i.test(text))).toBe(false); await page.keyboard.press("Escape");
      expect(scripts.filter(url => /expressioneditors?\.(javascript|liquid)|javascript.expression|liquid.expression/i.test(url))).toEqual([]);
      observations.push("minimal installed syntax menu and requested scripts exclude optional JavaScript/Liquid editors");
    }
  }
  return { api, version, line, root, seed, saved, stored, binding, canvas, select, open, chooser, trigger, replace, reference, memberName, path,
    commit, history, codeApply, publishRun, assertComposition, contexts, observations, selections, writes: () => writes };
}
