import { expect, test, type Locator, type Page } from "@playwright/test";

/** A real down/up pair catches focus-induced movement that DOM click() cannot reproduce. */
async function toggleWithPointer(page: Page, checkbox: Locator) {
  await checkbox.scrollIntoViewIfNeeded();
  const before = await checkbox.boundingBox();
  if (!before) throw new Error("The checkbox must have a visible box.");
  const point = { x: before.x + before.width / 2, y: before.y + before.height / 2 };
  await page.mouse.move(point.x, point.y);
  await page.mouse.down();
  const afterFocus = await checkbox.boundingBox();
  if (!afterFocus) throw new Error("The checkbox must remain visible after pointer focus.");
  expect(Math.abs(afterFocus.y - before.y)).toBeLessThanOrEqual(1);
  expect(Math.abs(afterFocus.x - before.x)).toBeLessThanOrEqual(1);
  await page.mouse.up();
  await expect(checkbox).toBeChecked();
}

for (const width of [320, 360, 1280]) {
  test(`native dashboard checkboxes keep pointer targets while focus changes at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/dashboard-checkboxes.html");
    const grouping = page.getByRole("group", { name: "Group by", exact: true });
    const status = grouping.getByRole("checkbox", { name: "Current status", exact: true });
    const runKind = grouping.getByRole("checkbox", { name: "Run kind", exact: true });
    await status.check();
    await toggleWithPointer(page, runKind);
    await expect(page.getByLabel("Selected grouping fields")).toHaveText("status, runKind");
    await runKind.press("Space");
    await expect(runKind).not.toBeChecked();
    await expect(page.getByLabel("Selected grouping fields")).toHaveText("status");

    await page.getByRole("button", { name: "Open tile settings", exact: true }).click();
    const dialog = page.getByRole("dialog", { name: "Tile presentation and parameters", exact: true });
    const category = dialog.getByRole("group", { name: "category (string)", exact: true }).getByRole("checkbox");
    const minimum = dialog.getByRole("group", { name: "minimum (integer)", exact: true }).getByRole("checkbox");
    await category.check();
    await toggleWithPointer(page, minimum);
    await page.getByRole("button", { name: "Use tile settings", exact: true }).click();
    await expect(dialog).toHaveCount(0);
    await expect(page.getByLabel("Tile parameter overrides")).toHaveText('{"category":{"type":"string","literal":"retail"},"minimum":{"type":"integer","literal":"1"}}');
  });
}


async function headingGeometry(heading: Locator, actions: Locator) {
  const actionBoxes = await actions.evaluateAll(elements => elements.map(element => element.getBoundingClientRect().toJSON()));
  return heading.evaluate((element, buttons) => {
    const range = document.createRange(); range.selectNodeContents(element);
    const bounds = element.getBoundingClientRect();
    const text = [...range.getClientRects()];
    return {
      fits: element.scrollWidth <= element.clientWidth + 1 && text.every(rect => rect.left >= bounds.left - 1 && rect.right <= bounds.right + 1),
      overlapsAction: text.some(rect => buttons.some(button => rect.left < button.right && rect.right > button.left && rect.top < button.bottom && rect.bottom > button.top))
    };
  }, actionBoxes);
}

for (const width of [320, 360, 1280]) {
  test(`legal unbroken dashboard names wrap clear of editor actions at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/dashboard-checkboxes.html?longName");
    const heading = page.getByRole("heading", { name: `Metric${"A".repeat(164)}`, level: 2, exact: true });
    await expect(heading).toBeVisible();
    const geometry = await headingGeometry(heading, page.locator(".studio-page-actions button"));
    expect(geometry.fits).toBe(true); expect(geometry.overlapsAction).toBe(false);
    await expect(page.getByRole("button", { name: "Save layout", exact: true })).toBeInViewport();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    await info.attach(`dashboard-long-name-${width}px`, { body: await page.screenshot({ path: info.outputPath(`dashboard-long-name-${width}px.png`) }), contentType: "image/png" });
  });

  test(`long dashboard sharing titles keep the modal and keyboard controls inside ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/dashboard-checkboxes.html?longName");
    const share = page.getByRole("button", { name: "Share", exact: true });
    await share.click();
    const title = `Share Metric${"A".repeat(164)}`;
    const dialog = page.getByRole("dialog", { name: title, exact: true });
    const close = dialog.getByRole("button", { name: `Close ${title}`, exact: true });
    await expect(dialog.getByRole("button", { name: "Save audience", exact: true })).toBeEnabled();
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    const geometry = await headingGeometry(dialog.getByRole("heading", { name: title, exact: true }), close);
    expect(geometry.fits).toBe(true); expect(geometry.overlapsAction).toBe(false);
    await expect(close).toBeInViewport();
    await info.attach(`dashboard-long-name-share-${width}px`, { body: await page.screenshot({ path: info.outputPath(`dashboard-long-name-share-${width}px.png`) }), contentType: "image/png" });
    const search = dialog.getByRole("textbox", { name: "Find authorized users or roles", exact: true });
    await search.focus(); await search.fill("viewer");
    expect(await dialog.evaluate(element => element.scrollLeft)).toBe(0);
    await close.focus(); await page.keyboard.press("Shift+Tab");
    await expect(dialog.getByRole("button", { name: "Save audience", exact: true })).toBeFocused();
    await page.keyboard.press("Tab"); await expect(close).toBeFocused();
    await close.press("Escape"); await expect(dialog).toHaveCount(0); await expect(share).toBeFocused();
  });
}

test("normal native dashboard headings retain their desktop action row", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 1000 });
  await page.goto("/dashboard-checkboxes.html?longName&shortName");
  const title = await page.getByRole("heading", { name: "Operations", level: 2, exact: true }).boundingBox();
  const action = await page.getByRole("button", { name: "Save layout", exact: true }).boundingBox();
  if (!title || !action) throw new Error("The title and primary action must have visible boxes.");
  expect(Math.abs(title.y - action.y)).toBeLessThanOrEqual(1);
});


for (const width of [320, 360, 1280]) {
  test(`source-record drill tables keep words readable and scroll inside the modal at ${width}px`, async ({ page }, info) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/dashboard-checkboxes.html?sourceRecords");
    const open = page.getByRole("button", { name: "View matching records", exact: true });
    await open.click();
    const dialog = page.getByRole("dialog", { name: "Matching source records", exact: true });
    const region = dialog.getByRole("region", { name: "Exact result values", exact: true });
    const close = dialog.getByRole("button", { name: "Close Matching source records", exact: true });
    await expect(region.getByRole("cell", { name: "publishedRun", exact: true })).toBeVisible();
    await info.attach(`dashboard-source-records-${width}px`, { body: await page.screenshot({ path: info.outputPath(`dashboard-source-records-${width}px.png`) }), contentType: "image/png" });
    for (const value of ["completed", "publishedRun"]) {
      const lines = await region.getByRole("cell", { name: value, exact: true }).evaluate(element => {
        const range = document.createRange(); range.selectNodeContents(element);
        return new Set([...range.getClientRects()].map(rect => rect.top.toFixed(1))).size;
      });
      expect(lines, `${value} must remain a single readable word`).toBe(1);
    }
    const headersReadable = await region.getByRole("columnheader").evaluateAll(elements => elements.every(element => {
      const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
      let node;
      while ((node = walker.nextNode())) {
        for (const word of (node.textContent ?? "").matchAll(/\S+/g)) {
          const range = document.createRange(); range.setStart(node, word.index!); range.setEnd(node, word.index! + word[0].length);
          if (new Set([...range.getClientRects()].map(rect => rect.top.toFixed(1))).size > 1) return false;
        }
      }
      return true;
    }));
    expect(headersReadable).toBe(true);
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth + 1)).toBe(true);
    expect(await region.evaluate(element => element.scrollWidth > element.clientWidth)).toBe(true);
    await expect(close).toBeInViewport();
    await region.focus(); await page.keyboard.press("ArrowRight");
    await expect.poll(() => region.evaluate(element => element.scrollLeft)).toBeGreaterThan(0);
    await region.getByRole("cell", { name: "publishedRun", exact: true }).scrollIntoViewIfNeeded();
    await region.getByRole("cell", { name: "completed", exact: true }).scrollIntoViewIfNeeded();
    expect(await dialog.evaluate(element => element.scrollLeft)).toBe(0);
    await info.attach(`dashboard-source-records-scrolled-${width}px`, { body: await page.screenshot({ path: info.outputPath(`dashboard-source-records-scrolled-${width}px.png`) }), contentType: "image/png" });
    await page.keyboard.press("Tab"); await expect(close).toBeFocused();
    await close.press("Escape"); await expect(dialog).toHaveCount(0); await expect(open).toBeFocused();
  });
}
