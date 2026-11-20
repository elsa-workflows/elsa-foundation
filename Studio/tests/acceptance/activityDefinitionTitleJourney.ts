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

  const views = page.getByRole("tablist", { name: "Activity Definition authoring views" });
  const geometry = () => views.evaluate(element => {
    const box = element.getBoundingClientRect(), style = getComputedStyle(element);
    return { x: box.x, y: box.y, width: box.width, height: box.height, padding: style.padding, margin: style.margin,
      buttons: Array.from(element.querySelectorAll("button"), button => {
        const bounds = button.getBoundingClientRect();
        return { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height };
      }) };
  });
  const designerGeometry = await geometry();
  for (const id of ["designer", "inputs", "outputs", "outcomes", "variables", "designer"]) {
    const target = views.locator(`[data-tab-id="${id}"]`);
    await target.click();
    await expect(target).toHaveAttribute("aria-selected", "true");
    expect(await geometry()).toEqual(designerGeometry);
    await capture(page, info, `activity-tabs-${id}`);
  }
  // Arrow navigation must preserve focus and geometry too.
  await views.locator('[data-tab-id="designer"]').focus();
  await page.keyboard.press("ArrowRight");
  await expect(views.locator('[data-tab-id="inputs"]')).toBeFocused();
  expect(await geometry()).toEqual(designerGeometry);
  await views.locator('[data-tab-id="designer"]').click();
  await expect(page.getByRole("region", { name: "Provider implementation editor", exact: true })).toBeVisible();


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
