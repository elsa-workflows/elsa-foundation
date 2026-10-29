import { expect, test } from "@playwright/test";

for (const host of ["workflow", "activity"]) {
  test(`${host} activity choices follow the destination scope`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=sequence&palette-context&theme=dark`);
    const search = page.getByRole("searchbox", { name: "Search activity palette" });
    await search.fill("BPMN");
    await expect(page.getByRole("treeitem", { name: /^BPMN Process,/ })).toBeVisible();
    await expect(page.getByRole("treeitem", { name: /^BPMN Decision,/ })).toHaveCount(0);
    await page.getByRole("treeitem", { name: /^BPMN Process,/ }).click();
    await page.getByLabel("BPMN Process child slots", { exact: true }).getByRole("button", { name: /^Open Activities/ }).click();
    await expect(page.getByRole("searchbox", { name: "Search element palette" })).toBeVisible();
    await expect(page.getByRole("treeitem", { name: /^BPMN Decision,/ })).toBeVisible();
    await page.locator(".wf-breadcrumb").getByRole("button", { name: "Root", exact: true }).click();
    await search.fill("Complete");
    await expect(page.getByRole("treeitem", { name: /^Complete,/ })).toHaveCount(host === "activity" ? 1 : 0);

    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&theme=dark`);
    await page.locator(".wf-palette-search input").fill("BPMN Decision");
    await expect(page.getByRole("treeitem", { name: /^BPMN Decision,/ })).toBeVisible();
  });
}

for (const host of ["workflow", "activity"]) {
  test(`${host} elements share search, keyboard insertion and native drag placement`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&palette-context&theme=dark`);
    const search = page.getByRole("searchbox", { name: "Search element palette" });
    await search.fill("Start event");
    await search.press("ArrowDown");
    await page.keyboard.press("ArrowDown");
    await page.keyboard.press("Enter");
    await expect(page.locator(".wf-bpmn-startEvent")).toHaveCount(1);
    await search.fill("End event");
    const end = page.getByRole("treeitem", { name: "End event", exact: true });
    await end.dragTo(page.locator(".react-flow__pane"), { targetPosition: { x: 500, y: 300 } });
    await expect(page.locator(".wf-bpmn-endEvent")).toHaveCount(1);
    await search.fill("xor");
    await page.getByRole("treeitem", { name: "Exclusive gateway", exact: true }).click();
    await expect(page.locator(".wf-bpmn-exclusiveGateway")).toHaveCount(1);
    await search.fill("BPMN");
    await expect(page.getByRole("tree", { name: "Available elements" }).getByRole("treeitem", { level: 2 })).toHaveCount(9);
    await expect(page.locator("[data-element-icon]")).toHaveCount(7);
    await page.screenshot({ path: testInfo.outputPath(`${host}-elements-dark.png`) });
  });
}

for (const theme of ["classic", "meridian", "drift", "schematic", "atelier", "elsa-cloud", "signal", "dusk"]) {
  for (const appearance of ["light", "dark"]) {
    test(`BPMN elements use shared palette rows in ${theme} ${appearance}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: 1440, height: 1000 });
      await page.goto(`/?mode=activity-duplicate-editor&composition=bpmn&palette-context&palette-theme=${theme}&appearance=${appearance}`);
      const search = page.getByRole("searchbox", { name: "Search element palette" });
      await search.fill("BPMN");
      const tree = page.getByRole("tree", { name: "Available elements" });
      await expect(tree.getByRole("treeitem", { level: 2 })).toHaveCount(9);
      const shape = tree.getByRole("treeitem", { name: "Start event", exact: true });
      const activity = tree.getByRole("treeitem", { name: /^BPMN Process,/ });
      for (const item of [shape, activity]) {
        expect(await item.evaluate(element => element.getBoundingClientRect().height)).toBeGreaterThanOrEqual(32);
        expect(await item.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
      }
      await shape.click();
      await expect(page.locator(".wf-bpmn-startEvent")).toHaveCount(1);
      await page.locator(".wf-palette-body").screenshot({ path: testInfo.outputPath(`elements-${theme}-${appearance}.png`) });
    });
  }
}
