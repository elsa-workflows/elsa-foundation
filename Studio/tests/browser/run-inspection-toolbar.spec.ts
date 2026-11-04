import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const host of ["editor", "instance"]) for (const theme of ["light", "dark"]) for (const width of [320, 390, 1440]) {
  test(`groups run controls without overflow (${host}, ${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1100 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=unique&execution-motion=causal&toolbar-long-name&theme=${theme}`);
    if (host === "editor") {
      await page.getByRole("button", { name: "Test Run", exact: true }).click();
      await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
      await expect(page.locator(".wf-editor-toolbar .wf-attached-test-run")).toHaveCount(0);
    }
    const actions = host === "instance" ? page.getByRole("group", { name: "Run workspace actions", exact: true })
      : page.getByRole("group", { name: "Execution controls", exact: true });
    const toggle = actions.getByRole("button", { name: "Pause live updates", exact: true });
    await expect(toggle).toBeEnabled();
    const canvas = host === "instance" ? page.getByRole("region", { name: "Workflow run canvas", exact: true }) : page.getByLabel("Workflow canvas", { exact: true });
    await expect(canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ })).toHaveAccessibleName(/Run state: Completed/);
    await toggle.press("Enter");
    const resume = actions.getByRole("button", { name: "Resume live updates", exact: true });
    await expect(resume).toBeFocused();
    await expect(actions.getByText(/^Live updates paused/)).toBeVisible();
    const refresh = actions.getByRole("button", { name: "Refresh execution", exact: true });
    await refresh.press("Enter");
    await expect(resume).toHaveAttribute("aria-pressed", "false");
    await resume.press("Enter");
    await expect(toggle).toBeFocused();
    if (host === "instance") await expect(actions.getByRole("button", { name: "Open definition", exact: true })).toBeVisible();
    else {
      await expect(page.getByRole("region", { name: "Test run summary", exact: true }).getByRole("button", { name: "Open run", exact: true })).toBeVisible();
      await expect(actions.getByRole("button", { name: "Return to editing", exact: true })).toBeVisible();
    }
    const bounds = await actions.boundingBox();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width + 1);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width + 1);
    await page.screenshot({ path: test.info().outputPath("run-toolbar.png"), fullPage: true });
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const result = await (window as unknown as { axe: { run(root: Element): Promise<{ violations: { id: string; impact: string }[] }> } }).axe.run(document.querySelector(".activity-duplicate-editor")!);
      return result.violations.filter(value => value.impact === "critical" || value.impact === "serious").map(value => value.id);
    });
    expect(violations).toEqual([]);
  });
}

test("opens secondary run actions with the keyboard and restores focus on Escape", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&host=instance&run-canvas=unique&toolbar-explain");
  const more = page.getByRole("button", { name: "More run actions", exact: true });
  await more.press("ArrowDown");
  await expect(page.getByRole("menuitem", { name: "Explain", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(more).toBeFocused();
  await expect(page.getByRole("menu", { name: "More run actions", exact: true })).toBeHidden();
});

for (const theme of ["light", "dark"]) test(`retains expanded evidence through Runtime pause, refresh and completion (${theme})`, async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(`/?mode=activity-duplicate-editor&run-canvas=repeated&execution-motion=causal&theme=${theme}`);
  await page.getByRole("button", { name: "Test Run", exact: true }).click();
  await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
  await page.getByLabel("Workflow canvas", { exact: true }).getByRole("button", { name: /^Notify buyer\. Node ID:/ }).click();
  await page.locator(".wf-execution-choice").click();
  await page.getByRole("list", { name: "Recorded activity executions", exact: true }).getByRole("button", { name: /^Execution 4 (Running|Completed)/ }).click();
  await expect(page.getByText("Current: 4", { exact: true })).toBeVisible();
  const details = page.locator(".wf-activity-execution-details");
  await details.locator("summary").click();
  const original = await details.elementHandle();
  await page.getByRole("tab", { name: "Runtime", exact: true }).click();
  expect(await original!.evaluate(element => element.isConnected)).toBe(true);
  await page.getByRole("button", { name: "Pause live updates", exact: true }).click();
  await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
  await page.getByRole("button", { name: "Refresh execution", exact: true }).click();
  const runtime = page.getByRole("region", { name: "Test run summary", exact: true });
  await expect(runtime.locator(".wf-status-badge")).toHaveText("Completed");
  await expect(runtime.getByText(/^Live updates paused/)).not.toContainText("The workflow keeps running");
  await expect(runtime.getByRole("button", { name: "Resume live updates", exact: true })).toBeEnabled();
  await page.getByRole("tab", { name: "Inspector", exact: true }).click();
  expect(await original!.evaluate(element => element.isConnected)).toBe(true);
  await expect(details).toHaveAttribute("open", "");
  await expect(page.getByText("Current: 4", { exact: true })).toBeVisible();
  await expect(page.getByText("Completion receipt: 4", { exact: true })).toBeVisible();
});

test.describe("touch targets", () => {
  test.use({ hasTouch: true });
  test("keeps compact run tools easy to tap", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 900 });
    await page.goto("/?mode=activity-duplicate-editor&host=instance&run-canvas=unique&toolbar-explain");
    const actions = page.getByRole("group", { name: "Run workspace actions", exact: true });
    for (const label of ["Runs", "Open definition", "Refresh execution", "Pause live updates", "More run actions"]) {
      const button = actions.getByRole("button", { name: label, exact: true });
      await expect(button).toBeEnabled();
      const box = (await button.boundingBox())!;
      expect(box.width).toBeGreaterThanOrEqual(44);
      expect(box.height).toBeGreaterThanOrEqual(44);
    }
  });
});
