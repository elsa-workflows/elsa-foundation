import { expect, test } from "@playwright/test";
import axe from "axe-core";

test("bulk deletion confirms exact runs, retains failed selection and retries", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html");
  await page.getByRole("checkbox", { name: "Select all workflow runs on this page" }).check();
  await expect(page.getByText("3 workflow runs selected", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  const confirmation = page.getByRole("dialog");
  await expect(confirmation).toContainText("quote-active");
  await expect(confirmation).toContainText("Active runs will be stopped");
  await confirmation.getByRole("button", { name: "Delete", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("1 workflow run could not be deleted");
  await expect(page.getByRole("checkbox", { name: "Select workflow run quote-retry", exact: true })).toBeChecked();
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-completed · quote-definition", exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: "Delete selected", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete", exact: true }).click();
  await expect(page.getByText("No workflow runs yet", { exact: true })).toBeVisible();
});

test("cancel sends no deletion and unsupported runtimes have no destructive controls", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html");
  await page.getByRole("button", { name: "Actions for workflow run quote-completed", exact: true }).click();
  await page.getByRole("menuitem", { name: "Delete", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Cancel", exact: true }).click();
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-completed · quote-definition", exact: true })).toBeVisible();
  await page.goto("/workflow-run-deletion.html?delete=disabled");
  await expect(page.getByRole("button", { name: "Inspect workflow run quote-active · quote-definition", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Delete selected", exact: true })).toHaveCount(0);
  await expect(page.getByRole("checkbox")).toHaveCount(0);
});

test("run actions keep deletion in a danger menu and disable it while pending", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html?delete=delayed");
  const actions = page.getByRole("button", { name: "Actions for workflow run quote-completed", exact: true });
  await actions.click();
  const remove = page.getByRole("menuitem", { name: "Delete", exact: true });
  await expect(remove).toHaveAttribute("data-destructive", "true");
  await remove.click();
  await page.getByRole("dialog").getByRole("button", { name: "Delete", exact: true }).click();
  await actions.click();
  await expect(remove).toBeDisabled();
  await expect.poll(() => page.evaluate(() =>
    (window as Window & { workflowRunDeletionRequests?: string[] }).workflowRunDeletionRequests?.length ?? 0
  )).toBe(1);
  await expect(page.locator("output")).toHaveCount(0);
});

for (const theme of ["light", "dark"]) for (const width of [390, 1440]) {
  test(`compact Runs keep exact identity, health and actions discoverable (${theme}, ${width})`, async ({ page, context }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/workflow-run-deletion.html?clean-list&theme=${theme}`);
    const row = page.getByRole("row", { name: /^Inspect workflow run quote-completed · Unified workspace/ });
    await expect(row).toBeVisible();
    const bounds = (await row.boundingBox())!;
    expect(bounds.height).toBeLessThanOrEqual(64);
    await expect(row.getByRole("cell", { name: "2 active · 1 blocking · Needs intervention", exact: true })).toContainText("Needs intervention");
    await row.getByRole("button", { name: "Copy Execution ID", exact: true }).click();
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("quote-completed");
    const trigger = row.getByRole("button", { name: "Identifiers for quote-completed", exact: true });
    await trigger.click();
    const dialog = page.getByRole("dialog", { name: "Workflow run identifiers for quote-completed", exact: true });
    await dialog.getByRole("button", { name: "Copy Artifact ID", exact: true }).click();
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("customer-quote");
    expect((await row.boundingBox())!.height).toBe(bounds.height);
    await page.screenshot({ path: `/tmp/clean-runs-${theme}-${width}.png` });
    await page.keyboard.press("Escape");
    await expect(trigger).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBeLessThanOrEqual(0);
    await expect(page.locator("output")).toHaveCount(0);
  });
}

test("selected run keeps its visual highlight", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html");
  const row = page.getByRole("row", { name: /^Inspect workflow run quote-completed/ });
  const background = await row.evaluate(element => getComputedStyle(element).background);
  await row.getByRole("checkbox", { name: "Select workflow run quote-completed", exact: true }).check();
  await expect(row).toHaveAttribute("aria-selected", "true");
  await page.mouse.move(0, 0);
  await expect.poll(() => row.evaluate(element => getComputedStyle(element).background)).not.toBe(background);
});

// Cells must retain full meaning when a screen reader navigates the table by column.
test("compact run cells expose incident totals and full health to assistive technology", async ({ page }) => {
  await page.goto("/workflow-run-deletion.html?clean-list");
  const row = page.getByRole("row", { name: /^Inspect workflow run quote-completed · Unified workspace/ });
  const cells = row.getByRole("cell");
  await expect.soft(cells.nth(6)).toHaveAccessibleName("4 activities · 0 incidents");
  await expect.soft(cells.nth(5)).toHaveAccessibleName("2 active · 1 blocking · Needs intervention");
  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (root: Element, options: object) => Promise<{ violations: Array<{ id: string; help: string }> }> };
    }).axe.run(document.querySelector(".wf-instance-grid")!, {
      runOnly: { type: "rule", values: ["aria-prohibited-attr"] }, resultTypes: ["violations"]
    });
    return result.violations;
  });
  expect(violations).toEqual([]);
});
