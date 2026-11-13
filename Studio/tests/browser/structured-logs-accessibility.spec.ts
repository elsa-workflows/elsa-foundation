import { expect, test } from "@playwright/test";
import axe from "axe-core";

for (const surface of ["logs", "logs-panel"]) {
  test(`structured ${surface} expose table cells and preserve keyboard row actions`, async ({ page, context }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.goto(`/minimal-studio.html?screen=${surface}`);
    const table = page.getByRole("table", { name: "Structured logs", exact: true });
    await expect(table).toBeVisible();
    await expect(table.getByRole("columnheader")).toHaveText(["Time", "Level", "Source", "Category", "Message", ""]);
    await expect(table.getByRole("columnheader").last()).toHaveAccessibleName("Actions");
    const records = table.getByRole("row", { name: /Select Information structured log/ });
    await expect(records.first()).toBeVisible();
    for (const record of await records.all()) {
      await expect(record.getByRole("cell")).toHaveCount(6);
    }

    await page.addScriptTag({ content: axe.source });
    const violations = await table.evaluate(async element => {
      const result = await (window as unknown as Window & { axe: typeof axe }).axe.run(element, {
        runOnly: { type: "rule", values: ["aria-required-children", "aria-required-parent", "aria-valid-attr-value", "aria-prohibited-attr"] }
      });
      return result.violations.map(violation => violation.id);
    });
    expect(violations).toEqual([]);

    const row = records.first();
    const actionCell = row.getByRole("cell").last();
    const alignment = await actionCell.evaluate(element => {
      const button = element.querySelector("button")!;
      return Math.abs(element.getBoundingClientRect().right - button.getBoundingClientRect().right);
    });
    expect(alignment, "the copy action remains aligned with the end of its column").toBeLessThanOrEqual(1);
    await expect(row.getByRole("cell").first().getByRole("time")).toBeVisible();
    const message = await row.getByRole("cell").nth(4).innerText();
    const copy = row.getByRole("button", { name: /Copy structured log message/ });
    for (const key of ["Enter", "Space"]) {
      await page.evaluate(() => navigator.clipboard.writeText(""));
      await copy.focus();
      await copy.press(key);
      await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(message);
      await expect(page.getByRole("complementary", { name: "Structured log details", exact: true })).toHaveCount(0);
    }

    if (surface === "logs") {
      for (const key of ["Enter", "Space"]) {
        await row.focus();
        await row.press(key);
        await expect(page.getByRole("complementary", { name: "Structured log details", exact: true })).toBeVisible();
        await page.getByRole("button", { name: "Close structured log details", exact: true }).click();
      }
    }
    await page.getByRole("button", { name: "Clear", exact: true }).click();
    const empty = table.getByRole("row").filter({ hasText: "No structured logs received yet." });
    await expect(empty.getByRole("cell")).toHaveAttribute("aria-colspan", "6");
    await expect(table.getByRole("columnheader")).toHaveCount(6);
  });
}
