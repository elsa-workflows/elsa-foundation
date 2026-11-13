import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Run = { instance: { status: string }; activities: Array<{ executableNodeId: string; activityExecutionId: string; startedAt: string }> };
type Occurrence = { id: string; value: string };

/** Normal hosts and real three-iteration evidence; no intercepted routes or synthetic execution reads. */
export async function executionNavigationJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  expect(backend, "The runner owns a disposable Workbench").toBeTruthy();
  async function api<T>(route: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${route}`, { method, data, timeout: 15_000 });
    expect(response.ok(), `${method} ${route}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const policy = (await api<{ requested: Record<string, unknown> }>("/runtime/workflows/diagnostics/settings")).requested;
  registerCleanup(async () => { await api("/runtime/workflows/diagnostics/settings", "PUT", policy); });
  await api("/runtime/workflows/diagnostics/settings", "PUT", {
    scope: policy.scope, defaultLevel: "DiagnosticSnapshot",
    subjectOverrides: { durableValues: "Metadata", incidents: "Metadata", diagnostics: "Metadata" }
  });
  const types = {
    sequence: "Elsa.Activities.Sequence.Activities.Sequence", foreach: "Elsa.Activities.ForEach.Activities.ForEach",
    write: "Elsa.Activities.Primitives.Activities.WriteLine", delay: "Elsa.Activities.Scheduling.Activities.Delay"
  };
  const versions: Record<string, string> = {};
  for (const [kind, type] of Object.entries(types)) {
    const catalog = await api<{ items: Array<{ definition: { activityTypeKey: string; headVersionId: string } }> }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const matches = catalog.items.filter(item => item.definition.activityTypeKey === type);
    expect(matches).toHaveLength(1);
    versions[kind] = matches[0].definition.headVersionId;
  }
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const node = (nodeId: string, kind: string, inputs: unknown[] = [], payload?: unknown) => ({
    nodeId, activityVersionId: versions[kind], inputs, outputs: [],
    ...(payload === undefined ? {} : { structure: { kind: `elsa.${kind}.structure`, schemaVersion: "1.0.0", payload } })
  });
  const body = node("item-body", "sequence", [], { activities: [
    node("item-message", "write", [{ referenceKey: "text", value: { expressionType: "JavaScript", value: "variables.currentItem" } }]),
    node("item-delay", "delay", [literal("Duration", "00:00:02")])
  ] });
  const each = node("line-items", "foreach", [literal("Collection", ["Notebook", "Pen", "Mug"]), literal("ExposeIndex", true)], { body });
  const state = { rootActivity: node("order", "sequence", [], { activities: [each] }), variables: [], inputs: [], outputs: [], strategyOptions: { persistenceMode: "Durable" } };
  const activityPresentation = Object.entries({ order: "Inspection", "line-items": "Prepare items", "item-body": "Prepare one item", "item-message": "Show current item", "item-delay": "Pause between items" })
    .map(([nodeId, displayName]) => ({ nodeId, displayName }));
  const created = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
    operationKey: crypto.randomUUID(), name: `Occurrence navigation · Three recorded iterations with a long workflow name · ${info.project.name}`,
    initialState: state, activityPresentation
  });
  const runs: string[] = [];
  async function completed(runId: string) {
    let observed!: Run;
    await expect.poll(async () => {
      observed = await api<Run>(`/runtime/workflows/instances/${encodeURIComponent(runId)}`);
      return observed.instance.status;
    }, { timeout: 30_000 }).toBe("Completed");
    return observed;
  }
  registerCleanup(async () => {
    try { for (const run of runs) await completed(run); }
    finally { await api(`/design/workflows/definitions/${encodeURIComponent(created.definition.id)}`, "DELETE"); }
  });
  await api(`/design/workflows/drafts/${created.draft.id}`, "PUT", { state, activityPresentation });
  const version = await api<{ id: string }>(`/design/workflows/drafts/${created.draft.id}/promote`, "POST", { operationKey: crypto.randomUUID() });
  const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${version.id}/publish`, "POST", {});
  const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
  runs.push(started.workflowExecutionId);
  async function occurrences(runId: string): Promise<Occurrence[]> {
    const observed = await completed(runId);
    const rows = observed.activities.filter(item => item.executableNodeId === "item-message").sort((a, b) => a.startedAt.localeCompare(b.startedAt));
    expect(rows).toHaveLength(3);
    const result: Occurrence[] = [];
    for (const row of rows) {
      const route = `/runtime/workflows/instances/${encodeURIComponent(runId)}/activity-executions/${encodeURIComponent(row.activityExecutionId)}`;
      const detail = await api<{ valueSnapshots: Array<{ inputKey: string; evidenceId: string }> }>(route);
      const snapshots = detail.valueSnapshots.filter(item => item.inputKey === "text");
      expect(snapshots).toHaveLength(1);
      const evidence = await api<{ payload: { preview: string } }>(`${route}/value-evidence/${encodeURIComponent(snapshots[0].evidenceId)}/payload`);
      result.push({ id: row.activityExecutionId, value: evidence.payload.preview });
    }
    expect(result.map(item => item.value)).toEqual(["Notebook", "Pen", "Mug"]);
    expect(new Set(result.map(item => item.id)).size).toBe(3);
    return result;
  }
  async function openDetails(scope: Locator) {
    const details = scope.locator(".wf-activity-execution-details");
    if (await details.getAttribute("open") === null) await details.locator(":scope > summary").press("Enter");
    await expect(details).toHaveAttribute("open", "");
    return details;
  }
  async function exact(scope: Locator, occurrence: Occurrence) {
    await expect(scope).toContainText(occurrence.value);
    const id = scope.locator(".wf-activity-meta-item").filter({ has: page.locator("dt").filter({ hasText: /^Activity [Ee]xecution ID$/ }) }).locator("dd code");
    await expect(id).toBeVisible();
    await expect(id).toHaveText(occurrence.id);
  }
  async function boundaries(navigator: Locator, scope: Locator, items: Occurrence[]) {
    const next = navigator.getByRole("button", { name: /^Next occurrence of / });
    const previous = navigator.getByRole("button", { name: /^Previous occurrence of / });
    const chooser = navigator.locator(".wf-execution-choice");
    for (let index = 1; index < items.length; index++) {
      await next.press("Enter");
      await exact(scope, items[index]);
      if (index === items.length - 1) await expect(next).toBeDisabled();
      else await expect(next).toBeEnabled();
      await expect(index === items.length - 1 ? chooser : next).toBeFocused();
    }
    for (let index = items.length - 2; index >= 0; index--) {
      await previous.press("Enter");
      await exact(scope, items[index]);
      if (index === 0) await expect(previous).toBeDisabled();
      else await expect(previous).toBeEnabled();
      await expect(index === 0 ? chooser : previous).toBeFocused();
    }
  }
  const items = await occurrences(started.workflowExecutionId);
  await page.goto(`/workflows/instances/${encodeURIComponent(started.workflowExecutionId)}?selected=${encodeURIComponent(items[0].id)}`);
  const standalone = page.locator(".wf-instance-inspector");
  await openDetails(standalone); await exact(standalone, items[0]);
  const actions = page.getByRole("group", { name: "Run workspace actions", exact: true });
  await expect(actions.getByRole("button", { name: "Refresh execution", exact: true })).toBeEnabled();
  await capture(page, info, "standalone-exact");
  const more = actions.getByRole("button", { name: "More run actions", exact: true });
  await more.press("ArrowDown");
  await expect(page.getByRole("menu", { name: "More run actions", exact: true }).getByRole("menuitem", { name: "Explain", exact: true })).toBeFocused();
  await exact(standalone, items[0]); await capture(page, info, "standalone-menu");
  await page.keyboard.press("Escape"); await expect(more).toBeFocused();
  // On narrow screens the run panel and canvas are alternative workspaces. The attached inspector
  // below exercises native boundaries there; desktop also exercises the separate canvas navigator.
  if ((page.viewportSize()?.width ?? 0) >= 1000) {
    await boundaries(page.getByRole("navigation", { name: "Execution scope", exact: true }), standalone, items);
    await exact(standalone, items[0]);
  }
  await page.goto(`/workflows/definitions?definition=${encodeURIComponent(created.definition.id)}`);
  // Opening a definition normalizes its editor layout. Save that actual draft before capturing
  // a Test Run snapshot, as a user would; a later draft change must still refuse restoration.
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved");
  await page.getByRole("button", { name: "Test Run", exact: true }).click();
  const update = page.getByRole("button", { name: "Update test", exact: true });
  if (await update.isVisible()) await update.click();
  await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
  const controls = page.getByRole("group", { name: "Execution controls", exact: true });
  const runtime = page.getByRole("region", { name: "Test run summary", exact: true });
  await controls.getByRole("button", { name: "Pause live updates", exact: true }).press("Enter");
  await expect(controls.getByRole("button", { name: "Resume live updates", exact: true })).toBeFocused();
  await expect(runtime).toContainText("Live updates paused");
  await runtime.getByRole("button", { name: "Technical details", exact: true }).click();
  const runId = await runtime.locator('[aria-label^="Run ID: "]').getAttribute("title");
  expect(runId).toBeTruthy(); runs.push(runId!);
  const attachedItems = await occurrences(runId!);
  await controls.getByRole("button", { name: "Refresh execution", exact: true }).click();
  await expect(runtime.locator(".wf-runtime-heading .wf-status-badge")).toHaveText("Completed");
  await expect(controls.getByRole("button", { name: "Resume live updates", exact: true })).toBeEnabled();
  await capture(page, info, "completed-while-observation-paused");
  const collapse = page.getByRole("button", { name: "Collapse activities panel", exact: true });
  if (await collapse.isVisible()) await collapse.click();
  await page.getByRole("button", { name: "Open Body, 1 activity", exact: true }).click();
  await page.getByRole("button", { name: "1. Show current item", exact: true }).click();
  await page.getByRole("tab", { name: "Inspector", exact: true }).click();
  const attached = page.locator(".wf-attached-run-inspection:visible");
  const chooser = attached.locator(".wf-execution-choice");
  await chooser.press("Enter");
  await attached.getByRole("list", { name: "Recorded activity executions", exact: true }).getByRole("button", { name: /^(Iteration|Execution) 1\b/ }).press("Enter");
  const details = await openDetails(attached); await exact(attached, attachedItems[0]);
  const retained = await details.elementHandle();
  await boundaries(attached.locator(".wf-execution-scope-occurrence"), attached, attachedItems);
  await expect(chooser).toBeFocused(); await capture(page, info, "attached-first-boundary");
  await attached.getByRole("button", { name: /^Next occurrence of / }).press("Enter");
  await exact(attached, attachedItems[1]);
  await page.getByRole("tab", { name: "Runtime", exact: true }).click();
  expect(await retained!.evaluate(element => element.isConnected)).toBe(true);
  const hidden = page.locator(".wf-inspector > .wf-workbench-panel-content").filter({ has: page.locator(".wf-activity-execution-details") });
  await expect(hidden).toHaveAttribute("hidden", ""); await expect(hidden).toHaveAttribute("inert", "");
  await controls.getByRole("button", { name: "Refresh execution", exact: true }).click();
  await page.getByRole("tab", { name: "Inspector", exact: true }).click();
  await exact(attached, attachedItems[1]);
  expect(await details.evaluate((element, original) => element === original, retained)).toBe(true);
  await expect(details).toHaveAttribute("open", ""); await capture(page, info, "retained-middle-occurrence");
  await page.getByRole("tab", { name: "Runtime", exact: true }).click();
  await controls.getByRole("button", { name: "Return to editing", exact: true }).press("Enter");
  await expect(controls).toContainText("Editing the current draft");
  await controls.getByRole("button", { name: "Show execution", exact: true }).press("Enter");
  await page.getByRole("tab", { name: "Inspector", exact: true }).click();
  await openDetails(attached); await exact(attached, attachedItems[1]);
  await capture(page, info, "restored-exact-occurrence");
}
