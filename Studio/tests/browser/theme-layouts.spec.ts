import { expect, test, type Page } from "@playwright/test";
import { mockGraphAuthoring } from "./graphAuthoringMocks";

test.setTimeout(60_000);

/**
 * Theme layouts as they render in a real browser: the graph-authoring workbench under each
 * `data-theme-layout` the host can publish. The fixture has no ThemeProvider, so the attribute is set
 * directly — exactly what ThemeProvider does when the user picks Drift, Schematic or Atelier.
 */
async function openFlowchartWithOneStep(page: Page, layout: string) {
  await mockGraphAuthoring(page);
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/?mode=activity-definition-graph-authoring&graph=flowchart");
  await page.evaluate(value => document.documentElement.setAttribute("data-theme-layout", value), layout);
  // Editorial starts an empty canvas with its own Add step; every other layout with Add activity.
  await page.getByRole("button", { name: layout === "editorial" ? "Add step" : "Add activity" }).click();
  await page.getByRole("option", { name: /Write line/ }).click();
  await expect(page.locator("[data-graph-node-id]")).toHaveCount(1);
}

test("floating layout floats the palette and inspector over a full-bleed canvas", async ({ page }) => {
  await openFlowchartWithOneStep(page, "floating");
  const canvas = (await page.locator(".wf-canvas-shell").boundingBox())!;
  const palette = (await page.locator(".wf-palette").boundingBox())!;
  const inspector = (await page.locator(".wf-inspector").boundingBox())!;

  // The canvas runs underneath both islands rather than sitting between them.
  expect(canvas.x).toBeLessThanOrEqual(palette.x);
  expect(canvas.x + canvas.width).toBeGreaterThanOrEqual(inspector.x + inspector.width);
  await expect(page.locator(".wf-palette")).not.toHaveCSS("border-radius", "0px");
  await expect(page.getByRole("separator", { name: "Resize activities panel" })).toBeHidden();
});

test("workbench layout keeps the palette dense", async ({ page }) => {
  await openFlowchartWithOneStep(page, "workbench");
  await page.getByRole("treeitem", { name: /Primitives/i }).click();
  const row = page.getByRole("treeitem", { name: /Write line/ });

  // Classic palette rows are 42px tall.
  expect((await row.boundingBox())!.height).toBeLessThan(42);
  await expect(page.locator(".wf-canvas")).not.toHaveCSS("background-image", "none");
});

test("editorial layout adds steps inline and keeps connection inserts in view", async ({ page }) => {
  await openFlowchartWithOneStep(page, "editorial");
  await expect(page.getByRole("searchbox", { name: "Search activity palette" })).toHaveCount(0);

  // Placing a step selects it, so Add step continues the flow from it.
  await page.getByRole("button", { name: "Add step" }).click();
  await page.getByRole("option", { name: /Write line/ }).click();

  await expect(page.locator("[data-graph-node-id]")).toHaveCount(2);
  const insert = page.getByRole("button", { name: "Insert activity into connection" });
  await expect(insert).toHaveCount(1);
  await expect(page.locator(".wf-edge-actions")).toHaveCSS("opacity", "1");
  await expect(page.getByRole("button", { name: "Delete connection" })).toBeHidden();
});

test("classic layout keeps the docked designer", async ({ page }) => {
  await openFlowchartWithOneStep(page, "classic");

  await expect(page.getByRole("separator", { name: "Resize activities panel" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Add step" })).toHaveCount(0);
});

test("classic layout hides the minimap while a one-step graph fits the canvas", async ({ page }) => {
  await openFlowchartWithOneStep(page, "classic");
  const canvas = page.locator(".react-flow");
  await expect(canvas).toBeVisible();
  await expect(canvas.locator(".react-flow__node")).toHaveCount(1);
  await expect.poll(() => canvas.evaluate(element => {
    const node = element.querySelector<HTMLElement>(".react-flow__node");
    return !!node && node.offsetWidth > 0 && node.offsetHeight > 0
      && node.offsetWidth < element.clientWidth && node.offsetHeight < element.clientHeight;
  }), { message: "the node fits in the canvas at its natural size" }).toBe(true);
  await expect(canvas.locator(".react-flow__minimap")).toHaveCount(0);
});

test("classic layout keeps the block toolbar on one row until a block is selected", async ({ page }) => {
  await openFlowchartWithOneStep(page, "classic");
  // Clear the selection that placing the step made, so the toolbar shows only its block-independent commands.
  await page.locator(".react-flow__pane").click({ position: { x: 10, y: 10 } });
  const toolbar = page.getByRole("group", { name: "Named block commands", exact: true });
  await expect(toolbar).toBeVisible();
  // Promote acts on one named block, so it only joins the toolbar then (#4712).
  await expect(toolbar.getByRole("button", { name: "Promote to reusable activity…", exact: true })).toHaveCount(0);
  // A command that wraps the toolbar onto a second row takes that row from the docked canvas.
  const rows = await toolbar.evaluate(element =>
    new Set([...element.querySelectorAll<HTMLElement>("button")].map(button => Math.round(button.getBoundingClientRect().top))).size);
  expect(rows, "the block toolbar fits on one row").toBe(1);
});

test("classic layout shows the minimap once the graph outgrows the canvas", async ({ page }) => {
  await openFlowchartWithOneStep(page, "classic");
  await addWriteLinesFromPalette(page, 4);
  await expect(page.locator("[data-graph-node-id]")).toHaveCount(5);
  await expect.poll(async () => page.locator(".react-flow__minimap").count()).toBe(1);
});

async function addWriteLinesFromPalette(page: Page, count: number) {
  const writeLine = page.getByRole("treeitem", { name: /Write line/ });
  if (await writeLine.count() === 0) {
    await page.getByRole("treeitem", { name: /Primitives/i }).click();
  }
  for (let index = 0; index < count; index += 1) {
    await writeLine.click();
  }
}
