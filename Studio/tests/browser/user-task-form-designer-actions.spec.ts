import { expect, test } from "@playwright/test";

for (const appearance of ["light", "dark"] as const) {
  for (const viewport of [{ name: "desktop", width: 1440, height: 1000 }, { name: "narrow", width: 390, height: 844 }]) {
    test(`form field actions stay compact and usable in ${appearance} ${viewport.name} layout`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.addInitScript(mode => localStorage.setItem("elsa-studio-theme-mode", mode), appearance);
      await page.goto("/form-designer.html");
      await page.getByRole("button", { name: "Create form", exact: true }).click();
      if (viewport.name === "narrow") await page.getByRole("button", { name: "Expand controls panel", exact: true }).click();
      await page.getByRole("button", { name: "Text input", exact: true }).click();
      await page.getByRole("button", { name: "Number", exact: true }).click();
      if (viewport.name === "narrow") await page.getByRole("button", { name: "Collapse controls panel", exact: true }).click();

      const firstActions = page.locator(".user-task-designer__field").first().locator(".user-task-designer__field-actions");
      const actionButtons = firstActions.getByRole("button");
      await expect(actionButtons).toHaveCount(4);
      await expect(actionButtons.nth(0)).toHaveAccessibleName("Move Text field 1 up");
      await expect(actionButtons.nth(1)).toHaveAccessibleName("Move Text field 1 down");
      await expect(actionButtons.nth(2)).toHaveAccessibleName("Duplicate Text field 1");
      await expect(actionButtons.nth(3)).toHaveAccessibleName("Remove Text field 1");
      await expect(actionButtons.nth(0)).toBeDisabled();
      await expect(actionButtons.nth(1)).toBeEnabled();

      const metrics = await firstActions.evaluate(element => {
        const buttons = Array.from(element.querySelectorAll("button"));
        const icons = Array.from(element.querySelectorAll("svg"));
        return {
          buttonTargets: buttons.map(button => {
            const bounds = button.getBoundingClientRect();
            return { width: bounds.width, height: bounds.height };
          }),
          icons: icons.map(icon => ({
            width: icon.getBoundingClientRect().width,
            height: icon.getBoundingClientRect().height,
            strokeWidth: Number.parseFloat(getComputedStyle(icon).strokeWidth)
          }))
        };
      });
      expect(metrics.buttonTargets.every(({ width, height }) => width >= 32 && height >= 32)).toBe(true);
      expect(metrics.icons).toHaveLength(4);
      expect(metrics.icons.every(({ width, height }) => width <= 16 && height <= 16)).toBe(true);
      expect(metrics.icons.every(({ strokeWidth }) => strokeWidth < 2)).toBe(true);

      await actionButtons.nth(2).focus();
      await expect(actionButtons.nth(2)).toBeFocused();
      await page.keyboard.press("Enter");
      await expect(page.locator(".user-task-designer")).toHaveAttribute("data-field-order", "text1,text1_copy,number1");
      await expect(page.getByRole("button", { name: "Move Text field 1 up", exact: true })).toBeDisabled();
      await expect(page.getByRole("button", { name: "Move Number field 1 down", exact: true })).toBeDisabled();

      await page.getByRole("button", { name: "Move Number field 1 up", exact: true }).click();
      await expect(page.locator(".user-task-designer")).toHaveAttribute("data-field-order", "text1,number1,text1_copy");
      await page.getByRole("button", { name: "Remove Number field 1", exact: true }).click();
      await expect(page.locator(".user-task-designer")).toHaveAttribute("data-field-order", "text1,text1_copy");

      if (viewport.name === "narrow") {
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      }
      await page.screenshot({ path: testInfo.outputPath(`form-designer-actions-${appearance}-${viewport.name}.png`) });
    });
  }
}