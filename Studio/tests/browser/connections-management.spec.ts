import { expect, test } from "@playwright/test";
import axe from "axe-core";

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
    await expect.poll(() => page.evaluate(() =>
      document.documentElement.scrollWidth <= document.documentElement.clientWidth
    )).toBe(true);

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

    const createOpener = page.getByRole("button", { name: "Create connection", exact: true });
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

test("creating a Connection preserves the accepted search and leaves absent results unselected", async ({ page }) => {
  const listQueries: string[] = [];
  const created = { ...connection, name: "billing-api", displayName: "Billing API" };
  await page.route("**/connections?*", route => {
    const query = new URL(route.request().url()).searchParams.get("search") ?? "";
    listQueries.push(query);
    return route.fulfill({ json: { items: query === "customer" ? [connection] : [] } });
  });
  await page.route("**/connections/descriptors", route => route.fulfill({ json: descriptors }));
  await page.route("**/connections", route => route.request().method() === "POST"
    ? route.fulfill({ json: created })
    : route.continue());
  await page.goto("/?mode=connections");

  const search = page.getByRole("searchbox", { name: "Search connections", exact: true });
  await expect.poll(() => listQueries).toEqual([""]);
  await search.fill("customer");
  await page.getByRole("button", { name: "Search", exact: true }).click();
  await expect.poll(() => listQueries).toEqual(["", "customer"]);
  await expect(page.getByRole("button", { name: "Details for Customer API", exact: true })).toBeVisible();

  await search.fill("draft-only");
  await page.getByRole("button", { name: "Create connection", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  await dialog.getByLabel("Name", { exact: true }).fill(created.name);
  await dialog.getByRole("button", { name: "Create", exact: true }).click();

  await expect(dialog).toHaveCount(0);
  await expect(search).toHaveValue("customer");
  await expect.poll(() => listQueries).toEqual(["", "customer", "customer"]);
  await expect(page.getByRole("status")).toContainText("Connection 'billing-api' created.");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});


// Mirrors the built-in HTTP/API-key descriptor; the fixture renders the real page/dialog.
const apiKeyDescriptors = {
  types: [{ name: "http", displayName: "HTTP", supportedSchemes: ["api-key"], settings: [
    { name: "baseUrl", displayName: "Base URL", editorHint: "url", required: false },
    { name: "allowedHosts", displayName: "Allowed hosts", editorHint: "text", required: false },
    { name: "allowInsecureHttp", displayName: "Allow insecure HTTP", editorHint: "checkbox", required: false }
  ] }],
  schemes: [{ name: "api-key", displayName: "API key", fields: [
    { name: "headerName", displayName: "Header name", sensitive: false, editorHint: "text", required: false },
    { name: "placement", displayName: "Placement", sensitive: false, editorHint: "text", required: false },
    { name: "queryName", displayName: "Query name", sensitive: false, editorHint: "text", required: false },
    { name: "apiKey", displayName: "API key", sensitive: true, editorHint: "password", required: true }
  ], hasInteractiveAuthorization: false }]
};

for (const width of [320, 1440]) for (const theme of ["light", "dark"]) {
  test(`API-key creation leads with credentials and retains advanced corrections at ${width}px in ${theme}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    const requests: string[] = [];
    const creates: Array<Record<string, unknown>> = [];
    const secretQueries: unknown[] = [];
    let rejectCreate = true;
    page.on("request", request => {
      const path = new URL(request.url()).pathname;
      if (/^\/(connections|secrets)(\/|$)/.test(path)) requests.push(`${request.method()} ${path}`);
    });
    await page.route("**/connections?*", route => route.fulfill({ json: { items: [] } }));
    await page.route("**/connections/descriptors", route => route.fulfill({ json: apiKeyDescriptors }));
    await page.route("**/secrets/picker", route => {
      secretQueries.push(route.request().postDataJSON());
      return route.fulfill({ json: { items: [{ name: "customer-api", displayName: "Customer API key", typeName: "text", status: "active", scope: "demo" }] } });
    });
    await page.route("**/connections", route => {
      const draft = route.request().postDataJSON();
      creates.push(draft);
      return rejectCreate
        ? route.fulfill({ status: 400, json: { title: "Fixture connection create failed." } })
        : route.fulfill({ json: { ...connection, name: draft.name, authScheme: "api-key", settings: draft.settings } });
    });
    await page.goto(`/?mode=connections&theme=${theme}`);
    const opener = page.getByRole("button", { name: "Create connection", exact: true });
    await opener.click();
    const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
    const more = dialog.getByRole("button", { name: /^More options/ });
    const create = dialog.getByRole("button", { name: "Create", exact: true });
    await expect(dialog.getByLabel("Name", { exact: true })).toBeFocused();
    for (const selector of [".connections-create-header", ".connections-create-content", ".connections-create-actions"]) {
      const insets = await dialog.locator(selector).evaluate(element => {
        const style = getComputedStyle(element);
        return [parseFloat(style.paddingLeft), parseFloat(style.paddingRight)];
      });
      expect(insets.every(inset => inset >= 16)).toBe(true);
    }
    await expect(more).toHaveAttribute("aria-expanded", "false");
    const bodyId = await more.getAttribute("aria-controls");
    expect(bodyId).toBeTruthy();
    await expect(dialog.locator(`[id="${bodyId}"]`)).toBeHidden();
    const credential = dialog.getByRole("group", { name: "API key (required)", exact: true });
    await expect(credential).toBeVisible();
    const baseUrlId = await dialog.getByLabel("Base URL", { exact: true }).getAttribute("id");
    expect(await credential.evaluate((element, id) => Boolean(element.compareDocumentPosition(document.getElementById(id!)!) & Node.DOCUMENT_POSITION_FOLLOWING), baseUrlId)).toBe(true);
    expect(await credential.evaluate(element => Boolean(element.compareDocumentPosition(document.querySelector(".studio-advanced-section")!) & Node.DOCUMENT_POSITION_FOLLOWING))).toBe(true);
    await expect(dialog.getByText(/Base URL.*Allowed hosts.*More options|Allowed hosts.*More options.*Base URL/i)).toBeVisible();
    await dialog.getByLabel("Name", { exact: true }).fill("customer-api");
    await dialog.getByLabel("Existing Secret for API key", { exact: true }).selectOption("customer-api");
    await expect(create).toBeDisabled();
    await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
    await expect(create).toBeEnabled();
    await page.screenshot({ path: testInfo.outputPath("api-key-primary.png") });

    await more.focus();
    await page.keyboard.press("Space");
    await expect(more).toHaveAttribute("aria-expanded", "true");
    const header = dialog.getByLabel("Header name", { exact: true });
    const query = dialog.getByLabel("Query name", { exact: true });
    const placement = dialog.getByRole("combobox", { name: "Placement", exact: true });
    await expect(header).toBeVisible();
    await expect(query).toBeHidden();
    await header.fill("Authorization");
    await placement.selectOption("query");
    await expect(header).toBeHidden();
    await query.fill("api_key");
    await placement.selectOption("header");
    await expect(header).toHaveValue("Authorization");
    await expect(query).toBeHidden();
    const insecure = dialog.getByLabel("Allow insecure HTTP", { exact: true });
    await insecure.check();
    await insecure.uncheck();
    await more.focus();
    await page.keyboard.press("Enter");
    await expect(more).toHaveAttribute("aria-expanded", "false");
    await expect(more).toContainText("3 set");
    await expect(create).toBeInViewport();
    await create.click();
    const alert = dialog.getByRole("alert");
    await expect(alert).toContainText("Fixture connection create failed.");
    await expect(alert).toBeFocused();
    await expect(more).toHaveAttribute("aria-expanded", "true");
    await header.fill("X-API-Key");
    await expect(header).toBeFocused();
    await expect(more).toHaveAttribute("aria-expanded", "true");
    await expect(alert).toHaveCount(0);
    await expect(header).toHaveValue("X-API-Key");
    await more.click();
    await expect(more).toHaveAttribute("aria-expanded", "false");
    await more.press("Enter");
    await expect(more).toHaveAttribute("aria-expanded", "true");
    await expect(header).toHaveValue("X-API-Key");
    await placement.selectOption("query");
    await expect(query).toHaveValue("api_key");
    await placement.selectOption("header");
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const result = await window.axe.run(document.querySelector('[role="dialog"]')!);
      return result.violations.map(item => ({ id: item.id, targets: item.nodes.map(node => node.target) }));
    });
    expect(violations).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("api-key-corrected.png") });
    rejectCreate = false;
    await create.click();
    await expect(dialog).toHaveCount(0);
    await expect(opener).toBeFocused();
    expect(creates).toHaveLength(2);
    expect(creates[1]).toEqual({ name: "customer-api", typeName: "http", authScheme: "api-key", settings: {
      baseUrl: "https://api.example", headerName: "X-API-Key", queryName: "api_key", placement: "header"
    }, secretFields: { apiKey: { sourceKind: "secret", existingSecretName: "customer-api" } } });

    // The alternative address and New value path keep runtime defaults un-authored.
    await opener.click();
    await dialog.getByLabel("Name", { exact: true }).fill("new-api");
    await dialog.getByRole("radio", { name: "New value", exact: true }).check();
    await dialog.getByLabel("New value for API key", { exact: true }).fill("fixture-operator-value");
    await expect(create).toBeDisabled();
    await more.press("Enter");
    await dialog.getByLabel("Allowed hosts", { exact: true }).fill("api.example");
    await more.press("Space");
    await expect(more).toHaveAttribute("aria-expanded", "false");
    await expect(more).toContainText("1 set");
    await expect(create).toBeEnabled();
    await create.click();
    await expect(dialog).toHaveCount(0);
    expect(creates[2]).toEqual({ name: "new-api", typeName: "http", authScheme: "api-key", settings: { allowedHosts: "api.example" }, secretFields: { apiKey: { value: "fixture-operator-value" } } });
    expect(secretQueries.length).toBeGreaterThanOrEqual(2);
    for (const body of secretQueries) expect(body).toEqual({ search: "", typeNames: ["text"], storeNames: [], activeOnly: true });
    expect(requests.every(request => ["GET /connections", "GET /connections/descriptors", "POST /secrets/picker", "POST /connections"].includes(request))).toBe(true);
    await testInfo.attach("metadata-only-request-proof.json", { body: JSON.stringify({ requests, secretQueries }), contentType: "application/json" });
  });
}

test("API-key creation explains denied Secret metadata and recovers without reading stored values", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 900 });
  let denied = true;
  const secretRequests: string[] = [];
  page.on("request", request => {
    if (new URL(request.url()).pathname.startsWith("/secrets")) secretRequests.push(`${request.method()} ${new URL(request.url()).pathname}`);
  });
  await page.route("**/connections?*", route => route.fulfill({ json: { items: [] } }));
  await page.route("**/connections/descriptors", route => route.fulfill({ json: apiKeyDescriptors }));
  await page.route("**/secrets/picker", route => denied
    ? route.fulfill({ status: 403, json: { title: "Forbidden" } })
    : route.fulfill({ json: { items: [{ name: "customer-api", displayName: "Customer API key", typeName: "text", status: "active" }] } }));
  await page.goto("/?mode=connections&theme=light");
  const opener = page.getByRole("button", { name: "Create connection", exact: true });
  await opener.click();
  const dialog = page.getByRole("dialog", { name: "Create connection", exact: true });
  await expect(dialog.getByRole("alert")).toContainText("Access to existing Secrets is denied.");
  await expect(dialog.getByRole("alert")).toContainText("you can enter a new value instead.");
  await dialog.getByLabel("Name", { exact: true }).fill("denied-api");
  await dialog.getByLabel("Base URL", { exact: true }).fill("https://api.example");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeDisabled();
  await dialog.getByRole("radio", { name: "New value", exact: true }).check();
  await dialog.getByLabel("New value for API key", { exact: true }).fill("fixture-new-value");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeEnabled();
  await dialog.getByRole("radio", { name: "Existing Secret", exact: true }).check();
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeDisabled();
  denied = false;
  const retry = dialog.getByRole("button", { name: "Retry", exact: true });
  await retry.focus();
  await page.keyboard.press("Enter");
  await dialog.getByLabel("Existing Secret for API key", { exact: true }).selectOption("customer-api");
  await expect(dialog.getByRole("button", { name: "Create", exact: true })).toBeEnabled();
  expect(secretRequests).toEqual(["POST /secrets/picker", "POST /secrets/picker"]);
  await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(opener).toBeFocused();
});
