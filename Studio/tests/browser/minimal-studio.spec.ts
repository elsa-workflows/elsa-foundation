import { expect, test, type Page } from "@playwright/test";
import axe from "axe-core";

const presets = [{ name: "Porcelain", id: "builtin:porcelain", mode: "light" }, { name: "Atelier", id: "builtin:atelier", mode: "light" }, { name: "Graphite", id: "builtin:graphite", mode: "dark" }];
async function preset(page: Page, name = "Atelier", width = 1440) {
  await page.setViewportSize({ width, height: 960 });
  await page.goto("/minimal-studio.html?screen=appearance&visual-study=1");
  await page.getByRole("radiogroup", { name: "Studio preset", exact: true }).getByRole("radio", { name, exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");
}
async function view(page: Page, name: string, extra = "") {
  await page.goto(`/minimal-studio.html?screen=${name}&visual-study=1${extra}`);
}

test("Atelier keeps the account readable in top navigation and renders its small selected-nav accent", async ({ page }) => {
  await preset(page);
  expect(await page.locator('.nav-section a.active').first().evaluate(element => getComputedStyle(element, "::before").display)).toBe("block");
  await page.getByRole("radiogroup", { name: "Navigation", exact: true }).getByRole("radio", { name: "Top", exact: true }).click();
  const account = page.getByRole("button", { name: "Account: Administrator", exact: true });
  await expect(account.locator(".user-menu-name")).toBeVisible();
  await page.addScriptTag({ content: axe.source });
  const violations = await account.evaluate(async element => (window as unknown as Window & { axe: typeof axe }).axe.run(element, { runOnly: ["color-contrast"] }).then(result => result.violations.map(v => v.id)));
  expect(violations).toEqual([]);
});

for (const item of presets) test(`${item.name} is a complete, persistent preset with restrained, accessible list details`, async ({ page, context }, testInfo) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await preset(page, item.name);
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", item.id);
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", item.mode);
  await expect(page.getByRole("radiogroup", { name: "Studio preset", exact: true }).getByRole("radio", { name: item.name, exact: true })).toHaveAttribute("aria-checked", "true");
  await view(page, "list");
  const table = page.getByRole("table", { name: "Workflow definitions", exact: true });
  await expect(table.getByRole("row")).toHaveCount(7);
  const row = table.getByRole("row").nth(1);
  await expect(row).toHaveCSS("border-bottom-style", "dotted");
  await expect(row).toHaveCSS("min-height", "56px");
  await expect(row).toContainText("Process new customer orders");
  const checkbox = row.getByRole("checkbox");
  await checkbox.check();
  await expect(row).toHaveAttribute("aria-selected", "true");
  await checkbox.uncheck();
  const copy = row.getByRole("button", { name: "Copy Definition ID", exact: true });
  await copy.click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("wf-study-1");
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toHaveCSS("font-size", "28px");
  await expect(page.locator(".sidebar")).toHaveCSS("width", "216px");
  const boundary = page.getByRole("region", { name: "Workflow definitions table", exact: true });
  expect(await boundary.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => (window as unknown as Window & { axe: typeof axe }).axe.run(document.querySelector(".studio-shell")!, { runOnly: ["color-contrast", "button-name", "label"] }).then(result => result.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.failureSummary) }))));
  expect(violations).toEqual([]);
  await page.screenshot({ path: testInfo.outputPath(`${item.id}-definitions.png`), fullPage: true });
});

