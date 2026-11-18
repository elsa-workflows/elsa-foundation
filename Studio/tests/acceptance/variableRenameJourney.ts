import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
const cleanupRequestTimeout = 15_000;

/**
 * Spec 1222 FR-021/FR-022 on the real hosts: renaming a workflow variable updates the script read that names it and
 * lists the computed read it cannot update, with a way to go to it. No fixture responses.
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
  // One literal read the rename can follow, and one computed read it can only flag.
  const script = "'Total: ' + variables.order.total + ' / ' + variables['ord' + 'er'].total";
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

  await page.goto(`/workflows/definitions?definition=${submitted.definition.id}&backend=legacy`);
  await expect(page.locator('.wf-sequence-step[data-id="message"]')).toBeVisible();
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
  const notice = page.getByRole("status", { name: "Variable rename", exact: true });
  await expect(notice).toContainText("Renamed 'order' to 'purchase'. 1 script read was updated; 1 reference needs your attention.");
  await expect(notice).toContainText("reads variables by a computed name and could read 'order'");
  await capture(page, info, "rename-notice");

  await notice.getByRole("button", { name: /· text$/ }).click();
  await expect(views.getByRole("tab", { name: "Designer", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(page.getByRole("button", { name: "Copy Node ID", exact: true })).toBeVisible();
  const text = page.locator(".wf-property-row").filter({ has: page.locator(".wf-property-row-header > label").filter({ hasText: /^Text$/ }) });
  await expect(text).toContainText("variables.purchase.total");
  await expect(text).toContainText("variables['ord' + 'er']");
  await capture(page, info, "flagged-reference");
}
