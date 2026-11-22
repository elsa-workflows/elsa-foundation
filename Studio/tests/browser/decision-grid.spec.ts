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
    await editor.pressSequentially("<= 9007199254740993");
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

test("accepts sequential cell typing, preserves direct-entry caret, and restores grid focus", async ({ page }) => {
  const grid = await openGrid(page, "dark");
  const amount = grid.getByRole("gridcell", { name: "Rule 1, input amount", exact: true });
  await amount.click();
  await amount.press("Enter");
  let editor = page.getByRole("textbox", { name: "Edit selected cell", exact: true });
  await editor.pressSequentially("<= 9007199254740993");
  await expect(editor).toHaveValue("<= 9007199254740993");
  await editor.press("Escape");
  await expect(amount).toContainText("<= 40");
  await expect(amount).toBeFocused();

  const directEntry = grid.getByRole("gridcell", { name: "Rule 3, input amount", exact: true });
  await directEntry.click();
  await directEntry.press("x");
  editor = page.getByRole("textbox", { name: "Edit selected cell", exact: true });
  await editor.pressSequentially(" > 10");
  await expect(editor).toHaveValue("x > 10");
  await editor.press("Enter");
  await expect(directEntry).toHaveText("x > 10");
  await expect(directEntry).toBeFocused();
});

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
  await expect(page.getByRole("heading", { name: "Rules", exact: true })).toBeVisible();
  const grid = page.getByRole("grid");
  await expect(grid).toHaveCount(1);
  return grid;
}

for (const [theme, width] of [["light", 1440], ["dark", 1440], ["dark", 390]] as const) {
  test(`navigates through the decision breadcrumb in ${theme} theme at ${width}px`, async ({ page }, testInfo) => {
    await openComposedDecision(page, theme, width);
    const breadcrumb = page.getByRole("navigation", { name: "Breadcrumb", exact: true });
    const title = page.getByRole("heading", { level: 1, name: "Approval", exact: true });
    const link = breadcrumb.getByRole("link", { name: "Decisions", exact: true });
    await expect(title).toBeVisible();
    await expect(breadcrumb.locator('[aria-current="page"]')).toContainText("Approval");
    await expect(link).toHaveAttribute("href", "/decisions");
    if (width < 600) {
      expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
      const bounds = await title.evaluate(element => element.getBoundingClientRect());
      expect(bounds.left).toBeGreaterThanOrEqual(0);
      expect(bounds.right).toBeLessThanOrEqual(width);
    }
    const screenshotPath = testInfo.outputPath(`decision-breadcrumb-${theme}-${width}.png`);
    await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });
    await testInfo.attach(`decision-breadcrumb-${theme}-${width}.png`, { path: screenshotPath, contentType: "image/png" });

    await link.click();
    await expect(page.getByRole("heading", { level: 1, name: "Decisions", exact: true })).toBeVisible();
    await expect(page.getByRole("tablist", { name: "Decision collections", exact: true })).toBeVisible();
    const row = page.getByRole("row", { name: /Approval/ });
    await row.getByRole("button", { name: "Approval", exact: true }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Approval", exact: true })).toBeVisible();
    await expect(page.getByRole("navigation", { name: "Breadcrumb", exact: true }).getByRole("link", { name: "Decisions", exact: true })).toHaveAttribute("href", "/decisions");
  });
}