for (const item of presets) test(`${item.name} keeps workflow headings inside their tracks at desktop and narrow widths`, async ({ page }) => {
  await preset(page, item.name, 1280);
  await view(page, "list", "&saved-version=1");
  const table = page.getByRole("table", { name: "Workflow definitions", exact: true });
  await expect(table).toBeVisible();
  await expect(table.locator(".wf-definition-description").first()).toBeVisible();
  const row = table.getByRole("row").nth(1);
  const latest = row.locator(".wf-definition-version").first();
  await expect(latest.locator(".wf-definition-version-number")).toHaveText("1.0.0");
  await page.evaluate(() => document.fonts.ready);
  for (const width of [1280, 1440, 320]) {
    await page.setViewportSize({ width, height: 960 });
    const versionCenters = await latest.evaluate(element => [...element.children].map(child => {
      const box = child.getBoundingClientRect();
      return box.y + box.height / 2;
    }));
    expect(versionCenters).toHaveLength(2);
    expect(Math.abs(versionCenters[0] - versionCenters[1])).toBeLessThanOrEqual(1);
    expect(await row.evaluate(element => element.getBoundingClientRect().height)).toBe(56);
    const overflowing = await table.getByRole("columnheader").evaluateAll(headers => headers.flatMap(header => {
      const label = header.textContent?.trim();
      if (!label) return [];
      const range = document.createRange();
      range.selectNodeContents(header);
      const text = range.getBoundingClientRect();
      const cell = header.getBoundingClientRect();
      return text.left < cell.left - 1 || text.right > cell.right + 1 ? [{ label, textLeft: text.left, textRight: text.right, cellLeft: cell.left, cellRight: cell.right }] : [];
    }));
    expect(overflowing, `${item.name} headings at ${width}px`).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  }
});

for (const width of [320, 1440]) test(`six real screen types retain containment and hierarchy at ${width}px`, async ({ page }, testInfo) => {
  await preset(page, "Atelier", width);
  for (const screen of ["appearance", "list", "runs", "templates", "logs", "inspection"]) {
    await view(page, screen, screen === "inspection" ? "&composition=sequence&host=instance&run-canvas=unique" : "");
    if (screen === "list") await expect(page.getByRole("table", { name: "Workflow definitions", exact: true })).toBeVisible();
    if (screen === "runs") await expect(page.getByRole("table").first()).toBeVisible();
    if (screen === "templates") await expect(page.getByRole("heading", { name: "Built-in starters", exact: true })).toBeVisible();
    if (screen === "logs") {
      await expect(page.getByRole("button", { name: "Pause", exact: true })).toBeVisible();
      await expect(page.getByRole("row", { name: /Select Information structured log/ }).first()).toBeVisible();
      await page.getByRole("row", { name: /Select Information structured log/ }).first().click();
      await expect(page.getByRole("complementary", { name: "Structured log details", exact: true })).toContainText("Request received");
    }
    if (screen === "inspection") {
      await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible();
      await page.getByRole("button", { name: "1. Notify buyer", exact: true }).click();
      await expect(page.getByRole("complementary", { name: "Run details panel", exact: true })).toContainText("Notify buyer");
    }
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), screen).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`${screen}-${width}.png`), fullPage: true });
  }
});

