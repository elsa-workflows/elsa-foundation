import { expect, test, type Locator } from "@playwright/test";

const longWorkflowName = "Published policy workflow with a long title to exercise the definition table grid and action column alignment";

async function expectColumnsAligned(headerRow: Locator, row: Locator) {
  const headerBoxes = await headerRow.getByRole("columnheader").evaluateAll(elements =>
    elements.map(element => {
      const { x, width } = element.getBoundingClientRect();
      return { x, width };
    }));
  const cellBoxes = await row.getByRole("cell").evaluateAll(elements =>
    elements.map(element => {
      const { x, width } = element.getBoundingClientRect();
      return { x, width };
    }));

  expect(headerBoxes).toHaveLength(6);
  expect(cellBoxes).toHaveLength(6);
  for (let index = 0; index < headerBoxes.length; index++) {
    expect(Math.abs(headerBoxes[index].x - cellBoxes[index].x), `column ${index + 1} starts in the same place`).toBeLessThanOrEqual(1);
    expect(Math.abs(headerBoxes[index].width - cellBoxes[index].width), `column ${index + 1} has the same width`).toBeLessThanOrEqual(1);
  }
  const actions = row.getByRole("cell").last();
  const [actionsBox, lastButtonBox] = await Promise.all([
    actions.boundingBox(),
    actions.getByRole("button").last().boundingBox()
  ]);
  expect(actionsBox).not.toBeNull();
  expect(lastButtonBox).not.toBeNull();
  expect(Math.abs(actionsBox!.x + actionsBox!.width - lastButtonBox!.x - lastButtonBox!.width),
    "actions end at the right-aligned column edge").toBeLessThanOrEqual(1);
}

async function scrollColumnIntoView(scrollArea: Locator, column: Locator) {
  const [scrollBox, columnBox, currentScrollLeft] = await Promise.all([
    scrollArea.boundingBox(),
    column.boundingBox(),
    scrollArea.evaluate(element => element.scrollLeft)
  ]);
  expect(scrollBox).not.toBeNull();
  expect(columnBox).not.toBeNull();
  const contentLeft = columnBox!.x - scrollBox!.x + currentScrollLeft;
  await scrollArea.evaluate((element, left) => {
    element.scrollLeft = Math.max(0, left - 8);
  }, contentLeft);
}

test("workflow definition version columns show saved and published state and stay aligned when scrolling", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto("/?theme=light&mode=definition-versions&state=all");

  const table = page.getByRole("table", { name: "Workflow definitions" });
  const headerRow = table.getByRole("row").first();
  const longRow = table.getByRole("row", { name: `Open workflow definition ${longWorkflowName}`, exact: true });
  const noDraftRow = table.getByRole("row", { name: "Open workflow definition Published workflow without a draft", exact: true });
  const draftOnlyRow = table.getByRole("row", { name: "Open workflow definition Draft only workflow", exact: true });
  const retractedRow = table.getByRole("row", { name: "Open workflow definition Retracted workflow with a newer saved version", exact: true });
  const designOnlyRow = table.getByRole("row", { name: "Open workflow definition Design only workflow", exact: true });
  const deletedRow = table.getByRole("row", { name: "Open workflow definition Retired workflow in the deleted list", exact: true });

  await expect(longRow).toBeVisible();
  await expect(table.getByRole("columnheader", { name: "Latest version" })).toBeVisible();
  await expect(table.getByRole("columnheader", { name: "Published version" })).toBeVisible();

  const longCells = longRow.getByRole("cell");
  await expect(longCells.nth(2)).toContainText("Draft");
  await expect(longCells.nth(2).locator(".wf-definition-version-number")).toHaveText("3.0.0");
  const draftDisclosure = longCells.nth(2).locator("details");
  await expect(draftDisclosure).not.toHaveAttribute("open");
  await draftDisclosure.locator("summary").focus();
  await page.keyboard.press("Enter");
  await expect(draftDisclosure).toHaveAttribute("open");
  await expect(draftDisclosure).toContainText("Draft based on saved version 3.0.0.");
  await page.keyboard.press("Enter");
  await expect(draftDisclosure).not.toHaveAttribute("open");
  await expect(longCells.nth(3)).toHaveText("2.0.0");

  const noDraftCells = noDraftRow.getByRole("cell");
  await expect(noDraftCells.nth(2)).toHaveText("3.0.0");
  await expect(noDraftCells.nth(3)).toHaveText("2.0.0");

  const draftOnlyCells = draftOnlyRow.getByRole("cell");
  await expect(draftOnlyCells.nth(2).locator("summary")).toHaveText("Draft");
  await expect(draftOnlyCells.nth(2).locator(".wf-definition-version-number")).toHaveCount(0);
  await expect(draftOnlyCells.nth(3)).toHaveText("Not published");

  const retractedCells = retractedRow.getByRole("cell");
  await expect(retractedCells.nth(2)).toHaveText("5.0.0");
  await expect(retractedCells.nth(3)).toHaveText("Retracted");

  const designOnlyCells = designOnlyRow.getByRole("cell");
  await expect(designOnlyCells.nth(2)).toHaveText("1.0.0");
  await expect(designOnlyCells.nth(3)).toHaveText("Unavailable");

  await expect(longCells.nth(5).getByRole("button")).toHaveCount(1);
  await expect(deletedRow.getByRole("cell").nth(5).getByRole("button")).toHaveCount(1);
  await expectColumnsAligned(headerRow, longRow);
  await expectColumnsAligned(headerRow, deletedRow);

  await page.setViewportSize({ width: 390, height: 844 });
  const scrollArea = page.getByRole("region", { name: "Workflow definitions table" });
  await expect(scrollArea).toHaveAttribute("tabindex", "0");
  const scrollDimensions = await scrollArea.evaluate(element => ({
    clientWidth: element.clientWidth,
    scrollWidth: element.scrollWidth
  }));
  expect(scrollDimensions.scrollWidth).toBeGreaterThan(scrollDimensions.clientWidth);
  await expectColumnsAligned(headerRow, longRow);
  await expectColumnsAligned(headerRow, deletedRow);

  const latestHeader = table.getByRole("columnheader", { name: "Latest version" });
  await scrollColumnIntoView(scrollArea, latestHeader);
  await scrollArea.focus();
  const scrollLeftBeforeKeyboard = await scrollArea.evaluate(element => element.scrollLeft);
  await page.keyboard.press("ArrowRight");
  await expect.poll(() => scrollArea.evaluate(element => element.scrollLeft)).toBeGreaterThan(scrollLeftBeforeKeyboard);
  await expect(latestHeader).toBeInViewport();
  await expect(longCells.nth(2)).toBeInViewport();

  const publishedHeader = table.getByRole("columnheader", { name: "Published version" });
  await scrollColumnIntoView(scrollArea, publishedHeader);
  await expect(publishedHeader).toBeInViewport();
  await expect(longCells.nth(3)).toBeInViewport();
});

