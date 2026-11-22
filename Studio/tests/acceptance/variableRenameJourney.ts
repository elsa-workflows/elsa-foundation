import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
const cleanupRequestTimeout = 15_000;

/**
 * Spec 1222 FR-021/FR-022 on the real hosts: renaming a workflow variable updates the script read that names it and
 * lists the computed read it cannot update, with a way to go to it. An undeclared read also exercises the real
 * Workflow Editor validation footer and its input navigation. This disposable invalid draft is never executed.
 */
export async function variableRenameJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
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
  const sequence = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const line = await version("Elsa.Activities.Primitives.Activities.WriteLine");
  // One literal read the rename can follow, one computed read it can only flag, and an unrelated missing declaration
  // that keeps VF-READ-002 present before and after the rename without changing either rename count.
  const script = "'Total: ' + variables.order.total + ' / ' + variables['ord' + 'er'].total + ' / ' + variables.missingForValidation.total";
  const submitted = await api<{ definition: { id: string } }>("/design/workflows/definitions/submit", "POST", {
    name: `Variable rename ${info.project.name} ${Date.now()}`,
    state: {
      inputs: [], outputs: [],
      variables: [{ referenceKey: "order-key", name: "order", type: { alias: "Object", collectionKind: "Single" }, storageDriverType: null, default: null }],
      rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [],
        structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [
          { nodeId: "message", activityVersionId: line, inputs: [{ referenceKey: "text", value: { expressionType: "JavaScript", value: script } }], outputs: [] }
        ] } } }
    }
  });
  registerCleanup(async () => {
    await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE", undefined, cleanupRequestTimeout);
  });

  const designerUrl = new URL(page.url());
  designerUrl.searchParams.set("definition", submitted.definition.id);
  await page.goto(designerUrl.toString());
  await expect(page.locator('.wf-sequence-step[data-id="message"]')).toBeVisible();
  await page.locator('.wf-sequence-step[data-id="message"]').click();
  if (info.project.name.startsWith("narrow")) {
    await page.getByRole("button", { name: "Expand inspector panel", exact: true }).click();
  }
  const inspectorSections = page.getByRole("tablist", { name: "Activity inspector sections", exact: true });
  await inspectorSections.getByRole("tab", { name: "Outputs", exact: true }).click();
  await expect(inspectorSections.getByRole("tab", { name: "Outputs", exact: true })).toHaveAttribute("aria-selected", "true");
  // At narrow widths the inspector covers the editor view tabs; the activities panel starts collapsed.
  if (info.project.name.startsWith("narrow")) {
    const collapse = page.getByRole("button", { name: "Collapse inspector panel", exact: true });
    if (await collapse.isVisible()) await collapse.click();
  }
  const views = page.getByRole("tablist", { name: "Editor view tabs", exact: true });
  await views.getByRole("tab", { name: "Variables", exact: true }).click();
  const name = page.getByRole("textbox", { name: "Variable name", exact: true });
  await expect(name).toHaveValue("order");

  const planned = page.waitForResponse(response => new URL(response.url()).pathname.endsWith("/scoped-variables/rename-plan"));
  await name.fill("purchase");
  await name.press("Tab");
  expect((await planned).ok()).toBe(true);
  const notice = page.getByRole("region", { name: "Variable rename", exact: true });
  await expect(notice).toContainText("Renamed 'order' to 'purchase'. 1 script read was updated; 1 reference needs your attention.");
  await expect(notice).toContainText("reads variables by a computed name and could read 'order'");
  await capture(page, info, "rename-notice");

  await notice.getByRole("button", { name: "Write Line · Text, go to input", exact: true }).click();
  await expect(views.getByRole("tab", { name: "Designer", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(inspectorSections.getByRole("tab", { name: "Inputs", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("button", { name: "Copy Node ID", exact: true })).toBeVisible();
  const text = page.getByRole("group", { name: "Text property", exact: true });
  await expect(text).toBeVisible();
  await expect(text).toContainText("variables.purchase.total");
  await expect(text).toContainText("variables['ord' + 'er']");
  await expect(text).toContainText("variables.missingForValidation.total");
  // This host activates CodeMirror lazily; retain evidence after the rendered editor has taken focus.
  const renderedEditor = text.locator('[data-studio-code-editor-surface="codemirror"] .cm-content');
  await expect(renderedEditor).toBeVisible();
  await expect(renderedEditor).toContainText("variables.purchase.total");
  await expect(renderedEditor).toBeFocused();
  // Go to moves focus into the flagged input's editor.
  await expect(text.locator("[data-studio-code-editor] :focus")).toHaveCount(1);
  await capture(page, info, "flagged-reference");

  // Keep validation navigation after rename's focus proof: a retained validation request must never supply rename focus.
  // Move to Outputs and focus the footer controls so this second action must itself reopen/focus Inputs.
  await inspectorSections.getByRole("tab", { name: "Outputs", exact: true }).click();
  if (info.project.name.startsWith("narrow")) {
    await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
  }
  const validation = page.locator(".wf-validation");
  const validationToggle = validation.locator(".wf-validation-toggle");
  const validationDetails = validation.locator("#wf-validation-details");
  await expect(validationToggle).toContainText("1 error · 1 warning");
  await expect(validationToggle).toHaveAttribute("aria-expanded", "false");
  await expect(validationDetails).toBeHidden();
  await validationToggle.focus();
  await expect(validationToggle).toBeFocused();
  await capture(page, info, "validation-collapsed");

  await validationToggle.press("Enter");
  await expect(validationToggle).toHaveAttribute("aria-expanded", "true");
  await expect(validationDetails).toBeVisible();
  const findings = validation.getByRole("list", { name: "Validation findings", exact: true }).getByRole("listitem");
  await expect(findings).toHaveCount(2);
  const missingRead = findings.filter({ hasText: "VF-READ-002" });
  await expect(missingRead).toHaveCount(1);
  await expect(missingRead.locator('.wf-validation-severity[data-severity="error"]')).toHaveText("Error");
  await expect(missingRead.locator(".wf-validation-location")).toHaveText("Write Line → Inputs → Text");
  await expect(missingRead).toContainText("reads variable 'missingForValidation'");
  await expect(findings.filter({ hasText: "VF-READ-004" })).toContainText("It could read: 'purchase'.");
  const goToValidationInput = missingRead.getByRole("button", { name: "Go to input", exact: true });
  await goToValidationInput.scrollIntoViewIfNeeded();
  await capture(page, info, "validation-expanded");

  await goToValidationInput.focus();
  await goToValidationInput.press("Enter");
  await expect(views.getByRole("tab", { name: "Designer", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(inspectorSections.getByRole("tab", { name: "Inputs", exact: true })).toHaveAttribute("aria-selected", "true");
  const validationInput = page.getByRole("group", { name: "Text property", exact: true });
  await expect(validationInput).toBeVisible();
  const validationEditor = validationInput.locator('[data-studio-code-editor-surface="codemirror"] .cm-content');
  await expect(validationEditor).toBeVisible();
  await expect(validationEditor).toContainText(script.replace("variables.order.total", "variables.purchase.total"));
  await expect(validationEditor).toBeFocused();
  await expect(validationInput.locator("[data-studio-code-editor] :focus")).toHaveCount(1);
  await capture(page, info, "validation-input-focused");

}
