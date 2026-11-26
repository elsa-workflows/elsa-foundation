import { expect, test, type Locator } from "@playwright/test";
import axe from "axe-core";

const connection = {
  name: "customer-api", displayName: "Customer API", typeName: "http", status: "Ready"
};

for (const width of [320, 1440]) for (const theme of ["light", "dark"]) {
  test(`keeps the Connection syntax picker compact across metadata states at ${width}px in ${theme}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    let releaseLoading = () => {};
    const loading = new Promise<void>(resolve => { releaseLoading = resolve; });
    let releaseRecovery = () => {};
    const recovery = new Promise<void>(resolve => { releaseRecovery = resolve; });
    let state: "loading" | "empty" | "failed" | "recovering" | "ready" = "loading";
    const metadataRequests: string[] = [];
    page.on("request", request => {
      const path = new URL(request.url()).pathname;
      if (/^\/(connections|secrets)(\/|$)/.test(path)) metadataRequests.push(`${request.method()} ${path}`);
    });
    await page.route("**/connections/picker", async route => {
      if (state === "loading") {
        await loading;
        return route.fulfill({ json: { items: [] } });
      }
      if (state === "failed") return route.abort("failed");
      if (state === "empty") return route.fulfill({ json: { items: [] } });
      if (state === "recovering") await recovery;
      return route.fulfill({ json: { items: [connection, { ...connection, name: "pending-api", status: "NeedsAuthorization" }] } });
    });

    await page.goto(`/?mode=connection-reference&theme=${theme}`);
    const picker = page.locator(".connection-picker-editor");
    const syntax = page.getByRole("button", { name: "Connection expression syntax", exact: false });
    const select = picker.getByRole("combobox", { name: "Connection", exact: true });
    const feedback = picker.locator("p");
    await expect(feedback).toHaveText("Loading connections…");
    await expectReferenceLayout(syntax, picker);

    state = "empty";
    releaseLoading();
    await expect(select).toBeEnabled();
    await expect(select.getByRole("option")).toHaveCount(1);
    await expectReferenceLayout(syntax, picker);

    state = "failed";
    await page.reload();
    await expect(feedback).toContainText("Connections could not be loaded.");
    await expect(feedback).toHaveAttribute("role", "status");
    await expect(select).toHaveAccessibleDescription(/Connections could not be loaded\./);
    await expect(select).toBeDisabled();
    const retry = picker.getByRole("button", { name: "Retry", exact: true });
    await expect(retry).toBeVisible();
    await expectReferenceLayout(syntax, picker);
    expect(await picker.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async () => {
      const root = document.querySelector(".connection-picker-editor")!;
      const result = await window.axe.run(root);
      return result.violations.map(item => ({ id: item.id, targets: item.nodes.map(node => node.target) }));
    });
    expect(violations).toEqual([]);
    await page.screenshot({ path: testInfo.outputPath("picker-load-failed.png") });

    await syntax.focus();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: /^Connection conversion:/ })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(retry).toBeFocused();
    state = "recovering";
    await page.keyboard.press("Enter");
    await expect(feedback).toHaveText("Loading connections…");
    await expect(select).toBeDisabled();
    await expect(retry).toHaveCount(0);
    state = "ready";
    releaseRecovery();
    await expect(select).toBeEnabled();
    await select.selectOption("pending-api");
    await expect(feedback).toContainText("Needs authorization.");
    await expectReferenceLayout(syntax, picker);

    await select.selectOption("customer-api");
    await expect(select).toHaveValue("customer-api");
    await select.focus();
    await page.keyboard.press("Shift+Tab");
    await expect(page.getByRole("button", { name: /^Connection conversion:/ })).toBeFocused();
    await page.keyboard.press("Shift+Tab");
    await expect(syntax).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("listbox", { name: "Connection expression syntax" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(syntax).toBeFocused();
    await expectReferenceLayout(syntax, picker);
    expect(metadataRequests).toHaveLength(3);
    expect(metadataRequests.every(request => request === "POST /connections/picker")).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("picker-recovered.png") });
  });
}

async function expectReferenceLayout(syntax: Locator, picker: Locator) {
  const trigger = await syntax.boundingBox();
  const editor = await picker.boundingBox();
  expect(trigger).not.toBeNull();
  expect(editor).not.toBeNull();
  expect(trigger!.height).toBeLessThanOrEqual(40);
  expect(trigger!.width).toBeLessThanOrEqual(40);
  expect(trigger!.y + trigger!.height).toBeLessThanOrEqual(editor!.y);
  expect(await syntax.evaluate(element => element.classList.contains("inline"))).toBe(false);
}
