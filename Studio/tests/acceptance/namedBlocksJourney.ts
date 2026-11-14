import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
type Content = { state: { rootActivity: Node; variables: unknown[]; inputs: unknown[]; outputs: unknown[] }; layout: unknown[]; activityPresentation: { nodeId: string; displayName: string; description?: string }[] };
type GraphDraft = { draftId: string; definitionId: string; revision: number; provider: { payload: { rootActivity: Node } }; activityPresentation: Content["activityPresentation"] };

/** Normal API setup and publication, real composed editors and runtime; no simulated browser responses. */
export async function namedBlocksJourney(page: Page, info: TestInfo, capture: Capture, profile: "commands" | "expression-results" = "commands") {
  const backend = process.env.ELSA_WORKBENCH_URL!;
  const multipleKey: "Meta" | "Control" = await page.evaluate(() => /Mac/.test(navigator.platform) ? "Meta" : "Control");
  const workflowIds: string[] = [], runIds: string[] = [], activityIds: string[] = [];
  const proof: { host: string; kind: string; before: unknown; after?: unknown; blockId?: string; saved?: unknown; baselineVersion?: string; changedVersion?: string }[] = [];
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
  const versions = {
    sequence: await version("Elsa.Activities.Sequence.Activities.Sequence"),
    flowchart: await version("Elsa.Activities.Flowchart.Activities.Flowchart"),
    line: await version("Elsa.Activities.Primitives.Activities.WriteLine"),
    complete: await version("Elsa.Activities.Graph.Runtime.Activities.Complete"),
    inline: await version("Elsa.Activities.Primitives.Activities.Inline")
  };
  const line = (nodeId: string): Node => ({ nodeId, activityVersionId: versions.line, inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: nodeId } }], outputs: [] });
  const complete: Node = { nodeId: "complete", activityVersionId: versions.complete, inputs: [{ referenceKey: "outcome", value: { expressionType: "Literal", value: "done" } }], outputs: [] };
  const body = (kind: "sequence" | "flowchart", terminal: boolean): Content => {
    const activities = [line("one"), terminal ? complete : line("two"), line("after")];
    return { state: { rootActivity: { nodeId: "root", activityVersionId: versions[kind], inputs: [], outputs: [], structure: {
      kind: `elsa.${kind}.structure`, schemaVersion: "1.0.0", payload: kind === "sequence" ? { activities, variables: [] } : {
        activities, variables: [], startNodeId: "one", nodeMetadata: {}, connectionMetadata: {}, connections: [
          { id: "one-two", source: { nodeId: "one", port: "Done" }, target: { nodeId: "two" } },
          { id: "two-after", source: { nodeId: "two", port: "Done" }, target: { nodeId: "after" } }
        ]
      }
    } }, variables: [], inputs: [], outputs: [] }, layout: [
      { nodeId: "one", x: 80, y: 100 }, { nodeId: terminal ? "complete" : "two", x: 380, y: 100 }, { nodeId: "after", x: 680, y: 100 }
    ], activityPresentation: [] };
  };
  async function createWorkflow(name: string, content: Content) {
    const value = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", { name, initialState: content.state, layout: content.layout, activityPresentation: content.activityPresentation });
    workflowIds.push(value.definition.id);
    return { definitionId: value.definition.id, draftId: value.draft.id };
  }
  const expectedText: Record<string, string> = profile === "expression-results" ? { two: "two", after: "43" } : { one: "one", two: "two", after: "after", "caller-after": "caller-after" };
  async function runWorkflow(draftId: string) {
    const version = await api<{ id: string }>(`/design/workflows/drafts/${draftId}/promote`, "POST", { operationKey: crypto.randomUUID() });
    const publication = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${version.id}/publish`, "POST", {});
    const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${publication.artifactId}/execute`, "POST", { sourceReferenceId: publication.sourceReferenceId });
    runIds.push(started.workflowExecutionId);
    let result: { instance: { status: string }; activities: Record<string, unknown>[] } | undefined;
    await expect.poll(async () => {
      const current = await api<{ instance: { status: string }; activities: Record<string, unknown>[] }>(`/runtime/workflows/instances/${started.workflowExecutionId}`);
      result = current;
      return current.instance.status;
    }).toBe("Completed");
    const executions = await api<{ items: { execution: { activityExecutionId: string; authoredActivityId: string; status: string; startedAt?: string | null } }[] }>(`/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions`);
    expect(result!.instance.status).toBe("Completed");
    const values: { authoredActivityId: string; activityExecutionId: string; value: string }[] = [];
    for (const { execution } of executions.items) {
      if (execution.status !== "Completed" || !Object.hasOwn(expectedText, execution.authoredActivityId)) continue;
      const resource = `/runtime/workflows/instances/${started.workflowExecutionId}/activity-executions/${execution.activityExecutionId}`;
      const detail = await api<{ valueSnapshots: { inputKey: string; evidenceId: string }[] }>(resource);
      const evidence = detail.valueSnapshots.find(value => value.inputKey === "text"); expect(evidence).toBeTruthy();
      const payload = await api<{ payload: { preview: string } }>(`${resource}/value-evidence/${evidence!.evidenceId}/payload`);
      expect(payload.payload.preview).toBe(expectedText[execution.authoredActivityId]);
      values.push({ authoredActivityId: execution.authoredActivityId, activityExecutionId: execution.activityExecutionId, value: payload.payload.preview });
    }
    return { runId: started.workflowExecutionId, publication, activities: executions.items.map(item => item.execution), values };
  }
  async function publishActivity(draft: GraphDraft, expectedHead: string | null) {
    const preflight = await api<{ isPublishable: boolean; minimumVersion: string; reviewToken: string; definitionHeadVersionId: string | null; diagnostics: unknown[] }>(`/design/activities/drafts/${draft.draftId}/publication-preflight`, "POST", { expectedDraftRevision: draft.revision, expectedDefinitionHeadVersionId: expectedHead });
    expect(preflight.isPublishable, JSON.stringify(preflight.diagnostics)).toBe(true);
    const receipt = await api<{ outcome: { definitionVersionId: string } }>(`/design/activities/drafts/${draft.draftId}/publish`, "POST", {
      expectedDraftRevision: draft.revision, expectedDefinitionHeadVersionId: preflight.definitionHeadVersionId,
      version: preflight.minimumVersion, reviewToken: preflight.reviewToken, idempotencyKey: crypto.randomUUID()
    });
    expect(receipt.outcome?.definitionVersionId).toBeTruthy();
    return receipt.outcome.definitionVersionId;
  }
  async function runActivity(versionId: string) {
    const content = body("sequence", false);
    content.state.rootActivity.structure!.payload.activities = [{ nodeId: "call", activityVersionId: versionId, inputs: [], outputs: [] }, line("caller-after")];
    const caller = await createWorkflow(`Named blocks caller ${info.project.name} ${Date.now()}`, content);
    return runWorkflow(caller.draftId);
  }
  async function canvas() {
    if ((page.viewportSize()?.width ?? 1000) <= 600) for (const name of ["Collapse inspector panel", "Collapse activities panel"]) {
      const button = page.getByRole("button", { name, exact: true });
      if (await button.isVisible()) await button.click();
    }
  }
  const row = (id: string) => page.locator(`.wf-sequence-step[data-id="${id}"], .react-flow__node[data-id="${id}"]`).first();
  try {
    for (const host of (profile === "expression-results" ? ["workflow"] as const : ["workflow", "activity"] as const)) for (const kind of (profile === "expression-results" ? ["sequence"] as const : ["sequence", "flowchart"] as const)) {
      const terminal = host === "activity" && kind === "sequence";
      const content = body(kind, terminal), name = `Pack order ${host} ${kind}`;
      if (profile === "expression-results") {
        const children = content.state.rootActivity.structure!.payload.activities as Node[];
        children[0] = { nodeId: "one", activityVersionId: versions.inline, inputs: [{ referenceKey: "Expression", value: { expressionType: "Object", value: 42 } }], outputs: [] };
        children[2].inputs = [{ referenceKey: "text", value: { expressionType: "JavaScript", value: {
          language: "JavaScript", source: "args.prior + 1", resultType: { alias: "String", collectionKind: "single" }, options: {}, capabilityProfile: "binding-pure-v1",
          parameters: { prior: { kind: "ActivityResult", producerNodeId: "one", projectionKey: "$result", producerScopeId: "scope:root" } }
        } } }];
      }
      let definitionId: string, draftId: string, baselineVersion: string | undefined;
      let before: unknown;
      let baselinePayload: unknown;
      if (host === "workflow") {
        const created = await createWorkflow(`Named blocks ${kind} ${info.project.name} ${Date.now()}`, content);
        ({ definitionId, draftId } = created);
        before = await runWorkflow(draftId);
      } else {
        const rootContract = await api<{ contract: { outcomes: { referenceKey: string; name: string; isEmitted: boolean }[] } }>(`/design/activities/versions/${versions[kind]}`);
        const done = rootContract.contract.outcomes.find(item => item.isEmitted && (item.name || item.referenceKey).toLowerCase() === "done");
        expect(done).toBeTruthy();
        const created = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
          category: "Qualification", displayName: `Named blocks ${kind} ${info.project.name} ${Date.now()}`, description: null,
          contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
          provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: content.state.rootActivity, variables: [], outputMappings: [], outcomeMappings: terminal ? [] : [{ sourceOutcomeReferenceKey: done!.referenceKey, boundaryOutcomeReferenceKey: "done" }] } },
          layout: content.layout.map(record => { const { nodeId, ...data } = record as { nodeId: string }; return { nodeId, data }; }), activityPresentation: []
        });
        definitionId = created.definition.definitionId; activityIds.push(definitionId);
        const initial = await api<GraphDraft>(`/design/activities/drafts/${created.draft.draftId}`);
        baselineVersion = await publishActivity(initial, null);
        baselinePayload = (await api<{ provider: { payload: unknown } }>(`/design/activities/versions/${baselineVersion}`)).provider.payload;
        before = await runActivity(baselineVersion);
        const fresh = await api<GraphDraft>(`/design/activities/definitions/${definitionId}/drafts`, "POST", { sourceVersionId: baselineVersion });
        draftId = fresh.draftId;
      }
      const item = { host, kind, before, baselineVersion } as typeof proof[number]; proof.push(item);
      const route = host === "workflow" ? `/workflows/definitions?backend=legacy&definition=${definitionId}` : `/workflows/activity-definitions?backend=legacy&definition=${definitionId}&section=editor&draft=${draftId}`;
      await page.goto(route); await canvas();
      await expect(row("one")).toBeVisible();
      await row("one").click();
      await row(terminal ? "complete" : "two").click({ modifiers: [kind === "sequence" ? "Shift" : multipleKey] });
      await expect(page.locator('.wf-sequence-step[aria-pressed="true"], .react-flow__node[aria-pressed="true"]')).toHaveCount(2);
      await capture(page, info, `${host}-${kind}-selection`);
      await page.getByRole("button", { name: "Extract block…", exact: true }).click();
      const dialog = page.getByRole("dialog", { name: "Extract named block", exact: true });
      await dialog.getByRole("textbox", { name: "Name", exact: true }).fill(name);
      await dialog.getByRole("textbox", { name: "Summary", exact: true }).fill("Grouped order steps");
      await dialog.getByRole("button", { name: "Preview change", exact: true }).click();
      await expect(dialog.getByRole("button", { name: "Apply change", exact: true })).toBeEnabled();
      await expect(dialog).toContainText(name);
      await capture(page, info, `${host}-${kind}-preview`);
      await dialog.getByRole("button", { name: "Apply change", exact: true }).click(); await expect(dialog).toBeHidden();
      const saved = async () => host === "workflow" ? api<Content>(`/design/workflows/drafts/${draftId}`) : api<GraphDraft>(`/design/activities/drafts/${draftId}`);
      let persisted: Content | GraphDraft | undefined;
      await expect.poll(async () => { persisted = await saved(); return persisted.activityPresentation.some(value => value.displayName === name); }).toBe(true);
      const blockId = persisted!.activityPresentation.find(value => value.displayName === name)!.nodeId;
      item.blockId = blockId; item.saved = persisted;
      if (profile === "expression-results") {
        const draft = persisted as Content;
        expect(draft.state.rootActivity.structure!.payload.variables).toHaveLength(1);
        const consumer = (draft.state.rootActivity.structure!.payload.activities as Node[]).find(node => node.nodeId === "after")!;
        const expression = (consumer.inputs[0] as { value: { value: { source: string; parameters: { prior: { kind: string } } } } }).value.value;
        expect(expression.source).toBe("args.prior + 1"); expect(expression.parameters.prior.kind).toBe("Variable");
      }
      await expect(row(blockId)).toBeVisible();
      await page.getByRole("button", { name: `Open ${name} (${profile === "expression-results" ? 3 : 2} steps)`, exact: true }).click();
      await expect(row("one")).toBeVisible(); await expect(page.locator(".wf-breadcrumb")).toContainText(name);
      if (terminal) await expect(row("complete")).toContainText("Ends reusable activity");
      await capture(page, info, `${host}-${kind}-nested`);
      await page.getByRole("button", { name: host === "workflow" ? "Undo" : "Undo Activity Graph edit", exact: true }).click();
      await expect(row(blockId)).toHaveCount(0);
      await expect.poll(async () => (await saved()).activityPresentation.some(value => value.nodeId === blockId)).toBe(false);
      await expect(page.locator('.wf-sequence-step[aria-pressed="true"], .react-flow__node[aria-pressed="true"]')).toHaveCount(2);
      await page.getByRole("button", { name: host === "workflow" ? "Redo" : "Redo Activity Graph edit", exact: true }).click();
      await expect(page.locator(".wf-breadcrumb")).toContainText(name);
      await expect.poll(async () => (await saved()).activityPresentation.some(value => value.nodeId === blockId)).toBe(true);
      await page.reload(); await canvas();
      await expect(row(blockId)).toBeVisible();
      await capture(page, info, `${host}-${kind}-reopened`);
      await expect.poll(async () => (await saved()).activityPresentation.some(value => value.nodeId === blockId && value.displayName === name)).toBe(true);
      // Exercise the blank-container and inverse commands through the same host authority/history path.
      await row(blockId).click();
      await page.getByRole("button", { name: `Create ${kind === "sequence" ? "Sequence" : "Flowchart"} block`, exact: true }).click();
      const createDialog = page.getByRole("dialog", { name: `Create ${kind === "sequence" ? "Sequence" : "Flowchart"} block`, exact: true });
      const emptyName = `Empty ${host} ${kind}`;
      await createDialog.getByRole("textbox", { name: "Name", exact: true }).fill(emptyName);
      const chooseVersion = createDialog.getByRole("combobox");
      if (await chooseVersion.count()) await chooseVersion.selectOption(versions[kind]);
      await createDialog.getByRole("button", { name: "Preview change", exact: true }).click();
      await expect(createDialog.getByRole("button", { name: "Apply change", exact: true })).toBeEnabled();
      await createDialog.getByRole("button", { name: "Apply change", exact: true }).click();
      await expect(createDialog).toBeHidden();
      let blankId: string | undefined;
      await expect.poll(async () => { blankId = (await saved()).activityPresentation.find(value => value.displayName === emptyName)?.nodeId; return !!blankId; }).toBe(true);
      await expect(row(blankId!)).toBeVisible();
      await capture(page, info, `${host}-${kind}-created-empty`);
      await row(blankId!).click();
      await page.getByRole("button", { name: "Ungroup…", exact: true }).click();
      const ungroup = page.getByRole("dialog", { name: "Ungroup block", exact: true });
      await ungroup.getByRole("button", { name: "Preview change", exact: true }).click();
      await expect(ungroup.getByRole("button", { name: "Apply change", exact: true })).toBeEnabled();
      await ungroup.getByRole("button", { name: "Apply change", exact: true }).click();
      await expect(ungroup).toBeHidden(); await expect(row(blankId!)).toHaveCount(0);
      await expect.poll(async () => (await saved()).activityPresentation.some(value => value.nodeId === blankId)).toBe(false);
      await expect(row(blockId)).toBeVisible();
      await capture(page, info, `${host}-${kind}-empty-ungrouped`);
      await row(blockId).click();
      await page.getByRole("button", { name: "Ungroup…", exact: true }).click();
      const inverse = page.getByRole("dialog", { name: "Ungroup block", exact: true });
      await inverse.getByRole("button", { name: "Preview change", exact: true }).click();
      await expect(inverse.getByRole("button", { name: "Apply change", exact: true })).toBeEnabled();
      await inverse.getByRole("button", { name: "Apply change", exact: true }).click();
      await expect(inverse).toBeHidden(); await expect(row(blockId)).toHaveCount(0);
      await expect(row("one")).toBeVisible(); await expect(row(terminal ? "complete" : "two")).toBeVisible();
      await expect.poll(async () => (await saved()).activityPresentation.some(value => value.nodeId === blockId)).toBe(false);
      await capture(page, info, `${host}-${kind}-ungrouped`);
      await page.getByRole("button", { name: host === "workflow" ? "Undo" : "Undo Activity Graph edit", exact: true }).click();
      await expect(row(blockId)).toBeVisible();
      await expect.poll(async () => (await saved()).activityPresentation.some(value => value.nodeId === blockId)).toBe(true);
      if (host === "workflow") item.after = await runWorkflow(draftId);
      else {
        const current = await api<GraphDraft>(`/design/activities/drafts/${draftId}`);
        item.changedVersion = await publishActivity(current, baselineVersion!);
        item.after = await runActivity(item.changedVersion);
        const original = await api<{ provider: { payload: { rootActivity: Node } } }>(`/design/activities/versions/${baselineVersion}`);
        expect(original.provider.payload).toEqual(baselinePayload);
      }
      if (!terminal) for (const run of [item.before, item.after]) {
        const activities = (run as { activities: Record<string, unknown>[] }).activities;
        for (const id of ["one", "two", "after", ...(host === "activity" ? ["caller-after"] : [])]) {
          const matched = activities.filter(value => value.authoredActivityId === id);
          expect(matched).toHaveLength(1); expect(matched[0].status).toBe("Completed");
        }
      }
      // The two terminal runs must prove an actual nearest graph exit and absence of the otherwise
      // ordered trailing step; their full execution records are retained for independent inspection.
      if (terminal) for (const run of [item.before, item.after]) {
        const activities = (run as { activities: Record<string, unknown>[] }).activities;
        expect(activities.filter(value => value.authoredActivityId === "complete")).toHaveLength(1);
        expect(activities.find(value => value.authoredActivityId === "complete")?.status).toBe("Completed");
        expect(activities.filter(value => value.authoredActivityId === "after").every(value => value.startedAt == null && value.status === "Cancelled")).toBe(true);
        expect(activities.find(value => value.authoredActivityId === "caller-after")?.status).toBe("Completed");
      }
    }
  } finally {
    const cleanup: { kind: string; id: string; status: string }[] = [];
    const failures: unknown[] = [];
    for (const [kind, ids, prefix] of [["run", runIds, "/runtime/workflows/instances"], ["workflow", workflowIds, "/design/workflows/definitions"]] as const) for (const id of ids) {
      try { await api(`${prefix}/${id}`, "DELETE"); cleanup.push({ kind, id, status: "deleted" }); }
      catch (error) { failures.push(error); cleanup.push({ kind, id, status: "failed" }); }
    }
    await info.attach("named-blocks-proof.json", { body: JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA, proof, cleanup, disposableActivityDefinitionIds: activityIds }, null, 2), contentType: "application/json" });
    if (failures.length) throw new AggregateError(failures, "Owned named-block fixture cleanup failed");
  }
}
