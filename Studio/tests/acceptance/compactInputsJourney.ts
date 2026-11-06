import { expect, type Page, type TestInfo } from "@playwright/test";
import type { ActivityExecutionInspection } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import type { RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Real, occurrence-specific task inputs; no browser-route or runtime-value fixture responses. */
export async function compactInputsJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  expect(backend, "The owned runner supplies its disposable Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
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
  try {
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    let owner = "";
    await expect.poll(async () => {
      const history = await api<RuntimeHistoryPage>(`/runtime/workflows/instances/${run.workflowExecutionId}/history?limit=100`);
      if (history.items.some(event => event.name === "ActivityFaulted")) {
        await info.attach("task-seed-fault.json", { body: JSON.stringify(history, null, 2), contentType: "application/json" });
        throw new Error("The owned task seed faulted before creating its expected bookmark; inspect task-seed-fault.json.");
      }
      owner = history.items.find(event => event.name === "BookmarkCreated")?.activityExecutionId ?? "";
      return owner;
    }, { timeout: 30_000 }).not.toBe("");
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
    await inspector.getByRole("tab", { name: "Data", exact: true }).press("Enter");
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
    await inspector.getByRole("heading", { name: /^Inputs/ }).scrollIntoViewIfNeeded();
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
    await capture(page, info, "technical-details");
    expect(valueRequests, "Opening presentation-only technical details does not re-read captured value evidence").toHaveLength(valueReadsBeforeDetails);
    await technical.locator("summary").first().focus();
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
    await inspector.getByRole("tab", { name: "Data", exact: true }).press("Enter");
    await expect(titleRow.locator(".wf-input-inspection-value")).toHaveText(title);
    await expect(details).toHaveAttribute("aria-expanded", "false");
    const after = await api<ActivityExecutionInspection>(`/runtime/workflows/instances/${run.workflowExecutionId}/activity-executions/${owner}`);
    expect(after.valueSnapshots.find(snapshot => snapshot.evidenceId === titleEvidence!.evidenceId)).toEqual(titleEvidence);
    await titleRow.scrollIntoViewIfNeeded();
    await capture(page, info, "details-closed");
    await page.setViewportSize({ width: 320, height: 900 });
    await page.goto(`/workflows/instances/${run.workflowExecutionId}?selected=${encodeURIComponent(owner)}`);
    await inspector.getByRole("tab", { name: "Data", exact: true }).press("Enter");
    await expect(titleRow.locator(".wf-input-inspection-value")).toHaveText(title);
    await titleRow.scrollIntoViewIfNeeded();
    await details.focus();
    await details.press("Enter");
    await expect(technical).toBeVisible();
    expect(await titleRow.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
    await capture(page, info, "technical-details-320");
    await details.press("Escape");
    await expect(technical).toBeHidden();
    await expect(details).toBeFocused();
    await capture(page, info, "compact-values-320");
  } finally {
    await api(`/design/workflows/definitions/${encodeURIComponent(submitted.definition.id)}`, "DELETE");
  }
}