for (const theme of ["light", "dark"] as const) {
  test(`composes a single unclipped decision grid with usable menus in ${theme} theme`, async ({ page }, testInfo) => {
    const grid = await openComposedDecision(page, theme);
    await expect(page.locator(".decision-editor-tabs")).toHaveClass(/studio-tabs/);
    await expect(page.getByRole("heading", { name: "Rules", exact: true })).toHaveCount(1);
    await expect(grid.locator("tbody tr")).toHaveCount(2);
    await expect(page.locator(".decision-editor-workspace .decision-table-section")).toHaveCount(1);
    await expect(page.locator(".decision-expression-help")).toHaveCount(0);
    await expect(page.locator(".decision-grid-footer")).toContainText("2 rules · 1 input · 1 output");
    const workspaceWidth = await page.locator(".decision-editor-workspace").evaluate(element => element.getBoundingClientRect().width);
    const gridWidth = await page.locator(".decision-table-scroll").evaluate(element => element.getBoundingClientRect().width);
    expect(gridWidth).toBeGreaterThan(workspaceWidth * .95);
    for (const view of ["Tests", "Settings", "Rules"]) {
      await page.getByRole("tab", { name: view, exact: true }).click();
      await expect(page.locator('[role="grid"]')).toHaveCount(1);
      await expect(page.locator(".decision-table-section")).toHaveCount(1);
      if (view === "Rules") await expect(page.getByRole("tabpanel", { name: "Rules" })).toBeVisible();
      else await expect(page.getByRole("tabpanel", { name: "Rules" })).toBeHidden();
    }
    await page.getByRole("tab", { name: "Rules", exact: true }).focus();
    await page.keyboard.press("ArrowRight");
    await expect(page.getByRole("tab", { name: "Tests", exact: true })).toHaveAttribute("aria-selected", "true");
    await page.keyboard.press("ArrowLeft");
    await expect(page.getByRole("tab", { name: "Rules", exact: true })).toHaveAttribute("aria-selected", "true");
    const dataHeader = grid.locator("thead tr:nth-child(2) th.decision-grid-column").first();
    const dataCell = grid.locator("tbody tr").first().locator(".decision-grid-cell").first();
    const geometry = await Promise.all([
      dataHeader.evaluate(element => element.getBoundingClientRect().width),
      dataCell.evaluate(element => element.getBoundingClientRect().width),
      dataCell.evaluate(element => element.getBoundingClientRect().height)
    ]);
    expect(Math.abs(geometry[0] - geometry[1])).toBeLessThan(2);
    expect(geometry[1]).toBeGreaterThan(320);
    expect(geometry[1]).toBeLessThan(700);
    expect(geometry[2]).toBeGreaterThanOrEqual(44);
    const dividerMetrics = await grid.evaluate(table => {
      const header = table.querySelector<HTMLElement>("thead tr:nth-child(2) th.decision-grid-column")!;
      const cell = table.querySelector<HTMLElement>("tbody tr:first-child .decision-grid-cell")!;
      const headerRowNumber = table.querySelector<HTMLElement>("thead .decision-grid-row-number")!;
      const rowNumber = table.querySelector<HTMLElement>("tbody tr:first-child .decision-grid-row-number")!;
      const lastCell = table.querySelector<HTMLElement>("tbody tr:first-child > *:last-child")!;
      return {
        headerIndexBorder: getComputedStyle(headerRowNumber).borderInlineEndWidth,
        indexBorder: getComputedStyle(rowNumber).borderInlineEndWidth,
        headerStart: getComputedStyle(header).borderInlineStartWidth,
        cellStart: getComputedStyle(cell).borderInlineStartWidth,
        headerLeft: header.getBoundingClientRect().left,
        cellLeft: cell.getBoundingClientRect().left,
        outerBorder: getComputedStyle(lastCell).borderInlineEndWidth
      };
    });
    expect(dividerMetrics.headerIndexBorder).toBe("1px");
    expect(dividerMetrics.indexBorder).toBe("1px");
    expect(dividerMetrics.headerStart).toBe("0px");
    expect(dividerMetrics.cellStart).toBe("0px");
    expect(Math.abs(dividerMetrics.headerLeft - dividerMetrics.cellLeft)).toBeLessThan(1);
    expect(dividerMetrics.outerBorder).toBe("0px");
    await dataCell.focus();
    await expect(dataCell).toBeFocused();
    expect(await dataCell.evaluate(element => getComputedStyle(element).outlineStyle)).toBe("solid");
    await dataCell.press("Enter");
    const focusEditor = page.getByRole("textbox", { name: "Edit selected cell", exact: true });
    await expect(focusEditor).toBeFocused();
    expect(await dataCell.evaluate(element => getComputedStyle(element).outlineStyle)).toBe("none");
    expect(await focusEditor.evaluate(element => getComputedStyle(element).outlineStyle)).toBe("solid");
    await focusEditor.press("Escape");
    await expect(dataCell).toBeFocused();
    const toolbarButton = page.getByRole("button", { name: "More table actions", exact: true });
    const surfaceColor = await page.locator(".decision-table-scroll").evaluate(element => getComputedStyle(element).backgroundColor);
    expect(await toolbarButton.evaluate(element => getComputedStyle(element).backgroundColor)).toBe(surfaceColor);
    expect(await page.getByRole("button", { name: "More decision actions", exact: true }).evaluate(element => getComputedStyle(element).backgroundColor)).toBe(surfaceColor);
    await toolbarButton.focus();
    await expect(toolbarButton).toBeFocused();
    const focusState = await dataCell.evaluate(element => ({
      active: element === document.activeElement,
      gridFocused: element.closest("[role='grid']")?.matches(":focus-within"),
      outlineStyle: getComputedStyle(element).outlineStyle
    }));
    expect(focusState.active).toBe(false);
    expect(focusState.gridFocused).toBe(false);
    expect(focusState.outlineStyle).toBe("none");
    await toolbarButton.click();
    await page.getByRole("menuitem", { name: "Expression help", exact: true }).click();
    await expect(page.getByRole("note", { name: "Expression syntax help" })).toContainText("The server validates the supported profile.");
    await page.getByRole("button", { name: "Hide expression help", exact: true }).click();
    await testInfo.attach(`decision-page-default-${theme}.png`, { body: await page.screenshot({ fullPage: true, animations: "disabled" }), contentType: "image/png" });

    const settings = page.getByRole("button", { name: "Settings for input 1", exact: true });
    await expect(settings).toHaveAttribute("aria-expanded", "false");
    await settings.click();
    const popover = page.getByRole("dialog", { name: "Input 1 settings", exact: true });
    await expect(popover).toBeVisible();
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
    const outputPopover = page.getByRole("dialog", { name: "Output 1 settings", exact: true });
    await expect(outputPopover).toBeVisible();
    expect(await outputPopover.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return bounds.left >= 0 && bounds.right <= window.innerWidth && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
    })).toBe(true);
    await page.keyboard.press("Escape");
    await expect(outputSettings).toBeFocused();

    const actions = page.getByRole("button", { name: "Actions for rule 1", exact: true });
    const actionCellWidth = await actions.evaluate(element => element.closest("td")!.getBoundingClientRect().width);
    expect(actionCellWidth).toBeLessThanOrEqual(64);
    const actionSurface = await actions.evaluate(element => ({
      background: getComputedStyle(element).backgroundColor,
      surface: getComputedStyle(element.closest(".decision-table-scroll")!).backgroundColor
    }));
    expect(actionSurface.background).toBe(actionSurface.surface);
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
    await toolbarButton.click();
    await page.getByRole("menuitem", { name: "Show annotations", exact: true }).click();
    await expect(cell).toHaveText("<= 21");
    await expect(toolbarButton).toBeFocused();
    await testInfo.attach(`decision-page-${theme}.png`, { body: await page.screenshot({ fullPage: true, animations: "disabled" }), contentType: "image/png" });
  });
}

