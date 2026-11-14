import { expect, type Page, type TestInfo } from "@playwright/test";

/** Records live only in the runner-owned disposable Workbench store, which is removed at teardown. */
export async function activityDefinitionTitleJourney(page: Page, info: TestInfo, capture: (page: Page, info: TestInfo, name: string) => Promise<void>) {
  await page.goto("/workflows/activity-definitions");
  await page.locator(".studio-page-header").getByRole("button", { name: "Create activity", exact: true }).click();
  const name = `Activity identity ${info.project.name} ${Date.now()}`;
  await page.getByRole("textbox", { name: "Display name", exact: true }).fill(name);
  const provider = page.getByRole("combobox", { name: "Implementation type", exact: true });
  if (await provider.count()) {
    const graph = provider.locator('option[value^="elsa.activity-graph|"]');
    await provider.selectOption((await graph.first().getAttribute("value"))!);
  }
  await page.locator("label.wf-root-card").filter({ has: page.getByRole("radio", { name: "Sequence", exact: true }) }).click();
  await page.getByRole("button", { name: "Create definition", exact: true }).click();
  const title = page.locator("#activity-draft-title");
  const tab = page.locator('.studio-workspace-tab-button[aria-selected="true"] .studio-workspace-tab-title');
  await expect(title).toHaveText(name);
  await expect(tab).toHaveText(name);
  const editor = new URL(page.url());
  const definitionId = editor.searchParams.get("definition");
  const draftId = editor.searchParams.get("draft");
  expect(definitionId).toBeTruthy(); expect(draftId).toBeTruthy();
  await capture(page, info, "activity-created");

  for (const state of ["activity-reloaded", "activity-reopened"]) {
    if (state === "activity-reloaded") await page.reload();
    else {
      await page.goto("/workflows/activity-definitions");
      await page.goto(editor.href);
    }
    await expect(title).toHaveText(name);
    await expect(tab).toHaveText(name);
    expect(new URL(page.url()).searchParams.get("definition")).toBe(definitionId);
    expect(new URL(page.url()).searchParams.get("draft")).toBe(draftId);
    await expect(page.getByRole("region", { name: "Provider implementation editor", exact: true })).toBeVisible();
    await capture(page, info, state);
  }
}
