import { expect, test } from "@playwright/test";

const fixtureValue = "fixture-secret-value-must-not-return";
const fixtureScope = " Org.Team-A/Case ";

async function loadSecretsPage(page: import("@playwright/test").Page, scenario = "default") {
  await page.goto(`/secret-scopes.html?scenario=${scenario}`);
  const opener = page.locator(".studio-page-header").getByRole("button", { name: "Create secret", exact: true });
  await expect(opener).toBeEnabled();
  return opener;
}

async function openCreateSecret(page: import("@playwright/test").Page, scenario = "default") {
  const opener = await loadSecretsPage(page, scenario);
  await opener.click();
  const dialog = page.getByRole("region", { name: "Create secret", exact: true });
  await expect(dialog).toBeVisible();
  return { dialog, opener };
}

async function openEditSecret(page: import("@playwright/test").Page, scenario = "editor") {
  await page.goto(`/secret-scopes.html?scenario=${scenario}`);
  const opener = page.getByRole("button", { name: "Edit billing.integration-key", exact: true });
  await expect(opener).toBeEnabled();
  await opener.click();
  const dialog = page.getByRole("region", { name: "Edit secret", exact: true });
  await expect(dialog).toBeVisible();
  return { dialog, opener };
}

async function selectSection(editor: import("@playwright/test").Locator, section: "Secret" | "Details" | "Storage & lifetime") {
  // A section may carry a count and, after a failed option load, an attention marker.
  const tab = editor.getByRole("tab", { name: new RegExp(`^${section}(?: \\(\\d+\\))?(?: — needs attention)?$`) });
  await tab.click();
  await expect(tab).toHaveAttribute("aria-selected", "true");
  return tab;
}

async function fillRequiredMetadata(dialog: import("@playwright/test").Locator, name: string) {
  await selectSection(dialog, "Secret");
  await dialog.getByLabel("Technical name", { exact: true }).fill(name);
  await dialog.getByLabel("Value", { exact: true }).fill(fixtureValue);
}

async function revealOptionalFields(editor: import("@playwright/test").Locator) {
  return selectSection(editor, "Storage & lifetime");
}

async function beginCustomScopeRefresh(dialog: import("@playwright/test").Locator) {
  await dialog.getByRole("combobox", { name: "Scope", exact: true }).selectOption({ label: "Custom…" });
  const custom = dialog.getByRole("textbox", { name: "Custom scope value", exact: true });
  await custom.fill(fixtureScope);
  await dialog.getByRole("button", { name: "Refresh scope suggestions", exact: true }).click();
  await expect(dialog.getByRole("status")).toContainText("Loading scope suggestions…");
  return custom;
}

