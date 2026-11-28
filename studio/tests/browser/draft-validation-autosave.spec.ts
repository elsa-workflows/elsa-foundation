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
      const toggle = validation.getByRole("button");
      await expect(toggle).toHaveAttribute("aria-expanded", "false");
      await toggle.click();
      await expect(validation).toContainText('Required input "Pipeline"');
      await expect.poll(() => reads(page)).toEqual([true]);
      const node = page.locator('.wf-canvas .react-flow__node[data-id="write-one"]');
      await node.focus(); await page.keyboard.press("Enter");
      await page.getByRole("button", { name: "Delete Run Etl", exact: true }).press("Enter");
      await expect(node).toHaveCount(0);
      await expect.poll(() => reads(page), { timeout: 10000 }).toEqual([true, false]);
      await expect(validation).toContainText("No validation issues found");
      await expect(validation).not.toContainText("Pipeline");
      if (save === "normal") {
        await page.getByRole("button", { name: "Undo", exact: true }).click();
        await expect(node).toBeVisible();
        await expect.poll(() => reads(page)).toEqual([true, false, true]);
        await expect(toggle).toHaveAttribute("aria-expanded", "false");
        await toggle.click();
        await expect(validation).toContainText('Required input "Pipeline"');
      } else {
        await page.reload();
        await expect(page.locator('.wf-canvas .react-flow__node[data-id="write-two"]')).toBeVisible();
        await expect(node).toHaveCount(0);
        await expect.poll(() => reads(page)).toEqual([false]);
        await expect(validation).toContainText("No validation issues found");
      }
    });
  }
}

test("validation details navigate to and focus the affected input", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/?mode=activity-duplicate-editor&theme=light&stale-validations=normal");
  const validation = page.locator(".wf-validation");
  const toggle = validation.getByRole("button");

  await expect(toggle).toHaveAttribute("aria-expanded", "false");
  await toggle.click();
  const finding = validation.locator(".wf-validation-item");
  await expect(finding).toContainText('Required input "Pipeline"');
  await expect(finding.locator(".wf-validation-hint")).toContainText("Set a value for this required input");
  await finding.getByRole("button", { name: "Go to input" }).click();

  const inspector = page.getByRole("complementary", { name: /inspector/i });
  await expect(inspector.getByRole("textbox", { name: "Pipeline", exact: true })).toBeFocused();
});

test("full validation details wrap without horizontal overflow on a narrow viewport", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/?mode=activity-duplicate-editor&theme=light&stale-validations=normal");
  const validation = page.locator(".wf-validation");
  await validation.getByRole("button").click();
  const message = validation.locator(".wf-validation-message");

  await expect(message).toContainText('Required input "Pipeline"');
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
  expect(await message.evaluate(element => getComputedStyle(element).whiteSpace)).toBe("normal");
  await page.screenshot({ path: testInfo.outputPath("validation-details-narrow.png") });
});
