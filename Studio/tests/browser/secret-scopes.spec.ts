import { expect, test } from "@playwright/test";

const fixtureValue = "fixture-secret-value-must-not-return";

async function loadSecretsPage(page: import("@playwright/test").Page, scenario = "default") {
  await page.goto(`/secret-scopes.html?scenario=${scenario}`);
  const opener = page.getByRole("button", { name: "Create", exact: true });
  await expect(opener).toBeEnabled();
  return opener;
}

async function openCreateSecret(page: import("@playwright/test").Page, scenario = "default") {
  const opener = await loadSecretsPage(page, scenario);
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Create secret", exact: true });
  await expect(dialog).toBeVisible();
  return { dialog, opener };
}

async function openEditSecret(page: import("@playwright/test").Page, scenario = "editor") {
  await page.goto(`/secret-scopes.html?scenario=${scenario}`);
  const opener = page.getByRole("button", { name: "Edit billing.integration-key", exact: true });
  await expect(opener).toBeEnabled();
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Edit secret", exact: true });
  await expect(dialog).toBeVisible();
  return { dialog, opener };
}

async function fillRequiredMetadata(dialog: import("@playwright/test").Locator, name: string) {
  await dialog.getByLabel("Technical name", { exact: true }).fill(name);
  await dialog.getByLabel("Value", { exact: true }).fill(fixtureValue);
}

