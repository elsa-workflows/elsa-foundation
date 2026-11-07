import { expect, test, type Locator } from "@playwright/test";

test("run detail fills the shell cell and switches responsive inspector modes", async ({ page }) => {
  await page.setViewportSize({ width: 1200, height: 800 });
  await page.goto("/?theme=light&mode=run-detail");

  const workbench = page.getByTestId("run-workbench");
  const canvas = page.getByRole("region", { name: "Workflow run canvas" });
  const inspector = page.getByRole("complementary", { name: "Run details" });
  const consolePanel = page.locator(".browser-run-console");
  await expect(workbench).toHaveAttribute("data-layout-mode", "desktop");
  await expect(inspector).toBeVisible();

  const [desktopWorkbenchBox, desktopCanvasBox, desktopInspectorBox, consoleBox] = await Promise.all([
    workbench.boundingBox(),
    canvas.boundingBox(),
    inspector.boundingBox(),
    consolePanel.boundingBox()
  ]);
  expect(desktopWorkbenchBox).not.toBeNull();
  expect(desktopCanvasBox?.width).toBeGreaterThanOrEqual(480);
  expect(desktopInspectorBox?.width).toBe(400);
  expect(Math.abs((desktopWorkbenchBox!.y + desktopWorkbenchBox!.height) - consoleBox!.y)).toBeLessThanOrEqual(1);

  await page.setViewportSize({ width: 700, height: 800 });
  await expect(workbench).toHaveAttribute("data-layout-mode", "medium");
  await expect(inspector).toBeHidden();
  await page.getByRole("button", { name: "Select activity" }).click();
  await expect(inspector).toBeVisible();
  await expect(inspector).toHaveCSS("position", "absolute");

  await page.getByRole("button", { name: "Close details" }).click();
  await page.setViewportSize({ width: 460, height: 800 });
  await expect(workbench).toHaveAttribute("data-layout-mode", "narrow");
  await expect(canvas).toBeVisible();
  await expect(inspector).toBeHidden();
  await page.getByRole("button", { name: "Select activity" }).click();
  await expect(canvas).toBeHidden();
  await expect(inspector).toBeVisible();
});

