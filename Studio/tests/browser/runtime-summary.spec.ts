import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const appearance of [{ preset: "classic", mode: "light" }, { preset: "classic", mode: "dark" }, { preset: "meridian", mode: "dark" }, { preset: "meridian", mode: "dim" }]) for (const width of [320, 1440]) {
  const theme = `${appearance.preset}-${appearance.mode}`;
  test(`runtime summary hierarchy, resource actions and exact copy (${theme}, ${width}px)`, async ({ page, context }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.setViewportSize({ width, height: 1100 });
    await page.goto(`/runtime-summary.html?theme=${appearance.mode}&preset=${appearance.preset}&backend=test-backend`);
    const panel = page.getByRole("region", { name: "Test run summary", exact: true });
    await expect(panel.locator(".wf-status-badge")).toHaveText("Completed");
    await expect(panel.getByRole("heading", { name: "Latest test run" })).toBeVisible();
    await expect(panel.getByText("Waiting triggers", { exact: true })).toBeHidden();
    await expect(panel.getByRole("button", { name: "Copy Artifact ID", exact: true })).toBeHidden();
    await expect(panel.getByRole("link", { name: "View artifact", exact: true })).toHaveAttribute("href", /\/workflows\/executables\/artifact-7e88b5cd7828-exact-very-long-reference-for-copy\?backend=test-backend$/);
    await panel.getByRole("button", { name: "Open run", exact: true }).click();
    await expect(page.getByLabel("Opened run", { exact: true })).toHaveText("run-exact-14qxVV6LzBr");
    await panel.screenshot({ path: test.info().outputPath("runtime-summary-collapsed.png") });

    const disclosure = panel.getByRole("button", { name: "Technical details", exact: true });
    await disclosure.focus(); await disclosure.press("Enter");
    await expect(disclosure).toHaveAttribute("aria-expanded", "true");
    const copy = panel.getByRole("button", { name: "Copy Artifact ID", exact: true });
    await copy.focus(); await copy.press("Enter");
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("artifact-7e88b5cd7828-exact-very-long-reference-for-copy");
    await expect(panel.getByRole("status").filter({ hasText: "Artifact ID copied" })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await panel.screenshot({ path: test.info().outputPath("runtime-summary-expanded.png") });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const result = await (window as Window & { axe: { run(root: Element): Promise<{ violations: Array<{ id: string; impact: string | null }> }> } }).axe.run(document.querySelector(".wf-runtime-card")!);
      return result.violations.filter(value => value.impact === "serious" || value.impact === "critical").map(value => value.id);
    });
    expect(violations).toEqual([]);
    await disclosure.press("Enter");
    await expect(copy).toBeHidden();
  });
}

test("runtime summary preserves waiting, faults and observation warnings", async ({ page }) => {
  await page.goto("/runtime-summary.html");
  const panel = page.getByRole("region", { name: "Test run summary", exact: true });
  const state = page.getByRole("combobox", { name: "Fixture execution state", exact: true });
  await state.selectOption("Suspended");
  await expect(panel.locator(".wf-status-badge")).toHaveText("Waiting");
  await expect(panel.getByRole("heading", { name: "Waiting triggers", exact: true })).toBeVisible();
  await state.selectOption("Faulted");
  await expect(panel.getByRole("button", { name: "Review incidents", exact: true })).toBeVisible();
  await expect(panel.getByRole("heading", { name: "Waiting triggers", exact: true })).toBeHidden();
  await state.selectOption("Accepted");
  await expect(panel.locator(".wf-runtime-outcome")).toContainText("Waiting for execution status");
  await expect(panel).not.toContainText("0 incidents");
  await state.selectOption("Paused");
  await expect(panel.locator(".wf-runtime-outcome")).toContainText("Last confirmed");
  await expect(panel.locator(".wf-test-drive-stale")).toBeVisible();
  await state.selectOption("Disconnected");
  await expect(panel.getByRole("button", { name: "Reconnect execution", exact: true })).toBeVisible();
  await expect(panel.getByText("Execution observation disconnected.", { exact: true })).toBeVisible();
  await state.selectOption("Expired");
  await expect(panel.locator(".wf-status-badge")).toHaveText("Expired");
});

test("the composed editor Runtime panel follows its real execution observer", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&run-canvas=unique&execution-motion=causal&theme=light&backend=fixture-backend&runtime-summary");
  await page.getByRole("button", { name: "Test Run", exact: true }).click();
  await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
  await page.getByRole("tab", { name: "Runtime", exact: true }).click();
  const panel = page.getByRole("region", { name: "Test run summary", exact: true });
  await expect(panel.locator(".wf-status-badge")).toHaveText("Running");
  await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
  await expect(panel.locator(".wf-status-badge")).toHaveText("Completed", { timeout: 10000 });
  await expect(panel.getByRole("heading", { name: "Waiting triggers", exact: true })).toBeHidden();
  const artifact = panel.getByRole("link", { name: "View artifact", exact: true });
  await expect(artifact).toHaveAttribute("href", "/workflows/executables/editor-artifact?backend=fixture-backend");
  await page.screenshot({ path: test.info().outputPath("composed-runtime-summary.png"), fullPage: true });
  // This fixture deliberately leaves autosave off; save before exercising guarded navigation.
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByRole("button", { name: "Save", exact: true })).toBeEnabled();
  await artifact.click();
  await expect(page).toHaveURL(/\/workflows\/executables\/editor-artifact\?backend=fixture-backend$/);
});
