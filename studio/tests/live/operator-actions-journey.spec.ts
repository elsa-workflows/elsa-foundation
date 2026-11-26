import { expect, test, type Locator, type Page } from "@playwright/test";
import {
  addSteps,
  createScopeWorkflow,
  expectAccessible,
  historyItems,
  openScope,
  publish,
  readScopeInFreshTab,
  recoveryChip,
  recoveryName,
  signIn,
  startTestRun,
  waitForPhase
} from "./support/journey";

// Spec 260 (M4), the browser layer against a real Studio and Workbench: an operator retries, resolves and abandons a
// waiting compensation recovery, a stale action from a second tab is refused, a reload during polling resumes the
// plan without submitting it again, and a reader without manage permission sees no action and gets 403.
// Restarts and every persistence mode are proven by the backend journeys (O1–O8) and the Workbench owned-process test.
// Spec 261 adds a scheduled retry: a scope with a handler retry delay shows "Retry scheduled" with its attempt and due
// time, offers no action while it waits, and needs action once the attempts run out.

test.skip(!process.env.ELSA_STUDIO_URL, "Set ELSA_STUDIO_URL to a running Studio connected to a Workbench.");
test.describe.configure({ mode: "serial" });

let designerUrl = "";
let waitingRunForReader = "";

test("an operator retries, resolves and abandons a waiting recovery, and a stale second tab is refused", async ({ page, context }) => {
  test.setTimeout(600_000);
  const submissions: string[] = [];
  page.on("request", request => {
    if (request.method() === "POST" && /\/runtime\/workflows\/alteration-plans$/.test(new URL(request.url()).pathname)) submissions.push(request.url());
  });

  await signIn(page);
  designerUrl = await authorAndPublishOrderWorkflow(page);

  // The scope waits: Cancel delivery failed three times.
  const first = await startTestRun(page);
  const designer = await openWaitingScope(page, first);
  await expect(designer.locator(".wf-compensation-source")).toHaveText("From the scope's journal.");
  await expect(designer.locator(".wf-compensation-cause")).toContainText("Recovery started because Confirm order failed");
  await expect(designer.locator(".wf-compensation-remaining")).toContainText("Still to undo, in this order: Reserve delivery, Reserve inventory.");
  await expect(recoveryChip(designer, "Reserve delivery")).toHaveAccessibleName(/^Recovery: Needs action \(1st\) · Attempt 3 of 3/);
  await expectAccessible(page, ".wf-compensation-designer");

  // Retry: the handler still fails, so the entry waits again after a fourth attempt.
  await designer.getByRole("button", { name: "Retry the compensation of Reserve delivery", exact: true }).click();
  const retry = page.getByRole("dialog", { name: 'Retry the compensation of "Reserve delivery"?' });
  await expect(retry).toContainText("Attempt 4 (limit 3 reached)");
  await expect(retry).toContainText("with the same operation key (");
  await expectAccessible(page, "[role=dialog]");
  await retry.getByRole("textbox", { name: "Reason (optional)" }).fill("Delivery service restored");
  await retry.getByRole("button", { name: "Retry", exact: true }).click();
  await expect(page.locator(".wf-operator-status")).toHaveText(/Retry submitted|Applying…|Retry applied\./);
  await expect(page.locator(".wf-operator-status")).toHaveText("Retry applied.", { timeout: 120_000 });
  await expect.poll(async () => recoveryName(await openScope(page, first), "Reserve delivery"), { timeout: 120_000, intervals: [2000] })
    .toMatch(/^Recovery: Needs action \(1st\) · Attempt 4 of 3/);
  await expect(historyItems(page)).toHaveCount(1);
  await expect(historyItems(page).first()).toContainText("Retry (Reserve delivery)");
  await expect(historyItems(page).first()).toContainText('"Delivery service restored"');

  // Resolve "effect absent", reloading while Studio follows the plan: it resumes and never submits again.
  let scope = page.locator(".wf-compensation-designer");
  await scope.getByRole("button", { name: "Resolve Reserve delivery", exact: true }).click();
  const resolve = page.getByRole("dialog", { name: 'Resolve "Reserve delivery" without running it?' });
  await resolve.getByRole("radio", { name: "Effect absent or undone" }).check();
  await resolve.getByRole("textbox", { name: "Reason (required)" }).fill("Cancelled with the carrier by phone, ticket OPS-7");
  await expectAccessible(page, "[role=dialog]");
  await resolve.getByRole("button", { name: "Resolve", exact: true }).click();
  await expect(page.locator(".wf-operator-status")).toHaveText(/Resolve submitted|Applying…|Resolve applied\./);
  await page.reload();
  expect(new URL(page.url()).pathname).toBe(`/workflows/instances/${first}`);
  await expect(page.locator(".wf-operator-status")).toHaveText(/Resolve submitted|Applying…|Resolve applied\./, { timeout: 30_000 });
  await expect(page.locator(".wf-operator-status")).toHaveText(/Resolve applied\.|The runtime accepted the action/, { timeout: 120_000 });
  expect(submissions).toHaveLength(2);
  await expect.poll(async () => (await openScope(page, first)).locator(".wf-compensation-phase").innerText(), { timeout: 120_000, intervals: [2000] })
    .toContain("Compensated");
  scope = page.locator(".wf-compensation-designer");
  await expect(recoveryChip(scope, "Reserve delivery")).toHaveAccessibleName(/^Recovery: Resolved: effect absent/);
  await expect(recoveryChip(scope, "Reserve inventory")).toHaveAccessibleName(/^Recovery: Compensated/);
  await expect(historyItems(page)).toHaveCount(2);
  await expect(historyItems(page).first()).toContainText("Resolved — effect absent (Reserve delivery)");
  await expectAccessible(page, ".wf-compensation-designer");

  // Two tabs on another waiting run: the first retries, so the wait the second still shows is gone, and its
  // resolve is refused as stale.
  await page.goto(designerUrl);
  const second = await startTestRun(page);
  const other = await context.newPage();
  scope = await openWaitingScope(page, second);
  const stale = await openWaitingScope(other, second);
  await scope.getByRole("button", { name: "Retry the compensation of Reserve delivery", exact: true }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Retry", exact: true }).click();
  await expect(page.locator(".wf-operator-status")).toHaveText("Retry applied.", { timeout: 120_000 });
  await resolveEntry(other, stale, "Effect absent or undone", "Resolved from a stale tab");
  await expect(other.locator(".wf-operator-status")).toHaveText(
    "Someone else already acted on this. The run has been refreshed. Nothing was changed.", { timeout: 120_000 });
  await other.close();

  // Abandon the same run once it waits again: the scope is abandoned, its open entries stay not undone, and the run
  // faults.
  scope = await openWaitingScope(page, second);
  await expect(recoveryChip(scope, "Reserve delivery")).toHaveAccessibleName(/Attempt 4 of 3/);
  const scopeLabel = (await scope.locator(".wf-compensation-operator-action[data-tone='danger']").getAttribute("aria-label"))!.replace(/^Abandon recovery of /, "");
  await scope.getByRole("button", { name: `Abandon recovery of ${scopeLabel}`, exact: true }).click();
  const abandon = page.getByRole("dialog", { name: `Abandon recovery of scope "${scopeLabel}"?` });
  await expect(abandon.getByRole("list", { name: "Steps that stay not undone" }).getByRole("listitem"))
    .toHaveText(["Reserve inventory (waiting to undo)", "Reserve delivery (undo failed)"]);
  await expectAccessible(page, "[role=dialog]");
  await abandon.getByRole("textbox", { name: "Reason (required)" }).fill("Order written off by finance");
  await abandon.getByRole("button", { name: "Abandon recovery", exact: true }).click();
  await expect(page.locator(".wf-operator-status")).toHaveText(/Recovery abandoned\.|The runtime accepted the action/, { timeout: 120_000 });
  await expect.poll(async () => (await openScope(page, second)).locator(".wf-compensation-phase").innerText(), { timeout: 120_000, intervals: [2000] })
    .toContain("Abandoned");
  scope = page.locator(".wf-compensation-designer");
  await expect(scope.getByLabel(/^Recovery: Not undone \(recovery abandoned\)/)).toHaveCount(2);
  await expect(historyItems(page).first()).toContainText("Abandoned");
  await expect(page.locator(".wf-instance-canvas-shell > header")).toContainText("Faulted");
  await expectAccessible(page, ".wf-compensation-designer");

  // A third waiting run for the reader below.
  await page.goto(designerUrl);
  waitingRunForReader = await startTestRun(page);
  await openWaitingScope(page, waitingRunForReader);
});

test("a reader without manage permission sees the journal and history but no action, and a direct submission gets 403", async ({ page }) => {
  test.skip(!process.env.ELSA_STUDIO_READONLY_USER || !process.env.ELSA_STUDIO_READONLY_PASSWORD,
    "Set ELSA_STUDIO_READONLY_USER and ELSA_STUDIO_READONLY_PASSWORD to a user with workflows/runtime:read but not workflows/runtime:manage.");
  test.skip(!waitingRunForReader, "The operator journey did not leave a waiting run.");

  let authorization: string | undefined;
  let backend: string | undefined;
  page.on("request", request => {
    const header = request.headers().authorization;
    if (header && /\/runtime\/workflows\//.test(request.url())) {
      authorization = header;
      backend = new URL(request.url()).origin;
    }
  });
  await signIn(page, process.env.ELSA_STUDIO_READONLY_USER, process.env.ELSA_STUDIO_READONLY_PASSWORD);
  const scope = await openWaitingScope(page, waitingRunForReader);
  await expect(scope.locator(".wf-compensation-source")).toHaveText("From the scope's journal.");
  await expect(scope.locator(".wf-compensation-operator-action")).toHaveCount(0);
  await expectAccessible(page, ".wf-compensation-designer");

  expect(authorization, "Studio sent no bearer token to the runtime API").toBeTruthy();
  const response = await page.request.post(`${backend}/runtime/workflows/alteration-plans`, {
    headers: { Authorization: authorization!, "Idempotency-Key": `live-reader-${Date.now()}`, "Content-Type": "application/json" },
    data: { target: { executionIds: [waitingRunForReader] }, alterations: [{ kind: "RetryFaultedActivity", schemaVersion: 1, payload: { sourceActivityExecutionId: "unknown" } }] }
  });
  expect(response.status()).toBe(403);
});

// Spec 261 (T520): with a handler retry delay of 20 s the failed handler's next attempt is scheduled, not run at once.
// The row says so with its attempt and due time, no action is offered while it waits (there is no blocking incident),
// and once the attempts run out the scope needs action as before. The handler fails at once, so the moment it is
// running ("Retrying") is too short to see; the journey sees the second and third attempts scheduled instead.
test("a scheduled retry waits without offering an action, then the scope needs action", async ({ page }) => {
  test.setTimeout(300_000);
  await signIn(page);
  await authorAndPublishOrderWorkflow(page, `Backoff journey ${Date.now()}`, { "Handler Retry Delay": "00:00:20" });
  const runId = await startTestRun(page);

  // Each poll reads the run in a fresh tab; the waits are long enough that reloading one tab would exhaust it.
  await expect.poll(() => readScopeInFreshTab(page, runId, scope => recoveryName(scope, "Reserve delivery")), { timeout: 60_000, intervals: [3000] })
    .toMatch(/^Recovery: Retry scheduled \(1st\) · Attempt 2 of 3 · after /);
  let scope = await openScope(page, runId);
  await expect(recoveryChip(scope, "Reserve delivery")).toHaveAccessibleName(/^Recovery: Retry (scheduled|due) \(1st\) · Attempt 2 of 3/);
  await expect(scope.locator(".wf-compensation-phase")).toContainText("Compensating");
  await expect(scope.locator(".wf-compensation-operator-action")).toHaveCount(0);
  await expectAccessible(page, ".wf-compensation-designer");

  await expect.poll(() => readScopeInFreshTab(page, runId, scope => recoveryName(scope, "Reserve delivery")), { timeout: 90_000, intervals: [3000] })
    .toMatch(/^Recovery: (Retry scheduled|Retry due|Needs action) \(1st\) · Attempt 3 of 3/);
  await expect.poll(() => readScopeInFreshTab(page, runId, scope => scope.locator(".wf-compensation-phase").innerText()), { timeout: 120_000, intervals: [3000] })
    .toContain("Needs action");

  scope = await openScope(page, runId);
  await expect(recoveryChip(scope, "Reserve delivery")).toHaveAccessibleName(/^Recovery: Needs action \(1st\) · Attempt 3 of 3/);
  await expect(scope.getByRole("button", { name: "Retry the compensation of Reserve delivery", exact: true })).toBeVisible();
  await expectAccessible(page, ".wf-compensation-designer");
});

// Scope: [ Reserve inventory ↔ Release inventory, Reserve delivery ↔ Cancel delivery (always fails), Confirm order (fails) ].
async function authorAndPublishOrderWorkflow(page: Page, name = `Operator actions journey ${Date.now()}`, scopeInputs: Record<string, string> = {}) {
  await createScopeWorkflow(page, name, scopeInputs);
  await addSteps(page, [
    { name: "Reserve inventory", activity: "Write Line", text: "Reserved inventory", compensation: { activity: "Write Line", text: "Released inventory" } },
    { name: "Reserve delivery", activity: "Write Line", text: "Reserved delivery", compensation: { activity: "Fault" } },
    { name: "Confirm order", activity: "Fault" }
  ]);
  await publish(page);
  return page.url();
}

async function openWaitingScope(page: Page, runId: string) {
  return waitForPhase(page, runId, "Needs action");
}

async function resolveEntry(page: Page, scope: Locator, outcome: string, reason: string) {
  await scope.getByRole("button", { name: "Resolve Reserve delivery", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: 'Resolve "Reserve delivery" without running it?' });
  await dialog.getByRole("radio", { name: outcome }).check();
  await dialog.getByRole("textbox", { name: "Reason (required)" }).fill(reason);
  await dialog.getByRole("button", { name: "Resolve", exact: true }).click();
  await expect(dialog).toBeHidden();
}
