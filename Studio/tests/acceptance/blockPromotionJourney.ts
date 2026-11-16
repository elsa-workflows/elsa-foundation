import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } | null };
type WorkflowDraft = { state: { rootActivity: Node }; activityPresentation: { nodeId: string; displayName: string }[] };
type ActivityDraft = { draftId: string; provider: { payload: { rootActivity: Node } }; activityPresentation: { nodeId: string; displayName: string }[] };

/**
 * Promotes a named block to a reusable activity in both resource editors through the real composed hosts:
 * review, ordinary draft creation and publication, exact-version replacement, two independently bound calls
 * and Undo that keeps the shared reusable activity. API calls set up fixtures and read results only.
 */
export async function blockPromotionJourney(page: Page, info: TestInfo, capture: Capture) {
  const backend = process.env.ELSA_WORKBENCH_URL!;
  const stamp = `${info.project.name} ${Date.now()}`;
  const workflowIds: string[] = [], runIds: string[] = [], activityIds: string[] = [];
  const proof: Record<string, unknown> = { sourceSha: process.env.UX_SOURCE_SHA };
  const failures: unknown[] = [];
  async function api<T>(route: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${route}`, { method, data });
    const text = await response.text();
    expect(response.ok(), `${method} ${route}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(key: string) {
    const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const entry = catalog.items.filter(item => item.definition.activityTypeKey === key);
    expect(entry).toHaveLength(1);
    return entry[0].definition.headVersionId;
  }
  const versions = { sequence: await version("Elsa.Activities.Sequence.Activities.Sequence"), line: await version("Elsa.Activities.Primitives.Activities.WriteLine") };
  const line = (nodeId: string, value: unknown): Node => ({ nodeId, activityVersionId: versions.line, inputs: [{ referenceKey: "text", value }], outputs: [] });
  const sequence = (nodeId: string, activities: Node[]): Node => ({ nodeId, activityVersionId: versions.sequence, inputs: [], outputs: [],
    structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities, variables: [] } } });
  const row = (id: string) => page.locator(`.wf-sequence-step[data-id="${id}"], .react-flow__node[data-id="${id}"]`).first();
  async function canvas() {
    if ((page.viewportSize()?.width ?? 1000) <= 600) for (const name of ["Collapse inspector panel", "Collapse activities panel"]) {
      const button = page.getByRole("button", { name, exact: true });
      if (await button.isVisible()) await button.click();
    }
  }

  async function runWorkflow(draftId: string, inputs?: Record<string, string>) {
    const promoted = await api<{ id: string }>(`/design/workflows/drafts/${draftId}/promote`, "POST", { operationKey: crypto.randomUUID() });
    const publication = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
    const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${publication.artifactId}/execute`, "POST", { inputs, sourceReferenceId: publication.sourceReferenceId });
    runIds.push(started.workflowExecutionId);
    let instance: { instance: { status: string }; incidents?: unknown[] } | undefined;
    try {
      await expect.poll(async () => (instance = await api<{ instance: { status: string }; incidents?: unknown[] }>(`/runtime/workflows/instances/${started.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
    } finally { proof[`run-${started.workflowExecutionId}`] = instance; }
    const executions = await api<{ items: { execution: { activityExecutionId: string; executableNodeId: string; authoredActivityId: string; activityType: string; status: string } }[] }>(`/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions`);
    // Placed template nodes carry template-relative authored ids; the layout's boundary origin names the workflow call
    // (its first authored-node segment) and the occurrence path inside the reusable body.
    const inside: { call: string; text: string; origin: { kind: string; id: string }[] }[] = [];
    for (const { execution } of executions.items.filter(item => item.execution.status === "Completed" && item.execution.activityType === "Elsa.Activities.Primitives.Activities.WriteLine"
      && item.execution.executableNodeId !== item.execution.authoredActivityId)) {
      const resource = `/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions/${execution.activityExecutionId}`;
      const layout = await api<{ boundaryOrigin: { kind: string; id: string }[] }>(`${resource}/layout`);
      const call = layout.boundaryOrigin.find(segment => segment.kind === "AuthoredNode")?.id ?? execution.executableNodeId;
      const detail = await api<{ valueSnapshots: { inputKey: string; evidenceId: string }[] }>(resource);
      const evidence = detail.valueSnapshots.find(value => value.inputKey === "text");
      expect(evidence).toBeTruthy();
      const payload = await api<{ payload: { preview?: string } }>(`${resource}/value-evidence/${evidence!.evidenceId}/payload`);
      inside.push({ call, text: payload.payload.preview ?? "", origin: layout.boundaryOrigin });
    }
    return { runId: started.workflowExecutionId, inside: inside.sort((left, right) => left.call.localeCompare(right.call)) };
  }

  async function promote(host: "workflow" | "activity", name: string) {
    const dialog = page.getByRole("dialog", { name: "Promote to reusable activity", exact: true });
    const trigger = page.getByRole("button", { name: "Promote to reusable activity…", exact: true });
    await row("block").click();
    await expect(trigger).toBeEnabled();
    // Cancel first: nothing is created and the source stays as it was.
    await trigger.click();
    await expect(dialog).toContainText("1 input");
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
    await capture(page, info, `${host}-promotion-review`);
    await dialog.getByRole("button", { name: "Cancel", exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
    expect((await api<{ items: unknown[] }>(`/design/activities/definitions?search=${encodeURIComponent(name)}`)).items).toHaveLength(0);

    await trigger.click();
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
    await dialog.getByRole("button", { name: "Create reusable activity draft", exact: true }).click();
    await expect(dialog).toContainText("Draft created");
    await capture(page, info, `${host}-promotion-created`);
    await dialog.getByRole("button", { name: "Prepare publication", exact: true }).click();
    const publish = dialog.getByRole("button", { name: /^Publish \d+\.\d+\.\d+$/ });
    await expect(publish).toBeEnabled();
    await publish.click();
    await expect(dialog).toContainText("Replace the block with this exact version.");
    await capture(page, info, `${host}-promotion-published`);
    await recordMaterial(host, name, dialog);
    await dialog.getByRole("button", { name: "Replace block with this version", exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole("status").filter({ hasText: "Undo restores the block; the reusable activity stays published." })).toBeVisible();
    await capture(page, info, `${host}-promotion-replaced`);
    const definitions = await api<{ items: { definition: { definitionId: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(name)}`);
    expect(definitions.items).toHaveLength(1);
    activityIds.push(definitions.items[0].definition.definitionId);
    return definitions.items[0].definition;
  }

  try {
    // Workflow host: a block reading the workflow's Greeting input becomes a reusable activity with one input.
    const greeting = { referenceKey: "greeting", name: "Greeting", displayName: "Greeting", description: "", category: "", uiHint: "singleline", storageDriverType: null,
      type: { alias: "String", collectionKind: "Single" }, isRequired: true, isNullable: false };
    const workflow = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
      name: `Block promotion ${stamp}`,
      initialState: { rootActivity: sequence("root", [sequence("block", [line("inside", { expressionType: "Input", value: { referenceKey: "greeting" } })]), line("after", { expressionType: "Literal", value: "after" })]),
        variables: [], inputs: [greeting], outputs: [] },
      layout: [{ nodeId: "block", x: 80, y: 100 }, { nodeId: "after", x: 380, y: 100 }],
      activityPresentation: [{ nodeId: "block", displayName: "Prepare shipment" }]
    });
    workflowIds.push(workflow.definition.id);
    const draftId = workflow.draft.id;
    const saved = () => api<WorkflowDraft>(`/design/workflows/drafts/${draftId}`);
    await page.goto(`/workflows/definitions?backend=legacy&definition=${workflow.definition.id}`); await canvas();
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.locator(".wf-editor-save-status")).toHaveText("Saved");
    await expect(row("block")).toBeVisible();
    const reusable = await promote("workflow", `Prepare shipment ${stamp}`);
    const calls = () => (saved().then(draft => draft.state.rootActivity.structure!.payload.activities as Node[]));
    await expect.poll(async () => (await calls())[0]?.activityVersionId).toBe(reusable.headVersionId);
    expect((await calls())[0]).toMatchObject({ nodeId: "block", inputs: [{ value: { expressionType: "Input", value: { referenceKey: "greeting" } } }] });
    const replaced = await runWorkflow(draftId, { Greeting: "hello" });
    expect(replaced.inside.map(item => item.text)).toEqual(["hello"]);
    proof.workflowReplacement = { definitionId: workflow.definition.id, reusable, run: replaced };

    // Two independently bound calls of the exact published version.
    const inputKey = ((await calls())[0].inputs[0] as { referenceKey: string }).referenceKey;
    const callers = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
      name: `Block promotion callers ${stamp}`, layout: [], activityPresentation: [],
      initialState: { rootActivity: sequence("root", ["one", "two"].map(value => ({ nodeId: `call-${value}`, activityVersionId: reusable.headVersionId,
        inputs: [{ referenceKey: inputKey, value: { expressionType: "Literal", value } }], outputs: [] }))), variables: [], inputs: [], outputs: [] }
    });
    workflowIds.push(callers.definition.id);
    const twoCalls = await runWorkflow(callers.draft.id);
    expect(twoCalls.inside.map(({ call, text }) => ({ call, text }))).toEqual([{ call: "call-one", text: "one" }, { call: "call-two", text: "two" }]);
    proof.twoCalls = twoCalls;

    // One Undo restores the block; the reusable activity stays published.
    await page.getByRole("button", { name: "Undo", exact: true }).click();
    await expect.poll(async () => (await calls())[0]?.structure?.kind ?? null).toBe("elsa.sequence.structure");
    await expect(row("block")).toBeVisible();
    const kept = await api<{ definition: { definitionId: string; headVersionId: string } }>(`/design/activities/definitions/${reusable.definitionId}`);
    expect(kept.definition.headVersionId).toBe(reusable.headVersionId);
    await capture(page, info, "workflow-promotion-undone");
    proof.workflowUndo = { restored: (await calls())[0].nodeId, reusableKept: kept.definition };

    // Activity Definition host: the same command in the reusable graph editor.
    const graph = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
      category: "Qualification", displayName: `Block promotion host ${stamp}`, description: null,
      contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
      provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: sequence("root", [sequence("block", [line("inside", { expressionType: "Literal", value: "packed" })]), line("after", { expressionType: "Literal", value: "after" })]),
        variables: [], outputMappings: [], outcomeMappings: [] } },
      layout: [{ nodeId: "block", data: { x: 80, y: 100 } }, { nodeId: "after", data: { x: 380, y: 100 } }], activityPresentation: [{ nodeId: "block", displayName: "Pack items" }]
    });
    activityIds.push(graph.definition.definitionId);
    const graphDraft = () => api<ActivityDraft>(`/design/activities/drafts/${graph.draft.draftId}`);
    await page.goto(`/workflows/activity-definitions?backend=legacy&definition=${graph.definition.definitionId}&section=editor&draft=${graph.draft.draftId}`); await canvas();
    await expect(row("block")).toBeVisible();
    const nested = await promoteWithoutInputs(`Pack items ${stamp}`);
    const graphCalls = async () => (await graphDraft()).provider.payload.rootActivity.structure!.payload.activities as Node[];
    await expect.poll(async () => (await graphCalls())[0]?.activityVersionId).toBe(nested.headVersionId);
    await page.getByRole("button", { name: "Undo Activity Graph edit", exact: true }).click();
    await expect.poll(async () => (await graphCalls())[0]?.structure?.kind ?? null).toBe("elsa.sequence.structure");
    await capture(page, info, "activity-promotion-undone");
    proof.activityReplacement = { definitionId: graph.definition.definitionId, reusable: nested };
  } catch (error) { failures.push(error); } finally {
    const cleanup: { kind: string; id: string; status: string }[] = [];
    for (const [kind, ids, prefix] of [["run", runIds, "/runtime/workflows/instances"], ["workflow", workflowIds, "/design/workflows/definitions"]] as const) for (const id of ids) {
      try { await api(`${prefix}/${id}`, "DELETE"); cleanup.push({ kind, id, status: "deleted" }); }
      catch (error) { failures.push(error); cleanup.push({ kind, id, status: "failed" }); }
    }
    await info.attach("block-promotion-proof.json", { body: JSON.stringify({ ...proof, cleanup, disposableActivityDefinitionIds: activityIds }, null, 2), contentType: "application/json" });
  }
  if (failures.length === 1) throw failures[0];
  if (failures.length) throw new AggregateError(failures, "Block promotion journey or owned fixture cleanup failed");

  // The reviewed candidate as Studio shows it, beside the exact published version the server returns.
  async function recordMaterial(host: string, name: string, dialog: ReturnType<Page["getByRole"]>) {
    const definition = (await api<{ items: { definition: { headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(name)}`)).items[0]?.definition;
    const published = definition ? await api<unknown>(`/design/activities/versions/${definition.headVersionId}`) : null;
    const candidate = await dialog.locator("details").filter({ hasText: "Reusable activity material" }).locator("pre").textContent();
    proof[`${host}Material`] = { candidate: candidate ? JSON.parse(candidate) : null, published };
  }

  async function promoteWithoutInputs(name: string) {
    const dialog = page.getByRole("dialog", { name: "Promote to reusable activity", exact: true });
    await row("block").click();
    await page.getByRole("button", { name: "Promote to reusable activity…", exact: true }).click();
    await expect(dialog).toContainText("0 inputs");
    await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
    await capture(page, info, "activity-promotion-review");
    await dialog.getByRole("button", { name: "Create reusable activity draft", exact: true }).click();
    await expect(dialog).toContainText("Draft created");
    await dialog.getByRole("button", { name: "Prepare publication", exact: true }).click();
    const publish = dialog.getByRole("button", { name: /^Publish \d+\.\d+\.\d+$/ });
    await expect(publish).toBeEnabled();
    await publish.click();
    await expect(dialog).toContainText("Replace the block with this exact version.");
    await recordMaterial("activity", name, dialog);
    await dialog.getByRole("button", { name: "Replace block with this version", exact: true }).click();
    await expect(dialog).toBeHidden();
    await capture(page, info, "activity-promotion-replaced");
    const definitions = await api<{ items: { definition: { definitionId: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(name)}`);
    expect(definitions.items).toHaveLength(1);
    activityIds.push(definitions.items[0].definition.definitionId);
    return definitions.items[0].definition;
  }
}
