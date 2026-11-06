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
const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });

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

async function selectElement(page: Page, editor: "integrated" | "bpmn-js", id: string) {
  if (editor === "bpmn-js") {
    // A second click toggles an already-selected native shape. Always make this a new selection.
    await shape(page, "end-high").locator(".djs-hit").click();
    await shape(page, id).locator(".djs-hit").click();
  } else await page.locator(`.react-flow__node[data-id='${id}']`).click();
}

async function seed(api: Api, suffix: string) {
  async function version(type: string) {
    const list = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const item = list.items.find(item => item.definition.activityTypeKey === type);
    expect(item, `Real catalog supplies ${type}`).toBeTruthy();
    return item!.definition.headVersionId;
  }
  const [bpmnVersion, decisionVersion, inlineVersion, sequenceVersion] = await Promise.all([
    version("Elsa.Activities.Bpmn.Activities.BpmnProcess"), version("Elsa.Activities.Bpmn.Activities.BpmnDecision"),
    version("Elsa.Activities.Primitives.Activities.Inline"), version("Elsa.Activities.Sequence.Activities.Sequence")
  ]);
  const inline = (nodeId: string, expression: string): Node => ({ nodeId, activityVersionId: inlineVersion, structure: null,
    inputs: [{ referenceKey: "Expression", value: { expressionType: "JavaScript", value: expression } }], outputs: [] });
  const captureResult = (nodeId: string, producerNodeId: string, producerScopeId: string): Node => ({
    nodeId, activityVersionId: "elsa.intrinsic.set-output@1", structure: null, outputs: [],
    intrinsic: { kind: "setOutput", valueType: { alias: "Elsa.Any", collectionKind: "single" } },
    inputs: [literal("name", "result"), { referenceKey: "value", value: {
      expressionType: "ActivityResult", value: { producerNodeId, producerScopeId, projectionKey: "$result" }
    } }]
  });
  const nested: Node = { nodeId: "nested-low", activityVersionId: sequenceVersion, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: {
      activities: [inline("low-inline", '"Nested low"'), captureResult("capture-low", "low-inline", "nested-low")]
    } } };
  // Required ActivityResult ordering is currently established by Sequence, not by the BPMN
  // validator. Keep the real Inline result/capture in that supported scope; attempt3 separately
  // retains the rejected direct-BPMN-sibling case rather than making its reference optional.
  const nestedHigh: Node = { nodeId: "nested-high", activityVersionId: sequenceVersion, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: {
      activities: [inline("high", '"Original high"'), captureResult("capture-high", "high", "nested-high")]
    } } };
  const elements: Element[] = [
    { elementId: "start", elementType: "startEvent" },
    { elementId: "gateway", elementType: "exclusiveGateway", name: "Order routing", childNodeId: "decision", defaultFlowId: "high-flow" },
    { elementId: "high-task", elementType: "subProcess", name: "High", childNodeId: "nested-high" },
    { elementId: "low-task", elementType: "subProcess", name: "Nested low", childNodeId: "nested-low" },
    { elementId: "end-high", elementType: "endEvent" }, { elementId: "end-low", elementType: "endEvent" }
  ];
  const sequenceFlows: Flow[] = [
    { flowId: "enter", sourceRef: "start", targetRef: "gateway" },
    { flowId: "high-flow", sourceRef: "gateway", targetRef: "high-task", isDefault: true },
    { flowId: "low-flow", sourceRef: "gateway", targetRef: "low-task", conditionOutcome: "Low" },
    { flowId: "high-end-flow", sourceRef: "high-task", targetRef: "end-high" },
    { flowId: "low-end-flow", sourceRef: "low-task", targetRef: "end-low" }
  ];
  const root: Node = { nodeId: "process", activityVersionId: bpmnVersion, inputs: [], outputs: [], structure: {
    kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
      activities: [{ nodeId: "decision", activityVersionId: decisionVersion, structure: null, inputs: [{ referenceKey: "Outcome", value: {
        expressionType: "WorkflowRequest", value: { memberKey: "route" }
      } }], outputs: [] }, nestedHigh, nested],
      elements, sequenceFlows
    }
  } };
  const submitted = await api<Submitted>("/design/workflows/definitions/submit", "POST", {
    name: `BPMN same-process ${suffix}`, state: { rootActivity: root, variables: [], outputs: [], inputs: [{
      referenceKey: "route", name: "route", displayName: "Route", isNullable: true,
      type: { alias: "String", collectionKind: "single" }, storageDriverType: null
    }] }
  });
  const initial = await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`);
  const layout: Layout[] = [
    { nodeId: "start", x: 60, y: 220 }, { nodeId: "gateway", x: 240, y: 210 },
    { nodeId: "high-task", x: 440, y: 100 },
    { nodeId: "low-task", x: 440, y: 360 }, { nodeId: "end-high", x: 700, y: 120 }, { nodeId: "end-low", x: 700, y: 380 },
    { nodeId: "high", x: 120, y: 120 }, { nodeId: "capture-high", x: 360, y: 120 },
    { nodeId: "low-inline", x: 120, y: 120 }, { nodeId: "capture-low", x: 360, y: 120 }
  ];
  const baseline = await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`, "PUT", { state: initial.state, layout });
  return { submitted, baseline };
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
    page.on("pageerror", error => browserErrors.push(error.message));
    await page.addInitScript(mode => {
      localStorage.removeItem("elsa-studio-theme");
      localStorage.setItem("elsa-studio-theme-mode", mode);
      localStorage.setItem("elsa-studio-workspace-style", "linen");
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

      await page.reload();
      await chooseEditor(page, "bpmn-js");
      await selectElement(page, "bpmn-js", "gateway");
      await expect(routing(page).getByLabel("Name", { exact: true })).toHaveValue(name);
      await expect(routing(page).getByLabel("Default flow to Nested low", { exact: true })).toBeChecked();
      await expect(routing(page).getByLabel("Condition outcome for flow to High", { exact: true })).toHaveValue("High");
      expect(await api<Draft>(`/design/workflows/drafts/${submitted.draftId}`)).toEqual(saved);
      await capture(page, info, "alternate-after-real-save-reload");

      // These are the production promotion/publication endpoints, applied to the exact saved draft.
      // This prototype does not claim the separate UI publication review journey has been qualified.
      await page.goto("/workflows/instances?backend=legacy");
      const promoted = await api<{ id: string; version: string }>(`/design/workflows/drafts/${submitted.draftId}/promote`, "POST", {});
      const published = await api<Published>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
      receipt.publication = { ...published, definitionVersionId: promoted.id, version: promoted.version };
      const runs: Record<string, unknown>[] = [];
      receipt.runs = runs;
      for (const [route, expected, producer, absent] of [["High", "Edited high", "high", "low-inline"], ["Unknown", "Nested low", "low-inline", "high"]]) {
        const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId, inputs: { route } });
        let instance!: Instance;
        await expect.poll(async () => {
          instance = await api<Instance>(`/runtime/workflows/instances/${started.workflowExecutionId}`);
          return instance.instance.status;
        }, { timeout: 60_000 }).toMatch(/^(Completed|Finished)$/);
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
        await capture(page, info, `actual-run-inspection-${route.toLowerCase()}`);
      }
      expect(browserErrors, "Uncaught real-host browser errors").toEqual([]);
      receipt.state = "passed-prototype";
    } finally {
      receipt.browserErrors = browserErrors;
      const receiptPath = info.outputPath("prototype-receipt.json");
      writeFileSync(receiptPath, JSON.stringify(receipt, null, 2));
      await info.attach("prototype-receipt.json", { path: receiptPath, contentType: "application/json" });
      await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE");
    }
  });
}
