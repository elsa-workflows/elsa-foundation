import { expect, test, type Locator, type Page } from "@playwright/test";
import axe from "axe-core";

// Spec 250 M1, the browser layer: author a reservation workflow with a compensation scope from scratch in Studio, publish
// it, run it against the real Workbench, and inspect the recovery. Restart recovery is proven against a real process by
// Essentials/tests/Workbench/Tests/WorkbenchCompensationRecoveryTests.cs; this journey proves authoring and inspection.

test.skip(!process.env.ELSA_STUDIO_URL, "Set ELSA_STUDIO_URL to a running Studio connected to a Workbench.");

test("a compensation scope is authored, published, run and inspected in Studio", async ({ page }) => {
  await signIn(page);

  // Author.
  await page.goto("/workflows/definitions");
  await page.getByRole("button", { name: "Create", exact: true }).click();
  await page.getByLabel("Display name").fill(`Compensation journey ${Date.now()}`);
  await page.locator(".wf-dialog-actions button[type=submit]").click();
  await page.locator("button", { hasText: "Compensation Scope" }).first().click();
  await page.locator("button.wf-node-slot-badge", { hasText: "Steps" }).first().click();
  const designer = page.locator(".wf-compensation-designer");
  await expect(designer).toBeVisible();

  await pick(page, page.getByRole("button", { name: "Add first step" }), "Write Line");
  await fill(page, "Text", "Reserved inventory");
  await rename(page, 1, "Reserve inventory");
  await pick(page, page.getByRole("button", { name: "Add compensation for Reserve inventory" }), "Write Line");
  await fill(page, "Text", "Released inventory");

  await pick(page, page.getByRole("button", { name: "Add step", exact: true }), "Write Line");
  await fill(page, "Text", "Reserved delivery");
  await rename(page, 2, "Reserve delivery");
  await pick(page, page.getByRole("button", { name: "Add compensation for Reserve delivery" }), "Write Line");
  await fill(page, "Text", "Cancelled delivery");

  await pick(page, page.getByRole("button", { name: "Add step", exact: true }), "Fault");
  await rename(page, 3, "Confirm order");

  const rows = designer.getByRole("listitem");
  await expect(rows).toHaveCount(3);
  await expect(rows.nth(1)).toHaveAccessibleName(/^Step 2, Reserve delivery, compensated by /);
  await expect(rows.nth(2)).toHaveAccessibleName("Step 3, Confirm order, no compensation");
  expect(await seriousViolations(page, ".wf-compensation-designer")).toEqual([]);

  // Publish.
  await page.getByRole("button", { name: "Review and Publish" }).click();
  const review = page.getByRole("dialog").last();
  await review.getByRole("button", { name: /^Publish/ }).first().click();
  await expect(page.getByRole("dialog", { name: "Published" })).toBeVisible();
  await page.getByRole("dialog", { name: "Published" }).getByRole("button", { name: "Close" }).click();

  // Run, then open the run.
  await page.getByRole("button", { name: "Run", exact: true }).click();
  await page.getByRole("button", { name: "Run workflow" }).click();
  const runId = (await page.locator("text=RUN / INSTANCE").locator("xpath=following-sibling::*[1]").innerText()).trim();
  expect(runId).not.toEqual("");
  await page.goto(`/workflows/instances/${runId}`);
  await page.locator("button.wf-node-slot-badge", { hasText: "Steps" }).first().click();

  // Inspect: the order failed, the two reservations were undone newest first.
  await expect(designer.locator(".wf-compensation-phase")).toContainText("Compensated");
  await expect(designer.locator(".wf-compensation-cause")).toContainText("Recovery started because Confirm order failed");
  await expect(designer.getByRole("button", { name: "Recovery: Compensated (1st). Show this occurrence" })).toHaveCount(1);
  await expect(designer.getByRole("button", { name: "Recovery: Compensated (2nd). Show this occurrence" })).toHaveCount(1);
  await expect(designer.getByLabel("Recovery: Not applicable")).toHaveCount(1);
  expect(await seriousViolations(page, ".wf-compensation-designer")).toEqual([]);

  // The first handler that ran says which forward occurrence it compensates.
  await designer.getByRole("button", { name: "Recovery: Compensated (1st). Show this occurrence" }).click();
  await page.getByRole("tab", { name: "Activity", exact: true }).click();
  await expect(page.locator(".wf-activity-compensates")).toContainText("Compensates Reserve delivery (occurrence");
});

async function signIn(page: Page) {
  await page.goto("/");
  await page.locator("input[name=username], input[type=text]").first().fill(process.env.ELSA_STUDIO_USER ?? "admin");
  await page.locator("input[type=password]").first().fill(process.env.ELSA_STUDIO_PASSWORD ?? "Password123!");
  await page.locator("button[type=submit], button:has-text('Sign in')").first().click();
  await page.waitForLoadState("networkidle");
}

async function pick(page: Page, trigger: Locator, activity: string) {
  await trigger.click();
  const listbox = page.getByRole("listbox").last();
  await expect(listbox).toBeVisible();
  await page.keyboard.type(activity);
  await expect(listbox.getByRole("option").first()).toContainText(activity);
  await page.keyboard.press("Enter");
  await expect(listbox).toBeHidden();
}

async function fill(page: Page, label: string, value: string) {
  const box = page.getByRole("textbox", { name: label, exact: true });
  await box.fill(value);
  await box.press("Tab");
}

async function rename(page: Page, index: number, value: string) {
  const box = page.getByLabel(`Name of step ${index}`);
  await box.fill(value);
  await box.press("Tab");
  await expect(box).toHaveValue(value);
}

async function seriousViolations(page: Page, selector: string) {
  await page.addScriptTag({ content: axe.source });
  return page.evaluate(async scope => {
    const result = await (window as Window & {
      axe: { run: (root: Element, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string }> }> };
    }).axe.run(document.querySelector(scope)!, { resultTypes: ["violations"] });
    return result.violations
      .filter(violation => violation.impact === "critical" || violation.impact === "serious")
      .map(violation => `${violation.id}: ${violation.help}`);
  }, selector);
}
