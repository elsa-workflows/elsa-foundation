import { expect, test } from "@playwright/test";

const scalarSelector = 'input[type="text"], textarea, input[type="number"], input[type="date"]';

async function expectSharedOutline(container: import("@playwright/test").Locator) {
  const controls = await container.locator(scalarSelector).evaluateAll(elements => elements.map(element => {
    const outline = element.closest(".studio-field")?.querySelector(":scope > .studio-form-control");
    const bounds = element.getBoundingClientRect();
    return {
      marked: element.hasAttribute("data-studio-field-control"),
      nativeBorder: getComputedStyle(element).borderTopWidth,
      outlineBorder: outline ? getComputedStyle(outline).borderTopWidth : "0px",
      controlWidth: bounds.width,
      outlineWidth: outline?.getBoundingClientRect().width ?? 0
    };
  }));

  expect(controls).toHaveLength(5);
  for (const control of controls) {
    expect(control.marked).toBe(true);
    expect(control.nativeBorder).toBe("0px");
    expect(Number.parseFloat(control.outlineBorder)).toBeGreaterThan(0);
    expect(control.controlWidth).toBeGreaterThan(control.outlineWidth * 0.95);
  }
}

test("built-in and contributed scalar controls use one full-width outline in canvas and Preview", async ({ page }, testInfo) => {
  for (const [viewport, size] of [[{ width: 1440, height: 1000 }, "desktop"], [{ width: 390, height: 844 }, "narrow"]] as const) {
    for (const colorScheme of ["light", "dark"] as const) {
      await page.setViewportSize(viewport);
      await page.emulateMedia({ colorScheme });
      await page.goto("/form-designer.html?custom-control");
      await page.getByRole("button", { name: "Create form", exact: true }).click();

      if (size === "narrow") await page.getByRole("button", { name: "Expand controls panel" }).click();
      const palette = page.getByRole("complementary", { name: "Field palette", exact: true });
      for (const name of ["Text input", "Long text", "Number", "Date", "Rating"]) {
        await palette.getByRole("button", { name, exact: true }).click();
      }
      if (size === "narrow") await page.getByRole("button", { name: "Collapse controls panel" }).click();

      const canvas = page.getByRole("region", { name: "Form canvas", exact: true });
      await expectSharedOutline(canvas);
      await page.screenshot({ path: testInfo.outputPath(`field-outlines-${size}-${colorScheme}-canvas.png`), animations: "disabled" });

      await page.getByRole("tab", { name: "Preview", exact: true }).click();
      const preview = page.getByRole("region", { name: "Recipient preview", exact: true });
      await expectSharedOutline(preview);
      const text = preview.locator('input[type="text"]');
      const outline = text.locator("xpath=ancestor::*[contains(concat(' ', normalize-space(@class), ' '), ' studio-field ')]").locator(":scope > .studio-form-control");
      const expectedFocus = await outline.evaluate(element => {
        const styles = getComputedStyle(element);
        return styles.getPropertyValue("--studio-focus-ring-solid").trim() || styles.getPropertyValue("--studio-accent").trim();
      });
      await text.focus();
      await expect.poll(() => outline.evaluate(element => element.matches(":focus-within"))).toBe(true);
      await expect.poll(() => outline.evaluate(element => getComputedStyle(element).borderTopColor)).toBe(expectedFocus);
      await text.fill("Preview response");
      await expect(text).toHaveValue("Preview response");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`field-outlines-${size}-${colorScheme}-preview.png`), animations: "disabled" });
    }
  }
});