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
  });
}

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
    const runFilters = page.getByRole("form", { name: "Workflow run filters" });
    const more = runFilters.getByText("More filters", { exact: true });
    const primary = runFilters.locator(".studio-collection-toolbar");
    expect((await more.boundingBox())!.y - (await primary.boundingBox())!.y - (await primary.boundingBox())!.height).toBeGreaterThanOrEqual(8);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("runs-empty.png"), fullPage: true });
    await page.goto(`/collection-ux.html?view=agents&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "No agents yet", exact: true })).toBeVisible();
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
  const empty = page.locator(".studio-result-state");
  await empty.getByRole("button", { name: "Create connection", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
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
