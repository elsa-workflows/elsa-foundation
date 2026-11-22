import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  test(`executable row hover and navigation remain consistent in ${theme}`, async ({ page }, info) => {
    await page.goto(`/collection-ux.html?theme=${theme}`);
    const table = page.getByRole("table", { name: "Workflow executables", exact: true });
    const row = table.getByRole("row", { name: "Inspect executable hello-artifact", exact: true });
    const source = row.getByRole("button", { name: "Open source definition hello: Hello World", exact: true });
    const background = () => row.evaluate(element => getComputedStyle(element).backgroundColor);
    await expect(source).toBeVisible();
    const resting = await background();

    await source.hover();
    const hovered = await background();
    expect(hovered).not.toBe(resting);
    await expect(source).toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
    expect((await source.boundingBox())!.width).toBeLessThan((await row.locator(".wf-executable-name").boundingBox())!.width);
    await page.screenshot({ path: info.outputPath("source-hover.png"), fullPage: true });

    const identifiers = row.locator(".wf-executable-identifiers");
    await identifiers.hover();
    expect(await background()).toBe(hovered);
    await page.screenshot({ path: info.outputPath("identifiers-hover.png"), fullPage: true });
    await row.getByRole("button", { name: "Copy Artifact ID", exact: true }).click();
    await expect(page).toHaveURL(/collection-ux\.html/);
    await row.getByRole("button", { name: "Identifiers for Hello World", exact: true }).click();
    await expect(page.getByRole("dialog", { name: "Workflow executable identifiers for Hello World", exact: true })).toBeVisible();
    await page.locator(".wf-definition-identifiers-popover").click({ position: { x: 2, y: 2 } });
    await expect(page).toHaveURL(/collection-ux\.html/);
    await page.keyboard.press("Escape");
    await expect(page).toHaveURL(/collection-ux\.html/);

    await identifiers.click();
    await expect(page).toHaveURL(/\/workflows\/executables\/hello-artifact$/);
    await page.goBack();
    await source.click();
    await expect(page).toHaveURL(/\/workflows\/definitions\?definition=hello$/);
    await page.goBack();
    for (const key of ["Enter", "Space"]) {
      await row.focus();
      await page.keyboard.press("Tab");
      await page.keyboard.press("Shift+Tab");
      await page.mouse.move(0, 0);
      await expect(row).toBeFocused();
      expect(await background()).toBe(hovered);
      await row.press(key);
      await expect(page).toHaveURL(/\/workflows\/executables\/hello-artifact$/);
      await page.goBack();
    }

    const actions = row.getByRole("button", { name: "Actions for Hello World", exact: true });
    await actions.focus();
    await page.keyboard.press("Enter");
    const managePublication = page.getByRole("menuitem", { name: "Manage publication", exact: true });
    await expect(managePublication).toBeVisible();
    await managePublication.focus();
    await page.keyboard.press("Enter");
    await expect(page).toHaveURL(/\/workflows\/definitions\?definition=hello&panel=artifacts$/);
  });
}

for (const width of [360, 1280]) for (const theme of ["light", "dark"]) for (const collection of ["dashboards", "questions"] as const) {
  test(`${collection} no-row states use the shared collection surface at ${width}px ${theme}`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/collection-ux.html?view=${collection}&theme=${theme}`);
    const empty = page.locator(".studio-page[data-studio-collection] .studio-result-state");
    await expect(page.getByRole("heading", { name: collection === "dashboards" ? "Dashboards" : "Questions", exact: true })).toBeVisible();
    await expect(empty).toBeVisible();
    await expect(empty).not.toHaveAttribute("data-tone", "danger");
    const emptyHeading = empty.locator(":is(h2, h3)");
    const initialHeading = (await emptyHeading.textContent())!;
    expect(initialHeading).toContain("No");
    const frame = (await empty.boundingBox())!;
    expect(frame.width).toBeGreaterThan(width - 64);
    expect(frame.height).toBeGreaterThanOrEqual(140);
    await expect(empty).toHaveCSS("border-top-style", "solid");
    await expect(empty).toHaveCSS("border-top-width", "1px");
    const createLabel = `Create ${collection === "dashboards" ? "dashboard" : "question"}`;
    const createButtons = page.getByRole("button", { name: createLabel, exact: true });
    await expect(createButtons).toHaveCount(2);
    for (const button of await createButtons.all()) await expect(button).toBeEnabled();
    await page.screenshot({ path: info.outputPath(`${collection}-empty.png`), fullPage: true });

    await page.getByRole("searchbox", { name: `Search ${collection}`, exact: true }).fill(`missing ${collection}`);
    await page.getByRole("button", { name: "Search", exact: true }).click();
    await expect(emptyHeading).not.toHaveText(initialHeading);
    await expect(empty).toBeVisible();
    await expect(page.getByRole("button", { name: createLabel, exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

for (const width of [360, 1280]) for (const theme of ["light", "dark"]) {
  test(`empty Executables collection uses the shared panel at ${width}px ${theme}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/collection-ux.html?empty&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No workflow executables", exact: true })).toBeVisible();
    const empty = page.locator(".studio-collection-empty");
    await expect(empty).toBeVisible();
    expect((await empty.boundingBox())!.width).toBeGreaterThan(width - 64);
    expect((await empty.boundingBox())!.height).toBeGreaterThanOrEqual(140);
    await expect(empty).toHaveCSS("border-top-width", "1px");
    await expect(page.getByRole("button", { name: "Open workflow definitions", exact: true })).toBeVisible();
    await expect(page.getByRole("table", { name: "Workflow executables" })).toHaveCount(0);
  });
}

