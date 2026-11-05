import { expect, test } from "@playwright/test";

test.use({ viewport: { width: 1440, height: 1000 } });

test("opens purpose groups and moves specialist destinations into local views", async ({ page }) => {
  await page.goto("/?mode=nav-placement");
  for (const name of ["Build", "Operate", "Integrations"]) await expect(page.getByRole("button", { name, exact: true })).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("link", { name: "User tasks", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "More tools" })).toHaveCount(0);
  const sidebar = page.locator(".sidebar");
  for (const name of ["Activity definitions", "Templates", "Executables", "Console", "Runtime evidence settings", "Activity availability"]) await expect(sidebar.getByRole("link", { name, exact: true })).toHaveCount(0);
  await sidebar.getByRole("link", { name: "Library", exact: true }).click();
  await page.getByRole("navigation", { name: "Library views" }).getByRole("link", { name: "Templates", exact: true }).click();
  await expect(sidebar.getByRole("link", { name: "Library", exact: true })).toHaveAttribute("aria-current", "page");
  await sidebar.getByRole("link", { name: "Workflows", exact: true }).click();
  await page.getByRole("navigation", { name: "Workflows views" }).getByRole("link", { name: "Executables", exact: true }).click();
  await expect(sidebar.getByRole("link", { name: "Workflows", exact: true })).toHaveAttribute("aria-current", "page");
  await sidebar.getByRole("link", { name: "Diagnostics", exact: true }).click();
  await page.getByRole("navigation", { name: "Diagnostics views" }).getByRole("link", { name: "Structured logs", exact: true }).click();
  await expect(sidebar.getByRole("link", { name: "Diagnostics", exact: true })).toHaveAttribute("aria-current", "page");
});

test("remembers collapse choices and reveals matching local views through search", async ({ page }) => {
  await page.goto("/?mode=nav-placement");
  await page.getByRole("button", { name: "Build", exact: true }).click();
  await expect(page.getByRole("link", { name: "Agents", exact: true })).toBeHidden();
  await page.reload();
  await expect(page.getByRole("button", { name: "Build", exact: true })).toHaveAttribute("aria-expanded", "false");
  const search = page.getByRole("searchbox", { name: "Search Studio", exact: true });
  await search.fill("Templates");
  await expect(page.locator(".sidebar").getByRole("link", { name: "Templates", exact: true })).toBeVisible();
  await search.fill("runtime evidence");
  await expect(page.locator(".sidebar").getByRole("link", { name: "Runtime evidence settings", exact: true })).toBeVisible();
  await search.fill("no-such-destination");
  await expect(page.getByRole("status")).toContainText("No destinations match");
  await search.press("Escape");
  await expect(search).toHaveValue("");
});

for (const width of [390, 768, 1024]) test(`keeps navigation compact and keyboard reachable at ${width}px`, async ({ page }) => {
  await page.setViewportSize({ width, height: 844 });
  await page.goto("/?mode=nav-placement");
  const before = await page.locator(".sidebar").boundingBox();
  const trigger = page.getByRole("button", { name: "Build", exact: true });
  await trigger.click();
  await trigger.click();
  await trigger.focus();
  await page.keyboard.press("Tab");
  await expect(page.getByRole("link", { name: "Workflows", exact: true })).toBeFocused();
  const after = await page.locator(".sidebar").boundingBox();
  expect(Math.abs(after!.height - before!.height)).toBeLessThanOrEqual(1);
  expect(after!.height).toBeLessThan(110);
  const overlaps = await page.locator(".sidebar .nav-section a, .sidebar .nav-group-toggle").evaluateAll(nodes => {
    const bounds = nodes.map(node => ({ label: node.textContent?.trim(), box: node.getBoundingClientRect() })).filter(item => item.box.width > 0 && item.box.height > 0).sort((a, b) => a.box.left - b.box.left);
    return bounds.slice(1).flatMap((item, index) => item.box.left < bounds[index].box.right - 1 ? [`${bounds[index].label} overlaps ${item.label}`] : []);
  });
  expect(overlaps).toEqual([]);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

for (const theme of ["classic", "drift", "atelier", "schematic"]) for (const appearance of ["light", "dark"]) test(`${theme} ${appearance} keeps group labels and spacing readable`, async ({ page }, info) => {
  await page.goto(`/?mode=nav-placement&theme=${theme}&appearance=${appearance}`);
  const sidebar = page.locator(".sidebar");
  await expect(page.locator("html")).toHaveAttribute("data-theme", theme);
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", appearance);
  await page.evaluate(() => document.fonts.ready);
  await expect(sidebar.getByRole("link", { name: "Connections", exact: true })).toBeVisible();
  const groups = await sidebar.getByRole("button", { name: "Build", exact: true }).boundingBox();
  const item = await sidebar.getByRole("link", { name: "Workflows", exact: true }).boundingBox();
  expect(groups!.height).toBeGreaterThanOrEqual(32);
  expect(item!.height).toBeGreaterThanOrEqual(40);
  await page.screenshot({ path: info.outputPath(`${theme}-${appearance}.png`), animations: "disabled" });
});

test("top search uses one results surface and Escape dismisses it with focus restored", async ({ page }) => {
  await page.goto("/?mode=nav-placement&navigation=top");
  const search = page.getByRole("searchbox", { name: "Search Studio", exact: true });
  await search.fill("e");
  const results = page.getByRole("navigation", { name: "Search results", exact: true });
  await expect(results).toBeVisible();
  await expect(page.locator('.nav-group-panel:not([hidden])')).toHaveCount(0);
  await expect(results.getByRole("link", { name: "Templates", exact: true })).toBeVisible();
  await search.press("Tab");
  await expect(results.locator("a").first()).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(results).toBeHidden();
  await expect(search).toBeFocused();
  await search.fill("templates");
  await results.getByRole("link", { name: "Templates", exact: true }).click();
  await expect(search).toHaveValue("");
  await expect(page.getByRole("navigation", { name: "Library views" })).toBeVisible();
});

test("a saved desktop rail preference does not lock narrow disclosure controls open", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.addInitScript(() => localStorage.setItem("elsa-studio-sidebar-collapsed", "true"));
  await page.goto("/?mode=nav-placement");
  const build = page.getByRole("button", { name: "Build", exact: true });
  await build.click();
  await expect(build).toHaveAttribute("aria-expanded", "false");
  await expect(page.getByRole("link", { name: "Agents", exact: true })).toBeHidden();
  await build.press("Enter");
  await expect(page.getByRole("link", { name: "Agents", exact: true })).toBeVisible();
});

test("top navigation finds Settings and closes a same-page group selection", async ({ page }) => {
  await page.goto("/?mode=nav-placement&navigation=top");
  const integrations = page.getByRole("button", { name: "Integrations", exact: true });
  await integrations.click();
  await page.getByRole("navigation", { name: "Integrations", exact: true }).getByRole("link", { name: "Connections", exact: true }).click();
  await expect(integrations).toHaveAttribute("aria-expanded", "false");
  const search = page.getByRole("searchbox", { name: "Search Studio", exact: true });
  await search.fill("settings");
  const results = page.getByRole("navigation", { name: "Search results", exact: true });
  await expect(results.getByRole("link", { name: "Settings", exact: true })).toBeVisible();
  await results.getByRole("link", { name: "Settings", exact: true }).click();
  await expect(search).toHaveValue("");
});
