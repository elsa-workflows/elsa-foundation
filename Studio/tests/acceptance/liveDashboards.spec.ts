import { writeFileSync } from "node:fs";
import { expect, test, type Page, type TestInfo } from "@playwright/test";
import { nativeDashboardEdgesJourney } from "./nativeDashboardEdgesJourney";
import { liveAcmeDashboardJourney, liveWorkflowDashboardJourney, restartWorkflowDashboardJourney, liveUnsupportedDashboardJourney } from "./liveDashboardsJourney";

async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

const errors = new WeakMap<Page, string[]>();
test.beforeEach(async ({ page }, info) => {
  if (process.env.UX_DASHBOARD_LIVE_OWNED !== "1") throw new Error("A real owned live profile is required; no skip or scripted fallback is allowed.");
  await page.addInitScript(() => {
    if (!localStorage.getItem("elsa-studio-theme-mode")) localStorage.setItem("elsa-studio-theme-mode", "light");
    localStorage.setItem("elsa-studio-workspace-style", "linen");
  });
  const observed: string[] = []; errors.set(page, observed);
  page.on("pageerror", error => observed.push(error.message));
  const source = info.outputPath("source.txt"); writeFileSync(source, process.env.UX_SOURCE_SHA ?? "unknown");
  await info.attach("source", { path: source, contentType: "text/plain" });
  await page.goto("/questions");
  const form = page.locator('form[action="/_elsa/identity/login"]');
  await form.getByLabel(/username/i).fill("admin");
  await form.getByLabel(/password/i).fill("Password123!");
  await form.getByRole("button", { name: /sign in/i }).click();
  await expect(page.getByRole("heading", { name: "Questions", level: 2, exact: true })).toBeVisible({ timeout: 60_000 });
  await capture(page, info, "live-reference");
});

test.afterEach(async ({ page }) => { expect(errors.get(page), "Uncaught browser errors").toEqual([]); });

if (process.env.UX_DASHBOARD_UNSUPPORTED === "1") {
  test("live unsupported Dashboard question preserves the request without fabrication", async ({ page }, info) => liveUnsupportedDashboardJourney(page, info, capture));
} else if (process.env.UX_DASHBOARD_NATIVE_EDGES === "1") {
  test("native Dashboard module repair and edge states", async ({ page }, info) => nativeDashboardEdgesJourney(page, info, capture));
} else if (process.env.UX_DASHBOARD_RESTART === "1") {
  test("live authored dashboard survives real host restart with provider disabled", async ({ page }, info) => restartWorkflowDashboardJourney(page, info, capture));
} else {
  test("live Workflow question clarification refinement and native refresh", async ({ page }, info) => liveWorkflowDashboardJourney(page, info, capture));
  test("live Acme business dimension clarification refinement and viewer results", async ({ page }, info) => liveAcmeDashboardJourney(page, info, capture));
}
