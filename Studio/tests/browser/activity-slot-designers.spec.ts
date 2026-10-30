import { expect, test } from "@playwright/test";
import type { ActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

for (const placement of ["root", "nested"] as const) {
  test(`Activity Definition opens the Sequence designer assigned to While.Body (${placement})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1600, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=activity&composition=sequence&slot-assignment=${placement}&theme=dark`);
    if (placement === "nested") await page.getByRole("button", { name: "1. While", exact: true }).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await page.getByRole("button", { name: "Choose Body activity", exact: true }).click();
    await page.getByRole("option", { name: /Sequence/ }).click();

    const designer = page.getByRole("main", { name: "Activity Graph designer", exact: true });
    await expect(designer.getByLabel("Sequence canvas", { exact: true })).toBeVisible();
    await expect(designer.getByRole("button", { name: "While / Body", exact: true })).toBeVisible();
    await expect(designer.locator("[data-graph-node-id]")).toHaveCount(0);
    await designer.getByRole("button", { name: "Add the first step", exact: true }).click();
    await page.getByRole("option", { name: /Write Line/ }).click();
    await expect(designer.locator(".wf-sequence-step")).toHaveCount(1);
    await expect(designer.locator(".wf-sequence-step")).toContainText("Write Line");
    const payload = JSON.parse((await page.getByTestId("activity-graph-payload").textContent())!) as { rootActivity: ActivityNode };
    const loop = placement === "root" ? payload.rootActivity : (payload.rootActivity.structure!.payload.activities as ActivityNode[])[0];
    const body = loop.structure!.payload.body as ActivityNode;
    expect(body.activityVersionId).toBe("activity-sequence-v1");
    expect(body.structure!.payload.activities).toEqual([expect.objectContaining({ activityVersionId: "activity-write-line-v1" })]);

    await page.getByRole("button", { name: "Undo Activity Graph edit", exact: true }).click();
    await expect(designer.locator(".wf-sequence-step")).toHaveCount(0);
    await page.getByRole("button", { name: "Redo Activity Graph edit", exact: true }).click();
    await expect(designer.locator(".wf-sequence-step")).toHaveCount(1);
    await page.screenshot({ path: testInfo.outputPath("while-body-sequence-designer.png") });

    await designer.getByRole("button", { name: "Root", exact: true }).click();
    if (placement === "nested") await page.getByRole("button", { name: "1. While", exact: true }).click();
    await page.getByRole("tab", { name: "Slots", exact: true }).click();
    await page.locator(".wf-slot-row > button").first().click();
    await expect(designer.getByLabel("Sequence canvas", { exact: true })).toBeVisible();
    await expect(designer.locator(".wf-sequence-step")).toHaveCount(1);
  });
}

test("empty Activity Definition slots open containers chosen through Browse all", async ({ page }) => {
  await page.setViewportSize({ width: 1600, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&host=activity&composition=sequence&slot-assignment=nested&theme=dark");
  await page.getByRole("button", { name: "1. While", exact: true }).click();
  await page.getByRole("tab", { name: "Slots", exact: true }).click();
  await page.locator(".wf-slot-row > button").first().click();
  await page.getByRole("button", { name: "Browse all activities…", exact: true }).click();
  await page.getByRole("option", { name: /Sequence/ }).click();
  const designer = page.getByRole("main", { name: "Activity Graph designer", exact: true });
  await expect(designer.getByLabel("Sequence canvas", { exact: true })).toBeVisible();
  await expect(designer.locator("[data-graph-node-id]")).toHaveCount(0);
  await designer.getByRole("button", { name: "Add the first step", exact: true }).click();
  await page.getByRole("option", { name: /Write Line/ }).click();
  await expect(designer.locator(".wf-sequence-step")).toHaveCount(1);
});
