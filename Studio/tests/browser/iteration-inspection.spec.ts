import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const host of ["editor", "instance"]) for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`selects exact occurrences without redundant controls (${host}, ${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=repeated&theme=${theme}`);
    if (host === "editor") {
      await page.getByRole("button", { name: "Test Run", exact: true }).click();
      await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    }
    const canvas = host === "instance" ? page.getByRole("region", { name: "Workflow run canvas", exact: true })
      : page.getByLabel("Workflow canvas", { exact: true });
    await canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ }).click();
    const details = host === "instance" ? page.getByRole("complementary", { name: "Run details panel", exact: true })
      : page.getByRole("complementary", { name: "Inspector panel", exact: true });
    if (host === "editor") await expect(details).toContainText("Choose a recorded execution");
    // No iteration provenance is present: repeated executions must keep execution terminology.
    const chooser = details.locator(".wf-execution-choice");
    await expect(chooser).toContainText("10 recorded executions");
    await chooser.press("Enter");
    const list = details.getByRole("list", { name: "Recorded activity executions", exact: true });
    await list.getByRole("button", { name: /^Execution 1 (Completed|Running)/ }).press("Enter");
    await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
    await expect(list).toBeHidden();
    await expect(chooser).toBeFocused();
    await expect(chooser).toContainText("Execution 1 of 10");
    await details.getByRole("button", { name: "Next activity execution", exact: true }).press("Enter");
    await expect(page.getByText("Current: 2", { exact: true })).toBeVisible();
    await expect(chooser).toContainText("Execution 2 of 10");
    await expect(details.getByText(/Iteration \d/)).toHaveCount(0);
    if (host === "instance" && width === 390) await page.getByRole("button", { name: "Back to workflow run canvas", exact: true }).click();
    await canvas.getByRole("button", { name: /^Write Line\. Node ID: write-two/ }).click();
    await expect(details.getByText("Current: 1", { exact: true })).toBeVisible();
    await expect(details.locator(".wf-execution-navigator")).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const result = await (window as Window & { axe: { run(root: Element): Promise<{ violations: Array<{ id: string; impact: string | null }> }> } })
        .axe.run(document.querySelector(".activity-duplicate-editor")!);
      return result.violations.filter(v => v.impact === "serious" || v.impact === "critical").map(v => v.id);
    });
    expect(violations).toEqual([]);
    await page.screenshot({ path: test.info().outputPath("occurrence-inspection.png"), fullPage: true });
  });
}

for (const host of ["editor", "instance"]) test(`keeps the occurrence chooser scrollable on a short ${host} screen`, async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 600 });
  await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=repeated&iteration-provenance=1`);
  if (host === "editor") {
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
  }
  const canvas = host === "instance" ? page.getByRole("region", { name: "Workflow run canvas", exact: true }) : page.getByLabel("Workflow canvas", { exact: true });
  await canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ }).click();
  const chooser = page.locator(".wf-execution-choice");
  await chooser.press("Enter");
  await page.getByRole("list", { name: "Recorded activity executions", exact: true }).getByRole("button", { name: /^Iteration 1 / }).press("Enter");
  await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
  await chooser.press("Enter");
  const popup = page.locator(".wf-execution-list-container");
  expect(await popup.evaluate(el => getComputedStyle(el).overflowY)).toMatch(/auto|scroll/);
  const last = popup.getByRole("button", { name: /^Iteration 10 / });
  await last.scrollIntoViewIfNeeded();
  const [outer, row] = await Promise.all([popup.boundingBox(), last.boundingBox()]);
  expect(row!.y + row!.height).toBeLessThanOrEqual(outer!.y + outer!.height + 1);
  await last.press("Enter");
  await expect(page.getByText("Current: 10", { exact: true })).toBeVisible();
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const result = await (window as Window & { axe: { run(root: Element): Promise<{ violations: Array<{ id: string; impact: string | null }> }> } }).axe.run(document.querySelector(".activity-duplicate-editor")!);
    return result.violations.filter(v => v.impact === "serious" || v.impact === "critical").map(v => v.id);
  });
  expect(violations).toEqual([]);
});