test("inventory coverage stays truthful through search, local scope filtering and detail return", async ({ page }) => {
  await page.emulateMedia({ colorScheme: "light" });
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/secret-scopes.html?scenario=inventory");

  const count = page.locator(".secrets-count");
  const coverage = page.locator(".secrets-coverage");
  const listRequests = page.getByTestId("secret-list-requests");
  await expect(count).toHaveText("100 shown · 100 loaded · 101 results");
  await expect(listRequests).toHaveText("1");
  await expect(coverage).toHaveText("Only the first 100 results are loaded. Search by name or display name to find other secrets. Filters apply to loaded results.");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "light");

  const search = page.getByRole("searchbox", { name: "Search secrets", exact: true });
  await search.fill("knownlaterintegrationbillingcredentialsforregionalpartnernetwork");
  await search.press("Enter");
  await expect(listRequests).toHaveText("2");
  await expect(page.getByRole("link", { name: "Known later credentials", exact: true })).toBeVisible();
  await expect(count).toHaveText("1 shown · 1 loaded · 1 result for “knownlaterintegrationbillingcredentialsforregionalpartnernetwork”");
  await search.fill("draft should not relabel this search");
  await expect(count).toHaveText("1 shown · 1 loaded · 1 result for “knownlaterintegrationbillingcredentialsforregionalpartnernetwork”");
  await expect(listRequests).toHaveText("2");

  const scope = page.getByRole("combobox", { name: "Scope", exact: true });
  await scope.selectOption("Integration:Later");
  const rowTrigger = page.getByRole("link", { name: "Known later credentials", exact: true });
  await rowTrigger.click();
  const detail = page.getByRole("region", { name: "Edit secret" });
  await expect(detail).toBeVisible();
  await detail.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("button", { name: "Edit knownlaterintegrationbillingcredentialsforregionalpartnernetwork", exact: true })).toBeFocused();
  await expect(scope).toHaveValue("Integration:Later");
  await expect(count).toHaveText("1 shown · 1 loaded · 1 result for “knownlaterintegrationbillingcredentialsforregionalpartnernetwork”");

  await rowTrigger.click();
  await expect(detail).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(detail).toBeVisible();
  await detail.getByRole("button", { name: "Back to secrets", exact: true }).click();
  await expect(detail).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Edit knownlaterintegrationbillingcredentialsforregionalpartnernetwork", exact: true })).toBeFocused();
  await expect(page.getByRole("link", { name: "Known later credentials", exact: true })).toBeVisible();
  await expect(scope).toHaveValue("Integration:Later");

  await page.emulateMedia({ colorScheme: "dark" });
  await page.setViewportSize({ width: 360, height: 640 });
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "dark");
  const measuredLayout = await page.evaluate(() => ({
    documentWidth: document.documentElement.scrollWidth,
    viewportWidth: window.innerWidth,
    count: (() => {
      const element = document.querySelector<HTMLElement>(".secrets-count")!;
      const bounds = element.getBoundingClientRect();
      const range = document.createRange();
      range.selectNodeContents(element);
      return {
        left: bounds.left,
        right: bounds.right,
        textRects: [...range.getClientRects()].map(textBounds => ({ left: textBounds.left, right: textBounds.right }))
      };
    })()
  }));
  expect(measuredLayout.documentWidth).toBeLessThanOrEqual(measuredLayout.viewportWidth);
  expect(measuredLayout.count.textRects.length).toBeGreaterThan(1);
  expect(measuredLayout.count.left).toBeGreaterThanOrEqual(0);
  expect(measuredLayout.count.right).toBeLessThanOrEqual(measuredLayout.viewportWidth);
  for (const bounds of measuredLayout.count.textRects) {
    expect(bounds.left).toBeGreaterThanOrEqual(0);
    expect(bounds.right).toBeLessThanOrEqual(measuredLayout.count.right);
  }
  const overflowsWithoutCountWrapping = await count.evaluate(element => {
    const previous = element.style.overflowWrap;
    const countBounds = element.getBoundingClientRect();
    element.style.overflowWrap = "normal";
    const range = document.createRange();
    range.selectNodeContents(element);
    const overflows = [...range.getClientRects()].some(bounds => bounds.right > countBounds.right);
    element.style.overflowWrap = previous;
    return overflows;
  });
  expect(overflowsWithoutCountWrapping).toBe(true);

  await page.goto("/secret-scopes.html?scenario=inventory");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "dark");
  await expect(count).toHaveText("100 shown · 100 loaded · 101 results");
  await expect(coverage).toBeVisible();
  const guidanceLayout = await coverage.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    const range = document.createRange();
    range.selectNodeContents(element);
    return {
      left: bounds.left,
      right: bounds.right,
      textRects: [...range.getClientRects()].map(textBounds => ({ left: textBounds.left, right: textBounds.right })),
      documentWidth: document.documentElement.scrollWidth,
      viewportWidth: window.innerWidth
    };
  });
  expect(guidanceLayout.left).toBeGreaterThanOrEqual(0);
  expect(guidanceLayout.right).toBeLessThanOrEqual(guidanceLayout.viewportWidth);
  expect(guidanceLayout.textRects.length).toBeGreaterThan(1);
  for (const bounds of guidanceLayout.textRects) {
    expect(bounds.left).toBeGreaterThanOrEqual(0);
    expect(bounds.right).toBeLessThanOrEqual(guidanceLayout.viewportWidth);
  }
  expect(guidanceLayout.documentWidth).toBeLessThanOrEqual(guidanceLayout.viewportWidth);
  await expect(page.getByTestId("secret-mutation-requests")).toHaveText("0");
  await expect(page.getByTestId("unexpected-secret-reads")).toHaveText("0");
  await expect(page.locator("body")).not.toContainText("raw-secret-value");
});

