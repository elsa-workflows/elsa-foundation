import { expect, test } from "@playwright/test";

for (const width of [390, 1440]) {
  test(`opens, saves, tests and reviews an Agent revision in one workspace at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 1100 });
    await page.goto("/?mode=activity-authoring&provider=agent&agent-workspace");

    const editor = page.getByTestId("agent-editor");
    const instructions = editor.getByRole("textbox", { name: /^Instructions/ });
    await expect(page).toHaveURL(/\/agents\/workspace\/quote-activity\/drafts\/quote-draft/);
    await expect(instructions).toBeVisible();
    await expect(page.locator(".ad-workbench-header")).toContainText("Agents");
    await instructions.fill("Proofread the supplied text and return concise suggestions.");
    await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved");

    await page.reload();
    await expect(editor.getByRole("textbox", { name: /^Instructions/ })).toHaveValue("Proofread the supplied text and return concise suggestions.");
    await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved");

    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    const playground = page.locator(".agent-playground--embedded");
    await expect(playground).toBeVisible();
    await expect(playground).toContainText("Saved revision 2");
    if (width < 800) {
      await expect(page.getByRole("tab", { name: "Test Run", exact: true })).toHaveAttribute("aria-selected", "true");
      await expect(page.getByRole("tab", { name: "Instructions", exact: true })).toBeVisible();
    }
    await playground.locator("textarea").fill("A sample paragraph to proofread.");
    await playground.getByRole("button", { name: "Run", exact: true }).click();
    const result = page.getByRole("region", { name: "Run result" });
    await expect(result).toHaveAttribute("data-status", "Completed");
    await expect(result).toContainText("The model's result was accepted.");
    const request = await page.evaluate(() => JSON.parse(localStorage.getItem("activity-authoring-2994-fixture:agent:agent-workspace:outcome-only:test-run-request") ?? "null"));
    expect(request.expectedRevision).toBe(2);
    expect(request.inputs.text.value).toBe("A sample paragraph to proofread.");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath("agent-workspace-test-run.png"), fullPage: true });

    await playground.getByRole("button", { name: "Close Test Run", exact: true }).click();
    await page.locator(".wf-publish-primary").click();
    const review = page.getByRole("dialog", { name: "Review & publish", exact: true });
    await expect(review).toBeVisible();
    await expect(review).toContainText(/Exact draft revision\s*2/);
    await expect(review).toContainText("Ready to publish");
    await page.screenshot({ path: testInfo.outputPath("agent-workspace-publication-review.png"), fullPage: true });
  });
}

test("keeps authoring available when the host does not expose Test Run operations", async ({ page }) => {
  await page.goto("/?mode=activity-authoring&provider=agent&agent-workspace&no-test-run");
  await expect(page.getByTestId("agent-editor")).toBeVisible();
  await expect(page.getByRole("button", { name: "Test Run", exact: true })).toHaveCount(0);
  await expect(page.locator(".wf-publish-primary")).toBeVisible();
});

for (const width of [390, 1440]) {
  test(`opens an Agent's current draft from its single collection row at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1100 });
    await page.goto("/?mode=activity-authoring&provider=agent&agents-collection");

    const table = page.getByRole("table");
    const row = page.locator(".studio-data-grid-row").filter({ hasText: "Proofreader" });
    await expect(table.getByRole("columnheader", { name: "Latest version" })).toBeVisible();
    await expect(table.getByRole("columnheader", { name: "Published version" })).toBeVisible();
    await expect(row).toHaveCount(1);
    await expect(row).toContainText("1 draft in this definition");
    await expect(row).toContainText("Published 1.0.0");
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    if (width < 800) expect(await page.locator(".studio-data-grid").evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);

    await row.click();
    await expect(page).toHaveURL(/\/agents\/workspace\/quote-activity\/drafts\/quote-draft/);
    await expect(page.getByTestId("agent-editor").getByRole("textbox", { name: /^Instructions/ })).toBeVisible();
  });
}