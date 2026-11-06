import { expect, test, type Page } from "@playwright/test";

// This exercises the real editors against the existing browser fixture's durable fake backend.
// It is regression evidence, not composed-host publication, execution or UX acceptance.
const fixture = "/?mode=activity-duplicate-editor&host=workflow&composition=bpmn&palette-context&bpmn-decision=bound";
const processEditor = (page: Page) => page.getByRole("region", { name: "bpmn-js process editor", exact: true });
const routing = (page: Page) => page.getByRole("region", { name: "BPMN routing", exact: true });
const integratedDecision = (page: Page) => page.locator(".react-flow__node[data-id='decision-element']");
const nativeDecision = (page: Page) => processEditor(page).locator(".djs-shape[data-element-id='decision-element']");

async function chooseEditor(page: Page, editor: "integrated" | "bpmn-js") {
  await page.getByRole("combobox", { name: "BPMN editor", exact: true }).selectOption(editor);
  if (editor === "bpmn-js") await expect(processEditor(page)).toHaveAttribute("data-bpmn-js-state", "ready");
  else await expect(integratedDecision(page)).toBeVisible();
}

async function selectDecision(page: Page, editor: "integrated" | "bpmn-js") {
  if (editor === "bpmn-js") {
    // bpmn-js toggles an already selected shape. Select another native element first so this
    // exercises selection delivery to Elsa instead of depending on the previous editor's state.
    await processEditor(page).locator(".djs-shape[data-element-id='low-end'] .djs-hit").click();
    await nativeDecision(page).locator(".djs-hit").click();
  }
  else await integratedDecision(page).click();
  await expect(routing(page)).toBeVisible();
}

async function expectConfiguration(page: Page, expression: string, name: string) {
  await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue(name);
  await expect(routing(page).getByLabel("Condition outcome for flow to High", { exact: true })).toHaveValue("High");
  await expect(routing(page).getByLabel("Default flow to Low", { exact: true })).toBeChecked();
  await expect(page.getByRole("button", { name: "Outcome expression syntax: JavaScript", exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: /JavaScript expression.*Activate to edit/ })).toContainText(expression);
}

async function expectDiagramContrast(page: Page) {
  const contrast = await processEditor(page).evaluate(root => {
    const paint = document.createElement("canvas").getContext("2d")!;
    const luminance = (color: string) => {
      paint.clearRect(0, 0, 1, 1);
      paint.fillStyle = color;
      paint.fillRect(0, 0, 1, 1);
      const [r, g, b] = paint.getImageData(0, 0, 1, 1).data;
      const linear = [r, g, b].map(channel => {
        const srgb = channel / 255;
        return srgb <= .04045 ? srgb / 12.92 : ((srgb + .055) / 1.055) ** 2.4;
      });
      return .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2];
    };
    const background = luminance(getComputedStyle(root).backgroundColor);
    const ratio = (color: string) => {
      const foreground = luminance(color);
      return (Math.max(background, foreground) + .05) / (Math.min(background, foreground) + .05);
    };
    const edge = root.querySelector(".djs-connection[data-element-id='high-flow'] .djs-visual path")!;
    const label = root.querySelector(".djs-shape[data-element-id='high-end_label'] text")!;
    const watermark = root.querySelector(".bjs-powered-by svg path")!;
    return {
      edge: ratio(getComputedStyle(edge).stroke),
      label: ratio(getComputedStyle(label).fill),
      watermark: ratio(getComputedStyle(watermark).fill)
    };
  });
  expect(contrast.edge, "BPMN paths contrast with the canvas").toBeGreaterThanOrEqual(3);
  expect(contrast.label, "BPMN labels contrast with the canvas").toBeGreaterThanOrEqual(4.5);
  expect(contrast.watermark, "bpmn.io attribution contrasts with the canvas").toBeGreaterThanOrEqual(4.5);
}

