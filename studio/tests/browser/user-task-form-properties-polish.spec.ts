import { expect, test } from "@playwright/test";

test("Boolean properties keep display visible and separate validation from answer labels", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "Create form", exact: true }).click();
  await page.getByRole("complementary", { name: "Field palette" }).getByRole("button", { name: "Yes / no", exact: true }).click();

  const properties = page.getByRole("complementary", { name: "Field properties" });
  const display = properties.getByLabel("Display");
  await expect(display).toBeVisible();
  await expect(display.locator("xpath=ancestor::details")).toHaveCount(0);

  const validation = properties.locator(".studio-advanced-section").filter({ has: page.getByRole("button", { name: "Validation" }) });
  const answerLabels = properties.locator(".studio-advanced-section").filter({ has: page.getByRole("button", { name: "Answer labels" }) });
  await expect(validation).toHaveCount(1);
  await expect(answerLabels).toHaveCount(1);
  await validation.getByRole("button", { name: "Validation" }).click();
  const mustBeChecked = properties.getByRole("checkbox", { name: "Must be checked" });
  await expect(mustBeChecked).toHaveAccessibleDescription(/Requires Yes/);

  const answerLabelsTrigger = answerLabels.getByRole("button", { name: "Answer labels" });
  await answerLabelsTrigger.focus();
  await page.keyboard.press("Enter");
  await expect(properties.locator("fieldset.user-task-designer__answer-labels")).toHaveCount(0);
  await expect(properties.getByRole("textbox", { name: "Yes label" })).toBeVisible();
  await properties.getByRole("textbox", { name: "Yes label" }).fill("I agree");
  await page.screenshot({ path: testInfo.outputPath("form-properties-polish-desktop.png"), animations: "disabled" });
  await page.getByRole("tab", { name: "Source" }).click();
  await expect(page.locator(".cm-content")).toContainText('"trueLabel": "I agree"');
});

test("property controls remain usable without horizontal overflow in narrow light and dark layouts", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 390, height: 844 });
  for (const colorScheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme });
    await page.goto("/form-designer.html");
    await page.getByRole("button", { name: "Create form", exact: true }).click();
    await page.getByRole("button", { name: "Expand controls panel" }).click();
    await page.getByRole("complementary", { name: "Field palette" }).getByRole("button", { name: "Yes / no", exact: true }).click();
    await page.getByRole("button", { name: "Expand properties panel" }).click();
    const properties = page.getByRole("complementary", { name: "Field properties" });
    await expect(properties.getByLabel("Display")).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`form-properties-polish-${colorScheme}-390.png`), animations: "disabled" });
  }
});

test("Date fields show common properties without an empty type-specific section", async ({ page }, testInfo) => {
  for (const [viewport, size] of [[{ width: 1440, height: 1000 }, "desktop"], [{ width: 390, height: 844 }, "narrow"]] as const) {
    for (const colorScheme of ["light", "dark"] as const) {
      await page.setViewportSize(viewport);
      await page.emulateMedia({ colorScheme });
      await page.goto("/form-designer.html");
      await page.getByRole("button", { name: "Create form", exact: true }).click();
      if (size === "narrow") await page.getByRole("button", { name: "Expand controls panel" }).click();
      await page.getByRole("complementary", { name: "Field palette" }).getByRole("button", { name: "Date", exact: true }).click();
      if (size === "narrow") await page.getByRole("button", { name: "Expand properties panel" }).click();

      const properties = page.getByRole("complementary", { name: "Field properties" });
      await expect(properties.getByRole("heading", { name: "Field properties" })).toBeVisible();
      await expect(properties.getByRole("heading", { name: "Date properties", exact: true })).toHaveCount(0);
      await expect(properties.getByLabel("Label")).toHaveValue("Date field 1");
      await expect(properties.getByLabel("Type")).toHaveValue("date");
      await expect(properties.getByRole("checkbox", { name: "Required" })).toBeVisible();
      await expect(properties.getByRole("checkbox", { name: "Read only" })).toBeVisible();
      await expect(properties.getByLabel("Help text")).toBeVisible();

      const type = properties.getByLabel("Type");
      await type.selectOption("text");
      await expect(properties.getByRole("button", { name: "Validation" })).toBeVisible();
      await type.selectOption("date");
      await expect(properties.getByRole("heading", { name: "Date properties", exact: true })).toHaveCount(0);
      await properties.getByLabel("Label").fill("Start date");
      await expect(properties.getByLabel("Label")).toHaveValue("Start date");
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`date-properties-${size}-${colorScheme}.png`), animations: "disabled" });
    }
  }
});