test("partial-results explanation aligns with the list at desktop and narrow viewports", async ({ page }, testInfo) => {
  const measured: Array<{
    width: number;
    colorScheme: string;
    coverage: { left: number; right: number };
    layout: { left: number; right: number };
    table: { left: number; right: number };
  }> = [];

  for (const viewport of [
    { width: 1280, height: 800, colorScheme: "light" as const },
    { width: 390, height: 844, colorScheme: "dark" as const }
  ]) {
    await page.emulateMedia({ colorScheme: viewport.colorScheme });
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.goto("/secret-scopes.html?scenario=inventory");

    const coverage = page.locator(".secrets-coverage");
    await expect(coverage).toBeVisible();
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", viewport.colorScheme);
    const geometry = await page.evaluate(() => {
      const bounds = (selector: string) => {
        const element = document.querySelector<HTMLElement>(selector)!;
        const rect = element.getBoundingClientRect();
        return { left: rect.left, right: rect.right };
      };
      return {
        coverage: bounds(".secrets-coverage"),
        layout: bounds(".secrets-layout"),
        table: bounds(".studio-table-container")
      };
    });
    measured.push({ width: viewport.width, colorScheme: viewport.colorScheme, ...geometry });
    await testInfo.attach(`coverage-geometry-${viewport.width}-${viewport.colorScheme}`, {
      body: JSON.stringify(geometry),
      contentType: "application/json"
    });
  }

  const alignment = measured.map(geometry => ({
    viewport: `${geometry.width}px ${geometry.colorScheme}`,
    layout: {
      left: Number((geometry.coverage.left - geometry.layout.left).toFixed(2)),
      right: Number((geometry.coverage.right - geometry.layout.right).toFixed(2))
    },
    table: {
      left: Number((geometry.coverage.left - geometry.table.left).toFixed(2)),
      right: Number((geometry.coverage.right - geometry.table.right).toFixed(2))
    }
  }));
  expect(alignment).toEqual([
    { viewport: "1280px light", layout: { left: 0, right: 0 }, table: { left: 0, right: 0 } },
    { viewport: "390px dark", layout: { left: 0, right: 0 }, table: { left: 0, right: 0 } }
  ]);
});

test("keeps an active loaded scope visible when search returns a different scope", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/secret-scopes.html?scenario=inventory");

  const count = page.locator(".secrets-count");
  await expect(count).toHaveText("100 shown · 100 loaded · 101 results");
  const scope = page.getByRole("combobox", { name: "Scope", exact: true });
  await scope.selectOption("Scope:1");

  const search = page.getByRole("searchbox", { name: "Search secrets", exact: true });
  const laterName = "knownlaterintegrationbillingcredentialsforregionalpartnernetwork";
  await search.fill(laterName);
  await page.getByRole("button", { name: "Search", exact: true }).click();

  await expect(count).toHaveText(`0 shown · 1 loaded · 1 result for “${laterName}”`);
  await expect(scope).toHaveValue("Scope:1");
  await expect(scope.locator("option:checked")).toHaveText("Scope:1 (not in loaded results)");

  await scope.selectOption("");
  await expect(page.getByRole("link", { name: "Known later credentials", exact: true })).toBeVisible();
  await expect(count).toHaveText(`1 shown · 1 loaded · 1 result for “${laterName}”`);
  await expect(scope).toHaveValue("");
  await expect(page.locator("body")).not.toContainText("raw-secret-value");
});

