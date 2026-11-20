import { expect, test } from "@playwright/test";

test.use({ viewport: { width: 1440, height: 1000 } });

test("opens purpose groups and moves domain destinations into local views", async ({ page }) => {
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

for (const width of [360, 390]) for (const appearance of ["light", "dark"]) test(`top search exposes its full label and target at ${width}px in ${appearance}`, async ({ page }) => {
  await page.setViewportSize({ width, height: 900 });
  await page.goto(`/?mode=nav-placement&navigation=top&appearance=${appearance}`);
  const search = page.getByRole("searchbox", { name: "Search Studio", exact: true });
  await search.fill("templates");
  const results = page.getByRole("navigation", { name: "Search results", exact: true });
  const target = results.getByRole("link", { name: "Templates", exact: true });
  await expect(target).toBeInViewport({ ratio: 1 });
  await expect(target.locator(".nav-label")).toBeInViewport({ ratio: 1 });
  expect(await target.evaluate(node => {
    const label = document.createRange(); label.selectNodeContents(node.querySelector(".nav-label")!);
    const box = label.getBoundingClientRect();
    return box.left >= 0 && box.right <= innerWidth && node.contains(document.elementFromPoint((box.left + box.right) / 2, (box.top + box.bottom) / 2));
  })).toBe(true);
  await search.press("Tab");
  await expect(target).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(results).toBeHidden();
  await expect(search).toBeFocused();
  await search.fill("");
  await search.fill("templates");
  await target.click();
  await expect(page.getByRole("navigation", { name: "Library views" })).toBeVisible();
});


test("desktop group overlays keep layout stable and scroll nested destinations into view", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 480 });
  await page.goto("/?mode=nav-placement&navigation=top&menuOverflow=1");
  await page.evaluate(() => document.fonts.ready);
  const sidebar = page.locator(".sidebar");
  const content = page.getByText("Navigation composition fixture.", { exact: true });
  const before = { header: await sidebar.boundingBox(), content: await content.boundingBox() };
  const build = page.getByRole("navigation", { name: "Build", exact: true });
  await build.getByRole("button", { name: "Build", exact: true }).click();
  const panel = build.locator(".nav-group-panel");
  await expect(panel).toBeVisible();
  const after = { header: await sidebar.boundingBox(), content: await content.boundingBox() };
  expect(Math.abs(after.header!.height - before.header!.height), "The opened overlay must not grow the header").toBeLessThanOrEqual(1);
  expect(Math.abs(after.content!.y - before.content!.y), "Opening the overlay must not move page content").toBeLessThanOrEqual(1);
  const bounds = await panel.boundingBox();
  expect(bounds!.height).toBeLessThanOrEqual(370);
  expect(bounds!.y).toBeGreaterThanOrEqual(0);
  expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(480);
  expect(await panel.evaluate(node => node.scrollHeight > node.clientHeight)).toBe(true);
  const last = panel.getByRole("link", { name: "Overflow destination 24", exact: true });
  const beforeScroll = await panel.evaluate(node => node.scrollTop);
  await last.scrollIntoViewIfNeeded();
  await expect(last).toBeInViewport({ ratio: 1 });
  expect(await panel.evaluate(node => node.scrollTop)).toBeGreaterThan(beforeScroll);
  expect(await last.evaluate(node => {
    const rect = node.getBoundingClientRect();
    const panel = node.closest(".nav-group-panel")!.getBoundingClientRect();
    return rect.left >= panel.left && rect.right <= panel.right && rect.top >= panel.top && rect.bottom <= panel.bottom &&
      node.contains(document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2));
  }), "The last nested destination must be inside the scrolled panel and unobscured").toBe(true);
  await last.click();
  await expect(page.getByRole("main").getByRole("heading", { name: "Overflow destination 24", exact: true })).toBeVisible();
  await expect(build.getByRole("button", { name: "Build", exact: true })).toHaveAttribute("aria-expanded", "false");
});

