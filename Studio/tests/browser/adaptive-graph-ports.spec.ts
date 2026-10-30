import { expect, test, type Locator, type Page } from "@playwright/test";

test.setTimeout(60_000);

const node = (page: Page, id: string) => page.locator(`.react-flow__node[data-id="${id}"]`).first();
const source = (page: Page, id = "write-one", port = "Done") => node(page, id).locator(`.source[data-handleid="${port}"]`);
const target = (page: Page, id = "write-two") => node(page, id).locator(".target");
const side = (handle: Locator, value: string) => expect(handle).toHaveAttribute("data-handlepos", value);

async function drag(page: Page, element: Locator, dx: number, dy: number) {
  const box = (await element.boundingBox())!;
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width / 2 + dx, box.y + box.height / 2 + dy, { steps: 20 });
  await page.mouse.up();
}

async function verticalEdge(page: Page) {
  const edge = page.locator('.react-flow__edge .react-flow__edge-path').first();
  await expect.poll(async () => edge.evaluate(element => {
    const path = element as SVGPathElement;
    const a = path.getPointAtLength(0), b = path.getPointAtLength(path.getTotalLength());
    return Math.abs(a.x - b.x) < 1 && b.y > a.y && path.getTotalLength() < b.y - a.y + 2;
  })).toBe(true);
}

for (const host of ["workflow", "activity"]) {
  for (const theme of ["classic", "dark"]) {
    test(`${host} ports follow dragging, history and reload (${theme})`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: 1500, height: 950 });
      await page.goto(`/?mode=activity-duplicate-editor&host=${host}&theme=${theme}&adaptive-ports=vertical`);
      await side(source(page), "bottom");
      await side(target(page), "top");
      await verticalEdge(page);
      await expect(node(page, "write-one").locator('.wf-node-port-label[data-side="bottom"]')).toHaveText("Done");
      await page.screenshot({ path: testInfo.outputPath(`${host}-${theme}.png`) });

      await drag(page, node(page, "write-two").locator(".wf-node-content"), 320, 0);
      await side(source(page), "right");
      await side(target(page), "left");
      await page.getByRole("button", { name: /^Undo(?: Activity Graph edit)?$/ }).click();
      await side(source(page), "bottom");
      await side(target(page), "top");
      await page.getByRole("button", { name: /^Redo(?: Activity Graph edit)?$/ }).click();
      await side(source(page), "right");
      if (host === "workflow") await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect.poll(() => page.evaluate(() => Object.keys(sessionStorage).some(key => key.startsWith("adaptive-ports:")))).toBe(true);
      await page.reload();
      await side(source(page), "right");
      await side(target(page), "left");
      const saved = await page.evaluate(() => JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find(key => key.startsWith("adaptive-ports:"))!)!));
      expect(JSON.stringify(saved)).not.toMatch(/labelWidth|sideChangeMargin|"offset"|"side"/);
      expect(JSON.stringify(saved)).toContain('"port":"Done"');
    });
  }
}

test("named branches choose independent sides and labels follow the actual handles", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1500, height: 950 });
  await page.goto("/?mode=activity-duplicate-editor&adaptive-ports=branches&theme=classic");
  await side(source(page, "write-one", "True"), "bottom");
  await side(source(page, "write-one", "False"), "right");
  await side(target(page), "top");
  await side(target(page, "write-three"), "left");
  await expect(node(page, "write-one").locator('.wf-node-port-label[data-side="bottom"]')).toHaveText("True");
  await expect(node(page, "write-one").locator('.wf-node-port-label[data-side="right"]')).toHaveText("False");
  await page.screenshot({ path: testInfo.outputPath("branches.png") });
});

test("draws a new connection from an adaptive handle and retains its logical outcome", async ({ page }) => {
  await page.setViewportSize({ width: 1500, height: 950 });
  await page.goto("/?mode=activity-duplicate-editor&adaptive-ports=vertical&theme=classic");
  await side(source(page), "bottom");
  await source(page).dragTo(target(page, "write-three"));
  await expect(page.locator(".react-flow__edge")).toHaveCount(2);
  await page.getByRole("button", { name: "Save", exact: true }).click();
  await page.reload();
  await expect(page.locator(".react-flow__edge")).toHaveCount(2);
  const connections = await page.evaluate(() => {
    const draft = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find(key => key.startsWith("adaptive-ports:"))!)!);
    return draft.state.rootActivity.structure.payload.connections;
  });
  expect(connections).toEqual(expect.arrayContaining([expect.objectContaining({ source: { nodeId: "write-one", port: "Done" }, target: { nodeId: "write-three" } })]));
});

test("reconnects an adaptive edge and resets its disconnected input", async ({ page }) => {
  await page.goto("/?mode=activity-duplicate-editor&adaptive-ports=vertical&theme=classic");
  await side(source(page), "bottom");
  const updater = page.locator('.react-flow__edge[data-id="flow-write-one-write-two"] .react-flow__edgeupdater-target');
  await updater.dragTo(target(page, "write-three"));
  await side(source(page), "right");
  await side(target(page), "left");
  await expect(page.locator(".react-flow__edge")).toHaveCount(1);
  await page.getByRole("button", { name: "Save", exact: true }).click();
  const connections = await page.evaluate(() => JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find(key => key.startsWith("adaptive-ports:"))!)!).state.rootActivity.structure.payload.connections);
  expect(connections).toEqual([expect.objectContaining({ source: { nodeId: "write-one", port: "Done" }, target: { nodeId: "write-three" } })]);
});

for (const theme of ["classic", "dark"]) {
  test(`read-only Run uses the same ports and edge geometry (${theme})`, async ({ page }, testInfo) => {
    await page.goto(`/?mode=activity-duplicate-editor&host=instance&run-canvas=unique&adaptive-ports=vertical&theme=${theme}`);
    await side(source(page), "bottom");
    await side(target(page), "top");
    await verticalEdge(page);
    await expect(node(page, "write-one")).not.toHaveClass(/draggable/);
    await source(page).dragTo(target(page, "write-three"));
    await expect(page.locator(".react-flow__edge")).toHaveCount(1);
    expect(await page.evaluate(() => Object.keys(sessionStorage).some(key => key.startsWith("adaptive-ports:")))).toBe(false);
    await page.evaluate(() => { document.documentElement.dataset.themeLayout = "floating"; });
    await verticalEdge(page);
    await side(source(page), "bottom");
    await page.screenshot({ path: testInfo.outputPath(`runtime-${theme}.png`) });
  });
}

for (const host of ["workflow", "activity"]) {
  test(`BPMN ${host} shares adaptive geometry and preserves flow handles`, async ({ page }) => {
    await page.setViewportSize({ width: 1500, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&host=${host}&composition=bpmn&adaptive-ports=vertical&theme=dark`);
    await side(source(page, "write-one", "flow"), "bottom");
    await side(target(page), "top");
    await drag(page, node(page, "write-two").locator(".wf-bpmn-task"), 320, 0);
    await side(source(page, "write-one", "flow"), "right");
    await side(target(page), "left");
  });
}