for (const source of [
  { scenario: "default", label: "Value" },
  { scenario: "configuration", label: "Configuration key" }
]) {
  test(`required source is visible at 1280x800: ${source.label}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    const { dialog: editor } = await openCreateSecret(page, source.scenario);
    const control = editor.getByLabel(source.label, { exact: true });
    await expect(control).toBeInViewport();
    await expect(editor.getByRole("button", { name: "Create", exact: true })).toBeInViewport();
    await expect(page.getByRole("table")).toHaveCount(0);
    await testInfo.attach("initial-source-geometry", { body: JSON.stringify(await control.boundingBox()), contentType: "application/json" });
  });
}

for (const viewport of [
  { width: 1440, height: 900 },
  { width: 1280, height: 800 },
  { width: 360, height: 640 }
]) {
  test(`secret page keeps sections, reachable actions, and values on errors at ${viewport.width}x${viewport.height}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    const { dialog: editor, opener } = await openCreateSecret(page, "tall-error");
    const cancel = editor.getByRole("button", { name: "Cancel", exact: true });
    const create = editor.getByRole("button", { name: "Create", exact: true });
    // Descriptor prose no longer forces authors through a scrolling modal.
    await expect(editor).not.toContainText("This explanatory text is intentionally long");
    await fillRequiredMetadata(editor, "preserved.secret");
    await selectSection(editor, "Details");
    await editor.getByLabel("Tags", { exact: true }).fill("retained");
    await page.setViewportSize({ ...viewport, height: viewport.height - 24 });
    await create.click();
    const alert = editor.getByRole("alert");
    await expect(alert).toContainText("Fixture secret creation failed.");
    await expect(alert).toBeFocused();
    await selectSection(editor, "Secret");
    const technicalName = editor.getByLabel("Technical name", { exact: true });
    await expect(technicalName).toHaveValue("preserved.secret");
    await expect(editor.getByLabel("Value", { exact: true })).toHaveValue(fixtureValue);
    await expect(editor.getByLabel("Value", { exact: true })).toHaveAttribute("type", "password");
    await selectSection(editor, "Details");
    await expect(editor.getByLabel("Tags", { exact: true })).toHaveValue("retained");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await cancel.click();
    await expect(editor).toHaveCount(0);
    await expect(opener).toBeFocused();
  });
}

test("suggested scope selection shows advisory metadata and creates metadata only", async ({ page }) => {
  const { dialog } = await openCreateSecret(page);
  await revealOptionalFields(dialog);
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
  await expect(page.getByTestId("unexpected-secret-reads")).toHaveText("0");
});

test("custom scope is revealed by keyboard and keeps its exact case at 320px", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 900 });
  const { dialog } = await openCreateSecret(page);
  await revealOptionalFields(dialog);
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
    const form = document.querySelector<HTMLElement>(".secrets-editor-page form")!;
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
  await fillRequiredMetadata(dialog, "preserved.secret");
  await selectSection(dialog, "Details");
  await dialog.getByLabel("Display name", { exact: true }).fill("Preserved display name");
  await dialog.getByLabel("Description", { exact: true }).fill("Keep this description during retry.");
  await dialog.getByLabel("Tags", { exact: true }).fill("reviewed, preserved");
  await selectSection(dialog, "Storage & lifetime");
  const scope = dialog.getByRole("combobox", { name: "Scope", exact: true });
  await scope.selectOption("Integration.Legacy-CRM");

  await dialog.getByRole("button", { name: "Refresh scope suggestions", exact: true }).click();
  const catalogError = dialog.getByRole("alert").filter({ hasText: "Scope suggestions could not be refreshed." });
  await expect(catalogError).toContainText("Scope suggestions could not be refreshed.");
  await selectSection(dialog, "Secret");
  await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveValue("preserved.secret");
  await selectSection(dialog, "Details");
  await expect(dialog.getByLabel("Display name", { exact: true })).toHaveValue("Preserved display name");
  await expect(dialog.getByLabel("Description", { exact: true })).toHaveValue("Keep this description during retry.");
  await expect(dialog.getByLabel("Tags", { exact: true })).toHaveValue("reviewed, preserved");

  await selectSection(dialog, "Storage & lifetime");
  await catalogError.getByRole("button", { name: "Retry scope suggestions", exact: true }).click();
  const customScope = dialog.getByRole("textbox", { name: "Custom scope value", exact: true });
  await expect(customScope).toHaveValue("Integration.Legacy-CRM");
  await expect(catalogError).toHaveCount(0);
  await selectSection(dialog, "Secret");
  await expect(dialog.getByLabel("Technical name", { exact: true })).toHaveValue("preserved.secret");

  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const metadata = page.getByTestId("created-secret-metadata");
  await expect(metadata).toContainText('"scope":"Integration.Legacy-CRM"');
  await expect(metadata).toContainText('"displayName":"Preserved display name"');
  await expect(metadata).not.toContainText(fixtureValue);
});

