import { expect, test, type Page } from "@playwright/test";
import { attachment, bpmnPrototypeGraph, capture, choose, label, login, native, nativeShape, noRefusals, properties, readCanvasFailures, savedName, select,
  type Api, type Editor, type Node as ActivityNode } from "./bpmnDocumentAcceptanceFixtures";

type Source = { documentXml: string; processId: string; activities: ActivityNode[]; bindings: { elementId: string; childNodeId?: string; listenerNodeId?: string }[]; scopeHeaders?: unknown[]; [key: string]: unknown };
type GraphDraft = { draftId: string; definitionId: string; revision: number; status: string; contract: Record<string, unknown>;
  provider: { providerKey: string; schemaVersion: string; manifestFingerprint: string; payload: { rootActivity: ActivityNode; variables: unknown[]; outputMappings: unknown[]; [key: string]: unknown } };
  layout: { nodeId: string; data: Record<string, unknown> }[]; activityPresentation?: unknown[]; [key: string]: unknown };
const model = "http://www.omg.org/spec/BPMN/20100524/MODEL", diagram = "http://www.omg.org/spec/BPMN/20100524/DI", bounds = "http://www.omg.org/spec/DD/20100524/DC";
const owner = (draft: GraphDraft) => draft.provider.payload.rootActivity;
const source = (draft: GraphDraft) => owner(draft).structure!.payload as Source;
const editorPath = (draft: GraphDraft) => `/workflows/activity-definitions?backend=legacy&definition=${draft.definitionId}&section=editor&draft=${draft.draftId}`;
const draftPath = (draft: GraphDraft) => `/design/activities/drafts/${draft.draftId}`;
const expression = (node: ActivityNode) => (node.inputs.find(value => (value as { referenceKey: string }).referenceKey === "Expression") as { value: { value: string } }).value.value;

async function seedGraph(api: Api, suffix: string): Promise<GraphDraft> {
  const capabilities = await api<{ contractSchemaVersions: string[]; providers: { providerKey: string; manifestSchemas: { schemaVersion: string; isAuthorable: boolean }[] }[] }>("/design/activities/authoring-capabilities");
  expect(capabilities.contractSchemaVersions).toContain("1");
  expect(capabilities.providers.find(provider => provider.providerKey === "elsa.activity-graph")?.manifestSchemas).toContainEqual(expect.objectContaining({ schemaVersion: "1", isAuthorable: true }));
  const { root, layout } = await bpmnPrototypeGraph(api), payload = root.structure!.payload;
  const activities = payload.activities as ActivityNode[];
  activities.find(node => node.nodeId === "decision")!.inputs = [{ referenceKey: "Outcome", value: { expressionType: "Literal", value: "Low" } }];
  // This journey tests reusable Activity Definition authoring. It does not fabricate a Workflow
  // output boundary for the prototype's SetOutput steps or claim a reusable runtime execution.
  for (const id of ["nested-high", "nested-low"]) {
    const body = activities.find(node => node.nodeId === id)!;
    body.structure!.payload.activities = (body.structure!.payload.activities as ActivityNode[]).filter(node => !node.nodeId.startsWith("capture-"));
  }
  const created = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
    category: "BPMN owned acceptance", displayName: `BPMN Activity Graph ${suffix}`, description: "Disposable composed source2 authoring witness",
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", payload: { rootActivity: root, variables: [], outputMappings: [] } },
    contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true, description: null }] },
    layout: layout.filter(row => !row.nodeId.startsWith("capture-")).map(({ nodeId, ...data }) => ({ nodeId, data }))
  });
  const draft = await api<GraphDraft>(`/design/activities/drafts/${created.draft.draftId}`);
  expect(draft.definitionId).toBe(created.definition.definitionId); expect(draft.provider.providerKey).toBe("elsa.activity-graph"); expect(draft.provider.schemaVersion).toBe("1");
  expect(owner(draft).structure!.schemaVersion).toBe("1.0.0"); return draft;
}

