import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  test(`follows a directly opened instance and retains selection and viewport through reconnect (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=instance&run-canvas=unique&theme=${theme}`);
    const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
    const node = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await expect(node).toHaveAccessibleName(/Run state: Running/);
    await node.click();
    await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
    const viewport = canvas.locator(".react-flow__viewport");
    const bounds = (await canvas.boundingBox())!;
    await page.mouse.move(bounds.x + 60, bounds.y + bounds.height - 80);
    await page.mouse.down();
    await page.mouse.move(bounds.x + 110, bounds.y + bounds.height - 120, { steps: 8 });
    await page.mouse.up();
    const transform = await viewport.getAttribute("style");
    const route = page.url();
    await page.getByRole("button", { name: "Disconnect fixture reads", exact: true }).click();
    await expect(page.getByText("Execution observation disconnected · last confirmed evidence", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Reconnect fixture reads", exact: true }).click();
    await page.locator(".wf-toolbar").getByRole("button", { name: "Refresh", exact: true }).click();
    await expect(page.getByText("Execution on this canvas", { exact: true })).toBeVisible();
    await expect(node).toHaveAccessibleName(/Selected/);
    expect(await viewport.getAttribute("style")).toBe(transform);
    expect(page.url()).toBe(route);
    await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed\. Selected/);
    await expect(page.getByText("Current: 1", { exact: true })).toBeVisible();
    expect(await viewport.getAttribute("style")).toBe(transform);
    expect(page.url()).toBe(route);
  });

  test(`keeps an explicit repeated occurrence while a directly opened instance completes (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=instance&run-canvas=repeated&theme=${theme}`);
    const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
    const node = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await expect(page.getByText(/Some activities ran more than once/)).toBeVisible();
    await node.click();
    await page.getByRole("button", { name: "10 recorded executions", exact: false }).click();
    await page.getByRole("list", { name: "Recorded activity executions" }).getByRole("button", { name: /^Execution 4 Running/ }).click();
    await expect(page.getByText("Current: 4", { exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Complete fixture execution", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed\. Selected/);
    await expect(page.getByText("Current: 4", { exact: true })).toBeVisible();
    await expect(page.getByText("Current: 10", { exact: true })).toHaveCount(0);
  });
}
