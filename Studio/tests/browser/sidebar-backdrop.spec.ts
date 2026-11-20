import { expect, test } from "@playwright/test";
import { builtInThemeDefinitions, cloneThemeDefinition } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";

const customTheme = {
  ...cloneThemeDefinition(builtInThemeDefinitions[0]),
  id: "custom-starfield",
  name: "Custom Starfield",
  source: "custom" as const,
  sidebarBackdrop: "starfield" as const
};

test("Space paints the desktop left navigation and remains visible in the collapsed icon rail", async ({ page }, info) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.addInitScript(() => localStorage.setItem("elsa-studio-sidebar-collapsed", "true"));
  await page.goto("/?mode=nav-placement&theme=space&appearance=dark");

  const sidebar = page.locator(".sidebar");
  await expect(page.locator("html")).toHaveAttribute("data-sidebar-backdrop", "starfield");
  await expect(page.locator(".studio-shell")).toHaveClass(/sidebar-collapsed/);
  await expect.poll(() => sidebar.evaluate(element => getComputedStyle(element).backgroundImage)).toContain("radial-gradient");
  await page.screenshot({ path: info.outputPath("space-sidebar-starfield.png"), animations: "disabled" });
});

test("Top navigation and the narrow strip do not receive the sidebar backdrop", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/?mode=nav-placement&theme=space&appearance=dark&navigation=top");
  await expect(page.locator("html")).not.toHaveAttribute("data-sidebar-backdrop");
  await expect.poll(() => page.locator(".sidebar").evaluate(element => getComputedStyle(element).backgroundImage)).toBe("none");

  await page.setViewportSize({ width: 1024, height: 844 });
  await page.goto("/?mode=nav-placement&theme=space&appearance=dark&navigation=left");
  const sidebar = page.locator(".sidebar");
  await expect(page.locator("html")).toHaveAttribute("data-sidebar-backdrop", "starfield");
  await expect.poll(() => sidebar.evaluate(element => getComputedStyle(element).backgroundImage)).toBe("none");
});

test("Theme Builder edits and previews a bounded sidebar backdrop", async ({ page }) => {
  await page.route("**/_elsa/theme-store", route => route.fulfill({ json: {
    themesMode: "AddToBuiltIns",
    themes: [customTheme],
    defaultThemeId: customTheme.id,
    assets: [],
    disabledBuiltInThemeIds: []
  } }));
  await page.goto("/theme-source-modes.html?view=builder");

  await page.locator(".theme-builder-list button").filter({ hasText: customTheme.name }).click();
  await page.getByRole("tab", { name: "Tokens" }).click();
  const backdrop = page.locator(".theme-token-field").filter({ hasText: "Sidebar backdrop" }).locator("select");
  await expect(backdrop).toHaveValue("starfield");
  await backdrop.selectOption("gradient");

  await page.getByRole("tab", { name: "Preview" }).click();
  await expect(page.locator(".theme-preview-surface nav")).toHaveAttribute("data-sidebar-backdrop", "gradient");
});