async function save(page: Page) {
  const button = page.getByRole("button", { name: "Save now", exact: true });
  if (await button.isEnabled()) await button.click();
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
}
async function saveName(page: Page, api: Api, draft: GraphDraft, id: string, name: string) {
  await save(page);
  await expect.poll(async () => {
    const saved = await api<GraphDraft>(draftPath(draft));
    return owner(saved).structure!.schemaVersion === "2.0.0" ? savedName(page, source(saved).documentXml, id) : "legacy";
  }).toBe(name);
}
function assertIdentity(saved: GraphDraft, baseline: GraphDraft) {
  expect(saved.draftId).toBe(baseline.draftId); expect(saved.definitionId).toBe(baseline.definitionId);
  expect(saved.provider.providerKey).toBe(baseline.provider.providerKey); expect(saved.provider.schemaVersion).toBe(baseline.provider.schemaVersion);
  expect(owner(saved).nodeId).toBe(owner(baseline).nodeId); expect(owner(saved).activityVersionId).toBe(owner(baseline).activityVersionId);
  expect(saved.contract).toEqual(baseline.contract); expect(saved.provider.payload.variables).toEqual(baseline.provider.payload.variables);
  expect(saved.provider.payload.outputMappings).toEqual(baseline.provider.payload.outputMappings);
  expect(saved.provider.manifestFingerprint).toBeTruthy();
}
async function exportSource(api: Api, draft: GraphDraft) {
  const exported = await api<{ xml: string }>("/interchange/bpmn/export", "POST", { processNode: owner(draft) });
  expect(exported.xml).toBe(source(draft).documentXml); return exported.xml;
}
async function advanced(page: Page, draft: GraphDraft): Promise<ActivityNode> {
  const result = structuredClone(owner(draft)), current = source(draft);
  const documentXml = await page.evaluate(({ xml, model, diagram, bounds }) => {
    const doc = new DOMParser().parseFromString(xml, "application/xml"), process = doc.getElementsByTagNameNS(model, "process")[0];
    const plane = doc.getElementsByTagNameNS(diagram, "BPMNPlane")[0];
    doc.documentElement.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:v", "urn:graph-retained");
    doc.documentElement.setAttributeNS("http://www.w3.org/2000/xmlns/", "xmlns:q", "urn:graph-qname");
    process.setAttribute("name", "Graph process");
    const extension = doc.createElementNS(model, "bpmn:extensionElements"), opaque = doc.createElementNS("urn:graph-retained", "v:keep");
    opaque.setAttributeNS("urn:graph-retained", "v:flag", "qualified"); opaque.setAttribute("flag", "unqualified"); opaque.setAttribute("ref", "q:Thing");
    opaque.append("before ", doc.createElementNS("urn:graph-retained", "v:child"), " after ", doc.createCDATASection("raw < scope & retained")); extension.append(opaque); process.prepend(extension);
    const complex = doc.createElementNS(model, "bpmn:complexGateway"); complex.setAttribute("id", "GraphComplex"); complex.setAttribute("name", "Retained graph complex"); process.append(complex);
    const body = doc.createElementNS(model, "bpmn:subProcess"); body.setAttribute("id", "GraphBody"); body.setAttribute("name", "Graph standard body"); process.append(body);
    for (const [id, x, y, width, height, expanded] of [["GraphComplex", 860, 260, 50, 50, false], ["GraphBody", 820, 420, 260, 150, true]] as const) {
      const shape = doc.createElementNS(diagram, "bpmndi:BPMNShape"); shape.setAttribute("id", `${id}DI`); shape.setAttribute("bpmnElement", id);
      if (expanded) shape.setAttribute("isExpanded", "true");
      const box = doc.createElementNS(bounds, "dc:Bounds"); for (const [key, value] of Object.entries({ x, y, width, height })) box.setAttribute(key, String(value)); shape.append(box); plane.append(shape);
    }
    return new XMLSerializer().serializeToString(doc);
  }, { xml: current.documentXml, model, diagram, bounds });
  result.structure = { kind: "elsa.bpmn.structure", schemaVersion: "2.0.0", payload: { ...current, documentXml,
    scopeHeaders: [...current.scopeHeaders ?? [], { scopeElementId: "GraphBody", nodeId: "graph-body-header", activityVersionId: result.activityVersionId, inputs: [], outputs: [],
      variables: [{ referenceKey: "scope-local", name: "scope-local", type: { alias: "String", collectionKind: "single" } }] }] } };
  return result;
}
async function facts(page: Page, xml: string) {
  return page.evaluate(({ xml, model }) => {
    const doc = new DOMParser().parseFromString(xml, "application/xml"), opaque = doc.getElementsByTagNameNS("urn:graph-retained", "keep")[0];
    return { opaque: new XMLSerializer().serializeToString(opaque), qualified: opaque.getAttributeNS("urn:graph-retained", "flag"), unqualified: opaque.getAttribute("flag"),
      qname: opaque.lookupNamespaceURI("q"), mixed: opaque.textContent, childCount: opaque.getElementsByTagNameNS("urn:graph-retained", "child").length,
      cdata: [...opaque.childNodes].some(node => node.nodeType === 4 && node.nodeValue === "raw < scope & retained"), complex: doc.getElementsByTagNameNS(model, "complexGateway")[0]?.id };
  }, { xml, model });
}

