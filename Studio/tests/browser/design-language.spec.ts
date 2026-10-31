import { expect, test } from "@playwright/test";

for (const width of [320, 1440]) {
  for (const theme of ["light", "dark"] as const) {
    test(`collections share typography and containment at ${width}px ${theme}`, async ({ page }, testInfo) => {
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
        { name: "secrets", url: "/secret-scopes.html?scenario=inventory" },
        { name: "connections", url: `/?mode=connections&theme=${theme}` },
        { name: "disabled weather sample", url: "/module-ui.html" }
      ];
      for (const item of cases) {
        await page.goto(item.url);
        const header = page.locator(".studio-page-header").first();
        await expect(header).toBeVisible();
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
  await expect(page.getByRole("table")).toHaveCount(0);
  fail = false;
  await page.getByRole("button", { name: "Retry", exact: true }).press("Enter");
  await expect(page.getByRole("cell", { name: "Sunny", exact: true })).toBeVisible();
  empty = true;
  await page.getByRole("button", { name: "Refresh", exact: true }).click();
  await expect(page.getByRole("status")).toContainText("No forecast available");
  await expect(page.getByRole("table")).toHaveCount(0);
  await expect(page.locator(".studio-collection-footer")).toHaveText("0 forecasts");
});
