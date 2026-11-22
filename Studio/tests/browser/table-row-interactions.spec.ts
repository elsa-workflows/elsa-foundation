import { expect, test, type Locator, type Page, type TestInfo } from "@playwright/test";

test.use({ trace: "on" });

async function feedback(page: Page, row: Locator, name: Locator | undefined, info: TestInfo, prefix = "") {
  await expect(row).toBeVisible();
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await page.mouse.move(0, 0);
    const resting = await row.evaluate(element => getComputedStyle(element).backgroundColor);
    const cell = row.locator(':scope > [role="cell"]:visible, :scope > td:visible').nth(1);
    await cell.hover();
    await expect.poll(() => row.evaluate(element => getComputedStyle(element).backgroundColor)).not.toBe(resting);
    const hovered = await row.evaluate(element => getComputedStyle(element).backgroundColor);
    await expect(row).toHaveCSS("cursor", "pointer");
    await page.screenshot({ path: info.outputPath(`${prefix}cell-hover-${width}.png`) });
    if (name) {
      await name.hover();
      expect(await row.evaluate(element => getComputedStyle(element).backgroundColor)).toBe(hovered);
      await expect(name).toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
      await page.screenshot({ path: info.outputPath(`${prefix}name-hover-${width}.png`) });
    }
  }
  await page.setViewportSize({ width: 1440, height: 900 });
  await row.focus();
  await page.keyboard.press("Tab");
  await page.keyboard.press("Shift+Tab");
  await expect(row).toBeFocused();
  await expect(row).toHaveCSS("outline-style", "solid");
  await page.screenshot({ path: info.outputPath(`${prefix}keyboard-focus.png`) });
  await info.attach(`${prefix}row-accessibility.yml`, { body: await row.ariaSnapshot(), contentType: "text/yaml" });
}

