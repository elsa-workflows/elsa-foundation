import { expect, type Page, type TestInfo } from "@playwright/test";
import type { ActivityExecutionInspection } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Real, occurrence-specific task inputs; no browser-route or runtime-value fixture responses. */
export async function compactInputsJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  expect(backend, "The owned runner supplies its disposable Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  let runId: string | null = null;
  const identity = await api<{ userId: string }>("/runtime/user-task-identity");
  const type = "Elsa.Activities.Primitives.Activities.CreateUserTask";
  const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
  const taskVersion = catalog.items.find(item => item.definition.activityTypeKey === type)?.definition.headVersionId;
  expect(taskVersion, "The normal catalog supplies CreateUserTask").toBeTruthy();
  const stamp = `${info.project.name}-${crypto.randomUUID().slice(0, 8)}`;
  const title = `Review purchase PO-1042 · Northwind supplies · ${stamp}`;
  const formId = `compact-inputs-${stamp}`;
  await api("/runtime/user-task-forms", "POST", {
    id: formId, version: "1", name: "Compact Inputs acceptance", provider: "elsa.basic-forms/1",
    fields: [
      { key: "Note", label: "Note", kind: "text", required: true },
      { key: "Rush", label: "Rush", kind: "boolean" },
      { key: "Quantity", label: "Quantity", kind: "number" },
      { key: "Extra", label: "Extra", kind: "text" }
    ]
  });
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const submitted = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", {
    name: `Compact Inputs ${stamp}`,
    state: {
      variables: [], inputs: [], outputs: [],
      rootActivity: {
        nodeId: "review-purchase", activityVersionId: taskVersion, outputs: [],
        inputs: [
          literal("Title", title), literal("AssigneeId", identity.userId),
          literal("Actions", ["Approve", "Reject", "Request changes"]),
          literal("FormId", formId), literal("FormVersion", "1"), literal("OptionalFormActions", []),
          literal("Prefill", { Note: "", Rush: false, Quantity: 0, Extra: null })
        ]
      }
    }
  });
  // Cleanups run from the harness after the test, each on its own, even if the body failed or timed out:
  // first close the run's still-open user task (the next project's inbox journey starts from "No tasks here"),
  // then delete the definition, so later journeys see no leftover workflow.
  registerCleanup(async () => {
    if (!runId) return;
    const openTasks = async () => (await api<{ items: Array<{ id: string; workflowExecutionId: string; status: string; revision: number }> }>("/runtime/user-tasks?view=mine&limit=100"))
      .items.filter(item => item.workflowExecutionId === runId && (item.status === "Open" || item.status === "Claimed"));
    for (const task of await openTasks()) {
      await api(`/runtime/user-tasks/${encodeURIComponent(task.id)}/complete`, "POST",
        { revision: task.revision, requestId: `acceptance-cleanup-${task.id}`, action: "Reject", data: { Note: "Closed by acceptance cleanup" } });
    }
    // Completion is accepted (202) and applied asynchronously; the next journey starts from an empty inbox only once it has.
    await expect.poll(async () => (await openTasks()).length, { timeout: 15_000, message: "The cleanup's task leaves the inbox" }).toBe(0);
  });
  registerCleanup(async () => { await api(`/design/workflows/definitions/${encodeURIComponent(submitted.definition.id)}`, "DELETE"); });
  const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
  const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
  runId = run.workflowExecutionId;
  const instance = `/runtime/workflows/instances/${run.workflowExecutionId}`;
  // The task exists once the occurrence has suspended on its bookmark; a fault ends the wait with evidence.
  await expect.poll(async () => {
    const state = await api<{ instance: { status: string } }>(instance);
    if (state.instance.status === "Faulted") {
      await info.attach("task-seed-fault.json", { body: JSON.stringify(state, null, 2), contentType: "application/json" });
      throw new Error("The owned task seed faulted before creating its task; inspect task-seed-fault.json.");
    }
    const tasks = await api<{ items: { workflowExecutionId: string }[] }>("/runtime/user-tasks?view=mine&limit=100");
    return tasks.items.some(item => item.workflowExecutionId === run.workflowExecutionId);
  }, { timeout: 30_000 }).toBe(true);
  const executions = await api<{ items: { execution: { activityExecutionId: string; authoredActivityId: string } }[] }>(`${instance}/activity-executions`);
  const owner = executions.items.find(item => item.execution.authoredActivityId === "review-purchase")?.execution.activityExecutionId ?? "";
  expect(owner, "The task occurrence is listed for its authored activity").toBeTruthy();
  const recorded = await api<ActivityExecutionInspection>(`/runtime/workflows/instances/${run.workflowExecutionId}/activity-executions/${owner}`);
  expect(recorded.activityExecutionId).toBe(owner);
  expect(recorded.workflowExecutionId).toBe(run.workflowExecutionId);
  const titleEvidence = recorded.valueSnapshots.find(snapshot => snapshot.subject === "ActivityInput" && snapshot.name === "Title");
  expect(titleEvidence?.evidenceId, "The selected occurrence owns actual captured Title evidence").toBeTruthy();
  const valueRequests: string[] = [];
  page.on("request", request => {
    if (request.url().includes(encodeURIComponent(titleEvidence!.evidenceId!))) valueRequests.push(request.url());
  });

  await page.goto(`/workflows/instances/${run.workflowExecutionId}?selected=${encodeURIComponent(owner)}`);
  const inspector = page.getByRole("complementary", { name: "Run details panel", exact: true });
  // Runtime Inputs live in the run inspector's Activity tab for the selected occurrence.
  const openActivityInputs = async () => {
    const activity = inspector.getByRole("tab", { name: "Activity", exact: true });
    await activity.click();
    await expect(activity).toHaveAttribute("aria-selected", "true");
  };
  await openActivityInputs();
  await expect(inspector.getByRole("heading", { name: /^Inputs/ })).toBeVisible();
  const row = (name: string) => inspector.locator(".wf-input-inspection-row").filter({
    has: page.getByRole("button", { name: `Technical details for ${name}`, exact: true })
  });
  const titleRow = row("Title");
  await expect(titleRow.locator(".wf-input-inspection-value")).toHaveText(title);
  await expect(inspector.getByText("Source & diagnostics", { exact: true })).toHaveCount(0);
  const details = titleRow.getByRole("button", { name: "Technical details for Title", exact: true });
  await expect(details).toHaveAttribute("aria-expanded", "false");
  await expect(titleRow.getByRole("region", { name: "Title technical details", exact: true })).toBeHidden();
  await titleRow.scrollIntoViewIfNeeded();
  await capture(page, info, "compact-values");
  const valueReadsBeforeDetails = valueRequests.length;

  await details.focus();
  await details.press("Enter");
  const technical = titleRow.getByRole("region", { name: "Title technical details", exact: true });
  await expect(details).toHaveAttribute("aria-expanded", "true");
  await expect(technical).toBeVisible();
  await expect(technical).toContainText("Type");
  await expect(technical.getByRole("heading", { name: "Authored source", exact: true })).toBeVisible();
  await expect(technical).toContainText(title);
  await expect(titleRow.locator(".wf-input-inspection-value")).toHaveText(title);
  // Focus the existing details control so the scrollable inspector exposes its real content.
  await technical.locator("summary").first().focus();
  await expect(technical.getByRole("heading", { name: "Authored source", exact: true })).toBeInViewport();
  await expect(technical.getByRole("heading", { name: "Capture details", exact: true })).toBeInViewport();
  await capture(page, info, "technical-details");
  expect(valueRequests, "Opening presentation-only technical details does not re-read captured value evidence").toHaveLength(valueReadsBeforeDetails);
  await page.keyboard.press("Escape");
  await expect(technical).toBeHidden();
  await expect(details).toBeFocused();

  const prefill = row("Prefill").locator(".wf-input-inspection-value");
  await prefill.locator("details > summary").first().press("Enter");
  for (const text of ['""', "false", "0", "null"]) {
    await expect(prefill.getByText(text, { exact: true })).toBeVisible();
  }
  // Basic forms deliberately have no array field kind; retain a real captured empty-array input.
  const optionalActions = inspector.locator(".wf-input-inspection-row").filter({
    has: page.getByRole("button", { name: /^Technical details for optional\s*form\s*actions$/i })
  });
  await expect(optionalActions.locator(".wf-input-inspection-value")).toHaveText("[]");
  await prefill.scrollIntoViewIfNeeded();
  await capture(page, info, "nested-values");
  await page.reload();
  await openActivityInputs();
  await expect(titleRow.locator(".wf-input-inspection-value")).toHaveText(title);
  await expect(details).toHaveAttribute("aria-expanded", "false");
  const after = await api<ActivityExecutionInspection>(`/runtime/workflows/instances/${run.workflowExecutionId}/activity-executions/${owner}`);
  expect(after.valueSnapshots.find(snapshot => snapshot.evidenceId === titleEvidence!.evidenceId)).toEqual(titleEvidence);
  await titleRow.scrollIntoViewIfNeeded();
  await capture(page, info, "details-closed");
  await page.setViewportSize({ width: 320, height: 900 });
  await page.goto(`/workflows/instances/${run.workflowExecutionId}?selected=${encodeURIComponent(owner)}`);
  await openActivityInputs();
  await expect(titleRow.locator(".wf-input-inspection-value")).toHaveText(title);
  await titleRow.scrollIntoViewIfNeeded();
  await details.focus();
  await details.press("Enter");
  await expect(technical).toBeVisible();
  await technical.locator("summary").first().focus();
  // At 320 px the wrapped run header leaves a shorter inspector, so focusing the summary scrolls it only to the
  // bottom edge. Scroll the opened details to their start, as a reader would, without moving keyboard focus.
  await technical.evaluate(element => element.scrollIntoView({ block: "start" }));
  await expect(technical.getByRole("heading", { name: "Authored source", exact: true })).toBeInViewport();
  await expect(technical.getByRole("heading", { name: "Capture details", exact: true })).toBeInViewport();
  await expect(technical.locator("summary").first()).toBeFocused();
  expect(await titleRow.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
  expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
  await capture(page, info, "technical-details-320");
  await page.keyboard.press("Escape");
  await expect(technical).toBeHidden();
  await expect(details).toBeFocused();
  await capture(page, info, "compact-values-320");
}
