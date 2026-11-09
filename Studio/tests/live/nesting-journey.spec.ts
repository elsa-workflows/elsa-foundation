import { expect, test } from "@playwright/test";
import { addSteps, createScopeWorkflow, expectAccessible, publish, recoveryChip, replaySafeWarnings, signIn, startTestRun, waitForPhase } from "./support/journey";

// Spec 261 Q13 (T519), the browser layer of spec 258 against a real Studio and Workbench: author an outer scope whose
// steps are a charge with its refund, a Confirm, an inner scope with two reservations and a shipment that fails. The
// Confirm keeps the charge; the inner scope completes and hands its reservations to the outer scope, which undoes them
// newest first when the shipment fails. Every step and compensation but Ship is a Write Line. Restarts and every
// persistence mode are proven by the backend nesting journeys and the Workbench owned-process test.

test.skip(!process.env.ELSA_STUDIO_URL, "Set ELSA_STUDIO_URL to a running Studio connected to a Workbench.");

test("a nested scope with a Confirm is authored, published, run, and its hand-over and kept step are shown", async ({ page }) => {
  test.setTimeout(300_000);
  await signIn(page);

  // Author the outer scope, then open the inner scope's steps and author them.
  await createScopeWorkflow(page, `Nesting journey ${Date.now()}`);
  await addSteps(page, [
    { name: "Charge", activity: "Write Line", text: "Charged card", compensation: { activity: "Write Line", text: "Refunded card" } },
    { name: "Confirm", activity: "Confirm" },
    { name: "Reserve", activity: "Compensation Scope" },
    { name: "Ship", activity: "Fault" }
  ]);
  const outer = page.locator(".wf-compensation-designer").first();
  await expect(outer.getByRole("listitem")).toHaveCount(4);
  await expect(outer.getByRole("listitem").nth(1)).toHaveAccessibleName("Step 2, Confirm, no compensation");
  await outer.getByRole("button", { name: "Open Steps of Compensation Scope", exact: true }).click();
  await expect(page.getByRole("button", { name: "Add first step" })).toBeVisible();
  await addSteps(page, [
    { name: "Reserve A", activity: "Write Line", text: "Reserved A", compensation: { activity: "Write Line", text: "Released A" } },
    { name: "Reserve B", activity: "Write Line", text: "Reserved B", compensation: { activity: "Write Line", text: "Released B" } }
  ]);

  // Publish: the three Write Line pairs warn, the Confirm, the inner scope and Ship do not, and Publish stays enabled.
  const { warnings } = await publish(page);
  expect([...warnings].sort()).toEqual(
    [...replaySafeWarnings("Charge"), ...replaySafeWarnings("Reserve A"), ...replaySafeWarnings("Reserve B")].sort());

  // Run: Ship fails, the outer scope undoes the handed-over reservations newest first and keeps the confirmed charge.
  const runId = await startTestRun(page);
  const scope = await waitForPhase(page, runId, "Compensated");
  await expect(scope.locator(".wf-compensation-cause")).toContainText("Recovery started because Ship failed");
  await expect(recoveryChip(scope, "Charge")).toHaveAccessibleName(/^Recovery: Confirmed/);
  await expect(scope.getByRole("button", { name: "Show what confirmed Charge", exact: true })).toBeVisible();

  const received = page.getByRole("region", { name: "Handed over by inner scopes" });
  await expect(received).toBeVisible();
  const handedOver = received.getByRole("list", { name: "Handed-over steps" }).getByRole("listitem");
  await expect(handedOver).toHaveCount(2);
  await expect(handedOver.filter({ hasText: "Reserve B" }).locator(".wf-compensation-state[data-kind='recovery']").first())
    .toHaveAccessibleName(/^Recovery: Compensated \(1st\)/);
  await expect(handedOver.filter({ hasText: "Reserve A" }).locator(".wf-compensation-state[data-kind='recovery']").first())
    .toHaveAccessibleName(/^Recovery: Compensated \(2nd\)/);
  await expect(received.getByRole("button", { name: /, where Reserve A ran$/ })).toHaveCount(1);
  await expectAccessible(page, ".wf-compensation-designer");

  // The inner scope's own view: its reservations were undone by the outer scope after the hand-over, so each reads
  // Compensated and links to the handler occurrence that undid it.
  await scope.getByRole("button", { name: "Open Steps of Compensation Scope", exact: true }).click();
  const inner = page.locator(".wf-compensation-designer").last();
  await expect(inner.getByRole("listitem")).toHaveCount(2);
  for (const step of ["Reserve A", "Reserve B"])
    await expect(recoveryChip(inner, step)).toHaveAccessibleName("Recovery: Compensated. Show this occurrence");
  await expectAccessible(page, ".wf-compensation-designer");
});
