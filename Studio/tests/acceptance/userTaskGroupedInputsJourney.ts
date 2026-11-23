import { writeFileSync } from "node:fs";
import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Expression = { expressionType: string; value: unknown };
type Draft = { state: {
  variables: Array<{ referenceKey: string; name: string; type: { alias: string; collectionKind: string }; default: Expression }>;
  rootActivity: { structure: { payload: { activities: Array<{ nodeId: string; inputs: Array<{ referenceKey: string; value: Expression }> }> } } };
} };

/**
 * Real grouped editors on the owned Workflow Editor, independent of form-authoring startup and task execution.
 * Current source behavior: the narrow inspector starts collapsed; Assignment's expression setup changes local mode
 * without autofocus. FormPicker focuses Expression returns on entry and its summary on return to fixed mode.
 * This journey uses those controls without forcing focus or changing the broader form-authoring journey.
 */
export async function userTaskGroupedInputsJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL;
  if (!base || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned Workbench URL is required");
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data, timeout: 15_000 });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const who = await api<{ userId: string }>("/runtime/user-task-identity");
  expect(who.userId).not.toBe("");
  const suffix = `${info.project.name}-${Date.now()}`;
  const formId = `grouped-inputs-${suffix}`;
  const formName = `Grouped response ${suffix}`;
  const form = { id: formId, version: "1", name: formName, provider: "elsa.basic-forms/1",
    fields: [{ key: "Note", label: "Note", kind: "text", required: true, readOnly: false }] };
  await api("/runtime/user-task-forms", "POST", form);
  const owned: { definitionId?: string } = {};
  registerCleanup(async () => {
    try {
      if (owned.definitionId) await api(`/design/workflows/definitions/${owned.definitionId}`, "DELETE");
    } finally {
      const current = await api<{ revision: number }>(`/runtime/user-task-forms/${formId}/definition`);
      await api(`/runtime/user-task-forms/${formId}/delete`, "POST", { revision: current.revision });
    }
  });
  await api("/runtime/user-task-forms", "POST", { ...form, version: "2" });
  async function version(key: string) {
    const result = await api<{ items: Array<{ definition: { activityTypeKey: string; headVersionId: string } }> }>(
      `/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const entry = result.items.find(item => item.definition.activityTypeKey === key);
    expect(entry, `Composed activity ${key}`).toBeTruthy();
    return entry!.definition.headVersionId;
  }
  const sequenceVersion = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const taskVersion = await version("Elsa.Activities.Primitives.Activities.CreateUserTask");
  const assignedPerson = { referenceKey: "assigned-person", name: "assignedPerson", type: { alias: "String", collectionKind: "Single" },
    storageDriverType: null, default: { expressionType: "Literal", value: who.userId } };
  const values = { Title: "Review grouped inputs", Description: "Verify the pinned response form and assignment.",
    AssigneeId: who.userId, GroupId: null, Actions: ["Approve", "Reject"], OptionalFormActions: [],
    FormId: formId, FormVersion: "1", Prefill: { Note: "Review before approving" } };
  const submitted = await api<{ definition: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
    name: `Grouped task inputs ${suffix}`,
    state: { inputs: [], outputs: [], variables: [assignedPerson], rootActivity: {
      nodeId: "root", activityVersionId: sequenceVersion, inputs: [], outputs: [],
      structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [{
        nodeId: "review", activityVersionId: taskVersion, outputs: [],
        inputs: Object.entries(values).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } }))
      }] } }
    } }
  });
  const definitionId = submitted.definition.id;
  owned.definitionId = definitionId;
  const draftPath = `/design/workflows/drafts/${submitted.draftId}`;
  const read = () => api<Draft>(draftPath);
  const taskInputs = (draft: Draft) => draft.state.rootActivity.structure.payload.activities.find(node => node.nodeId === "review")!.inputs;
  const stored = async (key: string) => taskInputs(await read()).find(input => input.referenceKey === key)?.value;
  const literal = (value: unknown) => ({ expressionType: "Literal", value });
  const editorUrl = new URL(page.url());
  editorUrl.searchParams.set("definition", definitionId);
  const inspectorSections = page.getByRole("tablist", { name: "Activity inspector sections", exact: true });
  const assignment = page.getByRole("group", { name: "Assignment", exact: true });
  const assignTo = assignment.getByRole("combobox", { name: "Assign to", exact: true });
  const formPicker = page.locator(".user-task-form-picker");
  const formSummary = formPicker.locator(".user-task-form-picker__disclosure > summary");
  async function open() {
    await page.goto(editorUrl.toString());
    const task = page.locator('.wf-sequence-step[data-id="review"]');
    await expect(task).toBeAttached();
    await task.press("Enter");
    const expand = page.getByRole("button", { name: "Expand inspector panel", exact: true });
    if (await expand.isVisible()) await expand.click();
    await inspectorSections.getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(assignment).toBeVisible();
  }
  async function save() {
    const saved = page.waitForResponse(response => response.request().method() === "PUT" && new URL(response.url()).pathname === draftPath);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    expect((await saved).ok()).toBe(true);
    // An autosave can answer the response wait; Saved without data-pending covers the live draft before reopening.
    const status = page.locator(".wf-editor-save-status");
    await expect(status).toHaveText(/Saved$/);
    await expect(status).not.toHaveAttribute("data-pending");
  }

  await open();
  await expect(assignment).toContainText("Assign this task to one person or one group.");
  await expect(assignTo).toHaveValue(`person:${who.userId}`);
  await assignment.getByRole("button", { name: "Clear assignment", exact: true }).click();
  await expect(assignTo).toHaveValue("");
  await expect(assignTo.locator(`option[value="person:${who.userId}"]`)).toBeAttached();
  await assignTo.selectOption(`person:${who.userId}`);
  await expect.poll(() => stored("AssigneeId")).toEqual(literal(who.userId));
  await expect.poll(() => stored("GroupId")).toEqual(literal(null));
  await assignment.getByRole("button", { name: "Use an expression", exact: true }).click();
  await expect(assignment.getByRole("combobox", { name: "Expression returns", exact: true })).toHaveValue("AssigneeId");
  await expect(assignment.getByRole("status")).toHaveText("Choose an expression syntax to configure this assignment.");
  await assignment.getByRole("button", { name: "Cancel expression setup", exact: true }).click();
  await expect(assignTo).toHaveValue(`person:${who.userId}`);
  await expect.poll(() => stored("AssigneeId")).toEqual(literal(who.userId));
  await expect.poll(() => stored("GroupId")).toEqual(literal(null));
  await assignTo.scrollIntoViewIfNeeded();
  await capture(page, info, "assignment-literal-preserved");

  await assignment.getByRole("button", { name: "Use an expression", exact: true }).click();
  const expressionRow = assignment.locator(".user-task-assignment-expression .wf-property-row");
  await expressionRow.locator(".wf-syntax-picker-trigger").click();
  await page.getByRole("option", { name: /^JavaScript/ }).click();
  const expression = expressionRow.getByRole("textbox").first();
  await expect(expression).toBeEditable();
  const source = "variables.assignedPerson";
  await expression.fill(source);
  const renderedEditor = expressionRow.locator('[data-studio-code-editor-surface="codemirror"] .cm-content');
  await expect(renderedEditor).toBeVisible();
  await expect(renderedEditor).toContainText(source);
  await expect(renderedEditor).toBeFocused();
  await expect.poll(() => stored("AssigneeId")).toEqual({ expressionType: "JavaScript", value: source });
  await expect.poll(() => stored("GroupId")).toEqual(literal(null));
  await capture(page, info, "assignment-expression");

  await formSummary.scrollIntoViewIfNeeded();
  await formSummary.press("Enter");
  await formPicker.getByRole("button", { name: `Select ${formName} version 2`, exact: true }).click();
  await expect(formSummary).toHaveAttribute("aria-label", `Task form: ${formName} · Version 2`);
  await expect(formSummary).toBeFocused();
  await expect.poll(() => stored("FormId")).toEqual(literal(formId));
  await expect.poll(() => stored("FormVersion")).toEqual(literal("2"));
  await formSummary.press("Enter");
  await formPicker.getByRole("button", { name: "Use an expression", exact: true }).click();
  const formExpressionReturns = formPicker.getByRole("combobox", { name: "Expression returns", exact: true });
  await expect(formExpressionReturns).toBeFocused();
  await formExpressionReturns.selectOption("FormVersion");
  await expect(formPicker.getByRole("status")).toHaveText("Choose an expression syntax to configure this assignment.");
  await formPicker.getByRole("button", { name: "Use a fixed form", exact: true }).click();
  await expect(formSummary).toBeFocused();
  await expect.poll(() => stored("FormId")).toEqual(literal(formId));
  await expect.poll(() => stored("FormVersion")).toEqual(literal("2"));
  await formSummary.press("Enter");
  await expect(formPicker.getByRole("button", { name: `Select ${formName} version 2`, exact: true })).toHaveAttribute("aria-pressed", "true");
  await formPicker.getByRole("button", { name: "Other versions", exact: true }).click();
  await expect(formPicker.getByRole("button", { name: `Select ${formName} version 1`, exact: true })).toBeVisible();
  const formRow = formPicker.locator(".user-task-form-picker__form-row").filter({ has: page.getByRole("button", { name: `Select ${formName} version 2`, exact: true }) });
  await expect.poll(() => formRow.evaluate(row => {
    const name = row.querySelector(".studio-button:first-child > span")!;
    const versions = row.querySelector(".studio-button:last-child")!.getBoundingClientRect();
    const range = document.createRange(); range.selectNodeContents(name);
    const lines = [...range.getClientRects()];
    return lines.length > 0 && lines.every(line => line.right <= versions.left + 1);
  }), { message: "The complete form name must remain readable beside Other versions" }).toBe(true);
  await capture(page, info, "selected-form-version");
  await page.keyboard.press("Escape");
  await expect(formPicker.locator(".user-task-form-picker__disclosure")).toHaveJSProperty("open", false);
  await expect(formSummary).toBeFocused();
  await save();

  const beforeReopen = await read();
  await open();
  await expect(assignment.getByRole("combobox", { name: "Expression returns", exact: true })).toHaveValue("AssigneeId");
  await expect(expressionRow.locator(".wf-syntax-picker-trigger")).toHaveAttribute("title", "JavaScript");
  await expect(expressionRow.locator(".wf-syntax-picker-trigger")).toHaveAttribute("aria-label", "Assignee ID expression syntax: JavaScript");
  await expect(expressionRow).toContainText(source);
  await expect(formSummary).toHaveAttribute("aria-label", `Task form: ${formName} · Version 2`);
  const reopened = await read();
  expect(taskInputs(reopened)).toEqual(taskInputs(beforeReopen));
  expect(reopened.state.variables).toHaveLength(1);
  expect(reopened.state.variables[0]).toMatchObject(assignedPerson);
  const expectedValues = { ...values, FormVersion: "2" };
  for (const [key, value] of Object.entries(expectedValues)) {
    const persisted = taskInputs(reopened).find(input => input.referenceKey === key)?.value;
    if (value !== null && typeof value === "object") {
      expect(persisted, `Canonical structured carrier for ${key}`).toEqual({ expressionType: "Object", value: JSON.stringify(value) });
      expect(JSON.parse(persisted!.value as string), `Exact persisted structure for ${key}`).toEqual(value);
    } else {
      expect(persisted, `Persisted ${key}`)
        .toEqual(key === "AssigneeId" ? { expressionType: "JavaScript", value: source } : literal(value));
    }
  }
  const validation = await api<{ errors: unknown[] }>(`${draftPath}/validations`);
  expect(validation.errors, "The completed grouped-input draft has no validation findings").toEqual([]);
  await formSummary.scrollIntoViewIfNeeded();
  await capture(page, info, "reopened-grouped-inputs");
  const evidence = info.outputPath("grouped-input-persistence.json");
  writeFileSync(evidence, JSON.stringify({ definitionId, draftId: submitted.draftId, formId, formVersion: "2", beforeReopen, reopened, validation }, null, 2));
  await info.attach("grouped-input-persistence.json", { path: evidence, contentType: "application/json" });
}
