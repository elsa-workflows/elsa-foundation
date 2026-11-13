import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`separates workflow views and retains edited declarations (${theme}, ${width}px)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=activity-duplicate-editor&theme=${theme}`);
    if (width < 600) {
      await expect(page.getByRole("button", { name: "Expand inspector panel", exact: true })).toBeVisible();
      await expect(page.getByRole("button", { name: "Expand activities panel", exact: true })).toBeVisible();
    }
    const views = page.getByRole("tablist", { name: "Editor view tabs" });
    await expect(views.getByRole("tab")).toHaveText(["Designer", "Code", "Properties", "Variables", "Inputs", "Outputs", "Versions"]);
    await views.getByRole("tab", { name: "Properties", exact: true }).click();
    await expect(page.locator(".wf-canvas-shell .wf-properties-view h3")).toHaveText(["Information", "Failure handling", "Persistence"]);
    const declarations = [
      { tab: "Variables", add: "Add variable", field: "Variable name", value: "Customer" },
      { tab: "Inputs", add: "Add input", field: "Input name", value: "Order" },
      { tab: "Outputs", add: "Add output", field: "Output name", value: "Receipt" }
    ];
    for (const item of declarations) {
      await views.getByRole("tab", { name: item.tab, exact: true }).click();
      await page.getByRole("button", { name: item.add, exact: true }).click();
      await page.getByRole("textbox", { name: item.field, exact: true }).fill(item.value);
    }
    const versions = views.getByRole("tab", { name: "Versions", exact: true });
    await versions.click();
    await expect(page.getByText("No published versions yet.", { exact: true })).toBeVisible();
    for (const item of declarations) {
      await views.getByRole("tab", { name: item.tab, exact: true }).click();
      await expect(page.getByRole("textbox", { name: item.field, exact: true })).toHaveValue(item.value);
    }
    await versions.focus();
    await versions.press("Home");
    await expect(views.getByRole("tab", { name: "Designer", exact: true })).toHaveAttribute("aria-selected", "true");
    await page.keyboard.press("End");
    await expect(versions).toHaveAttribute("aria-selected", "true");
    await expect(versions).toBeInViewport();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
    await page.screenshot({ path: testInfo.outputPath("workflow-view-tabs.png") });
  });
}
