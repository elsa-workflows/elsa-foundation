import { expect, test } from "@playwright/test";

async function openGrid(page: import("@playwright/test").Page, theme: "light" | "dark", width = 1440) {
  await page.setViewportSize({ width, height: 900 });
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.goto(`/decision-grid.html?theme=${theme}`);
  await expect(page.getByRole("heading", { name: "Decision table grid", exact: true })).toBeVisible();
  const grid = page.getByRole("grid", { name: "Approval rules", exact: true });
  await expect(grid).toBeVisible();
  return grid;
}

for (const theme of ["light", "dark"] as const) {
  test(`edits and copies decision cells in ${theme} theme`, async ({ page }, testInfo) => {
    const grid = await openGrid(page, theme);
    const amount = grid.getByRole("gridcell", { name: "Rule 1, input amount", exact: true });
    await amount.click();
    await amount.press("Enter");
    const editor = page.getByRole("textbox", { name: "Edit selected cell", exact: true });
    await editor.fill("<= 9007199254740993");
    await editor.press("Enter");
    await expect(amount).toContainText("<= 9007199254740993");
    await expect(amount).toBeFocused();
    await expect(page.getByRole("note")).toContainText("Review the age boundary");

    await amount.press("Tab");
    await expect(grid.getByRole("gridcell", { name: "Rule 1, input customerCategory", exact: true })).toBeFocused();
    await amount.click();
    await page.keyboard.press("Shift+ArrowDown");
    await page.keyboard.press("ControlOrMeta+C");
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe("<= 9007199254740993\n> 40");

    await page.evaluate(() => navigator.clipboard.writeText('<= 10\t"Child"\n> 10\t"Adult"'));
    await amount.click();
    await page.keyboard.press("ControlOrMeta+V");
    await expect(amount).toContainText("<= 10");
    await expect(grid.getByRole("gridcell", { name: "Rule 2, input amount", exact: true })).toHaveText("> 10");
    await expect(grid.getByRole("gridcell", { name: "Rule 1, input customerCategory", exact: true })).toHaveText('"Child"');
    expect(await page.getByTestId("serialized-source").getAttribute("data-source")).toContain("&lt;= 10");
    await page.screenshot({ path: testInfo.outputPath(`decision-grid-${theme}.png`), fullPage: true, animations: "disabled" });
  });
}

test("keeps compact grid headers visible while the table scrolls on narrow screens", async ({ page }, testInfo) => {
  const grid = await openGrid(page, "dark", 390);
  const scroll = page.locator(".decision-table-scroll");
  const metrics = await scroll.evaluate(element => ({ clientWidth: element.clientWidth, scrollWidth: element.scrollWidth }));
  expect(metrics.scrollWidth).toBeGreaterThan(metrics.clientWidth);
  expect(await grid.locator("thead th").first().evaluate(element => getComputedStyle(element).position)).toBe("sticky");
  expect(await grid.locator("tbody .decision-grid-row-number").first().evaluate(element => getComputedStyle(element).position)).toBe("sticky");
  await grid.getByRole("gridcell", { name: "Rule 1, input amount", exact: true }).focus();
  await page.screenshot({ path: testInfo.outputPath("decision-grid-narrow.png"), fullPage: true, animations: "disabled" });
  await grid.getByRole("gridcell", { name: "Rule 3, output result", exact: true }).focus();
  await expect(grid.getByRole("gridcell", { name: "Rule 3, output result", exact: true })).toBeFocused();
});

async function openComposedDecision(page: import("@playwright/test").Page, theme: "light" | "dark", width = 1440) {
  await page.setViewportSize({ width, height: 900 });
  await page.goto(`/decision-grid.html?page=full&theme=${theme}`);
  await expect(page.getByRole("heading", { name: "Decision table", exact: true })).toBeVisible();
  const grid = page.getByRole("grid");
  await expect(grid).toHaveCount(1);
  return grid;
}

