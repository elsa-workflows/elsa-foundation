import { expect, type Page, type TestInfo } from "@playwright/test";
import { readFileSync } from "node:fs";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
type Presentation = { nodeId: string; displayName?: string }[];
type WorkflowDraft = { id: string; state: { rootActivity: Node }; layout: unknown[]; activityPresentation: Presentation };
type GraphDraft = { draftId: string; revision: number; provider: { payload: { rootActivity: Node } }; layout: unknown[]; activityPresentation: Presentation };

/** Normal authoring/interchange/publication APIs and composed editors; no intercepted responses or direct model execution. */
export async function bpmnNamedBlocksJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = process.env.ELSA_WORKBENCH_URL!;
  const workflows: string[] = [], activities: string[] = [], runs: string[] = [], proof: unknown[] = [], failures: unknown[] = [];
  const multipleKey: "Meta" | "Control" = await page.evaluate(() => /Mac/.test(navigator.platform) ? "Meta" : "Control");
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${path}`, { method, data });
    const text = await response.text(); expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(key: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const matches = result.items.filter(item => item.definition.activityTypeKey === key); expect(matches).toHaveLength(1); return matches[0].definition.headVersionId;
  }
  const processVersion = await version("Elsa.Activities.Bpmn.Activities.BpmnProcess");
  const lineVersion = await version("Elsa.Activities.Primitives.Activities.WriteLine");
  const sequenceVersion = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const line = (id: string): Node => ({ nodeId: id, activityVersionId: lineVersion, inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: id } }], outputs: [] });
  const root = (): Node => ({ nodeId: "root", activityVersionId: processVersion, inputs: [], outputs: [], structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: {
    activities: [line("one"), line("two"), line("after")], variables: [], pools: [], lanes: [{ laneId: "shipping-lane", poolId: null, name: "Fulfilment" }],
    elements: [{ elementId: "start", elementType: "startEvent" }, ...["one", "two", "after"].map(id => ({ elementId: `element-${id}`, elementType: "task", childNodeId: id, laneId: "shipping-lane" })), { elementId: "end", elementType: "endEvent" }],
    sequenceFlows: ["start", "element-one", "element-two", "element-after"].map((sourceRef, index) => ({ flowId: `flow-${index}`, sourceRef, targetRef: ["element-one", "element-two", "element-after", "end"][index] })),
    diagram: { shapes: Object.fromEntries(["start", "element-one", "element-two", "element-after", "end"].map((id, index) => [id, { x: 80.25 + index * 180, y: 100.5, width: id === "start" || id === "end" ? 36 : 100, height: id === "start" || id === "end" ? 36 : 80 }])), edges: {} }
  } } });
  async function createWorkflow(name: string, node: Node) {
    const created = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", { name, initialState: { rootActivity: node, variables: [], inputs: [], outputs: [] }, layout: [], activityPresentation: [] });
    workflows.push(created.definition.id); return { definitionId: created.definition.id, draftId: created.draft.id };
  }
  async function executeWorkflow(draftId: string) {
    const version = await api<{ id: string }>(`/design/workflows/drafts/${draftId}/promote`, "POST", { operationKey: crypto.randomUUID() });
    const publication = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${version.id}/publish`, "POST", {});
    const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${publication.artifactId}/execute`, "POST", { sourceReferenceId: publication.sourceReferenceId }); runs.push(started.workflowExecutionId);
    let view: { instance: { status: string }; incidents: unknown[]; activities: { authoredActivityId: string; status: string }[] };
    await expect.poll(async () => { view = await api(`/runtime/workflows/instances/${started.workflowExecutionId}`); return view.instance.status; }).toBe("Completed");
    expect(view!.incidents).toEqual([]);
    const executions = await api<{ items: { execution: { activityExecutionId: string; executableNodeId: string; authoredActivityId: string; status: string } }[] }>(`/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions`);
    const observed = [];
    for (const { execution } of executions.items) {
      let fixtureId = execution.authoredActivityId;
      if (fixtureId === "root" && execution.executableNodeId !== "root") {
        const layout = await api<{ nodes: { executableNodeId: string }[]; boundaryOrigin: { kind: string; id: string }[] }>(`/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions/${execution.activityExecutionId}/layout`);
        expect(layout.nodes.some(item => item.executableNodeId === execution.executableNodeId)).toBe(true);
        const placement = [...layout.boundaryOrigin].reverse().find(item => item.kind === "NestedPlacement"); expect(placement).toBeTruthy(); fixtureId = placement!.id;
      }
      observed.push({ ...execution, fixtureId });
    }
    const values = [];
    for (const id of ["one", "two", "after"]) {
      const matches = observed.filter(item => item.fixtureId === id); expect(matches).toHaveLength(1); expect(matches[0].status).toBe("Completed");
      const path = `/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions/${matches[0].activityExecutionId}`;
      const detail = await api<{ valueSnapshots: { inputKey: string; evidenceId: string }[] }>(path);
      const snapshot = detail.valueSnapshots.find(item => item.inputKey === "text"); expect(snapshot).toBeTruthy();
      const evidence = await api<{ payload: { preview: string } }>(`${path}/value-evidence/${snapshot!.evidenceId}/payload`);
      expect(evidence.payload.preview).toBe(id); values.push({ id, evidence: snapshot!.evidenceId, value: evidence.payload.preview });
    }
    return { version: version.id, run: started.workflowExecutionId, view: view!, observed, values };
  }
  async function publishActivity(draft: GraphDraft, head: string | null) {
    const preflight = await api<{ isPublishable: boolean; minimumVersion: string; reviewToken: string; definitionHeadVersionId: string | null; diagnostics: unknown[] }>(`/design/activities/drafts/${draft.draftId}/publication-preflight`, "POST", { expectedDraftRevision: draft.revision, expectedDefinitionHeadVersionId: head });
    expect(preflight.isPublishable, JSON.stringify(preflight.diagnostics)).toBe(true);
    const published = await api<{ outcome: { definitionVersionId: string } }>(`/design/activities/drafts/${draft.draftId}/publish`, "POST", { expectedDraftRevision: draft.revision, expectedDefinitionHeadVersionId: preflight.definitionHeadVersionId, version: preflight.minimumVersion, reviewToken: preflight.reviewToken, idempotencyKey: crypto.randomUUID() });
    return published.outcome.definitionVersionId;
  }
  async function executeActivity(versionId: string) {
    const caller = await createWorkflow(`BPMN block caller ${info.project.name}`, { nodeId: "caller", activityVersionId: sequenceVersion, inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [{ nodeId: "call", activityVersionId: versionId, inputs: [], outputs: [] }] } } });
    return executeWorkflow(caller.draftId);
  }
  async function canvas() {
    if ((page.viewportSize()?.width ?? 1000) <= 600) for (const label of ["Collapse inspector panel", "Collapse activities panel"]) {
      const control = page.getByRole("button", { name: label, exact: true }); if (await control.isVisible()) await control.click();
    }
  }
  const element = (id: string) => page.locator(`.react-flow__node[data-id="${id}"]`);
  try {
    for (const host of ["workflow", "activity"] as const) {
      let definitionId: string, draftId: string, baselineVersion: string | null = null;
      const node = root();
      let baseline: unknown;
      if (host === "workflow") {
        ({ definitionId, draftId } = await createWorkflow(`BPMN named blocks ${info.project.name}`, node)); baseline = await executeWorkflow(draftId);
      } else {
        const contract = await api<{ contract: { outcomes: { referenceKey: string; name: string; isEmitted: boolean }[] } }>(`/design/activities/versions/${processVersion}`);
        const done = contract.contract.outcomes.find(item => item.isEmitted && (item.name || item.referenceKey).toLowerCase() === "done"); expect(done).toBeTruthy();
        const created = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", { category: "Qualification", displayName: `BPMN named blocks ${info.project.name}`, contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] }, provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: node, variables: [], outputMappings: [], outcomeMappings: [{ sourceOutcomeReferenceKey: done!.referenceKey, boundaryOutcomeReferenceKey: "done" }] } }, layout: [], activityPresentation: [] });
        definitionId = created.definition.definitionId; activities.push(definitionId);
        baselineVersion = await publishActivity(await api(`/design/activities/drafts/${created.draft.draftId}`), null); baseline = await executeActivity(baselineVersion);
        draftId = (await api<GraphDraft>(`/design/activities/definitions/${definitionId}/drafts`, "POST", { sourceVersionId: baselineVersion })).draftId;
      }
      const saved = async () => host === "workflow" ? api<WorkflowDraft>(`/design/workflows/drafts/${draftId}`) : api<GraphDraft>(`/design/activities/drafts/${draftId}`);
      const body = (draft: WorkflowDraft | GraphDraft) => "state" in draft ? draft.state.rootActivity : draft.provider.payload.rootActivity;
      await page.goto(host === "workflow" ? `/workflows/definitions?backend=legacy&definition=${definitionId}` : `/workflows/activity-definitions?backend=legacy&definition=${definitionId}&section=editor&draft=${draftId}`); await canvas();
      if (host === "workflow") { await page.getByRole("button", { name: "Save", exact: true }).click(); await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved"); }
      await expect(element("element-one")).toBeVisible(); await element("element-one").click(); await element("element-two").click({ modifiers: [multipleKey] });
      await expect(page.locator('.react-flow__node[aria-pressed="true"]')).toHaveCount(2); await capture(page, info, `${host}-bpmn-selection`);
      await page.getByRole("button", { name: "Extract block…", exact: true }).click();
      const extract = page.getByRole("dialog", { name: "Extract named block", exact: true });
      const blockName = `Prepare shipment ${host}`;
      await extract.getByRole("textbox", { name: "Name", exact: true }).fill(blockName); await extract.getByRole("button", { name: "Preview change", exact: true }).click();
      await expect(extract.getByRole("button", { name: "Apply change", exact: true })).toBeEnabled(); await capture(page, info, `${host}-bpmn-preview`);
      await extract.getByRole("button", { name: "Apply change", exact: true }).click(); await expect(extract).toBeHidden();
      let persisted: WorkflowDraft | GraphDraft;
      await expect.poll(async () => { persisted = await saved(); return persisted.activityPresentation.some(item => item.displayName === blockName); }).toBe(true);
      const blockId = persisted!.activityPresentation.find(item => item.displayName === blockName)!.nodeId;
      const payload = body(persisted!).structure!.payload;
      const blockElement = (payload.elements as { elementId: string; childNodeId?: string }[]).find(item => item.childNodeId === blockId)!;
      const block = (payload.activities as Node[]).find(item => item.nodeId === blockId)!;
      expect(block.structure?.kind).toBe("elsa.bpmn.structure"); expect((block.structure!.payload.activities as Node[]).map(item => item.nodeId)).toEqual(["one", "two"]);
      await page.getByRole("button", { name: `Open ${blockName} (2 steps)`, exact: true }).click(); await expect(element("element-one")).toBeVisible(); await expect(page.locator(".wf-breadcrumb")).toContainText(blockName); await capture(page, info, `${host}-bpmn-nested`);
      const undo = page.getByRole("button", { name: host === "workflow" ? "Undo" : "Undo Activity Graph edit", exact: true });
      const redo = page.getByRole("button", { name: host === "workflow" ? "Redo" : "Redo Activity Graph edit", exact: true });
      await undo.click(); await expect(element("element-one")).toBeVisible(); await expect(element(blockElement.elementId)).toHaveCount(0);
      await redo.click(); await expect(page.locator(".wf-breadcrumb")).toContainText(blockName);
      await page.locator(".wf-breadcrumb").getByRole("button", { name: "Root", exact: true }).click(); await canvas(); await page.reload(); await canvas();
      await expect(element(blockElement.elementId)).toBeVisible(); await capture(page, info, `${host}-bpmn-reopened`);
      const beforeImport = await saved();
      const downloaded = page.waitForEvent("download"); await page.getByRole("button", { name: "Export BPMN body", exact: true }).click();
      const download = await downloaded; const path = (await download.path())!; const xml = readFileSync(path, "utf8");
      expect(xml).toContain(blockName); expect(xml).toContain("taskBinding"); expect(xml).toContain(lineVersion); expect(xml).toContain("element-one");
      await page.getByLabel("Import native BPMN body file", { exact: true }).setInputFiles(path);
      const review = page.getByRole("dialog", { name: "Review BPMN body import", exact: true }); await expect(review).toContainText("Bindings retained in this file");
      const choices = review.getByRole("checkbox"); await expect(choices).toHaveCount(4);
      for (const choice of await choices.all()) { await expect(choice).not.toBeChecked(); await choice.check(); }
      await review.getByRole("button", { name: "Preview imported body", exact: true }).click(); await expect(review.getByRole("button", { name: "Apply imported body", exact: true })).toBeEnabled(); await capture(page, info, `${host}-bpmn-import-review`);
      await review.getByRole("button", { name: "Cancel", exact: true }).click(); expect(await saved()).toEqual(beforeImport);
      await page.getByLabel("Import native BPMN body file", { exact: true }).setInputFiles(path);
      await expect(review.getByRole("checkbox")).toHaveCount(4); for (const choice of await review.getByRole("checkbox").all()) await choice.check();
      await review.getByRole("button", { name: "Preview imported body", exact: true }).click(); await expect(review.getByRole("button", { name: "Apply imported body", exact: true })).toBeEnabled();
      await review.getByRole("button", { name: "Apply imported body", exact: true }).click(); await expect(review).toBeHidden();
      await expect.poll(async () => { const current = await saved(); return current.activityPresentation.some(item => item.nodeId === blockId && item.displayName === blockName); }).toBe(true);
      const imported = await saved(); expect(body(imported).nodeId).toBe("root"); expect(body(imported).activityVersionId).toBe(processVersion);
      const importedBlock = (body(imported).structure!.payload.activities as Node[]).find(item => item.nodeId === blockId)!;
      expect(importedBlock.activityVersionId).toBe(processVersion); expect((importedBlock.structure!.payload.activities as Node[]).map(item => [item.nodeId, item.activityVersionId, item.inputs])).toEqual((block.structure!.payload.activities as Node[]).map(item => [item.nodeId, item.activityVersionId, item.inputs]));
      await capture(page, info, `${host}-bpmn-imported`); await undo.click();
      await expect.poll(async () => body(await saved())).toEqual(body(beforeImport)); await redo.click();
      await element(blockElement.elementId).click(); await page.getByRole("button", { name: "Ungroup…", exact: true }).click();
      const ungroup = page.getByRole("dialog", { name: "Ungroup block", exact: true }); await ungroup.getByRole("button", { name: "Preview change", exact: true }).click();
      await expect(ungroup.getByRole("button", { name: "Apply change", exact: true })).toBeEnabled(); await ungroup.getByRole("button", { name: "Apply change", exact: true }).click(); await expect(ungroup).toBeHidden();
      await expect(element("element-one")).toBeVisible(); await expect(element(blockElement.elementId)).toHaveCount(0); await capture(page, info, `${host}-bpmn-ungrouped`); await undo.click(); await expect(element(blockElement.elementId)).toBeVisible();
      let after: unknown;
      if (host === "workflow") after = await executeWorkflow(draftId);
      else after = await executeActivity(await publishActivity(await api(`/design/activities/drafts/${draftId}`), baselineVersion));
      proof.push({ host, baseline, after, beforeImport, imported, xml, blockId, blockElementId: blockElement.elementId });
    }
  } catch (error) { failures.push(error); }
  finally {
    const cleanup: unknown[] = [];
    for (const [kind, ids, prefix] of [["run", runs, "/runtime/workflows/instances"], ["workflow", workflows, "/design/workflows/definitions"]] as const) for (const id of ids) {
      try { await api(`${prefix}/${id}`, "DELETE"); cleanup.push({ kind, id, deleted: true }); } catch (error) { failures.push(error); cleanup.push({ kind, id, deleted: false }); }
    }
    await info.attach("bpmn-named-blocks-proof.json", { body: JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA, proof, cleanup, disposableActivityDefinitionIds: activities }, null, 2), contentType: "application/json" });
  }
  if (failures.length === 1) throw failures[0];
  if (failures.length) throw new AggregateError(failures, "BPMN named-block journey or owned cleanup failed");
}
