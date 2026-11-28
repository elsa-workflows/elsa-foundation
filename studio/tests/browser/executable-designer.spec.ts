import { expect, test } from "@playwright/test";

test("bound BPMN keyboard inspection uses the pinned activity identity and cannot delete it", async ({ page }) => {
  await page.goto("/executable-designer.html?view=bpmn");
  const node = page.locator('.react-flow__node[data-id="task"]');
  await expect(node).toBeVisible();
  await node.press("Enter");
  await expect(page.getByLabel("Selected activity", { exact: true })).toHaveText("leaf");
  await expect(node).toHaveAttribute("aria-pressed", "true");
  await page.locator(".react-flow__pane").click({ position: { x: 5, y: 5 } });
  await expect(page.getByLabel("Selected activity", { exact: true })).toHaveText("None");
  await node.press("Space"); await expect(node).toHaveAttribute("aria-pressed", "true");
  await node.press("Delete"); await expect(node).toBeVisible();
  await expect(page.locator(".react-flow__controls-interactive")).toHaveCount(0);
});

for (const theme of ["light", "dark"]) for (const width of [1440, 360]) {
  test(`immutable Sequence shows an unavailable historical activity (${theme}, ${width}px)`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/executable-designer.html?theme=${theme}`);
    const step = page.getByRole("button", { name: /1. Activity, Activity unavailable/ });
    await expect(step).toBeVisible();
    await step.press("Enter"); await expect(step).toHaveAttribute("aria-pressed", "true");
    await step.press("Delete"); await expect(step).toBeVisible();
    await expect(page.getByRole("img", { name: "Activity unavailable in this environment", exact: true })).toBeVisible();
    await expect(page.locator(".wf-sequence-insert, .wf-sequence-actions")).toHaveCount(0);
    await expect(step).toHaveAttribute("draggable", "false");
    await expect(page.locator(".wf-sequence-type")).toHaveText("Unavailable activity");
    expect((await step.boundingBox())!.width).toBeGreaterThan(180);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: info.outputPath("unavailable.png"), fullPage: true });
  });
}
