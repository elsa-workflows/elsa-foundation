import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"] as const) {
 for (const width of [1280, 360]) {
  test(`explains an unavailable HTTP starter without changing the blank selection (${theme}, ${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await page.goto(`/?mode=workflow-creation-readiness&scenario=http-missing&theme=${theme}`);
    await page.getByRole("button", { name: "Open create workflow" }).click();

    const dialog = page.getByRole("dialog", { name: "Create Workflow" });
    const startingPoint = dialog.getByRole("combobox", { name: "Starting point" });
    await expect(dialog).toBeVisible();
    await expect(startingPoint).toHaveValue("blank");
    await expect(startingPoint.locator('option[value="http"]')).toHaveAttribute("disabled", "");
    await expect(startingPoint).toHaveAttribute("aria-describedby", "wf-http-starter-unavailable");
    const reason = dialog.locator("#wf-http-starter-unavailable");
    await expect(reason).toContainText("HTTP Endpoint is not present in the activity catalog");
    await expect(reason).toContainText("Write HTTP Response is not present in the activity catalog");

    await dialog.getByRole("textbox", { name: "Display name" }).fill("Blank workflow");
    await expect(dialog.getByRole("button", { name: "Create" })).toHaveAttribute("aria-disabled", "false");
    const bounds = await dialog.boundingBox();
    expect(bounds!.y).toBeGreaterThanOrEqual(0);
    expect(bounds!.y + bounds!.height).toBeLessThanOrEqual(800);
    const cancel = dialog.getByRole("button", { name: "Cancel" });
    await cancel.scrollIntoViewIfNeeded();
    await expect(cancel).toBeInViewport();
    await cancel.click();
    await expect(page.getByRole("button", { name: "Open create workflow" })).toBeFocused();
  });
 }
}

test("associates a concrete catalog availability reason with a disabled root choice", async ({ page }) => {
  await page.goto("/?mode=workflow-creation-readiness&scenario=root-unavailable");
  await page.getByRole("button", { name: "Open create workflow" }).click();

  const dialog = page.getByRole("dialog", { name: "Create Workflow" });
  const flowchart = dialog.getByRole("radio", { name: "Flowchart" });
  const flowchartReason = dialog.locator("#wf-root-availability-flowchart");
  await expect(flowchartReason).toHaveText("The selected engine does not expose Flowchart right now.");
  await expect(flowchart).toBeDisabled();
  await expect(flowchart).toHaveAttribute("aria-describedby", "wf-root-availability-flowchart");
  await expect(dialog.getByRole("radio", { name: "Sequence" })).toBeDisabled();

  await dialog.getByRole("textbox", { name: "Display name" }).fill("Unavailable root workflow");
  const description = dialog.getByRole("textbox", { name: "Description" });
  const startingPoint = dialog.getByRole("combobox", { name: "Starting point" });
  const cancel = dialog.getByRole("button", { name: "Cancel" });
  const create = dialog.getByRole("button", { name: "Create" });
  await description.focus();
  await page.keyboard.press("Tab");
  await expect(startingPoint).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(cancel).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(create).toBeFocused();
  await expect(create).toHaveAttribute("aria-disabled", "true");
  await expect(create).toHaveAttribute("aria-describedby", "wf-root-create-readiness");
  await page.keyboard.press("Enter");
  await expect(page.getByRole("status")).toHaveCount(0);
  await expect(dialog).toBeVisible();
});

test("recovers a failed activity catalog and creates with the explicitly selected current root version", async ({ page }) => {
  await page.goto("/?mode=workflow-creation-readiness&scenario=catalog-failed");
  await page.getByRole("button", { name: "Open create workflow" }).click();

  const dialog = page.getByRole("dialog", { name: "Create Workflow" });
  await expect(dialog.getByRole("alert")).toContainText("The activity catalog could not be loaded.");
  await expect(dialog.getByRole("combobox", { name: "Starting point" })).toHaveAccessibleDescription("The activity catalog could not be loaded. Retry");
  await dialog.getByRole("button", { name: "Retry" }).click();
  const sequence = dialog.getByRole("radio", { name: "Sequence" });
  await expect(sequence).toBeEnabled();

  await dialog.getByText("Sequence", { exact: true }).click();
  await expect(sequence).toBeChecked();
  await dialog.getByRole("textbox", { name: "Display name" }).fill("Selected root workflow");
  await dialog.getByRole("button", { name: "Create" }).click();

  await expect(page.getByRole("status")).toHaveText("Created root version: sequence-v7");
  await expect(page.getByRole("dialog")).toHaveCount(0);
});

test("explains pending catalog readiness on keyboard activation without reporting unavailable choices", async ({ page }) => {
  await page.goto("/?mode=workflow-creation-readiness&scenario=catalog-loading");
  await page.getByRole("button", { name: "Open create workflow" }).click();
  const dialog = page.getByRole("dialog", { name: "Create Workflow" });
  await dialog.getByRole("textbox", { name: "Display name" }).fill("Pending catalog");
  const create = dialog.getByRole("button", { name: "Create" });
  await create.focus();
  await expect(create).toHaveAttribute("aria-disabled", "true");
  await expect(create).toHaveAccessibleDescription("Loading available starting activities…");
  await expect(dialog.getByRole("combobox", { name: "Starting point" })).toHaveAccessibleDescription("Loading available starting activities…");
  await expect(dialog.locator("#wf-http-starter-unavailable, .wf-root-card-unavailable")).toHaveCount(0);
  await page.keyboard.press("Enter");
  await page.keyboard.press("Space");
  await expect(dialog).toBeVisible();
  await expect(page.getByText("Created root version:", { exact: false })).toHaveCount(0);
  await page.keyboard.press("Escape");
  await expect(page.getByRole("button", { name: "Open create workflow" })).toBeFocused();
});

for (const width of [1280, 360]) {
  test(`keeps a failed create visible and preserves the draft for retry (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 800 });
    await page.goto("/?mode=workflow-creation-readiness&scenario=create-failed");
    await page.getByRole("button", { name: "Open create workflow" }).click();
    const dialog = page.getByRole("dialog", { name: "Create Workflow" });
    await dialog.getByRole("textbox", { name: "Display name" }).fill("Retry this workflow");
    await dialog.getByText("Sequence", { exact: true }).click();
    await dialog.getByRole("button", { name: "Create", exact: true }).click();
    const alert = dialog.getByRole("alert");
    await expect(alert).toHaveText("The workflow could not be created. Try again.");
    await expect(alert).toBeFocused();
    await expect(alert).toBeInViewport();
    await expect(dialog.getByRole("textbox", { name: "Display name" })).toHaveValue("Retry this workflow");
    await expect(dialog.getByRole("radio", { name: "Sequence" })).toBeChecked();
    await page.keyboard.press("Shift+Tab");
    await expect(dialog.getByRole("radio", { name: "Sequence" })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(dialog.getByRole("button", { name: "Cancel" })).toBeFocused();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Enter");
    await expect(page.getByRole("status")).toHaveText("Created root version: sequence-v7");
  });
}
