import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  for (const width of [390, 1200]) {
    test(`incident details remain readable at ${width}px in ${theme}`, async ({ page }) => {
      await page.setViewportSize({ width, height: 800 });
      await page.goto(`/?mode=incident-diagnostics&theme=${theme}`);
      const card = page.locator(".wf-instance-incident");
      await expect(card).toBeVisible();
      const headingColor = await card.locator("strong").evaluate(element => getComputedStyle(element).color);
      await expect(card.locator(".wf-instance-incident-cause")).toHaveCSS("color", headingColor);
      const technical = card.locator(".wf-incident-technical-details");
      await expect(technical).not.toHaveAttribute("open");
      await technical.locator("summary").first().click();
      await expect(card.locator(".wf-incident-message")).toContainText("sha256:");
      await card.getByText("Stack trace", { exact: true }).click();
      for (const content of [card, technical, card.locator(".wf-incident-stacktrace pre")]) {
        expect(await content.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
      }
      const open = card.getByRole("button", { name: "Open incident details", exact: true });
      await open.click();
      const dialog = page.getByRole("dialog");
      await expect(dialog).toBeVisible();
      expect(await dialog.evaluate(element => {
        const box = element.getBoundingClientRect();
        return box.left >= 0 && box.right <= window.innerWidth && box.top >= 0 && box.bottom <= window.innerHeight;
      })).toBe(true);
      expect(await dialog.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
      await page.keyboard.press("Escape");
      await expect(dialog).toBeHidden();
      await expect(open).toBeFocused();
    });
  }
}