for (const [theme, width] of [["light", 1440], ["dark", 1440], ["dark", 390]] as const) {
  test(`separates saved-case actions from the table in ${theme} theme at ${width}px`, async ({ page }, testInfo) => {
    await openComposedDecision(page, theme, width);
    await page.getByRole("tab", { name: "Tests", exact: true }).click();
    const heading = page.locator(".decision-cases > .decision-section-heading");
    const table = page.getByRole("region", { name: "Saved decision cases table", exact: true });
    await expect(heading.getByRole("heading", { name: "Saved cases", exact: true })).toBeVisible();
    await expect(heading.getByRole("button", { name: "Add case", exact: true })).toBeVisible();
    await expect(heading.getByRole("button", { name: "Run saved cases", exact: true })).toBeVisible();
    const caseRow = table.getByRole("row", { name: /Under age boy/ });
    await expect(caseRow).toBeVisible();
    const actions = caseRow.getByRole("button", { name: "Actions for Under age boy", exact: true });
    await expect(actions).toBeVisible();
    await expect(caseRow.getByRole("button", { name: "Edit Under age boy", exact: true })).toHaveCount(0);
    await expect(caseRow.getByRole("button", { name: "Delete Under age boy", exact: true })).toHaveCount(0);
    const gap = await heading.evaluate(element => {
      const headingBottom = element.getBoundingClientRect().bottom;
      const tableTop = element.parentElement!.querySelector<HTMLElement>("[aria-label='Saved decision cases table']")!.getBoundingClientRect().top;
      return tableTop - headingBottom;
    });
    expect(gap).toBeGreaterThanOrEqual(12);
    const closedScreenshot = `test-results/saved-cases-menu-closed-${theme}-${width}.png`;
    await page.screenshot({ path: closedScreenshot, fullPage: true, animations: "disabled" });
    await testInfo.attach(`saved-cases-menu-closed-${theme}-${width}.png`, { path: closedScreenshot, contentType: "image/png" });
    await actions.click();
    const menu = page.getByRole("menu", { name: "Actions for Under age boy", exact: true });
    await expect(menu.getByRole("menuitem", { name: "Edit", exact: true })).toBeVisible();
    await expect(menu.getByRole("menuitem", { name: "Delete", exact: true })).toBeVisible();
    expect(await menu.evaluate(element => ({ display: getComputedStyle(element).display, padding: getComputedStyle(element).padding }))).toEqual({ display: "grid", padding: "6px" });
    expect(await menu.getByRole("menuitem", { name: "Edit", exact: true }).evaluate(element => getComputedStyle(element).color)).toBe(await page.evaluate(() => {
      const sample = document.createElement("span");
      sample.style.color = "var(--studio-text)";
      document.body.append(sample);
      const color = getComputedStyle(sample).color;
      sample.remove();
      return color;
    }));
    expect(await menu.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return bounds.left >= 0 && bounds.top >= 0 && bounds.right <= window.innerWidth && bounds.bottom <= window.innerHeight;
    })).toBe(true);
    const openScreenshot = `test-results/saved-cases-menu-open-${theme}-${width}.png`;
    await page.screenshot({ path: openScreenshot, fullPage: true, animations: "disabled" });
    await testInfo.attach(`saved-cases-menu-open-${theme}-${width}.png`, { path: openScreenshot, contentType: "image/png" });
    await page.keyboard.press("Escape");
    await expect(actions).toBeFocused();
    await actions.click();
    await menu.getByRole("menuitem", { name: "Edit", exact: true }).click();
    const editForm = page.getByRole("form", { name: "Edit decision case", exact: true });
    await expect(editForm).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(editForm).toBeHidden();
    await expect(actions).toBeFocused();
  });
}

