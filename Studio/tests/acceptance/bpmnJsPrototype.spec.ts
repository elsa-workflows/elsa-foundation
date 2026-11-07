import { seedBpmnPrototype as seed } from "./bpmnDocumentAcceptanceFixtures";
import { readFileSync, writeFileSync } from "node:fs";
import { expect, test, type Page, type TestInfo } from "@playwright/test";

type Node = {
  nodeId: string;
  activityVersionId: string;
  inputs: unknown[];
  outputs: unknown[];
  structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } | null;
  [key: string]: unknown;
};
type Element = { elementId: string; elementType: string; name?: string; childNodeId?: string; defaultFlowId?: string };
type Flow = { flowId: string; sourceRef: string; targetRef: string; conditionOutcome?: string | null; isDefault?: boolean };
type Layout = { nodeId: string; x: number; y: number; width?: number; height?: number };
type Draft = { id: string; definitionId: string; state: { rootActivity: Node; inputs: unknown[]; variables: unknown[]; outputs: unknown[] }; layout: Layout[] };
type Submitted = { definition: { id: string }; draftId: string; version: { id: string } };
type Published = { artifactId: string; sourceReferenceId: string };
type Instance = {
  instance: { status: string; definitionId: string; definitionVersionId: string; sourceReferenceId: string };
  activities: { activityExecutionId: string; executableNodeId: string; authoredActivityId: string; status: string }[];
  incidents: unknown[];
  outputs: Record<string, { value: unknown; isRedacted: boolean }>;
};
type Api = <T>(path: string, method?: string, data?: unknown) => Promise<T>;

const alternate = (page: Page) => page.getByRole("region", { name: "bpmn-js process editor", exact: true });
const routing = (page: Page) => page.getByRole("region", { name: "BPMN routing", exact: true });
const shape = (page: Page, id: string) => alternate(page).locator(`.djs-shape[data-element-id='${id}']`);
const payload = (draft: Draft) => draft.state.rootActivity.structure!.payload as unknown as { activities: Node[]; elements: Element[]; sequenceFlows: Flow[] };

async function capture(page: Page, info: TestInfo, name: string) {
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const screenshot = info.outputPath(`${name}.png`);
  await page.screenshot({ path: screenshot, fullPage: true, animations: "disabled" });
  await info.attach(`${name}.png`, { path: screenshot, contentType: "image/png" });
  const aria = info.outputPath(`${name}.aria.yml`);
  writeFileSync(aria, await page.locator("body").ariaSnapshot());
  await info.attach(`${name}.aria.yml`, { path: aria, contentType: "text/yaml" });
}

async function chooseEditor(page: Page, editor: "integrated" | "bpmn-js") {
  await page.getByRole("combobox", { name: "BPMN editor", exact: true }).selectOption(editor);
  if (editor === "bpmn-js") await expect(alternate(page)).toHaveAttribute("data-bpmn-js-state", "ready");
  else await expect(page.locator(".react-flow__node[data-id='gateway']")).toBeVisible();
}

async function expectNoRefusals(page: Page) {
  expect(await page.evaluate(() => (window as unknown as { __elsaBpmnUnexpectedRefusals: string[] }).__elsaBpmnUnexpectedRefusals),
    "Normal native edits must not enter refusal/recovery, including transient notices").toEqual([]);
}

async function captureInitialNativeFit(page: Page, info: TestInfo) {
  const clipped = await alternate(page).evaluate(root => {
    const canvas = root.querySelector(".djs-container")!.getBoundingClientRect();
    const palette = root.querySelector(".djs-palette")!.getBoundingClientRect();
    return [...root.querySelectorAll(".djs-shape .djs-visual")].flatMap(visual => {
      const bounds = visual.getBoundingClientRect();
      if (!bounds.width || !bounds.height) return [];
      return bounds.left >= Math.max(canvas.left + 16, palette.right + 16) && bounds.right <= canvas.right - 16
        && bounds.top >= canvas.top + 16 && bounds.bottom <= canvas.bottom - 24
        ? [] : [visual.closest("[data-element-id]")!.getAttribute("data-element-id")];
    });
  });
  expect(clipped, "Initial native shapes and labels fit inside the canvas clear of its palette").toEqual([]);
  await capture(page, info, "alternate-initial-fit");
}

