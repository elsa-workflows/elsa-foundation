import { expect, type Page } from "@playwright/test";

/** Uses the current workspace tabs and section navigation before opening a question control. */
export async function openQuestionSection(page: Page, path: string) {
  const workspace = page.locator(".named-question-workspace:visible");
  await expect(workspace).toHaveCount(1);
  if (path.startsWith("query.")) {
    const build = workspace.locator('[data-tab-id="build"]');
    if (await build.getAttribute("aria-selected") !== "true") await build.click();
  } else if (path === "visualization") {
    const properties = workspace.getByRole("button", { name: "Properties", exact: true });
    if (await properties.getAttribute("aria-expanded") !== "true") await properties.click();
  }
  const section = workspace.locator(`[data-query-path="${path}"]`).first();
  if (path.startsWith("query.")) {
    const id = await section.getAttribute("id");
    expect(id, `Question section ${path}`).toBeTruthy();
    const navigation = workspace.getByRole("navigation", { name: "Question sections", exact: true });
    await navigation.locator(`a[aria-controls="${id}"]`).click();
  }
  await expect(section).toBeVisible();
  const trigger = section.locator("button[aria-expanded]").first();
  if (await trigger.count() && await trigger.getAttribute("aria-expanded") !== "true") await trigger.click();
  return section;
}
