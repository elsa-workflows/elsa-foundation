import { expect, test } from "@playwright/test";

for (const theme of ["light", "black-glass"] as const) {
  test(`Expression Type options remain reachable in a constrained inspector (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 900, height: 600 });
    await page.goto(`/?theme=${theme}`);

    const inspector = page.getByRole("complementary", { name: "Activity inspector" });
    const trigger = page.getByRole("button", { name: "Path expression syntax" });
    await expect(inspector).toBeVisible();
    await trigger.click();

    const listbox = page.getByRole("listbox", { name: "Path expression syntax" });
    await expect(listbox).toBeVisible();
    await expect(listbox.getByRole("option")).toHaveCount(7);
    const popover = listbox.locator("..");
    await expect(popover).toHaveAttribute("data-placement", "top");
    expect(await popover.evaluate(element => element.parentElement === document.body)).toBe(true);

    const box = await popover.boundingBox();
    expect(box).not.toBeNull();
    expect(box!.x).toBeGreaterThanOrEqual(0);
    expect(box!.y).toBeGreaterThanOrEqual(0);
    expect(box!.x + box!.width).toBeLessThanOrEqual(900);
    expect(box!.y + box!.height).toBeLessThanOrEqual(600);
    expect(await popover.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);

    await popover.evaluate(element => { element.scrollTop = element.scrollHeight; });
    expect(await popover.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
    const variable = listbox.getByRole("option", { name: /Variable/ });
    await expect(variable).toBeDisabled();
    await expect(variable).toContainText("requires an inline editor Contribution");
    await listbox.getByRole("option", { name: "Liquid", exact: true }).click();
    await expect(trigger).toHaveText("Liquid");

    await trigger.click();
    await listbox.getByRole("option", { name: "JavaScript", exact: true }).click();
    await expect(trigger).toHaveText("JavaScript");

    await trigger.press("ArrowDown");
    await expect(listbox.getByRole("option", { name: "JavaScript", exact: true })).toBeFocused();
    await page.keyboard.press("Home");
    await expect(listbox.getByRole("option", { name: "JavaScript", exact: true })).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(listbox).toBeHidden();
    await expect(trigger).toBeFocused();
  });
}

test("scrolling the anchor out of view closes without fighting inspector scroll or focus", async ({ page }) => {
  await page.setViewportSize({ width: 900, height: 600 });
  await page.goto("/?theme=light&mode=scroll");

  const inspector = page.getByRole("complementary", { name: "Activity inspector" });
  const trigger = page.getByRole("button", { name: "Path expression syntax" });
  const listbox = page.getByRole("listbox", { name: "Path expression syntax" });
  await trigger.click();
  await expect(listbox.getByRole("option", { name: "Literal", exact: true })).toBeFocused();

  const maximumScroll = await inspector.evaluate(element => element.scrollHeight - element.clientHeight);
  expect(maximumScroll).toBeGreaterThan(0);
  await inspector.evaluate(element => element.scrollTo({ top: element.scrollHeight }));

  await expect(listbox).toBeHidden();
  await page.waitForTimeout(100);
  expect(await inspector.evaluate(element => element.scrollTop)).toBeGreaterThanOrEqual(maximumScroll - 1);
  await expect(trigger).not.toBeFocused();
});