test("catalog refresh shows its loading state without clearing the selected scope", async ({ page }) => {
  const { dialog } = await openCreateSecret(page, "loading");
  await revealOptionalFields(dialog);
  const scope = dialog.getByRole("combobox", { name: "Scope", exact: true });
  await scope.selectOption("Integration:Billing");
  await dialog.getByRole("button", { name: "Refresh scope suggestions", exact: true }).click();

  await expect(dialog.getByRole("status")).toContainText("Loading scope suggestions…");
  await expect(scope).toHaveValue("Integration:Billing");
  await expect(scope).toBeEnabled();
  await expect(dialog.getByRole("status")).toHaveCount(0, { timeout: 3000 });
  await expect(scope).toHaveValue("Integration:Billing");
});

test("named sections preserve authored choices and validate the first invalid field", async ({ page }) => {
  const { dialog: editor } = await openCreateSecret(page);
  await expect(editor.getByRole("tablist", { name: "Secret sections" })).toBeVisible();
  await expect(editor.getByRole("button", { name: /^More options/ })).toHaveCount(0);
  await selectSection(editor, "Details");
  await editor.getByLabel("Display name", { exact: true }).fill("Authored display name");
  await editor.getByLabel("Description", { exact: true }).fill("Authored description");
  await editor.getByLabel("Tags", { exact: true }).fill("retained");
  await expect(editor.getByRole("tab", { name: "Details (3)", exact: true })).toBeVisible();
  await editor.getByRole("button", { name: "Create", exact: true }).click();
  await expect(editor.getByRole("tab", { name: "Secret", exact: true })).toHaveAttribute("aria-selected", "true");
  await expect(editor.getByLabel("Technical name", { exact: true })).toBeFocused();
  await editor.getByLabel("Technical name", { exact: true }).fill("tabbed.secret");
  await selectSection(editor, "Storage & lifetime");
  // One compatible store is implicit; scope guidance belongs beside Scope.
  await expect(editor.getByLabel("Store", { exact: true })).toHaveCount(0);
  const scope = editor.getByRole("combobox", { name: "Scope", exact: true });
  const hint = editor.getByText("References must match this scope (ignoring case). Scope does not grant access.", { exact: true });
  await expect(hint).toBeVisible();
  expect((await scope.getAttribute("aria-describedby"))?.split(/\s+/)).toContain(await hint.getAttribute("id"));
  await editor.getByLabel(/Expires/).fill("2027-01-15T10:30");
  await editor.getByRole("button", { name: "Create", exact: true }).click();
  await expect(editor.getByLabel("Value", { exact: true })).toBeFocused();
  await editor.getByLabel("Value", { exact: true }).fill(fixtureValue);
  const secretTab = await selectSection(editor, "Secret");
  await secretTab.focus();
  await page.keyboard.press("ArrowRight");
  await expect(editor.getByRole("tab", { name: "Details (3)", exact: true })).toBeFocused();
  await expect(editor.getByLabel("Display name", { exact: true })).toHaveValue("Authored display name");
  await expect(editor.getByLabel("Description", { exact: true })).toHaveValue("Authored description");
  await expect(editor.getByLabel("Tags", { exact: true })).toHaveValue("retained");
  await page.keyboard.press("End");
  await expect(editor.getByRole("tab", { name: "Storage & lifetime (1)", exact: true })).toBeFocused();
  await expect(editor.getByLabel(/Expires/)).toHaveValue("2027-01-15T10:30");
  await page.keyboard.press("Home");
  await expect(secretTab).toBeFocused();
  await expect(editor.getByLabel("Value", { exact: true })).toHaveValue(fixtureValue);
  await expect(editor.getByLabel("Value", { exact: true })).toHaveAttribute("type", "password");
  await expect(editor.getByRole("tablist")).not.toContainText(fixtureValue);
});

