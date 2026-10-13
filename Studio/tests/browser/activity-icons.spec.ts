import { expect, test } from "@playwright/test";

test("a host --studio-wf-activity-* override retints palette and node icons", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-icons&host-override=1");

  const fixture = page.getByTestId("activity-icons-fixture");
  await expect(fixture).toBeVisible();

  const terminal = fixture.locator('.wf-activity-icon[data-tone="terminal"]').first();
  const composite = fixture.locator('.wf-activity-icon[data-tone="composite"]').first();
  const nodeTerminal = fixture.locator('.wf-node[data-tone="terminal"] .wf-node-icon').first();

  await expect(terminal).toHaveCSS("color", "rgb(255, 17, 0)");
  await expect(composite).toHaveCSS("color", "rgb(124, 58, 237)");
  await expect(nodeTerminal).toHaveCSS("color", "rgb(255, 17, 0)");
});