test("keeps composed page menus within the viewport on a narrow screen", async ({ page }, testInfo) => {
  const grid = await openComposedDecision(page, "dark", 390);
  await expect(page.locator(".decision-editor-tabs")).toHaveClass(/studio-tabs/);
  const scroll = page.locator(".decision-table-scroll");
  const metrics = await scroll.evaluate(element => ({ clientWidth: element.clientWidth, scrollWidth: element.scrollWidth }));
  expect(metrics.scrollWidth).toBeGreaterThan(metrics.clientWidth);
  const narrowDividerMetrics = await grid.evaluate(table => {
    const header = table.querySelector<HTMLElement>("thead tr:nth-child(2) th.decision-grid-column")!;
    const cell = table.querySelector<HTMLElement>("tbody tr:first-child .decision-grid-cell")!;
    const headerRowNumber = table.querySelector<HTMLElement>("thead .decision-grid-row-number")!;
    const rowNumber = table.querySelector<HTMLElement>("tbody tr:first-child .decision-grid-row-number")!;
    return {
      headerIndexBorder: getComputedStyle(headerRowNumber).borderInlineEndWidth,
      indexBorder: getComputedStyle(rowNumber).borderInlineEndWidth,
      headerStart: getComputedStyle(header).borderInlineStartWidth,
      cellStart: getComputedStyle(cell).borderInlineStartWidth
    };
  });
  expect(narrowDividerMetrics.headerIndexBorder).toBe("1px");
  expect(narrowDividerMetrics.indexBorder).toBe("1px");
  expect(narrowDividerMetrics.headerStart).toBe("0px");
  expect(narrowDividerMetrics.cellStart).toBe("0px");
  await scroll.evaluate(element => { element.scrollLeft = 240; });
  const scrolledIndex = await grid.evaluate(table => {
    const scroll = table.closest<HTMLElement>(".decision-table-scroll")!;
    const rowNumber = table.querySelector<HTMLElement>("tbody tr:first-child .decision-grid-row-number")!;
    return {
      left: rowNumber.getBoundingClientRect().left,
      scrollLeft: scroll.getBoundingClientRect().left,
      border: getComputedStyle(rowNumber).borderInlineEndWidth
    };
  });
  expect(scrolledIndex.border).toBe("1px");
  expect(Math.abs(scrolledIndex.left - scrolledIndex.scrollLeft)).toBeLessThan(2);
  const settings = page.getByRole("button", { name: "Settings for input 1", exact: true });
  await settings.click();
  const popover = page.getByRole("dialog", { name: "Input 1 settings", exact: true });
  await expect(popover).toBeVisible();
  expect(await popover.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
  })).toBe(true);
  await page.keyboard.press("Escape");
  const outputSettings = page.getByRole("button", { name: "Settings for output 1", exact: true });
  await outputSettings.click();
  const outputPopover = page.getByRole("dialog", { name: "Output 1 settings", exact: true });
  await expect(outputPopover).toBeVisible();
  expect(await outputPopover.evaluate(element => {
    const bounds = element.getBoundingClientRect();
    return bounds.left >= 0 && bounds.right <= window.innerWidth && bounds.top >= 0 && bounds.bottom <= window.innerHeight;
  })).toBe(true);
  await page.keyboard.press("Escape");
  await expect(outputSettings).toBeFocused();
  const tableActions = page.getByRole("button", { name: "More table actions", exact: true });
  await tableActions.focus();
  await expect(tableActions).toBeFocused();
  const focusState = await grid.evaluate(element => ({
    focused: element.matches(":focus-within"),
    activeCellOutline: getComputedStyle(element.querySelector(".decision-grid-cell.is-active")!).outlineStyle
  }));
  expect(focusState.focused).toBe(false);
  expect(focusState.activeCellOutline).toBe("none");
  await scroll.evaluate(element => { element.scrollLeft = 0; });
  await testInfo.attach("decision-page-narrow.png", { body: await page.screenshot({ fullPage: true, animations: "disabled" }), contentType: "image/png" });
  await expect(grid).toBeVisible();
});

