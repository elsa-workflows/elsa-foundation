import { expect, test, type Page, type Locator } from "@playwright/test";

const longLabel = "Message to include when notifying the customer about their completed order";
const shellNavigationSpace = ".activity-duplicate-editor { margin-left: 236px; width: calc(100vw - 236px); } .activity-duplicate-editor .wf-editor { width: 100%; }";

async function expectInsideViewport(locator: Locator, page: Page) {
  await expect(locator).toBeVisible();
  await expect.poll(async () => {
    const bounds = await locator.boundingBox();
    return !!bounds && bounds.width > 0 && bounds.x >= 0
      && bounds.x + bounds.width <= page.viewportSize()!.width + 1;
  }).toBe(true);
}

for (const host of ["workflow", "activity"]) for (const style of ["legacy", "quiet", "focus", "open"]) {
  test(`${host} ${style} expanded property editor covers panel handles`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.addInitScript(({ host, style }) => {
      const prefix = host === "workflow" ? "elsa-studio-workflow" : "elsa-studio-activity-graph";
      localStorage.setItem(`${prefix}-palette-width`, "460");
      localStorage.setItem(`${prefix}-inspector-width`, "560");
      if (style !== "legacy") localStorage.setItem("elsa-studio-workspace-style", style);
    }, { host, style });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=sequence&palette-theme=classic&appearance=dark`);
    await page.locator('.wf-sequence-step[data-id="write-one"]').press("Enter");
    const panelColor = await page.locator(".wf-inspector").evaluate(element => getComputedStyle(element).backgroundColor);
    await page.getByRole("button", { name: "Open expanded Text editor", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Text", exact: true });
    await expect(dialog).toBeVisible();
    await expect(dialog).toHaveCSS("background-color", panelColor);
    await expect(dialog.locator(".cm-content")).toContainText("buyer.email");
    const hits = await dialog.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return [...document.querySelectorAll<HTMLElement>(".wf-side-resize-handle")]
        .filter(handle => handle.getClientRects().length > 0)
        .map(handle => {
          const rect = handle.getBoundingClientRect();
          const x = rect.left + rect.width / 2;
          const y = Math.max(rect.top + 1, Math.min(bounds.top + bounds.height / 2, rect.bottom - 1));
          const hit = document.elementFromPoint(x, y);
          const insideDialog = x >= bounds.left && x <= bounds.right && y >= bounds.top && y <= bounds.bottom;
          return { insideDialog, covered: insideDialog ? element.contains(hit) : !!hit?.closest(".wf-property-editor-backdrop") };
        });
    });
    expect(hits.length).toBeGreaterThan(0);
    expect(hits.some(hit => hit.insideDialog)).toBe(true);
    expect(hits.every(hit => hit.covered)).toBe(true);
    await dialog.getByRole("button", { name: "Text expression syntax", exact: true }).click();
    const syntaxOption = page.getByRole("option", { name: "JavaScript", exact: true });
    await expect(syntaxOption).toBeVisible();
    expect(await syntaxOption.evaluate(element => {
      const rect = element.getBoundingClientRect();
      return element.contains(document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2));
    })).toBe(true);
    await syntaxOption.click();
    await page.screenshot({ path: testInfo.outputPath("expanded-property-layering.png") });
    const editor = dialog.locator('.cm-content[contenteditable="true"]');
    await editor.press("End");
    await editor.pressSequentially(" + '.updated'");
    await dialog.getByRole("button", { name: "Close", exact: true }).focus();
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    await page.getByRole("button", { name: "Open expanded Text editor", exact: true }).click();
    await expect(dialog.locator(".cm-content")).toContainText(".updated");
    await dialog.getByRole("button", { name: "Close", exact: true }).click();
    await expect(dialog).toHaveCount(0);
  });
}

for (const host of ["workflow", "activity"] as const) {
  for (const theme of ["light", "dark"] as const) {
    for (const saved of [false, true]) {
      test(`${host} ${theme} ${saved ? "maximum saved" : "fresh"} panels remain reachable and restore preferences`, async ({ page }, testInfo) => {
        const prefix = host === "workflow" ? "elsa-studio-workflow" : "elsa-studio-activity-graph";
        const preferred = saved ? [460, 560] : [260, 320];
        await page.addInitScript(({ prefix, saved }) => {
          if (saved) {
            localStorage.setItem(`${prefix}-palette-width`, "460");
            localStorage.setItem(`${prefix}-inspector-width`, "560");
          }
        }, { prefix, saved });
        await page.setViewportSize({ width: 1440, height: 900 });
        await page.goto(`/?mode=activity-duplicate-editor&composition=sequence&host=${host}&theme=${theme}&long-label`);
        // Reserve the same 236px shell-navigation space as the reported defect. The fixture
        // otherwise renders the real authoring surface at full viewport width without a shell.
        await page.addStyleTag({ content: shellNavigationSpace });
        const inspector = page.locator(".wf-inspector");
        const palette = page.locator(".wf-palette");
        const firstStep = page.locator('.wf-sequence-step[data-id="write-one"]');
        await expect(firstStep).toBeVisible();
        await firstStep.press("Enter");
        await expect(inspector.getByText(longLabel, { exact: true })).toBeVisible();

        for (const viewport of [{ width: 1440, height: 900 }, { width: 1280, height: 800 }, { width: 1440, height: 900 }]) {
          await page.setViewportSize(viewport);
          await expectInsideViewport(inspector, page);
          await expectInsideViewport(palette, page);
          await expectInsideViewport(inspector.getByRole("button", { name: "Maximize inspector panel" }), page);
          await expectInsideViewport(inspector.getByText(longLabel, { exact: true }), page);
          await expect.poll(async () => (await page.locator(".wf-canvas-shell").boundingBox())!.width).toBeGreaterThanOrEqual(360);
          const editor = inspector.locator("[data-studio-code-editor]");
          await expectInsideViewport(editor, page);
          await editor.click();
          const text = editor.locator(".cm-content");
          await expect(text).toBeVisible({ timeout: 15_000 });
          await text.focus();
          await expect(text).toBeFocused();
          await expectInsideViewport(text, page);
          await page.keyboard.press("ArrowRight");
          await expect(text).toBeFocused();
          const resize = page.getByRole("separator", { name: "Resize inspector panel" });
          await resize.focus();
          await expect(resize).toBeFocused();
          await expect.poll(async () => Number(await resize.getAttribute("aria-valuenow")))
            .toBeCloseTo((await inspector.boundingBox())!.width, 0);
        }

        await page.setViewportSize({ width: 1280, height: 800 });
        await page.getByRole("button", { name: "Open Body, 1 activity", exact: true }).press("Enter");
        await page.locator('.wf-sequence-step[data-id="nested"]').press("Enter");
        await expect(inspector.getByText(longLabel, { exact: true })).toBeVisible();
        await expectInsideViewport(inspector, page);
        const screenshot = testInfo.outputPath("nested-inspector-1280.png");
        await page.screenshot({ path: screenshot });
        await testInfo.attach("nested-inspector-1280", { path: screenshot, contentType: "image/png" });

        await page.setViewportSize({ width: 1920, height: 1080 });
        await expect.poll(async () => Math.round((await palette.boundingBox())!.width)).toBe(preferred[0]);
        await expect.poll(async () => Math.round((await inspector.boundingBox())!.width)).toBe(preferred[1]);
        expect(await page.evaluate(prefix => [localStorage.getItem(`${prefix}-palette-width`), localStorage.getItem(`${prefix}-inspector-width`)], prefix))
          .toEqual(preferred.map(String));
      });
    }
  }
}

test("compact editor switches panels with the keyboard without overwriting saved collapse preferences", async ({ page }) => {
  await page.setViewportSize({ width: 500, height: 800 });
  await page.goto("/?mode=activity-duplicate-editor&composition=sequence&theme=light");
  const expandPalette = page.getByRole("button", { name: "Expand activities panel" });
  await expectInsideViewport(expandPalette, page);
  await expandPalette.press("Enter");
  await expect(page.getByRole("searchbox", { name: "Search activity palette", exact: true })).toBeVisible();
  await page.setViewportSize({ width: 700, height: 800 });
  const paletteResize = page.getByRole("separator", { name: "Resize activities panel" });
  await expect(paletteResize).toHaveAttribute("aria-valuenow", "260");
  const paletteWidth = Number(await paletteResize.getAttribute("aria-valuenow"));
  await paletteResize.press("ArrowLeft");
  await expect(page.getByRole("searchbox", { name: "Search activity palette", exact: true })).toBeVisible();
  await expect(paletteResize).toHaveAttribute("aria-valuenow", String(paletteWidth - 16));
  await page.setViewportSize({ width: 500, height: 800 });
  const expandInspector = page.getByRole("button", { name: "Expand inspector panel" });
  await expectInsideViewport(expandInspector, page);
  await expandInspector.press("Enter");
  await expect(page.getByRole("button", { name: "Collapse inspector panel" })).toBeVisible();
  // A phone-width inspector is a sheet over the whole workbench, so it offers no resize handle.
  await expect(page.getByRole("separator", { name: "Resize inspector panel" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Back to canvas", exact: true })).toBeVisible();
  await expectInsideViewport(page.locator(".wf-inspector"), page);
  await page.setViewportSize({ width: 1440, height: 900 });
  await expect(page.getByRole("button", { name: "Collapse activities panel" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Collapse inspector panel" })).toBeVisible();
  expect(await page.evaluate(() => [localStorage.getItem("elsa-studio-workflow-palette-collapsed"), localStorage.getItem("elsa-studio-workflow-inspector-collapsed")]))
    .toEqual(["false", "false"]);
});

test("editorial workflow uses the available space when the palette is absent", async ({ page }) => {
  await page.addInitScript(() => {
    localStorage.setItem("elsa-studio-workflow-palette-width", "460");
    localStorage.setItem("elsa-studio-workflow-inspector-width", "560");
  });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/?mode=activity-duplicate-editor&composition=sequence&theme=light");
  await page.addStyleTag({ content: shellNavigationSpace });
  await page.evaluate(() => document.documentElement.setAttribute("data-theme-layout", "editorial"));
  await expect(page.locator(".wf-palette")).toHaveCount(0);
  await expect.poll(async () => Math.round((await page.locator(".wf-inspector").boundingBox())!.width)).toBe(560);
  await expectInsideViewport(page.locator(".wf-inspector"), page);
});

test("Test Run opens its inspector panel while the compact palette is selected", async ({ page }) => {
  await page.setViewportSize({ width: 700, height: 800 });
  await page.goto("/?mode=activity-duplicate-editor&composition=sequence&theme=light");
  await page.getByRole("button", { name: "Expand activities panel" }).press("Enter");
  await expect(page.getByRole("button", { name: "Expand inspector panel" })).toBeVisible();
  await page.getByRole("button", { name: "Test Run", exact: true }).press("Enter");
  await expect(page.getByRole("button", { name: "Collapse inspector panel" })).toBeVisible();
  await expect(page.getByRole("tab", { name: "Test Run", exact: true })).toHaveAttribute("aria-selected", "true");
  await expectInsideViewport(page.locator(".wf-inspector"), page);
});

// #4695: at phone widths an open inspector left the canvas a 30-50px sliver, and a floating palette
// rail covered nodes and the node toolbar. The inspector is now a full-width sheet with a way back,
// and the rails dock beside the canvas in every layout.
for (const width of [360, 390]) for (const layout of ["classic", "floating"]) for (const [host, composition] of [["workflow", "flowchart"], ["workflow", "sequence"], ["activity", "sequence"]] as const) {
  test(`${host} ${composition} ${layout} ${width}px inspector is a sheet and the canvas stays clear of the rails`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 800 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=${composition}&theme=light`);
    await page.evaluate(value => document.documentElement.setAttribute("data-theme-layout", value), layout);
    const canvas = page.locator(".wf-canvas-shell");
    const inspector = page.locator(".wf-inspector");
    const nodes = page.locator(".wf-canvas .react-flow__node, .wf-canvas-shell .wf-sequence-step");
    await expect(nodes.first()).toBeVisible();

    const clearOfRails = async () => page.evaluate(() => {
      const box = (selector: string) => document.querySelector(selector)!.getBoundingClientRect();
      const canvasBox = box(".wf-canvas-shell");
      const paletteBox = box(".wf-palette");
      const inspectorBox = box(".wf-inspector");
      const nodeHits = [...document.querySelectorAll<HTMLElement>(".wf-canvas .react-flow__node, .wf-canvas-shell .wf-sequence-step")].map(node => {
        const rect = node.getBoundingClientRect();
        const x = Math.max(rect.left + 4, canvasBox.left + 4);
        const y = rect.top + rect.height / 2;
        return x > canvasBox.right ? true : node.contains(document.elementFromPoint(x, y));
      });
      return {
        canvasWidth: Math.round(canvasBox.width),
        paletteClear: paletteBox.right <= canvasBox.left + 1,
        inspectorClear: inspectorBox.left >= canvasBox.right - 1,
        nodesReachable: nodeHits.every(Boolean)
      };
    });

    const docked = await clearOfRails();
    expect(docked).toMatchObject({ paletteClear: true, inspectorClear: true, nodesReachable: true });
    expect(docked.canvasWidth).toBeGreaterThanOrEqual(width - 2 * 42 - 2 * 10);
    await page.screenshot({ path: testInfo.outputPath("canvas.png") });

    await page.getByRole("button", { name: "Expand inspector panel", exact: true }).click();
    const back = page.getByRole("button", { name: "Back to canvas", exact: true });
    await expect(back).toBeVisible();
    await expect(canvas).toBeHidden();
    await expect(page.locator(".wf-palette")).toBeHidden();
    await expect.poll(async () => Math.round((await inspector.boundingBox())!.width)).toBeGreaterThanOrEqual(width - 1);
    await expectInsideViewport(inspector, page);
    await page.screenshot({ path: testInfo.outputPath("inspector-sheet.png") });

    await back.click();
    await expect(canvas).toBeVisible();
    await expect(page.getByRole("button", { name: "Expand inspector panel", exact: true })).toBeVisible();
    await expect.poll(clearOfRails).toMatchObject({ paletteClear: true, inspectorClear: true, nodesReachable: true });
    await nodes.first().click();
    await expect(nodes.first()).toHaveAttribute(composition === "flowchart" ? "class" : "aria-pressed", composition === "flowchart" ? /selected/ : "true");
  });
}
