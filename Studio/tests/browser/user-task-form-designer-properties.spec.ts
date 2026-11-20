import { expect, test } from "@playwright/test";

test("native task form designer communicates field count and capacity", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  const palette = page.getByRole("complementary", { name: "Field palette", exact: true });
  const counter = palette.locator("small");
  await expect(counter).toHaveText("0 fields in form · maximum 100");
  await palette.getByRole("button", { name: "Text input", exact: true }).click();
  await palette.getByRole("button", { name: "Number", exact: true }).click();
  await expect(counter).toHaveText("2 fields in form · maximum 100");
  await page.screenshot({ path: testInfo.outputPath("form-designer-properties-desktop.png"), animations: "disabled" });
});

test("native task form designer keeps palette icons compact without shrinking their hit areas", async ({ page }, testInfo) => {
  for (const colorScheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme });
    for (const viewport of [{ width: 1440, height: 1000 }, { width: 390, height: 844 }]) {
      await page.setViewportSize(viewport);
      await page.goto("/form-designer.html");
      const palette = page.getByRole("complementary", { name: "Field palette", exact: true });
      const button = palette.getByRole("button", { name: "Text input", exact: true });
      const geometry = await button.evaluate(element => {
        const icon = element.querySelector("svg")!;
        return { iconWidth: icon.getBoundingClientRect().width, iconHeight: icon.getBoundingClientRect().height, buttonHeight: element.getBoundingClientRect().height };
      });
      expect(geometry.iconWidth).toBeLessThanOrEqual(16);
      expect(geometry.iconHeight).toBeLessThanOrEqual(16);
      expect(geometry.buttonHeight).toBeGreaterThanOrEqual(32);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`form-designer-properties-${colorScheme}-${viewport.width}.png`), animations: "disabled" });
    }
  }
});

test("native task form designer only shows advanced options supported by the selected type", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  const palette = page.getByRole("complementary", { name: "Field palette", exact: true });
  await palette.getByRole("button", { name: "Text input", exact: true }).click();
  const properties = page.getByRole("complementary", { name: "Field properties", exact: true });
  const advanced = properties.locator(".user-task-designer__advanced");
  await expect(advanced.locator("summary")).toHaveText("Validation");
  await advanced.locator("summary").click();
  await properties.getByRole("spinbutton", { name: "Maximum characters" }).fill("120");

  const type = properties.getByLabel("Type");
  await type.selectOption("boolean");
  await expect(advanced).toHaveCount(0);
  await type.selectOption("date");
  await expect(advanced).toHaveCount(0);
  await type.selectOption("number");
  await expect(advanced.locator("summary")).toHaveText("Validation");
  await advanced.locator("summary").click();
  await expect(advanced.getByRole("spinbutton", { name: "Minimum" })).toBeVisible();
  await expect(advanced.getByRole("spinbutton", { name: "Maximum" })).toBeVisible();
  await type.selectOption("multiline");
  await expect(advanced.locator("summary")).toHaveText("Validation");
  await expect(advanced.getByRole("spinbutton", { name: "Maximum characters" })).toBeVisible();
  await type.selectOption("choice");
  await expect(advanced.locator("summary")).toHaveText("Choices");
  const choices = advanced.getByRole("textbox", { name: "Choices, one per line" });
  await choices.fill("First\nSecond");
  await expect(choices).toHaveValue("First\nSecond");
  await page.screenshot({ path: testInfo.outputPath("form-designer-choice-properties.png"), animations: "disabled" });
});