test("decision collections have linked keyboard tabs that retain focus after loading", async ({ page }) => {
  await page.goto("/decision-grid.html?page=collection&theme=light");
  const tablist = page.getByRole("tablist", { name: "Decision collections", exact: true });
  const decisions = tablist.getByRole("tab", { name: "Decisions", exact: true });
  const recycleBin = tablist.getByRole("tab", { name: "Recycle bin", exact: true });
  const panels = new Map<string, ReturnType<typeof page.locator>>();
  for (const tab of [decisions, recycleBin]) {
    const id = await tab.getAttribute("id");
    const panelId = await tab.getAttribute("aria-controls");
    expect(id).toBeTruthy();
    expect(panelId).toBeTruthy();
    const panel = page.locator(`[id="${panelId}"]`);
    await expect(panel).toHaveAttribute("role", "tabpanel");
    await expect(panel).toHaveAttribute("aria-labelledby", id!);
    panels.set(await tab.innerText(), panel);
  }

  await expect(panels.get("Decisions")!).toBeVisible();
  await decisions.focus();
  await page.keyboard.press("ArrowRight");
  await expect(recycleBin).toHaveAttribute("aria-selected", "true");
  await expect(recycleBin).toBeFocused();
  await expect(panels.get("Recycle bin")!).toBeVisible();
  await expect(panels.get("Decisions")!).toBeHidden();

  const recycledRow = page.getByRole("row", { name: /Approval/ });
  await expect(recycledRow).toBeVisible();
  await expect(recycleBin).toBeFocused();
  await page.getByRole("row", { name: /Approval/ }).getByRole("button", { name: "Actions for Approval", exact: true }).click();
  await page.getByRole("menuitem", { name: "Restore", exact: true }).click();
  await expect(page.getByRole("row", { name: /Approval/ })).toHaveCount(0);
  await decisions.click();
  await expect(page.getByRole("button", { name: "Approval", exact: true })).toBeVisible();
  await expect(page.getByText("Unpublished", { exact: true })).toBeVisible();

  const activeRow = page.getByRole("row", { name: /Approval/ });
  await activeRow.getByRole("button", { name: "Actions for Approval", exact: true }).click();
  const activeMenu = page.getByRole("menu", { name: "Actions for Approval", exact: true });
  await expect(activeMenu.getByRole("menuitem", { name: "Move to recycle bin", exact: true })).toBeVisible();
  await activeMenu.getByRole("menuitem", { name: "Move to recycle bin", exact: true }).click();
  await expect(page.getByRole("row", { name: /Approval/ })).toHaveCount(0);
  await recycleBin.click();
  await expect(page.getByRole("row", { name: /Approval/ })).toBeVisible();
  await page.getByRole("row", { name: /Approval/ }).getByRole("button", { name: "Actions for Approval", exact: true }).click();
  await page.getByRole("menuitem", { name: "Restore", exact: true }).click();
  await expect(page.getByRole("row", { name: /Approval/ })).toHaveCount(0);

  await page.goto("/decision-grid.html?page=collection&theme=light");
  await page.getByRole("tab", { name: "Recycle bin", exact: true }).click();
  await page.getByRole("row", { name: /Approval/ }).getByRole("button", { name: "Actions for Approval", exact: true }).click();
  await page.getByRole("menuitem", { name: "Delete permanently", exact: true }).click();
  await expect(page.getByRole("row", { name: /Approval/ })).toHaveCount(0);
  await expect(page.getByText("Recycle bin is empty")).toBeVisible();
});

