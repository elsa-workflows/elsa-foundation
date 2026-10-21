import { expect, test, type Locator, type Page } from "@playwright/test";
import axe from "axe-core";

test("primary triage filters active health across pages and composes with Running", async ({ page }) => {
  await openRuns(page);
  await page.getByLabel("Workflow run page size").selectOption("10");

  await page.getByRole("button", { name: "Needs attention", exact: true }).click();
  await expect(page.getByRole("button", { name: "Needs attention", exact: true })).toHaveAttribute("aria-pressed", "true");
  await expect(inspectRun(page, "run-01")).toBeVisible();
  await expect(inspectRun(page, "run-10")).toBeVisible();
  await expect(page.getByText("Showing 10 of 11 matching runs")).toBeVisible();
  await expect(page.getByText("1 active · 1 blocking · Needs intervention")).toBeVisible();
  await expect(page.getByText("1 active · Non-blocking").first()).toBeVisible();

  await page.getByRole("button", { name: "Next workflow run page" }).click();
  await expect(inspectRun(page, "run-12")).toBeVisible();
  await expect(page.getByText("Showing 1 of 11 matching runs")).toBeVisible();
  await expect(inspectRun(page, "run-13")).toHaveCount(0);

  await page.getByRole("button", { name: "Clear filters" }).click();
  await page.getByRole("button", { name: "Running" }).click();
  await expect(page.getByText("Showing 10 of 11 matching runs")).toBeVisible();
  await expect(inspectRun(page, "run-11")).toHaveCount(0);
  await page.getByRole("button", { name: "Next workflow run page" }).click();
  await expect(inspectRun(page, "run-11")).toBeVisible();
  await expect(runRow(page, "run-11").getByText("No active incidents", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Needs attention", exact: true }).click();
  await expect(page.getByText("Showing 10 of 10 matching runs")).toBeVisible();
  await expect(inspectRun(page, "run-10")).toBeVisible();
  await expect(inspectRun(page, "run-12")).toHaveCount(0);
});

test("workflow choice, advanced drafts, applied chips, selection and exact copy stay separate", async ({ page }) => {
  await openRuns(page);
  await page.getByLabel("Workflow run page size").selectOption("50");
  const executionId = "run-14-quarterly-reconciliation-2026-10-20-long-execution-reference";
  const row = runRow(page, executionId);
  await expect(row).toBeVisible();
  await expect(inspectRun(page, executionId)).toHaveAccessibleName(`Inspect workflow run ${executionId} · Quarterly regional reconciliation and partner settlement`);
  await expect(row.getByLabel(`Execution ID: ${executionId}`)).toBeVisible();
  await expect(page.locator(".wf-run-deletion-toolbar")).toHaveCount(0);

  const requestCountBeforeTyping = await readRunRequests(page).then(requests => requests.length);
  await openMoreFilters(page);
  await page.getByLabel("Workflow run execution id").fill(executionId);
  expect(await readRunRequests(page)).toHaveLength(requestCountBeforeTyping);
  await page.getByRole("button", { name: /More filters/ }).click();
  await page.getByRole("button", { name: /More filters/ }).click();
  await expect(page.getByLabel("Workflow run execution id")).toHaveValue(executionId);
  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(inspectRun(page, executionId)).toBeVisible();
  await expect(page.getByText(`Execution ID: ${executionId}`)).toBeVisible();

  await page.getByRole("button", { name: "Remove Execution ID filter" }).click();
  await expect(page.getByText("Showing 14 of 14 matching runs")).toBeVisible();
  await openMoreFilters(page);
  await page.getByLabel("Correlation").fill("corr-08");
  await page.getByLabel("From").fill("2026-10-20T00:00");
  await page.getByLabel("To").fill("2026-10-20T23:59");
  await page.getByRole("button", { name: "Apply filters" }).click();
  await expect(page.getByText("Showing 1 of 1 matching runs")).toBeVisible();
  await expect(inspectRun(page, "run-08")).toBeVisible();
  const dateRequest = (await readRunRequests(page))
    .map(path => new URL(path, "http://golden-runs.fixture"))
    .find(url => url.searchParams.get("correlationId") === "corr-08");
  expect(dateRequest?.searchParams.get("from")).toBeTruthy();
  expect(dateRequest?.searchParams.get("to")).toBeTruthy();
  await page.getByRole("button", { name: "Remove Correlation filter" }).click();
  await expect(page.getByText("Showing 14 of 14 matching runs")).toBeVisible();
  await page.getByRole("button", { name: "Clear filters" }).click();
  await expect(page.getByText("Showing 14 of 14 matching runs")).toBeVisible();

  const selectableRow = runRow(page, executionId);
  await selectableRow.getByRole("checkbox", { name: `Select workflow run ${executionId}` }).check();
  await expect(page.getByRole("button", { name: "Delete selected" })).toBeVisible();

  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await selectableRow.getByRole("button", { name: "Copy Execution ID" }).click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(executionId);
  await expect(page).toHaveURL(/\/workflows\/instances(?:\?|$)/);

  const workflowSearch = page.getByRole("combobox", { name: "Workflow", exact: true });
  await workflowSearch.fill("Billing workflow");
  const listboxId = await workflowSearch.getAttribute("aria-controls");
  expect(listboxId).toBeTruthy();
  const options = page.locator(`#${listboxId}`).getByRole("option");
  await expect(options).toHaveCount(2);
  await expect(options.nth(0)).toContainText("billing-definition-current");
  await expect(options.nth(1)).toContainText("billing-definition-legacy");
  await workflowSearch.press("ArrowDown");
  await workflowSearch.press("ArrowDown");
  await workflowSearch.press("Enter");
  await expect(page).toHaveURL(/definitionId=billing-definition-legacy/);
  await expect(page.getByText("Showing 6 of 6 matching runs")).toBeVisible();
  await expect(page.getByLabel("Workflow definition ID: billing-definition-legacy")).toBeVisible();
  await expectPrimaryFiltersAligned(page);
  await expect(page.locator(".wf-run-deletion-toolbar")).toHaveCount(0);
});

test("detail restores the filtered second page through Runs and browser Back", async ({ page }) => {
  await openRuns(page);
  await page.getByLabel("Workflow run page size").selectOption("10");
  await page.getByRole("button", { name: "Needs attention" }).click();
  await expect(page.getByText("Showing 10 of 11 matching runs")).toBeVisible();
  await page.getByRole("button", { name: "Next workflow run page" }).click();
  await expect(inspectRun(page, "run-12")).toBeVisible();
  await inspectRun(page, "run-12").click();
  await expect(page.locator(".wf-instance-canvas-shell h3")).toBeVisible();
  await page.getByRole("tab", { name: "Details" }).click();
  await expect(page.locator(".wf-instance-meta")).toContainText("corr-12");
  await page.getByRole("tab", { name: /^Issues/ }).click();
  await expect(page.locator(".wf-instance-incident")).toContainText("Fixture incident for run-12");
  await expect(page.locator(".wf-instance-incident")).toContainText("Non-blocking · Active");

  await page.goBack();
  await expect(page).toHaveURL(/incidentHealth=active/);
  await expect(page).toHaveURL(/cursor=golden-runs-page-2/);
  await expect(inspectRun(page, "run-12")).toBeVisible();

  await inspectRun(page, "run-12").click();
  await expect(page.locator(".wf-instance-canvas-shell h3")).toBeVisible();
  const returnTo = new URL(page.url()).searchParams.get("returnTo");
  expect(returnTo).toBeTruthy();
  const returnUrl = new URL(returnTo!, "http://golden-runs.fixture");
  expect(returnUrl.pathname).toBe("/workflows/instances");
  expect(returnUrl.searchParams.get("incidentHealth")).toBe("active");
  expect(returnUrl.searchParams.get("cursor")).toBe("golden-runs-page-2");
  expect(returnUrl.searchParams.get("pageSize")).toBe("10");

  await page.getByRole("button", { name: "Runs", exact: true }).click();
  await expect(page).toHaveURL(/incidentHealth=active/);
  await expect(page).toHaveURL(/cursor=golden-runs-page-2/);
  await expect(inspectRun(page, "run-12")).toBeVisible();
  await page.getByRole("button", { name: "Clear filters" }).click();
  await inspectRun(page, "run-01").click();
  await expect(page.locator(".wf-instance-canvas-shell h3")).toBeVisible();
  await page.getByRole("tab", { name: /^Issues/ }).click();
  await expect(page.locator(".wf-instance-incident")).toContainText("Fixture incident for run-01");
  await expect(page.locator(".wf-instance-incident")).toContainText("Needs intervention · Active");

  await page.goto("/golden-runs.html?directRun=run-01");
  await expect(page.locator(".wf-instance-canvas-shell h3")).toBeVisible();
  await expect(page.getByRole("button", { name: "Runs", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Runs", exact: true }).click();
  await expect(page).toHaveURL(/\/workflows\/instances(?:\?|$)/);
  await expect(page).not.toHaveURL(/returnTo=/);
});

test("long run identities remain copyable and keyboard inspection works at laptop widths in both modes", async ({ page }) => {
  await openRuns(page);
  const executionId = "run-14-quarterly-reconciliation-2026-10-20-long-execution-reference";
  const identifiers = [
    ["Execution ID", executionId],
    ["Artifact ID", "artifact-quarterly-regional-reconciliation-release-2026-10-20"],
    ["Definition ID", "definition-quarterly-regional-reconciliation-with-a-long-stable-identity"],
    ["Definition version ID", "billing-definition-very-long-version-2026-10-20-regional-partner-settlement-v14"]
  ] as const;
  const targetRow = runRow(page, executionId);

  for (const mode of ["light", "dark"] as const) {
    for (const width of [1280, 1440]) {
      await page.evaluate(value => localStorage.setItem("elsa-studio-theme-mode", value), mode);
      await page.setViewportSize({ width, height: 800 });
      await page.goto("/golden-runs.html");
      await expect(page.locator("html")).toHaveAttribute("data-theme-mode", mode);
      await expect(page.getByRole("row", { name: /run-01/ })).toBeVisible();
      await page.getByLabel("Workflow run page size").selectOption("50");
      await expect(page.getByRole("button", { name: /Inspect workflow run run-14-quarterly-reconciliation/ })).toBeVisible();
      await expect(page.getByRole("button", { name: "Copy Definition version ID" }).first()).toBeVisible();
      await expectIdentifierValues(targetRow, identifiers);
      expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)).toBe(false);
    }
  }

  for (const width of [840, 540]) {
    await page.setViewportSize({ width, height: 800 });
    expect(await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth)).toBe(false);
    await expect(targetRow.getByRole("button", { name: `Delete workflow run ${executionId}` })).toBeVisible();
    await expectIdentifierValues(targetRow, identifiers);
  }

  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await targetRow.getByRole("button", { name: "Copy Execution ID" }).click();
  await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(executionId);
  await targetRow.focus();
  await targetRow.press("Enter");
  await expect(page.locator(".wf-instance-canvas-shell h3")).toBeVisible();
});

test("loading, empty, unsupported and failed states stay distinct and failure can retry", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/golden-runs.html?fixtureDelay=1&fixtureError=1");
  await expect(page.getByRole("status", { name: "Loading workflow runs" })).toBeVisible();
  await expect(page.getByRole("alert")).toContainText("Fixture runtime request failed");
  await page.getByRole("button", { name: "Retry" }).click();
  await expect(inspectRun(page, "run-01")).toBeVisible();

  await page.goto("/golden-runs.html?workflowExecutionId=missing-run");
  await expect(page.getByText("No workflow runs match these filters")).toBeVisible();

  await page.goto("/golden-runs.html?health=unsupported");
  const healthButton = page.getByRole("button", { name: "Needs attention" });
  await expect(healthButton).toBeDisabled();
  await expect(page.locator(`#${await healthButton.getAttribute("aria-describedby")}`)).toContainText("not available from this host");

  await page.addScriptTag({ content: axe.source });
  const violations = await page.evaluate(async () => {
    const filterSurface = document.querySelector(".wf-run-filters");
    if (!filterSurface) throw new Error("Workflow run filters were not rendered");
    const result = await (window as Window & {
      axe: { run: (root: Element, options: object) => Promise<{ violations: Array<{ id: string; impact: string | null }> }> };
    }).axe.run(filterSurface, { resultTypes: ["violations"] });
    return result.violations.filter(violation => violation.impact === "critical" || violation.impact === "serious");
  });
  expect(violations).toEqual([]);
});

async function openRuns(page: Page) {
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto("/golden-runs.html");
  await expect(page.getByRole("heading", { name: "Runs" })).toBeVisible();
  await expect(page.getByRole("row", { name: /run-01/ })).toBeVisible();
}

async function openMoreFilters(page: Page) {
  const disclosure = page.getByRole("button", { name: /More filters/ });
  if (await disclosure.getAttribute("aria-expanded") !== "true") await disclosure.click();
}

function inspectRun(page: Page, executionId: string) {
  const escapedId = executionId.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return page.getByRole("button", { name: new RegExp(`^Inspect workflow run ${escapedId} · `) });
}

function runRow(page: Page, executionId: string) {
  return page.locator(".wf-grid-row").filter({
    has: inspectRun(page, executionId)
  });
}

async function expectIdentifierValues(row: Locator, identifiers: readonly (readonly [string, string])[]) {
  for (const [label, value] of identifiers) {
    const visibleId = row.getByLabel(`${label}: ${value}`);
    await expect(visibleId).toBeVisible();
    expect((await visibleId.boundingBox())?.width ?? 0).toBeGreaterThan(0);
    await expect(row.getByRole("button", { name: `Copy ${label}` })).toBeVisible();
  }
}

async function expectPrimaryFiltersAligned(page: Page) {
  // Measure one rendered frame: row-name hydration can move the scroll anchor between separate boundingBox calls.
  await expect.poll(() => page.locator(".wf-run-filters > .wf-toolbar").evaluate(toolbar => {
    const input = toolbar.querySelector<HTMLInputElement>('input[role="combobox"]');
    if (!input) return Number.POSITIVE_INFINITY;
    const inputBounds = input.getBoundingClientRect();
    const inputCenter = inputBounds.y + inputBounds.height / 2;
    const buttons = [...toolbar.querySelectorAll("button")];
    return Math.max(...["Refresh", "Needs attention", "Running", "Clear filters"].map(name => {
      const button = buttons.find(candidate => candidate.textContent?.trim() === name);
      if (!button) return Number.POSITIVE_INFINITY;
      const bounds = button.getBoundingClientRect();
      return Math.abs(bounds.y + bounds.height / 2 - inputCenter);
    }));
  })).toBeLessThanOrEqual(2);
}

async function readRunRequests(page: Page) {
  return page.evaluate(() => (window as Window & { __goldenRunRequests?: string[] }).__goldenRunRequests ?? []);
}
