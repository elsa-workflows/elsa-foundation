import { expect, type Locator, type Page } from "@playwright/test";
import axe from "axe-core";

// Shared steps for the compensation live journeys (spec 261 FR-520): sign in, author a compensation scope, publish it
// through Review & publish, start a Test Run, open a run's scope and wait for its phase, and check accessibility at
// both widths. When a Studio label changes, this module changes and the journeys do not (#3142).

export const studioUser = process.env.ELSA_STUDIO_USER ?? "admin";
export const studioPassword = process.env.ELSA_STUDIO_PASSWORD ?? "Password123!";

/** A step of a scope to author: its forward activity, and optionally a compensation. */
export interface StepSpec {
  name: string;
  activity: string;
  text?: string;
  compensation?: { activity: string; text?: string };
}

/** What the publication review showed before Publish was pressed. */
export interface PublicationOutcome {
  warnings: string[];
}

export async function signIn(page: Page, user = studioUser, password = studioPassword) {
  await page.goto("/");
  await page.locator("input[name=username], input[type=text]").first().fill(user);
  await page.locator("input[type=password]").first().fill(password);
  await page.locator("button[type=submit], button:has-text('Sign in')").first().click();
  await page.waitForLoadState("networkidle");
}

/** Opens an activity picker from its trigger, types the activity and chooses the first match. */
export async function pick(page: Page, trigger: Locator, activity: string) {
  await trigger.click();
  const listbox = page.getByRole("listbox").last();
  await expect(listbox).toBeVisible();
  await page.keyboard.type(activity);
  await expect(listbox.getByRole("option").first()).toContainText(activity);
  await page.keyboard.press("Enter");
  await expect(listbox).toBeHidden();
}

/** Fills the selected activity's text input and moves focus on, which commits it. */
export async function fill(page: Page, label: string, value: string) {
  const box = page.getByRole("textbox", { name: label, exact: true });
  await box.fill(value);
  await box.press("Tab");
}

/** Renames step <paramref name="index"/> (1-based) of the open scope designer. */
export async function rename(page: Page, index: number, value: string) {
  const box = page.getByLabel(`Name of step ${index}`);
  await box.fill(value);
  await box.press("Tab");
  await expect(box).toHaveValue(value);
}

/**
 * Creates a workflow whose root is a Compensation Scope. Scope inputs (by their field label, for example
 * "Handler retry delay") are set while the new scope is selected, before its steps are opened. Returns the designer.
 */
export async function createScopeWorkflow(page: Page, name: string, scopeInputs: Record<string, string> = {}) {
  await page.goto("/workflows/definitions");
  await page.getByRole("button", { name: "Create", exact: true }).click();
  await page.getByLabel("Display name").fill(name);
  await page.locator(".wf-dialog-actions button[type=submit]").click();
  await page.locator("button", { hasText: "Compensation Scope" }).first().click();
  for (const [label, value] of Object.entries(scopeInputs)) {
    const box = page.getByRole("textbox", { name: new RegExp(`^${escapeRegExp(label)}$`, "i") });
    await box.fill(value);
    await box.press("Tab");
    await expect(box).toHaveValue(value);
  }
  await page.locator("button.wf-node-slot-badge", { hasText: "Steps" }).first().click();
  const designer = page.locator(".wf-compensation-designer").last();
  await expect(designer).toBeVisible();
  return designer;
}

/** Adds steps to the open, empty scope designer, in order, with their compensations. */
export async function addSteps(page: Page, steps: StepSpec[], startIndex = 1) {
  for (const [offset, step] of steps.entries()) {
    const index = startIndex + offset;
    const trigger = index === 1
      ? page.getByRole("button", { name: "Add first step" })
      : page.getByRole("button", { name: "Add step", exact: true });
    await pick(page, trigger, step.activity);
    if (step.text !== undefined) await fill(page, "Text", step.text);
    await rename(page, index, step.name);
    if (step.compensation) {
      await pick(page, page.getByRole("button", { name: `Add compensation for ${step.name}` }), step.compensation.activity);
      if (step.compensation.text !== undefined) await fill(page, "Text", step.compensation.text);
    }
  }
}

/**
 * Publishes through Review & publish: waits until Publish is enabled, reads the warnings the review lists (spec 261
 * FR-515: they never block), publishes and closes the outcome. Returns the warnings that were shown.
 */
