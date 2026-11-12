import { expect, test, type Locator } from "@playwright/test";

// #4064: a run canvas's first view keeps User Task outcome labels at a readable rendered size
// (>= 10px). When the whole graph cannot fit legibly, it shows a readable part around the focus
// activity instead, and the Fit View control still gives the full overview.
// #4196: on a short phone-width pane the zoom controls must not cover an activity card either.
// "narrow-short" shrinks the window until the run canvas pane is as short as the composed journey's
// (about 245px at 360px wide), where the old vertical controls stack covered the first card's left edge.
const viewports: { name: string; width: number; height: number; paneHeight?: number }[] = [
  { name: "desktop", width: 1440, height: 1000 },
  { name: "narrow", width: 360, height: 900 },
  { name: "narrow-short", width: 360, height: 900, paneHeight: 245 }
];
const outcomes = ["Approve", "Reject", "Unmatched"];

const renderedFont = (label: Locator) => label.evaluate(element => {
  const viewport = element.closest(".react-flow__viewport");
  if (!viewport) throw new Error("Run canvas viewport is unavailable");
  const matrix = new DOMMatrixReadOnly(getComputedStyle(viewport).transform);
  return Number.parseFloat(getComputedStyle(element).fontSize) * Math.hypot(matrix.a, matrix.b);
});

type Box = { x: number; y: number; width: number; height: number };
const overlaps = (a: Box, b: Box) => a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;

/** Ids of the activity cards that the floating zoom controls cover. */
const cardsUnderControls = async (canvas: Locator) => {
  const controls = await canvas.locator(".react-flow__controls").boundingBox();
  if (!controls) throw new Error("Run canvas zoom controls are unavailable");
  const cards = await Promise.all((await canvas.locator(".react-flow__node").all())
    .map(async card => ({ id: await card.getAttribute("data-id"), box: await card.boundingBox() })));
  return cards.filter(card => card.box && overlaps(card.box, controls)).map(card => card.id);
};

for (const viewport of viewports) {
  for (const theme of ["light", "dark"]) {
    test(`run canvas first view keeps outcome labels readable (${viewport.name}, ${theme})`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await page.goto(`/?mode=activity-duplicate-editor&host=instance&run-canvas=unique&adaptive-ports=outcomes&theme=${theme}`);
      const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
      const pane = canvas.locator(".react-flow__pane");
      const task = canvas.locator('.react-flow__node[data-id="write-one"]');
      const label = (name: string) => task.locator(".wf-node-port-label").filter({ hasText: new RegExp(`^${name}$`) });
      await expect(task.locator('.source[data-handleid="Unmatched"]')).toBeAttached();
      if (viewport.paneHeight) {
        // The untouched first view re-fits when the pane shrinks, as it does when run feedback appears.
        const tallPane = (await pane.boundingBox())!.height;
        await page.setViewportSize({ width: viewport.width, height: Math.round(viewport.height - (tallPane - viewport.paneHeight)) });
        await expect.poll(async () => Math.round((await pane.boundingBox())?.height ?? 0), { message: "run canvas pane is short" })
          .toBeLessThanOrEqual(viewport.paneHeight + 1);
      }

      const fonts: Record<string, number> = {};
      for (const name of outcomes) {
        await expect(label(name)).toBeVisible();
        await expect.poll(() => renderedFont(label(name))).toBeGreaterThanOrEqual(10);
        fonts[name] = await renderedFont(label(name));
        // The readable first view shows the labels themselves, not just a readable scale.
        await expect.poll(async () => {
          const box = await label(name).boundingBox();
          const bounds = await pane.boundingBox();
          // The zoom controls and the MiniMap (three nodes here) float over the pane; a label under one is not readable.
          const panels = await Promise.all((await canvas.locator(".react-flow__panel:visible").all()).map(panel => panel.boundingBox()));
          const underPanel = panels.some(panel => box && panel && box.x < panel.x + panel.width && box.x + box.width > panel.x
            && box.y < panel.y + panel.height && box.y + box.height > panel.y);
          return Boolean(box && bounds && !underPanel && box.x >= bounds.x && box.x + box.width <= bounds.x + bounds.width
            && box.y >= bounds.y && box.y + box.height <= bounds.y + bounds.height);
        }, { message: `${name} label is inside the run canvas and clear of its controls and MiniMap` }).toBe(true);
      }
      await expect.poll(() => cardsUnderControls(canvas), { message: "no activity card is under the zoom controls in the first view" }).toEqual([]);
      await testInfo.attach("initial-outcome-font.json", { body: JSON.stringify(fonts), contentType: "application/json" });
      await page.screenshot({ path: testInfo.outputPath(`initial-${viewport.name}-${theme}.png`) });

      // Fit View remains the whole-graph overview the user asks for.
      await canvas.getByRole("button", { name: "Fit View", exact: true }).click();
      const bounds = (await pane.boundingBox())!;
      for (const node of await canvas.locator(".react-flow__node").all()) {
        await expect.poll(async () => {
          const box = await node.boundingBox();
          return Boolean(box && box.x >= bounds.x && box.x + box.width <= bounds.x + bounds.width);
        }).toBe(true);
      }
      await expect.poll(() => cardsUnderControls(canvas), { message: "no activity card is under the zoom controls after Fit View" }).toEqual([]);
      await page.screenshot({ path: testInfo.outputPath(`fit-view-${viewport.name}-${theme}.png`) });
    });
  }
}
