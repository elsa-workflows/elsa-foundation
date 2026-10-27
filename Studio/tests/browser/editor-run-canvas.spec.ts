import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  test(`keeps the existing canvas and viewport when attaching and detaching a run (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&theme=${theme}`);
    const canvas = page.getByLabel("Workflow canvas", { exact: true });
    const node = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await expect(node).toBeVisible();
    const viewport = canvas.locator(".react-flow__viewport");
    await expect(viewport).toHaveAttribute("style", /transform/);
    const original = await viewport.getAttribute("style");
    const bounds = (await canvas.boundingBox())!;
    await page.mouse.move(bounds.x + 120, bounds.y + bounds.height - 70);
    await page.mouse.down();
    await page.mouse.move(bounds.x + 175, bounds.y + bounds.height - 105, { steps: 5 });
    await page.mouse.up();
    await expect(viewport).not.toHaveAttribute("style", original!);
    const transform = await viewport.getAttribute("style");
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    expect(await viewport.getAttribute("style")).toBe(transform);
    const location = page.url();
    await page.getByRole("button", { name: "Return to editing", exact: true }).click();
    await expect(node).toHaveAccessibleName(/State: authoring/);
    expect(await viewport.getAttribute("style")).toBe(transform);
    expect(page.url()).toBe(location);
    await page.getByRole("button", { name: "Show execution", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    const box = (await node.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + 20);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 80, box.y + 90, { steps: 8 });
    await page.mouse.up();
    await expect(page.getByText("The design changed. Run overlays are hidden.")).toBeVisible();
    await expect(node).toHaveAccessibleName(/State: authoring/);
  });

  test(`inspects an explicit repeated occurrence in the same editor (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=repeated&theme=${theme}`);
    const canvas = page.getByLabel("Workflow canvas", { exact: true });
    const node = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(page.getByText(/Some activities ran more than once/)).toBeVisible();
    const viewport = await canvas.locator(".react-flow__viewport").getAttribute("style");
    const route = page.url();
    await node.click();
    const inspector = page.getByLabel("Inspector panel", { exact: true });
    await expect(inspector.getByRole("tab", { name: "Run data", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(inspector.getByText("Choose a recorded execution to inspect its evidence.")).toBeVisible();
    await inspector.getByRole("button", { name: "10 recorded executions", exact: false }).click();
    await inspector.getByRole("list", { name: "Recorded activity executions" }).getByRole("button", { name: /^Execution 4 Completed/ }).click();
    await expect(inspector.getByText("Current: 4", { exact: true })).toBeVisible();
    await expect(node).toHaveAccessibleName(/Run state: Completed\. Selected/);
    await inspector.getByRole("tab", { name: "Configuration", exact: true }).click();
    await inspector.getByRole("tab", { name: "Run data", exact: true }).click();
    await expect(inspector.getByText("Current: 4", { exact: true })).toBeVisible();
    await inspector.getByRole("button", { name: "Execution 4 of 10", exact: false }).click();
    await inspector.getByRole("list", { name: "Recorded activity executions" }).getByRole("button", { name: /^Execution 10 Completed/ }).click();
    await expect(inspector.getByText("Current: 10", { exact: true })).toBeVisible();
    await expect(inspector.getByText("Current: 4", { exact: true })).toHaveCount(0);
    expect(await canvas.locator(".react-flow__viewport").getAttribute("style")).toBe(viewport);
    expect(page.url()).toBe(route);
  });

  test(`keeps attached-run controls within a narrow editor (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=repeated&theme=${theme}`);
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    const status = page.getByRole("group", { name: "Attached Test Run", exact: true });
    await expect(status.getByText(/Some activities ran more than once/)).toBeVisible();
    const box = (await status.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(320);
    await expect(status.getByRole("button", { name: "Return to editing" })).toBeVisible();
    await status.getByRole("button", { name: "Return to editing" }).click();
    await expect(status.getByText("Editing the current draft")).toBeVisible();
  });
}
