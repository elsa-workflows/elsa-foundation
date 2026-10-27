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
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    const transform = await viewport.getAttribute("style");
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
