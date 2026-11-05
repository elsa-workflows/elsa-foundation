import { writeFileSync } from "node:fs";
import { expect, type Page, type TestInfo } from "@playwright/test";

export async function capture(page: Page, info: TestInfo, name: string) {
  await page.waitForFunction(() => document.fonts.status === "loaded", undefined, { timeout: 10_000 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const screenshot = info.outputPath(`${name}.png`);
  // Native screenshot handling finishes finite animations without waiting on paused panels.
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled", timeout: 10_000 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}
