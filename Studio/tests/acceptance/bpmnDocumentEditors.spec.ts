import { expect, test, type Page } from "@playwright/test";
import { seedBpmnPrototype, readCanvasFailures, capture, attachment, login, choose, select, label, savedName, disableAutosave, noRefusals, exportXml, native, properties, nativeShape, integratedShape, type Api, type Draft, type Node as ActivityNode, type Instance, type Published } from "./bpmnDocumentAcceptanceFixtures";

type Payload = { documentXml: string; processId: string; activities: ActivityNode[]; bindings: { elementId: string; childNodeId?: string }[]; [key: string]: unknown };
const model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
const vendor = "https://elsa-workflows.io/schemas/bpmn";
const payload = (draft: Draft) => draft.state.rootActivity.structure!.payload as Payload;
async function diPosition(page: Page, source: string, id: string): Promise<{ x: number; y: number }> {
  return page.evaluate(({ source, id }) => {
    const doc = new DOMParser().parseFromString(source, "application/xml");
    const matching = [...doc.getElementsByTagNameNS("http://www.omg.org/spec/BPMN/20100524/DI", "BPMNShape")].filter(shape => {
      const reference = shape.getAttribute("bpmnElement") ?? "";
      if (!reference.includes(":")) return reference === id;
      const [prefix, local] = reference.split(":");
      return local === id && shape.lookupNamespaceURI(prefix) === doc.documentElement.getAttribute("targetNamespace");
    });
    if (matching.length !== 1) throw new Error(`Expected one actual DI instance of '${id}', got ${matching.length}`);
    const bounds = matching[0].getElementsByTagNameNS("http://www.omg.org/spec/DD/20100524/DC", "Bounds")[0];
    if (!bounds || bounds.getAttribute("x") === null || bounds.getAttribute("y") === null) throw new Error(`Missing '${id}' DI bounds`);
    const x = Number(bounds.getAttribute("x")), y = Number(bounds.getAttribute("y"));
    if (!Number.isFinite(x) || !Number.isFinite(y)) throw new Error(`Non-finite '${id}' DI bounds`);
    return { x, y };
  }, { source, id });
}
function assertNative(draft: Draft, expected: ActivityNode[]) {
  expect(draft.state.rootActivity.structure!.schemaVersion).toBe("2.0.0");
  for (const field of ["elements", "sequenceFlows", "diagram", "pools", "lanes", "messageFlows", "isTransaction"]) expect(Object.hasOwn(payload(draft), field)).toBe(false);
  expect(payload(draft).activities).toEqual(expected);
}
function output(instance: Instance) {
  const result = instance.outputs.result; expect(result.isRedacted).toBe(false);
  return result.value && typeof result.value === "object" ? (result.value as { preview?: unknown; value?: unknown }).preview ?? (result.value as { value?: unknown }).value : result.value;
}

