import { expect, test } from "@playwright/test";

test("activity Inspector tabs keep fixed context, scroll independently, and survive outer-panel navigation", async ({ page }) => {
  await page.setViewportSize({ width: 420, height: 720 });
  await page.goto("/?theme=light&mode=activity-inspector-tabs");

  const inspector = page.getByRole("complementary", { name: "Activity inspector" });
  const innerTabs = inspector.getByRole("tab");
  const inputs = inspector.getByRole("tab", { name: "Inputs" });
  const outputs = inspector.getByRole("tab", { name: "Outputs" });
  const version = inspector.getByRole("tab", { name: "Version" });
  const context = inspector.locator(".wf-inspector-context");
  const tablist = inspector.locator(".wf-inspector-tabs");

  await expect(inspector).toBeVisible();
  await expect(context).toContainText("Long running browser activity");
  await expect(context).toContainText("Container of this canvas");
  await expect(context).toContainText("No longer available for new use");
  await expect(innerTabs).toHaveText(["Inputs", "Outputs", "Variables", "Slots", "Details", "Version"]);
  await expect(inputs).toHaveAttribute("aria-selected", "true");
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  const inputsPanel = inspector.locator("[role='tabpanel']:not([hidden])");
  expect(await inputsPanel.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
  const contextBeforeScroll = await context.boundingBox();
  const tablistBeforeScroll = await tablist.boundingBox();
  await inputsPanel.evaluate(element => { element.scrollTop = element.scrollHeight; });
  expect(await inputsPanel.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  expect(await context.boundingBox()).toEqual(contextBeforeScroll);
  expect(await tablist.boundingBox()).toEqual(tablistBeforeScroll);

  await outputs.click();
  await expect(outputs).toHaveAttribute("aria-selected", "true");
  await inputs.click();
  expect(await inputsPanel.evaluate(element => element.scrollTop)).toBeGreaterThan(0);

  expect(await tablist.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
  await inputs.focus();
  await inputs.press("End");
  await expect(version).toBeFocused();
  await expect(version).toHaveAttribute("aria-selected", "true");
  expect(await tablist.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
  await version.press("Home");
  await expect(inputs).toBeFocused();
  await expect(inputs).toHaveAttribute("aria-selected", "true");

  await outputs.click();
  await page.getByRole("button", { name: "Runtime", exact: true }).click();
  await expect(inspector.getByLabel("runtime panel")).toBeVisible();
  await page.getByRole("button", { name: "Inspector", exact: true }).click();
  await expect(outputs).toHaveAttribute("aria-selected", "true");
});

for (const theme of ["light", "black-glass"] as const) {
  test(`activity Inspector tabs fit normal and minimum widths in ${theme}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1000, height: 760 });
    await page.goto(`/?theme=${theme}&mode=activity-inspector-tabs`);

    const inspector = page.getByRole("complementary", { name: "Activity inspector" });
    const inputsPanel = inspector.locator("[role='tabpanel']:not([hidden])");
    await inspector.evaluate(element => { element.style.width = "360px"; });
    await page.screenshot({ path: testInfo.outputPath(`${theme}-normal.png`), fullPage: true });

    await inspector.evaluate(element => { element.style.width = "260px"; });
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`${theme}-minimum.png`), fullPage: true });

    await inputsPanel.evaluate(element => { element.scrollTop = element.scrollHeight; });
    await expect(inspector.locator(".wf-inspector-context")).toContainText("Long running browser activity");
    await page.screenshot({ path: testInfo.outputPath(`${theme}-scrolled.png`), fullPage: true });
  });
}
