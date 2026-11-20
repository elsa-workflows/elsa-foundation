import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`contextual refresh preserves the executable view at ${width}px in ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.clock.install();
    await page.goto(`/collection-ux.html?theme=${theme}`);
    const header = page.locator(".studio-page-header");
    const refresh = header.getByRole("button", { name: "Refresh executables", exact: true });
    const options = header.locator('button[aria-haspopup="menu"]');
    const table = page.getByRole("table", { name: "Workflow executables", exact: true });
    await expect(table).toBeVisible();
    await expect(refresh).toBeEnabled();
    await expect(header.locator('[data-studio-slot="studio.page-header.refresh"]')).toHaveCount(1);
    await table.evaluate(element => element.setAttribute("data-refresh-witness", "retained"));
    await expect.poll(() => page.evaluate(() => document.getAnimations().filter(animation => animation.playState === "running").length)).toBe(0);
    const refreshAppearance = await refresh.evaluate(element => {
      const style = getComputedStyle(element);
      return { opacity: style.opacity, color: style.color };
    });

    await options.focus();
    await options.press("ArrowDown");
    const manual = page.getByRole("menuitemradio", { name: "Manual", exact: true });
    await expect(manual).toBeChecked();
    await expect(manual).toBeFocused();
    await expect(refresh).toBeEnabled();
    expect(await refresh.evaluate(element => {
      const style = getComputedStyle(element);
      return { opacity: style.opacity, color: style.color };
    })).toEqual(refreshAppearance);
    await page.screenshot({ path: info.outputPath("refresh-options.png"), fullPage: true });
    await manual.press("ArrowDown");
    const automatic = page.getByRole("menuitemradio", { name: "Every 15 seconds", exact: true });
    await expect(automatic).toBeFocused();
    await automatic.press("Enter");
    await expect(options).toBeFocused();
    await expect(header).toContainText(/15/);
    await page.clock.runFor(15_000);
    await expect(refresh).toBeEnabled();
    await expect(table).toHaveAttribute("data-refresh-witness", "retained");
    await page.screenshot({ path: info.outputPath("auto-refresh.png"), fullPage: true });

    await options.click();
    await expect(automatic).toBeChecked();
    await manual.click();
    await expect(options).toBeFocused();
    await refresh.click();
    await expect(refresh).toBeEnabled();
    await expect(table).toHaveAttribute("data-refresh-witness", "retained");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

test("only opted-in views show refresh, including empty Runs", async ({ page }) => {
  await page.goto("/collection-ux.html?view=runs");
  await expect(page.getByRole("heading", { name: "No workflow runs yet", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Refresh workflow runs", exact: true })).toBeEnabled();
  await page.goto("/collection-ux.html?view=settings");
  await expect(page.getByRole("heading", { name: "Host policy", exact: true })).toBeVisible();
  await expect(page.locator('[data-studio-slot="studio.page-header.refresh"]')).toHaveCount(0);
});