import { expect, test } from "@playwright/test";

for (const host of ["workflow", "activity"]) for (const width of [360, 390]) for (const mode of ["light", "dark"]) {
  test(`${host} block commands stay inside the phone canvas at ${width} ${mode}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=sequence&palette-theme=classic&appearance=${mode}`);
    await page.locator('.wf-sequence-step[data-id="write-one"]').press("Enter");
    await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
    await page.setViewportSize({ width, height: 900 });
    const expandInspector = page.getByRole("button", { name: "Expand inspector panel", exact: true });
    await expect(expandInspector).toBeVisible();
    await expandInspector.click();
    await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
    const canvas = page.locator(".wf-canvas-shell");
    const commands = canvas.getByRole("group", { name: "Named block commands", exact: true });
    // A phone-width inspector is a sheet (#4695): the canvas and its commands wait behind it rather
    // than being squeezed beside it, so no command can paint into the active inspector.
    await expect(canvas).toBeHidden();
    await expect(commands).toBeHidden();
    await expect(page.getByRole("button", { name: "Back to canvas", exact: true })).toBeVisible();
    await page.screenshot({ path: info.outputPath("inspector-sheet.png"), fullPage: true });

    await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
    const palette = page.getByRole("button", { name: "Collapse activities panel", exact: true });
    if (await palette.isVisible()) await palette.click();
    const create = commands.getByRole("button", { name: "Create Sequence block", exact: true });
    await expect(create).toBeVisible();
    // This isolated activity fixture has no resource candidate-validation endpoint.
    // Its commands must stay unavailable; editable activity drafts are covered on the composed host.
    if (host === "activity") {
      await expect(create).toBeDisabled();
      await page.screenshot({ path: info.outputPath("canvas-returned.png"), fullPage: true });
      return;
    }
    await expect(create).toBeEnabled();
    await create.focus();
    await create.press("Enter");
    const dialog = page.getByRole("dialog", { name: "Create Sequence block", exact: true });
    await expect(dialog).toBeVisible();
    await dialog.getByRole("textbox", { name: "Name", exact: true }).press("Escape");
    await expect(dialog).toHaveCount(0);
    await expect(create).toBeFocused();
    await page.screenshot({ path: info.outputPath("canvas-returned.png"), fullPage: true });
  });
}

for (const appearance of ["light", "dark"]) {
  test(`workflow BPMN history preserves element selection in ${appearance}`, async ({ page }, info) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=workflow&composition=bpmn&palette-context&bpmn-decision=legacy&palette-theme=classic&appearance=${appearance}`);
    const element = page.locator('.react-flow__node[data-id="decision-element"]');
    await element.click();
    const routing = page.getByRole("region", { name: "BPMN routing", exact: true });
    await routing.getByLabel("Gateway type", { exact: true }).selectOption("exclusiveGateway");
    await expect(element.locator(".wf-bpmn-gateway-marker")).toHaveText("×");
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await expect(element.locator(".wf-bpmn-task")).toBeVisible();
    await expect(element).toHaveAttribute("aria-pressed", "true");
    await expect(routing.getByLabel("Gateway type", { exact: true })).toHaveValue("task");
    await page.getByRole("button", { name: "Redo", exact: true }).click();
    await expect(element.locator(".wf-bpmn-gateway-marker")).toHaveText("×");
    await expect(element).toHaveAttribute("aria-pressed", "true");
    await expect(routing.getByLabel("Gateway type", { exact: true })).toHaveValue("exclusiveGateway");
    await page.screenshot({ path: info.outputPath("bpmn-restored-selection.png"), fullPage: true });
  });
}