for (const theme of ["light", "dark"]) {
  test(`decision collection follows the shared table recipe in ${theme}`, async ({ page }) => {
    await page.goto(`/row-interactions.html?view=decisions&theme=${theme}`);
    const region = page.getByRole("region", { name: "Decisions table", exact: true });
    const table = page.locator("table.decision-assets");
    await expect(table).toHaveClass(/studio-table/);
    await expect(table.locator("thead th")).toHaveCount(3);
    await expect(region).toHaveAttribute("tabindex", "0");
    const styles = await page.evaluate(() => {
      const root = getComputedStyle(document.documentElement);
      const header = getComputedStyle(document.querySelector(".decision-assets thead th")!);
      const rowHeader = getComputedStyle(document.querySelector(".decision-assets tbody th[scope='row']")!);
      const cell = getComputedStyle(document.querySelector(".decision-assets tbody td")!);
      return {
        headerHeight: header.height,
        expectedHeaderHeight: root.getPropertyValue("--studio-table-header-height").trim(),
        headerFontSize: header.fontSize,
        expectedHeaderFontSize: root.getPropertyValue("--studio-label-size").trim(),
        rowHeaderColor: rowHeader.color,
        cellColor: cell.color,
        rowHeaderBackground: rowHeader.backgroundColor,
        cellBackground: cell.backgroundColor
      };
    });
    expect(styles.headerHeight).toBe(styles.expectedHeaderHeight);
    expect(styles.headerFontSize).toBe(styles.expectedHeaderFontSize);
    expect(styles.rowHeaderColor).toBe(styles.cellColor);
    expect(styles.rowHeaderBackground).toBe(styles.cellBackground);
    await page.setViewportSize({ width: 390, height: 900 });
    expect(await region.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
    await region.focus();
    await expect(region).toBeFocused();
    await page.getByLabel("Search decisions").fill("no matching decision");
    await expect(page.getByText("No matching decisions")).toBeVisible();
  });

  test(`collection search keeps a full-height caret in ${theme}`, async ({ page }, info) => {
    for (const view of ["questions", "dashboards"]) {
      await page.goto(`/row-interactions.html?view=${view}&theme=${theme}`);
      const input = page.getByRole("searchbox", { name: `Search ${view}` });
      const shell = input.locator("..");
      await input.focus();
      await expect(input).toBeFocused();

      const geometry = await input.evaluate(element => {
        const fontSize = Number.parseFloat(getComputedStyle(element).fontSize);
        const inputStyle = getComputedStyle(element);
        const shell = element.parentElement!;
        const shellStyle = getComputedStyle(shell);
        const rootStyle = getComputedStyle(document.documentElement);
        return {
          inputHeight: element.getBoundingClientRect().height,
          fontSize,
          expectedFontSize: Number.parseFloat(rootStyle.getPropertyValue("--studio-body-size")) + 2,
          caretColor: inputStyle.caretColor,
          textColor: inputStyle.color,
          supportsCaretAnimation: CSS.supports("caret-animation", "manual"),
          caretAnimation: inputStyle.getPropertyValue("caret-animation"),
          shellHeight: shell.getBoundingClientRect().height,
          expectedShellHeight: Number.parseFloat(rootStyle.getPropertyValue("--studio-control-height")),
          outlineStyle: shellStyle.outlineStyle,
          outlineWidth: shellStyle.outlineWidth
        };
      });
      expect(geometry.inputHeight + 0.1).toBeGreaterThanOrEqual(geometry.fontSize * 1.4);
      expect(geometry.fontSize).toBeGreaterThanOrEqual(geometry.expectedFontSize - 0.1);
      expect(geometry.caretColor).toBe(geometry.textColor);
      if (geometry.supportsCaretAnimation) expect(geometry.caretAnimation).toBe("manual");
      expect(geometry.shellHeight).toBe(geometry.expectedShellHeight);
      expect(geometry.outlineStyle).toBe("solid");
      expect(geometry.outlineWidth).toBe("2px");
      await info.attach(`${view}-search-caret-${theme}.png`, { body: await page.screenshot({ caret: "initial" }), contentType: "image/png" });

      await page.setViewportSize({ width: 390, height: 900 });
      await input.fill("Purchase approval");
      await input.press("Enter");
      await expect(input).toHaveValue("Purchase approval");
      await expect(page.getByText("Purchase approval", { exact: true }).first()).toBeVisible();
      expect(await shell.evaluate(element => element.getBoundingClientRect().right <= window.innerWidth)).toBe(true);
    }
  });

  for (const view of ["connections", "decisions", "questions", "dashboards"]) {
    test(`${view} rows share feedback and activation in ${theme}`, async ({ page }, info) => {
      await page.goto(`/row-interactions.html?view=${view}&theme=${theme}`);
      const row = page.locator("[data-studio-row-interactive]").first();
      const name = row.locator(".studio-row-link");
      await feedback(page, row, name, info);
      const destination = page.getByRole("status", { name: "Opened destination" });
      for (const key of ["Enter", "Space"]) {
        await row.press(key);
        await expect(destination).not.toBeEmpty();
      }
      const expected = await destination.innerText();
      await page.reload();
      await row.locator("td").last().click();
      await expect(destination).toHaveText(expected);
    });
  }

  test(`workflow definition rows preserve selection and identifier controls in ${theme}`, async ({ page }, info) => {
    await page.addInitScript(theme => localStorage.setItem("elsa-studio-theme-mode", theme), theme);
    await page.goto("/minimal-studio.html?screen=list&visual-study=1");
    const row = page.locator(".wf-definition-grid > .wf-grid-row").first();
    await feedback(page, row, row.locator(".wf-definition-link"), info);
    const initial = page.url();
    await row.getByRole("checkbox").check();
    await expect(row).toHaveAttribute("aria-selected", "true");
    await expect(page).toHaveURL(initial);
    await row.getByRole("button", { name: "Copy Definition ID", exact: true }).click();
    await expect(page).toHaveURL(initial);
    await row.getByRole("button", { name: /^Identifiers for / }).click();
    await page.locator(".wf-definition-identifiers-popover").click({ position: { x: 2, y: 2 } });
    await expect(page).toHaveURL(initial);
    await page.keyboard.press("Escape");
    await row.locator(".wf-definition-primary-identifier").click({ position: { x: 2, y: 2 } });
    await expect(page).not.toHaveURL(initial);
  });

  test(`run rows isolate identifier popovers in ${theme}`, async ({ page }, info) => {
    await page.addInitScript(theme => localStorage.setItem("elsa-studio-theme-mode", theme), theme);
    await page.goto("/minimal-studio.html?screen=runs");
    const row = page.locator(".wf-instance-grid > .wf-grid-row").first();
    await feedback(page, row, row.locator(".wf-run-inspect"), info);
    await row.getByRole("button", { name: /^Identifiers for / }).click();
    await page.locator(".wf-definition-identifiers-popover").click({ position: { x: 2, y: 2 } });
    await expect(page.locator(".wf-instance-grid")).toBeVisible();
    await page.keyboard.press("Escape");
    await row.press("Space");
    await expect(page.locator("html")).toHaveAttribute("data-row-destination", /\/workflows\/instances\/run_/);
  });

  test(`task and form rows open their details in ${theme}`, async ({ page }, info) => {
    await page.goto(`/user-tasks-ux.html?theme=${theme}`);
    let row = page.locator(".user-task-table [data-studio-row-interactive]").first();
    await feedback(page, row, row.locator(".studio-row-link"), info, "task-");
    await row.press("Enter");
    await expect(page.getByRole("region", { name: /^Review purchase request/ })).toBeVisible();
    await page.goto(`/user-tasks-ux.html?forms&populated-forms&theme=${theme}`);
    row = page.locator(".user-task-forms-table [data-studio-row-interactive]").first();
    await feedback(page, row, row.locator(".studio-row-link"), info, "form-");
    await row.locator("td").nth(1).click();
    await expect(page.getByRole("region", { name: "Published form", exact: true })).toBeVisible();
    await page.getByRole("navigation", { name: "Breadcrumb", exact: true }).getByRole("button", { name: "Forms", exact: true }).click();
    await expect(row.locator(".studio-row-link")).toBeFocused();
  });

  test(`secret rows open without triggering actions in ${theme}`, async ({ page }, info) => {
    await page.addInitScript(theme => localStorage.setItem("elsa-studio-theme-mode", theme), theme);
    await page.goto("/secret-scopes.html?scenario=editor");
    const row = page.locator(".secrets-table [data-studio-row-interactive]").first();
    await feedback(page, row, row.locator(".studio-row-link"), info);
    await row.getByRole("button", { name: /^Copy / }).first().click();
    await expect(page.locator(".secrets-table")).toBeVisible();
    await row.locator("td").nth(1).click();
    await expect(page.getByRole("region", { name: "Edit secret", exact: true })).toBeVisible();
    await expect(page.getByTestId("secret-mutation-requests")).toHaveText("0");
  });

  test(`backend rows open details in ${theme}`, async ({ page }, info) => {
    await page.addInitScript(theme => localStorage.setItem("elsa-studio-theme-mode", theme), theme);
    await page.route("**/authoring/capabilities", route => route.fulfill({ json: { capabilities: [] } }));
    await page.goto("/backendConnections.html");
    const region = page.getByRole("region", { name: "Backends table", exact: true });
    await expect(page.locator(".backends-list")).toHaveClass(/studio-table/);
    await expect(region).toHaveAttribute("tabindex", "0");
    const row = page.locator(".backends-list [data-studio-row-interactive]").first();
    await feedback(page, row, row.locator(".studio-row-link"), info);
    await page.setViewportSize({ width: 390, height: 900 });
    expect(await region.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
    await region.focus();
    await expect(region).toBeFocused();
    await row.press("Space");
    await expect(page.getByRole("tab", { name: "Connection", exact: true })).toBeVisible();
  });

  test(`trace rows preserve selection and table semantics in ${theme}`, async ({ page }, info) => {
    await page.goto(`/row-interactions.html?view=traces&theme=${theme}`);
    const table = page.getByRole("table", { name: "Trace summaries", exact: true });
    const rows = table.locator("[data-studio-row-interactive]");
    const row = rows.nth(1);
    await expect(rows.first()).toHaveAttribute("aria-selected", "true");
    await expect(row.getByRole("cell")).toHaveCount(6);
    await feedback(page, row, undefined, info);
    await row.press("Enter");
    await expect(row).toHaveAttribute("aria-selected", "true");
    await expect(rows.first()).toHaveAttribute("aria-selected", "false");
    await rows.first().press("Space");
    await expect(rows.first()).toHaveAttribute("aria-selected", "true");
  });
}
