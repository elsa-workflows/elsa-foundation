import { expect, test } from "@playwright/test";
import type { ActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

test("root If slots preserve siblings, nested edits, breadcrumbs and history", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&root-slots=empty&theme=dark");
  const designer = page.getByRole("main", { name: "Activity Graph designer", exact: true });
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await page.locator(".wf-slot-row > button").filter({ hasText: "Else" }).click();
  await expect(designer.getByRole("group", { name: "Fill Else" })).toBeVisible();
  await designer.getByRole("button", { name: "Browse all activities…", exact: true }).click();
  await page.getByRole("option", { name: /Sequence/ }).click();
  await expect(designer.getByLabel("Sequence canvas", { exact: true })).toBeVisible();
  await expect(designer.getByRole("button", { name: "If / Else", exact: true })).toBeVisible();
  await designer.getByRole("button", { name: "Add the first step", exact: true }).click();
  await page.getByRole("option", { name: /Write Line/ }).click();
  await expect(designer.locator(".wf-sequence-step")).toHaveCount(1);
  const payload = async () => (JSON.parse((await page.getByTestId("activity-graph-payload").textContent())!) as { rootActivity: ActivityNode }).rootActivity.structure!.payload;
  expect((await payload()).then).toBeNull();
  const otherwise = (await payload()).else as ActivityNode;
  expect(otherwise.structure!.payload.activities).toHaveLength(1);
  await page.getByRole("button", { name: "Undo Activity Graph edit", exact: true }).click();
  await expect(designer.locator(".wf-sequence-step")).toHaveCount(0);
  await page.getByRole("button", { name: "Redo Activity Graph edit", exact: true }).click();
  await expect(designer.locator(".wf-sequence-step")).toHaveCount(1);
  await page.screenshot({ path: testInfo.outputPath("root-if-else-sequence.png") });
  await designer.getByRole("button", { name: "Root", exact: true }).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await page.getByRole("button", { name: "Choose Then activity", exact: true }).click();
  await page.getByRole("option", { name: /Write Line/ }).click();
  await expect(page.locator(".wf-inspector-context h3")).toHaveText("Write Line");
  expect((await payload()).else).toEqual(otherwise);
  await designer.getByRole("button", { name: "Root", exact: true }).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await page.locator(".wf-slot-row > button").filter({ hasText: "Else" }).click();
  await expect(designer.locator(".wf-sequence-step")).toHaveCount(1);
});


for (const theme of ["light", "dark"]) {
  test(`Workflow root secondary slot opens its container and retains sibling ownership (${theme})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&composition=sequence&root-slots=filled&theme=${theme}`);
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await page.locator(".wf-slot-row > button").filter({ hasText: "Else" }).click();
    await expect(page.getByLabel("Sequence canvas", { exact: true })).toBeVisible();
    await expect(page.locator(".wf-sequence-step")).toHaveCount(1);
    await expect(page.locator(".wf-breadcrumb")).toHaveText("RootIf / Else");
    await page.getByRole("button", { name: "1. Write Line", exact: true }).click();
    await page.getByRole("button", { name: "Delete Write Line", exact: true }).click();
    await expect(page.locator(".wf-sequence-step")).toHaveCount(0);
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await expect(page.locator(".wf-sequence-step")).toHaveCount(1);
    await page.getByRole("button", { name: "Root", exact: true }).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await page.locator(".wf-slot-row > button").filter({ hasText: "Then" }).click();
    await expect(page.locator(".wf-inspector-context h3")).toHaveText("Write Line");
    await expect(page.locator(".react-flow__node[data-id='then-sibling']")).toBeVisible();
    await page.getByRole("button", { name: "Root", exact: true }).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await page.locator(".wf-slot-row > button").filter({ hasText: "Else" }).click();
    await expect(page.locator(".wf-sequence-step[data-id='else-leaf']")).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath(`workflow-root-else-${theme}.png`) });
  });
}

test("read-only Activity Graph diagnostic navigates and focuses a secondary root-slot descendant", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&root-slots=filled&read-only&theme=light");
  const before = await page.getByTestId("activity-graph-payload").textContent();
  await page.getByRole("button", { name: "Focus Else diagnostic", exact: true }).click();
  await expect(page.getByTestId("diagnostic-result")).toHaveText("focused");
  await expect(page.locator(".wf-sequence-step[data-id='else-leaf']")).toBeFocused();
  await expect(page.locator(".wf-breadcrumb")).toHaveText("RootIf / Else");
  expect(await page.getByTestId("activity-graph-payload").textContent()).toBe(before);
  await expect(page.getByRole("button", { name: "Undo Activity Graph edit", exact: true })).toBeDisabled();
});
