import { expect, test, type Locator } from "@playwright/test";

const statusValues = ["Autosaving...", "Autosaved", "Saving before review...", "Published to an unusually long publication channel", ""];

for (const width of [320, 768, 1024, 1440]) {
  test(`keeps workflow actions stationary through save status changes at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 720 });
    await page.goto("/?mode=workflow-toolbar&theme=dark&name=A%20long%20workflow%20definition%20name%20that%20must%20not%20crowd%20the%20actions");
    const toolbar = page.locator(".wf-editor-toolbar");
    const buttons = toolbar.getByRole("button");
    const initial = await readButtonPositions(buttons);
    const initialToolbar = await toolbar.boundingBox();
    const initialStatus = await toolbar.getByRole("status").boundingBox();
    const title = toolbar.locator("strong[title]");
    const badge = toolbar.getByText("Draft", { exact: true });
    const titleRow = toolbar.locator(".wf-editor-title");
    const definitions = toolbar.getByRole("button", { name: "Definitions", exact: true });
    const breadcrumbChevron = toolbar.locator(".wf-editor-identity > svg");
    const save = toolbar.getByRole("button", { name: "Save", exact: true });
    const canvasTools = toolbar.locator(".wf-canvas-tools");
    const initialTitle = await title.boundingBox();
    const initialBadge = await badge.boundingBox();
    const initialTitleRow = await titleRow.boundingBox();
    const initialDefinitions = await definitions.boundingBox();
    const initialChevron = await breadcrumbChevron.boundingBox();
    const initialSave = await save.boundingBox();
    const initialCanvasTools = await canvasTools.boundingBox();
    expect(initialTitleRow!.height).toBeGreaterThanOrEqual(32);
    expect(Math.abs(centerY(initialTitle!) - centerY(initialDefinitions!))).toBeLessThan(1);
    expect(Math.abs(centerY(initialTitle!) - centerY(initialChevron!))).toBeLessThan(1);
    expect(initialBadge!.x).toBeGreaterThanOrEqual(initialTitle!.x + initialTitle!.width);
    expect(Math.abs(initialBadge!.y + initialBadge!.height / 2 - initialTitle!.y - initialTitle!.height / 2)).toBeLessThan(1);
    expect(initialBadge!.x + initialBadge!.width).toBeLessThanOrEqual(width);
    if (width >= 768) {
      expect(Math.abs(centerY(initialCanvasTools!) - centerY(initialSave!))).toBeLessThan(1);
    }
    if (width >= 1024) {
      expect(Math.abs(centerY(initialTitle!) - centerY(initialSave!))).toBeLessThan(1);
    } else if (width === 768) {
      expect(initialTitleRow!.y + initialTitleRow!.height).toBeLessThanOrEqual(initialSave!.y);
    }
    await expect(title).toHaveAttribute("title", "A long workflow definition name that must not crowd the actions");
    expect(initial.every(box => box.x >= 0 && box.x + box.width <= width)).toBe(true);

    for (const value of statusValues) {
      await page.getByLabel("Save status").selectOption(value);
      await expect(toolbar.getByRole("status")).toHaveText(value);
      expect(await toolbar.getByRole("status").boundingBox()).toEqual(initialStatus);
      expect(await readButtonPositions(buttons)).toEqual(initial);
      expect(await title.boundingBox()).toEqual(initialTitle);
      expect(await badge.boundingBox()).toEqual(initialBadge);
      expect(await titleRow.boundingBox()).toEqual(initialTitleRow);
      expect(await definitions.boundingBox()).toEqual(initialDefinitions);
      expect(await breadcrumbChevron.boundingBox()).toEqual(initialChevron);
    }
    await expect(toolbar.getByRole("button", { name: "Review and Publish" })).toBeVisible();
    if (width === 768) {
      await toolbar.getByRole("button", { name: "Publish options", exact: true }).click();
      await page.getByRole("menuitemradio", { name: "Publish", exact: true }).click();
      await expect(toolbar.getByRole("button", { name: "Publish", exact: true })).toBeVisible();
      expect((await toolbar.boundingBox())!.height).toBe(initialToolbar!.height);
      expect(await titleRow.boundingBox()).toEqual(initialTitleRow);
    }
  });
}

test("makes secondary actions keyboard accessible and preserves autosave state", async ({ page }) => {
  await page.goto("/?mode=workflow-toolbar");
  const trigger = page.getByRole("button", { name: "More workflow actions" });
  await trigger.focus();
  await trigger.press("ArrowDown");
  const menu = page.getByRole("menu", { name: "More workflow actions" });
  const autosave = menu.getByRole("menuitemcheckbox", { name: /Autosave/ });
  await expect(autosave).toBeFocused();
  await expect(autosave).toHaveAttribute("aria-checked", "true");
  await autosave.press("Space");
  await expect(autosave).toHaveAttribute("aria-checked", "false");
  await autosave.press("ArrowDown");
  await expect(menu.getByRole("menuitem", { name: "Propose update" })).toBeFocused();
  await expect(menu.getByRole("menuitem", { name: "Review risks" })).toBeDisabled();
  await expect(menu.getByRole("menuitem", { name: "Export artifact" })).toBeDisabled();
  await expect(menu.getByRole("menuitem", { name: "Import BPMN" })).toBeVisible();
  await menu.getByRole("menuitem", { name: "Propose update" }).press("ArrowDown");
  await expect(menu.getByRole("menuitem", { name: "Export JSON" })).toBeFocused();
  await menu.getByRole("menuitem", { name: "Export JSON" }).press("Enter");
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await expect(page.getByText("Selected action: Export JSON")).toBeVisible();

  await trigger.click();
  await expect(autosave).toHaveAttribute("aria-checked", "false");
  await autosave.press("Escape");
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();

  await trigger.click();
  await autosave.press("Tab");
  await expect(menu).toHaveCount(0);
  await expect(page.getByLabel("Save status")).toBeFocused();
  await trigger.press("ArrowUp");
  await expect(menu.getByRole("menuitem", { name: "Export BPMN" })).toBeFocused();
  await menu.getByRole("menuitem", { name: "Export BPMN" }).press("Shift+Tab");
  await expect(menu).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Run", exact: true })).toBeFocused();
  await trigger.click();
  await page.getByLabel("Save status").click();
  await expect(menu).toHaveCount(0);
});

function readButtonPositions(buttons: Locator) {
  return buttons.evaluateAll(elements => elements.map(element => {
    const { x, y, width, height } = element.getBoundingClientRect();
    return { x, y, width, height };
  }));
}

function centerY(box: { y: number; height: number }) {
  return box.y + box.height / 2;
}

test("keeps the overflow menu inside a narrow viewport and primary commands direct", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 600 });
  await page.goto("/?mode=workflow-toolbar&theme=dark");
  const toolbar = page.locator(".wf-editor-toolbar");
  for (const name of ["Save", "Review and Publish", "Run"]) {
    await toolbar.getByRole("button", { name, exact: true }).click();
    await expect(page.getByText(`Selected action: ${name}`, { exact: true })).toBeVisible();
  }
  await toolbar.getByRole("button", { name: "More workflow actions" }).click();
  const menu = page.getByRole("menu", { name: "More workflow actions" });
  await expect(menu).toBeVisible();
  const box = await menu.boundingBox();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(320);
  expect(box!.y + box!.height).toBeLessThanOrEqual(600);
});

test("supports keyboard publish-mode selection and repeats the remembered mode at narrow widths", async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 600 });
  await page.goto("/?mode=workflow-toolbar&theme=dark");
  const toolbar = page.locator(".wf-editor-toolbar");
  const trigger = toolbar.getByRole("button", { name: "Publish options", exact: true });

  await trigger.focus();
  await trigger.press("ArrowDown");
  const menu = page.getByRole("menu", { name: "Publish action" });
  const review = menu.getByRole("menuitemradio", { name: "Review and Publish", exact: true });
  const publish = menu.getByRole("menuitemradio", { name: "Publish", exact: true });
  await expect(review).toBeFocused();
  await expect(review).toHaveAttribute("aria-checked", "true");
  await review.press("ArrowDown");
  await expect(publish).toBeFocused();
  await publish.press("Enter");
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await expect(page.getByText("Selected action: Publish", { exact: true })).toBeVisible();

  const rememberedPrimary = toolbar.getByRole("button", { name: "Publish", exact: true });
  await expect(rememberedPrimary).toBeVisible();
  await rememberedPrimary.click();
  await expect(page.getByText("Selected action: Publish", { exact: true })).toBeVisible();

  await trigger.click();
  await expect(publish).toHaveAttribute("aria-checked", "true");
  const box = await menu.boundingBox();
  expect(box).not.toBeNull();
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(320);
  expect(box!.y).toBeGreaterThanOrEqual(0);
  expect(box!.y + box!.height).toBeLessThanOrEqual(600);
  await publish.press("Escape");
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();
});

for (const theme of ["light", "dark"]) {
  for (const width of [320, 768, 1024, 1440]) {
    test(`keeps primary authoring actions stationary through Test Run status changes at ${width}px in ${theme}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width, height: 900 });
      await page.goto(`/?mode=workflow-toolbar&theme=${theme}`);
      const toolbar = page.locator(".wf-editor-toolbar");
      // Select the primary publish action structurally while its approved wording is under test elsewhere.
      const primary = toolbar.getByRole("button").filter({ hasText: /^(Review and Publish|Review & publish)$/ });
      const actions = [toolbar.locator(".wf-save-button"), primary, toolbar.locator(".wf-run-button")];
      const initial = await Promise.all(actions.map(action => action.boundingBox()));
      expect(initial.every(Boolean)).toBe(true);
      const observations = [];
      for (const state of ["accepted", "rejected", "incidents", "runtime-issue", "stale", ""]) {
        await page.getByLabel("Test Run status", { exact: true }).selectOption(state);
        const status = toolbar.locator(".wf-test-run-trigger");
        if (state) {
          await expect(status).toBeVisible();
          await expect(status).toHaveText(state === "rejected" ? /Test run rejected/i : state === "incidents" ? /Test run accepted with incidents/i : state === "runtime-issue" ? /Test run accepted with a runtime issue/i : state === "stale" ? /Earlier draft · Test run dispatched/i : /Test run dispatched/i);
          await status.focus();
          await status.press("Enter");
          await expect(page.getByText("Selected action: Runtime details", { exact: true })).toBeVisible();
        } else await expect(status).toHaveCount(0);
        for (const saveStatus of statusValues) {
          await page.getByLabel("Save status", { exact: true }).selectOption(saveStatus);
          await expect(toolbar.locator(".wf-editor-save-status")).toHaveText(saveStatus);
          const boxes = await Promise.all(actions.map(action => action.boundingBox()));
          observations.push({ state, saveStatus, boxes });
          for (let index = 0; index < actions.length; index++) {
            await expect(actions[index]).toBeVisible();
            expect(boxes[index]!.x).toBeGreaterThanOrEqual(0);
            expect(boxes[index]!.x + boxes[index]!.width).toBeLessThanOrEqual(width);
          }
        }
      }
      await testInfo.attach("authoring-status-geometry", { body: JSON.stringify({ initial, observations }), contentType: "application/json" });
      for (const { boxes } of observations) for (let index = 0; index < boxes.length; index++) {
        expect(Math.abs(boxes[index]!.x - initial[index]!.x)).toBeLessThanOrEqual(1);
        expect(Math.abs(boxes[index]!.y - initial[index]!.y)).toBeLessThanOrEqual(1);
      }
    });
  }
}
