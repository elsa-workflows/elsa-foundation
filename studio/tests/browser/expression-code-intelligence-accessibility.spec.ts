import { expect, test } from "@playwright/test";
import axe from "axe-core";

test("expression editors have no critical or serious automated accessibility violations", async ({ page }) => {
  await page.goto("/?mode=expression-code-intelligence");
  const preview = page.getByRole("button", { name: /JavaScript expression\. Activate to edit\./ });
  await expect(preview).toBeVisible();
  await preview.click();
  await expect(page.locator(".studio-code-editor-rich-compact .cm-content")).toBeVisible();

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
