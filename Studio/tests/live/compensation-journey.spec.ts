import { expect, test } from "@playwright/test";
import { addSteps, createScopeWorkflow, expectAccessible, openScope, publish, replaySafeWarnings, signIn, startTestRun, waitForPhase } from "./support/journey";

// Spec 250 M1, the browser layer, repaired in spec 261 (#3142): author a reservation workflow with a compensation scope
// from scratch in Studio, publish it through Review & publish, start a Test Run against the real Workbench, and inspect
// the recovery. Every step and compensation is a Write Line, which is safe to repeat, so the publication review lists
// the replay-safe warnings for both reservations (spec 250 FR-014, spec 261 FR-515) and still publishes. Restart
// recovery and the participant's persisted state are proven against a real process by
// Essentials/tests/Workbench/Tests/WorkbenchCompensationRecoveryTests.cs; this journey proves authoring and inspection.

test.skip(!process.env.ELSA_STUDIO_URL, "Set ELSA_STUDIO_URL to a running Studio connected to a Workbench.");

test("a compensation scope is authored, published with its warnings, run and inspected in Studio", async ({ page }) => {
  await signIn(page);

  // Author.
  const designer = await createScopeWorkflow(page, `Compensation journey ${Date.now()}`);
  await addSteps(page, [
    { name: "Reserve inventory", activity: "Write Line", text: "Reserved inventory", compensation: { activity: "Write Line", text: "Released inventory" } },
    { name: "Reserve delivery", activity: "Write Line", text: "Reserved delivery", compensation: { activity: "Write Line", text: "Cancelled delivery" } },
    { name: "Confirm order", activity: "Fault" }
  ]);
  const rows = designer.getByRole("listitem");
  await expect(rows).toHaveCount(3);
  await expect(rows.nth(1)).toHaveAccessibleName(/^Step 2, Reserve delivery, compensated by /);
  await expect(rows.nth(2)).toHaveAccessibleName("Step 3, Confirm order, no compensation");
  await expectAccessible(page, ".wf-compensation-designer");

  // Publish: the review warns about both Write Line pairs and keeps Publish enabled; the Fault step has no compensation.
  const { warnings } = await publish(page);
  expect([...warnings].sort()).toEqual([...replaySafeWarnings("Reserve inventory"), ...replaySafeWarnings("Reserve delivery")].sort());

  // Run, then open the run once the scope has compensated.
  const runId = await startTestRun(page);
  const scope = await waitForPhase(page, runId, "Compensated");

  // Inspect: the order failed, the two reservations were undone newest first.
  await expect(scope.locator(".wf-compensation-cause")).toContainText("Recovery started because Confirm order failed");
  await expect(scope.getByRole("button", { name: "Recovery: Compensated (1st). Show this occurrence" })).toHaveCount(1);
  await expect(scope.getByRole("button", { name: "Recovery: Compensated (2nd). Show this occurrence" })).toHaveCount(1);
  await expect(scope.getByRole("listitem").filter({ hasText: "Reserve delivery" }).getByRole("button", { name: /^Recovery: Compensated \(1st\)/ })).toHaveCount(1);
  await expect(scope.getByRole("listitem").filter({ hasText: "Reserve inventory" }).getByRole("button", { name: /^Recovery: Compensated \(2nd\)/ })).toHaveCount(1);
  await expect(scope.getByLabel("Recovery: Not applicable")).toHaveCount(1);
  await expectAccessible(page, ".wf-compensation-designer");

  // The first handler that ran says which forward occurrence it compensates.
  await scope.getByRole("button", { name: "Recovery: Compensated (1st). Show this occurrence" }).click();
  await page.getByRole("tab", { name: "Activity", exact: true }).click();
  await expect(page.locator(".wf-activity-compensates")).toContainText("Compensates Reserve delivery (occurrence");

  // A reload shows the same recovery.
  const reloaded = await openScope(page, runId);
  await expect(reloaded.locator(".wf-compensation-phase")).toContainText("Compensated");
});
