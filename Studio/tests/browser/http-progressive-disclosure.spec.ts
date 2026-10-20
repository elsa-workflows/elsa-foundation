import { mkdir } from "node:fs/promises";
import { resolve } from "node:path";
import { expect, test, type Page, type TestInfo } from "@playwright/test";
import axe from "axe-core";

type Theme = "light" | "dark";

async function openInspector(page: Page, { theme, width, seeded = false }: { theme: Theme; width: number; seeded?: boolean }) {
  await page.setViewportSize({ width, height: 768 });
  await page.route("**/connections/picker", route => route.fulfill({
    json: { items: [{ name: "customer-api", displayName: "Customer API", typeName: "http", status: "Ready" }] }
  }));
  await page.goto(`/?mode=http-progressive-disclosure&theme=${theme}${seeded ? "&seed=1" : ""}`);
  return page.getByRole("complementary", { name: "Activity inspector" });
}

async function assertAccessible(page: Page, testInfo: TestInfo, name: string) {
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (root: Document) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: unknown[] }> }> };
    }).axe.run(document);
    return result.violations;
  });
  await testInfo.attach(`${name}-full-page-axe.json`, {
    body: JSON.stringify(violations, null, 2),
    contentType: "application/json"
  });
  expect(violations).toEqual([]);
}

async function captureEvidence(page: Page, testInfo: TestInfo, name: string) {
  await page.screenshot({ path: testInfo.outputPath(`${name}.png`), fullPage: true, animations: "disabled" });
  const evidenceDirectory = process.env.HTTP_DISCLOSURE_EVIDENCE_DIR;
  if (evidenceDirectory) {
    await mkdir(evidenceDirectory, { recursive: true });
    await page.screenshot({ path: resolve(evidenceDirectory, `3064-${name}.png`), fullPage: true, animations: "disabled" });
  }
}

for (const theme of ["light", "dark"] as const) {
  test(`HTTP properties stay lossless and usable in ${theme} at laptop size`, async ({ page }, testInfo) => {
    const inspector = await openInspector(page, { theme, width: 1365, seeded: true });
    await expect(inspector.getByRole("heading", { name: "Send HTTP Request" })).toBeVisible();
    const directLabels = inspector.locator(".wf-properties > .wf-property-group .wf-property-row-header label");
    await expect(directLabels).toHaveText(["URL", "Method", "Content", "Connection"]);
    await expect(directLabels.nth(3)).toBeInViewport();
    await expect(inspector.getByRole("combobox", { name: "Connection", exact: true })).toBeInViewport();

    const disclosure = inspector.locator(".studio-advanced-section");
    const trigger = disclosure.locator(".studio-disclosure-fold-trigger");
    await expect(trigger).toHaveAttribute("aria-expanded", "true");

    const contentType = inspector.getByLabel("Content type", { exact: true });
    await expect(contentType).toHaveValue("string:text/plain");
    const requestHeaders = inspector.getByRole("textbox", { name: "Request headers expression" });
    await requestHeaders.fill("invalid");
    await expect(disclosure.getByText("Request headers expression is invalid.")).toBeVisible();
    await expect(trigger).toHaveAttribute("aria-expanded", "true");
    await requestHeaders.fill("headers()");
    await expect(disclosure.getByText("Request headers expression is invalid.")).toHaveCount(0);
    await expect(requestHeaders).toHaveValue("headers()");
    const configuredState = await inspector.getByTestId("authored-state").textContent();
    await trigger.click();
    await expect(trigger).toHaveAttribute("aria-expanded", "false");
    await expect(trigger).toContainText("set");
    await expect(contentType).toBeHidden();
    expect(await inspector.getByTestId("authored-state").textContent()).toBe(configuredState);
    await trigger.press("Enter");
    await expect(trigger).toHaveAttribute("aria-expanded", "true");
    await expect(contentType).toHaveValue("string:text/plain");
    await expect(requestHeaders).toHaveValue("headers()");

    const body = inspector.getByRole("textbox", { name: "Content", exact: true });
    await expect(body).toHaveValue("existing request body");
    const method = inspector.getByRole("combobox", { name: "Method", exact: true });
    for (const value of ["HEAD", "POST", "GET"]) {
      await method.selectOption(value);
      const state = JSON.parse((await inspector.getByTestId("authored-state").textContent())!);
      expect(state.Method.expression.value).toBe(value);
      expect(state.Content.expression.value).toBe("existing request body");
      expect(state.ContentType).toMatchObject({
        expression: { type: "Literal", value: "text/plain" },
        conversion: { mode: "profile", profile: { id: "body-conversion", version: "1" } },
        argumentExtras: { source: "foreign-template", future: { preserve: true } }
      });
      expect(state.RequestHeaders.expression.value).toBe("headers()");
    }

    await assertAccessible(page, testInfo, theme);
    await captureEvidence(page, testInfo, `http-disclosure-${theme}-1365x768`);
  });
}

for (const theme of ["light", "dark"] as const) {
  for (const width of [320, 768, 1024, 1440]) {
    test(`More options preserves keyboard access and authored state at ${width}px in ${theme}`, async ({ page }, testInfo) => {
      const inspector = await openInspector(page, { theme, width });
      const disclosure = inspector.locator(".studio-advanced-section");
      const trigger = disclosure.locator(".studio-disclosure-fold-trigger");
      const stateOutput = inspector.getByTestId("authored-state");
      const initialState = await stateOutput.textContent();
      await expect(trigger).toHaveAttribute("aria-expanded", "false");

      await trigger.focus();
      await page.keyboard.press("Enter");
      await expect(trigger).toHaveAttribute("aria-expanded", "true");
      await expect(disclosure.locator(".studio-disclosure-fold-body")).toHaveAttribute("id", (await trigger.getAttribute("aria-controls"))!);
      await expect(disclosure.getByRole("textbox", { name: "Request headers expression" })).toBeVisible();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
      expect(await inspector.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
      await assertAccessible(page, testInfo, `${theme}-${width}`);
      await captureEvidence(page, testInfo, `http-disclosure-${theme}-${width}x768`);
      await page.keyboard.press("Space");
      await expect(trigger).toHaveAttribute("aria-expanded", "false");
      expect(await stateOutput.textContent()).toBe(initialState);
      await expect(disclosure.locator(".wf-property-row")).toHaveCount(4);
    });
  }
}