for (const width of [360, 1280]) for (const theme of ["light", "dark"]) {
  test(`Connections no-row states use the shared collection panel at ${width}px ${theme}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/collection-ux.html?view=connections&theme=${theme}`);
    const empty = page.locator(".studio-collection-empty");
    await expect(page.getByRole("heading", { name: "No connections yet", exact: true })).toBeVisible();
    await expect(empty).toBeVisible();
    expect((await empty.boundingBox())!.width).toBeGreaterThan(width - 64);
    expect((await empty.boundingBox())!.height).toBeGreaterThanOrEqual(140);
    await expect(page.getByRole("button", { name: "New connection", exact: true })).toBeVisible();
    await page.goto(`/collection-ux.html?view=connections&searched&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No matching connections", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Clear search", exact: true })).toBeVisible();
  });
}

test("dashboard collection errors remain errors instead of becoming empty results", async ({ page }) => {
  await page.goto("/collection-ux.html?view=dashboards&dashboard-error");
  await expect(page.getByRole("alert")).toContainText("Dashboard collection unavailable.");
  await expect(page.locator(".studio-collection-empty")).toHaveCount(0);
  await expect(page.locator(".studio-page[data-studio-collection] .studio-result-state")).not.toHaveCSS("border-top-width", "1px");
});

for (const width of [320, 1440, 2552]) for (const theme of ["light", "dark"]) {
  test(`collection spacing and evidence labels at ${width}px ${theme}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto(`/collection-ux.html?theme=${theme}`);
    const table = page.getByRole("table", { name: "Workflow executables", exact: true });
    await expect(table.getByText("Hello World", { exact: true })).toBeVisible();
    const checkbox = page.getByRole("checkbox", { name: "Include retired references" });
    expect((await checkbox.boundingBox())!.width).toBeLessThanOrEqual(20);
    expect((await checkbox.boundingBox())!.height).toBeLessThanOrEqual(20);
    const toolbar = page.locator(".studio-collection-toolbar");
    const bounds = (await table.boundingBox())!;
    expect(bounds.y - (await toolbar.boundingBox())!.y - (await toolbar.boundingBox())!.height).toBeGreaterThanOrEqual(12);
    expect(bounds.height).toBeLessThan(300);
    await page.screenshot({ path: testInfo.outputPath("executables.png"), fullPage: true });
    await page.goto(`/collection-ux.html?view=settings&theme=${theme}`);
    await expect(page.getByRole("radio", { name: /^Diagnostic snapshots/ })).toBeChecked();
    await expect(page.getByText("New capture events use")).toHaveCount(0);
    const labels = page.locator(".runtime-diagnostics-applied");
    await expect(labels).toHaveCount(8);
    for (const label of await labels.all()) {
      expect(await label.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      expect(await label.evaluate(element => getComputedStyle(element).textOverflow)).not.toBe("ellipsis");
    }
    await expect(page.getByRole("heading", { name: "Host policy", exact: true })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("settings.png"), fullPage: true });
    await page.goto(`/collection-ux.html?view=runs&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No workflow runs yet", exact: true })).toBeVisible();
    await expect(page.locator(".studio-collection-empty")).toBeVisible();
    const runFilters = page.getByRole("form", { name: "Workflow run filters" });
    const more = runFilters.getByText("More filters", { exact: true });
    const primary = runFilters.locator(".studio-collection-toolbar");
    expect((await more.boundingBox())!.y - (await primary.boundingBox())!.y - (await primary.boundingBox())!.height).toBeGreaterThanOrEqual(8);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("runs-empty.png"), fullPage: true });
    const disclosure = runFilters.getByRole("button", { name: /More filters/ });
    await expect(disclosure).toHaveAttribute("aria-expanded", "false");
    await disclosure.click();
    await expect(disclosure).toHaveAttribute("aria-expanded", "true");
    const advancedFilters = runFilters.locator(".wf-run-advanced-panel");
    await expect(advancedFilters).toBeVisible();
    const fieldWidths: number[] = [];
    for (const [name, fieldCount] of [["Run attributes", 3], ["Workflow identifiers", 3], ["Date range", 2]] as const) {
      const group = advancedFilters.getByRole("group", { name });
      const fields = group.locator(".wf-run-filter-field");
      await expect(fields).toHaveCount(fieldCount);
      const positions = await fields.evaluateAll(elements => elements.map(element => {
        const { x, y, width: fieldWidth } = element.getBoundingClientRect();
        return { x: Math.round(x), y: Math.round(y), width: Math.round(fieldWidth) };
      }));
      expect(new Set(positions.map(position => position.y)).size).toBe(width <= 320 ? fieldCount : 1);
      expect(new Set(positions.map(position => position.x)).size).toBe(width <= 320 ? 1 : fieldCount);
      expect(Math.max(...positions.map(position => position.width)) - Math.min(...positions.map(position => position.width))).toBeLessThanOrEqual(1);
      fieldWidths.push(...positions.map(position => position.width));
    }
    expect(Math.max(...fieldWidths) - Math.min(...fieldWidths)).toBeLessThanOrEqual(1);
    for (const label of ["Workflow run execution id", "Workflow run artifact", "Workflow run correlation", "Workflow run from", "Workflow run to"]) {
      const control = advancedFilters.getByLabel(label);
      await expect(control).toBeVisible();
      expect(await control.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    }
    await expect(advancedFilters.getByRole("button", { name: "Apply filters", exact: true })).toBeVisible();
    expect(await advancedFilters.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("runs-filters-expanded.png"), fullPage: true });
    await disclosure.click();
    await expect(disclosure).toHaveAttribute("aria-expanded", "false");
    await expect(advancedFilters).toBeHidden();
    await page.goto(`/collection-ux.html?view=agents&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No agents yet", exact: true })).toBeVisible();
    await expect(page.locator(".studio-collection-empty")).toBeVisible();
    const createAgent = page.getByRole("button", { name: "Create agent", exact: true });
    const createBounds = (await createAgent.boundingBox())!;
    expect(createBounds.x).toBeGreaterThanOrEqual(0);
    expect(createBounds.x + createBounds.width).toBeLessThanOrEqual(width);
    await createAgent.click();
    await expect(page.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
    await expect(page.getByRole("searchbox")).toHaveCount(0);
    await expect(page.getByRole("table")).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("new-agent.png"), fullPage: true });
    await page.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(page.getByRole("button", { name: "New agent", exact: true })).toBeFocused();
  });
}

