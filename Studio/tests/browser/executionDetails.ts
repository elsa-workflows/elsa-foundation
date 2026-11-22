import { expect, type Locator, type Page } from "@playwright/test";

/** Wait for the actual inspector surface before deciding which nested data tab to open. */
async function openExecutionData(scope: Page | Locator) {
  const runData = scope.getByRole("tab", { name: "Run data", exact: true });
  const historyViews = scope.getByRole("tablist", { name: "Activity execution views", exact: true });
  const details = scope.getByRole("tablist", { name: "Activity execution details", exact: true });
  await expect(runData.or(historyViews).or(details).first()).toBeVisible();
  if (await runData.isVisible() && await runData.getAttribute("aria-selected") !== "true") await runData.click();
  await expect(historyViews.or(details).first()).toBeVisible();
  if (await historyViews.isVisible()) await historyViews.getByRole("tab", { name: "Data", exact: true }).click();
  await expect(details).toBeVisible();
  return details;
}

/** Activity input data lives in the Inputs tab of the activity execution details (#4903). */
export async function openExecutionInputs(scope: Page | Locator) {
  await (await openExecutionData(scope)).getByRole("tab", { name: "Inputs", exact: true }).click();
}

/** The editor inspector keeps run data on its Run data tab (#4903). */
export async function openRunData(page: Page) {
  const inspector = page.getByRole("tab", { name: "Inspector", exact: true });
  if (await inspector.count() && await inspector.getAttribute("aria-selected") !== "true") await inspector.click();
  const runData = page.getByRole("tab", { name: "Run data", exact: true });
  if (await runData.count() && await runData.getAttribute("aria-selected") !== "true") await runData.click();
}

/** Activity output evidence lives in the Outputs tab of the activity execution details (#4903). */
export async function openExecutionOutputs(scope: Page | Locator) {
  await (await openExecutionData(scope)).getByRole("tab", { name: "Outputs", exact: true }).click();
}
