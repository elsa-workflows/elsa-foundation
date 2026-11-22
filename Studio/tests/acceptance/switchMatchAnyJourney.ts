import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = { nodeId: string; structure?: { payload: { mode?: string; cases: { match: string; caseId: string; activity?: Node; condition: unknown }[]; default?: Node } } };
type Draft = { state: { rootActivity: { structure: { payload: { activities: Node[] } } } } };
type Published = { artifactId: string; sourceReferenceId: string };

/** Actual shared editor, persisted draft, publication and runtime; no intercepted requests or Code view. */
export async function switchMatchAnyJourney(page: Page, info: TestInfo, capture: Capture, cleanup: (action: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data, timeout: 30_000 });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(key: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const item = result.items.find(item => item.definition.activityTypeKey === key);
    expect(item, key).toBeTruthy();
    return item!.definition.headVersionId;
  }
  const sequence = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const switchVersion = await version("Elsa.Activities.Switch.Activities.Switch");
  const line = await version("Elsa.Activities.Primitives.Activities.WriteLine");
  const submitted = await api<{ definition: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
    name: `Switch matches ${info.project.name} ${Date.now()}`,
    state: { inputs: [], outputs: [], variables: [], rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [],
      structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [
        { nodeId: "switch", activityVersionId: switchVersion, inputs: [], outputs: [], structure: { kind: "elsa.switch.structure", schemaVersion: "2.0.0", payload: { cases: [] } } },
        { nodeId: "after", activityVersionId: line, inputs: [{ referenceKey: "Text", value: { expressionType: "Literal", value: "after all matches" } }], outputs: [] }
      ] } } } }
  });
  cleanup(async () => { await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE"); });
  const cases = () => page.getByRole("region", { name: "Cases", exact: true });
  async function selectOwner() {
    await page.locator('.wf-sequence-step[data-id="switch"]').press("Enter");
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await expect(cases().getByRole("combobox", { name: "Match mode", exact: true })).toBeVisible();
  }
  async function returnToOwner() {
    await page.getByRole("button", { name: "Root", exact: true }).click();
    await selectOwner();
  }
  async function open() {
    await page.goto(`/workflows/definitions?definition=${submitted.definition.id}&backend=legacy`);
    await selectOwner();
  }
  async function condition(index: number, value: boolean) {
    const row = cases().locator(".wf-slot-collection-item").nth(index);
    const syntax = row.getByRole("button", { name: /Condition expression syntax:/ });
    if (!(await syntax.getAttribute("aria-label"))?.includes("JavaScript")) {
      await syntax.click(); await page.getByRole("option", { name: /JavaScript/ }).click();
    }
    await row.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
    const editor = row.locator(".cm-content").first();
    await editor.fill(String(value)); await editor.press("Escape");
  }
  async function stored() {
    const draft = await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`);
    return draft.state.rootActivity.structure.payload.activities[0];
  }
  async function save() {
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
    return stored();
  }
  async function publishAndRun(expected: string[]) {
    const response = page.waitForResponse(item => item.request().method() === "POST" && item.url().endsWith("/publish"));
    await page.getByRole("button", { name: "Review & publish", exact: true }).click();
    const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
    await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
    await review.getByRole("button", { name: "Publish", exact: true }).click();
    const published = await (await response).json() as Published;
    await page.getByRole("dialog", { name: "Published", exact: true }).getByRole("button", { name: "Close", exact: true }).click();
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    const instance = `/runtime/workflows/instances/${run.workflowExecutionId}`;
    await expect.poll(async () => (await api<{ instance: { status: string } }>(instance)).instance.status, { timeout: 30_000 }).toBe("Completed");
    const executions = await api<{ items: { execution: { authoredActivityId: string }; status: string }[] }>(`${instance}/activity-executions`);
    expect(executions.items.map(item => item.execution.authoredActivityId).sort()).toEqual(["root", "switch", "after", ...expected].sort());
    expect(executions.items.every(item => item.status === "Completed")).toBe(true);
    await info.attach(`run-${expected.length}.json`, { body: JSON.stringify({ published, run, executions }, null, 2), contentType: "application/json" });
  }
  await open();
  await capture(page, info, "reference");
  await cases().getByRole("combobox", { name: "Match mode", exact: true }).selectOption("MatchAny");
  for (const [index, label] of ["Approved", "Notify"].entries()) {
    await cases().getByRole("button", { name: "Add case", exact: true }).click();
    const name = cases().getByRole("textbox", { name: `Case ${index + 1} label`, exact: true });
    await name.fill(index ? "Approved" : label); await name.press("Enter");
    if (index) {
      await expect(cases().getByRole("alert")).toContainText("already used");
      await capture(page, info, "invalid-label");
      await name.fill(label); await name.press("Enter");
      await expect(cases().getByRole("alert")).toHaveCount(0);
    }
    await condition(index, true);
    await cases().getByRole("button", { name: `Choose ${label} activity`, exact: true }).click();
    await page.getByRole("option", { name: /Write Line/ }).click();
    await returnToOwner();
  }
  const authored = await save();
  expect(authored.structure!.payload.mode).toBe("MatchAny");
  const bodies = authored.structure!.payload.cases.map(item => item.activity!.nodeId);
  expect(new Set(bodies).size).toBe(2);
  await capture(page, info, "match-any-authored");
  await open();
  await expect(cases().getByRole("combobox", { name: "Match mode", exact: true })).toHaveValue("MatchAny");
  expect(await stored()).toEqual(authored);
  await capture(page, info, "match-any-reopened");
  await publishAndRun(bodies);
  await cases().getByRole("combobox", { name: "Match mode", exact: true }).selectOption("MatchFirst");
  const first = await save();
  expect(first.structure!.payload.cases).toEqual(authored.structure!.payload.cases);
  await capture(page, info, "match-first");
  await publishAndRun([bodies[0]]);
  await cases().getByRole("combobox", { name: "Match mode", exact: true }).selectOption("MatchAny");
  await condition(0, false); await condition(1, false);
  await save();
  await capture(page, info, "no-match");
  await publishAndRun([]);
  await page.getByRole("button", { name: "Choose Default activity", exact: true }).click();
  await page.getByRole("option", { name: /Write Line/ }).click();
  await returnToOwner();
  const fallback = (await save()).structure!.payload.default!.nodeId;
  await capture(page, info, "default");
  await publishAndRun([fallback]);
}
