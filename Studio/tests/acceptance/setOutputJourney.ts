import { expect, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = { nodeId: string; activityVersionId: string; inputs: unknown[]; outputs: unknown[]; intrinsic?: unknown; structure?: { kind: string; schemaVersion: string; payload: { activities: Node[]; variables: unknown[] } } };
type Draft = { draftId: string; definitionId: string; revision: number; contract: { outputs: { referenceKey: string; name: string; type: { alias: string }; isRequired: boolean }[] }; layout: unknown[]; activityPresentation?: unknown[]; provider: { payload: { rootActivity: Node; outputMappings: unknown[] } } };

/** Actual composed UI authoring plus real publication/runtime, scoped to runner-owned disposable stores. */
export async function setOutputJourney(page: Page, info: TestInfo, capture: Capture, registerWorkflow: (id: string) => void) {
  page.setDefaultTimeout(15_000);
  const backend = process.env.ELSA_WORKBENCH_URL!;
  const viewport = page.viewportSize()!;
  const proof: Record<string, unknown> = { sourceSha: process.env.UX_SOURCE_SHA };
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(backend + path, { method, data });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  async function version(key: string) {
    const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const entry = catalog.items.filter(item => item.definition.activityTypeKey === key);
    expect(entry).toHaveLength(1); return entry[0].definition.headVersionId;
  }
  const sequence = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const rootContract = await api<{ contract: { outcomes: { referenceKey: string; name: string; isEmitted: boolean }[] } }>(`/design/activities/versions/${sequence}`);
  const done = rootContract.contract.outcomes.find(item => item.isEmitted && item.name.toLowerCase() === "done")!;
  const type = { alias: "Double", collectionKind: "Single" };
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const set: Node = { nodeId: "set-sum", activityVersionId: "elsa.intrinsic.set-output@1", inputs: [], outputs: [], intrinsic: { kind: "SetOutput", valueType: { alias: "Elsa.Any", collectionKind: "Single" } } };
  const root = (activities: Node[]): Node => ({ nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities, variables: [] } } });
  const created = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
    displayName: `Sum Double ${info.project.name} ${Date.now()}`, category: "Qualification",
    contract: { contractSchemaVersion: "1", inputs: ["A", "B"].map(name => ({ referenceKey: name.toLowerCase(), name, type, storageDriverKey: "elsa.json", isRequired: true, isNullable: false })), outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: root([set]), variables: [], outputMappings: [], outcomeMappings: [{ sourceOutcomeReferenceKey: done.referenceKey, boundaryOutcomeReferenceKey: "done" }] } }, layout: [], activityPresentation: []
  });
  const route = `/workflows/activity-definitions?backend=legacy&definition=${created.definition.definitionId}&section=editor&draft=${created.draft.draftId}`;
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto(route);
  const views = page.getByRole("tablist", { name: "Activity Definition authoring views" });
  const row = () => page.locator('.wf-sequence-step[data-id="set-sum"]');
  const target = () => page.getByRole("combobox", { name: "Output Name", exact: true });
  await row().click();
  await expect(target()).toBeDisabled();
  await expect(page.getByText("Add an output in this editor's Outputs tab, then select it here.", { exact: true })).toBeVisible();
  await capture(page, info, "set-output-empty");
  await views.getByRole("tab", { name: /^Outputs/ }).click();
  await page.getByRole("button", { name: "Add output", exact: true }).click();
  const outputName = page.getByRole("textbox", { name: "Output name", exact: true });
  await expect(outputName).toBeFocused();
  await outputName.fill("Cancelled"); await outputName.press("Escape");
  await expect(page.getByRole("button", { name: "Add output", exact: true })).toBeFocused();
  await page.getByRole("button", { name: "Add output", exact: true }).click();
  await outputName.fill("Sum"); await outputName.press("Enter");
  await page.getByRole("combobox", { name: "Type", exact: true }).selectOption("Double");
  await page.getByRole("checkbox", { name: /^Required/ }).check();
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await capture(page, info, "set-output-declaration");
  await views.getByRole("tab", { name: "Designer", exact: true }).click();
  await row().click();
  await expect(target()).toBeEnabled();
  await target().focus();
  await expect(target()).toBeFocused();
  await target().press("s"); await target().press("Enter");
  await expect(target()).toHaveValue("Sum");
  const value = page.getByLabel("Value property", { exact: true });
  await expect(value.locator(".wf-property-row-header-meta")).toContainText("Double");
  await value.getByRole("button", { name: /^Value expression syntax:/ }).click();
  await page.getByRole("listbox", { name: "Value expression syntax" }).getByRole("option", { name: "JavaScript", exact: true }).click();
  await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  const expression = page.getByRole("textbox", { name: "JavaScript expression", exact: true });
  await expression.fill("args.A + args.B"); await value.locator(".wf-property-row-header").click();
  const inputBindings = value.getByRole("combobox", { name: "Activity input bindings", exact: true });
  await inputBindings.selectOption("A"); await inputBindings.selectOption("B");
  await expect(value.getByRole("list", { name: "Bound activity inputs", exact: true })).toContainText("args.A");
  await expect(value.getByRole("list", { name: "Bound activity inputs", exact: true })).toContainText("args.B");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await capture(page, info, "set-output-expression");
  await page.reload(); await row().click();
  await expect(target()).toHaveValue("Sum");
  await page.getByRole("button", { name: "JavaScript expression. Activate to edit.", exact: true }).click();
  await expect(expression).toHaveText("args.A + args.B");
  await expect(value.locator(".wf-property-row-header-meta")).toContainText("Double");
  await views.getByRole("tab", { name: /^Outputs/ }).click();
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("RenamedSum");
  await page.getByRole("textbox", { name: "Name", exact: true }).press("Tab");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await views.getByRole("tab", { name: "Designer", exact: true }).click(); await row().click();
  await expect(target()).toHaveAttribute("aria-invalid", "true");
  await capture(page, info, "set-output-removed-target");
  await views.getByRole("tab", { name: /^Outputs/ }).click();
  await page.getByRole("textbox", { name: "Name", exact: true }).fill("Sum");
  await page.getByRole("textbox", { name: "Name", exact: true }).press("Tab");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await views.getByRole("tab", { name: "Designer", exact: true }).click(); await row().click();
  await expect(target()).toHaveAttribute("aria-invalid", "false");
  await page.setViewportSize(viewport);
  if (viewport.width < 600) await page.getByRole("button", { name: "Expand inspector panel", exact: true }).press("Enter");
  await target().scrollIntoViewIfNeeded();
  await capture(page, info, "set-output-reopened");
  await views.getByRole("tab", { name: /^Outputs/ }).click();
  await page.locator(".ad-output-value-source").scrollIntoViewIfNeeded();
  await capture(page, info, "set-output-connections");
  await expect(page.getByText("Assigned by Set Output in the designer.", { exact: true })).toBeVisible();
  const unchanged = await api<Draft>(`/design/activities/drafts/${created.draft.draftId}`);
  await page.getByRole("button", { name: "Change value source", exact: true }).click();
  await expect(page.getByRole("button", { name: "Choose value", exact: true })).toBeDisabled();
  expect((await api<Draft>(`/design/activities/drafts/${created.draft.draftId}`)).provider.payload).toEqual(unchanged.provider.payload);
  // Existing conflicting drafts still need a recovery path; the new UI does not create a conflict.
  await api(`/design/activities/drafts/${unchanged.draftId}`, "PUT", {
    expectedRevision: unchanged.revision, contract: unchanged.contract,
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { ...unchanged.provider.payload, outputMappings: [{ outputReferenceKey: unchanged.contract.outputs[0].referenceKey, source: { syntax: "Literal", value: 42.5 } }] } },
    layout: unchanged.layout, activityPresentation: unchanged.activityPresentation ?? []
  });
  await page.reload(); await views.getByRole("tab", { name: /^Outputs/ }).click();
  await expect(page.getByText("This output also has a Set Output assignment. Remove this connection or the assignment before publishing.", { exact: true })).toBeVisible();
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  const rejectedReview = page.waitForResponse(response => response.request().method() === "POST" && response.url().endsWith("/publication-preflight"));
  await page.getByRole("button", { name: "Review & publish", exact: true }).click();
  const rejected = await (await rejectedReview).json();
  expect(rejected.isPublishable).toBe(false);
  expect(rejected.diagnostics.some((item: { code: string }) => item.code === "activity.graph.set-output-mapping-conflict")).toBe(true);
  proof.rejectedConflict = rejected;
  const review = page.getByRole("dialog", { name: "Review & publish", exact: true });
  await expect(review.getByRole("button", { name: /^Publish / })).toBeDisabled();
  await expect(review).toContainText("Set Output");
  await expect(review).toContainText(/Remove|remove/);
  await capture(page, info, "set-output-conflict-review");
  await review.getByRole("button", { name: "Return to editor", exact: true }).click();
  await page.getByRole("button", { name: "Remove connection", exact: true }).click();
  await expect(page.getByText("Assigned by Set Output in the designer.", { exact: true })).toBeVisible();
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  await page.locator(".ad-output-value-source").scrollIntoViewIfNeeded();
  await capture(page, info, "set-output-conflict-repaired");
  const saved = await api<Draft>(`/design/activities/drafts/${created.draft.draftId}`);
  expect(saved.contract.outputs).toHaveLength(1);
  expect(saved.contract.outputs[0]).toMatchObject({ name: "Sum", type: { alias: "Double" }, isRequired: true });
  expect(saved.provider.payload.outputMappings).toEqual([]);
  const assignedValue = (saved.provider.payload.rootActivity.structure!.payload.activities[0].inputs as { referenceKey: string; value: unknown }[]).find(input => input.referenceKey === "value")!.value;
  expect(assignedValue).toMatchObject({ expressionType: "JavaScript", value: { source: "args.A + args.B", parameters: {
    A: { kind: "WorkflowRequest", memberKey: "a" }, B: { kind: "WorkflowRequest", memberKey: "b" }
  } } });
  const sumKey = saved.contract.outputs[0].referenceKey;
  const preflight = await api<{ isPublishable: boolean; minimumVersion: string; reviewToken: string; definitionHeadVersionId: string | null; diagnostics: unknown[] }>(`/design/activities/drafts/${saved.draftId}/publication-preflight`, "POST", { expectedDraftRevision: saved.revision, expectedDefinitionHeadVersionId: null });
  expect(preflight.isPublishable, JSON.stringify(preflight.diagnostics)).toBe(true);
  const publication = await api<{ outcome: { definitionVersionId: string } }>(`/design/activities/drafts/${saved.draftId}/publish`, "POST", { expectedDraftRevision: saved.revision, expectedDefinitionHeadVersionId: preflight.definitionHeadVersionId, version: preflight.minimumVersion, reviewToken: preflight.reviewToken, idempotencyKey: crypto.randomUUID() });
  await page.goto(route); await row().click();
  if (viewport.width < 600) await page.getByRole("button", { name: "Expand inspector panel", exact: true }).press("Enter");
  await expect(target()).toHaveValue("Sum"); await expect(target()).toBeDisabled();
  await target().scrollIntoViewIfNeeded(); await capture(page, info, "set-output-readonly");
  const variable = { referenceKey: "captured", declaringScopeId: "workflow" };
  const caller = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
    name: `Caller ${info.project.name} ${Date.now()}`,
    initialState: { variables: [{ referenceKey: "captured", name: "Captured", type, storageDriverType: "elsa.json", isNullable: false }], inputs: [], outputs: [{ referenceKey: "caller-sum", name: "CallerSum", type, isNullable: false }], rootActivity: root([
      { nodeId: "call", activityVersionId: publication.outcome.definitionVersionId, inputs: [literal("a", 19.25), literal("b", 23.25)], outputs: [{ referenceKey: sumKey, value: { expressionType: "Variable", value: variable } }] },
      { ...set, nodeId: "caller-output", inputs: [literal("name", "CallerSum"), { referenceKey: "value", value: { expressionType: "Variable", value: variable } }] }
    ]) }, layout: [], activityPresentation: []
  });
  registerWorkflow(caller.definition.id);
  const promoted = await api<{ id: string }>(`/design/workflows/drafts/${caller.draft.id}/promote`, "POST", { operationKey: crypto.randomUUID() });
  const executable = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
  const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${executable.artifactId}/execute`, "POST", { sourceReferenceId: executable.sourceReferenceId });
  const runtimeRoute = `/runtime/workflows/instances/${started.workflowExecutionId}`;
  let completed: { instance: { status: string; blockingIncidentCount?: number }; outputs: Record<string, { value: { value: unknown } }>; incidents?: unknown[] } | undefined;
  const deadline = Date.now() + 30_000;
  try {
    for (;;) {
      completed = await api<typeof completed>(runtimeRoute);
      expect(completed?.instance.blockingIncidentCount ?? 0, JSON.stringify(completed?.incidents)).toBe(0);
      if (completed?.instance.status === "Completed") break;
      expect(Date.now(), `Caller did not complete: ${JSON.stringify(completed)}`).toBeLessThan(deadline);
      await page.waitForTimeout(250);
    }
    expect(Object.keys(completed.outputs)).toEqual(["CallerSum"]);
    expect(completed.outputs.CallerSum.value.value).toBe(42.5);
  } finally {
    Object.assign(proof, { saved, publication, executable, workflowExecutionId: started.workflowExecutionId, completed });
    const path = info.outputPath("set-output-proof.json"); writeFileSync(path, JSON.stringify(proof, null, 2));
    await info.attach("set-output-proof.json", { path, contentType: "application/json" });
  }
  await page.goto(`/workflows/instances/${started.workflowExecutionId}`);
  await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible();
  await page.locator('.wf-sequence-step[data-id="call"], .react-flow__node[data-id="call"]').first().press("Enter");
  await page.getByRole("tab", { name: "Details", exact: true }).click();
  const result = page.getByRole("region", { name: "Workflow outputs", exact: true });
  await expect(result).toContainText("CallerSum"); await expect(result).toContainText("42.5");
  await result.scrollIntoViewIfNeeded(); await capture(page, info, "set-output-caller-result");
}
