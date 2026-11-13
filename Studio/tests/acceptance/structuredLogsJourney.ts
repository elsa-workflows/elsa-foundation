import { expect, type Page, type TestInfo } from "@playwright/test";
import axe from "axe-core";

export async function structuredLogsJourney(page: Page, info: TestInfo, capture: (page: Page, info: TestInfo, name: string) => Promise<void>) {
  await page.goto("/diagnostics/structured-logs");
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"], { origin: new URL(page.url()).origin });
  const table = page.getByRole("table", { name: "Structured logs", exact: true });
  const row = table.getByRole("row", { name: /Select Information structured log/ }).first();
  await expect(row).toBeVisible();
  await page.getByRole("button", { name: "Pause", exact: true }).click();
  const autoscroll = page.getByRole("button", { name: "Autoscroll", exact: true });
  if (await autoscroll.getAttribute("aria-pressed") === "true") await autoscroll.click();
  await expect(table.getByRole("columnheader")).toHaveCount(6);
  await expect(row.getByRole("cell")).toHaveCount(6);
  await row.scrollIntoViewIfNeeded();
  await capture(page, info, "logs-page");

  const message = await row.getByRole("cell").nth(4).innerText();
  const copy = row.getByRole("button", { name: /Copy structured log message/ });
  await copy.focus();
  await copy.press("Enter");
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(message);
  await expect(page.getByRole("complementary", { name: "Structured log details", exact: true })).toHaveCount(0);
  await row.focus();
  await row.press("Space");
  await expect(page.getByRole("complementary", { name: "Structured log details", exact: true })).toBeVisible();
  await capture(page, info, "logs-selected");
  await page.getByRole("button", { name: "Close structured log details", exact: true }).click();

  await page.getByRole("textbox", { name: "Filter by category", exact: true }).fill("no-match-structured-log-acceptance");
  const empty = table.getByRole("row").filter({ hasText: "No structured logs received yet." });
  await expect(empty.getByRole("cell")).toHaveAttribute("aria-colspan", "6");
  await capture(page, info, "logs-empty");
  await page.addScriptTag({ content: axe.source });
  const violations = await table.evaluate(async element => {
    const result = await (window as unknown as Window & { axe: typeof axe }).axe.run(element, {
      runOnly: { type: "rule", values: ["aria-required-children", "aria-required-parent", "aria-valid-attr-value", "aria-prohibited-attr"] }
    });
    return result.violations.map(violation => violation.id);
  });
  expect(violations).toEqual([]);

  await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
  const dock = page.getByRole("region", { name: "Bottom panel", exact: true });
  await dock.getByRole("tab", { name: "Structured Logs", exact: true }).click();
  const dockTable = dock.getByRole("table", { name: "Structured logs", exact: true });
  await expect(dockTable.getByRole("row", { name: /Select Information structured log/ }).first()).toBeVisible();
  await expect(dockTable.getByRole("columnheader")).toHaveCount(6);
  await expect(dockTable.getByRole("row", { name: /Select Information structured log/ }).first().getByRole("cell")).toHaveCount(6);
  await capture(page, info, "logs-dock");
}
