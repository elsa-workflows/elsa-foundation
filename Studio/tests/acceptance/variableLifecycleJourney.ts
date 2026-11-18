import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = Record<string, unknown> & { nodeId: string };
type Execution = { activityExecutionId: string; authoredActivityId: string; status?: string };
// Teardown runs in the spec's afterEach with its own budget; each request is bounded so a stuck call cannot hold it up.
const cleanupRequestTimeout = 15_000;

/**
 * The variable lifecycle in Studio (#4735, spec 1221) on the real composed host, with no fixture responses. A step
 * reads `count`, waits on an event while another branch writes `count`, and on resume writes `count` on its stale
 * base: a real lost-update warning (Default) and a real refusal (Fault). The journey inspects both in the run view,
 * the opt-in Variable writes diagnostics subject, and edits the lost-update mode in the designer, checking the draft.
 * Accept recorded completion and background input refreshes need a background-eligible or a normal-invoke refusal,
 * which this host cannot produce; vitest covers them.
 */
export async function variableLifecycleJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "Owned Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown, timeout?: number): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data, timeout });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(key: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const entry = result.items.find(item => item.definition.activityTypeKey === key);
    expect(entry, key).toBeTruthy(); return entry!.definition.headVersionId;
  }
  const [flowchart, writeLine, event] = await Promise.all([
    version("Elsa.Activities.Flowchart.Activities.Flowchart"),
    version("Elsa.Activities.Primitives.Activities.WriteLine"),
    version("Elsa.Activities.Primitives.Activities.Event")
  ]);

  const definitions: string[] = [];
  // Registered before the first interaction: the spec's afterEach runs it even when this journey times out.
  registerCleanup(async () => {
    // Each deletion runs even when another fails; the failures are reported together.
    const failures: unknown[] = [];
    for (const id of definitions) {
      try { await api(`/design/workflows/definitions/${id}`, "DELETE", undefined, cleanupRequestTimeout); }
      catch (error) { failures.push(error); }
    }
    if (failures.length) throw new AggregateError(failures, "Could not delete every seeded variable-lifecycle definition");
  });

  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const connection = (from: string, to: string) => ({ id: `${from}-${to}`, source: { nodeId: from, port: "Done" }, target: { nodeId: to } });
  const stamp = `${info.project.name}-${crypto.randomUUID().slice(0, 8)}`;

  async function seed(mode: "Default" | "Fault") {
    const wait: Node = {
      nodeId: "wait-for-total", activityVersionId: event,
      inputs: [
        literal("EventName", `variable-lifecycle-${mode}-${stamp}`),
        literal("CanStartWorkflow", false),
        // Reads count, so the step's input read set holds count at version 0; blank keeps the wait unscoped.
        { referenceKey: "CorrelationId", value: { expressionType: "JavaScript", value: "getVariable('count') > 1000 ? 'x' : ''" } }
      ],
      outputs: [{ referenceKey: "Payload", value: { expressionType: "Variable", value: { referenceKey: "count", declaringScopeId: "workflow" } } }],
      ...(mode === "Fault" ? { lostUpdateMode: 1 } : {})
    };
    const submitted = await api<{ definition: { id: string }; version: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
      name: `Variable lifecycle ${mode} ${stamp}`,
      state: {
        variables: [{ referenceKey: "count", name: "count", type: { alias: "Object", collectionKind: "Single" }, default: { expressionType: "Literal", value: 0 } }],
        inputs: [], outputs: [],
        rootActivity: {
          nodeId: "flow", activityVersionId: flowchart, inputs: [], outputs: [],
          structure: {
            kind: "elsa.flowchart.structure", schemaVersion: "1.0.0",
            payload: {
              activities: [
                { nodeId: "start", activityVersionId: writeLine, inputs: [literal("text", "start")], outputs: [] },
                wait,
                { nodeId: "pad", activityVersionId: writeLine, inputs: [literal("text", "other branch")], outputs: [] },
                {
                  nodeId: "set-count", activityVersionId: "elsa.intrinsic.set@1",
                  intrinsic: { kind: "Set", valueType: { alias: "Elsa.Any", collectionKind: "Single" }, variable: { referenceKey: "count", declaringScopeId: "workflow" } },
                  inputs: [literal("value", 7)], outputs: []
                }
              ],
              connections: [connection("start", "wait-for-total"), connection("start", "pad"), connection("pad", "set-count")],
              startNodeId: "start", nodeMetadata: {}, connectionMetadata: {}
            }
          }
        }
      }
    });
    definitions.push(submitted.definition.id);
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    const instance = `/runtime/workflows/instances/${run.workflowExecutionId}`;
    const executions = async () => (await api<{ items: { execution: Execution; status?: string }[] }>(`${instance}/activity-executions`))
      .items.map(item => ({ ...item.execution, status: item.status ?? item.execution.status }));
    // The wait step has suspended on its bookmark and the other branch has committed count = 7.
    let step = "";
    let bookmark: { stimulusType: string; stimulusHash: string } | undefined;
    await expect.poll(async () => {
      const all = await executions();
      step = all.find(item => item.authoredActivityId === "wait-for-total")?.activityExecutionId ?? "";
      // Listed is not enough: a scheduled Set step has not written count yet.
      const written = all.some(item => item.authoredActivityId === "set-count" && item.status === "Completed");
      if (step) bookmark = (await api<{ bookmarks: { stimulusType: string; stimulusHash: string }[] }>(`${instance}/activity-executions/${step}`)).bookmarks[0];
      return { waiting: !!bookmark, written };
    }, { timeout: 30_000, message: "The wait step suspends and the other branch writes count" }).toEqual({ waiting: true, written: true });
    const resumed = await api<{ resumedCount: number }>("/runtime/workflows/stimuli", "POST", { stimulusType: bookmark!.stimulusType, stimulusHash: bookmark!.stimulusHash, input: 42 });
    expect(resumed.resumedCount).toBe(1);
    const expected = mode === "Fault" ? "Faulted" : "Completed";
    await expect.poll(async () => (await api<{ instance: { status: string } }>(instance)).instance.status, { timeout: 30_000 }).toBe(expected);
    return { definitionId: submitted.definition.id, draftId: submitted.draftId, runId: run.workflowExecutionId, step };
  }

  const inspector = () => page.getByRole("complementary", { name: "Run details panel", exact: true });
  async function openActivity(runId: string, step: string) {
    await page.goto(`/workflows/instances/${runId}?selected=${encodeURIComponent(step)}`);
    const tab = inspector().getByRole("tab", { name: "Activity", exact: true });
    await tab.click();
    await expect(tab).toHaveAttribute("aria-selected", "true");
    const section = inspector().getByLabel("Variable write diagnostics", { exact: true });
    await expect(section).toBeVisible({ timeout: 30_000 });
    await section.scrollIntoViewIfNeeded();
    return section;
  }

  // A lost-update warning names both writers and keeps the overwritten value.
  const warning = await seed("Default");
  let section = await openActivity(warning.runId, warning.step);
  await expect(section).toContainText("Lost update");
  await expect(section).toContainText("Read version 0 · was version 1 · now version 2");
  await expect(section.locator(".wf-variable-write-facts")).toContainText('Writer"Event"');
  await expect(section.locator(".wf-variable-write-facts")).toContainText('Overwritten writer"Set Variable"');
  await expect(section.locator(".wf-variable-write-facts")).toContainText("Overwritten valueKept for recovery");
  await expect(inspector().locator(".wf-activity-overview")).toContainText("1 lost update");
  await capture(page, info, "lost-update-warning");

  // A Fault refusal says what was refused and why.
  const refusal = await seed("Fault");
  section = await openActivity(refusal.runId, refusal.step);
  await expect(section).toContainText("Write refused");
  await expect(section).toContainText("faulted with VariableLostUpdate");
  await expect(section.locator(".wf-variable-write-facts")).toContainText("Refused valueKept for recovery");
  await expect(section).not.toContainText("Recorded completion kept");
  await capture(page, info, "fault-refusal");

  // The write history's values are opt-in.
  await page.goto("/workflows/runtime-diagnostics");
  const row = page.locator(".runtime-diagnostics-subject").filter({ hasText: "Variable writes" });
  await expect(row).toContainText("Runtime applies: Off", { timeout: 30_000 });
  await row.scrollIntoViewIfNeeded();
  await capture(page, info, "diagnostics-variable-writes");

  // The lost-update mode is a node setting in the designer, stored on the draft.
  await page.goto(`/workflows/definitions?definition=${warning.definitionId}`);
  const node = page.locator('.react-flow__node[data-id="wait-for-total"]').first();
  await expect(node).toBeAttached({ timeout: 45_000 });
  // Selecting by keyboard works at every width, including when the narrow canvas is mostly covered.
  await node.press("Enter");
  await expect(page.locator(".wf-inspector-context h3")).toHaveText("Event");
  await page.getByRole("tab", { name: "Details", exact: true }).click();
  const settings = page.getByRole("region", { name: "Variable writes" }).or(page.locator(".wf-node-lifecycle-settings")).first();
  await expect(settings).toBeVisible();
  const mode = settings.locator("select");
  await expect(mode).toHaveValue("Default");
  // The refresh switch applies to background placement only; this step runs inline.
  await expect(settings.locator("input[type=checkbox]")).toBeDisabled();
  await settings.scrollIntoViewIfNeeded();
  await capture(page, info, "node-settings");
  await mode.selectOption("Fault");
  await expect(settings).toContainText("the write is refused and the step faults");
  await expect.poll(async () => {
    const draft = await api<{ draft?: { state?: { rootActivity?: Node } } }>(`/design/workflows/definitions/${warning.definitionId}`);
    const children = ((draft.draft?.state?.rootActivity?.structure as { payload?: { activities?: Node[] } } | undefined)?.payload?.activities ?? []);
    return children.find(child => child.nodeId === "wait-for-total")?.lostUpdateMode;
  }, { timeout: 30_000, message: "Autosave stores the node's lost-update mode on the draft" }).toBe(1);
  await capture(page, info, "node-settings-fault");
}
