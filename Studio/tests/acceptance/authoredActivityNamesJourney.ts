import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Real saved presentation, immutable publication, and run evidence on the owned composed host. */
export async function authoredActivityNamesJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = process.env.ELSA_WORKBENCH_URL!;
  async function api<T>(route: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${route}`, { method, data });
    expect(response.ok(), `${method} ${route}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(type: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const matches = result.items.filter(item => item.definition.activityTypeKey === type);
    expect(matches).toHaveLength(1);
    return matches[0].definition.headVersionId;
  }
  const sequence = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const write = await version("Elsa.Activities.Primitives.Activities.WriteLine");
  const names = ["3fa85f64-5717-4562-b3fc-2c963f66afa6", "deadbeefdeadbeef", "2026111400000001"];
  const activities = names.map((_, index) => ({ nodeId: `named-${index}`, activityVersionId: write,
    inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: `Message ${index}` } }], outputs: [] }));
  const state = { rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities } } }, variables: [], inputs: [], outputs: [] };
  const created = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
    name: `Authored names ${info.project.name} ${Date.now()}`, initialState: state,
    activityPresentation: names.map((displayName, index) => ({ nodeId: `named-${index}`, displayName: index === 0 ? "Before rename" : displayName }))
  });
  let runId: string | undefined;
  try {
    await page.goto(`/workflows/definitions?backend=legacy&definition=${created.definition.id}`);
    const card = page.locator('.wf-sequence-step[data-id="named-0"]');
    await card.press("Enter");
    const expand = page.getByRole("button", { name: "Expand inspector panel", exact: true });
    if (await expand.count()) await expand.click();
    await page.getByRole("tab", { name: "Details", exact: true }).click();
    const input = page.getByRole("textbox", { name: "Display name", exact: true });
    await input.fill(names[0]); await input.press("Tab");
    await expect.poll(async () => (await api<{ activityPresentation: { nodeId: string; displayName: string }[] }>(`/design/workflows/drafts/${created.draft.id}`)).activityPresentation.find(item => item.nodeId === "named-0")?.displayName).toBe(names[0]);
    await page.reload();
    for (const [index, name] of names.entries()) await expect(page.locator(`.wf-sequence-step[data-id="named-${index}"] .wf-sequence-title`)).toHaveText(name);
    for (const label of ["Collapse inspector panel", "Collapse activities panel"]) {
      const control = page.getByRole("button", { name: label, exact: true });
      if (await control.count()) await control.click();
    }
    await capture(page, info, "authored-definition");
    const saved = await api<{ activityPresentation: unknown[] }>(`/design/workflows/drafts/${created.draft.id}`);
    const submitted = await api<{ version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
      definitionId: created.definition.id, name: "Authored name publication", state, activityPresentation: saved.activityPresentation
    });
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    await page.goto(`/workflows/executables/${published.artifactId}?backend=legacy&ref=${published.sourceReferenceId}`);
    const canvas = page.getByRole("region", { name: "Executable canvas", exact: true });
    for (const [index, name] of names.entries()) await expect(canvas.locator(`[data-id="named-${index}"] .wf-sequence-title`)).toHaveText(name);
    // A later draft rename must not overwrite the immutable publication/run names.
    await api(`/design/workflows/drafts/${created.draft.id}`, "PUT", { state,
      activityPresentation: names.map((_, index) => ({ nodeId: `named-${index}`, displayName: `Current draft ${index}` })) });
    await page.reload();
    for (const [index, name] of names.entries()) await expect(canvas.locator(`[data-id="named-${index}"] .wf-sequence-title`)).toHaveText(name);
    await capture(page, info, "authored-executable");
    const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    runId = started.workflowExecutionId;
    let executionId: string | undefined;
    await expect.poll(async () => {
      const run = await api<{ instance: { status: string }; activities: { executableNodeId: string; activityExecutionId: string }[] }>(`/runtime/workflows/instances/${runId}`);
      executionId = run.activities.find(item => item.executableNodeId === "named-0")?.activityExecutionId;
      return run.instance.status;
    }).toBe("Completed");
    expect(executionId).toBeTruthy();
    await page.goto(`/workflows/instances/${runId}?selected=${executionId}`);
    await expect(page.locator(".wf-activity-overview-title h4")).toHaveText(names[0]);
    await capture(page, info, "authored-run");
  } finally {
    if (runId) await api(`/runtime/workflows/instances/${runId}`, "DELETE");
    await api(`/design/workflows/definitions/${created.definition.id}`, "DELETE");
  }
}
