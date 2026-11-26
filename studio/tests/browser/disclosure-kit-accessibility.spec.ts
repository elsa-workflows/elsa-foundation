import { expect, test } from "@playwright/test";
import axe from "axe-core";

test("disclosure kit fixture has no critical or serious automated accessibility violations", async ({ page }) => {
  await page.goto("/disclosure.html");
  await expect(page.getByRole("heading", { name: "Disclosure kit" })).toBeVisible();

  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (root: Document, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string; nodes: unknown[] }> }> };
    }).axe.run(document, { resultTypes: ["violations"] });
    return result.violations.filter(violation =>
      violation.impact === "critical" || violation.impact === "serious"
    );
  });

  await test.info().attach("axe-critical-serious.json", {
    body: JSON.stringify(violations, null, 2),
    contentType: "application/json"
  });
  expect(violations).toEqual([]);
});
