import { expect, test, type Locator } from "@playwright/test";

for (const width of [320, 1440]) {
  test(`run history honors the public Minimal control height and keyboard behavior (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/golden-runs.html?workbench-shell=1");
    await page.getByRole("button", { name: "Inspect workflow run run-01 · Billing workflow", exact: true }).click();
    const timeline = page.getByRole("region", { name: "Run timeline", exact: true });
    await expect(timeline).toBeVisible();
    const root = page.locator("html");
    const previousStyle = await root.getAttribute("data-workspace-style");
    // This fixture hosts the actual workbench without Studio's appearance menu. Exercise
    // the documented public host styling contract; its existing SDK data stays unchanged.
    await root.evaluate(element => element.setAttribute("data-workspace-style", "minimal"));
    try {
      const search = timeline.getByRole("searchbox", { name: "Search recorded events", exact: true });
      const submit = timeline.getByRole("button", { name: "Search", exact: true });
      const surfaces: Array<[string, Locator]> = [
        ["Search field", search.locator("xpath=ancestor::*[contains(concat(' ', normalize-space(@class), ' '), ' studio-form-control ')][1]")],
        ["Search surface", search.locator("..")],
        ...["History scope", "History detail", "History category"].map(name => [name, timeline.getByRole("combobox", { name, exact: true })] as [string, Locator]),
        ["Search button", submit],
        ["Refresh button", timeline.getByRole("button", { name: "Refresh run history", exact: true })],
        ["Live button", timeline.getByRole("button", { name: "Pause live updates", exact: true })],
        ["Follow button", timeline.getByRole("button", { name: "Follow latest", exact: true })]
      ];
      for (const [name, control] of surfaces) {
        await expect(control).toBeVisible();
        await expect.poll(() => control.evaluate(element =>
          Number.parseFloat(getComputedStyle(element).getPropertyValue("--studio-control-height"))),
        { message: `${name}: the supported Minimal density is active` }).toBe(36);
        // Appearance changes animate the control height. Check its settled native
        // geometry without weakening the public role or the existing tolerance.
        await expect.poll(() => control.evaluate(element =>
          element.getBoundingClientRect().height + 0.5 - Number.parseFloat(getComputedStyle(element).getPropertyValue("--studio-control-height"))),
        { message: `${name}: rendered height honors the public role` }).toBeGreaterThanOrEqual(0);
      }
      await search.focus();
      await page.keyboard.press("Tab");
      await expect(submit).toBeFocused();
      await expect(submit).toHaveCSS("outline-style", "solid");
      await expect(submit).toHaveCSS("outline-width", "2px");
      await page.keyboard.press("Shift+Tab");
      await expect(search).toBeFocused();
      await search.fill("bookmark");
      expect(new URL(page.url()).searchParams.get("historySearch")).toBeNull();
      await page.keyboard.press("Tab");
      await page.keyboard.press("Enter");
      await expect.poll(() => new URL(page.url()).searchParams.get("historySearch")).toBe("bookmark");
      await timeline.getByRole("button", { name: "Clear filters", exact: true }).press("Enter");
      await expect(search).toHaveValue("");
      await expect.poll(() => new URL(page.url()).searchParams.get("historySearch")).toBeNull();
      const live = timeline.getByRole("button", { name: "Pause live updates", exact: true });
      await live.press("Enter");
      await expect(timeline.getByRole("button", { name: "Resume live updates", exact: true })).toHaveAttribute("aria-pressed", "false");
      const follow = timeline.getByRole("button", { name: "Follow latest", exact: true });
      await follow.press("Enter");
      await expect(timeline.getByRole("button", { name: "Following latest", exact: true })).toHaveAttribute("aria-pressed", "true");
      await expect(timeline.getByRole("button", { name: "Resume live updates", exact: true })).toHaveAttribute("aria-pressed", "false");
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: test.info().outputPath("run-history-minimal.png"), fullPage: true });
    } finally {
      await root.evaluate((element, previous) => {
        if (previous === null) element.removeAttribute("data-workspace-style");
        else element.setAttribute("data-workspace-style", previous);
      }, previousStyle);
    }
  });
}