for (const [theme, width] of [["light", 1440], ["dark", 1440], ["dark", 390]] as const) {
  test(`keeps the active decision row menu usable in ${theme} theme at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/decision-grid.html?page=collection&theme=${theme}`);
    await page.getByRole("tab", { name: "Recycle bin", exact: true }).click();
    await page.getByRole("row", { name: /Approval/ }).getByRole("button", { name: "Actions for Approval", exact: true }).click();
    await page.getByRole("menuitem", { name: "Restore", exact: true }).click();
    await page.getByRole("tab", { name: "Decisions", exact: true }).click();
    const row = page.getByRole("row", { name: /Approval/ });
    const trigger = row.getByRole("button", { name: "Actions for Approval", exact: true });
    await trigger.click();
    const menu = page.getByRole("menu", { name: "Actions for Approval", exact: true });
    await expect(menu.getByRole("menuitem", { name: "Move to recycle bin", exact: true })).toBeVisible();
    expect(await menu.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      return bounds.left >= 0 && bounds.top >= 0 && bounds.right <= window.innerWidth && bounds.bottom <= window.innerHeight;
    })).toBe(true);
    await testInfo.attach(`decision-collection-menu-${theme}-${width}.png`, { body: await page.screenshot({ fullPage: true, animations: "disabled" }), contentType: "image/png" });
  });
}

