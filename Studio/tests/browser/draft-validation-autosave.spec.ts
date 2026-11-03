import { expect, test } from "@playwright/test";

async function reads(page: import("@playwright/test").Page) {
  return page.evaluate(() => (window as Window & { staleValidationReads?: boolean[] }).staleValidationReads ?? []);
}

for (const theme of ["light", "dark"]) {
  for (const save of ["normal", "slow-save"]) {
    test(`deleted Run Etl diagnostics clear after ${save} autosave (${theme})`, async ({ page }) => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.goto(`/?mode=activity-duplicate-editor&theme=${theme}&stale-validations=${save}`);
      const validation = page.locator(".wf-validation");
      await expect(validation).toContainText('Required input "Pipeline"');
      await expect.poll(() => reads(page)).toEqual([true]);
      const node = page.locator('.wf-canvas .react-flow__node[data-id="write-one"]');
      await node.focus(); await page.keyboard.press("Enter");
      await page.getByRole("button", { name: "Delete Run Etl", exact: true }).press("Enter");
      await expect(node).toHaveCount(0);
      await expect.poll(() => reads(page), { timeout: 10000 }).toEqual([true, false]);
      await expect(validation).toContainText("No validation errors");
      await expect(validation).not.toContainText("Pipeline");
      if (save === "normal") {
        await page.getByRole("button", { name: "Undo", exact: true }).click();
        await expect(node).toBeVisible();
        await expect.poll(() => reads(page)).toEqual([true, false, true]);
        await expect(validation).toContainText('Required input "Pipeline"');
      } else {
        await page.reload();
        await expect(page.locator('.wf-canvas .react-flow__node[data-id="write-two"]')).toBeVisible();
        await expect(node).toHaveCount(0);
        await expect.poll(() => reads(page)).toEqual([false]);
        await expect(validation).toContainText("No validation errors");
      }
    });
  }
}