test("minimal logs preserve live controls, selected details and empty state", async ({ page }) => {
  await preset(page, "Graphite");
  await view(page, "logs");
  await page.getByRole("row", { name: /Select Information structured log/ }).first().click();
  await expect(page.getByRole("complementary", { name: "Structured log details", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Pause", exact: true }).click();
  await expect(page.getByRole("button", { name: "Resume", exact: true })).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "Resume", exact: true }).click();
  await page.getByRole("button", { name: "Close structured log details", exact: true }).click();
  await page.getByRole("textbox", { name: "Filter by category", exact: true }).fill("empty");
  await expect(page.getByRole("row", { name: /Select Information structured log/ })).toHaveCount(0);
  await expect(page.getByText("No structured logs received yet.", { exact: true })).toBeVisible();
});

test("a public UI consumer opts into Minimal without host CSS", async ({ page }) => {
  await page.route("**/samples/weather-forecast", route => route.fulfill({ json: [{ date: "2026-10-31", summary: "Sunny", temperatureC: 20, temperatureF: 68 }] }));
  await page.goto("/module-ui.html");
  await page.evaluate(() => { document.documentElement.dataset.workspaceStyle = "minimal"; });
  const header = page.locator(".studio-page-header").first();
  await expect(header.getByRole("heading")).toHaveCSS("font-size", "28px");
  const grid = page.getByRole("region", { name: "Selectable public collection", exact: true });
  const row = grid.getByRole("row").nth(1);
  await expect(row).toHaveCSS("min-height", "56px");
  await expect(row).toHaveCSS("border-bottom-style", "dotted");
  await row.focus();
  await row.press("Enter");
  await expect(row).toHaveAttribute("aria-selected", "true");
});

for (const item of presets) test(`${item.name} keeps long folder trees beside the full library view`, async ({ page }, testInfo) => {
  await preset(page, item.name);
  const navigation = page.getByRole("complementary", { name: "Workflow folders", exact: true });
  for (const treeHeight of ["short", "tall"]) {
    await view(page, "library", `&folder-layout=1${treeHeight === "tall" ? "&tall-folders=1" : ""}`);
    const folder = navigation.locator('[data-folder-id="finance"]');
    await folder.focus();
    await folder.press("ArrowRight");
    const child = navigation.locator('[data-folder-id="layout-child"]');
    await expect(child).toBeVisible();
    await folder.press("ArrowRight");
    await expect(child).toBeFocused();
    const table = page.getByRole("region", { name: "Workflow definitions table", exact: true });
    await expect(table).toBeVisible();
    for (const width of [1440, 1280]) {
      await page.setViewportSize({ width, height: 960 });
      for (const filtersOpen of [false, true]) {
        if (filtersOpen) await page.getByRole("button", { name: "Filters", exact: true }).click();
        const bounds = await navigation.boundingBox();
        const tableBounds = await table.boundingBox();
        expect(tableBounds!.x).toBeGreaterThanOrEqual(bounds!.x + bounds!.width);
        const controls = await page.getByRole("region", { name: "Workflow library organization", exact: true }).boundingBox();
        expect(Math.abs(controls!.x - tableBounds!.x)).toBeLessThanOrEqual(1);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
        if (filtersOpen) await page.getByRole("button", { name: "Filters", exact: true }).click();
      }
      for (const row of [folder.locator(":scope > .wf-folder-tree-folder"), child.locator(":scope > .wf-folder-tree-folder")]) {
        expect((await row.boundingBox())!.height).toBeLessThanOrEqual(34);
      }
      const folderBounds = await navigation.boundingBox();
      for (const row of await navigation.locator(".wf-folder-tree-row").all()) {
        const bounds = await row.boundingBox();
        expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(folderBounds!.x + folderBounds!.width);
      }
      const label = folder.locator(".wf-folder-tree-label").first();
      await expect(label).toHaveAttribute("title", "Customer operations and international support");
      await expect(label).toHaveText("Customer operations and international support");
      await page.screenshot({ path: testInfo.outputPath(`${item.name}-${treeHeight}-folder-layout-${width}.png`), fullPage: true });
    }
  }
  await page.setViewportSize({ width: 320, height: 960 });
  await expect(navigation).toBeHidden();
  await page.getByRole("button", { name: "Folders", exact: true }).click();
  const drawer = page.getByRole("dialog", { name: "Workflow folders", exact: true });
  await expect(drawer).toBeVisible();
  await expect(drawer.locator('[data-folder-id="layout-child"]')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});

test("the supported sixteen folder levels retain bounded labels and tree semantics", async ({ page }, testInfo) => {
  await preset(page);
  await view(page, "library", "&folder-layout=1&deep-folders=1");
  const navigation = page.getByRole("complementary", { name: "Workflow folders", exact: true });
  const id = (depth: number) => depth === 0 ? "finance" : depth === 1 ? "layout-child" : `layout-depth-${depth}`;
  for (let depth = 0; depth < 15; depth++) {
    const parent = navigation.locator(`[data-folder-id="${id(depth)}"]`);
    await parent.focus();
    await parent.press("ArrowRight");
    await expect(navigation.locator(`[data-folder-id="${id(depth + 1)}"]`)).toBeVisible();
  }
  await expect(navigation.getByRole("treeitem")).toHaveCount(18);
  const checkBounds = async (container: import("@playwright/test").Locator) => {
    const boundary = await container.boundingBox();
    for (const row of await container.locator(".wf-folder-tree-row").all()) {
      const bounds = await row.boundingBox();
      expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(boundary!.x + boundary!.width);
      expect(bounds!.height).toBeLessThanOrEqual(34);
    }
    for (const label of await container.locator(".wf-folder-tree-label").all()) {
      await expect(label).toBeVisible();
      expect((await label.boundingBox())!.width).toBeGreaterThanOrEqual(40);
    }
  };
  await checkBounds(navigation);
  await page.screenshot({ path: testInfo.outputPath("atelier-deep-folders-1440.png"), fullPage: true });
  await page.setViewportSize({ width: 320, height: 960 });
  await page.getByRole("button", { name: "Folders", exact: true }).click();
  const drawer = page.getByRole("dialog", { name: "Workflow folders", exact: true });
  await expect(drawer.getByRole("treeitem")).toHaveCount(18);
  await checkBounds(drawer);
});

test("Linen keeps the library beside folders with contained table scrolling", async ({ page }) => {
  await preset(page);
  await page.getByRole("radiogroup", { name: "Workspace style", exact: true }).getByRole("radio", { name: "Linen", exact: true }).click();
  await view(page, "library", "&folder-layout=1");
  const navigation = page.getByRole("complementary", { name: "Workflow folders", exact: true });
  const table = page.getByRole("region", { name: "Workflow definitions table", exact: true });
  await expect(table).toBeVisible();
  expect((await table.boundingBox())!.x).toBeGreaterThanOrEqual((await navigation.boundingBox())!.x + (await navigation.boundingBox())!.width);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
});

test("Classic dark keeps library fields and buttons aligned beside folders", async ({ page }, testInfo) => {
  await preset(page);
  const appearance = page.getByRole("region", { name: "Appearance", exact: true });
  await appearance.getByText("Theme presets", { exact: true }).click();
  await appearance.getByRole("radiogroup", { name: "Theme", exact: true }).getByRole("radio", { name: "Classic", exact: true }).click();
  await appearance.getByRole("radiogroup", { name: "Colour mode", exact: true }).getByRole("radio", { name: "Dark", exact: true }).click();
  await view(page, "library", "&folder-layout=1");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "classic");
  await expect(page.locator("html")).toHaveAttribute("data-theme", "classic");
  await expect(page.locator("html")).not.toHaveAttribute("data-workspace-style", /.+/);
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "dark");
  const controls = page.getByRole("region", { name: "Workflow library organization", exact: true });
  const table = page.getByRole("region", { name: "Workflow definitions table", exact: true });
  await expect(table).toBeVisible();
  for (const width of [1440, 1329]) {
    await page.setViewportSize({ width, height: 960 });
    const sort = await controls.getByLabel("Sort workflows", { exact: true }).boundingBox();
    for (const button of ["Favorites", "Filters", "Save view", "Manage tags"].map(name => controls.getByRole("button", { name, exact: true }))) {
      const bounds = await button.boundingBox();
      expect(Math.abs(bounds!.y + bounds!.height - sort!.y - sort!.height)).toBeLessThanOrEqual(1);
    }
    const navigation = await page.getByRole("complementary", { name: "Workflow folders", exact: true }).boundingBox();
    expect((await table.boundingBox())!.x).toBeGreaterThanOrEqual(navigation!.x + navigation!.width);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  }
  await page.screenshot({ path: testInfo.outputPath("classic-dark-folders-1329.png"), fullPage: true });
});