for (const [theme, width] of [["light", 1440], ["dark", 390]] as const) {
  test(`recovers from a saved-case compilation failure in ${theme} theme at ${width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`/decision-grid.html?page=full&recovery=1&theme=${theme}`);
    await expect(page.getByRole("heading", { level: 1, name: "Approval", exact: true })).toBeVisible();
    await page.getByRole("tab", { name: "Tests", exact: true }).click();
    await page.getByRole("button", { name: "Run saved cases", exact: true }).click();
    await expect(page.getByRole("alert")).toContainText("The exact candidate cannot be evaluated.");
    const diagnostics = page.getByLabel("Decision compilation diagnostics");
    await expect(diagnostics).toContainText("This condition is not supported by the selected profile.");
    await expect(diagnostics).toContainText("Rule 1 · Column amount");
    await expect(diagnostics).not.toContainText("decision_");
    const failedScreenshot = testInfo.outputPath(`decision-test-failure-${theme}-${width}.png`);
    await page.screenshot({ path: failedScreenshot, fullPage: true, animations: "disabled" });
    await testInfo.attach(`decision-test-failure-${theme}-${width}.png`, { path: failedScreenshot, contentType: "image/png" });

    await page.getByRole("button", { name: "Review rules", exact: true }).click();
    const cell = page.getByRole("gridcell", { name: "Rule 1, input amount", exact: true });
    await expect(cell).toBeFocused();
    await expect(page.getByRole("note")).toContainText("This condition is not supported by the selected profile.");
    await expect(page.locator(".decision-diagnostics")).toContainText("Rule 1 · Column amount");
    await cell.press("Enter");
    const editor = page.getByRole("textbox", { name: "Edit selected cell", exact: true });
    await editor.fill("< 21");
    await editor.press("Enter");
    await expect(cell).toHaveText("< 21");
    await page.getByRole("button", { name: "Save decision", exact: true }).click();
    await expect.poll(() => page.evaluate(() => document.querySelector(".decision-editor-title .studio-status-chip")?.textContent)).toBe("Saved draft");

    await page.getByRole("tab", { name: "Tests", exact: true }).click();
    await page.getByRole("button", { name: "Run saved cases", exact: true }).click();
    await expect(page.getByRole("region", { name: "Decision test results" })).toContainText("All cases passed");
    await expect(page.getByRole("alert")).toHaveCount(0);
    if (width < 600) expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    const successScreenshot = testInfo.outputPath(`decision-test-recovery-${theme}-${width}.png`);
    await page.screenshot({ path: successScreenshot, fullPage: true, animations: "disabled" });
    await testInfo.attach(`decision-test-recovery-${theme}-${width}.png`, { path: successScreenshot, contentType: "image/png" });
  });
}
test("Tab exits the data cells at both grid boundaries while browsing or editing", async ({ page }) => {
  const grid = await openGrid(page, "light");
  const cells = grid.getByRole("gridcell", { name: /^Rule \d+, (input|output) / });
  for (const [cell, key] of [[cells.first(), "Shift+Tab"], [cells.last(), "Tab"]] as const) {
    await cell.click();
    await cell.press(key);
    await expect(cell).not.toBeFocused();
    expect(await page.evaluate(() => document.activeElement?.closest('[role="gridcell"]') === null)).toBe(true);
    await cell.click();
    await cell.press("Enter");
    await page.getByRole("textbox", { name: "Edit selected cell" }).press(key);
    await expect(page.getByRole("textbox", { name: "Edit selected cell" })).toHaveCount(0);
    expect(await page.evaluate(() => document.activeElement?.closest('[role="gridcell"]') === null)).toBe(true);
  }
});
