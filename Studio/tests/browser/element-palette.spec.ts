import { expect, test } from "@playwright/test";

for (const host of ["workflow", "activity"]) {
  test(`${host} activity choices follow the destination scope`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=sequence&palette-context&theme=dark`);
    const search = page.getByRole("searchbox", { name: "Search activity palette" });
    await search.fill("BPMN");
    await expect(page.getByRole("treeitem", { name: /^BPMN Process,/ })).toBeVisible();
    await expect(page.getByRole("treeitem", { name: /^BPMN Decision,/ })).toHaveCount(0);
    await search.fill("Complete");
    await expect(page.getByRole("treeitem", { name: /^Complete,/ })).toHaveCount(host === "activity" ? 1 : 0);

    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&theme=dark`);
    // This remains an activity-only search until the unified elements palette lands.
    await page.locator(".wf-palette-search input").fill("BPMN Decision");
    await expect(page.getByRole("treeitem", { name: /^BPMN Decision,/ })).toBeVisible();
  });
}
