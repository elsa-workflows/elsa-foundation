import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
const jsonAlias = "System.Text.Json.JsonElement";

// Real Studio authoring and autosave, followed by real publication, response and result reads.
// Forms/tasks, run navigation and results use the project's viewport. Multi-pane graph wiring
// uses a desktop size (the desktop project's own); affected authoring inspectors are additionally
// captured at project size.
export async function userTaskAuthoringJourney(page: Page, info: TestInfo, capture: Capture, registerDefinition: (id: string) => void, registerCleanup: (cleanup: () => Promise<void>) => void) {
  const base = process.env.UX_BACKEND_URL;
  if (!base || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned backend URL is required");
  const get = async (path: string) => { const response = await page.request.get(`${base}${path}`); expect(response.ok(), await response.text()).toBe(true); return response.json(); };
  const post = async (path: string, data: unknown) => { const response = await page.request.post(`${base}${path}`, { data }); expect(response.ok(), await response.text()).toBe(true); return response.json(); };
  const projectViewport = page.viewportSize()!;
  // A desktop project authors at its own size, so its captures show the canvas it was laid out in.
  const authoringViewport = projectViewport.width >= 1400 ? projectViewport : { width: 1600, height: 1100 };
  type Box = { x: number; y: number; width: number; height: number };
  const overlaps = (a: Box, b: Box) => a.x < b.x + b.width && a.x + a.width > b.x && a.y < b.y + b.height && a.y + a.height > b.y;
  const inside = async (target: Locator, frame: Locator) => {
    const [box, bounds] = await Promise.all([target.boundingBox(), frame.boundingBox()]);
    return Boolean(box && bounds && box.x >= bounds.x && box.y >= bounds.y && box.x + box.width <= bounds.x + bounds.width && box.y + box.height <= bounds.y + bounds.height);
  };
  // Every run panel tab shows its whole label beside the panel buttons; none scrolls under them (#4069).
  // Pass collapsed only where the run panel is folded away (the narrow canvas view); everywhere else the tabs must show.
  const expectRunTabsUnclipped = async (collapsed = false) => {
    const tablist = page.getByRole("tablist", { name: "Run details tabs", exact: true });
    if (collapsed) { await expect(tablist).toBeHidden(); return; }
    await expect(tablist).toBeVisible();
    for (const tab of await tablist.getByRole("tab").all()) await expect.poll(() => inside(tab, tablist)).toBe(true);
  };
  const editorViews = page.getByRole("tablist", { name: "Editor view tabs", exact: true });
  const inspectorSections = page.getByRole("tablist", { name: "Activity inspector sections", exact: true });
  const suffix = `${info.project.name}-${Date.now()}`;
  const formId = `approval-${suffix}`;
  const title = `Approval ${suffix}`;
  const who = await get("/runtime/user-task-identity");
  let runId: string | null = null;
  // A failure between starting the run and Approve would leave an open task in admin's inbox, and the next
  // project's inbox journey starts from "No tasks here". Close any task this run still holds open.
  registerCleanup(async () => {
    if (!runId) return;
    const listed = await page.request.get(`${base}/runtime/user-tasks?view=mine&limit=100`, { timeout: 5_000 });
    if (!listed.ok()) throw new Error(`Cleanup could not list user tasks: HTTP ${listed.status()}`);
    const open = ((await listed.json()).items as Array<{ id: string; workflowExecutionId: string; status: string; revision: number }>)
      .filter(item => item.workflowExecutionId === runId && (item.status === "Open" || item.status === "Claimed"));
    for (const task of open) {
      const closed = await page.request.post(`${base}/runtime/user-tasks/${encodeURIComponent(task.id)}/complete`, {
        timeout: 5_000,
        data: { revision: task.revision, requestId: `acceptance-cleanup-${task.id}`, action: "Reject", data: { Note: "Closed by acceptance cleanup" } }
      });
      if (!closed.ok()) throw new Error(`Cleanup could not close open user task ${task.id}: HTTP ${closed.status()}`);
    }
  });
  const save = async () => {
    const response = page.waitForResponse(item => item.request().method() === "PUT" && item.url().includes("/design/workflows/drafts/"));
    await page.getByRole("button", { name: "Save", exact: true }).click();
    expect((await response).ok()).toBe(true);
    // An in-flight autosave can answer the wait above. "Saved" appears only once the explicit save covers the
    // live draft, and nothing is pending, so a reload cannot drop the final layout or meet the unsaved-changes guard.
    const saveStatus = page.locator(".wf-editor-save-status");
    await expect(saveStatus).toHaveText(/Saved$/);
    await expect(saveStatus).not.toHaveAttribute("data-pending");
  };
  const selectVariable = async (select: Locator) => {
    await expect(select).toBeEnabled();
    await expect.poll(() => select.locator("option").evaluateAll(options => options.find(item => item.textContent?.startsWith("Review data"))?.getAttribute("value") ?? "")).not.toBe("");
    const choice = await select.locator("option").evaluateAll(options => options.find(item => item.textContent?.startsWith("Review data"))!.getAttribute("value")!);
    await select.selectOption(choice);
    await expect(select.locator("option:checked")).not.toContainText("incompatible");
  };
  const captureInspector = async (name: string, target: Locator) => {
    if (projectViewport.width < 800) await page.getByRole("button", { name: "Maximize inspector panel", exact: true }).click();
    await page.setViewportSize(projectViewport); await target.scrollIntoViewIfNeeded(); await capture(page, info, name);
    await page.setViewportSize(authoringViewport);
    if (projectViewport.width < 800) await page.getByRole("button", { name: "Restore inspector panel", exact: true }).click();
  };
  await page.getByRole("button", { name: "More tools", exact: true }).click();
  await page.getByRole("link", { name: "Forms", exact: true }).click();
  await expect(page).toHaveURL(/\/user-tasks\/forms(?:\?|$)/);
  await page.getByRole("button", { name: "Create form version", exact: true }).first().click();
  await expect(page.getByRole("textbox", { name: "Form ID", exact: true })).toBeFocused();
  await page.getByLabel("Form ID", { exact: true }).fill(formId);
  await page.getByLabel("Name", { exact: true }).fill(title);
  await page.getByRole("button", { name: "Add field", exact: true }).click();
  await page.getByLabel("Key", { exact: true }).fill("Note"); await page.getByLabel("Label", { exact: true }).fill("Note");
  await page.getByLabel("Required", { exact: true }).check();
  await page.getByRole("textbox", { name: "Form ID", exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "form-authoring");
  await page.getByRole("button", { name: "Publish form version", exact: true }).click();
  // Publication is transient; the published immutable form remains inspectable after confirmation.
  await expect(page.locator(".studio-toast").filter({ hasText: "Form version published." })).toBeVisible();
  await expect(page.locator(".studio-alert[data-tone=success]")).toHaveCount(0);
  await expect(page.getByRole("group", { name: "Form published", exact: true })).toHaveCount(0);
  const formRow = page.getByRole("row").filter({ hasText: formId });
  const formLink = formRow.getByRole("button", { name: title, exact: true });
  await expect(formLink).toBeVisible(); await formLink.press("Enter");
  await expect(page.getByRole("heading", { name: "Published version 1", exact: true })).toBeFocused();
  await expect(page.getByRole("heading", { name: "Published version 1", exact: true })).toHaveCSS("outline-style", "none");
  await capture(page, info, "published-form");
  await page.getByRole("button", { name: "Back to forms", exact: true }).press("Enter");
  await expect(formLink).toBeFocused();

  await page.setViewportSize(authoringViewport);
  await page.goto("/workflows/definitions");
  await page.getByRole("button", { name: /^(Create|Create workflow)$/ }).first().click();
  const dialog = page.getByRole("dialog", { name: "Create Workflow", exact: true });
  await dialog.getByLabel("Display name").fill(title);
  const rootKind = dialog.getByRole("radio", { name: "Flowchart", exact: true });
  await expect(rootKind).toBeEnabled(); await rootKind.press("Space"); await expect(rootKind).toBeChecked();
  const created = page.waitForResponse(response => response.request().method() === "POST" && /\/design\/workflows\/definitions$/.test(response.url()));
  await dialog.getByRole("button", { name: "Create", exact: true }).click();
  const definitionId: string = (await (await created).json()).definition.id;
  registerDefinition(definitionId);
  const palette = page.getByRole("searchbox", { name: "Search activity palette" });
  await palette.fill("Create User Task"); await page.getByRole("treeitem", { name: /^Create User Task,/ }).click();
  const taskNode = page.locator(".react-flow__node").filter({ hasText: "Create User Task" });
  const taskNodeId = await taskNode.getAttribute("data-id");
  // A palette click lands the first node inside the pane, its START badge clear of the canvas header (#4069).
  const designerPane = page.locator(".react-flow").filter({ has: taskNode });
  await expect.poll(() => inside(taskNode.locator(".wf-node-start-badge"), designerPane)).toBe(true);
  await expect.poll(() => inside(taskNode, designerPane)).toBe(true);
  await page.getByRole("textbox", { name: "Title", exact: true }).fill(title);
  await page.getByLabel("Task assignee", { exact: true }).selectOption(who.userId);
  await page.getByLabel("Task form", { exact: true }).selectOption(formId);
  await expect(page.getByLabel("Task form version", { exact: true })).toHaveValue("1");
  for (const label of ["Task form", "Task form version"]) {
    await expect.poll(() => page.getByLabel(label, { exact: true }).evaluate(element => getComputedStyle(element).backgroundSize)).toBe("5px 5px, 5px 5px");
  }
  await expect(page.getByRole("button", { name: "Remove action 1", exact: true })).toHaveText("");
  await page.getByLabel("Action 1", { exact: true }).scrollIntoViewIfNeeded();
  await captureInspector("task-authoring", page.getByLabel("Action 1", { exact: true }));
  await page.getByLabel("Task form", { exact: true }).scrollIntoViewIfNeeded(); await captureInspector("form-selection", page.getByLabel("Task form", { exact: true }));

  await palette.fill("Set Output"); await page.getByRole("treeitem", { name: /^Set Output,/ }).click();
  const outputNode = page.locator(".react-flow__node").filter({ hasText: "Set Output" });
  const outputNodeId = await outputNode.getAttribute("data-id");
  // The next palette node is visible and clear of the User Task's outcome labels (#4069).
  await expect.poll(() => inside(outputNode, designerPane)).toBe(true);
  await expect.poll(async () => {
    const node = await outputNode.boundingBox();
    if (!node) return false;
    for (const label of await taskNode.locator(".wf-node-port-label").all()) {
      const box = await label.boundingBox();
      if (!box || overlaps(box, node)) return false;
    }
    return true;
  }).toBe(true);
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toBeDisabled();
  await expect(page.getByText("Add a workflow output in the editor's Outputs tab, then select it here.", { exact: true })).toBeVisible();
  await editorViews.getByRole("tab", { name: "Outputs", exact: true }).click();
  const outputSave = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/design/workflows/drafts/") && (response.request().postDataJSON().state?.outputs?.length ?? 0) > 0);
  await page.getByRole("button", { name: "Add output", exact: true }).click();
  const added = await outputSave; expect(added.status()).toBe(200);
  expect(added.request().postDataJSON().state.outputs[0].isNullable).toBe(false);
  await page.getByLabel("Output name", { exact: true }).fill("ReviewData");
  await page.getByLabel("Output type", { exact: true }).selectOption(jsonAlias);
  await editorViews.getByRole("tab", { name: "Variables", exact: true }).click();
  await page.getByRole("button", { name: "Add variable", exact: true }).click();
  await page.getByLabel("Variable name", { exact: true }).fill("Review data");
  await page.getByLabel("Variable type", { exact: true }).selectOption(jsonAlias);
  await page.getByLabel("Variable storage driver", { exact: true }).selectOption("elsa.json");
  await page.getByLabel("Variable default value", { exact: true }).fill("{}");
  await expect(page.getByLabel("Variable type", { exact: true })).toHaveValue(jsonAlias);
  await expect(page.getByLabel("Variable storage driver", { exact: true })).toHaveValue("elsa.json");
  await editorViews.getByRole("tab", { name: "Outputs", exact: true }).click();
  await expect(page.getByLabel("Output type", { exact: true })).toHaveValue(jsonAlias);
  await capture(page, info, "json-output-definitions");
  await editorViews.getByRole("tab", { name: "Designer", exact: true }).click();
  await page.getByRole("combobox", { name: "Output Name", exact: true }).selectOption("ReviewData");
  await editorViews.getByRole("tab", { name: "Outputs", exact: true }).click();
  await page.getByLabel("Output name", { exact: true }).fill("RenamedData");
  await editorViews.getByRole("tab", { name: "Designer", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toHaveAttribute("aria-invalid", "true");
  // aria-invalid is also visible: the field's border takes the danger fill and its message the danger text (#4069).
  const expectVisiblyInvalid = async () => {
    const message = page.locator(".wf-property-row-error");
    await expect(message).toBeVisible();
    const muted = await page.locator(".wf-property-row-header label").first().evaluate(element => getComputedStyle(element).color);
    const [dangerText, dangerFill] = await message.evaluate(element => {
      const probe = (value: string) => {
        const swatch = document.createElement("span");
        swatch.style.color = value;
        element.append(swatch);
        const color = getComputedStyle(swatch).color;
        swatch.remove();
        return color;
      };
      return [probe("var(--studio-danger-fg, var(--wf-danger))"), probe("var(--studio-danger, var(--wf-danger))")];
    });
    expect(dangerText).not.toBe(muted);
    await expect.poll(() => message.evaluate(element => getComputedStyle(element).color)).toBe(dangerText);
    await expect.poll(() => page.getByRole("combobox", { name: "Output Name", exact: true }).evaluate(element => getComputedStyle(element).borderTopColor)).toBe(dangerFill);
  };
  await expectVisiblyInvalid();
  await captureInspector("renamed-output", page.getByRole("combobox", { name: "Output Name", exact: true }));
  await page.getByRole("combobox", { name: "Output Name", exact: true }).selectOption("RenamedData");
  await editorViews.getByRole("tab", { name: "Outputs", exact: true }).click();
  await page.getByRole("button", { name: "Remove output RenamedData", exact: true }).click();
  await editorViews.getByRole("tab", { name: "Designer", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toBeDisabled();
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toHaveAttribute("aria-invalid", "true");
  await expect(page.getByText(/This output is no longer defined. Add a workflow output in the editor's Outputs tab/)).toBeVisible();
  await expectVisiblyInvalid();
  await captureInspector("removed-output", page.getByRole("combobox", { name: "Output Name", exact: true }));
  await editorViews.getByRole("tab", { name: "Outputs", exact: true }).click();
  await page.getByRole("button", { name: "Add output", exact: true }).click();
  await page.getByLabel("Output name", { exact: true }).fill("RenamedData");
  await page.getByLabel("Output type", { exact: true }).selectOption(jsonAlias);
  await editorViews.getByRole("tab", { name: "Designer", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toHaveAttribute("aria-invalid", "false");
  await expect.poll(() => page.getByRole("combobox", { name: "Output Name", exact: true }).evaluate(element => getComputedStyle(element).backgroundSize)).toBe("5px 5px, 5px 5px");
  const valueRow = page.locator(".wf-property-row").filter({ has: page.locator(".studio-field-label, .wf-property-row-header > span", { hasText: /^Value$/ }) });
  await valueRow.locator("button.wf-syntax-picker-trigger").click(); await page.getByRole("option", { name: /^Variable/ }).click();
  await selectVariable(page.getByRole("combobox", { name: "Variable reference", exact: true }));
  await captureInspector("output-picker", page.getByRole("combobox", { name: "Output Name", exact: true }));
  await taskNode.click(); await inspectorSections.getByRole("tab", { name: "Outputs", exact: true }).click();
  await selectVariable(page.locator('select[aria-label="Capture Data into a variable"]'));
  await inspectorSections.getByRole("tab", { name: "Inputs", exact: true }).click();
  await page.getByRole("button", { name: "Auto-layout", exact: true }).click(); await page.getByRole("button", { name: "Fit View", exact: true }).click();
  await taskNode.locator('.source[data-handleid="Approve"]').dragTo(page.locator(`.react-flow__node[data-id="${outputNodeId}"] .target`));
  await expect(page.locator(".react-flow__edge")).toHaveCount(1);
  // Lay out the completed graph, rather than saving the disconnected palette placement.
  await page.getByRole("button", { name: "Auto-layout", exact: true }).click();
  await save(); await page.reload();
  const savedTask = page.locator(`.react-flow__node[data-id="${taskNodeId}"]`);
  await savedTask.click(); await expect(page.getByLabel("Task form version", { exact: true })).toHaveValue("1");
  const publication = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/publish"));
  await page.getByRole("button", { name: "Review & publish", exact: true }).click();
  const review = page.getByRole("dialog", { name: "Publish workflow", exact: true });
  await expect(review.getByRole("button", { name: "Publish", exact: true })).toBeEnabled({ timeout: 60_000 });
  await review.getByRole("button", { name: "Publish", exact: true }).click();
  const published = await (await publication).json();
  await page.getByRole("dialog", { name: "Published", exact: true }).getByRole("button", { name: "Close", exact: true }).click();
  runId = (await post(`/runtime/workflows/executables/${published.artifactId}/execute`, { sourceReferenceId: published.sourceReferenceId })).workflowExecutionId;
  let taskId = "";
  await expect.poll(async () => { taskId = (await get("/runtime/user-tasks?view=mine&limit=100")).items.find((item: { workflowExecutionId: string }) => item.workflowExecutionId === runId)?.id ?? ""; return taskId; }, { timeout: 30_000 }).not.toBe("");
  // Publishing a later form leaves this open task's original snapshot intact.
  await post("/runtime/user-task-forms", { id: formId, version: "2", name: `${title} revised`, provider: "elsa.basic-forms/1", fields: [{ key: "Changed", label: "Changed", kind: "text", required: true, readOnly: false }] });
  const frozen = await get(`/runtime/user-tasks/${taskId}`); expect(frozen.form.version).toBe("1"); expect(frozen.form.fields[0].key).toBe("Note");
  await page.setViewportSize(projectViewport); await page.goto(`/user-tasks/${taskId}`);
  await page.getByLabel(/^Note/).fill("Approved with case preserved"); await capture(page, info, "frozen-task-form");
  await page.getByRole("button", { name: "Approve", exact: true }).click();
  await expect.poll(async () => (await get(`/runtime/workflows/instances/${runId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
  await page.getByRole("button", { name: "Refresh task", exact: true }).click();
  await expect(page.getByRole("button", { name: "Back to task list", exact: true })).toBeEnabled();
  const completed = await get(`/runtime/workflows/instances/${runId}`);
  // The output carries its declared JSON type, not SetOutput's dynamic authoring type (#4069).
  expect(completed.outputs.RenamedData).toMatchObject({ isRedacted: false, captureMode: "DiagnosticSnapshot", value: { kind: "object", typeName: jsonAlias, properties: [{ name: "Note", value: { preview: "Approved with case preserved" } }] } });
  await page.setViewportSize(authoringViewport); await page.goto(`/workflows/instances/${runId}`);
  const runtimeTask = page.locator(`.react-flow__node[data-id="${taskNodeId}"]`);
  await expect(runtimeTask.locator('.source[data-handleid="Approve"]')).toBeVisible();
  await expect(runtimeTask.locator('.source[data-handleid="Reject"]')).toBeVisible();
  await page.setViewportSize(projectViewport);
  const runCanvas = page.getByRole("region", { name: "Workflow run canvas", exact: true });
  const outcomeLabels = runtimeTask.locator(".wf-node-port-label").filter({ hasText: /^(Approve|Reject|Unmatched)$/ });
  const renderedOutcomeFont = () => outcomeLabels.first().evaluate(element => {
    const viewport = element.closest(".react-flow__viewport");
    if (!viewport) throw new Error("Runtime graph viewport is unavailable");
    const matrix = new DOMMatrixReadOnly(getComputedStyle(viewport).transform);
    return Number.parseFloat(getComputedStyle(element).fontSize) * Math.hypot(matrix.a, matrix.b);
  });
  // The first view keeps the outcome labels readable (#4064): the whole graph when it fits legibly,
  // otherwise a readable part around the task, with every outcome label inside the canvas.
  await expect.poll(renderedOutcomeFont).toBeGreaterThanOrEqual(10);
  const initialFont = await renderedOutcomeFont();
  for (const label of ["Approve", "Reject", "Unmatched"]) {
    await expect.poll(async () => {
      const box = await outcomeLabels.filter({ hasText: new RegExp(`^${label}$`) }).boundingBox();
      const pane = await runCanvas.locator(".react-flow__pane").boundingBox();
      // The zoom controls and MiniMap float over the pane, so a label under one of them is not readable.
      const panels = await Promise.all((await runCanvas.locator(".react-flow__controls:visible, .react-flow__minimap:visible").all()).map(panel => panel.boundingBox()));
      const underPanel = panels.some(panel => box && panel && box.x < panel.x + panel.width && box.x + box.width > panel.x
        && box.y < panel.y + panel.height && box.y + box.height > panel.y);
      return Boolean(box && pane && !underPanel && box.x >= pane.x && box.x + box.width <= pane.x + pane.width
        && box.y >= pane.y && box.y + box.height <= pane.y + pane.height);
    }, { message: `${label} is readable inside the run canvas's first view` }).toBe(true);
  }
  await capture(page, info, "run-outcomes-initial");
  await page.getByRole("button", { name: "Fit View", exact: true }).click();
  expect((await page.getByRole("button", { name: "All executions", exact: true }).boundingBox())!.height).toBeGreaterThanOrEqual(32);
  const canvasBox = (await runCanvas.boundingBox())!;
  for (const node of await page.locator(".react-flow__node").all()) {
    await expect.poll(async () => {
      const box = await node.boundingBox();
      return Boolean(box && box.x >= canvasBox.x && box.x + box.width <= canvasBox.x + canvasBox.width);
    }).toBe(true);
  }
  for (const label of ["Approve", "Reject", "Unmatched"]) {
    await expect.poll(async () => {
      const box = await runtimeTask.locator(".wf-node-port-label").filter({ hasText: new RegExp(`^${label}$`) }).boundingBox();
      if (!box || box.x < canvasBox.x || box.x + box.width > canvasBox.x + canvasBox.width) return false;
      const others = await page.locator(`.react-flow__node:not([data-id="${taskNodeId}"])`).all();
      for (const other of others) {
        const target = await other.boundingBox();
        if (target && box.x < target.x + target.width && box.x + box.width > target.x && box.y < target.y + target.height && box.y + box.height > target.y) return false;
      }
      return true;
    }).toBe(true);
  }
  await expect(page.locator(".react-flow__edge-text").filter({ hasText: /^Approve$/ })).toHaveCount(0);
  await expectRunTabsUnclipped(projectViewport.width < 600);
  if (projectViewport.width < 600) {
    // On a phone the run header stays compact: title and instance id share a line, and the run's
    // name, back link and status share another (#4069).
    const title = (await page.getByRole("heading", { name: "Run", exact: true }).boundingBox())!;
    const instanceId = (await page.locator(".wf-page-context code").boundingBox())!;
    expect(Math.abs((title.y + title.height / 2) - (instanceId.y + instanceId.height / 2))).toBeLessThan(title.height / 2);
    const identity = (await page.locator(".wf-run-identity").boundingBox())!;
    expect(identity.height).toBeLessThanOrEqual(48);
    const toolbar = (await page.getByRole("group", { name: "Run workspace actions", exact: true }).boundingBox())!;
    // The whole run header (title through run tools) stays a small part of the viewport; it took about half before.
    expect(toolbar.y + toolbar.height - title.y).toBeLessThanOrEqual(projectViewport.height * 0.2);
    await info.attach("narrow-run-header.json", { body: JSON.stringify({ titleTop: title.y, toolbarBottom: toolbar.y + toolbar.height, viewportHeight: projectViewport.height }), contentType: "application/json" });
  }
  await capture(page, info, "run-outcomes");

  // Fit View is the whole-graph overview. Verify ordinary controls can recover readable outcomes from it.
  const overviewFont = await renderedOutcomeFont();
  const zoomIn = runCanvas.getByRole("button", { name: "Zoom In", exact: true });
  await expect(zoomIn).toBeVisible(); await expect(zoomIn).toBeEnabled();
  for (let step = 0; step < 8 && await renderedOutcomeFont() < 10; step++) {
    const previous = await renderedOutcomeFont();
    await zoomIn.press("Enter"); await expect(zoomIn).toBeFocused();
    await expect.poll(renderedOutcomeFont).toBeGreaterThan(previous);
  }
  await expect.poll(renderedOutcomeFont).toBeGreaterThanOrEqual(10);
  const panTo = async (targets: Locator[], avoidFloatingPanels = false) => {
    const pane = (await runCanvas.locator(".react-flow__pane").boundingBox())!;
    // The zoom controls and MiniMap float over the pane; a target meant to be read must clear them, not just the pane edge.
    const floatingPanels = avoidFloatingPanels
      ? (await Promise.all((await runCanvas.locator(".react-flow__panel:visible").all()).map(panel => panel.boundingBox()))).filter((box): box is Box => Boolean(box))
      : [];
    const unobscured = (box: Box, dx: number, dy: number) => {
      const moved = { ...box, x: box.x + dx, y: box.y + dy };
      return moved.x >= pane.x + 8 && moved.x + moved.width <= pane.x + pane.width - 8 && moved.y >= pane.y + 8 && moved.y + moved.height <= pane.y + pane.height - 8
        && !floatingPanels.some(panel => overlaps(moved, panel));
    };
    for (let gesture = 0; gesture < 6; gesture++) {
      const boxes = await Promise.all(targets.map(target => target.boundingBox()));
      if (boxes.some(box => !box)) throw new Error("Runtime graph target is unavailable");
      const left = Math.min(...boxes.map(box => box!.x));
      const right = Math.max(...boxes.map(box => box!.x + box!.width));
      let dx = left < pane.x + 8 ? pane.x + 8 - left : right > pane.x + pane.width - 8 ? pane.x + pane.width - 8 - right : 0;
      let dy = 0;
      if (avoidFloatingPanels) {
        // Pick the smallest ordinary pan that puts every target inside the pane and clear of every floating panel.
        const top = Math.min(...boxes.map(box => box!.y));
        const bottom = Math.max(...boxes.map(box => box!.y + box!.height));
        const xs = [0, pane.x + 8 - left, pane.x + pane.width - 8 - right, ...floatingPanels.flatMap(panel => [panel.x + panel.width + 8 - left, panel.x - 8 - right])];
        const ys = [0, pane.y + 8 - top, pane.y + pane.height - 8 - bottom, ...floatingPanels.flatMap(panel => [panel.y + panel.height + 8 - top, panel.y - 8 - bottom])];
        const feasible = xs.flatMap(x => ys.map(y => ({ x, y }))).filter(shift => boxes.every(box => unobscured(box!, shift.x, shift.y)));
        if (!feasible.length) throw new Error("Runtime graph targets have no unobscured position in the current pane");
        ({ x: dx, y: dy } = feasible.sort((a, b) => Math.hypot(a.x, a.y) - Math.hypot(b.x, b.y))[0]!);
      }
      if (Math.abs(dx) < 1 && Math.abs(dy) < 1) break;
      const distanceX = Math.sign(dx) * Math.min(Math.abs(dx), pane.width / 2 - 24);
      const distanceY = Math.sign(dy) * Math.min(Math.abs(dy), pane.height / 2 - 24);
      const x = pane.x + pane.width / 2; const y = distanceY > 0 ? pane.y + 12 : pane.y + pane.height - 12;
      const before = await runCanvas.locator(".react-flow__viewport").getAttribute("style");
      await page.mouse.move(x, y); await page.mouse.down();
      await page.mouse.move(x + distanceX, y + distanceY, { steps: 8 }); await page.mouse.up();
      await expect.poll(() => runCanvas.locator(".react-flow__viewport").getAttribute("style")).not.toBe(before);
    }
    for (const target of targets) {
      await expect.poll(async () => {
        const box = await target.boundingBox();
        return Boolean(box && box.x >= pane.x && box.x + box.width <= pane.x + pane.width && box.y >= pane.y && box.y + box.height <= pane.y + pane.height
          && !floatingPanels.some(panel => overlaps(box, panel)));
      }).toBe(true);
    }
  };
  // Frame the outcome labels with their node's name, so the capture shows whose outcomes they are (#4069).
  await panTo([...await outcomeLabels.all(), runtimeTask.locator(".wf-node-copy strong")], true);
  for (const label of ["Approve", "Reject", "Unmatched"]) await expect(outcomeLabels.filter({ hasText: new RegExp(`^${label}$`) })).toBeVisible();
  await info.attach("runtime-outcome-readable-font.json", { body: JSON.stringify({ initialFont, overviewFont, recoveredFont: await renderedOutcomeFont() }), contentType: "application/json" });
  await capture(page, info, "run-outcomes-readable");
  const runtimeOutput = page.locator(`.react-flow__node[data-id="${outputNodeId}"]`);
  await panTo([runtimeOutput]);
  await expect(runtimeOutput).toContainText("Set Output");
  await capture(page, info, "run-output-reached");
  await runCanvas.getByRole("button", { name: "Fit View", exact: true }).press("Enter");
  await info.attach("workflow-outputs.json", { body: JSON.stringify(completed.outputs, null, 2), contentType: "application/json" });
  // Keyboard activation opens the real responsive inspector after changing viewport.
  await runtimeTask.press("Enter");
  await page.getByRole("tab", { name: "Details", exact: true }).click();
  const maximize = page.getByRole("button", { name: "Maximize run details panel", exact: true });
  if (await maximize.isVisible()) await maximize.click();
  const result = page.getByRole("region", { name: "Workflow outputs", exact: true });
  await expect(result).toContainText("RenamedData"); await expect(result).toContainText("Approved with case preserved");
  await expect(result.locator(".wf-runtime-snapshot-node > summary").first()).toHaveText("JSON");
  const resultValue = result.locator(".wf-runtime-snapshot-property code");
  expect((await resultValue.boundingBox())!.width).toBeGreaterThan(150);
  await result.scrollIntoViewIfNeeded(); await expectRunTabsUnclipped(); await capture(page, info, "workflow-result");

  const catalog = await get("/design/activities/definitions?search=CreateUserTask");
  const taskVersion = catalog.items.find((item: { definition: { activityTypeKey: string } }) => item.definition.activityTypeKey.endsWith("CreateUserTask"))!.definition.headVersionId;
  const flowcharts = await get("/design/activities/definitions?search=Flowchart");
  const flowchartVersion = flowcharts.items.find((item: { definition: { activityTypeKey: string } }) => item.definition.activityTypeKey.endsWith("Flowchart"))!.definition.headVersionId;
  for (const variant of ["assignment", "prefill"]) {
    const values = { Title: "Invalid task configuration", Actions: ["Approve", "Reject"], Prefill: variant === "prefill" ? "{}" : {}, ...(variant === "assignment" ? {} : { AssigneeId: who.userId }) };
    const child = { nodeId: "invalid-review", activityVersionId: taskVersion, inputs: Object.entries(values).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } })), outputs: [] };
    const root = variant === "assignment" ? { nodeId: "invalid-flow", activityVersionId: flowchartVersion, inputs: [], outputs: [], structure: { kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: { activities: [child], connections: [], startNodeId: child.nodeId } } } : child;
    const definition = await post("/design/workflows/definitions/submit", { name: `Invalid ${variant} ${suffix}`, state: { variables: [], inputs: [], outputs: [], rootActivity: root } });
    registerDefinition(definition.definition.id);
    const publication = await post(`/publishing/workflows/${definition.version.id}/publish`, {});
    const execution = await post(`/runtime/workflows/executables/${publication.artifactId}/execute`, { sourceReferenceId: publication.sourceReferenceId });
    await expect.poll(async () => (await get(`/runtime/workflows/instances/${execution.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Faulted");
    const fault = await get(`/runtime/workflows/instances/${execution.workflowExecutionId}`);
    const expected = variant === "assignment" ? "Select a task assignee or a group." : "Prefill must be a JSON object";
    expect(fault.incidents.some((item: { message: string }) => item.message.includes(expected))).toBe(true);
    await page.setViewportSize(projectViewport); await page.goto(`/workflows/instances/${execution.workflowExecutionId}`);
    await page.getByRole("button", { name: /^Open run incidents:/ }).click();
    const maximize = page.getByRole("button", { name: "Maximize run details panel", exact: true });
    if (await maximize.isVisible()) await maximize.click();
    await expect(page.locator(".wf-instance-incident-summary").filter({ hasText: expected })).toBeVisible();
    if (variant === "assignment") {
      await expect(page.locator(".wf-instance-incident-cause").filter({ hasText: "Review its incident to correct the cause." })).toBeVisible();
      await expectRunTabsUnclipped();
      await capture(page, info, "task-fault-causes");
      const parent = page.locator(".wf-instance-incident").filter({ hasText: "Review its incident to correct the cause." });
      await parent.getByText("Technical details", { exact: true }).click();
      await expect(parent.locator(".wf-incident-message")).toContainText("a faulted child cannot complete");
      // Show the expanded message itself, not only its heading at the fold (#4069).
      await parent.locator(".wf-incident-message").scrollIntoViewIfNeeded();
      await capture(page, info, "task-fault-details");
    } else {
      await capture(page, info, "prefill-fault");
    }
  }
}