test("scope failure preserves focused custom input and supports recovery", async ({ page }) => {
  const { dialog: editor } = await openCreateSecret(page, "focused-retry");
  await fillRequiredMetadata(editor, "focused.scope");
  await selectSection(editor, "Storage & lifetime");
  const custom = await beginCustomScopeRefresh(editor);
  await custom.focus();
  await page.evaluate(() => window.dispatchEvent(new Event("fixture-scope-failure")));
  const error = editor.getByRole("alert").filter({ hasText: "Scope suggestions could not be refreshed." });
  await expect(error).toBeVisible();
  await expect(custom).toBeFocused();
  await expect(custom).toHaveValue(fixtureScope);
  await error.getByRole("button", { name: "Retry scope suggestions", exact: true }).click();
  await expect(error).toHaveCount(0);
  await expect(custom).toHaveValue(fixtureScope);
  await selectSection(editor, "Secret");
  await editor.getByRole("button", { name: "Create", exact: true }).click();
  const metadata = page.getByTestId("created-secret-metadata");
  await expect(metadata).toContainText('"scope":" Org.Team-A/Case "');
  await expect(metadata).not.toContainText(fixtureValue);
  await expect(page.getByTestId("unexpected-secret-reads")).toHaveText("0");
});

test("scope failure is recoverable across section changes without stealing focus", async ({ page }) => {
  const { dialog: editor } = await openCreateSecret(page, "focused-retry");
  await fillRequiredMetadata(editor, "background.scope");
  await selectSection(editor, "Storage & lifetime");
  await beginCustomScopeRefresh(editor);
  const detailsTab = await selectSection(editor, "Details");
  await page.evaluate(() => window.dispatchEvent(new Event("fixture-scope-failure")));
  await expect(editor.getByRole("alert").filter({ hasText: "Secret options could not be loaded." })).toBeVisible();
  await expect(detailsTab).toHaveAttribute("aria-selected", "true");
  await expect(detailsTab).toBeFocused();
  const attention = editor.getByRole("tab", { name: /^Storage & lifetime(?: \(\d+\))? — needs attention$/ });
  await expect(attention).toBeVisible();
  await selectSection(editor, "Storage & lifetime");
  await expect(editor.getByRole("textbox", { name: "Custom scope value", exact: true })).toHaveValue(fixtureScope);
  await editor.getByRole("button", { name: "Retry scope suggestions", exact: true }).click();
  await expect(editor.getByRole("alert")).toHaveCount(0);
  await expect(attention).toHaveCount(0);
  await selectSection(editor, "Secret");
  await expect(editor.getByLabel("Value", { exact: true })).toHaveValue(fixtureValue);
  await expect(page.getByTestId("unexpected-secret-reads")).toHaveText("0");
});

test("Configuration creation returns metadata without the authored source", async ({ page }) => {
  const { dialog } = await openCreateSecret(page, "configuration");
  const configurationKey = "Fixture:Private:ConfigurationKey";
  await dialog.getByLabel("Technical name", { exact: true }).fill("configuration.secret");
  await dialog.getByLabel("Configuration key", { exact: true }).fill(configurationKey);
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const metadata = page.getByTestId("created-secret-metadata");
  await expect(metadata).toContainText('"storeName":"configuration"');
  await expect(metadata).not.toContainText(configurationKey);
  const result = JSON.parse(await metadata.innerText());
  expect(result).not.toHaveProperty("configurationKey");
  expect(result).not.toHaveProperty("value");
  await expect(page.getByTestId("unexpected-secret-reads")).toHaveText("0");
});