for (const firstEditor of ["integrated", "bpmn-js"] as const) {
  test(`full-source from ${firstEditor}: migration, shared inspector, history, two published routes and settled inspection`, async ({ page }, info) => {
    const browserErrors: string[] = []; let consoleErrorCount = 0;
    page.on("pageerror", error => browserErrors.push(error.message)); page.on("console", message => { if (message.type() === "error") consoleErrorCount++; });
    const api = await login(page, info), { submitted, baseline } = await seedBpmnPrototype(api, `source2-${firstEditor}-${info.project.name}-${Date.now()}`);
    const draftPath = `/design/workflows/drafts/${submitted.draftId}`;
    const receipt: Record<string, unknown> = { source: process.env.UX_SOURCE_SHA, firstEditor, kind: "runtime", definitionId: submitted.definition.id, draftId: submitted.draftId, state: "incomplete", runs: [] };
    try {
      await page.goto(`/workflows/definitions?backend=legacy&definition=${submitted.definition.id}`); await choose(page, firstEditor); await disableAutosave(page);
      await page.getByRole("button", { name: "Save", exact: true }).click();
      expect((await api<Draft>(draftPath)).state.rootActivity.structure!.schemaVersion).toBe("1.0.0");
      await capture(page, info, "opened-without-migration");
      await label(page, firstEditor, "gateway", "First source edit");
      const other = firstEditor === "integrated" ? "bpmn-js" : "integrated";
      await choose(page, other);
      await label(page, other, "gateway", "Complete source routing", false);
      // Explicit Save must finish a local integrated property or native serialization group.
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect.poll(async () => { const saved = await api<Draft>(draftPath); return saved.state.rootActivity.structure!.schemaVersion === "2.0.0" ? savedName(page, payload(saved).documentXml, "gateway") : "legacy"; }).toBe("Complete source routing");
      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await select(page, other, "gateway");
      await properties(page).getByRole("button", { name: "Edit label", exact: true }).click();
      await expect(properties(page).getByLabel("Label", { exact: true })).toHaveValue("First source edit");
      await properties(page).getByRole("button", { name: "Discard BPMN property", exact: true }).click();
      await page.getByRole("button", { name: "Redo", exact: true }).click();
      await choose(page, "bpmn-js");
      const beforePoolXml = await exportXml(page, info, "before-pool.bpmn");
      const participantTool = native(page).locator('.djs-palette .entry[data-action="create.participant-expanded"]');
      await participantTool.click();
      const canvasBounds = await native(page).locator(".djs-container").boundingBox(); expect(canvasBounds).not.toBeNull();
      await page.mouse.move(canvasBounds!.x + canvasBounds!.width * 0.7, canvasBounds!.y + canvasBounds!.height * 0.65, { steps: 6 });
      await page.mouse.click(canvasBounds!.x + canvasBounds!.width * 0.7, canvasBounds!.y + canvasBounds!.height * 0.65);
      await expect(native(page).locator(".djs-shape[data-element-id^='Participant_']")).not.toHaveCount(0);
      await capture(page, info, "native-pool-create-before-shared-undo");
      await page.getByRole("button", { name: "Undo", exact: true }).click();
      await expect(native(page)).toHaveAttribute("data-bpmn-js-state", "ready");
      await expect(native(page).locator(".djs-shape[data-element-id^='Participant_']")).toHaveCount(0);
      expect(await exportXml(page, info, "after-pool-undo.bpmn")).toBe(beforePoolXml);
      await choose(page, "bpmn-js"); await select(page, "bpmn-js", "high-task");
      await page.getByRole("region", { name: "Elsa activity configuration", exact: true }).getByRole("tab", { name: "Slots", exact: true }).click();
      await page.locator(".wf-slot-list .wf-slot-row > button").first().click();
      await page.locator(".wf-sequence-step[data-id='high']").click();
      await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
      await page.locator(".cm-content[contenteditable='true']").fill('"Edited high"');
      await page.locator(".wf-sequence-step[data-id='high']").click();
      await capture(page, info, "native-sequence-through-shared-inspector");
      await page.locator(".wf-breadcrumb").getByRole("button", { name: "Root", exact: true }).click(); await choose(page, "bpmn-js");
      const beforeDrag = await diPosition(page, await exportXml(page, info, "before-active-drag.bpmn"), "gateway");
      const before = await nativeShape(page, "gateway").getAttribute("transform"), box = await nativeShape(page, "gateway").boundingBox(); expect(box).not.toBeNull();
      await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2); await page.mouse.down();
      await page.mouse.move(box!.x + box!.width / 2 + 42, box!.y + box!.height / 2 + 28, { steps: 10 });
      // Keyboard activation of the real Save control while the pointer remains held exercises
      // Modeler's actual drag completion through the parent checkpoint, before mouseup.
      const saveDuringDrag = page.getByRole("button", { name: "Save", exact: true });
      await saveDuringDrag.focus(); await saveDuringDrag.press("Enter");
      let afterDrag = beforeDrag;
      await expect.poll(async () => {
        const saved = await api<Draft>(draftPath); expect(saved.state.rootActivity.structure!.schemaVersion).toBe("2.0.0");
        afterDrag = await diPosition(page, payload(saved).documentXml, "gateway");
        return afterDrag.x !== beforeDrag.x || afterDrag.y !== beforeDrag.y;
      }).toBe(true);
      receipt.activeDrag = { before: beforeDrag, after: afterDrag, savedBeforeMouseup: true };
      await page.mouse.up();
      await expect(nativeShape(page, "gateway")).not.toHaveAttribute("transform", before!);
      await expect(native(page).locator(".bjs-powered-by")).toBeVisible(); await capture(page, info, "native-after-shared-configuration");
      await choose(page, "integrated");
      const shape = integratedShape(page, "gateway"), initial = await shape.getAttribute("style"), bounds = await shape.boundingBox(); expect(bounds).not.toBeNull();
      await shape.focus(); await shape.press("ArrowRight");
      await expect(shape).not.toHaveAttribute("style", initial!);
      await capture(page, info, "integrated-keyboard-layout-and-properties");
      await page.getByRole("button", { name: "Save", exact: true }).click();
      const expected = structuredClone(baseline.state.rootActivity.structure!.payload.activities as ActivityNode[]);
      const inline = ((expected.find(node => node.nodeId === "nested-high")!.structure!.payload.activities) as ActivityNode[]).find(node => node.nodeId === "high")!;
      (inline.inputs.find(value => (value as { referenceKey: string }).referenceKey === "Expression") as { value: { value: string } }).value.value = '"Edited high"';
      await expect.poll(async () => (await api<Draft>(draftPath)).state.rootActivity.structure!.payload.activities).toEqual(expected);
      const saved = await api<Draft>(draftPath); assertNative(saved, expected);
      expect(saved.id).toBe(baseline.id); expect(saved.definitionId).toBe(baseline.definitionId);
      expect(saved.state.rootActivity.nodeId).toBe(baseline.state.rootActivity.nodeId);
      expect(saved.state.rootActivity.activityVersionId).toBe(baseline.state.rootActivity.activityVersionId);
      expect(await savedName(page, payload(saved).documentXml, "gateway")).toBe("Complete source routing");
      for (const id of ["start", "gateway", "high-task", "low-task", "end-high", "end-low"]) expect(saved.layout.some(row => row.nodeId === id)).toBe(false);
      for (const id of ["high", "capture-high", "low-inline", "capture-low"]) expect(saved.layout.find(row => row.nodeId === id)).toEqual(baseline.layout.find(row => row.nodeId === id));
      await attachment(info, "expected-native-activities.json", expected); await attachment(info, "saved-draft.json", saved);
      expect(await exportXml(page, info, "complete-process.bpmn")).toBe(payload(saved).documentXml);
      await noRefusals(page);
      await page.reload(); await choose(page, "bpmn-js"); await expect(nativeShape(page, "gateway")).toContainText("Complete source routing");
      expect(await api<Draft>(draftPath)).toEqual(saved); await capture(page, info, "native-after-save-reload"); await noRefusals(page);
      await page.goto("/workflows/instances?backend=legacy");
      const promoted = await api<{ id: string; version: string }>(`${draftPath}/promote`, "POST", {}), published = await api<Published>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
      receipt.publication = { ...published, definitionVersionId: promoted.id, version: promoted.version };
      const runs: Record<string, unknown>[] = []; receipt.runs = runs;
      for (const [route, expectedValue, producer, absent] of [["High", "Edited high", "high", "low-inline"], ["Low", "Nested low", "low-inline", "high"]]) {
        const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId, inputs: { route } });
        let instance!: Instance;
        await expect.poll(async () => { instance = await api<Instance>(`/runtime/workflows/instances/${started.workflowExecutionId}`); return instance.instance.status; }, { timeout: 60_000 }).toMatch(/^(Completed|Finished|Faulted|Cancelled|Aborted)$/);
        expect(instance.instance.status).toMatch(/^(Completed|Finished)$/); expect(instance.incidents).toEqual([]); expect(output(instance)).toBe(expectedValue);
        expect(instance.instance.definitionId).toBe(submitted.definition.id); expect(instance.instance.definitionVersionId).toBe(promoted.id); expect(instance.instance.sourceReferenceId).toBe(published.sourceReferenceId);
        const occurrence = instance.activities.filter(activity => activity.authoredActivityId === producer); expect(occurrence).toHaveLength(1); expect(instance.activities.filter(activity => activity.authoredActivityId === absent)).toHaveLength(0);
        const inspection = await api<Record<string, unknown>>(`/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions/${occurrence[0].activityExecutionId}`);
        expect(inspection.authoredActivityId).toBe(producer); expect(inspection.status).toBe("Completed");
        await attachment(info, `runtime-${route}.json`, { instance, inspection });
        runs.push({ route, expected: expectedValue, actual: output(instance), producer, workflowExecutionId: started.workflowExecutionId, activityExecutionId: occurrence[0].activityExecutionId, state: "passed" });
        await page.goto(`/workflows/instances/${started.workflowExecutionId}?backend=legacy&selected=${occurrence[0].activityExecutionId}&tab=activity`);
        await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible();
        await expect(page.locator(".wf-activity-overview-status")).toHaveText("Completed");
        await expect(page.locator('.wf-input-inspection-value[aria-label="Expression runtime value"]')).toHaveText(expectedValue);
        await expect(page.getByRole("status").filter({ hasText: /^(?:Loading visible execution evidence|Loading recorded executions|Loading captured value|Resolving captured value|Some visible execution evidence could not be loaded|Execution history (?:could not be loaded|is unavailable)|Execution browsing is unavailable)/ })).toHaveCount(0);
        await capture(page, info, `actual-run-inspection-${route.toLowerCase()}`);
      }
      expect(browserErrors).toEqual([]);
      expect(await page.evaluate(() => (window as unknown as { __elsaBpmnUnexpectedRefusals: string[] }).__elsaBpmnUnexpectedRefusals)).toEqual([]);
      receipt.state = "passed-full-source-runtime";
    } finally { receipt.unexpectedCanvasFailures = await readCanvasFailures(page); receipt.browserErrors = browserErrors; receipt.consoleErrorCount = consoleErrorCount; await attachment(info, "document-receipt.json", receipt); await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE"); }
  });
}

