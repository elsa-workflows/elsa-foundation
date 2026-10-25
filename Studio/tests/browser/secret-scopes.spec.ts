import { expect, test } from "@playwright/test";

const fixtureValue = "fixture-secret-value-must-not-return";
const fixtureScope = " Org.Team-A/Case ";

async function loadSecretsPage(page: import("@playwright/test").Page, scenario = "default") {
  await page.goto(`/secret-scopes.html?scenario=${scenario}`);
  const opener = page.locator(".secrets-header").getByRole("button", { name: "Create secret", exact: true });
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

async function revealOptionalFields(dialog: import("@playwright/test").Locator) {
  const more = dialog.getByRole("button", { name: /^More options/ });
  await expect(more).toHaveAttribute("aria-expanded", "false");
  await more.click();
  await expect(more).toHaveAttribute("aria-expanded", "true");
  return more;
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
  await expect(page.getByRole("button", { name: "Known later credentials", exact: true })).toBeVisible();
  await expect(count).toHaveText("1 shown · 1 loaded · 1 result for “knownlaterintegrationbillingcredentialsforregionalpartnernetwork”");
  await search.fill("draft should not relabel this search");
  await expect(count).toHaveText("1 shown · 1 loaded · 1 result for “knownlaterintegrationbillingcredentialsforregionalpartnernetwork”");
  await expect(listRequests).toHaveText("2");

  const scope = page.getByRole("combobox", { name: "Scope", exact: true });
  await scope.selectOption("Integration:Later");
  const rowTrigger = page.getByRole("button", { name: "Known later credentials", exact: true });
  await rowTrigger.click();
  const detail = page.getByRole("dialog", { name: "Edit secret" });
  await expect(detail).toBeVisible();
  await detail.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(rowTrigger).toBeFocused();
  await expect(scope).toHaveValue("Integration:Later");
  await expect(count).toHaveText("1 shown · 1 loaded · 1 result for “knownlaterintegrationbillingcredentialsforregionalpartnernetwork”");

  await rowTrigger.click();
  await expect(detail).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(detail).toHaveCount(0);
  await expect(rowTrigger).toBeFocused();
  await expect(page.getByRole("button", { name: "Known later credentials", exact: true })).toBeVisible();
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
        table: bounds(".secrets-table-shell")
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
  await expect(page.getByRole("button", { name: "Known later credentials", exact: true })).toBeVisible();
  await expect(count).toHaveText(`1 shown · 1 loaded · 1 result for “${laterName}”`);
  await expect(scope).toHaveValue("");
  await expect(page.locator("body")).not.toContainText("raw-secret-value");
});