for (const theme of ["light", "dark"] as const) {
  test(`composes a single unclipped decision grid with usable menus in ${theme} theme`, async ({ page }, testInfo) => {
    const grid = await openComposedDecision(page, theme);
    await expect(page.getByRole("heading", { name: "Decision table", exact: true })).toHaveCount(1);
    await expect(grid.locator("tbody tr")).toHaveCount(2);
    await page.screenshot({ path: testInfo.outputPath(`decision-page-default-${theme}.png`), fullPage: true, animations: "disabled" });

    const settings = page.getByRole("button", { name: "Settings for input 1", exact: true });
    await expect(settings).toHaveAttribute("aria-expanded", "false");
    await settings.click();
    const popover = page.locator(".decision-column-popover");
    await expect(popover.getByRole("dialog", { name: "Input 1 settings" })).toBeVisible();
    await expect(popover.getByRole("textbox", { name: "Input 1 name" })).toBeFocused();
    expect(await popover.evaluate(element => element.closest(".decision-table-scroll"))).toBeNull();
    expect(await popover.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return bounds.left >= 0 && bounds.top >= 0 && bounds.right <= window.innerWidth && bounds.bottom <= window.innerHeight;
    })).toBe(true);
    await page.keyboard.press("Escape");
    await expect(settings).toBeFocused();
    await expect(settings).toHaveAttribute("aria-expanded", "false");

    const outputSettings = page.getByRole("button", { name: "Settings for output 1", exact: true });
    await outputSettings.click();
    const outputPopover = page.locator(".decision-column-popover");
    await expect(outputPopover.getByRole("dialog", { name: "Output 1 settings" })).toBeVisible();
    expect(await outputPopover.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return bounds.left >= 0 && bounds.right <= window.innerWidth && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
    })).toBe(true);
    await page.keyboard.press("Escape");
    await expect(outputSettings).toBeFocused();

    const actions = page.getByRole("button", { name: "Actions for rule 1", exact: true });
    const actionCellWidth = await actions.evaluate(element => element.closest("td")!.getBoundingClientRect().width);
    expect(actionCellWidth).toBeLessThanOrEqual(64);
    await actions.click();
    const menu = page.getByRole("menu", { name: "Actions for rule 1", exact: true });
    await expect(menu.getByRole("menuitem", { name: "Duplicate below" })).toBeVisible();
    expect(await menu.evaluate(element => element.closest(".decision-table-scroll"))).toBeNull();
    await page.keyboard.press("Escape");
    await expect(actions).toBeFocused();

    const cell = grid.getByRole("gridcell", { name: "Rule 1, input amount", exact: true });
    await cell.click();
    await cell.press("Enter");
    const editor = page.getByRole("textbox", { name: "Edit selected cell", exact: true });
    await editor.fill("<= 21");
    const annotations = page.getByRole("button", { name: "Show annotations", exact: true });
    await annotations.click();
    await expect(cell).toHaveText("<= 21");
    await expect(page.getByRole("button", { name: "Hide annotations", exact: true })).toBeFocused();
    await page.screenshot({ path: testInfo.outputPath(`decision-page-${theme}.png`), fullPage: true, animations: "disabled" });
  });
}

test("keeps composed page menus within the viewport on a narrow screen", async ({ page }, testInfo) => {
  const grid = await openComposedDecision(page, "dark", 390);
  const scroll = page.locator(".decision-table-scroll");
  const metrics = await scroll.evaluate(element => ({ clientWidth: element.clientWidth, scrollWidth: element.scrollWidth }));
  expect(metrics.scrollWidth).toBeGreaterThan(metrics.clientWidth);
  const settings = page.getByRole("button", { name: "Settings for input 1", exact: true });
  await settings.click();
  const popover = page.locator(".decision-column-popover");
  await expect(popover.getByRole("dialog", { name: "Input 1 settings" })).toBeVisible();
  expect(await popover.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
  })).toBe(true);
  await page.keyboard.press("Escape");
  const outputSettings = page.getByRole("button", { name: "Settings for output 1", exact: true });
  await outputSettings.click();
  const outputPopover = page.locator(".decision-column-popover");
  await expect(outputPopover.getByRole("dialog", { name: "Output 1 settings" })).toBeVisible();
  expect(await outputPopover.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
  })).toBe(true);
  await page.keyboard.press("Escape");
  await page.screenshot({ path: testInfo.outputPath("decision-page-narrow.png"), fullPage: true, animations: "disabled" });
  await expect(grid).toBeVisible();
});