for (const width of [390, 1440, 2400]) {
  test(`run page gives its heading breathing room and keeps actions compact (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1200 });
    await page.goto("/golden-runs.html?workbench-shell=1");
    await page.getByRole("button", { name: "Inspect workflow run run-01 · Billing workflow", exact: true }).click();
    await page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Diagram", exact: true }).click();
    const header = page.locator(".studio-page-header");
    const toolbar = page.locator(".wf-toolbar");
    const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
    await expect(canvas).toBeVisible();
    const pageBox = (await page.locator(".golden-runs-page").boundingBox())!;
    const titleBox = (await header.getByRole("heading", { name: "Run", exact: true }).boundingBox())!;
    const contextBox = (await header.locator(".wf-page-context").boundingBox())!;
    const toolbarBox = (await toolbar.boundingBox())!;
    const buttonBox = (await toolbar.getByRole("button", { name: "Runs", exact: true }).boundingBox())!;
    const canvasBox = (await canvas.boundingBox())!;
    expect(canvasBox.height).toBeGreaterThan(600);
    expect(Math.abs(canvasBox.y + canvasBox.height - 1200)).toBeLessThanOrEqual(1);
    expect(titleBox.x - pageBox.x).toBeGreaterThanOrEqual(16);
    expect(titleBox.y - pageBox.y).toBeGreaterThanOrEqual(16);
    expect(pageBox.x + pageBox.width - contextBox.x - contextBox.width).toBeGreaterThanOrEqual(16);
    expect(buttonBox.y - toolbarBox.y).toBeLessThanOrEqual(16);
    const viewNavigationBox = (await page.getByRole("navigation", { name: "Run workspace views", exact: true }).boundingBox())!;
    expect(canvasBox.y - viewNavigationBox.y - viewNavigationBox.height).toBeLessThanOrEqual(24);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: test.info().outputPath("run-layout.png"), fullPage: true });
  });
}

for (const width of [320, 1440]) {
  test(`run history honors the public Minimal control height and keyboard behavior (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/golden-runs.html?workbench-shell=1");
    await page.getByRole("button", { name: "Inspect workflow run run-01 · Billing workflow", exact: true }).click();
    const timeline = page.getByRole("region", { name: "Run timeline", exact: true });
    await expect(timeline).toBeVisible();
    const root = page.locator("html");
    const previousStyle = await root.getAttribute("data-workspace-style");
    // This fixture hosts the actual workbench without Studio's appearance menu. Exercise
    // the documented public host styling contract; its existing SDK data stays unchanged.
    await root.evaluate(element => element.setAttribute("data-workspace-style", "minimal"));
    try {
      const search = timeline.getByRole("searchbox", { name: "Search recorded events", exact: true });
      const submit = timeline.getByRole("button", { name: "Search", exact: true });
      const surfaces: Array<[string, Locator]> = [
        ["Search field", search.locator("xpath=ancestor::label[1]")],
        ["Search surface", search.locator("..")],
        ...["History scope", "History detail", "History category"].map(name => [name, timeline.getByRole("combobox", { name, exact: true })] as [string, Locator]),
        ["Search button", submit],
        ["Refresh button", timeline.getByRole("button", { name: "Refresh run history", exact: true })],
        ["Live button", timeline.getByRole("button", { name: "Pause live updates", exact: true })],
        ["Follow button", timeline.getByRole("button", { name: "Follow latest", exact: true })]
      ];
      for (const [name, control] of surfaces) {
        await expect(control).toBeVisible();
        const bounds = await control.evaluate(element => ({
          height: element.getBoundingClientRect().height,
          roleHeight: Number.parseFloat(getComputedStyle(element).getPropertyValue("--studio-control-height"))
        }));
        expect(bounds.roleHeight, `${name}: the supported Minimal density is active`).toBe(36);
        // Larger touch targets remain valid; local chrome must not shrink the public role.
        expect(bounds.height + 0.5, `${name}: rendered height honors the public role`).toBeGreaterThanOrEqual(bounds.roleHeight);
      }
      await search.focus();
      await page.keyboard.press("Tab");
      await expect(submit).toBeFocused();
      await expect(submit).toHaveCSS("outline-style", "solid");
      await expect(submit).toHaveCSS("outline-width", "2px");
      await page.keyboard.press("Shift+Tab");
      await expect(search).toBeFocused();
      await search.fill("bookmark");
      expect(new URL(page.url()).searchParams.get("historySearch")).toBeNull();
      await page.keyboard.press("Tab");
      await page.keyboard.press("Enter");
      await expect.poll(() => new URL(page.url()).searchParams.get("historySearch")).toBe("bookmark");
      await timeline.getByRole("button", { name: "Clear filters", exact: true }).press("Enter");
      await expect(search).toHaveValue("");
      await expect.poll(() => new URL(page.url()).searchParams.get("historySearch")).toBeNull();
      const live = timeline.getByRole("button", { name: "Pause live updates", exact: true });
      await live.press("Enter");
      await expect(timeline.getByRole("button", { name: "Resume live updates", exact: true })).toHaveAttribute("aria-pressed", "false");
      const follow = timeline.getByRole("button", { name: "Follow latest", exact: true });
      await follow.press("Enter");
      await expect(timeline.getByRole("button", { name: "Following latest", exact: true })).toHaveAttribute("aria-pressed", "true");
      await expect(timeline.getByRole("button", { name: "Resume live updates", exact: true })).toHaveAttribute("aria-pressed", "false");
      await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
      await page.screenshot({ path: test.info().outputPath("run-history-minimal.png"), fullPage: true });
    } finally {
      await root.evaluate((element, previous) => {
        if (previous === null) element.removeAttribute("data-workspace-style");
        else element.setAttribute("data-workspace-style", previous);
      }, previousStyle);
    }
  });
}

test("BPMN run canvas retains the definition's positions instead of element storage order", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  const fixture = "/?mode=activity-duplicate-editor&composition=bpmn&run-layout&theme=light";
  await page.goto(fixture);
  const positions = async () => {
    const result = [];
    for (const id of ["start", "write-task", "end"]) {
      const node = page.locator(`.react-flow__node[data-id="${id}"]`);
      await expect(node).toBeVisible();
      result.push(await node.evaluate(element => (element as HTMLElement).style.transform));
    }
    return result;
  };
  const authored = await positions();
  await page.goto(`${fixture}&host=instance&run-canvas=unique`);
  expect(await positions()).toEqual(authored);
  const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
  const start = (await canvas.locator('.react-flow__node[data-id="start"]').boundingBox())!;
  const task = (await canvas.locator('.react-flow__node[data-id="write-task"]').boundingBox())!;
  const end = (await canvas.locator('.react-flow__node[data-id="end"]').boundingBox())!;
  expect(start.x + start.width).toBeLessThan(task.x);
  expect(task.x + task.width).toBeLessThan(end.x);
  await page.screenshot({ path: test.info().outputPath("bpmn-run-layout.png"), fullPage: true });
});
