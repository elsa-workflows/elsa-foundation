import { expect, test } from "@playwright/test";
import { isFieldUnobscured } from "../visibleContent";

for (const appearance of ["light", "dark"]) for (const height of [600, 844]) {
  test(`keeps the last Settings field clear of wrapped persistent toasts (${appearance}, ${height}px)`, async ({ page }, info) => {
    await page.setViewportSize({ width: 390, height });
    await page.goto(`/settings/appearance?mode=settings&appearance=${appearance}&focus-notices`);
    await page.evaluate(() => document.fonts.ready);
    const field = page.getByRole("textbox", { name: "Setting 12", exact: true });
    await field.fill("Retained phone value");
    await field.evaluate(element => (element as HTMLInputElement).setSelectionRange(3, 9));
    for (let notice = 0; notice < 3; notice++) {
      await page.getByRole("button", { name: "Show focus notice", exact: true }).click();
      await expect(page.locator(".studio-toast")).toHaveCount(notice + 1);
      await expect(field).toBeFocused();
      await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    }
    await expect(field).toHaveValue("Retained phone value");
    expect(await field.evaluate(element => [(element as HTMLInputElement).selectionStart, (element as HTMLInputElement).selectionEnd])).toEqual([3, 9]);
    await page.screenshot({ path: info.outputPath("focused-field-above-notices.png") });
    await page.setViewportSize({ width: 390, height: height === 600 ? 844 : 600 });
    await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    await expect(field).toBeFocused();

    const dismiss = page.getByRole("button", { name: "Dismiss notification", exact: true }).first();
    await dismiss.focus();
    await expect(dismiss).toBeFocused();
    await expect.poll(() => page.locator(".content").evaluate(element => (element as HTMLElement).style.paddingBlockEnd)).toBe("");
    await dismiss.press("Enter");
    await expect(page.locator(".studio-toast")).toHaveCount(2);
    await expect(page.locator(".studio-toast").first()).toBeFocused();
    while (await page.locator(".studio-toast").count()) {
      await page.getByRole("button", { name: "Dismiss notification", exact: true }).first().click();
    }
    await field.focus();
    await expect.poll(() => field.evaluate(isFieldUnobscured)).toBe(true);
    await expect(field).toHaveValue("Retained phone value");
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  });
}
