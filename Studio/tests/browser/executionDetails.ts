import type { Locator, Page } from "@playwright/test";

/** Activity input evidence lives in the Inputs tab of the activity execution details (#4903). */
export async function openExecutionInputs(scope: Page | Locator) {
  const runData = scope.getByRole("tab", { name: "Run data", exact: true });
  if (await runData.count() && await runData.getAttribute("aria-selected") !== "true") await runData.click();
  const historyViews = scope.getByRole("tablist", { name: "Activity execution views", exact: true });
  if (await historyViews.count()) await historyViews.getByRole("tab", { name: "Data", exact: true }).click();
  await scope.getByRole("tablist", { name: "Activity execution details", exact: true }).getByRole("tab", { name: "Inputs", exact: true }).click();
}

/** The editor inspector keeps run evidence on its Run data tab (#4903). */
export async function openRunData(page: Page) {
  const inspector = page.getByRole("tab", { name: "Inspector", exact: true });
  if (await inspector.count() && await inspector.getAttribute("aria-selected") !== "true") await inspector.click();
  const runData = page.getByRole("tab", { name: "Run data", exact: true });
  if (await runData.count() && await runData.getAttribute("aria-selected") !== "true") await runData.click();
}

/** Activity output evidence lives in the Outputs tab of the activity execution details (#4903). */
export async function openExecutionOutputs(scope: Page | Locator) {
  const historyViews = scope.getByRole("tablist", { name: "Activity execution views", exact: true });
  if (await historyViews.count()) await historyViews.getByRole("tab", { name: "Data", exact: true }).click();
  await scope.getByRole("tablist", { name: "Activity execution details", exact: true }).getByRole("tab", { name: "Outputs", exact: true }).click();
}
