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

// Spec 261: a handler retry that waits for its due time.

test("the run view shows a scheduled retry as text and icon with its attempt and due time, and offers no action", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=retry&theme=light");

  await expect(page.locator(".wf-compensation-phase")).toContainText("Compensating");
  const retry = page.locator(".wf-compensation-state[data-kind='recovery'][data-state='RetryScheduled']");
  await expect(retry).toBeVisible();
  await expect(retry).toContainText(/^\s*Retry scheduled \(1st\) · Attempt 2 of 3 · after .*2099.*\d{1,2}:\d{2}:\d{2}/);
  await expect(retry.locator("svg")).toHaveCount(1);
  await expect(page.locator(".wf-compensation-operator-action")).toHaveCount(0);
  await expect(page.getByRole("button", { name: /waiting on/ })).toHaveCount(0);

  await retry.press("Enter");
  await expect(page.getByTestId("fixture-execution")).toHaveText("ae-4");
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

// Spec 260: operator actions on a waiting recovery.

test("retries a failed handler by keyboard: the dialog names target and effect, then the row shows submitted, Applying… and the result", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=actions&theme=light");

  await expect(page.locator(".wf-compensation-source")).toHaveText("From the scope's journal.");
  await expect(page.locator(".wf-compensation-cause")).toContainText("(message withheld)");
  await expect(page.locator(".wf-compensation-remaining")).toContainText("Still to undo, in this order: Reserve delivery, Reserve inventory.");

  const retry = page.getByRole("button", { name: "Retry the compensation of Reserve delivery", exact: true });
  await retry.focus();
  await page.keyboard.press("Enter");
  const dialog = page.getByRole("dialog", { name: 'Retry the compensation of "Reserve delivery"?' });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('Scope "Order" › step "Reserve delivery" › compensation "Cancel delivery"');
  await expect(dialog).toContainText("Attempt 4 (limit 3 reached) · incident inc-5 · run wf-42");
  await expect(dialog).toContainText('Runs "Cancel delivery" again with the same operation key (ae-scope/step/1/compensate).');
  await expect(dialog).toContainText('If it succeeds, recovery continues with "Release inventory", the compensation of "Reserve inventory".');
  const reason = dialog.getByRole("textbox", { name: "Reason (optional)", exact: true });
  await expect(reason).toBeFocused();
  await expect(reason).toHaveAccessibleDescription(/Visible to everyone who can view this run\. Do not include secrets\./);
  await reason.fill("Delivery service restored");
  await dialog.getByRole("button", { name: "Retry", exact: true }).press("Enter");

  await expect(dialog).toBeHidden();
  await expect(page.getByRole("status")).toHaveText("Retry submitted");
  await expect(page.getByLabel("Recovery: Retry submitted")).toBeVisible();
  // The Retry button is gone while the action is followed, so focus is on the row's state chip.
  await expect(page.getByRole("button", { name: "Recovery: Needs action (1st) · Attempt 3 of 3. Show this occurrence", exact: true })).toBeFocused();
  await expect(page.getByRole("status")).toHaveText("Retry applied.", { timeout: 10_000 });
  await expect(page.getByLabel(/^Recovery: Resolved: effect absent/)).toBeVisible();
  await expect(page.getByTestId("fixture-submissions")).toHaveText(/^studio-[0-9a-f-]+$/);
});

