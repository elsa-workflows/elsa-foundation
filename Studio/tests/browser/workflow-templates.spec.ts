import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  test(`creates, edits, copies, uses and deletes a persistent custom template (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1550, height: 1000 });
    await page.goto(`/?mode=workflow-templates&theme=${theme}`);
    await expect(page.getByRole("heading", { name: "Workflow templates", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "New template", exact: true }).click();
    const create = page.getByRole("dialog", { name: "Create template", exact: true });
    await create.getByRole("textbox", { name: "Display name" }).fill("Order starter");
    await create.getByRole("radio", { name: "Sequence", exact: true }).focus();
    await page.keyboard.press("Space");
    await expect(create.getByRole("radio", { name: "Sequence", exact: true })).toBeChecked();
    await create.getByRole("button", { name: "Create template", exact: true }).click();
    await expect(page.getByRole("button", { name: "Templates", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /Review & publish|Test Run/ })).toHaveCount(0);
    await expect(page.getByRole("tab", { name: "Test Run", exact: true })).toHaveCount(0);
    await page.getByRole("tab", { name: "Properties", exact: true }).click();
    await expect(page.getByText("Variables", { exact: true }).first()).toBeVisible();
    await page.getByRole("button", { name: "Add variable", exact: true }).click();
    await page.getByRole("button", { name: "Add input", exact: true }).click();
    await page.getByRole("button", { name: "Add output", exact: true }).click();
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByRole("status").filter({ hasText: "Saved" })).toBeVisible();
    await page.screenshot({ path: `/tmp/workflow-template-editor-${theme}.png` });
    await page.getByRole("button", { name: "Templates", exact: true }).click();
    const original = page.locator("article").filter({ has: page.getByRole("button", { name: "Order starter", exact: true }) });
    await expect(original).toBeVisible();
    await original.getByRole("button", { name: "Actions for Order starter", exact: true }).click();
    await page.getByRole("menuitem", { name: "Copy", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Create template", exact: true }).click();
    await expect(page.getByRole("button", { name: "Templates", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Templates", exact: true }).click();
    const copy = page.locator("article").filter({ has: page.getByRole("button", { name: "Order starter copy", exact: true }) });
    await expect(copy).toBeVisible();
    await copy.getByRole("button", { name: "Actions for Order starter copy", exact: true }).click();
    await page.getByRole("menuitem", { name: "Delete", exact: true }).click();
    await expect(copy).toHaveCount(0);
    await page.reload();
    await expect(original).toBeVisible();
    const builtIn = page.locator("article").filter({ has: page.getByRole("heading", { name: "Blank workflow", exact: true }) });
    await expect(builtIn.getByRole("button", { name: "Delete", exact: true })).toHaveCount(0);
    await page.screenshot({ path: `/tmp/workflow-template-catalog-${theme}.png` });
    await original.getByRole("button", { name: "Use template", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Create", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Workflow created", exact: true })).toBeVisible();
    const document = JSON.parse(await page.getByTestId("created-document").innerText());
    expect(document.initialState.rootActivity.structure.kind).toBe("elsa.sequence.structure");
    expect(document.initialState.variables).toHaveLength(1);
    expect(document.initialState.inputs).toHaveLength(1);
    expect(document.initialState.outputs).toHaveLength(1);
  });
}

for (const theme of ["light", "dark"]) {
  test(`saves the live workflow as a template without creating a workflow publication (${theme})`, async ({ page }) => {
    await page.goto(`/?mode=workflow-templates&source-workflow=1&composition=sequence&theme=${theme}`);
    await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
    await page.getByRole("menuitem", { name: "Save as template", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Save as template", exact: true });
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Saved live workflow");
    await dialog.getByRole("button", { name: "Save template", exact: true }).click();
    await expect(dialog.getByRole("status")).toContainText("Template saved");
    const done = dialog.getByRole("button", { name: "Done", exact: true });
    const actionLayout = await done.evaluate(button => {
      const actions = button.closest(".wf-dialog-actions");
      if (!actions) return null;
      const actionRect = actions.getBoundingClientRect();
      const buttonRect = button.getBoundingClientRect();
      const style = getComputedStyle(actions);
      return {
        display: style.display,
        justifyContent: style.justifyContent,
        actionWidth: actionRect.width,
        buttonWidth: buttonRect.width,
        rightGap: actionRect.right - buttonRect.right
      };
    });
    expect(actionLayout).not.toBeNull();
    expect(actionLayout?.display).toBe("flex");
    expect(actionLayout?.justifyContent).toBe("flex-end");
    expect(actionLayout?.buttonWidth).toBeLessThan(actionLayout?.actionWidth ?? 0);
    expect(actionLayout?.rightGap).toBeLessThan(1);
    const saved = await page.evaluate(() => JSON.parse(localStorage.getItem("workflow-template-fixture")!));
    expect(saved[0].content.state.rootActivity.structure.kind).toBe("elsa.sequence.structure");
    expect(saved[0].content.activityPresentation[0].displayName).toBe("Notify buyer");
  });
}


test("keeps a renamed template open when leaving would lose a failed metadata save", async ({ page }) => {
  await page.goto("/?mode=workflow-templates&theme=dark");
  await page.getByRole("button", { name: "New template", exact: true }).click();
  const create = page.getByRole("dialog");
  await create.getByRole("textbox", { name: "Display name" }).fill("Before rename");
  await create.getByRole("radio", { name: "Sequence", exact: true }).focus();
  await page.keyboard.press("Space");
  await create.getByRole("button", { name: "Create template", exact: true }).click();
  await page.getByRole("tab", { name: "Properties", exact: true }).click();
  await page.getByRole("button", { name: "Add variable", exact: true }).click();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await page.evaluate(() => { localStorage.setItem("template-fixture-fail-saves", "1"); localStorage.setItem("template-fixture-save-delay", "800"); });
  await page.getByRole("textbox", { name: "Workflow name", exact: true }).fill("Keep my renamed template");
  await page.getByRole("tab", { name: "Designer", exact: true }).click();
  await page.getByRole("button", { name: "Templates", exact: true }).click();
  const editor = page.locator("[data-studio-workflow-editor]");
  await expect(editor).toHaveAttribute("inert", "");
  await expect(editor).toHaveAttribute("aria-busy", "true");
  await page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true, bubbles: true, cancelable: true })));
  await expect(page.getByRole("status", { name: "Couldn't save name/description.", exact: true })).toBeVisible();
  await expect(editor).not.toHaveAttribute("inert", "");
  await page.getByRole("tab", { name: "Properties", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Variable name", exact: true })).toHaveCount(1);
  await expect(page.getByRole("textbox", { name: "Workflow name", exact: true })).toHaveValue("Keep my renamed template");
  await page.evaluate(() => localStorage.removeItem("template-fixture-fail-saves"));
  await page.getByRole("button", { name: "Templates", exact: true }).click();
  await expect(page.getByRole("button", { name: "Keep my renamed template", exact: true })).toBeVisible();
});
