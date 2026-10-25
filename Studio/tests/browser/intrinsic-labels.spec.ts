import { expect, test } from "@playwright/test";

test("Runs and Executable Inspector display intrinsic names and preserve custom names", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.goto("/?mode=intrinsic-labels&theme=light");
  for (const name of ["Run", "Executable Inspector"]) {
    const canvas = page.getByRole("region", { name, exact: true });
    await expect(canvas.locator('.wf-node[data-runtime-node-id="set"]')).toContainText("Set Variable");
    await expect(canvas.locator('.wf-node[data-runtime-node-id="output"]')).toContainText("Set Output");
    await expect(canvas.locator('.wf-node[data-runtime-node-id="print"]')).toContainText("Write Line");
    await expect(canvas.locator('.wf-node[data-runtime-node-id="custom"]')).toContainText("Increment counter");
    await expect(canvas.locator(".wf-node-ghost")).toHaveCount(0);
  }
  await expect(page.getByRole("list", { name: "Execution timeline" }).locator("strong"))
    .toHaveText(["Set Variable", "Set Output", "Write Line", "Increment counter"]);
  await page.screenshot({ path: testInfo.outputPath("intrinsic-labels.png"), fullPage: true });

  await page.getByLabel("Historical catalog unavailable").check();
  for (const name of ["Run", "Executable Inspector"]) {
    const canvas = page.getByRole("region", { name, exact: true });
    await expect(canvas.locator('.wf-node[data-runtime-node-id="set"]')).toContainText("Set Variable");
    await expect(canvas.locator('.wf-node[data-runtime-node-id="output"]')).toContainText("Set Output");
    await expect(canvas.locator('.wf-node[data-runtime-node-id="custom"]')).toContainText("Increment counter");
  }
  await expect(page.getByRole("list", { name: "Execution timeline" }).locator("strong"))
    .toHaveText(["Set Variable", "Set Output", "Write Line", "Increment counter"]);
});
