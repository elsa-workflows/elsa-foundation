import { expect, test } from "@playwright/test";
import { autosaveDelayMs } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/constants";

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`separates workflow views and retains edited declarations (${theme}, ${width}px)`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/?mode=activity-duplicate-editor&theme=${theme}`);
    if (width < 600) {
      await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
      await page.getByRole("button", { name: "Collapse activities panel", exact: true }).click();
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

test("clears Autosaving when autosave is turned off during debounce", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/?mode=activity-duplicate-editor&theme=light&autosave");
  const toolbar = page.locator(".wf-editor-toolbar");
  const status = toolbar.getByRole("status");
  await expect(toolbar.getByRole("button", { name: "More workflow actions" })).toBeVisible();
  await expect(status).not.toHaveText("Autosaving...", { timeout: 10_000 });

  const views = page.getByRole("tablist", { name: "Editor view tabs" });
  await views.getByRole("tab", { name: "Variables", exact: true }).click();
  await page.getByRole("button", { name: "Add variable", exact: true }).click();
  const variableName = page.getByRole("textbox", { name: "Variable name", exact: true });
  await variableName.fill("Customer");
  await expect(status).toHaveText("Autosaving...");
  await expect(status).toHaveText("Autosaved", { timeout: 10_000 });

  await variableName.fill("Customer2");
  await expect(status).toHaveText("Autosaving...");
  await page.screenshot({ path: testInfo.outputPath("autosave-pending.png") });

  await toolbar.getByRole("button", { name: "More workflow actions" }).click();
  await page.getByRole("menuitemcheckbox", { name: /Autosave/ }).click();
  await expect(status).toHaveText("Unsaved changes");
  await expect(status).toHaveAttribute("data-unsaved", "true");
  await expect(status).not.toHaveAttribute("data-pending", "true");
  await page.waitForTimeout(autosaveDelayMs + 200);
  await expect(status).toHaveText("Unsaved changes");
  await page.screenshot({ path: testInfo.outputPath("autosave-cancelled.png") });

  await toolbar.getByRole("button", { name: "Undo", exact: true }).click();
  await expect(variableName).toHaveValue("Customer");
  await expect(status).toHaveText("Autosaved");
});
