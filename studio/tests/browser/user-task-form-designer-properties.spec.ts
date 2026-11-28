import { confirmNewResource } from "../newResourceDialog";
import { expect, test } from "@playwright/test";

test("native task form designer communicates field count and capacity", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
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
      await page.getByRole("button", { name: "New form", exact: true }).click();
      await confirmNewResource(page, "form", "New form");
      const palette = page.getByRole("complementary", { name: "Field palette", exact: true });
      if (viewport.width <= 600) await page.getByRole("button", { name: "Expand controls panel" }).click();
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
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  const palette = page.getByRole("complementary", { name: "Field palette", exact: true });
  await palette.getByRole("button", { name: "Text input", exact: true }).click();
  const properties = page.getByRole("complementary", { name: "Field properties", exact: true });
  const advanced = properties.locator(".studio-advanced-section").first();
  await expect(advanced.locator(".studio-disclosure-fold-label")).toHaveText("Validation");
  await advanced.getByRole("button", { name: "Validation" }).click();
  await properties.getByRole("spinbutton", { name: "Maximum characters" }).fill("120");

  const type = properties.getByLabel("Type");
  await type.selectOption("boolean");
  await expect(properties.getByLabel("Display")).toBeVisible();
  await expect(advanced.locator(".studio-disclosure-fold-label")).toHaveText("Validation");
  await advanced.getByRole("button", { name: "Validation" }).click();
  await expect(advanced.getByRole("checkbox", { name: "Must be checked" })).toBeVisible();
  await expect(properties.getByRole("button", { name: "Answer labels" })).toBeVisible();
  await type.selectOption("date");
  await expect(advanced).toHaveCount(0);
  await type.selectOption("number");
  await expect(advanced.locator(".studio-disclosure-fold-label")).toHaveText("Validation");
  await advanced.getByRole("button", { name: "Validation" }).click();
  await expect(advanced.getByRole("spinbutton", { name: "Minimum" })).toBeVisible();
  await expect(advanced.getByRole("spinbutton", { name: "Maximum" })).toBeVisible();
  await type.selectOption("multiline");
  await expect(advanced.locator(".studio-disclosure-fold-label")).toHaveText("Validation");
  await advanced.getByRole("button", { name: "Validation" }).click();
  await expect(advanced.getByRole("spinbutton", { name: "Maximum characters" })).toBeVisible();
  await type.selectOption("choice");
  await expect(advanced.locator(".studio-disclosure-fold-label")).toHaveText("Choices");
  await advanced.getByRole("button", { name: "Choices" }).click();
  const choices = advanced.getByRole("textbox", { name: "Choices, one per line" });
  await choices.fill("First\nSecond");
  await expect(choices).toHaveValue("First\nSecond");
  await page.screenshot({ path: testInfo.outputPath("form-designer-choice-properties.png"), animations: "disabled" });
});

test("primary Forms editor shares resizable panels while preserving its own saved widths", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("elsa-studio-workflow-palette-width", "380"));
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");

  const controlsResize = page.getByRole("separator", { name: "Resize controls panel" });
  const originalWidth = Number(await controlsResize.getAttribute("aria-valuenow"));
  expect(originalWidth).toBe(260);
  await controlsResize.focus();
  await page.keyboard.press("ArrowRight");
  await expect(controlsResize).toHaveAttribute("aria-valuenow", String(originalWidth + 16));

  await page.getByRole("button", { name: "Collapse controls panel" }).click();
  await expect(page.locator(".user-task-designer__palette .user-task-designer__panel-content")).toBeHidden();
  await page.getByRole("button", { name: "Expand controls panel" }).click();
  await expect(page.locator(".user-task-designer__layout")).not.toHaveClass(/palette-collapsed/);
  await expect(page.getByRole("button", { name: "Text input", exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Maximize properties panel" }).click();
  await expect(page.getByRole("region", { name: "Form canvas" })).toBeHidden();
  await page.keyboard.press("Escape");
  await expect(page.getByRole("region", { name: "Form canvas" })).toBeVisible();

  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.getByRole("button", { name: "Expand controls panel" })).toBeVisible();
  await page.getByRole("button", { name: "Expand controls panel" }).click();
  await expect(page.locator(".user-task-designer__layout")).not.toHaveClass(/palette-collapsed/);
  await expect(page.getByRole("button", { name: "Text input", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Expand properties panel" }).click();
  await expect(page.getByRole("textbox", { name: "Form ID" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test("primary Forms editor previews, publishes immutably, and starts the next draft from that publication", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/form-designer.html");
  await page.getByRole("button", { name: "New form", exact: true }).click();
  await confirmNewResource(page, "form", "New form");
  await expect(page.getByText("Form ID is required.", { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.getByText("Form ID is required.", { exact: true })).toBeVisible();
  await page.getByRole("textbox", { name: "Form ID" }).fill("new-contact");
  await page.getByRole("textbox", { name: "Name" }).fill("New contact");
  await page.getByRole("button", { name: "Text input", exact: true }).click();
  await page.getByRole("textbox", { name: "Label" }).fill("Email");
  await page.getByRole("tab", { name: "Preview" }).click();
  const preview = page.getByRole("region", { name: "Recipient preview" });
  await preview.getByRole("textbox", { name: "Email" }).fill("recipient@example.test");
  await page.getByRole("tab", { name: "Designer" }).click();
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.getByText("Published · version 1", { exact: true }).first()).toBeVisible();
  const requests = await page.evaluate(() => (window as unknown as { formRequests: { body: unknown }[] }).formRequests);
  expect(JSON.stringify(requests[0].body)).toContain('"id":"new-contact"');
  expect(JSON.stringify(requests[0].body)).toContain('"key":"text1"');
  expect(JSON.stringify(requests[0].body)).not.toContain("recipient@example.test");
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("table", { name: "Task forms" })).toBeVisible();
  const row = page.getByRole("row").filter({ hasText: "new-contact" });
  await expect(row).toContainText("Published · v1");
  await row.press("Enter");
  await expect(page.getByRole("textbox", { name: "Form ID" })).toBeDisabled();
  await page.getByRole("textbox", { name: "Name" }).fill("New contact revision");
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await expect(page.getByText("Draft saved", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Publish", exact: true }).click();
  await expect(page.getByText("Published · version 2", { exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(row).toContainText("Published · v2");
});