test("resolves only after an outcome and a reason, and abandons with a danger confirmation that lists what stays not undone", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=actions&theme=light");

  await page.getByRole("button", { name: "Resolve Reserve delivery", exact: true }).click();
  const resolve = page.getByRole("dialog", { name: 'Resolve "Reserve delivery" without running it?' });
  const outcomes = resolve.getByRole("group", { name: "Outcome" });
  await expect(outcomes.getByRole("radio", { name: "Effect absent or undone" })).toBeFocused();
  await expect(outcomes.getByRole("radio", { name: "Effect absent or undone" })).toHaveAccessibleDescription("The effect does not exist: it never happened, or it was undone outside Elsa.");
  await expect(resolve).toContainText('Recovery continues with "Release inventory", the compensation of "Reserve inventory".');
  const confirm = resolve.getByRole("button", { name: "Resolve", exact: true });
  await expect(confirm).toBeDisabled();
  await resolve.getByRole("textbox", { name: "Reason (required)" }).fill("Refunded manually, ticket FIN-221");
  await expect(confirm).toBeDisabled();
  await outcomes.getByRole("radio", { name: "Effect present, keep it" }).check();
  await expect(confirm).toBeEnabled();
  await page.keyboard.press("Escape");
  await expect(resolve).toBeHidden();
  await expect(page.getByRole("button", { name: "Resolve Reserve delivery", exact: true })).toBeFocused();

  await page.getByRole("button", { name: "Abandon recovery of Order", exact: true }).click();
  const abandon = page.getByRole("dialog", { name: 'Abandon recovery of scope "Order"?' });
  await expect(abandon.getByRole("list", { name: "Steps that stay not undone" }).getByRole("listitem")).toHaveText(["Reserve inventory (waiting to undo)", "Reserve delivery (undo failed)"]);
  await expect(abandon).toContainText("The scope fails with compensation.scope.abandoned.");
  await expect(abandon).toContainText("The run's incident strategy decides next; by default the run fails.");
  const reason = abandon.getByRole("textbox", { name: "Reason (required)" });
  await reason.fill("line one\nline two");
  await reason.blur();
  await expect(reason).toHaveAccessibleDescription(/Remove line breaks and other control characters\./);
  await expect(abandon.getByRole("button", { name: "Abandon recovery", exact: true })).toBeDisabled();
});

test("shows a refusal and a missing permission as messages, and changes nothing", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=actions&outcome=refused&theme=light");
  await page.getByRole("button", { name: "Abandon recovery of Order", exact: true }).click();
  const abandon = page.getByRole("dialog");
  await abandon.getByRole("textbox", { name: "Reason (required)" }).fill("Order written off by finance");
  await abandon.getByRole("button", { name: "Abandon recovery", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("Someone else already acted on this. The run has been refreshed. Nothing was changed.", { timeout: 10_000 });
  await expect(page.getByLabel(/^Recovery: Needs action/)).toBeVisible();

  await page.goto("/?mode=compensation-scope&view=actions&outcome=forbidden&theme=light");
  await page.getByRole("button", { name: "Retry the compensation of Reserve delivery", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry", exact: true }).click();
  await expect(page.getByRole("status")).toHaveText("You do not have permission to change this run. Nothing was changed.");
  // The refreshed permission signal says the caller may not manage the run, so the actions are gone.
  await expect(page.locator(".wf-compensation-operator-action")).toHaveCount(0);
});

test("a reader who may not manage the run sees the journal and history but no action", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=readonly&theme=light");

  await expect(page.getByLabel(/^Recovery: Needs action/)).toBeVisible();
  await expect(page.getByRole("region", { name: "Operator actions" }).getByRole("listitem")).toHaveCount(1);
  await expect(page.locator(".wf-compensation-operator-action")).toHaveCount(0);
});

test("shows the result states, the abandoned phase, the history and the derived fallback as text and icon", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=resolved&theme=light");
  await expect(page.locator(".wf-compensation-source")).toHaveText("Derived from run data: a scope keeps its journal only until it completes.");
  await expect(page.getByLabel("Recovery: Resolved: effect absent (1st) · Attempt 3 of 3")).toBeVisible();
  const history = page.getByRole("region", { name: "Operator actions" }).getByRole("listitem");
  await expect(history.nth(0)).toContainText("Resolved — effect absent (Reserve delivery)");
  await expect(history.nth(0)).toContainText('"Refunded manually, ticket FIN-221"');
  await history.nth(0).getByRole("button", { name: "Show the incident for Resolved — effect absent on Reserve delivery" }).press("Enter");
  await expect(page.getByTestId("fixture-incident")).toHaveText("inc-5");

  await page.goto("/?mode=compensation-scope&view=abandoned&theme=light");
  await expect(page.locator(".wf-compensation-phase")).toContainText("Abandoned");
  await expect(page.locator(".wf-compensation-phase svg")).toHaveCount(1);
  await expect(page.getByLabel("Recovery: Not undone (recovery abandoned)", { exact: true })).toHaveCount(1);

  await page.goto("/?mode=compensation-scope&view=derived&theme=light");
  await expect(page.locator(".wf-compensation-source")).toHaveText("Derived from run data: the scope's journal is not available.");
});

test("offers Retry and Resolve on an uncertain step without a handler next to its forward state", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=step&theme=light");

  const row = page.locator('[data-step-index="1"] .wf-compensation-step');
  await expect(row.getByRole("button", { name: "Retry Reserve delivery", exact: true })).toBeVisible();
  await row.getByRole("button", { name: "Resolve Reserve delivery", exact: true }).click();
  const resolve = page.getByRole("dialog", { name: 'Resolve "Reserve delivery" without running it?' });
  await expect(resolve).toContainText("Effect absent: the completed steps are undone, newest first.");
  await expect(resolve).toContainText("Effect present: the scope carries on with the next step.");
});