for (const viewport of [
  { width: 1440, height: 900 },
  { width: 1280, height: 800 },
  { width: 360, height: 640 }
]) {
  test(`secret create keeps actions visible, body scrollable, and values on errors at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const { dialog, opener } = await openCreateSecret(page, "tall-error");
    const content = dialog.locator(".secrets-create-content");
    const cancel = dialog.getByRole("button", { name: "Cancel", exact: true });
    const create = dialog.getByRole("button", { name: "Create", exact: true });
    await expect.poll(() => content.evaluate(element => element.scrollHeight > element.clientHeight)).toBe(true);
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();

    await dialog.getByLabel("Technical name", { exact: true }).fill("preserved.secret");
    await dialog.getByLabel("Value", { exact: true }).fill(fixtureValue);
    await dialog.getByLabel("Expires", { exact: true }).scrollIntoViewIfNeeded();
    await expect.poll(() => content.evaluate(element => element.scrollTop > 0)).toBe(true);
    expect(await dialog.evaluate(element => element.scrollTop)).toBe(0);
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();

    await page.setViewportSize({ ...viewport, height: viewport.height - 24 });
    await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveValue("preserved.secret");
    await expect(dialog.getByLabel("Value", { exact: true })).toHaveValue(fixtureValue);
    await expect(create).toBeInViewport();
    await create.click();

    const alert = dialog.getByRole("alert");
    await expect(alert).toContainText("Fixture secret creation failed.");
    await expect(alert).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect(create).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(alert).toBeFocused();
    await page.keyboard.press("Tab");
    const technicalName = dialog.getByLabel("Technical name", { exact: true });
    await expect(technicalName).toBeFocused();
    await technicalName.scrollIntoViewIfNeeded();
    await expect(technicalName).toBeInViewport();
    await expect(technicalName).toHaveValue("preserved.secret");
    await expect(dialog.getByLabel("Value", { exact: true })).toHaveValue(fixtureValue);
    await expect(cancel).toBeInViewport();
    await expect(create).toBeInViewport();
    await cancel.click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
  });
}

test("suggested scope selection shows advisory metadata and creates metadata only", async ({ page }) => {
  const { dialog } = await openCreateSecret(page);
  const scope = dialog.getByRole("combobox", { name: "Scope", exact: true });
  await expect(scope).toHaveValue("");
  await expect(scope.locator("option:checked")).toHaveText("No scope");

  const search = dialog.getByRole("searchbox", { name: "Search scopes", exact: true });
  await search.fill("CRM");
  await scope.selectOption("Integration:CRM");
  await expect(dialog).toContainText("Credentials used by the CRM integration.");
  await expect(dialog).toContainText("Source: CRM Connector");

  await fillRequiredMetadata(dialog, "selected.scope");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();

  const metadata = page.getByTestId("created-secret-metadata");
  await expect(metadata).toBeVisible();
  expect(JSON.parse(await metadata.innerText())).toEqual({
    name: "selected.scope",
    displayName: "selected.scope",
    typeName: "text",
    storeName: "encrypted",
    scope: "Integration:CRM",
    tags: [],
    status: "Active"
  });
  await expect(metadata).not.toContainText(fixtureValue);
});

test("custom scope is revealed by keyboard and keeps its exact case at 320px", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 900 });
  const { dialog } = await openCreateSecret(page);
  const scope = dialog.getByRole("combobox", { name: "Scope", exact: true });
  await expect(scope).toHaveValue("");
  await expect(scope.locator("option:checked")).toHaveText("No scope");
  await scope.focus();
  await page.keyboard.press("c");
  await page.keyboard.press("u");
  await page.keyboard.press("Tab");

  const customScope = dialog.getByRole("textbox", { name: "Custom scope value", exact: true });
  await expect(customScope).toBeVisible();
  await expect(customScope).toBeFocused();
  await customScope.fill("Org.Team-A/Case");
  await fillRequiredMetadata(dialog, "custom.scope");

  const dimensions = await page.evaluate(() => {
    const form = document.querySelector<HTMLElement>(".secrets-modal form")!;
    const bounds = form.getBoundingClientRect();
    return { viewport: window.innerWidth, document: document.documentElement.scrollWidth, left: bounds.left, right: bounds.right };
  });
  expect(dimensions.document).toBeLessThanOrEqual(dimensions.viewport);
  expect(dimensions.left).toBeGreaterThanOrEqual(0);
  expect(dimensions.right).toBeLessThanOrEqual(dimensions.viewport);

  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const metadata = page.getByTestId("created-secret-metadata");
  await expect(metadata).toContainText('"scope":"Org.Team-A/Case"');
  await expect(metadata).not.toContainText(fixtureValue);
});

test("catalog retry preserves the form and a removed provider scope as authored", async ({ page }) => {
  const { dialog } = await openCreateSecret(page, "retry-removed");
  await dialog.getByLabel("Technical name", { exact: true }).fill("preserved.secret");
  await dialog.getByLabel("Display name", { exact: true }).fill("Preserved display name");
  await dialog.getByLabel("Description", { exact: true }).fill("Keep this description during retry.");
  await dialog.getByLabel("Tags", { exact: true }).fill("reviewed, preserved");
  const scope = dialog.getByRole("combobox", { name: "Scope", exact: true });
  await scope.selectOption("Integration.Legacy-CRM");
  await dialog.getByLabel("Value", { exact: true }).fill(fixtureValue);

  await dialog.getByRole("button", { name: "Refresh scope suggestions", exact: true }).click();
  const catalogError = dialog.getByRole("alert");
  await expect(catalogError).toContainText("Scope suggestions could not be refreshed.");
  await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveValue("preserved.secret");
  await expect(dialog.getByLabel("Display name", { exact: true })).toHaveValue("Preserved display name");
  await expect(dialog.getByLabel("Description", { exact: true })).toHaveValue("Keep this description during retry.");
  await expect(dialog.getByLabel("Tags", { exact: true })).toHaveValue("reviewed, preserved");

  await catalogError.getByRole("button", { name: "Retry scope suggestions", exact: true }).click();
  const customScope = dialog.getByRole("textbox", { name: "Custom scope value", exact: true });
  await expect(customScope).toHaveValue("Integration.Legacy-CRM");
  await expect(catalogError).toHaveCount(0);
  await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveValue("preserved.secret");

  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const metadata = page.getByTestId("created-secret-metadata");
  await expect(metadata).toContainText('"scope":"Integration.Legacy-CRM"');
  await expect(metadata).toContainText('"displayName":"Preserved display name"');
  await expect(metadata).not.toContainText(fixtureValue);
});

test("catalog refresh shows its loading state without clearing the selected scope", async ({ page }) => {
  const { dialog } = await openCreateSecret(page, "loading");
  const scope = dialog.getByRole("combobox", { name: "Scope", exact: true });
  await scope.selectOption("Integration:Billing");
  await dialog.getByRole("button", { name: "Refresh scope suggestions", exact: true }).click();

  await expect(dialog.getByRole("status")).toContainText("Loading scope suggestions…");
  await expect(scope).toHaveValue("Integration:Billing");
  await expect(scope).toBeEnabled();
  await expect(dialog.getByRole("status")).toHaveCount(0, { timeout: 3000 });
  await expect(scope).toHaveValue("Integration:Billing");
});

test("Edit secret keeps Scope read-only, explains matching, and saves metadata only", async ({ page }) => {
  const { dialog } = await openEditSecret(page);
  const scope = dialog.getByRole("textbox", { name: "Scope", exact: true });
  await expect(scope).toHaveValue("Integration:CRM");
  await expect(scope).toHaveAttribute("readonly", "");
  const descriptionId = await scope.getAttribute("aria-describedby");
  expect(descriptionId).toBeTruthy();
  const scopeHelp = dialog.locator(`[id="${descriptionId}"]`);
  await expect(scopeHelp).toContainText("A reference that specifies a scope must match it (ignoring case).");
  await expect(scopeHelp).toContainText("Scope does not grant access.");
  await expect(scopeHelp).toContainText("Scope is read-only in this editor.");

  await dialog.getByLabel("Display name", { exact: true }).fill("Updated billing integration");
  await dialog.getByLabel("Description", { exact: true }).fill("Updated metadata only.");
  await dialog.getByRole("button", { name: "Save changes", exact: true }).click();

  const metadata = page.getByTestId("updated-secret-metadata");
  await expect(metadata).toBeVisible();
  expect(JSON.parse(await metadata.innerText())).toEqual({
    name: "billing.integration-key",
    displayName: "Updated billing integration",
    description: "Updated metadata only.",
    typeName: "text",
    storeName: "encrypted",
    scope: "Integration:CRM",
    status: "Active"
  });
  await expect(metadata).not.toContainText(fixtureValue);
  await expect(dialog).toHaveCount(0);
});

test("Edit save errors preserve fields and Cancel restores focus at 320px", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 900 });
  const { dialog, opener } = await openEditSecret(page, "editor-error");
  const bounds = await dialog.evaluate(element => {
    const rect = element.getBoundingClientRect();
    return { viewport: window.innerWidth, document: document.documentElement.scrollWidth, left: rect.left, right: rect.right };
  });
  expect(bounds.document).toBeLessThanOrEqual(bounds.viewport);
  expect(bounds.left).toBeGreaterThanOrEqual(0);
  expect(bounds.right).toBeLessThanOrEqual(bounds.viewport);

  const displayName = dialog.getByLabel("Display name", { exact: true });
  const description = dialog.getByLabel("Description", { exact: true });
  await displayName.fill("Keep this display name");
  await description.fill("Keep this description after a save failure.");
  const save = dialog.getByRole("button", { name: "Save changes", exact: true });
  await save.click();
  await expect(dialog.getByRole("alert")).toContainText("Fixture metadata update failed.");
  await expect(displayName).toHaveValue("Keep this display name");
  await expect(description).toHaveValue("Keep this description after a save failure.");

  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(opener).toBeFocused();
});