test("Edit secret keeps Scope read-only, explains matching, and saves metadata only", async ({ page }) => {
  const { dialog } = await openEditSecret(page);
  await selectSection(dialog, "Storage & lifetime");
  const scope = dialog.getByRole("textbox", { name: "Scope", exact: true });
  await expect(scope).toHaveValue("Integration:CRM");
  await expect(scope).toHaveAttribute("readonly", "");
  const descriptionId = await scope.getAttribute("aria-describedby");
  expect(descriptionId).toBeTruthy();
  const scopeHelp = dialog.locator(`[id="${descriptionId}"]`);
  await expect(scopeHelp).toContainText("References must match this scope (ignoring case).");
  await expect(scopeHelp).toContainText("Scope does not grant access.");
  await expect(scopeHelp).toContainText("Scope is read-only in this editor.");

  await selectSection(dialog, "Details");
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

test("restores row Edit focus when a metadata refresh temporarily removes the opener", async ({ page }) => {
  await page.goto("/secret-scopes.html?scenario=editor-focus-refresh");
  const originalOpener = page.locator('a[data-secret-open="billing.integration-key"]');
  await expect(originalOpener).toBeVisible();
  const originalElement = await originalOpener.elementHandle();
  expect(originalElement).toBeTruthy();
  await originalOpener.click();

  const dialog = page.getByRole("region", { name: "Edit secret", exact: true });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel("Display name", { exact: true }).fill("Focus restored after delayed refresh");
  await dialog.getByRole("button", { name: "Save changes", exact: true }).click();
  await expect(page.getByTestId("secret-list-requests")).toHaveText("2");
  await expect(page.locator(".secrets-loading")).toBeVisible();
  await expect(originalOpener).toHaveCount(0);
  expect(await originalElement!.evaluate(element => element.isConnected)).toBe(false);

  await page.evaluate(() => window.dispatchEvent(new Event("fixture-release-editor-refresh")));
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('button[data-secret-edit="billing.integration-key"]')).toBeFocused();
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

test("secret mutation attempts are visible while pending and remain counted after failure", async ({ page }) => {
  await page.goto("/secret-scopes.html?scenario=mutation-attempts");
  const count = page.getByTestId("secret-mutation-requests");
  await expect(count).toHaveText("0");

  const create = page.getByRole("button", { name: "Create secret", exact: true });
  await create.click();
  const createDialog = page.getByRole("region", { name: "Create secret", exact: true });
  await fillRequiredMetadata(createDialog, "fixture.attempted-create");
  await createDialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(count).toHaveText("1");
  await page.evaluate(() => window.dispatchEvent(new Event("fixture-release-mutation")));
  await expect(createDialog.getByRole("alert")).toContainText("Fixture secret creation failed after the request was attempted.");
  await expect(createDialog.getByLabel("Technical name", { exact: true })).toHaveValue("fixture.attempted-create");
  await expect(createDialog.getByLabel("Value", { exact: true })).toHaveAttribute("type", "password");
  await expect(page.locator("body")).not.toContainText(fixtureValue);
  await expect(count).toHaveText("1");
  await createDialog.getByRole("button", { name: "Cancel", exact: true }).click();

  const edit = page.getByRole("button", { name: "Edit billing.integration-key", exact: true });
  await edit.click();
  const editDialog = page.getByRole("region", { name: "Edit secret", exact: true });
  await editDialog.getByLabel("Display name", { exact: true }).fill("Attempted metadata update");
  await editDialog.getByRole("button", { name: "Save changes", exact: true }).click();
  await expect(count).toHaveText("2");
  await page.evaluate(() => window.dispatchEvent(new Event("fixture-release-mutation")));
  await expect(editDialog.getByRole("alert")).toContainText("Fixture metadata update failed after the request was attempted.");
  await expect(editDialog.getByLabel("Display name", { exact: true })).toHaveValue("Attempted metadata update");
  await expect(count).toHaveText("2");
  await editDialog.getByRole("button", { name: "Cancel", exact: true }).click();

  const actions = page.getByRole("button", { name: "Actions for billing.integration-key", exact: true });
  await actions.click();
  const menu = page.getByRole("menu", { name: "Actions for billing.integration-key", exact: true });
  await menu.getByRole("menuitem", { name: "Delete", exact: true }).click();
  await expect(count).toHaveText("3");
  await page.evaluate(() => window.dispatchEvent(new Event("fixture-release-mutation")));
  await expect(page.getByRole("alert")).toContainText("Fixture secret deletion failed after the request was attempted.");
  await expect(count).toHaveText("3");
  await expect(edit).toBeVisible();
  await expect(page.getByTestId("unexpected-secret-reads")).toHaveText("0");
  await expect(page.locator("body")).not.toContainText(fixtureValue);
});
