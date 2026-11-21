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
    await page.addInitScript(() => localStorage.setItem("elsa-studio-theme-mode", "light"));
    await page.goto("/golden-runs.html?workbench-shell=1");
    await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "light");
    await page.getByRole("button", { name: "Inspect workflow run run-01 · Billing workflow", exact: true }).click();
    const header = page.locator(".wf-detail-header");
    const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
    await expect(canvas).toBeVisible();
    const pageBox = (await page.locator(".golden-runs-page").boundingBox())!;
    await expect(page.locator(".studio-page-header")).toHaveCount(0);
    const heading = header.getByRole("heading", { name: "Run · Billing workflow", exact: true });
    await expect(heading).toHaveAccessibleName("Run · Billing workflow");
    await expect(heading).toHaveAttribute("title", "Run · Billing workflow");
    await expect(page.getByRole("heading", { name: "Run · Billing workflow", exact: true })).toHaveCount(1);
    await expect(header.getByRole("button", { name: "Refresh execution", exact: true })).toBeVisible();
    await expect(header.getByRole("button", { name: "Refresh execution", exact: true })).toHaveAttribute("data-size", "sm");
    await expect(header.getByText("Running", { exact: true })).toBeVisible();
    await expect(canvas.locator(":scope > header")).toHaveCount(0);
    await expect(canvas.locator(".wf-instance-breadcrumb")).toHaveCount(0);
    await expect(canvas.locator(".wf-execution-scope")).toHaveCount(0);
    const titleBox = (await heading.boundingBox())!;
    expect(titleBox.height).toBeLessThanOrEqual(32);
    const actionsBox = (await header.locator(".wf-detail-header__actions").boundingBox())!;
    const breadcrumbBox = (await header.getByRole("link", { name: "Runs", exact: true }).boundingBox())!;
    const canvasBox = (await canvas.boundingBox())!;
    const headerBox = (await header.boundingBox())!;
    expect(canvasBox.height).toBeGreaterThan(600);
    expect(Math.abs(canvasBox.y + canvasBox.height - 1200)).toBeLessThanOrEqual(1);
    expect(titleBox.x - pageBox.x).toBeGreaterThanOrEqual(16);
    expect(titleBox.y - pageBox.y).toBeGreaterThanOrEqual(8);
    expect(pageBox.x + pageBox.width - actionsBox.x - actionsBox.width).toBeGreaterThanOrEqual(12);
    expect(breadcrumbBox.y - (await header.boundingBox())!.y).toBeLessThanOrEqual(48);
    expect(canvasBox.y - headerBox.y - headerBox.height).toBeLessThanOrEqual(1);
    expect(canvasBox.y - breadcrumbBox.y - breadcrumbBox.height).toBeLessThanOrEqual(width < 600 ? 180 : 24);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: test.info().outputPath("run-layout.png"), fullPage: true });
  });
}

test("Run workspace hierarchy stays compact in dark mode", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.addInitScript(() => localStorage.setItem("elsa-studio-theme-mode", "dark"));
  await page.goto("/golden-runs.html?workbench-shell=1");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "dark");
  await page.getByRole("button", { name: "Inspect workflow run run-01 · Billing workflow", exact: true }).click();

  const header = page.locator(".wf-detail-header");
  const canvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
  await expect(header.getByRole("link", { name: "Runs", exact: true })).toBeVisible();
  await expect(header.getByRole("button", { name: "Refresh execution", exact: true })).toBeVisible();
  await expect(canvas.locator(".wf-instance-breadcrumb")).toHaveCount(0);
  await expect(canvas.locator(".wf-execution-scope")).toHaveCount(0);
  await page.screenshot({ path: test.info().outputPath("run-layout-dark.png"), fullPage: true });
});