test("connection empty state creates and returns to populated list without another dialog", async ({ page }) => {
  let created = false;
  const connection = { tenantId: "demo", name: "example", displayName: "Example", typeName: "http", authScheme: "bearer", settings: {}, secretFieldNames: [], owner: "tenant", managedBy: "", status: "Ready", createdAt: "2026-11-03T10:00:00Z", tags: [] };
  await page.route("**/connections?*", route => route.fulfill({ json: { items: created ? [connection] : [] } }));
  await page.route("**/connections/descriptors", route => route.fulfill({ json: { types: [{ name: "http", displayName: "HTTP", supportedSchemes: ["bearer"], settings: [] }], schemes: [{ name: "bearer", displayName: "Bearer token", fields: [], hasInteractiveAuthorization: false }] } }));
  await page.route("**/connections", route => { created = true; return route.fulfill({ json: connection }); });
  await page.goto("/?mode=connections");
  const empty = page.locator(".studio-collection-empty");
  await expect(empty).toBeVisible();
  expect((await empty.boundingBox())!.height).toBeGreaterThanOrEqual(140);
  await empty.getByRole("button", { name: "New connection", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "New connection", exact: true });
  await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("example");
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  await expect(page.getByRole("button", { name: "Details for Example", exact: true })).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(page.getByRole("searchbox", { name: "Search connections", exact: true })).toBeFocused();

  const row = page.getByRole("row", { name: /Example/ });
  const service = row.getByRole("cell", { name: "HTTP", exact: true });
  await service.hover();
  await expect(row).toHaveCSS("cursor", "pointer");
  await service.click();
  const details = page.getByRole("dialog", { name: "Example", exact: true });
  await expect(details).toBeVisible();
  await details.getByRole("button", { name: "Close connection details" }).click();
  await expect(details).toBeHidden();
  await row.press("Enter");
  await expect(details).toBeVisible();
});
