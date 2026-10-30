import { expect, test } from "@playwright/test";

const themes = ["classic", "meridian", "drift", "schematic", "atelier", "elsa-cloud", "signal", "dusk"];
for (const theme of themes) {
  for (const appearance of ["light", "dark", "dim", "high-contrast"]) {
    test(`shared dropdowns follow ${theme} ${appearance}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: 1000, height: 1000 });
      await page.goto(`/dropdowns.html?theme=${theme}&appearance=${appearance}`);
      await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
      await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", appearance);
      const capture = page.getByRole("combobox", { name: "Capture into", exact: true });
      const styles = await capture.evaluate(element => {
        const style = getComputedStyle(element);
        return { appearance: style.appearance, height: element.getBoundingClientRect().height, background: style.backgroundColor, image: style.backgroundImage, padding: parseFloat(style.paddingInlineEnd) };
      });
      expect(styles.appearance).toBe("none");
      expect(styles.height).toBeGreaterThanOrEqual(32);
      expect(styles.background).not.toBe("rgba(0, 0, 0, 0)");
      expect(styles.image).toContain("gradient");
      expect(styles.padding).toBeGreaterThanOrEqual(28);
      await capture.selectOption("workflow::payload");
      await expect(capture).toHaveValue("workflow::payload");
      await capture.focus();
      expect(await capture.evaluate(element => getComputedStyle(element).outlineStyle)).toBe("solid");
      for (const name of ["Response mode", "Page size"]) {
        const select = page.getByRole("combobox", { name, exact: true });
        expect(await select.evaluate(element => getComputedStyle(element).appearance)).toBe("none");
        expect(await select.evaluate(element => parseFloat(getComputedStyle(element).paddingInlineEnd))).toBeGreaterThanOrEqual(28);
      }
      await expect(page.getByRole("combobox", { name: "Unavailable" })).toBeDisabled();
      const multiple = page.getByRole("listbox", { name: "Choose several" });
      expect(await multiple.evaluate(element => getComputedStyle(element).backgroundImage)).toBe("none");
      const selectedColor = await multiple.getByRole("option", { name: "Request", exact: true }).evaluate(element => getComputedStyle(element).backgroundColor);
      const unselectedColor = await multiple.getByRole("option", { name: "Payload", exact: true }).evaluate(element => getComputedStyle(element).backgroundColor);
      expect(selectedColor).not.toBe(unselectedColor);
      await multiple.selectOption(["Request", "Payload"]);
      await expect(multiple).toHaveValues(["Request", "Payload"]);
      if (theme === "classic" || theme === "drift") await page.screenshot({ path: testInfo.outputPath("dropdowns.png") });
    });
  }
}

test("dropdowns fit a narrow view and keep a native control in forced colors", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 950 });
  await page.goto("/dropdowns.html?theme=classic&appearance=dark");
  const capture = page.getByRole("combobox", { name: "Capture into", exact: true });
  const bounds = (await capture.boundingBox())!;
  expect(bounds.x + bounds.width).toBeLessThanOrEqual(320);
  await page.emulateMedia({ forcedColors: "active" });
  expect(await capture.evaluate(element => getComputedStyle(element).appearance)).toBe("auto");
  await capture.selectOption("workflow::request");
  await expect(capture).toHaveValue("workflow::request");
});