async function expectInitialFit(page: Page) {
  const clipped = await processEditor(page).evaluate(root => {
    const canvas = root.querySelector(".djs-container")!.getBoundingClientRect();
    const palette = root.querySelector(".djs-palette")!.getBoundingClientRect();
    return [...root.querySelectorAll(".djs-shape .djs-visual")].flatMap(visual => {
      const box = visual.getBoundingClientRect();
      if (!box.width || !box.height) return [];
      const clear = box.left >= Math.max(canvas.left + 16, palette.right + 16) && box.right <= canvas.right - 16
        && box.top >= canvas.top + 16 && box.bottom <= canvas.bottom - 24;
      return clear ? [] : [visual.closest("[data-element-id]")!.getAttribute("data-element-id")];
    });
  });
  expect(clipped, "initial BPMN shapes and labels fit inside the canvas clear of its native palette").toEqual([]);
}

async function savedProcess(page: Page) {
  return page.evaluate(() => {
    const key = Object.keys(sessionStorage).find(candidate => candidate.startsWith("adaptive-ports:"));
    if (!key) return null;
    const draft = JSON.parse(sessionStorage.getItem(key)!);
    const process = draft.state.rootActivity;
    const payload = process.structure.payload;
    const decision = payload.activities.find((activity: { nodeId: string }) => activity.nodeId === "decision");
    return {
      draftId: draft.id,
      definitionId: draft.definitionId,
      processId: process.nodeId,
      decisionId: decision?.nodeId,
      decisionVersion: decision?.activityVersionId,
      inputs: decision?.inputs,
      elements: payload.elements,
      flows: payload.sequenceFlows,
      diagram: payload.diagram,
      layout: draft.layout
    };
  });
}