test("the incident dialog offers Retry and each offered decision with its label", async ({ page }) => {
  await page.goto("/?mode=compensation-scope&view=incident&theme=light");

  await page.getByRole("button", { name: "Open incident details" }).click();
  const actions = page.getByRole("dialog", { name: "Incident details" }).getByRole("group", { name: "Operator actions" });
  await expect(actions.getByRole("button")).toHaveText(["Retry", "Resolve: effect absent or undone", "Resolve: effect present, keep it", "Abandon recovery"]);
  await actions.getByRole("button", { name: "Abandon recovery" }).click();
  await expect(page.getByRole("dialog", { name: /^Apply "Abandon recovery"\?$/ })).toBeVisible();
});

const listItemsByView: Record<string, number> = {
  author: 3, run: 3, waiting: 3, retry: 3, nested: 4, confirmed: 3, loop: 3,
  actions: 4, resolved: 5, abandoned: 4, readonly: 4, derived: 4, step: 3
};
// Each dialog, opened from the waiting scope, is checked the same way.
const dialogs: Record<string, string> = {
  "retry-dialog": "Retry the compensation of Reserve delivery",
  "resolve-dialog": "Resolve Reserve delivery",
  "abandon-dialog": "Abandon recovery of Order"
};

for (const theme of ["light", "dark"]) {
  for (const [name, trigger] of Object.entries(dialogs)) {
    for (const width of [390, 1200]) {
      test(`the ${name} fits ${width}px in ${theme} with no serious accessibility violations`, async ({ page }) => {
        await page.setViewportSize({ width, height: 800 });
        await page.goto(`/?mode=compensation-scope&view=actions&theme=${theme}`);
        await page.getByRole("button", { name: trigger, exact: true }).click();
        const dialog = page.getByRole("dialog");
        await expect(dialog).toBeVisible();

        expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
        const box = await dialog.boundingBox();
        expect(box!.x).toBeGreaterThanOrEqual(0);
        expect(box!.x + box!.width).toBeLessThanOrEqual(width);
        for (const button of await dialog.getByRole("button").all()) {
          const bounds = await button.boundingBox();
          expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
        }
        expect(await seriousViolations(page)).toEqual([]);
      });
    }
  }
}

for (const theme of ["light", "dark"]) {
  for (const view of Object.keys(listItemsByView)) {
    for (const width of [390, 1200]) {
      test(`the ${view} view fits ${width}px in ${theme} with no serious accessibility violations`, async ({ page }) => {
        await page.setViewportSize({ width, height: 800 });
        await page.goto(`/?mode=compensation-scope&view=${view}&theme=${theme}`);
        await expect(page.getByRole("listitem")).toHaveCount(listItemsByView[view]);

        expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
        // The action buttons and history rows wrap rather than clip at phone width.
        for (const control of await page.locator(".wf-compensation-operator-action, .wf-compensation-history li, .wf-compensation-state").all()) {
          const bounds = await control.boundingBox();
          expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width);
        }
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