async function selectElement(page: Page, editor: "integrated" | "bpmn-js", id: string) {
  if (editor === "bpmn-js") {
    // A second click toggles an already-selected native shape. Always make this a new selection.
    await shape(page, "end-high").locator(".djs-hit").click();
    await shape(page, id).locator(".djs-hit").click();
  } else await page.locator(`.react-flow__node[data-id='${id}']`).click();
}


function outputValue(instance: Instance) {
  const output = instance.outputs.result;
  expect(output, "The bound Inline result is captured as a workflow output").toBeTruthy();
  expect(output.isRedacted).toBe(false);
  const value = output.value;
  return value && typeof value === "object" ? (value as { preview?: unknown; value?: unknown }).preview ?? (value as { value?: unknown }).value : value;
}

for (const firstEditor of ["integrated", "bpmn-js"] as const) {
  test(`owned-host prototype from ${firstEditor}: same process edits, publication, both runtime branches and inspection`, async ({ page }, info) => {
    info.annotations.push({ type: "evidence", description: "Embedded prototype in disposable real hosts; not final production adoption or full profile/UX acceptance." });
    const browserErrors: string[] = [];
    let consoleErrorCount = 0;
    page.on("pageerror", error => browserErrors.push(error.message));
    page.on("console", message => { if (message.type() === "error") consoleErrorCount++; });
    await page.addInitScript(mode => {
      localStorage.removeItem("elsa-studio-theme");
      localStorage.setItem("elsa-studio-theme-mode", mode);
      localStorage.setItem("elsa-studio-workspace-style", "linen");
      const observed = window as unknown as { __elsaBpmnUnexpectedRefusals: string[] };
      observed.__elsaBpmnUnexpectedRefusals = [];
      const inspect = () => {
        for (const notice of document.querySelectorAll("[data-bpmn-js-canvas] [role='alert']")) {
          if (notice.textContent?.includes("Change refused:")) observed.__elsaBpmnUnexpectedRefusals.push("Normal editing displayed a change-refused notice");
        }
      };
      new MutationObserver(records => {
        for (const record of records) {
          if (record.type === "attributes" && (record.oldValue === "refused"
            || (record.target as HTMLElement).getAttribute("data-bpmn-js-state") === "refused")) {
            observed.__elsaBpmnUnexpectedRefusals.push("Normal editing entered refused state");
          }
          for (const node of record.addedNodes) {
            if (node instanceof Element && node.matches(".wf-bpmn-js-notice") && node.textContent?.includes("Change refused:"))
              observed.__elsaBpmnUnexpectedRefusals.push("Normal editing inserted a change-refused notice");
          }
        }
        inspect();
      }).observe(document, { subtree: true, childList: true, attributes: true, attributeFilter: ["data-bpmn-js-state"], attributeOldValue: true });
    }, info.project.use.colorScheme ?? "light");
    await info.attach("source", { body: process.env.UX_SOURCE_SHA ?? "unknown", contentType: "text/plain" });
    const base = process.env.ELSA_WORKBENCH_URL!;
    const api: Api = async <T>(path: string, method = "GET", data?: unknown): Promise<T> => {
      const response = await page.request.fetch(`${base}${path}`, { method, data });
      expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
      return response.status() === 204 ? undefined as T : await response.json() as T;
    };
    await page.goto("/workflows/definitions");
    const login = page.locator('form[action="/_elsa/identity/login"]');
    await login.getByLabel(/username/i).fill("admin");
    await login.getByLabel(/password/i).fill("Password123!");
    await login.getByRole("button", { name: /sign in/i }).click();
    await expect(page.getByRole("heading", { name: "Workflow definitions", exact: true })).toBeVisible({ timeout: 60_000 });
    await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
    const { submitted, baseline } = await seed(api, `${firstEditor}-${info.project.name}-${Date.now()}`);
    const receipt: Record<string, unknown> = { source: process.env.UX_SOURCE_SHA, firstEditor, definitionId: submitted.definition.id,
      draftId: submitted.draftId, state: "incomplete", runs: [], publicXml: "Not a complete Elsa authoring transport; internal preservation is asserted separately." };
    try {
      await page.goto(`/workflows/definitions?backend=legacy&definition=${submitted.definition.id}`);
      await chooseEditor(page, firstEditor);
      if (firstEditor === "bpmn-js") await captureInitialNativeFit(page, info);
      await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
      const autosave = page.getByRole("menuitemcheckbox", { name: /^Autosave/ });
      if (await autosave.getAttribute("aria-checked") === "true") await autosave.click();
      await page.keyboard.press("Escape");
      await selectElement(page, firstEditor, "gateway");
      await routing(page).getByLabel("Default flow to Nested low", { exact: true }).check();
      await routing(page).getByLabel("Condition outcome for flow to High", { exact: true }).fill("High");
      let beforeNativeRename = "Order routing";
      if (firstEditor === "bpmn-js") {
        // The reverse journey also edits in Elsa before returning to the alternate canvas. Merely
        // selecting each editor in a different order would not exercise the other writing direction.
        await chooseEditor(page, "integrated");
        await selectElement(page, "integrated", "gateway");
        beforeNativeRename = "Integrated handoff from bpmn-js";
        await routing(page).getByLabel("Name", { exact: true }).fill(beforeNativeRename);
        await routing(page).getByLabel("Name", { exact: true }).blur();
      }
      await chooseEditor(page, "bpmn-js");
      if (firstEditor === "integrated") await captureInitialNativeFit(page, info);
      await selectElement(page, "bpmn-js", "gateway");
      await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue(beforeNativeRename);
      await expect(routing(page).getByLabel("Default flow to Nested low", { exact: true })).toBeChecked();
      await expect(routing(page).getByLabel("Condition outcome for flow to High", { exact: true })).toHaveValue("High");
      await selectElement(page, "bpmn-js", "high-task");
      await page.getByRole("tab", { name: "Slots", exact: true }).click();
      await page.locator(".wf-slot-list .wf-slot-row > button").first().click();
      await page.locator(".wf-sequence-step[data-id='high']").click();
      await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
      await page.locator(".cm-content[contenteditable='true']").fill('"Edited high"');
      await page.locator(".wf-sequence-step[data-id='high']").click();
      await expect(page.getByRole("button", { name: /JavaScript expression.*Activate to edit/ })).toContainText('"Edited high"');
      await capture(page, info, "high-inline-binding-through-shared-inspector");
      await page.locator(".wf-breadcrumb").getByRole("button", { name: "Root", exact: true }).click();
      await expect(alternate(page)).toHaveAttribute("data-bpmn-js-state", "ready");

      await selectElement(page, "bpmn-js", "low-task");
      await page.getByRole("tab", { name: "Slots", exact: true }).click();
      await page.locator(".wf-slot-list .wf-slot-row > button").first().click();
      await expect(page.locator(".wf-sequence-step[data-id='low-inline']")).toBeVisible();
      await capture(page, info, "nested-sequence-through-shared-inspector");
      await page.locator(".wf-breadcrumb").getByRole("button", { name: "Root", exact: true }).click();
      await expect(alternate(page)).toHaveAttribute("data-bpmn-js-state", "ready");

      const gateway = shape(page, "gateway");
      const initialTransform = await gateway.getAttribute("transform");
      const bounds = await gateway.boundingBox();
      expect(bounds).not.toBeNull();
      await page.mouse.move(bounds!.x + bounds!.width / 2, bounds!.y + bounds!.height / 2);
      await page.mouse.down();
      await page.mouse.move(bounds!.x + bounds!.width / 2 + 55, bounds!.y + bounds!.height / 2 + 30, { steps: 12 });
      await page.mouse.up();
      await expect(gateway).not.toHaveAttribute("transform", initialTransform!);
      await chooseEditor(page, "integrated");
      await chooseEditor(page, "bpmn-js");
      const name = `Edited routing from ${firstEditor}`;
      await shape(page, "gateway").locator(".djs-hit").dblclick();
      const nativeLabel = page.locator(".djs-direct-editing-content[contenteditable='true']");
      await nativeLabel.fill(name);
      await nativeLabel.press("Enter");
      await selectElement(page, "bpmn-js", "gateway");
      await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue(name);
      await expect(alternate(page).locator(".bjs-powered-by")).toBeVisible();
      expect(await alternate(page).locator(".bjs-powered-by").evaluate(element => {
        const box = element.getBoundingClientRect();
        return element.contains(document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2));
      })).toBe(true);
      await capture(page, info, "alternate-shared-routing-and-attribution");
      const enteringPath = () => alternate(page).locator(".djs-connection[data-element-id='enter'] .djs-visual > path").first();
      const pathBeforeIntegratedMove = await enteringPath().getAttribute("d");

      await chooseEditor(page, "integrated");
      await selectElement(page, "integrated", "gateway");
      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await selectElement(page, "integrated", "gateway");
      await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue(beforeNativeRename);
      await page.getByRole("button", { name: "Redo", exact: true }).click();
      await selectElement(page, "integrated", "gateway");
      await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue(name);
      // Existing native DI must follow a subsequent integrated edit, including connected routes.
      const integratedGateway = page.locator(".react-flow__node[data-id='gateway']");
      const integratedTransform = await integratedGateway.getAttribute("style");
      const integratedBounds = await integratedGateway.locator(".wf-bpmn-gateway").boundingBox();
      expect(integratedBounds).not.toBeNull();
      await page.mouse.move(integratedBounds!.x + integratedBounds!.width / 2, integratedBounds!.y + integratedBounds!.height / 2);
      await page.mouse.down();
      await page.mouse.move(integratedBounds!.x + integratedBounds!.width / 2 + 35, integratedBounds!.y + integratedBounds!.height / 2 - 25, { steps: 10 });
      await page.mouse.up();
      await expect(integratedGateway).not.toHaveAttribute("style", integratedTransform!);
      await chooseEditor(page, "bpmn-js");
      await expect(enteringPath()).not.toHaveAttribute("d", pathBeforeIntegratedMove!);
      await capture(page, info, "alternate-after-integrated-layout-edit");
      await chooseEditor(page, "integrated");
      expect(payload(await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`)).elements.find(element => element.elementId === "gateway")!.name).toBe("Order routing");
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect.poll(async () => payload(await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`)).elements.find(element => element.elementId === "gateway")!.name).toBe(name);
      const saved = await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`);
      expect(saved.id).toBe(baseline.id);
      expect(saved.definitionId).toBe(submitted.definition.id);
      expect(saved.state.rootActivity.nodeId).toBe("process");
      expect(saved.state.rootActivity.activityVersionId).toBe(baseline.state.rootActivity.activityVersionId);
      expect(payload(saved).activities.find(node => node.nodeId === "nested-low")).toEqual(payload(baseline).activities.find(node => node.nodeId === "nested-low"));
      expect(payload(saved).activities.find(node => node.nodeId === "decision")).toEqual(payload(baseline).activities.find(node => node.nodeId === "decision"));
      const expectedHigh = structuredClone(payload(baseline).activities.find(node => node.nodeId === "nested-high")!);
      const expectedHighInline = (expectedHigh.structure!.payload.activities as Node[]).find(node => node.nodeId === "high")!;
      const expressionInput = expectedHighInline.inputs.find(input => (input as { referenceKey: string }).referenceKey === "Expression") as { value: { value: string } };
      expressionInput.value.value = '"Edited high"';
      expect(payload(saved).activities.find(node => node.nodeId === "nested-high")).toEqual(expectedHigh);
      expect(payload(saved).elements).toEqual(expect.arrayContaining([expect.objectContaining({ elementId: "gateway", childNodeId: "decision", defaultFlowId: "low-flow", name })]));
      expect(payload(saved).sequenceFlows).toEqual(expect.arrayContaining([
        expect.objectContaining({ flowId: "high-flow", sourceRef: "gateway", targetRef: "high-task", conditionOutcome: "High", isDefault: false }),
        expect.objectContaining({ flowId: "low-flow", sourceRef: "gateway", targetRef: "low-task", conditionOutcome: null, isDefault: true })
      ]));
      expect(saved.layout.find(record => record.nodeId === "gateway")).not.toMatchObject({ x: 240, y: 210 });
      const diagram = saved.state.rootActivity.structure!.payload.diagram as {
        shapes: Record<string, { x: number; y: number; width: number; height: number }>;
        edges: Record<string, { waypoints: { x: number; y: number }[] }>;
      };
      const gatewayShape = diagram.shapes.gateway;
      expect(saved.layout.find(record => record.nodeId === "gateway")).toMatchObject({ x: gatewayShape.x, y: gatewayShape.y });
      const arrival = diagram.edges.enter.waypoints.at(-1)!;
      expect([[gatewayShape.x, gatewayShape.y + gatewayShape.height / 2], [gatewayShape.x + gatewayShape.width, gatewayShape.y + gatewayShape.height / 2],
        [gatewayShape.x + gatewayShape.width / 2, gatewayShape.y], [gatewayShape.x + gatewayShape.width / 2, gatewayShape.y + gatewayShape.height]])
        .toContainEqual([arrival.x, arrival.y]);
      for (const id of ["high", "capture-high", "low-inline", "capture-low"]) expect(saved.layout.find(record => record.nodeId === id)).toEqual(baseline.layout.find(record => record.nodeId === id));
      await capture(page, info, "integrated-saved-same-process");

      await page.getByRole("button", { name: "More workflow actions", exact: true }).click();
      const download = page.waitForEvent("download");
      await page.getByRole("menuitem", { name: "Export BPMN", exact: true }).click();
      const xmlPath = info.outputPath("edited-process.bpmn");
      await (await download).saveAs(xmlPath);
      const xml = readFileSync(xmlPath, "utf8");
      expect(xml).toMatch(/exclusiveGateway\b[^>]*\bid="gateway"/);
      expect(xml).toContain('default="low-flow"');
      expect(xml).toContain("BPMNShape");
      await info.attach("edited-process.bpmn", { path: xmlPath, contentType: "application/xml" });
      receipt.publicXmlContainsEditedInlineExpression = xml.includes("Edited high");
      await expectNoRefusals(page);

      await page.reload();
      await chooseEditor(page, "bpmn-js");
      await selectElement(page, "bpmn-js", "gateway");
      await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue(name);
      await expect(routing(page).getByLabel("Default flow to Nested low", { exact: true })).toBeChecked();
      await expect(routing(page).getByLabel("Condition outcome for flow to High", { exact: true })).toHaveValue("High");
      expect(await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`)).toEqual(saved);
      await capture(page, info, "alternate-after-real-save-reload");
      await expectNoRefusals(page);

      // These are the production promotion/publication endpoints, applied to the exact saved draft.
      // This prototype does not claim the separate UI publication review journey has been qualified.
      await page.goto("/workflows/instances?backend=legacy");
      const promoted = await api<{ id: string; version: string }>(`/design/workflows/drafts/${submitted.draftId}/promote`, "POST", {});
      const published = await api<Published>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
      receipt.publication = { ...published, definitionVersionId: promoted.id, version: promoted.version };
      const runs: Record<string, unknown>[] = [];
      receipt.runs = runs;
      for (const [route, expected, producer, absent] of [["High", "Edited high", "high", "low-inline"], ["Done", "Nested low", "low-inline", "high"]]) {
        const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId, inputs: { route } });
        let instance!: Instance;
        await expect.poll(async () => {
          instance = await api<Instance>(`/runtime/workflows/instances/${started.workflowExecutionId}`);
          return instance.instance.status;
        }, { timeout: 60_000 }).toMatch(/^(Completed|Finished|Faulted|Cancelled|Aborted)$/);
        if (!/^(Completed|Finished)$/.test(instance.instance.status)) {
          await info.attach(`runtime-${route}-failure.json`, { body: JSON.stringify({ instance }, null, 2), contentType: "application/json" });
        }
        expect(instance.instance.status, `Actual ${route} run must complete without a runtime fault`).toMatch(/^(Completed|Finished)$/);
        expect(instance.instance.definitionId).toBe(submitted.definition.id);
        expect(instance.instance.definitionVersionId).toBe(promoted.id);
        expect(instance.instance.sourceReferenceId).toBe(published.sourceReferenceId);
        expect(instance.incidents).toEqual([]);
        expect(outputValue(instance)).toBe(expected);
        const occurrence = instance.activities.filter(activity => activity.authoredActivityId === producer);
        expect(occurrence).toHaveLength(1);
        expect(instance.activities.filter(activity => activity.authoredActivityId === absent)).toHaveLength(0);
        const inspection = await api<Record<string, unknown>>(`/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions/${occurrence[0].activityExecutionId}`);
        expect(inspection.authoredActivityId).toBe(producer);
        expect(inspection.status).toBe("Completed");
        await info.attach(`runtime-${route}.json`, { body: JSON.stringify({ instance, inspection }, null, 2), contentType: "application/json" });
        runs.push({ route, expected, workflowExecutionId: started.workflowExecutionId, producer, activityExecutionId: occurrence[0].activityExecutionId, actual: outputValue(instance), state: "passed" });
        await page.goto(`/workflows/instances/${started.workflowExecutionId}?backend=legacy&selected=${occurrence[0].activityExecutionId}&tab=activity`);
        await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible();
        await expect(page.getByRole("heading", { name: "Run", exact: true })).toBeVisible();
        await expect(page.locator(".wf-activity-overview")).toContainText("Inline");
        // Await the resolved runtime input and execution browser before capturing.
        // The Completed badge can precede these asynchronous evidence requests.
        await expect(page.locator(".wf-activity-overview-status")).toHaveText("Completed");
        await expect(page.locator('.wf-input-inspection-value[aria-label="Expression runtime value"]')).toHaveText(expected);
        await expect(page.getByRole("status").filter({ hasText: /^(?:Loading visible execution evidence|Loading recorded executions|Loading captured value|Resolving captured value|Some visible execution evidence could not be loaded|Execution history (?:could not be loaded|is unavailable)|Execution browsing is unavailable)/ })).toHaveCount(0);
        await capture(page, info, `actual-run-inspection-${route.toLowerCase()}`);
      }
      expect(browserErrors, "Uncaught real-host browser errors").toEqual([]);
      await expectNoRefusals(page);
      receipt.state = "passed-prototype";
    } finally {
      receipt.browserErrors = browserErrors;
      receipt.consoleErrorCount = consoleErrorCount;
      const receiptPath = info.outputPath("prototype-receipt.json");
      writeFileSync(receiptPath, JSON.stringify(receipt, null, 2));
      await info.attach("prototype-receipt.json", { path: receiptPath, contentType: "application/json" });
      await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE");
    }
  });
}