for (const appearance of ["light", "dark"]) for (const scenario of ["no-match Tab", "matching Shift+Tab"]) {
  test(`top search dismisses ${scenario} outside the input/results region in ${appearance}`, async ({ page }) => {
    await page.goto(`/?mode=nav-placement&navigation=top&appearance=${appearance}`);
    const search = page.getByRole("searchbox", { name: "Search Studio", exact: true });
    const query = scenario === "no-match Tab" ? "absent-destination-keyboard" : "templates";
    const results = page.getByRole("navigation", { name: "Search results", exact: true });
    await search.fill(query);
    await expect(results).toBeVisible();
    await search.press(scenario === "no-match Tab" ? "Tab" : "Shift+Tab");
    const outside = await page.evaluateHandle(() => document.activeElement);
    expect(await outside.evaluate(node => node instanceof HTMLElement && node !== document.querySelector('input[aria-label="Search Studio"]') && !node.closest(".nav-search-results"))).toBe(true);
    await expect(results).toBeHidden();
    await expect(search).toHaveValue(query);
    await page.keyboard.press("Escape");
    expect(await outside.evaluate(node => node === document.activeElement)).toBe(true);
    await outside.dispose();
    await search.fill(query + "-changed");
    await expect(results).toBeVisible();
    await search.press("Escape");
    await expect(results).toBeHidden();
    await expect(search).toBeFocused();
    await search.fill("templates");
    await search.press("Tab");
    await expect(results.getByRole("link", { name: "Templates", exact: true })).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect(search).toBeFocused();
    await expect(results).toBeVisible();
  });
}

for (const appearance of ["light", "dark"] as const) for (const coarse of [false, true]) {
  test(`actual ETL nested destinations preserve ${coarse ? "coarse 44px" : "fine 30px"} sizing in desktop ${appearance}`, async ({ browser, baseURL }) => {
    const context = await browser.newContext({ baseURL, viewport: { width: 1440, height: 900 }, colorScheme: appearance, isMobile: false, hasTouch: coarse });
    try {
      const page = await context.newPage();
      await page.addInitScript(() => {
        (window as Window & { lastNavPointer?: string }).lastNavPointer = "";
        window.addEventListener("pointerdown", event => { (window as Window & { lastNavPointer?: string }).lastNavPointer = event.pointerType; }, true);
      });
      await page.goto("/?mode=nav-placement&navigation=top&etlNavigation=1");
      expect(await page.evaluate(() => ({ coarse: matchMedia("(pointer: coarse)").matches, fine: matchMedia("(pointer: fine)").matches, points: navigator.maxTouchPoints })))
        .toEqual({ coarse, fine: !coarse, points: coarse ? 1 : 0 });
      await page.evaluate(() => document.fonts.ready);
      const build = page.getByRole("navigation", { name: "Build", exact: true });
      const trigger = build.getByRole("button", { name: "Build", exact: true });
      for (const name of ["Connector definitions", "Runs"]) {
        await trigger.click();
        const target = build.getByRole("link", { name, exact: true });
        await expect(target).toHaveClass(/nav-child/);
        await target.scrollIntoViewIfNeeded();
        const geometry = await target.evaluate(node => {
          const box = node.getBoundingClientRect();
          const label = document.createRange(); label.selectNodeContents(node.querySelector(".nav-label") ?? node);
          const rects = Array.from(label.getClientRects());
          const panel = node.closest(".nav-group-panel")!.getBoundingClientRect();
          return { height: box.height, minimum: parseFloat(getComputedStyle(node).minHeight),
            visible: rects.length > 0 && rects.every(rect => rect.left >= 0 && rect.right <= innerWidth && rect.top >= panel.top && rect.bottom <= panel.bottom &&
              node.contains(document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2))) };
        });
        expect(geometry.minimum).toBe(coarse ? 44 : 30);
        if (coarse) expect(geometry.height).toBeGreaterThanOrEqual(44);
        else expect(geometry.height).toBe(30);
        expect(geometry.visible).toBe(true);
        if (coarse) await target.tap(); else await target.click();
        expect(await page.evaluate(() => (window as Window & { lastNavPointer?: string }).lastNavPointer)).toBe(coarse ? "touch" : "mouse");
        await expect(page.getByRole("main").getByRole("heading", { name, exact: true })).toBeVisible();
        await expect(trigger).toHaveAttribute("aria-expanded", "false");
      }
    } finally { await context.close(); }
  });
}
