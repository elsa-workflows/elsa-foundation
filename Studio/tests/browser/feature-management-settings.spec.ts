import { expect, test } from "@playwright/test";

async function openPersistenceSettings(page: import("@playwright/test").Page) {
  await page.goto("/feature-management.html?persistence-settings");
  await page.getByRole("button", { name: "All features" }).click();
  await page.locator(".feature-management-feature-button").filter({ hasText: "Agents Entity Framework Core Persistence" }).click();
  await page.getByRole("button", { name: "Edit settings" }).click();
  return page.getByRole("dialog", { name: "Agents Entity Framework Core Persistence" });
}

test("Feature Management settings use spaced shared fields without duplicate technical labels", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  const dialog = await openPersistenceSettings(page);
  const persistenceGroup = dialog.locator(".feature-management-setting-group").filter({ hasText: "Connection name" });
  const fieldStack = persistenceGroup.locator(":scope > div");
  const spacing = await fieldStack.evaluate(element => Number.parseFloat(getComputedStyle(element).rowGap));

  expect(spacing).toBeGreaterThan(0);
  await expect(dialog.locator(".feature-management-setting.outlined")).toHaveCount(5);
  await expect(dialog.locator(".feature-management-setting.outlined .studio-form-control")).toHaveCount(5);
  await expect(dialog.locator(".feature-management-setting.outlined code")).toHaveCount(0);
  const booleanWrapperHasNoBorder = await dialog.locator(".feature-management-setting.boolean").evaluate(element =>
    getComputedStyle(element).borderWidth === "0px"
  );
  expect(booleanWrapperHasNoBorder).toBe(true);
  const outlinedWrappersHaveNoBorder = await dialog.locator(".feature-management-setting.outlined").evaluateAll(elements =>
    elements.every(element => getComputedStyle(element).borderWidth === "0px")
  );
  expect(outlinedWrappersHaveNoBorder).toBe(true);
  await expect(dialog.getByRole("checkbox", { name: "Pooled contexts" })).toBeVisible();
  const connectionName = dialog.getByRole("textbox", { name: "Connection name" });
  await expect(connectionName).toBeVisible();
  const screenshot = await page.screenshot({ path: "artifacts/feature-management-settings-dialog.png", animations: "disabled" });
  await testInfo.attach("feature-management-settings-dialog.png", { body: screenshot, contentType: "image/png" });
  await connectionName.focus();
  await expect(connectionName).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("textbox", { name: "Connection string" })).toBeFocused();
});

test("Feature Management settings preserve edit, reset, cancel, and local draft behavior", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  let dialog = await openPersistenceSettings(page);
  const connectionName = dialog.getByRole("textbox", { name: "Connection name" });
  await connectionName.fill("Canceled connection");
  await dialog.getByRole("button", { name: "Reset changes" }).click();
  await expect(connectionName).toHaveValue("");
  await connectionName.fill("Canceled connection");
  await dialog.getByRole("button", { name: "Cancel" }).click();

  dialog = await openPersistenceSettings(page);
  await expect(dialog.getByRole("textbox", { name: "Connection name" })).toHaveValue("");
  await dialog.getByRole("textbox", { name: "Connection name" }).fill("AgentStore");
  await dialog.getByRole("checkbox", { name: "Pooled contexts" }).check();
  await dialog.getByLabel("Provider").selectOption("SqlServer");

  const migrationOptions = dialog.getByRole("textbox", { name: "Migration options" });
  await migrationOptions.fill("{");
  await expect(dialog.getByText("Invalid JSON")).toBeVisible();
  await migrationOptions.fill('{"useTransactions":false}');
  await expect(migrationOptions).toHaveAttribute("aria-invalid", "false");

  await dialog.getByRole("button", { name: "Save" }).click();
  await expect(page.getByRole("button", { name: "Edit settings" })).toBeVisible();
  await expect(page.locator(".feature-management-card.selected")).toContainText("Pending changes");
  await page.getByRole("button", { name: "Edit settings" }).click();
  dialog = page.getByRole("dialog", { name: "Agents Entity Framework Core Persistence" });
  await expect(dialog.getByRole("textbox", { name: "Connection name" })).toHaveValue("AgentStore");
  await expect(dialog.getByRole("checkbox", { name: "Pooled contexts" })).toBeChecked();
  await expect(dialog.getByLabel("Provider")).toHaveValue('"SqlServer"');
  await expect(dialog.getByRole("textbox", { name: "Migration options" })).toHaveValue('{\n  "useTransactions": false\n}');
  await expect(dialog.getByRole("button", { name: "Save" })).toBeVisible();
});

test("Feature Management settings fit narrow light and dark viewports", async ({ page }, testInfo) => {
  for (const colorScheme of ["light", "dark"] as const) {
    await page.emulateMedia({ colorScheme });
    await page.setViewportSize({ width: 390, height: 640 });
    const dialog = await openPersistenceSettings(page);
    const body = dialog.locator(".feature-management-dialog-body");
    const geometry = await dialog.evaluate(element => ({
      clientWidth: element.clientWidth,
      scrollWidth: element.scrollWidth,
      documentWidth: document.documentElement.clientWidth,
      documentScrollWidth: document.documentElement.scrollWidth,
      footerFits: (() => {
        const dialogRect = element.getBoundingClientRect();
        const footerRect = element.querySelector(".feature-management-dialog-actions")!.getBoundingClientRect();
        return footerRect.top >= dialogRect.top && footerRect.bottom <= dialogRect.bottom;
      })(),
      footerHeight: element.querySelector(".feature-management-dialog-actions")!.clientHeight,
      bodyHeight: element.querySelector(".feature-management-dialog-body")!.clientHeight
    }));
    const bodyFits = await body.evaluate(element => element.scrollWidth <= element.clientWidth);

    expect(geometry.scrollWidth).toBeLessThanOrEqual(geometry.clientWidth);
    expect(geometry.documentScrollWidth).toBeLessThanOrEqual(geometry.documentWidth);
    expect(geometry.footerFits).toBe(true);
    expect(geometry.footerHeight).toBeLessThanOrEqual(220);
    expect(geometry.bodyHeight).toBeGreaterThan(200);
    expect(bodyFits).toBe(true);
    await expect(dialog.getByRole("button", { name: "Save" })).toBeVisible();
    await expect(dialog.getByRole("textbox", { name: "Connection name" })).toBeVisible();
    const screenshot = await page.screenshot({ path: `artifacts/feature-management-settings-narrow-${colorScheme}.png`, animations: "disabled" });
    await testInfo.attach(`feature-management-settings-narrow-${colorScheme}.png`, { body: screenshot, contentType: "image/png" });
    await dialog.getByRole("button", { name: "Cancel" }).click();
  }
});