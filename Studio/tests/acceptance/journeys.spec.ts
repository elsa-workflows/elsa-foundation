import { settingsTabsJourney } from "./settingsTabsJourney";
import { templateToastJourney } from "./templateToastJourney";
import { executableDesignerJourney } from "./executableDesignerJourney";
import { writeFileSync } from "node:fs";
import { expect, test, type Page, type TestInfo } from "@playwright/test";
import { userTasksJourney } from "./userTasksJourney";
import { decisionsJourney } from "./decisionsJourney";

async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

async function signIn(page: Page) {
  await page.goto("/workflows/definitions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin");
  await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
}

const browserErrors = new WeakMap<Page, string[]>();
const createdWorkflows = new WeakMap<Page, Array<{ url: string; authorization: string; id: string }>>();

test("user tasks inbox and response", async ({ page }, info) => userTasksJourney(page, info, capture));
test("settings tabs and preserved edits", async ({ page }, info) => settingsTabsJourney(page, info, capture));
test("business decisions authoring and explanations", async ({ page }, info) => decisionsJourney(page, info, capture));
