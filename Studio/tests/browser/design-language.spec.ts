import { expect, test } from "@playwright/test";

for (const width of [320, 1440]) {
  for (const theme of ["light", "dark"] as const) for (const workspace of ["flat", "linen"]) {
    test(`collections share typography and containment at ${width}px ${theme} ${workspace}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      await page.emulateMedia({ colorScheme: theme });
      await page.route("**/connections?*", route => route.fulfill({ json: { items: [{
        tenantId: "demo", name: "sample", displayName: "Sample connection", typeName: "http", authScheme: "bearer",
        settings: {}, secretFieldNames: [], owner: "tenant", managedBy: "", status: "Ready", createdAt: "2026-10-18T12:00:00Z", tags: []
      }] } }));
      await page.route("**/connections/descriptors", route => route.fulfill({ json: { types: [], schemes: [] } }));
      await page.route("**/samples/weather-forecast", route => route.fulfill({ json: [{ date: "2026-10-31", summary: "Sunny", temperatureC: 20, temperatureF: 68 }] }));
      const cases = [
        { name: "definitions", url: `/?mode=workflow-definition-browse&theme=${theme}` },
        { name: "runs", url: `/workflow-run-deletion.html?theme=${theme}` },
        { name: "secrets", url: "/secret-scopes.html?scenario=inventory" },
        { name: "connections", url: `/?mode=connections&theme=${theme}` },
        { name: "disabled weather sample", url: "/module-ui.html" }
      ];
      for (const item of cases) {
        await page.goto(item.url);
        const header = page.locator(".studio-page-header").first();
        await expect(header).toBeVisible();
        await page.evaluate(style => {
          if (style === "flat") delete document.documentElement.dataset.workspaceStyle;
          else document.documentElement.dataset.workspaceStyle = style;
        }, workspace);
        const table = page.getByRole("table").first();
        await expect(table.getByRole("columnheader").first()).toBeVisible();
        const typography = await header.getByRole("heading").evaluate(element => ({
          size: getComputedStyle(element).fontSize,
          margin: getComputedStyle(element).marginTop
        }));
        expect(typography, item.name).toEqual({ size: "20px", margin: "0px" });
        expect(await table.getByRole("columnheader").first().evaluate(element => ({
          size: getComputedStyle(element).fontSize,
          transform: getComputedStyle(element).textTransform
        })), item.name).toEqual({ size: "12px", transform: "none" });
        const boundary = page.locator(".studio-table-container").first();
        expect(await boundary.evaluate(element => getComputedStyle(element).overflowX), item.name).toBe("auto");
        const ariaTable = page.locator(".studio-collection-table").first();
        if (await ariaTable.count()) expect(await ariaTable.getByRole("row").nth(1).evaluate(element => getComputedStyle(element).minHeight), item.name).toBe("48px");
        const search = page.locator(".studio-collection-toolbar .studio-search").first();
        if (await search.count()) expect(await search.evaluate(element => element.getBoundingClientRect().height), item.name).toBe(32);
        expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), item.name).toBe(true);
        await expect(page.locator(".studio-collection-footer").first()).toBeVisible();
        await page.screenshot({ path: testInfo.outputPath(`${item.name.replaceAll(" ", "-")}.png`), fullPage: true });
      }
    });
  }
}

test("a disabled sample module uses public styles for failure, retry, populated and empty states", async ({ page }) => {
  let fail = true;
  let empty = false;
  await page.route("**/samples/weather-forecast", route => fail
    ? route.fulfill({ status: 503, body: "Unavailable" })
    : route.fulfill({ json: empty ? [] : [{ date: "2026-10-31", summary: "Sunny", temperatureC: 20, temperatureF: 68 }] }));
  await page.goto("/module-ui.html");
  await expect(page.getByRole("alert")).toContainText("Weather forecast could not be loaded");
  await expect(page.getByRole("heading", { name: "Weather forecast could not be loaded", level: 3 })).toBeVisible();
  await expect(page.locator(".studio-page").getByRole("table")).toHaveCount(0);
  fail = false;
  await page.getByRole("button", { name: "Retry", exact: true }).press("Enter");
  await expect(page.getByRole("cell", { name: "Sunny", exact: true })).toBeVisible();
  empty = true;
  await page.getByRole("button", { name: "Refresh", exact: true }).click();
  await expect(page.locator(".studio-page").getByRole("status")).toContainText("No forecast available");
  await expect(page.getByRole("heading", { name: "No forecast available", level: 3 })).toBeVisible();
  await expect(page.locator(".studio-page").getByRole("table")).toHaveCount(0);
  await expect(page.locator(".studio-collection-footer")).toHaveText("0 forecasts");
});


test("public identifier styles keep exact copying accessible without changing layout", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.setViewportSize({ width: 320, height: 800 });
  await page.route("**/samples/weather-forecast", route => route.fulfill({ json: [] }));
  await page.goto("/module-ui.html");
  const identifier = page.getByRole("region", { name: "Public identifier", exact: true }).locator(".studio-copyable-identifier");
  const before = await identifier.boundingBox();
  await page.getByRole("button", { name: "Copy Example ID" }).click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("module-example-0123456789abcdef0123456789abcdef");
  await expect(identifier.getByRole("status")).toHaveText("Example ID copied");
  expect(await identifier.getByRole("status").evaluate(element => ({ position: getComputedStyle(element).position, width: element.getBoundingClientRect().width }))).toEqual({ position: "absolute", width: 1 });
  expect((await identifier.boundingBox())?.height).toBe(before?.height);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test("nested copy controls keep native keyboard activation without selecting their row", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.route("**/samples/weather-forecast", route => route.fulfill({ json: [] }));
  await page.goto("/module-ui.html");
  const table = page.getByRole("region", { name: "Selectable public collection" }).getByRole("table");
  const row = table.getByRole("row").nth(2);
  const copy = row.getByRole("button", { name: "Copy Row ID" });
  for (const key of ["Enter", "Space"]) {
    await page.evaluate(() => navigator.clipboard.writeText(""));
    await copy.focus();
    await copy.press(key);
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("second-row");
    await expect(row).toHaveAttribute("aria-selected", "false");
  }
  await copy.click();
  await expect(row).toHaveAttribute("aria-selected", "false");
  await row.focus();
  await row.press("Enter");
  await expect(row).toHaveAttribute("aria-selected", "true");
  const firstRow = table.getByRole("row").nth(1);
  await firstRow.focus();
  await firstRow.press("Space");
  await expect(firstRow).toHaveAttribute("aria-selected", "true");
});

for (const width of [320, 1440]) {
  test(`Home header actions retain spacing when viewed and edited at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto("/?mode=home");
    const header = page.locator(".home-page > .studio-page-header");
    for (const editing of [false, true]) {
      if (editing) await header.getByRole("button", { name: "Edit home" }).click();
      const refresh = header.getByRole("button", { name: "Refresh all" });
      const next = header.getByRole("button", { name: editing ? "Add widget" : "Edit home", exact: true });
      const a = (await refresh.boundingBox())!;
      const b = (await next.boundingBox())!;
      const gap = Math.abs(a.y - b.y) < 1 ? b.x - a.x - a.width : b.y - a.y - a.height;
      expect(gap).toBeGreaterThanOrEqual(8);
      expect(await header.evaluate(element => element.getBoundingClientRect().right <= innerWidth)).toBe(true);
    }
  });
}

for (const url of ["/?mode=workflow-definition-browse", "/workflow-run-deletion.html"]) {
  test(`selected rows retain a visible highlight in ${url}`, async ({ page }) => {
    await page.goto(url);
    const row = page.locator(".studio-collection-table [role='row'][aria-selected]").first();
    await expect(row).toBeVisible();
    const before = await row.evaluate(element => getComputedStyle(element).backgroundColor);
    await row.getByRole("checkbox").check();
    await page.mouse.move(0, 0);
    await expect(row).toHaveAttribute("aria-selected", "true");
    expect(await row.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(before);
    expect(await row.evaluate(element => getComputedStyle(element).boxShadow)).not.toBe("none");
  });
}
