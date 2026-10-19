import { expect, test } from "@playwright/test";

const emptyPickerResponse = {
  items: [],
  canCreateInline: false
};

const populatedPickerResponse = {
  items: [{
    id: "api-token",
    name: "api-token",
    displayName: "API token",
    typeName: "text",
    storeName: "encrypted",
    scope: "workflow",
    status: "Active",
    tags: [],
    createdAt: "2026-10-18T00:00:00Z"
  }],
  canCreateInline: false
};

for (const width of [320, 1440]) {
  test(`keeps the Secret syntax picker compact beside help text at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });

    let releaseFirstResponse = () => {};
    const firstResponseGate = new Promise<void>(resolve => { releaseFirstResponse = resolve; });
    let markFirstRequestStarted = () => {};
    const firstRequestStarted = new Promise<void>(resolve => { markFirstRequestStarted = resolve; });
    let requestCount = 0;
    await page.route("**/secrets/picker", async route => {
      requestCount++;
      if (requestCount === 1) {
        markFirstRequestStarted();
        await firstResponseGate;
        return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(emptyPickerResponse) });
      }
      if (requestCount === 2) return route.abort("failed");
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(populatedPickerResponse) });
    });

    await page.goto("/?mode=secret-reference");
    const syntax = page.getByRole("button", { name: "API token expression syntax", exact: false });
    const picker = page.locator(".secret-picker-editor");
    const status = picker.locator(".secret-picker-status");
    const metadata = picker.locator(".secret-picker-metadata");
    const select = page.getByLabel("Secret", { exact: true });

    await firstRequestStarted;
    await expect(status).toHaveText("Loading Secret metadata...");
    await expectPickerLayout(syntax, status, metadata);

    releaseFirstResponse();
    await expect(status).toHaveText("No active Secrets are available.");
    await expectPickerLayout(syntax, status, metadata);

    await chooseSyntax(page, "Literal");
    await expect(syntax).toHaveAttribute("title", "Literal");
    expect((await syntax.boundingBox())!.width).toBeLessThanOrEqual(40);
    await expect(select).toHaveCount(0);
    await chooseSyntax(page, "Secret");
    await expect(syntax).toHaveAttribute("title", "Secret");
    expect((await syntax.boundingBox())!.width).toBeLessThanOrEqual(40);
    await expect(page.getByRole("alert")).toContainText("Secret metadata is unavailable.");
    await expectPickerLayout(syntax, page.getByRole("alert"), metadata);

    await page.getByRole("button", { name: "Retry", exact: true }).click();
    await expect(select).toBeEnabled();
    await expect(metadata).toHaveText("Secret values are never displayed.");
    await expectPickerLayout(syntax, status, metadata);

    await select.selectOption("api-token");
    await expect(select).toHaveValue("api-token");
    await expect(metadata).toHaveText("API token · api-token · workflow · encrypted · text · Active");
    await expectPickerLayout(syntax, status, metadata);
  });
}

async function chooseSyntax(page: import("@playwright/test").Page, syntax: "Literal" | "Secret") {
  await page.getByRole("button", { name: "API token expression syntax", exact: false }).click();
  await page.getByRole("option", { name: syntax, exact: true }).click();
}

async function expectPickerLayout(
  syntax: import("@playwright/test").Locator,
  status: import("@playwright/test").Locator,
  metadata: import("@playwright/test").Locator
) {
  await expect(syntax).toBeVisible();
  const triggerBox = await syntax.boundingBox();
  expect(triggerBox).not.toBeNull();
  expect(triggerBox!.height).toBeLessThanOrEqual(40);
  expect(triggerBox!.width).toBeLessThanOrEqual(40);

  for (const help of [status, metadata]) {
    if (!await help.isVisible()) continue;
    const helpBox = await help.boundingBox();
    expect(helpBox).not.toBeNull();
    const overlaps = triggerBox!.x < helpBox!.x + helpBox!.width
      && triggerBox!.x + triggerBox!.width > helpBox!.x
      && triggerBox!.y < helpBox!.y + helpBox!.height
      && triggerBox!.y + triggerBox!.height > helpBox!.y;
    expect(overlaps, `syntax control overlaps picker help: ${JSON.stringify({ triggerBox, helpBox })}`).toBe(false);
  }
}
