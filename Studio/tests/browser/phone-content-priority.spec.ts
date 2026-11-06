import { expect, test } from "@playwright/test";

for (const host of ["workflow", "activity"]) for (const theme of ["light", "dark"]) for (const saved of [false, true]) {
  test(`${host} ${theme} ${saved ? "saved widths" : "fresh"} phone canvas and explicit tools retain preferences`, async ({ page }, info) => {
    const prefix = host === "workflow" ? "elsa-studio-workflow" : "elsa-studio-activity-graph";
    await page.addInitScript(({ prefix, saved }) => {
      if (saved) { localStorage.setItem(`${prefix}-palette-width`, "460"); localStorage.setItem(`${prefix}-inspector-width`, "560"); }
    }, { prefix, saved });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/?mode=activity-duplicate-editor&composition=sequence&host=${host}&theme=${theme}&long-label`);
    const canvas = page.locator(".wf-canvas-shell");
    await expect(page.getByRole("button", { name: "Expand activities panel", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Expand inspector panel", exact: true })).toBeVisible();
    await expect.poll(async () => (await canvas.boundingBox())!.width).toBeGreaterThanOrEqual(260);
    await page.getByRole("button", { name: "Expand inspector panel", exact: true }).focus();
    await page.setViewportSize({ width: 400, height: 844 });
    await expect(page.getByRole("button", { name: "Expand inspector panel", exact: true })).toBeVisible();
    await page.setViewportSize({ width: 390, height: 844 });
    await page.reload();
    await expect.poll(async () => (await canvas.boundingBox())!.width).toBeGreaterThanOrEqual(260);
    await page.getByRole("button", { name: "Expand inspector panel", exact: true }).press("Enter");
    await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Maximize inspector panel", exact: true }).click();
    await expect(page.getByRole("button", { name: "Restore inspector panel", exact: true })).toBeVisible();
    await page.keyboard.press("Escape");
    await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
    await expect.poll(async () => (await canvas.boundingBox())!.width).toBeGreaterThanOrEqual(260);
    await page.getByRole("button", { name: "Expand activities panel", exact: true }).press("Enter");
    const search = page.getByRole("searchbox", { name: "Search activity palette", exact: true });
    await search.fill("Write");
    await page.getByRole("button", { name: "Collapse activities panel", exact: true }).click();
    await page.getByRole("button", { name: "Expand activities panel", exact: true }).click();
    await expect(search).toHaveValue("Write");
    await page.getByRole("button", { name: "Collapse activities panel", exact: true }).click();
    await page.screenshot({ path: info.outputPath("phone-canvas.png") });
    for (const width of [768, 1280]) {
      await page.setViewportSize({ width, height: 900 });
      await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
      await expect.poll(async () => (await canvas.boundingBox())!.width).toBeGreaterThanOrEqual(260);
    }
    await page.locator('.wf-sequence-step[data-id="write-one"]').press("Enter");
    await page.locator(".wf-inspector [data-studio-code-editor]").first().click();
    const text = page.locator('.wf-inspector .cm-content[contenteditable="true"]').first();
    await text.focus();
    await text.press("End"); await text.pressSequentially(' + ".phone"');
    await page.setViewportSize({ width: 390, height: 844 });
    await expect(text).toBeFocused(); await expect(text).toContainText(".phone");
    await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
    expect(await page.evaluate(prefix => [localStorage.getItem(`${prefix}-palette-width`), localStorage.getItem(`${prefix}-inspector-width`), localStorage.getItem(`${prefix}-palette-collapsed`), localStorage.getItem(`${prefix}-inspector-collapsed`)], prefix)).toEqual(saved ? ["460", "560", "false", "false"] : ["260", "320", "false", "false"]);
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

for (const appearance of ["light", "dark"]) {
  test(`${appearance} Settings section selector keeps current content near the top and carries navigation focus`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`/settings/appearance?mode=settings&appearance=${appearance}`);
    await page.getByRole("navigation", { name: "Settings sections", exact: true }).getByRole("link", { name: "Appearance", exact: true }).focus();
    await page.setViewportSize({ width: 390, height: 844 });
    const select = page.getByRole("combobox", { name: "Settings section", exact: true });
    await expect(select).toBeFocused();
    await expect(page.getByRole("navigation", { name: "Settings sections", exact: true })).toHaveCount(0);
    expect((await page.getByRole("heading", { name: "Appearance", exact: true }).boundingBox())!.y).toBeLessThan(700);
    await select.selectOption({ label: "Modules" });
    await expect(page).toHaveURL(/\/settings\/modules/);
    await expect(page.getByRole("heading", { name: "Modules", exact: true })).toBeVisible();
    for (const width of [768, 1280]) {
      await select.focus(); await page.setViewportSize({ width, height: 900 });
      if (width === 1280) await expect(page.getByRole("navigation", { name: "Settings sections", exact: true }).getByRole("link", { name: "Modules", exact: true })).toBeFocused();
    }
    await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  });
}

for (const theme of ["light", "dark"]) {
  test(`${theme} inline authoring without an Activities pane also starts on the phone canvas`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`/?mode=activity-duplicate-editor&composition=sequence&theme=${theme}`);
    await page.evaluate(() => document.documentElement.setAttribute("data-theme-layout", "editorial"));
    await expect(page.locator(".wf-palette")).toHaveCount(0);
    const canvas = page.locator(".wf-canvas-shell");
    await expect.poll(async () => (await canvas.boundingBox())!.width).toBeGreaterThanOrEqual(260);
    const open = page.getByRole("button", { name: "Expand inspector panel", exact: true });
    await open.focus(); await page.setViewportSize({ width: 400, height: 844 }); await expect(open).toBeVisible();
    await open.press("Enter");
    await expect(page.getByRole("button", { name: "Collapse inspector panel", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
    await expect.poll(async () => (await canvas.boundingBox())!.width).toBeGreaterThanOrEqual(260);
    await open.focus(); await page.setViewportSize({ width: 390, height: 844 }); await expect(open).toBeVisible();
  });
}