for (const source of [
  { scenario: "default", label: "Value", wrapper: ".secrets-value-field" },
  { scenario: "configuration", label: "Configuration key", wrapper: "label" }
]) {
  test(`required source is visible at 1280x800: ${source.label}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    const { dialog } = await openCreateSecret(page, source.scenario);
    const control = dialog.getByLabel(source.label, { exact: true });
    await expect(control).toHaveCount(1);
    await expect(control).toBeVisible();
    // Measure before any field interaction or scrollIntoView: labels and the complete
    // input/toggle must fit inside the body's visible client area, not merely its DOM.
    const geometry = await control.evaluate((element, wrapper) => {
      const body = element.closest(".secrets-create-content")!;
      const sourceField = element.closest(wrapper)!;
      const bodyRect = body.getBoundingClientRect();
      const fieldRect = sourceField.getBoundingClientRect();
      return {
        scrollTop: body.scrollTop,
        bodyTop: bodyRect.top + body.clientTop,
        bodyBottom: bodyRect.top + body.clientTop + body.clientHeight,
        fieldTop: fieldRect.top,
        fieldBottom: fieldRect.bottom
      };
    }, source.wrapper);
    await testInfo.attach("initial-source-geometry", { body: JSON.stringify(geometry), contentType: "application/json" });
    expect(geometry.scrollTop).toBe(0);
    expect(geometry.fieldTop).toBeGreaterThanOrEqual(geometry.bodyTop);
    expect(geometry.fieldBottom).toBeLessThanOrEqual(geometry.bodyBottom);
    await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeInViewport();
  });
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
    await revealOptionalFields(dialog);
    await dialog.getByLabel("Tags", { exact: true }).scrollIntoViewIfNeeded();
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
  await revealOptionalFields(dialog);
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

test("More options preserves authored choices and keeps context outside the fold", async ({ page }) => {
  const { dialog } = await openCreateSecret(page);
  const optionalFields = [
    { label: "Display name", role: "textbox" },
    { label: "Description", role: "textbox" },
    { label: "Tags", role: "textbox" },
    { label: "Store", role: "combobox" },
    { label: "Scope", role: "combobox" }
  ] as const;
  for (const { label, role } of optionalFields) {
    const field = dialog.getByRole(role, { name: label, exact: true, includeHidden: true });
    await expect(field).toHaveCount(1);
    await expect(field).toBeHidden();
  }
  const hint = dialog.getByText("Optional qualifier. A reference that specifies a scope must match it (ignoring case). Scope does not grant access.", { exact: true });
  await expect(hint).toHaveCount(1);
  await expect(hint).toBeVisible();
  const storeContext = dialog.getByText("Store: Encrypted", { exact: true });
  const expires = dialog.getByLabel("Expires", { exact: true });
  await expect(storeContext).toBeVisible();
  await expect(expires).toBeVisible();
  await fillRequiredMetadata(dialog, "folded.secret");
  await dialog.getByLabel("Expires", { exact: true }).fill("2027-01-15T10:30");
  const more = await revealOptionalFields(dialog);
  const regionId = await more.getAttribute("aria-controls");
  expect(regionId).toBeTruthy();
  const region = dialog.locator(`[id="${regionId}"]`);
  for (const { label, role } of optionalFields) await expect(region.getByRole(role, { name: label, exact: true })).toBeVisible();
  await expect(region.getByLabel("Expires", { exact: true })).toHaveCount(0);
  await expect(region.getByText("Store: Encrypted", { exact: true })).toHaveCount(0);
  await expect(storeContext).toBeVisible();
  await expect(expires).toBeVisible();
  await expect(more).not.toContainText("set");
  const scope = dialog.getByRole("combobox", { name: "Scope", exact: true });
  const descriptionIds = (await scope.getAttribute("aria-describedby"))?.split(/\s+/) ?? [];
  expect(descriptionIds).toContain(await hint.getAttribute("id"));
  await dialog.getByLabel("Display name", { exact: true }).fill("Authored display name");
  await dialog.getByLabel("Description", { exact: true }).fill("Authored description");
  await dialog.getByLabel("Tags", { exact: true }).fill("retained");
  await expect(more).toContainText("3 set");
  await more.click();
  await expect(more).toHaveAttribute("aria-expanded", "false");
  await expect(hint).toBeVisible();
  await expect(storeContext).toBeVisible();
  await expect(expires).toBeVisible();
  await expect(more).not.toContainText(fixtureValue);
  await expect(more).not.toContainText("Authored display name");
  await more.click();
  await expect(dialog.getByLabel("Display name", { exact: true })).toHaveValue("Authored display name");
  await expect(dialog.getByLabel("Description", { exact: true })).toHaveValue("Authored description");
  await expect(dialog.getByLabel("Tags", { exact: true })).toHaveValue("retained");
  await expect(dialog.getByLabel("Value", { exact: true })).toHaveValue(fixtureValue);
});

test("Scope failure preserves focused custom input and stays disclosed after recovery", async ({ page }) => {
  const { dialog } = await openCreateSecret(page, "focused-retry");
  const more = await revealOptionalFields(dialog);
  await fillRequiredMetadata(dialog, "focused.scope");
  const custom = await beginCustomScopeRefresh(dialog);
  await custom.focus();
  await page.evaluate(() => window.dispatchEvent(new Event("fixture-scope-failure")));
  const error = dialog.getByRole("alert");
  await expect(error).toContainText("Scope suggestions could not be refreshed.");
  await expect(custom).toBeFocused();
  await expect(custom).toHaveValue(fixtureScope);
  await expect(more).toHaveAttribute("aria-expanded", "true");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeEnabled();
  await error.getByRole("button", { name: "Retry scope suggestions", exact: true }).click();
  await expect(error).toHaveCount(0);
  await expect(more).toHaveAttribute("aria-expanded", "true");
  await more.click();
  await expect(more).toHaveAttribute("aria-expanded", "false");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const metadata = page.getByTestId("created-secret-metadata");
  await expect(metadata).toContainText('"scope":" Org.Team-A/Case "');
  await expect(metadata).not.toContainText(fixtureValue);
  await expect(page.getByTestId("unexpected-secret-reads")).toHaveText("0");
});

test("Scope failure reopens collapsed options and keeps recovery visible until dismissed", async ({ page }) => {
  const { dialog } = await openCreateSecret(page, "focused-retry");
  const more = await revealOptionalFields(dialog);
  await fillRequiredMetadata(dialog, "collapsed.scope");
  const custom = await beginCustomScopeRefresh(dialog);
  await more.click();
  await expect(more).toHaveAttribute("aria-expanded", "false");
  await page.evaluate(() => window.dispatchEvent(new Event("fixture-scope-failure")));
  await expect(more).toHaveAttribute("aria-expanded", "true");
  await expect(custom).toHaveValue(fixtureScope);
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeEnabled();
  await more.click();
  await expect(more).toHaveAttribute("aria-expanded", "true");
  await dialog.getByRole("button", { name: "Retry scope suggestions", exact: true }).click();
  await expect(dialog.getByRole("alert")).toHaveCount(0);
  await expect(more).toHaveAttribute("aria-expanded", "true");
  await more.click();
  await expect(more).toHaveAttribute("aria-expanded", "false");
  await more.click();
  await expect(custom).toHaveValue(fixtureScope);
  await expect(dialog.getByLabel("Value", { exact: true })).toHaveValue(fixtureValue);
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

test("restores row Edit focus when a metadata refresh temporarily removes the opener", async ({ page }) => {
  await page.goto("/secret-scopes.html?scenario=editor-focus-refresh");
  const originalOpener = page.locator('button[data-secret-open="billing.integration-key"]');
  await expect(originalOpener).toBeVisible();
  const originalElement = await originalOpener.elementHandle();
  expect(originalElement).toBeTruthy();
  await originalOpener.click();

  const dialog = page.getByRole("dialog", { name: "Edit secret", exact: true });
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
  const createDialog = page.getByRole("dialog", { name: "Create secret", exact: true });
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
  const editDialog = page.getByRole("dialog", { name: "Edit secret", exact: true });
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
