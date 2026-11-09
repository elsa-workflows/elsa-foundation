import { expect, test } from "@playwright/test";

for (const mode of ["light", "dark"] as const) {
  for (const width of [1440, 390]) {
    test(`scrolls workflow Properties above the bottom panel at ${width}px in ${mode}`, async ({ page }) => {
      await page.setViewportSize({ width, height: 720 });
      await page.addInitScript(mode => {
        localStorage.setItem("elsa-studio-theme-mode", mode);
      }, mode);
      await page.goto("/workspace-appearance.html?composition=sequence&bottom-panel");
      if (width < 600) await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
      await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
      await page.getByRole("tab", { name: "Properties", exact: true }).click();

      const properties = page.locator(".wf-properties-view");
      const bottomPanel = page.locator(".bottom-panel");
      await expect.poll(() => properties.evaluate(element => element.scrollHeight - element.clientHeight)).toBeGreaterThan(0);
      const before = (await properties.boundingBox())!;
      expect(before.y + before.height).toBeLessThanOrEqual((await bottomPanel.boundingBox())!.y + 1);

      await page.mouse.move(before.x + before.width / 2, before.y + before.height / 2);
      await page.mouse.wheel(0, 10000);
      await expect.poll(() => properties.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
      const persistenceHelp = properties.locator("#wf-persistence-mode-help");
      await expect(persistenceHelp).toBeInViewport();
      const helpBox = (await persistenceHelp.boundingBox())!;
      expect(helpBox.y + helpBox.height).toBeLessThan((await bottomPanel.boundingBox())!.y);
      expect(await page.evaluate(() => window.scrollY)).toBe(0);

      // Focusing a field above the fold must scroll the same content pane back into view.
      const scrolledTop = await properties.evaluate(element => element.scrollTop);
      const name = properties.getByRole("textbox", { name: "Workflow name", exact: true });
      await name.focus();
      await expect(name).toBeInViewport();
      await expect.poll(() => properties.evaluate(element => element.scrollTop)).toBeLessThan(scrolledTop);
      await expect(bottomPanel).toBeVisible();
      await page.getByRole("tablist", { name: "Editor view tabs" }).getByRole("tab", { name: "Versions", exact: true }).click();
      await expect(page.getByText("No published versions yet.", { exact: true })).toBeInViewport();
    });
  }
}

for (const mode of ["light", "dark"] as const) {
  test(`keeps saved workflow metadata readable at 360px in ${mode}`, async ({ page }) => {
    await page.setViewportSize({ width: 360, height: 900 });
    await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), mode);
    await page.goto("/workspace-appearance.html?composition=sequence");
    await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
    await page.getByRole("tab", { name: "Properties", exact: true }).click();
    const collapseActivities = page.getByRole("button", { name: "Collapse activities panel", exact: true });
    if (await collapseActivities.isVisible()) await collapseActivities.click();
    for (const label of ["Workflow name", "Workflow description"]) {
      const field = page.getByRole("textbox", { name: label, exact: true });
      await expect(field).toBeInViewport();
      expect((await field.boundingBox())!.width).toBeGreaterThanOrEqual(120);
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}
