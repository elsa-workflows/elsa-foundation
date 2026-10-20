import { expect, test, type Page } from "@playwright/test";
import axe from "axe-core";

// Spec 250 M1: the paired step list for a compensation scope, authored and inspected.

test("authors steps by keyboard and keeps each row's pairing readable", async ({ page }) => {
  await page.setViewportSize({ width: 1200, height: 800 });
  await page.goto("/?mode=compensation-scope&theme=light");

  const rows = page.getByRole("listitem");
  await expect(rows).toHaveCount(3);
  await expect(rows.nth(0)).toHaveAccessibleName("Step 1, Reserve inventory, compensated by Release inventory");
  await expect(rows.nth(2)).toHaveAccessibleName("Step 3, Confirm order, no compensation");

  // Arrow keys move between the same cell of neighbouring rows.
  await page.getByRole("button", { name: "Step 1, Reserve inventory: Reserve inventory", exact: true }).focus();
  await page.keyboard.press("ArrowDown");
  await expect(page.getByRole("button", { name: "Step 2, Reserve delivery: Reserve delivery", exact: true })).toBeFocused();

  // Rename, reorder and remove a compensation; each edit is announced and lands in the scope's steps.
  const name = page.getByRole("textbox", { name: "Name of step 3" });
  await name.fill("Confirm");
  await name.press("Tab");
  await expect(page.getByTestId("fixture-steps")).toHaveText("Reserve inventory|Reserve delivery|Confirm");
  await page.getByRole("button", { name: "Move Confirm up", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-steps")).toHaveText("Reserve inventory|Confirm|Reserve delivery");
  await page.getByRole("button", { name: "Remove compensation for Reserve inventory", exact: true }).press("Enter");
  await expect(rows.nth(0)).toHaveAccessibleName("Step 1, Reserve inventory, no compensation");

  // Adding a compensation asks the host for an activity for that row.
  await page.getByRole("button", { name: "Add compensation for Reserve inventory", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-requested")).toHaveText("compensation:0");
});

test("a duplicate step name is reported on its row", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&theme=light");

  const name = page.getByRole("textbox", { name: "Name of step 2" });
  await name.fill("Reserve inventory");
  await expect(name).toHaveAttribute("aria-invalid", "true");
  await expect(name).toHaveAccessibleDescription('Step "Reserve inventory" is defined more than once.');
});

test("the run view shows forward and recovery state as text and opens an occurrence", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=run&theme=light");

  await expect(page.locator(".wf-compensation-phase")).toContainText("Compensated");
  await expect(page.locator(".wf-compensation-cause")).toContainText("Recovery started because Confirm order failed");
  await expect(page.getByRole("textbox")).toHaveCount(0);

  await page.getByRole("button", { name: "Recovery: Compensated (1st). Show this occurrence", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-execution")).toHaveText("ae-4");
});

test("the run view shows an uncertain step and a handler that needs an operator, and opens its incident", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=waiting&theme=light");

  await expect(page.locator(".wf-compensation-phase")).toContainText("Needs action");
  await expect(page.locator(".wf-compensation-cause")).toContainText("Recovery started because Reserve delivery could not confirm its outcome");
  await expect(page.getByRole("button", { name: "Forward: Uncertain. Show this occurrence", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Recovery: Needs action (1st) · Attempt 3 of 3. Show this occurrence", exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Show the incident Reserve delivery is waiting on", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-incident")).toHaveText("inc-6");
});

// Spec 258: nesting, confirmation, Compensate, cancellation and repeated scopes.

test("an outer scope lists work an inner scope handed over and links to where it ran", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=nested&theme=light");

  const received = page.getByRole("region", { name: "Handed over by inner scopes" });
  await expect(received.getByRole("listitem")).toHaveAccessibleName("Reserve hotel, from Book hotel (iteration it-2)");
  await expect(received.getByRole("button", { name: "Recovery: Compensated (1st). Show this occurrence", exact: true })).toBeVisible();

  await received.getByRole("button", { name: "Show Book hotel (iteration it-2), where Reserve hotel ran", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-execution")).toHaveText("ae-inner");
});

test("a completed inner scope shows confirmed and compensable-by steps as text and opens what confirmed one", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=confirmed&theme=light");

  await expect(page.locator(".wf-compensation-phase")).toContainText("Completed");
  const confirmed = page.locator(".wf-compensation-state[data-kind='recovery']", { hasText: "Confirmed" });
  await expect(confirmed).toBeVisible();
  await expect(confirmed.locator("svg")).toHaveCount(1);

  await page.getByRole("button", { name: "Show what confirmed Reserve inventory", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-execution")).toHaveText("ae-confirm");
  await page.getByRole("button", { name: "Recovery: Compensable by Travel booking. Show this occurrence", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-execution")).toHaveText("ae-outer");
});

test("a repeated scope picks its occurrence by keyboard, and names a Compensate request and a cancelled occurrence", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=loop&theme=light");

  const picker = page.getByRole("combobox", { name: "Occurrence", exact: true });
  await expect(picker).toHaveAccessibleDescription("This scope ran 2 times. Choose the occurrence to show.");
  await expect(picker).toHaveValue("ae-scope-2");
  await expect(page.locator(".wf-compensation-phase")).toContainText("Cancelled — nothing undone");

  await picker.focus();
  await page.keyboard.press("ArrowUp");
  await expect(page.getByTestId("fixture-execution")).toHaveText("ae-scope-1");
  await expect(picker).toHaveValue("ae-scope-1");
  await expect(picker).toBeFocused();
  await expect(page.locator(".wf-compensation-phase")).toContainText("Compensated");
  await expect(page.locator(".wf-compensation-cause")).toContainText("Recovery started because a Compensate activity asked for it while Reserve delivery was running.");
  await expect(page.getByRole("button", { name: "Forward: Uncertain. Show this occurrence", exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Show request", exact: true }).press("Enter");
  await expect(page.getByTestId("fixture-execution")).toHaveText("ae-undo");
});

const listItemsByView: Record<string, number> = { author: 3, run: 3, waiting: 3, nested: 4, confirmed: 3, loop: 3 };

for (const theme of ["light", "dark"]) {
  for (const view of Object.keys(listItemsByView)) {
    for (const width of [390, 1200]) {
      test(`the ${view} view fits ${width}px in ${theme} with no serious accessibility violations`, async ({ page }) => {
        await page.setViewportSize({ width, height: 800 });
        await page.goto(`/?mode=compensation-scope&view=${view}&theme=${theme}`);
        await expect(page.getByRole("listitem")).toHaveCount(listItemsByView[view]);

        expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
        if (view === "loop") {
          // The page clips rather than scrolls, so check the picker itself stays inside the viewport.
          const picker = await page.getByRole("combobox", { name: "Occurrence", exact: true }).boundingBox();
          expect(picker!.x + picker!.width).toBeLessThanOrEqual(width);
        }
        expect(await seriousViolations(page)).toEqual([]);
      });
    }
  }
}

async function seriousViolations(page: Page) {
  await page.addScriptTag({ content: axe.source });
  return page.evaluate(async () => {
    const result = await (window as Window & {
      axe: { run: (root: Document, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null; help: string }> }> };
    }).axe.run(document, { resultTypes: ["violations"] });
    return result.violations
      .filter(violation => violation.impact === "critical" || violation.impact === "serious")
      .map(violation => `${violation.id}: ${violation.help}`);
  });
}
