import { expect, test } from "@playwright/test";

test("fills the template designer above the bottom dock and keeps scrolling inside its panes", async ({ page }) => {
  await page.setViewportSize({ width: 1550, height: 720 });
  await page.addInitScript(() => localStorage.setItem("elsa-studio-theme-mode", "dark"));
  await page.goto("/?mode=workflow-templates&theme=dark&bottom-panel");
  await expect(page.getByRole("tab", { name: "Console", exact: true })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Structured Logs", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "New template", exact: true }).click();
  const create = page.getByRole("dialog", { name: "Create template", exact: true });
  await create.getByRole("textbox", { name: "Display name" }).fill("Dock-sized template");
  await create.getByRole("radio", { name: "Sequence", exact: true }).focus();
  await page.keyboard.press("Space");
  await create.getByRole("button", { name: "Create template", exact: true }).click();

  const editor = page.locator(".wf-editor");
  const content = page.locator(".content");
  const dock = page.locator(".bottom-panel");
  await expect(editor).toBeVisible();
  const expectEditorAboveDock = async () => {
    const [editorBox, dockBox] = await Promise.all([editor.boundingBox(), dock.boundingBox()]);
    expect(editorBox).not.toBeNull();
    expect(dockBox).not.toBeNull();
    expect(Math.abs(editorBox!.y + editorBox!.height - dockBox!.y)).toBeLessThanOrEqual(1);
    expect(await content.evaluate(element => element.scrollHeight - element.clientHeight)).toBe(0);
  };
  await expectEditorAboveDock();

  await page.getByRole("button", { name: "Expand bottom panel", exact: true }).click();
  await expectEditorAboveDock();
  const palette = page.locator(".wf-palette-list");
  await expect.poll(() => palette.evaluate(element => element.scrollHeight - element.clientHeight)).toBeGreaterThan(0);
  const paletteBox = (await palette.boundingBox())!;
  await page.mouse.move(paletteBox.x + paletteBox.width / 2, paletteBox.y + paletteBox.height / 2);
  await page.mouse.wheel(0, 800);
  await expect.poll(() => palette.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  await expectEditorAboveDock();

  await page.getByRole("link", { name: "Templates", exact: true }).click();
  await expect(content.getByRole("heading", { name: "Workflow templates", exact: true })).toBeVisible();
  expect(parseFloat(await content.evaluate(element => getComputedStyle(element).paddingTop))).toBeGreaterThan(0);
  expect(await content.evaluate(element => getComputedStyle(element).overflowY)).toBe("auto");
});

for (const theme of ["light", "dark"]) {
  test(`creates, edits, copies, uses and deletes a persistent custom template (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1550, height: 1000 });
    await page.goto(`/?mode=workflow-templates&theme=${theme}`);
    await expect(page.locator(".content").getByRole("heading", { name: "Workflow templates", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "New template", exact: true }).click();
    const create = page.getByRole("dialog", { name: "Create template", exact: true });
    await create.getByRole("textbox", { name: "Display name" }).fill("Order starter");
    await create.getByRole("radio", { name: "Sequence", exact: true }).focus();
    await page.keyboard.press("Space");
    await expect(create.getByRole("radio", { name: "Sequence", exact: true })).toBeChecked();
    await create.getByRole("button", { name: "Create template", exact: true }).click();
    await expect(page.getByRole("link", { name: "Templates", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /Review & publish|Test Run/ })).toHaveCount(0);
    await expect(page.getByRole("tab", { name: "Test Run", exact: true })).toHaveCount(0);
    const views = page.getByRole("tablist", { name: "Editor view tabs" });
    await views.getByRole("tab", { name: "Variables", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Variables", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Add variable", exact: true }).click();
    await views.getByRole("tab", { name: "Inputs", exact: true }).click();
    await page.getByRole("button", { name: "Add input", exact: true }).click();
    await views.getByRole("tab", { name: "Outputs", exact: true }).click();
    await page.getByRole("button", { name: "Add output", exact: true }).click();
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByRole("status").filter({ hasText: "Saved" })).toBeVisible();
    await page.screenshot({ path: `/tmp/workflow-template-editor-${theme}.png` });
    await page.getByRole("link", { name: "Templates", exact: true }).click();
    const original = page.locator("article").filter({ has: page.getByRole("button", { name: "Order starter", exact: true }) });
    await expect(original).toBeVisible();
    await original.getByRole("button", { name: "Actions for Order starter", exact: true }).click();
    await page.getByRole("menuitem", { name: "Copy", exact: true }).click();
    await page.getByRole("dialog").getByRole("button", { name: "Create template", exact: true }).click();
    await expect(page.getByRole("link", { name: "Templates", exact: true })).toBeVisible();
    await page.getByRole("link", { name: "Templates", exact: true }).click();
    const copy = page.locator("article").filter({ has: page.getByRole("button", { name: "Order starter copy", exact: true }) });
    await expect(copy).toBeVisible();
    await copy.getByRole("button", { name: "Actions for Order starter copy", exact: true }).click();
    await page.getByRole("menuitem", { name: "Delete", exact: true }).click();
    await expect(copy).toHaveCount(0);
    await page.reload();
    await expect(original).toBeVisible();
    await expect(original.getByText("Custom", { exact: true })).toHaveCount(0);
    const starterSection = page.locator(".wf-template-starters-section");
    const startersToggle = starterSection.getByRole("button", { name: "Built-in starters (2)", exact: true });
    await expect(starterSection.locator("h2 + #workflow-template-starters")).toHaveCount(1);
    await expect(page.locator("#custom-templates-title").locator("xpath=../following-sibling::section[contains(@class, 'wf-template-starters-section')]")).toHaveCount(1);
    await expect(startersToggle).toHaveAttribute("aria-expanded", "false");
    await expect(page.locator("#workflow-template-starters")).toBeHidden();
    await startersToggle.click();
    const starters = page.getByRole("region", { name: "Built-in starters", exact: true });
    await expect(starters.getByRole("article")).toHaveCount(2);
    const builtIn = starters.locator("article").filter({ has: page.getByRole("heading", { name: "Blank workflow", exact: true }) });
    await expect(builtIn.getByRole("button", { name: "Edit", exact: true })).toHaveCount(0);
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

test("shows first-template onboarding and keeps built-in starters in a keyboard-accessible disclosure", async ({ page }) => {
  await page.setViewportSize({ width: 1550, height: 1000 });
  await page.goto("/?mode=workflow-templates");
  await expect(page.getByRole("heading", { name: "Create your first template", exact: true })).toBeVisible();
  const starterSection = page.locator(".wf-template-starters-section");
  const startersToggle = starterSection.getByRole("button", { name: "Built-in starters (2)", exact: true });
  await expect(starterSection.locator("h2 + #workflow-template-starters")).toHaveCount(1);
  await expect(page.locator("#first-template-title").locator("xpath=../following-sibling::section[contains(@class, 'wf-template-starters-section')]")).toHaveCount(1);
  await expect(startersToggle).toHaveAttribute("aria-expanded", "true");
  const starters = page.getByRole("region", { name: "Built-in starters", exact: true });
  await expect(starters.getByRole("article")).toHaveCount(2);
  await page.screenshot({ path: "/tmp/workflow-template-empty-catalog.png" });

  await startersToggle.focus();
  await page.keyboard.press("Space");
  await expect(startersToggle).toHaveAttribute("aria-expanded", "false");
  await expect(page.locator("#workflow-template-starters")).toBeHidden();
  await page.keyboard.press("Enter");
  await expect(startersToggle).toHaveAttribute("aria-expanded", "true");

  await page.getByRole("button", { name: "New template", exact: true }).click();
  const firstTemplate = page.getByRole("dialog", { name: "Create template", exact: true });
  await expect(firstTemplate).toBeVisible();
  await firstTemplate.getByRole("button", { name: "Cancel", exact: true }).click();

  await page.getByRole("searchbox", { name: "Search templates", exact: true }).fill("nothing matches this");
  await expect(page.getByRole("status").filter({ hasText: "No templates match" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Create your first template", exact: true })).toHaveCount(0);
  await expect(starters.getByRole("article")).toHaveCount(2);

  await page.getByRole("searchbox", { name: "Search templates", exact: true }).fill("");
  await starters.getByRole("button", { name: "Use", exact: true }).first().click();
  const useDialog = page.getByRole("dialog", { name: "New workflow", exact: true });
  await expect(useDialog).toBeVisible();
  await useDialog.getByRole("button", { name: "Cancel", exact: true }).click();

  await starters.getByRole("button", { name: "Copy", exact: true }).first().click();
  const copyDialog = page.getByRole("dialog", { name: "Create template", exact: true });
  await expect(copyDialog).toBeVisible();
  await copyDialog.getByRole("button", { name: "Cancel", exact: true }).click();
});

test("reopens a saved template for editing after a page reload", async ({ page }) => {
  await page.goto("/?mode=workflow-templates");
  await page.getByRole("button", { name: "New template", exact: true }).click();
  const create = page.getByRole("dialog", { name: "Create template", exact: true });
  await create.getByRole("textbox", { name: "Display name" }).fill("Persisted starter");
  await create.getByRole("radio", { name: "Sequence", exact: true }).focus();
  await page.keyboard.press("Space");
  await create.getByRole("button", { name: "Create template", exact: true }).click();
  await page.getByRole("link", { name: "Templates", exact: true }).click();
  const card = page.locator("article").filter({ has: page.getByRole("button", { name: "Persisted starter", exact: true }) });
  await expect(card).toBeVisible();

  await page.reload();
  const persistedCard = page.locator("article").filter({ has: page.getByRole("button", { name: "Persisted starter", exact: true }) });
  await expect(persistedCard).toBeVisible();
  await persistedCard.getByRole("button", { name: "Edit", exact: true }).click();
  await expect(page.getByRole("tablist", { name: "Editor view tabs" })).toBeVisible();
  await expect(page.getByRole("link", { name: "Templates", exact: true })).toBeVisible();
});

for (const theme of ["light", "dark"]) {
  test(`saves the live workflow as a template without creating a workflow publication (${theme})`, async ({ page }) => {
    await page.goto(`/?mode=workflow-templates&source-workflow=1&composition=sequence&theme=${theme}`);
    await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
    await page.getByRole("menuitem", { name: "Save as template", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Save as template", exact: true });
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill("Saved live workflow");
    await dialog.getByRole("button", { name: "Save template", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    const toast = page.getByRole("group", { name: "Template saved", exact: true });
    await expect(toast).toBeVisible();
    await expect(page.getByRole("button", { name: "More workflow actions", exact: true })).toBeFocused();
    await expect(toast.getByRole("button", { name: "View templates", exact: true })).toBeVisible();
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
  const views = page.getByRole("tablist", { name: "Editor view tabs" });
  await views.getByRole("tab", { name: "Variables", exact: true }).click();
  await page.getByRole("button", { name: "Add variable", exact: true }).click();
  await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeEnabled();
  await page.evaluate(() => { localStorage.setItem("template-fixture-fail-saves", "1"); localStorage.setItem("template-fixture-save-delay", "800"); });
  await views.getByRole("tab", { name: "Properties", exact: true }).click();
  await page.getByRole("textbox", { name: "Workflow name", exact: true }).fill("Keep my renamed template");
  await page.getByRole("tab", { name: "Designer", exact: true }).click();
  await page.getByRole("link", { name: "Templates", exact: true }).click();
  const editor = page.locator("[data-studio-workflow-editor]");
  await expect(editor).toHaveAttribute("inert", "");
  await expect(editor).toHaveAttribute("aria-busy", "true");
  await page.evaluate(() => window.dispatchEvent(new KeyboardEvent("keydown", { key: "z", ctrlKey: true, bubbles: true, cancelable: true })));
  await expect(page.getByRole("status", { name: "Couldn't save name/description.", exact: true })).toBeVisible();
  await expect(editor).not.toHaveAttribute("inert", "");
  await views.getByRole("tab", { name: "Variables", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Variable name", exact: true })).toHaveCount(1);
  await views.getByRole("tab", { name: "Properties", exact: true }).click();
  await expect(page.getByRole("textbox", { name: "Workflow name", exact: true })).toHaveValue("Keep my renamed template");
  await page.evaluate(() => localStorage.removeItem("template-fixture-fail-saves"));
  await page.getByRole("link", { name: "Templates", exact: true }).click();
  await expect(page.getByRole("button", { name: "Keep my renamed template", exact: true })).toBeVisible();
});