for (const width of [390, 1440]) {
  test(`Executable Inspector uses the Definition workbench frame and keeps actions reachable (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/?mode=activity-duplicate-editor&host=executable&run-canvas=unique&theme=light");

    const pageRoot = page.locator(".wf-page--executable-inspector");
    const header = page.locator(".wf-executable-toolbar");
    const workbench = page.locator(".wf-executable-workbench");
    const canvas = page.getByRole("region", { name: "Executable canvas", exact: true });
    const details = page.getByRole("complementary", { name: "Executable details panel", exact: true });
    await expect(header.getByRole("heading", { name: "Executable editor-artifact", exact: true })).toBeVisible();
    await expect(canvas).toBeVisible();
    await expect(workbench).toHaveClass(/graph-authoring-workspace/);
    await expect(workbench.locator(".wf-palette")).toHaveCount(0);
    await expect(workbench.locator(".wf-canvas-tools")).toHaveCount(0);
    await expect(workbench.locator("[data-graph-authoring-resource]")).toHaveCount(0);
    await expect(header.getByRole("link", { name: "Executables", exact: true })).toBeVisible();
    await expect(header.getByRole("button", { name: "Refresh executable", exact: true })).toBeVisible();
    await expect(header.getByRole("button", { name: /Open source definition unavailable/ })).toBeVisible();
    await expect(canvas.locator(".wf-instance-breadcrumb")).toHaveCount(0);
    const expandPanel = details.getByRole("button", { name: "Expand inspector panel", exact: true });
    if (await expandPanel.count()) await expandPanel.click();
    await expect(details.getByRole("tab", { name: "Identity", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(details.getByRole("button", { name: "Copy Artifact ID", exact: true })).toBeVisible();
    const provenance = details.locator(".wf-executable-layout-provenance");
    await expect(provenance).toHaveText("No source reference; automatic layout");
    await expect(provenance).toHaveCSS("white-space", "normal");
    await expect(provenance).toHaveCSS("overflow-wrap", "anywhere");
    expect(await provenance.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true);
    await provenance.scrollIntoViewIfNeeded();
    await expect(provenance).toBeInViewport();
    if (await details.getByRole("button", { name: "Collapse inspector panel", exact: true }).count()) {
      await details.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
      await canvas.locator(".react-flow__node").first().click();
      await expect(details.getByRole("tab", { name: "Activity", exact: true })).toHaveAttribute("aria-selected", "true");
      await expect(details.getByText("Activity details", { exact: true })).toBeVisible();
    } else {
      await canvas.locator(".react-flow__node").first().click();
      await expect(details.getByText("Activity details", { exact: true })).toBeVisible();
    }
    const pageBox = (await pageRoot.boundingBox())!;
    const workbenchBox = (await workbench.boundingBox())!;
    expect(workbenchBox.x - pageBox.x).toBeGreaterThanOrEqual(width < 600 ? 6 : 20);
    expect(workbenchBox.y + workbenchBox.height).toBeLessThanOrEqual(pageBox.y + pageBox.height);
    expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    await page.screenshot({ path: test.info().outputPath("executable-inspector-layout.png"), fullPage: true });
  });
}

test("Executable Inspector confirms Explain with a toast without moving the canvas", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/?mode=activity-duplicate-editor&host=executable&run-canvas=unique&toolbar-explain=1&theme=light");

  const canvas = page.getByRole("region", { name: "Executable canvas", exact: true });
  await expect(canvas).toBeVisible();
  const canvasTop = (await canvas.boundingBox())!.y;

  await page.getByRole("button", { name: "Explain", exact: true }).click();

  const toast = page.getByRole("region", { name: "Notifications" }).locator(".studio-toast");
  await expect(toast).toContainText("Sent editor-artifact to Weaver");
  await expect(toast).toHaveAttribute("data-level", "success");
  await expect(page.locator(".wf-status-line")).toHaveCount(0);
  expect((await canvas.boundingBox())!.y).toBe(canvasTop);
});

test("Executable Inspector keeps the shared workbench surfaces coherent in dark mode", async ({ page }) => {
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.addInitScript(() => localStorage.setItem("elsa-studio-theme-mode", "dark"));
  await page.goto("/?mode=activity-duplicate-editor&host=executable&run-canvas=unique&theme=dark");

  const workbench = page.locator(".wf-executable-workbench");
  await expect(page.locator("html")).toHaveAttribute("data-theme-mode", "dark");
  await expect(workbench).toBeVisible();
  await expect(workbench).toHaveCSS("border-radius", "12px");
  await expect(page.getByRole("region", { name: "Executable canvas", exact: true })).toBeVisible();
  await page.screenshot({ path: test.info().outputPath("executable-inspector-layout-dark.png"), fullPage: true });
});

for (const width of [390, 1440]) {
  test(`Executable Inspector aligns copyable Identity and Source fields (${width}px)`, async ({ page, context }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/?mode=activity-duplicate-editor&host=executable&run-canvas=unique&identity-fields=1&theme=light");

    const details = page.getByRole("complementary", { name: "Executable details panel", exact: true });
    await expect(page.locator(".wf-executable-toolbar h2")).toBeVisible();
    const expandPanel = details.getByRole("button", { name: "Expand inspector panel", exact: true });
    if (await expandPanel.count()) await expandPanel.click();
    await expect(details.getByRole("tab", { name: "Identity", exact: true })).toHaveAttribute("aria-selected", "true");

    const identity = details.getByRole("heading", { name: "Identity", exact: true }).locator("xpath=..");
    const source = details.getByRole("heading", { name: "Source", exact: true }).locator("xpath=..");
    const artifactHash = "sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
    const identityHashRow = identity.locator("dt").filter({ hasText: "Artifact hash" });
    const identityHashValue = identityHashRow.locator("xpath=following-sibling::dd[1]");
    const definitionIdRow = source.locator("dt").filter({ hasText: "Definition ID" });
    const definitionId = "definition-reference-0123456789abcdef0123456789abcdef";
    const definitionIdValue = definitionIdRow.locator("xpath=following-sibling::dd[1]");
    const definitionVersionRow = source.locator("dt").filter({ hasText: "Definition version ID" });
    const definitionVersionValue = definitionVersionRow.locator("xpath=following-sibling::dd[1]");

    for (const [label, row, value] of [
      ["Artifact ID", identity.locator("dt").filter({ hasText: "Artifact ID" }), identity.locator("dt").filter({ hasText: "Artifact ID" }).locator("xpath=following-sibling::dd[1]")],
      ["Artifact hash", identityHashRow, identityHashValue],
      ["Definition ID", definitionIdRow, definitionIdValue],
      ["Definition version ID", definitionVersionRow, definitionVersionValue]
    ] as const) {
      await expect(row).toHaveCount(1);
      await expect(value.locator(".studio-copyable-identifier")).toHaveAttribute("data-label-placement", "external");
      await expect(value.locator(".studio-copyable-identifier__label")).toHaveCount(0);
      await expect(value.getByRole("button", { name: `Copy ${label}`, exact: true })).toBeVisible();
      expect(await row.boundingBox()).not.toBeNull();
      expect(await value.boundingBox()).not.toBeNull();
    }

    await expect(identityHashValue.locator(".studio-copyable-identifier__value")).toContainText("…");
    await expect(identityHashValue.locator(".studio-copyable-identifier__value")).toHaveAttribute("title", artifactHash);
    await expect(definitionIdValue.locator(".studio-copyable-identifier__value")).toHaveAttribute("title", definitionId);
    const pageWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    expect(pageWidth).toBeLessThanOrEqual(width);
    await page.screenshot({ path: test.info().outputPath(`executable-identity-fields-${width}.png`), fullPage: true });
    await definitionIdValue.getByRole("button", { name: "Copy Definition ID", exact: true }).click();
    await expect.poll(() => page.evaluate(() => navigator.clipboard.readText())).toBe(definitionId);
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
