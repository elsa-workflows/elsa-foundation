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
  const resize = page.getByRole("separator", { name: "Resize inspector panel" });
  await expect(resize).toHaveAttribute("aria-valuemin", "260");
  expect(Number(await resize.getAttribute("aria-valuenow"))).toBeGreaterThanOrEqual(Number(await resize.getAttribute("aria-valuemin")));
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
