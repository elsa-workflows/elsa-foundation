import { expect, test } from "@playwright/test";

const connection = {
  tenantId: "demo", name: "customer-api", displayName: "Customer API", typeName: "http", authScheme: "bearer",
  settings: {}, secretFieldNames: ["token"], owner: "tenant", managedBy: "", status: "Ready",
  createdAt: "2026-10-18T12:00:00Z", tags: []
};
const descriptors = {
  types: [{ name: "http", displayName: "HTTP", supportedSchemes: ["bearer"], settings: [] }],
  schemes: [{ name: "bearer", displayName: "Bearer token", fields: [], hasInteractiveAuthorization: false }]
};

for (const width of [320, 768, 1024, 1440]) {
  test(`Connections list and dialogs remain usable at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.route("**/connections?*", route => route.fulfill({ json: { items: [connection] } }));
    await page.route("**/connections/descriptors", route => route.fulfill({ json: descriptors }));
    await page.goto("/?mode=connections");
    const table = page.getByRole("table", { name: "Connections", exact: true });
    await expect(table.getByRole("button", { name: "Customer API", exact: true })).toBeVisible();
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

    const opener = page.getByRole("button", { name: "Details for Customer API", exact: true });
    await opener.click();
    const details = page.getByRole("dialog", { name: "Customer API", exact: true });
    await expect(details).toBeVisible();
    await expect(details.locator("details")).not.toHaveAttribute("open");
    await details.getByRole("button", { name: "Close connection details", exact: true }).press("Shift+Tab");
    await expect(details.getByRole("button", { name: "Test", exact: true })).toBeFocused();
    await page.keyboard.press("Escape");
    await expect(details).toHaveCount(0);
    await expect(opener).toBeFocused();

    const createOpener = page.getByRole("button", { name: "New connection", exact: true });
    await createOpener.click();
    const create = page.getByRole("dialog", { name: "Create connection", exact: true });
    await expect(create.getByRole("textbox", { name: "Name", exact: true })).toBeFocused();
    const bounds = await create.boundingBox();
    expect(bounds!.x).toBeGreaterThanOrEqual(0);
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
    await page.keyboard.press("Escape");
    await expect(create).toHaveCount(0);
    await expect(createOpener).toBeFocused();
  });
}