export async function publish(page: Page): Promise<PublicationOutcome> {
  await page.getByRole("button", { name: "Review & publish" }).click();
  const review = page.getByRole("dialog", { name: "Publish workflow" });
  await expect(review).toBeVisible();
  const publishButton = review.getByRole("button", { name: "Publish", exact: true });
  await expect(publishButton).toBeEnabled({ timeout: 60_000 });
  const warningsRegion = review.getByRole("region", { name: "Warnings" });
  const warnings = await warningsRegion.count() > 0 ? await warningsRegion.getByRole("listitem").allInnerTexts() : [];
  // The warnings are spec 261's part of the review; the dialog's own controls are covered by its mocked browser spec.
  if (warnings.length > 0) await expectAccessible(page, ".wf-publication-warnings");
  await publishButton.click();
  const published = page.getByRole("dialog", { name: "Published" });
  await expect(published).toBeVisible({ timeout: 60_000 });
  await published.getByRole("button", { name: "Close" }).click();
  return { warnings };
}

/** Starts a Test Run of the open workflow and returns its run id. */
export async function startTestRun(page: Page) {
  await page.getByRole("button", { name: "Test Run", exact: true }).click();
  await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
  const runLink = page.locator("text=RUN / INSTANCE").locator("xpath=following-sibling::*[1]");
  await expect(runLink).not.toHaveText("", { timeout: 30_000 });
  const runId = (await runLink.innerText()).trim();
  expect(runId).toMatch(/^\S+$/);
  return runId;
}

/** Opens a run and its root scope's designer, reloading the run view. */
export async function openScope(page: Page, runId: string) {
  await page.goto(`/workflows/instances/${runId}`);
  const badge = page.locator("button.wf-node-slot-badge", { hasText: "Steps" }).first();
  const designer = page.locator(".wf-compensation-designer");
  await expect(badge.or(designer).first()).toBeVisible({ timeout: 30_000 });
  if (!(await designer.first().isVisible())) await badge.click();
  await expect(designer.first()).toBeVisible();
  return designer.first();
}

/** Reloads the run until its root scope shows <paramref name="phase"/>, then returns the designer. */
export async function waitForPhase(page: Page, runId: string, phase: string, timeout = 120_000) {
  await expect.poll(async () => (await openScope(page, runId)).locator(".wf-compensation-phase").innerText(), { timeout, intervals: [2000] })
    .toContain(phase);
  return page.locator(".wf-compensation-designer").first();
}

/**
 * Reads a run's root scope in a fresh tab and closes it. A long wait polled by reloading one tab exhausts the browser
 * (net::ERR_INSUFFICIENT_RESOURCES after a few dozen reloads), and Studio then cannot load its modules.
 */
export async function readScopeInFreshTab<T>(page: Page, runId: string, read: (scope: Locator) => Promise<T>): Promise<T> {
  const tab = await page.context().newPage();
  try {
    return await read(await openScope(tab, runId));
  } finally {
    await tab.close();
  }
}

/** The recovery chip of the row named <paramref name="step"/>. */
export function recoveryChip(scope: Locator, step: string) {
  return scope.getByRole("listitem").filter({ hasText: step }).first().locator(".wf-compensation-state[data-kind='recovery']").first();
}

export async function recoveryName(scope: Locator, step: string) {
  return (await recoveryChip(scope, step).getAttribute("aria-label")) ?? "";
}

export function historyItems(page: Page) {
  return page.getByRole("region", { name: "Operator actions" }).getByRole("listitem");
}

/** Axe on one part of the page at 1200 and 390 px; serious and critical violations fail. The size is restored after. */
export async function expectAccessible(page: Page, selector: string) {
  const original = page.viewportSize();
  for (const width of [1200, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await expect(page.locator(selector).first()).toBeVisible();
    await page.addScriptTag({ content: axe.source });
    const violations = await page.evaluate(async scope => {
      const result = await (window as unknown as Window & {
        axe: {
          run: (root: Element, options: object) => Promise<{
            violations: Array<{ id: string; impact: string | null; help: string; nodes: Array<{ target: unknown[]; failureSummary?: string }> }>;
          }>;
        };
      }).axe.run(document.querySelector(scope)!, { resultTypes: ["violations"] });
      return result.violations
        .filter(violation => violation.impact === "critical" || violation.impact === "serious")
        .map(violation => `${violation.id}: ${violation.help} (${violation.nodes.map(node => `${node.target.join(" ")}: ${node.failureSummary ?? ""}`).join("; ")})`);
    }, selector);
    expect(violations, `${selector} at ${width}px`).toEqual([]);
  }
  if (original) await page.setViewportSize(original);
}

/** The two replay-safe warnings (spec 261 OD20 A) for a step whose forward activity and compensation are both leaves. */
export function replaySafeWarnings(step: string) {
  return [
    `"${step}" is marked safe to repeat, so its call may not be committed before it runs; if the process stops, recovery may not know it happened. Mark it as an external effect unless it has none.`,
    `The compensation of "${step}" is marked safe to repeat, so an undo may run again after a restart without a committed claim. Mark it as an external effect unless it is idempotent and has no effect of its own.`
  ];
}

function escapeRegExp(value: string) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
