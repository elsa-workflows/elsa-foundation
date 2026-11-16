import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  for (const composition of ["sequence", "flowchart"]) {
    test(`workflow-start shortcut stays in sync with the inspector and undo (${composition}, ${theme})`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: 1440, height: 950 });
      await page.goto(`/?mode=activity-duplicate-editor&workflow-start&composition=${composition}&theme=${theme}`);
      const node = composition === "sequence"
        ? page.locator('.wf-sequence-step[data-id="write-one"]')
        : page.getByLabel("Workflow canvas", { exact: true }).getByRole("button", { name: /^Http Endpoint\. Node ID:/ });
      await node.click();
      const toggle = page.getByRole("button", { name: "Start new workflows from Http Endpoint", exact: true });
      const setting = page.getByRole("switch", { name: "Start new workflows", exact: true });
      await expect(toggle).toHaveAttribute("aria-pressed", "true");
      await expect(setting).toBeChecked();
      await toggle.click();
      await expect(toggle).toHaveAttribute("aria-pressed", "false");
      await expect(setting).not.toBeChecked();
      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await node.click();
      await expect(toggle).toHaveAttribute("aria-pressed", "true");
      await expect(setting).toBeChecked();
      await setting.uncheck();
      await expect(toggle).toHaveAttribute("aria-pressed", "false");
      await toggle.focus();
      await page.keyboard.press("Space");
      await expect(setting).toBeChecked();
      await expect(toggle).toHaveAttribute("title", "Disable workflow start");
      await page.screenshot({ path: testInfo.outputPath("workflow-start.png") });
    });
  }
}