test("a host without folders keeps its full-width browse content", async ({ page }) => {
  await preset(page);
  await view(page, "library", "&folders=unavailable");
  const table = page.getByRole("region", { name: "Workflow definitions table", exact: true });
  await expect(table).toBeVisible();
  await expect(page.getByRole("complementary", { name: "Workflow folders", exact: true })).toHaveCount(0);
  await expect(page.getByRole("searchbox", { name: "Search all workflows", exact: true })).toBeVisible();
  await expect(page.getByRole("region", { name: "Workflow library organization", exact: true })).toBeVisible();
  const list = await page.locator(".wf-definitions-list").boundingBox();
  const bounds = await table.boundingBox();
  expect(Math.abs(bounds!.x - list!.x)).toBeLessThanOrEqual(1);
  expect(Math.abs(bounds!.width - list!.width)).toBeLessThanOrEqual(1);
});

for (const item of presets) for (const width of [320, 1440]) test(`${item.name} integrates folders, tags, favorites and saved views at ${width}px`, async ({ page }, testInfo) => {
  await preset(page, item.name, width);
  await view(page, "library");
  const payment = page.getByRole("row", { name: "Open workflow definition Collect overdue payment", exact: true });
  await expect(payment).toBeVisible();
  await payment.getByRole("button", { name: "Actions for Collect overdue payment", exact: true }).click();
  await page.getByRole("menuitem", { name: "Organization", exact: true }).click();
  const organize = page.getByRole("dialog", { name: "Organize Collect overdue payment", exact: true });
  await expect(organize.getByText("Finance", { exact: true })).toBeVisible();
  await organize.getByRole("checkbox", { name: "Critical", exact: true }).check();
  await expect(organize.getByRole("button", { name: "Save tags", exact: true })).toBeEnabled();
  await page.screenshot({ path: testInfo.outputPath(`${item.name}-organization-${width}.png`), fullPage: true });
  await organize.getByRole("button", { name: "Save tags", exact: true }).click();
  await expect(organize).toBeHidden();
  await page.getByRole("button", { name: "Filters", exact: true }).click();
  await page.getByRole("button", { name: "Add tag filter", exact: true }).click();
  await page.getByRole("checkbox", { name: "Tags", exact: true }).check();
  await page.getByRole("checkbox", { name: "Location", exact: true }).check();
  await page.getByRole("button", { name: "Save view", exact: true }).click();
  const save = page.getByRole("dialog", { name: "Save workflow view", exact: true });
  await save.getByRole("textbox", { name: "View name", exact: true }).fill("Critical workflows");
  await save.getByRole("checkbox", { name: "Share within this tenant", exact: true }).check();
  await save.getByRole("button", { name: "Save as new view", exact: true }).click();
  await expect(save).toBeHidden();
  await page.getByRole("combobox", { name: "Saved workflow view", exact: true }).selectOption({ label: "Critical workflows · Shared" });
  await expect(page).toHaveURL(/view=view-/);
  await expect(page.getByRole("columnheader", { name: "Tags", exact: true })).toBeVisible();
  await expect(page.getByRole("columnheader", { name: "Location", exact: true })).toBeVisible();
  const boundary = (await page.getByRole("region", { name: "Workflow definitions table", exact: true }).boundingBox())!;
  const actions = (await payment.getByRole("button", { name: "Actions for Collect overdue payment", exact: true }).boundingBox())!;
  expect(actions.x + actions.width).toBeLessThanOrEqual(boundary.x + boundary.width + 1);
  await payment.getByRole("button", { name: "Actions for Collect overdue payment", exact: true }).click();
  await page.getByRole("menuitem", { name: "Add to favorites", exact: true }).click();
  await page.getByRole("button", { name: "Favorites", exact: true }).click();
  await expect(payment).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1)).toBe(true);
  await page.screenshot({ path: testInfo.outputPath(`${item.name}-library-${width}.png`), fullPage: true });
});
