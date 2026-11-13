import { expect, test } from "@playwright/test";

for (const host of ["editor", "instance"]) for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`keeps keyboard focus through occurrence navigation and its boundaries (${host}, ${theme}, ${width}px)`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&run-canvas=repeated&theme=${theme}`);
    if (host === "editor") {
      await page.getByRole("button", { name: "Test Run", exact: true }).click();
      await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    }
    const canvas = host === "instance" ? page.getByRole("region", { name: "Workflow run canvas", exact: true })
      : page.getByLabel("Workflow canvas", { exact: true });
    await canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ }).click();
    const details = page.getByRole("complementary", { name: host === "instance" ? "Run details panel" : "Inspector panel", exact: true });
    const chooser = details.locator(".wf-execution-choice");
    const next = details.getByRole("button", { name: "Next activity execution", exact: true });
    const previous = details.getByRole("button", { name: "Previous activity execution", exact: true });
    await chooser.press("Enter");
    await details.getByRole("list", { name: "Recorded activity executions", exact: true })
      .getByRole("button", { name: /^Execution 1 (Completed|Running)/ }).press("Enter");
    await expect(chooser).toBeFocused();
    for (let occurrence = 2; occurrence <= 10; occurrence++) {
      await next.press("Enter");
      await expect(chooser).toContainText(`Execution ${occurrence} of 10`);
      await expect(details.getByText(`Current: ${occurrence}`, { exact: true })).toBeVisible();
      await expect(occurrence === 10 ? chooser : next).toBeFocused();
    }
    await expect(next).toBeDisabled();
    await page.screenshot({ path: info.outputPath("last-occurrence-focus.png"), fullPage: true });
    for (let occurrence = 9; occurrence >= 1; occurrence--) {
      await previous.press("Enter");
      await expect(chooser).toContainText(`Execution ${occurrence} of 10`);
      await expect(details.getByText(`Current: ${occurrence}`, { exact: true })).toBeVisible();
      await expect(occurrence === 1 ? chooser : previous).toBeFocused();
    }
    await expect(previous).toBeDisabled();
    await page.screenshot({ path: info.outputPath("first-occurrence-focus.png"), fullPage: true });
  });
}