for (const firstEditor of ["integrated", "bpmn-js"] as const) {
  test(`activity-graph from ${firstEditor}: migration, shared Slot, history, source retention and publication guard`, async ({ page }, info) => {
    const browserErrors: string[] = []; let consoleErrorCount = 0;
    page.on("pageerror", error => browserErrors.push(error.message)); page.on("console", message => { if (message.type() === "error") consoleErrorCount++; });
    const api = await login(page, info), baseline = await seedGraph(api, `${firstEditor}-${info.project.name}-${Date.now()}`), path = draftPath(baseline);
    const receipt: Record<string, unknown> = { source: process.env.UX_SOURCE_SHA, kind: "activity-graph", firstEditor, definitionId: baseline.definitionId, draftId: baseline.draftId, baselineRevision: baseline.revision, state: "incomplete" };
    const writes: { expectedRevision: number; status: number; revision?: number; error?: string }[] = [];
    const responses: Promise<void>[] = [];
    page.on("response", response => {
      if (response.request().method() !== "PUT" || !new URL(response.url()).pathname.endsWith(path)) return;
      const row = { expectedRevision: (response.request().postDataJSON() as { expectedRevision: number }).expectedRevision, status: response.status() } as typeof writes[number];
      writes.push(row); responses.push(response.json().then((value: GraphDraft) => { row.revision = value.revision; }).catch(error => { row.error = String(error); }));
    });
    try {
      await attachment(info, "graph-opened-draft.json", baseline);
      await page.goto(editorPath(baseline)); await choose(page, firstEditor);
      const other: Editor = firstEditor === "integrated" ? "bpmn-js" : "integrated";
      await choose(page, other); await choose(page, firstEditor);
      await expect(page.getByRole("button", { name: "Save now", exact: true })).toBeDisabled();
      const noOp = await api<GraphDraft>(path); expect(noOp).toEqual(baseline);
      expect(owner(noOp).structure!.schemaVersion).toBe("1.0.0"); receipt.noOpRevision = noOp.revision;
      await attachment(info, "graph-noop-draft.json", noOp); await capture(page, info, "graph-opened-without-migration");
      await label(page, firstEditor, "gateway", "First graph source edit"); await saveName(page, api, baseline, "gateway", "First graph source edit");
      const firstEdit = await api<GraphDraft>(path); assertIdentity(firstEdit, baseline); expect(firstEdit.revision).toBeGreaterThan(baseline.revision);
      expect(source(firstEdit).activities).toEqual(owner(baseline).structure!.payload.activities);
      await attachment(info, "graph-first-edit-draft.json", firstEdit);
      await choose(page, other); await label(page, other, "gateway", "Graph complete source", false);
      await page.getByRole("button", { name: "Save now", exact: true }).click(); await saveName(page, api, baseline, "gateway", "Graph complete source");
      await page.getByRole("button", { name: "Undo", exact: true }).click(); await select(page, other, "gateway");
      await properties(page).getByRole("button", { name: "Edit label", exact: true }).click();
      await expect(properties(page).getByLabel("Label", { exact: true })).toHaveValue("First graph source edit");
      await properties(page).getByRole("button", { name: "Discard BPMN property", exact: true }).click();
      await page.getByRole("button", { name: "Redo", exact: true }).click();
      await choose(page, "bpmn-js"); await select(page, "bpmn-js", "high-task");
      await page.getByRole("region", { name: "Elsa activity configuration", exact: true }).getByRole("tab", { name: "Slots", exact: true }).click();
      await page.locator(".wf-slot-list .wf-slot-row > button").first().click();
      await page.locator(".wf-sequence-step[data-id='high']").click();
      await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
      await page.locator(".cm-content[contenteditable='true']").fill('"Graph edited high"');
      await page.locator(".wf-sequence-step[data-id='high']").click();
      await capture(page, info, "graph-native-slot-shared-configuration");
      await page.locator(".wf-breadcrumb").getByRole("button", { name: "Root", exact: true }).click(); await choose(page, "bpmn-js");
      const shape = nativeShape(page, "gateway"), box = await shape.boundingBox(); expect(box).not.toBeNull();
      await page.mouse.move(box!.x + box!.width / 2, box!.y + box!.height / 2); await page.mouse.down();
      await page.mouse.move(box!.x + box!.width / 2 + 30, box!.y + box!.height / 2 + 20, { steps: 8 }); await page.mouse.up();
      await expect(native(page)).toHaveAttribute("data-bpmn-js-state", "ready"); await save(page);
      const expectedNative = structuredClone(owner(baseline).structure!.payload.activities as ActivityNode[]), expectedBody = expectedNative.find(node => node.nodeId === "nested-high")!;
      const expectedInline = (expectedBody.structure!.payload.activities as ActivityNode[]).find(node => node.nodeId === "high")!;
      (expectedInline.inputs[0] as { value: { value: string } }).value.value = '"Graph edited high"';
      await expect.poll(async () => source(await api<GraphDraft>(path)).activities).toEqual(expectedNative);
      const saved = await api<GraphDraft>(path); assertIdentity(saved, baseline); expect(owner(saved).structure!.schemaVersion).toBe("2.0.0");
      expect(source(saved).bindings).toEqual(source(firstEdit).bindings); expect(expression((source(saved).activities.find(node => node.nodeId === "nested-high")!.structure!.payload.activities as ActivityNode[])[0])).toBe('"Graph edited high"');
      expect(await savedName(page, source(saved).documentXml, "gateway")).toBe("Graph complete source");
      for (const field of ["elements", "sequenceFlows", "diagram", "pools", "lanes", "messageFlows"]) expect(Object.hasOwn(source(saved), field)).toBe(false);
      for (const id of ["start", "gateway", "high-task", "low-task", "end-high", "end-low"]) expect(saved.layout.some(row => row.nodeId === id)).toBe(false);
      for (const id of ["high", "low-inline"]) expect(saved.layout.find(row => row.nodeId === id)).toEqual(baseline.layout.find(row => row.nodeId === id));
      await attachment(info, "graph-saved-draft.json", saved); await attachment(info, "graph-expected-native.json", expectedNative);
      await info.attach("graph-complete-process.bpmn", { body: await exportSource(api, saved), contentType: "application/xml" });
      await capture(page, info, "graph-both-editors-and-history"); await noRefusals(page);
      await page.reload(); await choose(page, "bpmn-js"); await expect(nativeShape(page, "gateway")).toContainText("Graph complete source");
      const reloaded = await api<GraphDraft>(path); expect(reloaded).toEqual(saved); receipt.savedRevision = saved.revision; receipt.reloadedRevision = reloaded.revision;
      await attachment(info, "graph-reloaded-draft.json", reloaded); await capture(page, info, "graph-after-save-reload");

      // Retain unsupported complete source in this same reusable draft, then exercise both actual
      // canvases and the native standard-scope header before asking the real publication gate.
      const advancedOwner = await advanced(page, saved);
      await Promise.all(responses);
      const staged = await page.request.put(`${process.env.ELSA_WORKBENCH_URL}${path}`, { data: { expectedRevision: saved.revision, contract: saved.contract,
        provider: { providerKey: saved.provider.providerKey, schemaVersion: saved.provider.schemaVersion, payload: { ...saved.provider.payload, rootActivity: advancedOwner } }, layout: saved.layout, activityPresentation: saved.activityPresentation ?? [] } });
      expect(staged.status()).toBe(200); const advancedInitial = await staged.json() as GraphDraft;
      writes.push({ expectedRevision: saved.revision, status: staged.status(), revision: advancedInitial.revision });
      await attachment(info, "graph-advanced-initial-draft.json", advancedInitial);
      const expectedFacts = await facts(page, source(advancedInitial).documentXml);
      await page.reload(); await choose(page, firstEditor); await label(page, firstEditor, "GraphComplex", "First graph advanced label");
      await choose(page, other); await label(page, other, "GraphComplex", "Graph advanced retained"); await saveName(page, api, baseline, "GraphComplex", "Graph advanced retained");
      await choose(page, "integrated");
      await page.getByRole("combobox", { name: "BPMN scope", exact: true }).selectOption({ label: "Graph standard body" });
      await page.getByRole("region", { name: "Elsa activity configuration", exact: true }).getByRole("tab", { name: "Variables", exact: true }).click();
      await expect(page.getByRole("region", { name: "Elsa activity configuration", exact: true }).getByRole("textbox", { name: "Variable name", exact: true })).toHaveValue("scope-local");
      await capture(page, info, "graph-standard-header-variables");
      await page.getByRole("combobox", { name: "BPMN scope", exact: true }).selectOption({ label: "Graph process" });
      await save(page); const advancedSaved = await api<GraphDraft>(path); assertIdentity(advancedSaved, baseline);
      expect(source(advancedSaved).activities).toEqual(expectedNative); expect(source(advancedSaved).bindings).toEqual(source(saved).bindings);
      expect(source(advancedSaved).scopeHeaders).toEqual(source(advancedInitial).scopeHeaders);
      const advancedFacts = await facts(page, source(advancedSaved).documentXml); expect(advancedFacts).toEqual(expectedFacts);
      expect(advancedFacts).toMatchObject({ qualified: "qualified", unqualified: "unqualified", qname: "urn:graph-qname", childCount: 1, cdata: true, complex: "GraphComplex" });
      await attachment(info, "graph-advanced-saved-draft.json", advancedSaved); await attachment(info, "graph-advanced-facts.json", advancedFacts);
      await info.attach("graph-advanced-process.bpmn", { body: await exportSource(api, advancedSaved), contentType: "application/xml" });
      await page.reload(); await choose(page, "bpmn-js"); expect(await api<GraphDraft>(path)).toEqual(advancedSaved);
      await capture(page, info, "graph-advanced-after-reload"); await noRefusals(page);
      const preflight = await api<{ draftId: string; draftRevision: number; isPublishable: boolean; reviewToken: string; minimumVersion: string; diagnostics: unknown[] }>(`${path}/publication-preflight`, "POST", { expectedDraftRevision: advancedSaved.revision, expectedDefinitionHeadVersionId: null });
      expect(preflight.draftId).toBe(baseline.draftId); expect(preflight.draftRevision).toBe(advancedSaved.revision); expect(preflight.isPublishable).toBe(false);
      expect(JSON.stringify(preflight.diagnostics)).toMatch(/complexGateway|GraphComplex/);
      const publication = await page.request.post(`${process.env.ELSA_WORKBENCH_URL}${path}/publish`, { data: {
        expectedDraftRevision: advancedSaved.revision, expectedDefinitionHeadVersionId: null, version: preflight.minimumVersion, reviewToken: preflight.reviewToken,
        idempotencyKey: `graph-source2-${firstEditor}-${info.project.name}-${Date.now()}`
      } });
      const rejection = await publication.text();
      expect([200, 201, 400, 409, 422]).toContain(publication.status());
      if (publication.ok()) expect((JSON.parse(rejection) as { status: string }).status).toMatch(/^(Rejected|Failed)$/);
      expect(rejection).toMatch(/complexGateway|GraphComplex/);
      expect(await api<GraphDraft>(path)).toEqual(advancedSaved);
      const definition = await api<{ lifecycle: { versionCount: number } }>(`/design/activities/definitions/${baseline.definitionId}`); expect(definition.lifecycle.versionCount).toBe(0);
      await attachment(info, "graph-publication-guard.json", { preflight, status: publication.status(), rejection, versionCount: definition.lifecycle.versionCount });
      await Promise.all(responses);
      for (const write of writes) { expect(write.status).toBe(200); expect(write.revision).toBe(write.expectedRevision + 1); }
      for (let i = 1; i < writes.length; i++) expect(writes[i].expectedRevision).toBe(writes[i - 1].revision);
      expect(browserErrors).toEqual([]); receipt.state = "passed-full-source-activity-graph"; receipt.advancedRevision = advancedSaved.revision;
    } finally {
      await Promise.all(responses);
      receipt.browserErrors = browserErrors; receipt.consoleErrorCount = consoleErrorCount; receipt.unexpectedCanvasFailures = await readCanvasFailures(page); receipt.writes = writes;
      await attachment(info, "activity-graph-receipt.json", receipt);
      // The enclosing runner owns/disposes the complete hosts/stores. Keep this exact draft and all
      // failed receipts available for diagnosis rather than discarding the reusable source mid-proof.
    }
  });
}
