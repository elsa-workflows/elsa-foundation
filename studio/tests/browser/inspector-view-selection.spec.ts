import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  test(`preserves the activity view when selecting another designer activity (${theme})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&theme=${theme}`);
    const canvas = page.getByLabel("Workflow canvas", { exact: true });
    const first = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    const second = canvas.getByRole("button", { name: /^Write Line\. Node ID: write-two/ });
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(first).toHaveAccessibleName(/Run state: Completed/);
    await first.click();
    const inspector = page.getByLabel("Inspector panel", { exact: true });
    await expect(inspector.getByRole("tab", { name: "Inspector", exact: true })).toHaveAttribute("aria-selected", "true");
    await inspector.getByRole("tab", { name: "Inspector", exact: true }).click();
    const configuration = inspector.getByRole("tab", { name: "Configuration", exact: true });
    const runData = inspector.getByRole("tab", { name: "Run data", exact: true });
    await runData.click();
    await configuration.click();
    await second.click();
    await expect(second).toHaveAccessibleName(/Selected/);
    await expect(configuration).toHaveAttribute("aria-selected", "true");
    await first.click();
    await expect(configuration).toHaveAttribute("aria-selected", "true");
    await page.screenshot({ path: testInfo.outputPath("configuration-after-selection.png") });
    await runData.click();
    await second.click();
    await expect(runData).toHaveAttribute("aria-selected", "true");
  });
}
