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
