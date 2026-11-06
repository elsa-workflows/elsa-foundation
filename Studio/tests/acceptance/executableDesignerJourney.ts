import { expect, type Page, type TestInfo } from "@playwright/test";

type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; structure?: { kind: string; schemaVersion: string; payload: Record<string, unknown> } };
type Submitted = { definition: { id: string }; draftId: string; version: { id: string } };
type Published = { artifactId: string; sourceReferenceId: string };
type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;

/** Real catalog versions, immutable publication and editable source draft in the disposable host. */
export async function executableDesignerJourney(page: Page, info: TestInfo, capture: Capture) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  expect(base, "The owned runner supplies its private Workbench URL").toBeTruthy();
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(type: string) {
    const list = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const item = list.items.find(item => item.definition.activityTypeKey === type);
    expect(item, `Registered ${type}`).toBeTruthy();
    return item!.definition.headVersionId;
  }
  const [sequenceVersion, lineVersion, flowVersion, compensationVersion, bpmnVersion] = await Promise.all([
    version("Elsa.Activities.Sequence.Activities.Sequence"), version("Elsa.Activities.Primitives.Activities.WriteLine"),
    version("Elsa.Activities.Flowchart.Activities.Flowchart"), version("Elsa.Activities.Compensation.Activities.CompensationScope"),
    version("Elsa.Activities.Bpmn.Activities.BpmnProcess")
  ]);
  const line = (id: string, text: string): Node => ({ nodeId: id, activityVersionId: lineVersion, inputs: [{ referenceKey: "text", value: { expressionType: "Literal", value: text } }], outputs: [] });
  const structure = (id: string, activityVersionId: string, kind: string, payload: Record<string, unknown>): Node => ({ nodeId: id, activityVersionId, inputs: [], outputs: [], structure: { kind, schemaVersion: "1.0.0", payload } });
  const seq = (id: string, activities: Node[]) => structure(id, sequenceVersion, "elsa.sequence.structure", { activities });
  const definitions: string[] = [];
  const stamp = `${info.project.name}-${Date.now()}`;
  const state = (rootActivity: Node) => ({ rootActivity, variables: [], inputs: [], outputs: [] });
  async function publish(root: Node, name: string) {
    const submitted = await api<Submitted>("/design/workflows/definitions/submit", "POST", { name: `${name} ${stamp}`, state: state(root) });
    definitions.push(submitted.definition.id);
    const published = await api<Published>(`/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    return { submitted, published };
  }
  async function inspect(published: Published) {
    await page.goto(`/workflows/executables/${published.artifactId}?backend=legacy&ref=${published.sourceReferenceId}`);
    await expect(page.getByRole("region", { name: "Executable canvas", exact: true })).toBeVisible();
  }
  try {
    const root = seq("root", [line("first", "Pinned hello"), seq("nested", [line("inner", "Nested hello")]), line("last", "Pinned goodbye")]);
    const { submitted, published } = await publish(root, "Sequence parity");
    await page.goto(`/workflows/definitions?backend=legacy&definition=${submitted.definition.id}&version=${submitted.version.id}`);
    await expect(page.locator(".wf-sequence-step")).toHaveCount(3);
    const narrow = (info.project.use.viewport?.width ?? 1440) < 800;
    if (narrow) {
      await page.getByRole("button", { name: "Collapse inspector panel", exact: true }).click();
      await page.getByRole("button", { name: "Collapse activities panel", exact: true }).click();
    }
    await expect(page.locator('.wf-sequence-step[data-id="first"]')).toBeVisible();
    await expect.poll(async () => (await page.locator('.wf-sequence-step[data-id="first"]').boundingBox())?.width ?? 0).toBeGreaterThan(180);
    await capture(page, info, "definition-sequence");
    if (narrow) {
      await page.getByRole("button", { name: "Expand activities panel", exact: true }).click();
      await page.getByRole("button", { name: "Expand inspector panel", exact: true }).click();
    }
    // Change the current source after publication. Inspection must retain the pinned texts and topology.
    await inspect(published); // Unmount the editor before changing its draft outside the UI.
    await api(`/design/workflows/drafts/${submitted.draftId}`, "PUT", { state: state(seq("root", [line("changed", "CURRENT DRAFT ONLY")])), layout: [] });
    await page.reload();
    const changed = await api<{ state: { rootActivity: Node } }>(`/design/workflows/drafts/${submitted.draftId}`);
    expect(changed.state.rootActivity.structure?.payload.activities).toEqual([expect.objectContaining({ nodeId: "changed" })]);
    expect(JSON.stringify(changed.state.rootActivity)).toContain("CURRENT DRAFT ONLY");
    const canvas = page.getByRole("region", { name: "Executable canvas", exact: true });
    await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
    await expect(canvas).toContainText("Pinned hello");
    await expect(canvas).not.toContainText("CURRENT DRAFT ONLY");
    const first = canvas.locator('[data-id="first"]');
    await first.press("Enter"); await expect(first).toHaveAttribute("aria-pressed", "true");
    await first.press("Delete"); await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
    await expect(canvas.locator(".wf-sequence-insert, .wf-sequence-actions")).toHaveCount(0);
    await expect(first).toHaveAttribute("draggable", "false");
    expect((await first.boundingBox())!.width).toBeGreaterThan(180);
    await capture(page, info, "executable-sequence");
    await canvas.getByRole("button", { name: /^Open Activities/ }).press("Enter");
    await expect(canvas.locator('[data-id="inner"]')).toBeVisible();
    await capture(page, info, "nested-sequence");
    await canvas.getByRole("button", { name: "Root", exact: true }).click();
    await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
    await page.reload(); await expect(canvas).toContainText("Pinned goodbye");
    await expect(canvas.locator(".wf-sequence-step")).toHaveCount(3);
    await expect(page.getByRole("button", { name: "Collapse executable details panel", exact: true })).toBeVisible();
    await page.getByRole("button", { name: "Collapse executable details panel", exact: true }).click();
    await expect(first).toBeVisible();
    await page.getByRole("button", { name: "Expand executable details panel", exact: true }).click();

    const draftRun = await api<{ artifactId: string; status: string }>("/publishing/workflows/drafts/test-runs", "POST", {
      definitionId: submitted.definition.id, snapshotId: `designer-${stamp}`, state: state(seq("draft-root", [line("draft-step", "Draft snapshot hello")])), layout: [], acknowledgeUnavailableExpressionValidation: true
    });
    expect(draftRun.artifactId, JSON.stringify(draftRun)).toBeTruthy();
    await page.goto(`/workflows/executables/${draftRun.artifactId}?backend=legacy`);
    await expect(canvas.locator(".wf-sequence-step")).toHaveCount(1);
    await expect(canvas).toContainText("Draft snapshot hello");
    await capture(page, info, "draft-sequence");

    const empty = await publish(seq("empty", []), "Empty sequence parity");
    await inspect(empty.published); await expect(canvas).toContainText("This sequence has no steps.");
    await expect(canvas.locator(".wf-sequence-insert")).toHaveCount(0);
    await capture(page, info, "empty-sequence");

    const compensation = await publish(structure("scope", compensationVersion, "elsa.compensation-scope.structure", { steps: [{ name: "Reserve", activity: line("reserve", "Reserve inventory"), compensation: line("release", "Release inventory") }] }), "Compensation parity");
    await inspect(compensation.published);
    await expect(canvas.locator(".wf-compensation-designer")).toBeVisible();
    await expect(canvas.getByRole("listitem", { name: "Step 1, Reserve, compensated by Write Line", exact: true })).toBeVisible();
    await expect(canvas.locator('[data-graph-node-id="reserve"]')).toBeVisible();
    const handler = canvas.locator('[data-graph-node-id="release"]');
    await handler.press("Enter"); await expect(handler).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByRole("complementary", { name: "Executable details panel", exact: true })).toContainText("release");
    await expect(canvas).toContainText("Undoes: Reserve");
    await expect(canvas.getByRole("textbox")).toHaveCount(0);
    await capture(page, info, "compensation");

    const flow = await publish(structure("flow", flowVersion, "elsa.flowchart.structure", {
      activities: [line("flow-a", "Flow one"), line("flow-b", "Flow two")], startNodeId: "flow-a",
      connections: [{ id: "edge", source: { nodeId: "flow-a", port: "Done" }, target: { nodeId: "flow-b" } }], nodeMetadata: {}, connectionMetadata: {}
    }), "Flowchart parity");
    await inspect(flow.published);
    await expect(canvas.locator(".react-flow__node")).toHaveCount(2);
    await expect(canvas.locator(".react-flow__edge")).toHaveCount(1);
    await expect(canvas.locator(".wf-sequence-designer")).toHaveCount(0);
    await capture(page, info, "flowchart");

    const bpmn = await publish(structure("process", bpmnVersion, "elsa.bpmn.structure", {
      activities: [line("bound", "BPMN hello")], elements: [{ elementId: "start", elementType: "startEvent" }, { elementId: "task", elementType: "task", childNodeId: "bound" }, { elementId: "end", elementType: "endEvent" }],
      sequenceFlows: [{ flowId: "s-t", sourceRef: "start", targetRef: "task" }, { flowId: "t-e", sourceRef: "task", targetRef: "end" }]
    }), "BPMN parity");
    await inspect(bpmn.published);
    await expect(canvas.locator(".wf-bpmn-node")).toHaveCount(3);
    await expect(canvas.locator(".react-flow__edge")).toHaveCount(2);
    const boundTask = canvas.locator('.react-flow__node[data-id="task"]');
    await boundTask.press("Enter"); await expect(boundTask).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByRole("complementary", { name: "Executable details panel", exact: true })).toContainText("Write Line");
    await boundTask.press("Delete"); await expect(canvas.locator(".wf-bpmn-node")).toHaveCount(3);
    await capture(page, info, "bpmn");
  } finally {
    for (const id of definitions) await api(`/design/workflows/definitions/${id}`, "DELETE");
  }
}
