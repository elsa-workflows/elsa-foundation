import { expect, test, type Locator } from "@playwright/test";
import { builtInThemeDefinitions, cloneThemeDefinition } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";

const customTheme = {
  ...cloneThemeDefinition(builtInThemeDefinitions[0]),
  id: "custom-starfield",
  name: "Custom Starfield",
  source: "custom" as const,
  sidebarBackdrop: "starfield" as const
};

async function expectStarfieldPaint(element: Locator) {
  const paint = await element.evaluate(node => {
    const style = getComputedStyle(node);
    return { image: style.backgroundImage, size: style.backgroundSize };
  });
  expect(paint.image.match(/radial-gradient/g)?.length).toBeGreaterThanOrEqual(8);
  expect(paint.image).toContain("repeating-linear-gradient");
  expect(paint.image).not.toContain("url(");
  expect(paint.size).toContain("40px 40px");
}

test("Space paints the desktop left navigation and remains visible in the collapsed icon rail", async ({ page }, info) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.addInitScript(() => localStorage.setItem("elsa-studio-sidebar-collapsed", "true"));
  await page.goto("/?mode=nav-placement&theme=space&appearance=dark");

  const sidebar = page.locator(".sidebar");
  await expect(page.locator("html")).toHaveAttribute("data-sidebar-backdrop", "starfield");
  await expect(page.locator(".studio-shell")).toHaveClass(/sidebar-collapsed/);
  await expectStarfieldPaint(sidebar);
  await page.screenshot({ path: info.outputPath("space-sidebar-starfield.png"), animations: "disabled" });
});

test("Space gives the expanded Home sidebar a subtle starfield and keeps a light pinned sidebar readable", async ({ page }, info) => {
  await page.setViewportSize({ width: 1600, height: 1600 });
  await page.goto("/?mode=home&theme=space&appearance=dark");

  const sidebar = page.locator(".sidebar");
  await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
  await expect(page.getByText("Recent runs", { exact: true })).toBeVisible();
  await expect(page.locator("html")).toHaveAttribute("data-sidebar-backdrop", "starfield");
  await expectStarfieldPaint(sidebar);
  await page.screenshot({ path: info.outputPath("space-home-starfield.png"), fullPage: true, animations: "disabled" });

  await page.getByRole("button", { name: "Collapse navigation" }).click();
  await expect(page.locator(".studio-shell")).toHaveClass(/sidebar-collapsed/);
  await page.screenshot({ path: info.outputPath("space-home-starfield-collapsed.png"), fullPage: true, animations: "disabled" });

  await page.evaluate(() => localStorage.removeItem("elsa-studio-sidebar-collapsed"));
  await page.addInitScript(() => localStorage.setItem("elsa-studio-sidebar-color", "light"));
  await page.goto("/?mode=home&theme=space&appearance=dark");
  await expect(page.locator("html")).toHaveAttribute("data-sidebar-color", "light");
  await expectStarfieldPaint(sidebar);
  await expect.poll(() => page.evaluate(() => {
    const sidebarElement = document.querySelector(".sidebar");
    if (!sidebarElement) return false;
    const expectedColor = getComputedStyle(document.documentElement).getPropertyValue("--studio-nav-bg");
    const probe = document.createElement("div");
    probe.style.backgroundColor = expectedColor;
    document.body.append(probe);
    const result = getComputedStyle(sidebarElement).backgroundColor === getComputedStyle(probe).backgroundColor;
    probe.remove();
    return result;
  })).toBe(true);
});

for (const appearance of ["light", "dim"] as const) {
  test(`Space keeps the Home sidebar starfield subtle in ${appearance} mode`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1600, height: 1600 });
    await page.goto(`/?mode=home&theme=space&appearance=${appearance}`);

    const sidebar = page.locator(".sidebar");
    await expect(page.getByRole("heading", { level: 2, name: "Home" })).toBeVisible();
    await expectStarfieldPaint(sidebar);
    await page.screenshot({ path: info.outputPath(`space-home-${appearance}-starfield.png`), fullPage: true, animations: "disabled" });
  });
}

test("Appearance offers Space as a complete preset with the subtle starfield preview", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/theme-source-modes.html?view=appearance");

  const presets = page.getByRole("radiogroup", { name: "Studio preset" });
  await presets.getByRole("radio", { name: "Porcelain" }).click();
  const spacePreset = presets.getByRole("radio", { name: "Space" });
  await expect(spacePreset).toBeVisible();
  await expectStarfieldPaint(spacePreset.locator(".settings-preset-nav"));
  await spacePreset.click();

  await expect(page.locator("html")).toHaveAttribute("data-theme", "space");
  await expect(page.locator("html")).toHaveAttribute("data-color-scheme", "space");
  await expect(page.locator("html")).toHaveAttribute("data-workspace-style", "minimal");
  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "light");
  await expect.poll(() => page.evaluate(() => [
    localStorage.getItem("elsa-studio-theme"),
    localStorage.getItem("elsa-studio-workspace-style"),
    localStorage.getItem("elsa-studio-theme-mode")
  ])).toEqual(["space", "minimal", "light"]);

  await page.goto("/?mode=home&theme=space&appearance=light");
  const sidebar = page.locator(".sidebar");
  await expect(page.locator("html")).toHaveAttribute("data-sidebar-backdrop", "starfield");
  await expectStarfieldPaint(sidebar);
});

test("Space keeps the Home sidebar backdrop off in High contrast", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/?mode=home&theme=space&appearance=high-contrast");

  await expect(page.locator("html")).toHaveAttribute("data-theme-appearance", "high-contrast");
  await expect(page.locator("html")).not.toHaveAttribute("data-sidebar-backdrop");
  await expect.poll(() => page.locator(".sidebar").evaluate(element => getComputedStyle(element).backgroundImage)).toBe("none");
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
  let preview = page.locator(".theme-preview-surface nav");
  await expect(preview).toHaveAttribute("data-sidebar-backdrop", "gradient");

  await page.getByRole("tab", { name: "Tokens" }).click();
  await backdrop.selectOption("starfield");
  await page.getByRole("tab", { name: "Preview" }).click();
  preview = page.locator(".theme-preview-surface nav");
  await expect(preview).toHaveAttribute("data-sidebar-backdrop", "starfield");
  await expectStarfieldPaint(preview);
});