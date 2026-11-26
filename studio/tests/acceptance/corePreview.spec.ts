import { writeFileSync } from "node:fs";
import { expect, test, type Page, type TestInfo } from "@playwright/test";
import { corePreviewSmokeJourney, type CorePreviewStack } from "./corePreviewSmokeJourney";

// The Core Preview journeys (tools/ux-acceptance/core_preview.py): a Workbench whose default shell serves its
// publications on the kernel, on a throwaway PostgreSQL store, with Studio and a local HTTP stub.
const backend = process.env.UX_BACKEND_URL;
const stub = process.env.UX_HTTP_STUB_URL;
if (process.env.UX_ACCEPTANCE_SUITE !== "core-preview" || !backend || !stub) {
  throw new Error("Run the Core Preview journeys through tools/ux-acceptance/core_preview.py.");
}
const stack: CorePreviewStack = { backend, stub, tenant: process.env.UX_CORE_PREVIEW_TENANT };

async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

async function receipt(info: TestInfo, name: string, value: unknown) {
  const path = info.outputPath(name);
  writeFileSync(path, JSON.stringify(value, null, 2) + "\n");
  await info.attach(name, { path, contentType: "application/json" });
}

const browserErrors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }) => {
  const errors: string[] = [];
  browserErrors.set(page, errors);
  page.on("pageerror", error => errors.push(error.message));
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin");
  await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
});
test.afterEach(async ({ page }) => {
  expect(browserErrors.get(page), "Uncaught browser errors").toEqual([]);
});

test("core preview stack is ready", async ({ page, request }, info) => corePreviewSmokeJourney(page, request, info, stack, capture, receipt));