test("definition actions support keyboard navigation and keep selection independent from opening", async ({ page }) => {
  await page.goto("/?mode=definition-versions&theme=dark&state=all");
  const trigger = page.getByRole("button", { name: "Actions for Published workflow without a draft", exact: true });
  await trigger.focus();
  await page.keyboard.press("ArrowDown");
  const menu = page.getByRole("menu", { name: "Actions for Published workflow without a draft", exact: true });
  await expect(menu.getByRole("menuitem", { name: "Artifacts", exact: true })).toBeFocused();
  await page.keyboard.press("ArrowDown");
  await expect(menu.getByRole("menuitem", { name: "Delete", exact: true })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(menu).toHaveCount(0);
  await expect(trigger).toBeFocused();

  const checkbox = page.getByRole("checkbox", { name: "Select workflow definition Published workflow without a draft", exact: true });
  await checkbox.check();
  await expect(checkbox).toBeChecked();
  await expect(page.getByText("1 selected", { exact: true })).toBeVisible();
  expect(new URL(page.url()).searchParams.has("definition")).toBe(false);

  const deletedTrigger = page.getByRole("button", { name: "Actions for Retired workflow in the deleted list", exact: true });
  await deletedTrigger.click();
  const deletedMenu = page.getByRole("menu", { name: "Actions for Retired workflow in the deleted list", exact: true });
  await expect(deletedMenu.getByRole("menuitem", { name: "Restore", exact: true })).toBeVisible();
  await expect(deletedMenu.getByRole("menuitem", { name: "Delete permanently", exact: true })).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(deletedTrigger).toBeFocused();
});

for (const scenario of ["empty", "failed", "loading"]) {
  test(`definitions ${scenario} state stays visible without fabricated version rows`, async ({ page }) => {
    await page.goto(`/?mode=definition-versions&scenario=${scenario}`);
    await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible();
    await expect(page.locator(".wf-grid-row")).toHaveCount(0);
    if (scenario === "failed") await expect(page.getByText("Couldn't load workflow definitions", { exact: false })).toBeVisible();
    else if (scenario === "loading") await expect(page.getByLabel("Loading", { exact: true })).toBeVisible();
    else await expect(page.getByRole("heading", { name: "No active workflow definitions in All workflows", exact: true })).toBeVisible();
  });
}
