import { expect, type Page, type TestInfo } from "@playwright/test";

type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[];
  structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Owned composed host: advisory catalog guidance, actual Placement refusal, correction and publication. */
export async function startTriggerPlacementJourney(page: Page, info: TestInfo, capture: Capture) {
  const base = process.env.ELSA_WORKBENCH_URL;
  if (!base || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned composed Workbench is required");
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const catalog = await api<{ activities: { activityTypeKey: string; activityVersionId: string; executionType: string; inputs: { referenceKey: string }[] }[] }>("/design/activities/catalog");
  const descriptor = (type: string) => {
    const item = catalog.activities.find(candidate => candidate.activityTypeKey === type);
    expect(item, `Composed catalog must contain ${type}`).toBeTruthy();
    return item!;
  };
  const timer = descriptor("Elsa.Activities.Scheduling.Activities.Timer");
  const cron = descriptor("Elsa.Activities.Scheduling.Activities.Cron");
  expect(timer.executionType).toBe("Trigger");
  expect(cron.executionType).toBe("Trigger");
  expect(timer.inputs.some(input => input.referenceKey.toLowerCase() === "canstartworkflow")).toBe(false);
  const sequence = descriptor("Elsa.Activities.Sequence.Activities.Sequence");
  const flow = descriptor("Elsa.Activities.Flowchart.Activities.Flowchart");
  const line = descriptor("Elsa.Activities.Primitives.Activities.WriteLine");
  const event = descriptor("Elsa.Activities.Primitives.Activities.Event");
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const timerNode = (nodeId: string): Node => ({ nodeId, activityVersionId: timer.activityVersionId, inputs: [literal("Interval", "PT3H")], outputs: [] });
  const lineNode = (nodeId: string): Node => ({ nodeId, activityVersionId: line.activityVersionId, inputs: [literal("text", "Placement acceptance")], outputs: [] });
  const seq = (activities: Node[]): Node => ({ nodeId: "sequence", activityVersionId: sequence.activityVersionId, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities } } });
  const state = (rootActivity: Node) => ({ rootActivity, inputs: [], outputs: [], variables: [] });
  // Seed a valid source; all invalid states below are ordinary UI edits, not privileged submission.
  const created = await api<{ definition: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
    name: `Start trigger placement ${info.project.name} ${Date.now()}`, state: state(seq([timerNode("seed-timer"), lineNode("seed-after")]))
  });
  const draftPath = `/design/workflows/drafts/${created.draftId}`;
  const views = page.getByRole("tablist", { name: "Editor view tabs", exact: true });
  const validation = page.getByRole("region", { name: "Workflow validation", exact: true });
  async function apply(root: Node) {
    await views.getByRole("tab", { name: "Code", exact: true }).click();
    await page.getByRole("textbox", { name: "Workflow JSON", exact: true }).fill(JSON.stringify({ state: state(root), layout: [], activityPresentation: [] }));
    await page.getByRole("button", { name: "Apply", exact: true }).click();
    await views.getByRole("tab", { name: "Designer", exact: true }).click();
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect.poll(async () => (await api<{ state: { rootActivity: Node } }>(draftPath)).state.rootActivity).toMatchObject(root);
  }
  try {
    await page.goto(`/workflows/definitions?backend=legacy&definition=${created.definition.id}`);
    await expect(views).toBeVisible();
    for (const name of ["Collapse activities panel", "Collapse inspector panel"]) {
      const button = page.getByRole("button", { name, exact: true });
      if (await button.isVisible()) await button.click();
    }
    const flowRoot: Node = { nodeId: "flow", activityVersionId: flow.activityVersionId, inputs: [], outputs: [], structure: {
      kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: {
        startNodeId: "first", activities: [lineNode("first"), timerNode("timer-misplaced"),
          { nodeId: "cron-independent", activityVersionId: cron.activityVersionId, inputs: [literal("Expression", "0 0 * * *")], outputs: [] },
          lineNode("after-cron"), { nodeId: "wait-disabled", activityVersionId: event.activityVersionId,
            inputs: [literal("EventName", `placement-${info.project.name}`), literal("CanStartWorkflow", false)], outputs: [] }],
        connections: [["first", "timer-misplaced"], ["first", "wait-disabled"], ["cron-independent", "after-cron"]].map(([source, target], index) => ({
          id: `edge-${index}`, source: { nodeId: source, port: "Done" }, target: { nodeId: target }
        }))
      }
    } };
    await apply(flowRoot);
    const warning = validation.locator("li").filter({ has: page.locator('.wf-validation-message[title="timer-misplaced"]') });
    await expect(warning).toContainText("If it registers a workflow start");
    await expect(warning).toContainText("use a waiting activity");
    await expect(warning).not.toContainText("turn off");
    await expect(validation.locator('.wf-validation-message[title="cron-independent"], .wf-validation-message[title="after-cron"], .wf-validation-message[title="wait-disabled"]')).toHaveCount(0);
    await warning.getByRole("button", { name: "Go to", exact: true }).click();
    await expect(page.locator('.react-flow__node[data-id="timer-misplaced"] .wf-node')).toHaveClass(/selected/);
    await capture(page, info, "trigger-flowchart-guidance");

    await apply(seq([lineNode("before"), timerNode("timer-blocked")]));
    await expect(validation.locator('.wf-validation-message[title="timer-misplaced"]')).toHaveCount(0);
    const refusal = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === "/publishing/workflows/preflight");
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const refused = await refusal;
    expect(refused.status()).toBe(400);
    expect(await refused.json()).toMatchObject({ errorCode: "workflow.trigger.placement", triggerNodeId: "timer-blocked", compositeNodeId: "sequence" });
    await expect(validation).toBeFocused();
    const pinned = validation.locator("li").filter({ has: page.locator('.wf-validation-message[title="timer-blocked"]') });
    await expect(pinned).toContainText("cannot start this workflow");
    await pinned.getByRole("button", { name: "Go to", exact: true }).click();
    await expect(page.locator('.wf-sequence-step[data-id="timer-blocked"]')).toHaveAttribute("aria-pressed", "true");
    await capture(page, info, "trigger-publication-refused");
    await page.getByRole("button", { name: "Move Timer up", exact: true }).click();
    await expect(validation.locator('.wf-validation-message[title="timer-blocked"]')).toHaveCount(0);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect.poll(async () => (await api<{ state: { rootActivity: Node } }>(draftPath)).state.rootActivity.structure!.payload.activities).toMatchObject([timerNode("timer-blocked"), lineNode("before")]);
    await capture(page, info, "trigger-corrected");
    await page.reload();
    await expect(validation.locator('.wf-validation-message[title="timer-blocked"]')).toHaveCount(0);
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toContainText("Ready to publish.");
    const publication = page.waitForResponse(response => response.request().method() === "POST" && /\/publishing\/workflows\/[^/]+\/publish$/.test(new URL(response.url()).pathname));
    await dialog.getByRole("button", { name: "Publish", exact: true }).click();
    const published = await publication;
    expect(published.ok()).toBe(true);
    const executable = await published.json() as { artifactId: string; sourceReferenceId: string };
    await expect(dialog).toContainText("Published");
    await capture(page, info, "trigger-published");
    await page.goto(`/workflows/executables/${executable.artifactId}?backend=legacy&ref=${executable.sourceReferenceId}`);
    await expect(page.getByRole("region", { name: "Executable canvas", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Move Timer up", exact: true })).toHaveCount(0);
    await capture(page, info, "trigger-read-only");
  } finally {
    await api(`/design/workflows/definitions/${created.definition.id}`, "DELETE");
  }
}
