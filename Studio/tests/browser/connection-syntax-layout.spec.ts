import { expect, test, type Locator } from "@playwright/test";

const connection = {
  name: "customer-api", displayName: "Customer API", typeName: "http", status: "Ready"
};

for (const width of [320, 1440]) {
  test(`keeps the Connection syntax picker compact across metadata states at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    let releaseLoading = () => {};
    const loading = new Promise<void>(resolve => { releaseLoading = resolve; });
    let requests = 0;
    await page.route("**/connections/picker", async route => {
      requests++;
      if (requests === 1) {
        await loading;
        return route.fulfill({ json: { items: [] } });
      }
      if (requests === 2) return route.abort("failed");
      return route.fulfill({ json: { items: [connection, { ...connection, name: "pending-api", status: "NeedsAuthorization" }] } });
    });

    await page.goto("/?mode=connection-reference");
    const picker = page.locator(".connection-picker-editor");
    const syntax = page.getByRole("button", { name: "Connection expression syntax", exact: true });
    const select = picker.getByRole("combobox", { name: "Connection", exact: true });
    const feedback = picker.locator("p");
    await expect(feedback).toHaveText("Loading connections…");
    await expectReferenceLayout(syntax, picker);

    releaseLoading();
    await expect(select).toBeEnabled();
    await expect(select.getByRole("option")).toHaveCount(1);
    await expectReferenceLayout(syntax, picker);

    await page.reload();
    await expect(picker.getByRole("button", { name: "Retry", exact: true })).toBeVisible();
    await expectReferenceLayout(syntax, picker);

    await picker.getByRole("button", { name: "Retry", exact: true }).click();
    await expect(select).toBeEnabled();
    await select.selectOption("pending-api");
    await expect(feedback).toContainText("Needs authorization.");
    await expectReferenceLayout(syntax, picker);

    await select.selectOption("customer-api");
    await expect(select).toHaveValue("customer-api");
    await select.focus();
    await page.keyboard.press("Shift+Tab");
    await expect(syntax).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("listbox", { name: "Connection expression syntax" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(syntax).toBeFocused();
    await expectReferenceLayout(syntax, picker);
  });
}

async function expectReferenceLayout(syntax: Locator, picker: Locator) {
  const trigger = await syntax.boundingBox();
  const editor = await picker.boundingBox();
  expect(trigger).not.toBeNull();
  expect(editor).not.toBeNull();
  expect(trigger!.height).toBeLessThanOrEqual(40);
  expect(trigger!.y + trigger!.height).toBeLessThanOrEqual(editor!.y);
  expect(await syntax.evaluate(element => element.classList.contains("inline"))).toBe(false);
}