for (const firstEditor of ["integrated", "bpmn-js"] as const) {
  test(`same-process switching from ${firstEditor} preserves Decision edits, native movement and shared undo through fixture reload`, async ({ page }, testInfo) => {
    testInfo.annotations.push({ type: "evidence", description: "Isolated browser fixture; no real backend publication or execution claim." });
    await page.setViewportSize({ width: 1100, height: 1000 });
    await page.addInitScript(() => {
      const refusals: string[] = [];
      (window as unknown as { bpmnCanvasRefusals: string[] }).bpmnCanvasRefusals = refusals;
      new MutationObserver(records => {
        if (refusals.length) return;
        for (const record of records) {
          const target = record.target as Element;
          if (record.oldValue === "refused" || target.getAttribute?.("data-bpmn-js-state") === "refused"
            || target.textContent?.includes("Change refused:")) {
            refusals.push("Canvas refused a normal journey edit");
            break;
          }
        }
      }).observe(document, { subtree: true, childList: true, characterData: true, attributes: true,
        attributeOldValue: true, attributeFilter: ["data-bpmn-js-state"] });
    });
    await page.goto(`${fixture}&theme=${firstEditor === "integrated" ? "light" : "dark"}`);
    await chooseEditor(page, firstEditor);
    if (firstEditor === "bpmn-js") await expectInitialFit(page);
    await selectDecision(page, firstEditor);

    const expression = '2000 > 1000 ? "High" : "Low"';
    await page.getByRole("button", { name: /^Outcome expression syntax:/ }).click();
    await page.getByRole("option", { name: "JavaScript", exact: true }).click();
    await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
    await page.locator(".cm-content[contenteditable='true']").fill(expression);
    await routing(page).getByLabel("Condition outcome for flow to High", { exact: true }).fill("High");
    await routing(page).getByLabel("Default flow to Low", { exact: true }).check();

    await chooseEditor(page, "bpmn-js");
    await expectInitialFit(page);
    await page.screenshot({ path: testInfo.outputPath("bpmn-js-initial-fit.png") });
    await page.setViewportSize({ width: 1500, height: 1000 });
    await selectDecision(page, "bpmn-js");
    const shape = nativeDecision(page);
    const originalTransform = await shape.getAttribute("transform");
    const bounds = await shape.boundingBox();
    expect(bounds).not.toBeNull();
    const x = bounds!.x + bounds!.width / 2;
    const y = bounds!.y + bounds!.height / 2;
    await page.mouse.move(x, y);
    await page.mouse.down();
    await page.mouse.move(x + 65, y + 40, { steps: 12 });
    await page.mouse.up();
    await expect(shape).not.toHaveAttribute("transform", originalTransform!);
    await expect(processEditor(page)).toHaveAttribute("data-bpmn-js-state", "ready");
    expect(await page.evaluate(() => (window as unknown as { bpmnCanvasRefusals: string[] }).bpmnCanvasRefusals)).toEqual([]);

    await selectDecision(page, "bpmn-js");
    const name = `Order routing from ${firstEditor}`;
    await shape.locator(".djs-hit").dblclick();
    const nativeLabel = page.locator(".djs-direct-editing-content[contenteditable='true']");
    await nativeLabel.fill(name);
    await nativeLabel.press("Enter");
    expect(await page.evaluate(() => (window as unknown as { bpmnCanvasRefusals: string[] }).bpmnCanvasRefusals)).toEqual([]);
    await expectConfiguration(page, expression, name);
    // External labels are separate native diagram elements but configure their owning BPMN node.
    await processEditor(page).locator(".djs-shape[data-element-id='low-end'] .djs-hit").click();
    // The routed outgoing edge crosses the label's center; click its unobstructed text corner.
    await processEditor(page).locator(".djs-shape[data-element-id='decision-element_label'] .djs-hit").click({ position: { x: 10, y: 5 } });
    await expectConfiguration(page, expression, name);
    const watermark = processEditor(page).locator(".bjs-powered-by");
    await expect(watermark).toBeVisible();
    expect(await watermark.evaluate(element => {
      const box = element.getBoundingClientRect();
      const foreground = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
      return foreground !== null && element.contains(foreground);
    })).toBe(true);
    await expectDiagramContrast(page);
    await page.screenshot({ path: testInfo.outputPath("bpmn-js-shared-inspector.png") });

    await chooseEditor(page, "integrated");
    await selectDecision(page, "integrated");
    await expectConfiguration(page, expression, name);
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await selectDecision(page, "integrated");
    await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue("");
    await page.getByRole("button", { name: "Redo", exact: true }).click();
    await selectDecision(page, "integrated");
    await expectConfiguration(page, expression, name);
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect.poll(() => savedProcess(page)).toMatchObject({
      draftId: "draft-1", definitionId: "definition-1", processId: "bpmn-root",
      decisionId: "decision", decisionVersion: "decision-v1",
      inputs: [{ referenceKey: "Outcome", value: { expressionType: "JavaScript", value: expression } }],
      elements: expect.arrayContaining([expect.objectContaining({ elementId: "decision-element", elementType: "exclusiveGateway", childNodeId: "decision", name, defaultFlowId: "low-flow" })]),
      flows: expect.arrayContaining([
        expect.objectContaining({ flowId: "high-flow", sourceRef: "decision-element", targetRef: "high-end", conditionOutcome: "High" }),
        expect.objectContaining({ flowId: "low-flow", sourceRef: "decision-element", targetRef: "low-end", isDefault: true })
      ])
    });
    const saved = await savedProcess(page);
    expect(saved!.layout.find((record: { nodeId: string }) => record.nodeId === "decision-element")).not.toMatchObject({ x: 220, y: 200 });
    expect(JSON.stringify(saved!.diagram), "display theme must not become authored BPMNDI colors")
      .not.toMatch(/--studio-|var\(|"(?:fill|stroke|color|background-color|border-color|label-color)"\s*:/);
    await page.screenshot({ path: testInfo.outputPath("integrated-after-switch-undo-redo.png") });

    await page.reload();
    await chooseEditor(page, "integrated");
    await selectDecision(page, "integrated");
    await expectConfiguration(page, expression, name);
    expect(await savedProcess(page)).toEqual(saved);
    await chooseEditor(page, "bpmn-js");
    await selectDecision(page, "bpmn-js");
    await expectConfiguration(page, expression, name);
    // SVG line wrapping splits tspans without inserting text-node spaces.
    await expect(processEditor(page).locator(".djs-label").filter({ hasText: new RegExp(name.split(" ").join("\\s*")) })).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("bpmn-js-after-persisted-reload.png") });
  });
}