async function advancedSource(page: Page, api: Api, baseline: Draft): Promise<ActivityNode> {
  const exported = await api<{ xml: string }>("/interchange/bpmn/export", "POST", { processNode: baseline.state.rootActivity });
  const documentXml = await page.evaluate(({ xml, model, vendor }) => {
    const doc = new DOMParser().parseFromString(xml, "application/xml"), definitions = doc.documentElement;
    definitions.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:q", "urn:retained-qname");
    const process = doc.getElementsByTagNameNS(model, "process")[0], plane = doc.getElementsByTagNameNS("http://www.omg.org/spec/BPMN/20100524/DI", "BPMNPlane")[0];
    const create = (name: string, attributes: Record<string, string> = {}, namespace = model, prefix = "bpmn") => {
      const element = doc.createElementNS(namespace, `${prefix}:${name}`); for (const [key, value] of Object.entries(attributes)) element.setAttribute(key, value); return element;
    };
    const diagramNS = "http://www.omg.org/spec/BPMN/20100524/DI", dc = "http://www.omg.org/spec/DD/20100524/DC", di = "http://www.omg.org/spec/DD/20100524/DI";
    const shape = (id: string, x: number, y: number, width: number, height: number, attrs: Record<string, string> = {}) => {
      const element = create("BPMNShape", { id: `${id}DI`, bpmnElement: id, ...attrs }, diagramNS, "bpmndi");
      element.append(create("Bounds", { x: String(x), y: String(y), width: String(width), height: String(height) }, dc, "dc")); plane.append(element); return element;
    };
    const extension = create("extensionElements"), opaque = create("keep", { ref: "q:Uninterpreted", literal: "native-high" }, "urn:retained-vendor", "v");
    opaque.append("before ", create("child", {}, "urn:retained-vendor", "v"), " after ", doc.createCDATASection("raw < text & native-high")); extension.append(opaque); process.prepend(extension);
    const boundary = create("boundaryEvent", { id: "TimerBoundary", attachedToRef: "high-task", cancelActivity: "false", name: "Deadline" });
    const timer = create("timerEventDefinition"), duration = create("timeDuration"); duration.textContent = "PT5M"; timer.append(duration); boundary.append(timer); process.append(boundary); shape("TimerBoundary", 510, 162, 36, 36);
    const script = create("scriptTask", { id: "Script", name: "Retained script", scriptFormat: "text/javascript" }), scriptBody = create("script");
    scriptBody.append(doc.createCDATASection('return "kept < script & value";')); script.append(scriptBody);
    const loop = create("multiInstanceLoopCharacteristics", { isSequential: "true" }), cardinality = create("loopCardinality"); cardinality.textContent = "3"; loop.append(cardinality); script.prepend(loop); process.append(script); shape("Script", 850, 100, 120, 90);
    process.append(create("complexGateway", { id: "Complex", name: "Complex routing" })); shape("Complex", 850, 260, 50, 50);
    const body = create("subProcess", { id: "EventBody", name: "Event body", triggeredByEvent: "true" }), start = create("startEvent", { id: "EventStart", isInterrupting: "false" });
    start.append(create("messageEventDefinition", { messageRef: "OrderMessage" })); body.append(start); process.append(body); shape("EventBody", 800, 420, 280, 160, { isExpanded: "true" }); shape("EventStart", 835, 465, 36, 36);
    process.append(create("dataObject", { id: "Data" }), create("dataObjectReference", { id: "DataRef", dataObjectRef: "Data", name: "Order data" })); shape("DataRef", 680, 500, 50, 64);
    const note = create("textAnnotation", { id: "Note" }), text = create("text"); text.textContent = "Full source retained"; note.append(text); process.append(note); shape("Note", 80, 500, 180, 60);
    const collaboration = create("collaboration", { id: "Collaboration", name: "Advanced collaboration" });
    collaboration.append(create("participant", { id: "White", name: "Authoring process", processRef: process.id }), create("participant", { id: "Black", name: "External participant" }));
    collaboration.append(create("messageFlow", { id: "MessageFlow", sourceRef: "Script", targetRef: "Black", messageRef: "OrderMessage" }));
    const firstDiagram = [...definitions.children].find(element => element.namespaceURI === diagramNS);
    definitions.insertBefore(create("message", { id: "OrderMessage", name: "Order" }), firstDiagram ?? null);
    definitions.insertBefore(collaboration, firstDiagram ?? null); plane.setAttribute("bpmnElement", "Collaboration");
    const pool = shape("White", 15, 25, 1100, 590, { isHorizontal: "true", isExpanded: "true" }); plane.prepend(pool);
    shape("Black", 15, 680, 1100, 80, { isHorizontal: "true", isExpanded: "false" });
    const edge = create("BPMNEdge", { id: "MessageFlowDI", bpmnElement: "MessageFlow" }, diagramNS, "bpmndi");
    edge.append(create("waypoint", { x: "910", y: "190" }, di, "di"), create("waypoint", { x: "910", y: "680" }, di, "di")); plane.append(edge);
    // The other process is retained independently; a compact native header keeps its scope distinct.
    definitions.insertBefore(create("process", { id: "Other", name: "Other process", isExecutable: "false" }), firstDiagram ?? null);
    const otherDiagram = create("BPMNDiagram", { id: "OtherDiagram" }, diagramNS, "bpmndi"); otherDiagram.append(create("BPMNPlane", { id: "OtherPlane", bpmnElement: "Other" }, diagramNS, "bpmndi")); definitions.append(otherDiagram);
    const eventDiagram = create("BPMNDiagram", { id: "EventDiagram" }, diagramNS, "bpmndi");
    const eventPlane = create("BPMNPlane", { id: "EventPlane", bpmnElement: "EventBody" }, diagramNS, "bpmndi"), eventShape = create("BPMNShape", { id: "EventStartLocalDI", bpmnElement: "EventStart" }, diagramNS, "bpmndi");
    eventShape.append(create("Bounds", { x: "80.25", y: "100.5", width: "36", height: "36" }, dc, "dc")); eventPlane.append(eventShape); eventDiagram.append(eventPlane); definitions.append(eventDiagram);
    for (const element of [...doc.getElementsByTagNameNS(model, "*")]) element.removeAttributeNS(vendor, "taskBinding");
    return new XMLSerializer().serializeToString(doc);
  }, { xml: exported.xml, model, vendor });
  const root = structuredClone(baseline.state.rootActivity), processId = "process";
  const scopeHeader = (scopeElementId: string, nodeId: string) => ({ scopeElementId, nodeId, activityVersionId: root.activityVersionId,
    inputs: [{ referenceKey: "CanStartWorkflow", value: { expressionType: "Literal", value: false } }], outputs: [], variables: [{ referenceKey: `${nodeId}-local`, name: `${nodeId}-local`, type: { alias: "String", collectionKind: "single" } }] });
  root.structure = { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: { documentXml, processId,
    activities: structuredClone(baseline.state.rootActivity.structure!.payload.activities as ActivityNode[]),
    bindings: [{ elementId: "gateway", childNodeId: "decision" }, { elementId: "high-task", childNodeId: "nested-high" }, { elementId: "low-task", childNodeId: "nested-low" }],
    variables: [], scopeHeaders: [scopeHeader("EventBody", "event-native-scope"), scopeHeader("Other", "other-native-scope")] } };
  return root;
}

