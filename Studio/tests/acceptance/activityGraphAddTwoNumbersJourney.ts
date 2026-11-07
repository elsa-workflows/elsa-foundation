import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Argument = { referenceKey: string; value: { expressionType: string; value: unknown } };
type Node = { nodeId: string; activityVersionId: string; inputs: Argument[]; outputs: Argument[]; structure?: { kind: string; schemaVersion: string; payload: { activities: Node[] } } };
type Draft = { draftId: string; revision: number; provider: { payload: { rootActivity: Node; variables: unknown[]; outputMappings: unknown[] } } };

/** Actual Studio -> activity draft -> immutable publication -> caller execution; no mocked API routes. */
export async function activityGraphAddTwoNumbersJourney(page: Page, info: TestInfo, capture: Capture) {
  const base = process.env.ELSA_WORKBENCH_URL;
  if (!base || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned composed Workbench is required");
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  const catalog = await api<{ activities: { activityTypeKey: string; activityVersionId: string }[] }>("/design/activities/catalog?availability=All");
  const version = (key: string) => {
    const activity = catalog.activities.find(item => item.activityTypeKey === key);
    expect(activity, `The composed host must provide ${key}`).toBeTruthy();
    return activity!.activityVersionId;
  };
  const sequence = version("Elsa.Activities.Sequence.Activities.Sequence");
  const script = version("Elsa.Activities.Scripting.Activities.RunJavaScript");
  const capabilities = await api<{ storageDriverKeys: string[] }>("/design/activities/authoring-capabilities");
  const storage = capabilities.storageDriverKeys.find(key => key === "elsa.json") ?? capabilities.storageDriverKeys[0];
  expect(storage).toBeTruthy();
  const literal = (referenceKey: string, value: unknown): Argument => ({ referenceKey, value: { expressionType: "Literal", value } });
  const compute = (id: string, source: string, program: string): Node => ({ nodeId: id, activityVersionId: script, outputs: [], inputs: [
    literal("script", program), { referenceKey: "arguments", value: { expressionType: "JavaScript", value: source } }
  ] });
  const root: Node = { nodeId: "body-root", activityVersionId: sequence, inputs: [], outputs: [], structure: {
    kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [
      compute("compute", "({a: args.A, b: args.B})", "return args.a + args.b;"),
      compute("forward", "({sum: args.result})", "return args.sum;")
    ] }
  } };
  const member = (name: string) => ({ referenceKey: name, name, type: { alias: "Int32", collectionKind: "Single" },
    isRequired: true, isNullable: false, storageDriverKey: storage, durability: "required" });
  const created = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
    category: "Acceptance", displayName: `Add two numbers ${info.project.name} ${Date.now()}`, description: "Picked graph inputs and earlier result",
    contract: { contractSchemaVersion: "1", inputs: [member("A"), member("B")].map(input => ({ ...input, default: null })),
      outputs: [member("Sum")], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "1", payload: { rootActivity: root, variables: [{
      referenceKey: "sum-value", name: "SumValue", type: { alias: "Int32", collectionKind: "Single" }, storageDriverKey: storage,
      initialValue: { syntax: "Literal", value: 0 }
    }], outputMappings: [] } }, layout: []
  });
  const draftPath = `/design/activities/drafts/${created.draft.draftId}`;
  const readDraft = () => api<Draft>(draftPath);
  const authoringTabs = page.getByRole("tablist", { name: "Activity Definition authoring views", exact: true });
  const body = page.getByLabel("Activity Graph designer", { exact: true });
  async function selectNode(id: string) {
    await authoringTabs.getByRole("tab", { name: "Designer", exact: true }).click();
    const restore = page.getByRole("button", { name: "Restore inspector panel", exact: true });
    if (await restore.isVisible()) await restore.click();
    await body.locator(`[data-graph-node-id='${id}']`).first().click();
    if (info.project.name.startsWith("narrow")) {
      const maximize = page.getByRole("button", { name: "Maximize inspector panel", exact: true });
      if (await maximize.isVisible()) await maximize.click();
    }
  }
  function argumentsRow() { return page.locator(".wf-inspector-tab-panels .wf-property-row").filter({ hasText: "Arguments" }).first(); }
  async function save() {
    const button = page.getByRole("button", { name: "Save now", exact: true });
    if (await button.isEnabled()) await button.click();
    await expect(page.locator(".ad-save-chip")).toContainText("Saved");
  }
  await page.goto(`/workflows/activity-definitions?definition=${created.definition.definitionId}&draft=${created.draft.draftId}&backend=legacy`);
  await expect(body).toBeVisible();
  await capture(page, info, "graph-reference");
  await selectNode("compute");
  const inputs = argumentsRow().getByLabel("Public input bindings", { exact: true });
  await inputs.selectOption("A");
  await inputs.selectOption("B");
  await expect(argumentsRow().getByLabel("Bound public inputs")).toContainText("args.A");
  await expect(argumentsRow().getByLabel("Bound public inputs")).toContainText("args.B");
  await save();
  await capture(page, info, "graph-input-bindings");
  const noEarlier = argumentsRow().getByLabel("Earlier result bindings", { exact: true });
  await noEarlier.focus();
  await expect(noEarlier).toContainText("No earlier results");
  await expect(noEarlier.locator("option")).toHaveCount(1);
  await capture(page, info, "graph-no-earlier-results");
  await selectNode("forward");
  const results = argumentsRow().getByLabel("Earlier result bindings", { exact: true });
  await results.focus();
  await expect(results.locator("option").filter({ hasText: "compute." })).toHaveCount(1);
  await expect(results.locator("option").filter({ hasText: "forward." })).toHaveCount(0);
  await results.selectOption({ label: await results.locator("option").filter({ hasText: "compute." }).textContent() ?? "" });
  await expect(argumentsRow().getByLabel("Bound earlier results")).toContainText("args.result");
  await capture(page, info, "graph-earlier-result");
  await page.getByRole("tablist", { name: "Activity inspector sections", exact: true }).getByRole("tab", { name: "Outputs", exact: true }).click();
  const captureResult = page.getByRole("combobox", { name: /^Capture .* into a variable$/ });
  await captureResult.selectOption("workflow::sum-value");
  await save();
  await capture(page, info, "graph-output-capture");
  await authoringTabs.getByRole("tab", { name: /^Outputs/ }).click();
  const mapping = page.locator(".ad-graph-output-mapping").filter({ hasText: "Sum" });
  await mapping.getByRole("button", { name: "Set value", exact: true }).click();
  await mapping.getByLabel("Value source", { exact: true }).selectOption("Variable");
  await mapping.getByRole("combobox", { name: "Variable reference", exact: true }).selectOption("workflow::sum-value");
  await save();
  await capture(page, info, "graph-sum-mapping");
  const saved = await readDraft();
  const nodes = saved.provider.payload.rootActivity.structure!.payload.activities;
  const parameterValue = (node: Node) => node.inputs.find(input => input.referenceKey === "arguments")!.value.value as { source: string; parameters: Record<string, unknown> };
  expect(parameterValue(nodes[0]!).source).toBe("({a: args.A, b: args.B})");
  expect(parameterValue(nodes[1]!).source).toBe("({sum: args.result})");
  expect(nodes[0]!.inputs.find(input => input.referenceKey === "script")!.value).toEqual({ expressionType: "Literal", value: "return args.a + args.b;" });
  expect(nodes[1]!.inputs.find(input => input.referenceKey === "script")!.value).toEqual({ expressionType: "Literal", value: "return args.sum;" });
  expect(parameterValue(nodes[0]!).parameters).toEqual({ A: { kind: "WorkflowRequest", memberKey: "A" }, B: { kind: "WorkflowRequest", memberKey: "B" } });
  expect(parameterValue(nodes[1]!).parameters).toEqual({ result: { kind: "ActivityResult", producerNodeId: "compute", projectionKey: "result", isOptional: false } });
  expect(nodes[1]!.outputs).toContainEqual(expect.objectContaining({ referenceKey: "result", value: { expressionType: "Variable", value: { referenceKey: "sum-value", declaringScopeId: "workflow" } } }));
  expect(saved.provider.payload.outputMappings).toEqual([{ outputReferenceKey: "Sum", source: { syntax: "Variable", value: { referenceKey: "sum-value", declaringScopeId: "workflow" } } }]);
  await page.reload();
  await selectNode("forward");
  await expect(argumentsRow().getByLabel("Bound earlier results")).toContainText("args.result");
  await page.getByRole("tablist", { name: "Activity inspector sections", exact: true }).getByRole("tab", { name: "Outputs", exact: true }).click();
  await expect(page.getByRole("combobox", { name: /^Capture .* into a variable$/ })).toHaveValue("workflow::sum-value");
  await capture(page, info, "graph-reopened");
  const publication = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname === `${draftPath}/publish`);
  await page.getByRole("button", { name: "Review & publish", exact: true }).click();
  const dialog = page.getByRole("dialog");
  await expect(dialog.getByText("Ready to publish", { exact: true })).toBeVisible();
  await dialog.getByRole("button", { name: /^Publish \d/ }).click();
  const publishedResponse = await publication;
  expect(publishedResponse.ok(), await publishedResponse.text()).toBe(true);
  const published = await publishedResponse.json() as { status: string; outcome: { definitionVersionId: string } };
  expect(published.status.toLowerCase()).toBe("applied");
  await expect(dialog.getByText(/Published immutable version/)).toBeVisible();
  await capture(page, info, "graph-published");
  await dialog.getByRole("button", { name: "Close publication review", exact: true }).click();
  await expect(page.getByRole("combobox", { name: /^Capture .* into a variable$/ })).toBeDisabled();
  await capture(page, info, "graph-read-only");
  const setOutput = (name: string, expressionType: string, value: unknown): Record<string, unknown> => ({ nodeId: `output-${name}`, activityVersionId: "elsa.intrinsic.set-output@1", outputs: [],
    intrinsic: { kind: "setOutput", valueType: { alias: "Int32", collectionKind: "Single" } }, inputs: [literal("name", name), { referenceKey: "value", value: { expressionType, value } }] });
  const caller = await api<{ definition: { id: string }; version: { id: string } }>("/design/workflows/definitions/submit", "POST", { name: `Caller ${info.project.name}`, state: {
    inputs: ["A", "B"].map(name => ({ referenceKey: name, name, type: { alias: "Int32", collectionKind: "Single" }, isNullable: false, displayName: name })), outputs: [],
    variables: [{ referenceKey: "sum", name: "Sum", type: { alias: "Int32", collectionKind: "Single" }, storageDriverType: storage, default: { expressionType: "Literal", value: 0 } }],
    rootActivity: { nodeId: "caller-root", activityVersionId: sequence, inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities: [
      { nodeId: "add", activityVersionId: published.outcome.definitionVersionId, inputs: [literal("A", 2), literal("B", 3)], outputs: [{ referenceKey: "Sum", value: { expressionType: "Variable", value: { referenceKey: "sum", declaringScopeId: "workflow" } } }] },
      setOutput("Sum", "Variable", { referenceKey: "sum", declaringScopeId: "workflow" }), setOutput("CallerA", "WorkflowRequest", { memberKey: "A" }), setOutput("CallerB", "WorkflowRequest", { memberKey: "B" })
    ] } } }
  } });
  try {
    const artifact = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${caller.version.id}/publish`, "POST", {});
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${artifact.artifactId}/execute`, "POST", { sourceReferenceId: artifact.sourceReferenceId, inputs: { A: 100, B: 200 } });
    type Instance = { instance: { status: string }; incidents: unknown[]; outputs: Record<string, { value: { value: number } }> };
    await expect.poll(async () => (await api<Instance>(`/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
    const completed = await api<Instance>(`/runtime/workflows/instances/${run.workflowExecutionId}`);
    expect(completed.incidents).toHaveLength(0);
    expect(completed.outputs.Sum.value.value).toBe(5);
    expect(completed.outputs.CallerA.value.value).toBe(100);
    expect(completed.outputs.CallerB.value.value).toBe(200);
    await page.goto(`/workflows/instances/${run.workflowExecutionId}?backend=legacy`);
    await expect(page.getByText("Completed", { exact: true }).first()).toBeVisible();
    await capture(page, info, "graph-caller-completed");
  } finally { await api(`/design/workflows/definitions/${caller.definition.id}`, "DELETE"); }
}
