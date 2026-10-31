import { expect, test, type Page } from "@playwright/test";
import axe from "axe-core";

const presets = [{ name: "Porcelain", id: "builtin:porcelain", mode: "light" }, { name: "Atelier", id: "builtin:atelier", mode: "light" }, { name: "Graphite", id: "builtin:graphite", mode: "dark" }];
async function preset(page: Page, name = "Atelier", width = 1440) {
  await page.setViewportSize({ width, height: 960 });
  await page.goto("/minimal-studio.html?view=appearance&visual-study=1");
  await page.getByRole("radiogroup", { name: "Studio preset", exact: true }).getByRole("radio", { name, exact: true }).click();
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");
}
async function view(page: Page, name: string, extra = "") {
  await page.goto(`/minimal-studio.html?view=${name}&visual-study=1${extra}`);
}

test("Atelier keeps the account readable in top navigation and renders its small selected-nav accent", async ({ page }) => {
  await preset(page);
  expect(await page.locator('.nav-section a.active').first().evaluate(element => getComputedStyle(element, "::before").display)).toBe("block");
  await page.getByRole("radiogroup", { name: "Navigation", exact: true }).getByRole("radio", { name: "Top", exact: true }).click();
  const account = page.getByRole("button", { name: "Account: Administrator", exact: true });
  await expect(account.locator(".user-menu-name")).toBeVisible();
  await page.addScriptTag({ content: axe.source });
  const violations = await account.evaluate(async element => (window as Window & { axe: typeof axe }).axe.run(element, { runOnly: ["color-contrast"] }).then(result => result.violations.map(v => v.id)));
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
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => (window as Window & { axe: typeof axe }).axe.run(document.querySelector(".studio-shell")!, { runOnly: ["color-contrast", "button-name", "label"] }).then(result => result.violations.map(v => ({ id: v.id, nodes: v.nodes.map(n => n.failureSummary) }))));
  expect(violations).toEqual([]);
  await page.screenshot({ path: testInfo.outputPath(`${item.id}-definitions.png`), fullPage: true });
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
