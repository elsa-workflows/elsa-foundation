import { expect, test } from "@playwright/test";

for (const theme of ["light", "dark"]) {
  test(`colors Workflow JSON tokens and preserves editing, reset, and scrolling (${theme})`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1440, height: 700 });
    await page.goto(`/?mode=activity-duplicate-editor&theme=${theme}`);
    await page.getByRole("tablist", { name: "Editor view tabs" }).getByRole("tab", { name: "Code", exact: true }).click();

    const codeView = page.locator(".wf-code-view");
    const richSurface = codeView.locator('[data-studio-code-editor-surface="codemirror"]');
    const content = richSurface.locator(".cm-content");
    await expect(content).toBeVisible({ timeout: 15_000 });
    const tokenColors = () => content.locator("span").evaluateAll(spans => Array.from(new Set(spans.map(span => getComputedStyle(span).color))));
    await expect.poll(async () => (await tokenColors()).length).toBeGreaterThan(1);
    await page.screenshot({ path: testInfo.outputPath(`workflow-json-syntax-${theme}.png`) });

    const scroller = richSurface.locator(".cm-scroller");
    const scrollMetrics = await scroller.evaluate(element => ({ scrollHeight: element.scrollHeight, clientHeight: element.clientHeight }));
    expect(scrollMetrics.scrollHeight).toBeGreaterThan(scrollMetrics.clientHeight);
    await scroller.hover();
    await page.mouse.wheel(0, 800);
    await expect.poll(() => scroller.evaluate(element => element.scrollTop)).toBeGreaterThan(0);

    await content.click();
    await content.press("ControlOrMeta+End");
    await page.keyboard.insertText(" ");
    await expect(codeView.getByRole("button", { name: "Apply", exact: true })).toBeEnabled();
    await codeView.getByRole("button", { name: "Reset", exact: true }).click();
    await expect(codeView.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
  });

  for (const dismissal of ["manual", "automatic"]) test(`keeps run status and navigation after ${dismissal} toast dismissal (${theme})`, async ({ page }, testInfo) => {
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&theme=${theme}`);
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    const toast = page.getByRole("group", { name: "Test run started", exact: true });
    await expect(toast).toBeVisible();
    await expect(page.locator(".wf-editor-run-status")).toHaveCount(0);
    if (dismissal === "manual") await toast.getByRole("button", { name: "Dismiss notification" }).click();
    await expect(toast).toHaveCount(0, { timeout: 10_000 });
    const runtime = page.getByRole("region", { name: "Test run summary" });
    await expect(runtime.locator(".wf-status-badge")).toHaveText("Completed");
    await runtime.getByRole("button", { name: "Return to editing", exact: true }).click();
    await runtime.getByRole("button", { name: "Show execution", exact: true }).click();
    await expect(page.getByLabel("Workflow canvas", { exact: true }).getByRole("button", { name: /^Notify buyer\. Node ID:/ })).toHaveAccessibleName(/Run state: Completed/);
    if (dismissal === "manual") await page.screenshot({ path: testInfo.outputPath("runtime-after-toast.png") });
    await runtime.getByRole("button", { name: "Open run", exact: true }).click();
    await expect(page).toHaveURL(/\/workflows\/instances\/editor-execution$/);
  });

  test(`restores the same Test Run explicitly after a reload (${theme})`, async ({ page }) => {
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&theme=${theme}`);
    const node = page.getByLabel("Workflow canvas", { exact: true }).getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    await page.reload();
    await expect(node).toHaveAccessibleName(/State: authoring/);
    await expect(page.getByRole("group", { name: "Execution controls", exact: true })).toHaveCount(0);
    await page.getByRole("tab", { name: "Runtime", exact: true }).click();
    const previous = page.getByRole("group", { name: "Previous Test Run", exact: true });
    await expect(previous).toBeVisible();
    await previous.getByRole("button", { name: "Restore Test Run", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    await expect(previous).toHaveCount(0);
    await expect(page.getByRole("group", { name: "Execution controls", exact: true })).toBeVisible();
    await page.getByRole("tab", { name: "Inspector", exact: true }).click();
    await page.getByRole("tab", { name: "Run data", exact: true }).click();
    await node.click();
    const inspector = page.getByLabel("Inspector panel", { exact: true });
    await inspector.getByRole("tablist", { name: "Activity execution details" }).getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(inspector.getByText("Current: 1", { exact: true })).toBeVisible();
  });

  test(`keeps the existing canvas and viewport when attaching and detaching a run (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=unique&theme=${theme}`);
    const canvas = page.getByLabel("Workflow canvas", { exact: true });
    const node = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await expect(node).toBeVisible();
    const viewport = canvas.locator(".react-flow__viewport");
    await expect(viewport).toHaveAttribute("style", /transform/);
    const original = await viewport.getAttribute("style");
    const bounds = (await canvas.boundingBox())!;
    await page.mouse.move(bounds.x + 120, bounds.y + bounds.height - 70);
    await page.mouse.down();
    await page.mouse.move(bounds.x + 175, bounds.y + bounds.height - 105, { steps: 5 });
    await page.mouse.up();
    await expect(viewport).not.toHaveAttribute("style", original!);
    const transform = await viewport.getAttribute("style");
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    expect(await viewport.getAttribute("style")).toBe(transform);
    const location = page.url();
    await page.getByRole("tab", { name: "Runtime", exact: true }).click();
    await page.getByRole("button", { name: "Return to editing", exact: true }).click();
    await expect(node).toHaveAccessibleName(/State: authoring/);
    expect(await viewport.getAttribute("style")).toBe(transform);
    expect(page.url()).toBe(location);
    await page.getByRole("button", { name: "Show execution", exact: true }).click();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    const capturedPosition = await node.evaluate(element => (element as HTMLElement).style.transform);
    const box = (await node.boundingBox())!;
    await page.mouse.move(box.x + box.width / 2, box.y + 20);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 80, box.y + 90, { steps: 8 });
    await page.mouse.up();
    await page.getByRole("tab", { name: "Runtime", exact: true }).click();
    await expect(page.getByText("The design changed. Run overlays are hidden.")).toBeVisible();
    await expect(node).toHaveAccessibleName(/State: authoring/);
    const editedPosition = await node.evaluate(element => (element as HTMLElement).style.transform);
    expect(editedPosition).not.toBe(capturedPosition);
    await page.getByRole("button", { name: "Inspect captured snapshot", exact: true }).click();
    await expect(page.getByText("Captured snapshot · read only. Your draft edits are preserved.", { exact: true })).toBeVisible();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
    await expect.poll(() => node.evaluate(element => (element as HTMLElement).style.transform)).toBe(capturedPosition);
    // A measured snapshot node must stay visible while the editor re-renders. A hidden frame lets a click
    // fall through to the pane, which clears the selection instead of selecting the node (#4040).
    await node.evaluate(element => {
      const hidden = { count: 0 };
      (window as unknown as { snapshotNodeHidden: typeof hidden }).snapshotNodeHidden = hidden;
      new MutationObserver(records => {
        if (records.some(record => record.oldValue?.includes("visibility: hidden")) || (element as HTMLElement).style.visibility === "hidden") hidden.count += 1;
      }).observe(element, { attributes: true, attributeFilter: ["style"], attributeOldValue: true });
    });
    await node.click();
    await expect(node).toHaveAccessibleName(/Run state: Completed\. Selected\./);
    await page.keyboard.press("Delete");
    await page.keyboard.press("ControlOrMeta+d");
    await expect(node).toHaveCount(1);
    await expect.poll(() => node.evaluate(element => (element as HTMLElement).style.transform)).toBe(capturedPosition);
    await expect(page.getByRole("button", { name: "Undo", exact: true })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Save", exact: true })).toBeDisabled();
    const inspector = page.getByLabel("Inspector panel", { exact: true });
    await inspector.getByRole("tab", { name: "Configuration", exact: true }).click();
    await expect(inspector.locator("fieldset[disabled]")).toHaveCount(1);
    expect(await page.evaluate(() => (window as unknown as { snapshotNodeHidden: { count: number } }).snapshotNodeHidden.count)).toBe(0);
    const editorViews = page.getByRole("tablist", { name: "Editor view tabs" });
    for (const view of ["Properties", "Variables", "Inputs", "Outputs"]) {
      await editorViews.getByRole("tab", { name: view, exact: true }).click();
      await expect(page.locator(".wf-canvas-shell fieldset[disabled]")).toHaveCount(1);
    }
    await page.getByRole("tab", { name: "Code", exact: true }).click();
    await expect(page.getByRole("button", { name: "Apply", exact: true })).toBeDisabled();
    // The inspector can also contain a read-only expression editor for this snapshot.
    await expect(page.locator('.wf-code-view .studio-code-editor[data-readonly="true"]')).toHaveCount(1);
    const snapshotContent = page.locator('.wf-code-view [data-studio-code-editor-surface="codemirror"] .cm-content');
    await expect(snapshotContent).toBeVisible({ timeout: 15_000 });
    await expect(snapshotContent).toHaveAttribute("contenteditable", "false");
    await expect(snapshotContent).toHaveAttribute("aria-readonly", "true");
    await expect(inspector.locator('.studio-code-editor:not([data-readonly="true"])')).toHaveCount(0);
    await page.getByRole("tab", { name: "Designer", exact: true }).click();
    await page.getByRole("tab", { name: "Runtime", exact: true }).click();
    await page.getByRole("button", { name: "Return to editing", exact: true }).click();
    await expect(node).toHaveAccessibleName(/State: authoring/);
    await expect.poll(() => node.evaluate(element => (element as HTMLElement).style.transform)).toBe(editedPosition);
    expect(await viewport.getAttribute("style")).toBe(transform);
    expect(page.url()).toBe(location);
  });

  test(`inspects an explicit repeated occurrence in the same editor (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=repeated&theme=${theme}`);
    const canvas = page.getByLabel("Workflow canvas", { exact: true });
    const node = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(page.getByText(/Some activities ran more than once/)).toBeVisible();
    await page.getByRole("tab", { name: "Inspector", exact: true }).click();
    await page.getByRole("tab", { name: "Run data", exact: true }).click();
    const viewport = await canvas.locator(".react-flow__viewport").getAttribute("style");
    const route = page.url();
    await node.click();
    const inspector = page.getByLabel("Inspector panel", { exact: true });
    await expect(inspector.getByRole("tab", { name: "Run data", exact: true })).toHaveAttribute("aria-selected", "true");
    await expect(inspector.getByText("Choose a recorded execution to inspect its run data.")).toBeVisible();
    await inspector.getByRole("button", { name: "10 recorded executions", exact: false }).click();
    await inspector.getByRole("list", { name: "Recorded activity executions" }).getByRole("button", { name: /^Execution 4 Completed/ }).click();
    await inspector.getByRole("tablist", { name: "Activity execution details" }).getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(inspector.getByText("Current: 4", { exact: true })).toBeVisible();
    await expect(node).toHaveAccessibleName(/Run state: Completed\. Selected/);
    await inspector.getByRole("tab", { name: "Configuration", exact: true }).click();
    await inspector.getByRole("tab", { name: "Run data", exact: true }).click();
    await inspector.getByRole("tablist", { name: "Activity execution details" }).getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(inspector.getByText("Current: 4", { exact: true })).toBeVisible();
    await inspector.getByRole("button", { name: "Execution 4 of 10", exact: false }).click();
    await inspector.getByRole("list", { name: "Recorded activity executions" }).getByRole("button", { name: /^Execution 10 Completed/ }).click();
    await expect(inspector.getByText("Current: 10", { exact: true })).toBeVisible();
    await expect(inspector.getByText("Current: 4", { exact: true })).toHaveCount(0);
    expect(await canvas.locator(".react-flow__viewport").getAttribute("style")).toBe(viewport);
    expect(page.url()).toBe(route);
  });

  test(`opens a second activity's incident while another activity is selected (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=incident&theme=${theme}`);
    const canvas = page.getByLabel("Workflow canvas", { exact: true });
    const first = canvas.getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    const second = canvas.getByRole("button", { name: /^Write Line\. Node ID: write-two/ });
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await expect(second).toHaveAccessibleName(/Run state: Faulted/);
    await first.click();
    await expect(first).toHaveAccessibleName(/Selected/);
    await canvas.getByRole("button", { name: /^Open incidents for Write Line/ }).click();
    const inspector = page.getByLabel("Inspector panel", { exact: true });
    await expect(inspector.getByText("Second activity failed in its exact execution.", { exact: true })).toBeVisible();
    await expect(second).toHaveAccessibleName(/Selected/);
    await expect(first).toHaveAccessibleName(/Not selected/);
  });

  test(`retains confirmed evidence and focus during delayed occurrence navigation (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 1440, height: 950 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=repeated&evidence-latency=1&theme=${theme}`);
    const node = page.getByLabel("Workflow canvas", { exact: true }).getByRole("button", { name: /^Notify buyer\. Node ID:/ });
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    await page.getByRole("tab", { name: "Inspector", exact: true }).click();
    await page.getByRole("tab", { name: "Run data", exact: true }).click();
    await page.getByRole("tab", { name: "Inspector", exact: true }).click();
    await node.click();
    const inspector = page.getByLabel("Inspector panel", { exact: true });
    await inspector.getByRole("button", { name: "10 recorded executions", exact: false }).click();
    await inspector.getByRole("list", { name: "Recorded activity executions" }).getByRole("button", { name: /^Execution 4 Completed/ }).click();
    const activityDetailsTabs = inspector.getByRole("tablist", { name: "Activity execution details" });
    await activityDetailsTabs.getByRole("tab", { name: "Inputs", exact: true }).click();
    await expect(inspector.getByText("Current: 4", { exact: true })).toBeVisible();
    const overview = await inspector.locator(".wf-activity-overview").elementHandle();
    await activityDetailsTabs.getByRole("tab", { name: "Overview", exact: true }).click();
    const details = inspector.getByText("Execution details", { exact: true });
    await details.click();
    await activityDetailsTabs.getByRole("tab", { name: "Inputs", exact: true }).click();
    const previous = inspector.locator(".wf-activity-overview + .wf-execution-browser").getByRole("button", { name: "Previous activity execution", exact: true });
    await previous.click();
    await expect(inspector.locator(".wf-execution-content")).toHaveAttribute("aria-busy", "true");
    await expect(inspector.locator(".wf-execution-loading")).toBeVisible();
    await expect(inspector.getByText("Current: 4", { exact: true })).toBeVisible();
    await expect(inspector.getByRole("button", { name: "Execution 4 of 10", exact: false })).toBeVisible();
    await expect(previous).toBeFocused();
    await expect(inspector.getByText("Current: 3", { exact: true })).toBeVisible();
    await expect(inspector.getByText("Current: 4", { exact: true })).toHaveCount(0);
    expect(await overview!.evaluate(element => element.isConnected)).toBe(true);
    await expect(inspector.locator(".wf-activity-execution-details")).toHaveAttribute("open", "");
    await expect(previous).toBeFocused();
    await expect(node).toHaveAccessibleName(/Run state: Completed/);
  });

  test(`keeps attached-run controls within a narrow editor (${theme})`, async ({ page }) => {
    await page.setViewportSize({ width: 320, height: 1000 });
    await page.goto(`/?mode=activity-duplicate-editor&run-canvas=repeated&theme=${theme}`);
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await page.getByRole("button", { name: "Start Test Run", exact: true }).click();
    const status = page.getByRole("group", { name: "Execution controls", exact: true });
    await expect(status.getByText(/Some activities ran more than once/)).toBeVisible();
    const box = (await status.boundingBox())!;
    expect(box.x).toBeGreaterThanOrEqual(0);
    expect(box.x + box.width).toBeLessThanOrEqual(320);
    await expect(status.getByRole("button", { name: "Return to editing" })).toBeVisible();
    await status.getByRole("button", { name: "Return to editing" }).click();
    await expect(status.getByText("Editing the current draft")).toBeVisible();
  });
}