for (const firstEditor of ["integrated", "bpmn-js"] as const) {
  test(`advanced-source from ${firstEditor}: full drafts survive both editors and block execution`, async ({ page }, info) => {
    const errors: string[] = []; let consoleErrorCount = 0; page.on("pageerror", error => errors.push(error.message)); page.on("console", message => { if (message.type() === "error") consoleErrorCount++; });
    const api = await login(page, info), { submitted, baseline } = await seedBpmnPrototype(api, `advanced-${firstEditor}-${info.project.name}-${Date.now()}`), draftPath = `/design/workflows/drafts/${submitted.draftId}`;
    const root = await advancedSource(page, api, baseline), initial = await api<Draft>(draftPath, "PUT", { state: { ...baseline.state, rootActivity: root }, layout: baseline.layout.filter(row => ["high", "capture-high", "low-inline", "capture-low"].includes(row.nodeId)) });
    const receipt: Record<string, unknown> = { source: process.env.UX_SOURCE_SHA, firstEditor, kind: "advanced", definitionId: submitted.definition.id, draftId: submitted.draftId, state: "incomplete" };
    try {
      const initialOpaque = await page.evaluate(source => new XMLSerializer().serializeToString(new DOMParser().parseFromString(source, "application/xml").getElementsByTagNameNS("urn:retained-vendor", "keep")[0]), payload(initial).documentXml);
      await attachment(info, "advanced-initial-draft.json", initial);
      await page.goto(`/workflows/definitions?backend=legacy&definition=${submitted.definition.id}`); await choose(page, firstEditor); await disableAutosave(page);
      await label(page, firstEditor, "Script", "Advanced script from first editor");
      await capture(page, info, "advanced-first-editor");
      const other = firstEditor === "integrated" ? "bpmn-js" : "integrated"; await choose(page, other);
      await label(page, other, "Script", "Advanced script from both editors");
      await select(page, other, "TimerBoundary"); await capture(page, info, "advanced-boundary-loop-collaboration");
      await choose(page, "integrated");
      await page.getByRole("combobox", { name: "BPMN scope", exact: true }).selectOption({ label: "Other process" });
      await page.getByRole("region", { name: "Elsa activity configuration", exact: true }).getByRole("tab", { name: "Variables", exact: true }).click();
      await expect(page.getByRole("region", { name: "Elsa activity configuration", exact: true }).getByRole("textbox", { name: "Variable name", exact: true })).toHaveValue("other-native-scope-local");
      await capture(page, info, "advanced-independent-process-context");
      await page.getByRole("combobox", { name: "BPMN scope", exact: true }).selectOption({ label: "Event body" });
      await expect(page.getByRole("region", { name: "Elsa activity configuration", exact: true }).getByRole("textbox", { name: "Variable name", exact: true })).toHaveValue("event-native-scope-local");
      await capture(page, info, "advanced-event-subprocess-context");
      await page.getByRole("button", { name: "Save", exact: true }).click();
      await expect.poll(async () => savedName(page, payload(await api<Draft>(draftPath)).documentXml, "Script")).toBe("Advanced script from both editors");
      const saved = await api<Draft>(draftPath); assertNative(saved, payload(initial).activities);
      expect(payload(saved).scopeHeaders).toEqual(payload(initial).scopeHeaders);
      const facts = await page.evaluate(({ source, model }) => {
        const doc = new DOMParser().parseFromString(source, "application/xml"), byId = (id: string) => doc.querySelector(`[id='${id}']`)!;
        const opaque = doc.getElementsByTagNameNS("urn:retained-vendor", "keep")[0];
        return { opaqueXml: new XMLSerializer().serializeToString(opaque), opaqueRef: opaque.getAttribute("ref"), opaqueLiteral: opaque.getAttribute("literal"),
          opaqueChildren: opaque.getElementsByTagNameNS("urn:retained-vendor", "child").length, opaqueCdata: [...opaque.childNodes].some(node => node.nodeType === Node.CDATA_SECTION_NODE && node.nodeValue === "raw < text & native-high"),
          script: byId("Script").getElementsByTagNameNS(model, "script")[0].textContent, sequential: byId("Script").getElementsByTagNameNS(model, "multiInstanceLoopCharacteristics")[0].getAttribute("isSequential"),
          timer: byId("TimerBoundary").getElementsByTagNameNS(model, "timeDuration")[0].textContent, interrupting: byId("TimerBoundary").getAttribute("cancelActivity"),
          advanced: byId("Complex").localName, message: byId("MessageFlow").getAttribute("targetRef"), data: byId("DataRef").getAttribute("dataObjectRef"),
          opaque: doc.getElementsByTagNameNS("urn:retained-vendor", "keep")[0].textContent, opaqueQName: doc.getElementsByTagNameNS("urn:retained-vendor", "keep")[0].lookupNamespaceURI("q") };
      }, { source: payload(saved).documentXml, model });
      expect(facts).toEqual({ opaqueXml: initialOpaque, opaqueRef: "q:Uninterpreted", opaqueLiteral: "native-high", opaqueChildren: 1, opaqueCdata: true, script: 'return "kept < script & value";', sequential: "true", timer: "PT5M", interrupting: "false", advanced: "complexGateway", message: "Black", data: "Data", opaque: "before  after raw < text & native-high", opaqueQName: "urn:retained-qname" });
      expect(await exportXml(page, info, "advanced-process.bpmn")).toBe(payload(saved).documentXml);
      await attachment(info, "saved-draft.json", saved); await attachment(info, "expected-native-activities.json", payload(initial).activities); await attachment(info, "advanced-facts.json", facts); await noRefusals(page);
      await page.reload(); await choose(page, "bpmn-js"); expect(await api<Draft>(draftPath)).toEqual(saved); await capture(page, info, "advanced-after-save-reload"); await noRefusals(page);
      const promotion = await page.request.post(`${process.env.ELSA_WORKBENCH_URL}${draftPath}/promote`, { data: {} });
      receipt.promotionStatus = promotion.status();
      let rejection = promotion;
      if (promotion.ok()) { const version = await promotion.json() as { id: string }; rejection = await page.request.post(`${process.env.ELSA_WORKBENCH_URL}/publishing/workflows/${version.id}/publish`, { data: {} }); receipt.publicationStatus = rejection.status(); }
      expect([400, 409, 422]).toContain(rejection.status());
      const body = await rejection.text(); expect(body).toMatch(/bpmn|unsupported|validation|qualified/i); receipt.executionRejection = body;
      expect(await api<Draft>(draftPath)).toEqual(saved); expect(errors).toEqual([]); receipt.state = "passed-full-source-advanced";
    } finally { receipt.unexpectedCanvasFailures = await readCanvasFailures(page); receipt.browserErrors = errors; receipt.consoleErrorCount = consoleErrorCount; await attachment(info, "document-receipt.json", receipt); await api(`/design/workflows/definitions/${submitted.definition.id}`, "DELETE"); }
  });
}
