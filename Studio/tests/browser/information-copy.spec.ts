import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  for (const width of [390, 1440]) {
    test(`copies activity information exactly at ${width}px in ${theme}`, async ({ page, context }, testInfo) => {
      await context.grantPermissions(["clipboard-read", "clipboard-write"]);
      await page.setViewportSize({ width, height: 950 });
      await page.goto(`/?mode=activity-authoring&view=overview&long-identities=1&theme=${theme}`);
      await page.locator(".ad-technical-disclosure > summary").click();
      const facts = page.locator(".ad-identity-strip");
      await expect(facts).toBeVisible();
      for (const [label, value] of [["Activity Type Key", `elsa.user.quote.${"0123456789abcdef".repeat(8)}`], ["Definition identity", "quote-activity"], ["Authority key", "elsa.activity-design"]]) {
        const copy = facts.getByRole("button", { name: `Copy ${label}`, exact: true });
        await copy.focus();
        await page.keyboard.press("Enter");
        await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(value);
        await expect(copy.locator("..").getByRole("status")).toHaveText(`${label} copied`);
      }
      await expect(facts.getByRole("button", { name: "Copy Authority", exact: true })).toHaveCount(0);
      await expect(facts.getByRole("button", { name: "Copy Provider", exact: true })).toHaveCount(0);
      await expect(facts.getByRole("button", { name: "Copy Recommended version", exact: true })).toHaveCount(0);
      expect(await facts.evaluate(element => element.scrollWidth - element.clientWidth)).toBeLessThanOrEqual(1);
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
      await page.screenshot({ path: testInfo.outputPath("activity-information-copy.png") });
    });
  }
}

test("copies disclosed connection metadata without activating another action", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.route("**/connections?*", route => route.fulfill({ json: { items: [{
    tenantId: "demo", name: "customer-api", displayName: "Customer API", typeName: "http", authScheme: "bearer",
    settings: {}, secretFieldNames: ["token"], owner: "tenant", managedBy: "", status: "Ready", createdAt: "2026-10-18T12:00:00Z", tags: []
  }] } }));
  await page.route("**/connections/descriptors", route => route.fulfill({ json: {
    types: [{ name: "http", displayName: "HTTP", supportedSchemes: ["bearer"], settings: [] }],
    schemes: [{ name: "bearer", displayName: "Bearer token", fields: [], hasInteractiveAuthorization: false }]
  } }));
  await page.goto("/?mode=connections&theme=dark");
  await page.getByRole("button", { name: "Details for Customer API", exact: true }).click();
  const details = page.getByRole("dialog", { name: "Customer API", exact: true });
  await details.locator("details > summary").click();
  const copy = details.locator("details").getByRole("button", { name: "Copy Connection name", exact: true });
  const value = await copy.locator("..").locator(".studio-copyable-identifier__value").textContent();
  await copy.click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(value);
  await expect(details).toBeVisible();
});
