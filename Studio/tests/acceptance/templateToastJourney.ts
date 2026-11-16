import { expect, type Dialog, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
// Teardown has its own timeout budget (see test.afterEach in journeys.spec.ts); each call is bounded
// so a stuck backend request cannot hold up the next journey or leave the definition behind unnoticed.
const cleanupRequestTimeout = 15_000;
export async function templateToastJourney(page: Page, info: TestInfo, capture: Capture, registerCleanup: (cleanup: () => Promise<void>) => void) {
  async function revealCanvas() {
    if (!info.project.name.startsWith("narrow")) return;
    for (const name of ["Collapse activities panel", "Collapse inspector panel"]) {
      const collapse = page.getByRole("button", { name, exact: true });
      if (await collapse.isVisible()) await collapse.click();
    }
  }
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
  const workflowName = `Template toast ${info.project.name} ${Date.now()}`;
  const submitted = await api<{ definition: { id: string }; draftId: string }>("/design/workflows/definitions/submit", "POST", {
    name: workflowName,
    state: { inputs: [], outputs: [], variables: [], rootActivity: { nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [],
      structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: "message", activityVersionId: line, inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: "Original saved text" } }], outputs: [] }] } } } }
  });
  const created: { templateId?: string } = {};
  // Registered before the first interaction: the spec's afterEach runs it even when this journey times out.
  registerCleanup(async () => {
    const { templateId } = created;
    if (templateId) {
      const current = await api<{ revision: string }>(`/design/workflows/templates/${templateId}`, "GET", undefined, cleanupRequestTimeout);
      await api(`/design/workflows/templates/${templateId}/delete`, "POST", { expectedRevision: current.revision }, cleanupRequestTimeout);
    }
    await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE", undefined, cleanupRequestTimeout);
  });
  const publicationRequests: string[] = [];
  const templateRequests: string[] = [];
  page.on("request", request => {
    if (request.method() !== "POST") return;
    if (request.url().includes("/publishing/")) publicationRequests.push(request.url());
    if (new URL(request.url()).pathname.endsWith("/design/workflows/templates")) templateRequests.push(request.url());
  });
  await page.goto(`/workflows/definitions?definition=${submitted.definition.id}&backend=legacy`);
  const actions = page.getByRole("button", { name: "More workflow actions", exact: true });
  await expect(page.locator('.wf-sequence-step[data-id="message"]')).toBeVisible();
  await actions.click();
  const autosave = page.getByRole("menuitemcheckbox", { name: /Autosave/ });
  if (await autosave.getAttribute("aria-checked") === "true") await autosave.click();
  await page.keyboard.press("Escape");
  await page.locator('.wf-sequence-step[data-id="message"]').press("Enter");
  const text = page.getByRole("textbox", { name: "Text", exact: true });
  await expect(text).toBeVisible(); await text.fill("Unsaved template text"); await text.press("Tab");
  await actions.click(); await page.getByRole("menuitem", { name: "Save as template", exact: true }).click();
  let dialog = page.getByRole("dialog", { name: "Save as template", exact: true });
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(dialog).toHaveCount(0); await expect(actions).toBeFocused();
  await expect(page.getByRole("group", { name: "Template saved", exact: true })).toHaveCount(0);
  expect(templateRequests).toHaveLength(0);
  // Keyboard-only open. The menu moves focus to its first item on the next animation frame after it
  // opens, so wait for that focus before navigating; pressing Enter earlier can land on the wrong item.
  await actions.press("Enter");
  await expect(page.getByRole("menu", { name: "More workflow actions", exact: true })).toBeVisible();
  await expect(page.getByRole("menuitemcheckbox", { name: /Autosave/ })).toBeFocused();
  const saveAsTemplate = page.getByRole("menuitem", { name: "Save as template", exact: true });
  await page.keyboard.press("ArrowDown");
  await expect(saveAsTemplate).toBeFocused();
  await page.keyboard.press("Enter");
  dialog = page.getByRole("dialog", { name: "Save as template", exact: true });
  await expect(dialog).toBeVisible();
  const name = `Toast starter ${info.project.name} ${Date.now()}`;
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("");
  await expect(dialog.getByRole("button", { name: "Save template", exact: true })).toBeDisabled();
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
  await capture(page, info, "save-form");
  const savedResponse = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/design/workflows/templates"));
  await dialog.getByRole("button", { name: "Save template", exact: true }).click();
  const saved = await (await savedResponse).json() as { id: string; content: unknown };
  created.templateId = saved.id;
  await expect(dialog).toHaveCount(0);
  const toast = page.getByRole("group", { name: "Template saved", exact: true });
  await expect(toast).toBeVisible(); await toast.hover();
  await expect(actions).toBeFocused();
  expect(templateRequests).toHaveLength(1); expect(publicationRequests).toHaveLength(0);
  expect(JSON.stringify(saved.content)).toContain("Unsaved template text");
  const source = await api<{ state: unknown }>(`/design/workflows/drafts/${submitted.draftId}`);
  expect(JSON.stringify(source.state)).not.toContain("Unsaved template text");
  await capture(page, info, "saved-toast");
  await toast.getByRole("button", { name: "View templates", exact: true }).press("Enter");
  await expect(page).toHaveURL(/\/workflows\/templates\?backend=legacy/);
  await expect(page.getByRole("heading", { name: "Workflow templates", exact: true })).toBeVisible();
  const savedTemplate = page.getByRole("button", { name, exact: true });
  await expect(savedTemplate).toBeVisible();
  await savedTemplate.scrollIntoViewIfNeeded();
  await expect(savedTemplate).toBeInViewport();
  await capture(page, info, "template-list");
  const templateTabLabel = await page.getByRole("tablist", { name: "Open screens", exact: true }).getByRole("tab", { selected: true }).getAttribute("aria-label");
  expect(templateTabLabel, "The Templates workspace tab has an accessible name").toBeTruthy();
  // The toast opens another workspace tab so the source workflow keeps its unsaved edits.
  await page.getByRole("tab", { name: new RegExp(`^${workflowName}`) }).click();
  await expect(page.getByRole("textbox", { name: "Text", exact: true })).toHaveValue("Unsaved template text");
  await revealCanvas();
  await expect(page.locator(".wf-sequence-summary").filter({ hasText: "Unsaved template text" })).toBeVisible();
  await page.getByRole("tab", { name: templateTabLabel!, exact: true }).click();
  // The original workflow deliberately retains unsaved edits. Its template copy is persisted;
  // accept only the native reload guard for this owned disposable journey.
  const allowOwnedReload = async (dialog: Dialog) => { expect(dialog.type()).toBe("beforeunload"); await dialog.accept(); };
  page.on("dialog", allowOwnedReload);
  await page.reload();
  page.off("dialog", allowOwnedReload);
  await page.getByRole("button", { name, exact: true }).click();
  await expect(page.locator('.wf-sequence-step[data-id="message"]')).toBeVisible();
  await revealCanvas();
  await expect(page.locator(".wf-sequence-summary").filter({ hasText: "Unsaved template text" })).toBeVisible();
  await capture(page, info, "reopened-template");
}
