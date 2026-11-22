import { expect, test, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { signIn } from "./support/journey";
import type { ActivityDefinitionDraftView } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityDefinitionTypes";
import type { ActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

test.skip(process.env.VARIABLE_OUTPUT_VALIDATION !== "owned", "Requires an explicitly owned separate validation store.");
const backend = process.env.ELSA_WORKBENCH_URL!;
const type = { alias: "Double", collectionKind: "Single" as const };
const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
async function capture(page: Page, info: TestInfo, name: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const path = info.outputPath(name + ".png"); await page.screenshot({ path, fullPage: true, animations: "disabled" });
  await info.attach(name, { path, contentType: "image/png" });
  writeFileSync(info.outputPath(name + ".aria.yml"), await page.locator("body").ariaSnapshot());
}

test("direct graph Variable source survives Studio publication and returns the final nested child value", async ({ page }, info) => {
  test.setTimeout(180_000); page.setDefaultTimeout(15_000);
  await page.addInitScript(() => { localStorage.setItem("elsa-studio-theme-mode", "light"); localStorage.setItem("elsa-studio-workspace-style", "linen"); });
  await signIn(page); const errors: string[] = []; page.on("pageerror", e => errors.push(e.message));
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const r = await page.request.fetch(backend + path, { method, data }); const text = await r.text();
    expect(r.ok(), method + " " + path + ": " + text).toBe(true); return text ? JSON.parse(text) as T : undefined as T;
  }
  const key = "Elsa.Activities.Sequence.Activities.Sequence";
  const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>("/design/activities/definitions?search=" + encodeURIComponent(key));
  const sequence = catalog.items.find(i => i.definition.activityTypeKey === key)!.definition.headVersionId;
  const sequenceNode = (nodeId: string, activities: ActivityNode[]): ActivityNode => ({ nodeId, activityVersionId: sequence, inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities, variables: [] } } });
  const reference = { referenceKey: "running-total", declaringScopeId: "workflow" };
  const contract = { contractSchemaVersion: "1", inputs: ["A", "B"].map(name => ({ referenceKey: name.toLowerCase(), name, type, isRequired: true, isNullable: false, default: null, storageDriverKey: "elsa.json", durability: "required" })), outputs: [{ referenceKey: "sum", name: "Sum", type, isRequired: true, isNullable: false, storageDriverKey: "elsa.json", durability: "required" }], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] };
  async function graph(name: string, nodes: ActivityNode[], variables: unknown[] = []) {
    const g = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
      displayName: `Variable output ${name} ${Date.now()}`, category: "Validation", contract,
      provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: sequenceNode("root", nodes), variables, outputMappings: [], outcomeMappings: [{ sourceOutcomeReferenceKey: "Done", boundaryOutcomeReferenceKey: "done" }] } }, layout: [], activityPresentation: []
    });
    return { ...g, route: `/workflows/activity-definitions?backend=legacy&definition=${g.definition.definitionId}&section=editor&draft=${g.draft.draftId}` };
  }
  const inner = await graph("producer", [{ nodeId: "produce", activityVersionId: "elsa.intrinsic.set-output@1", inputs: [literal("name", "Sum"), { referenceKey: "value", value: { expressionType: "JavaScript", value: { language: "JavaScript", source: "args.A + args.B", resultType: type, parameters: { A: { kind: "WorkflowRequest", memberKey: "a" }, B: { kind: "WorkflowRequest", memberKey: "b" } }, options: {}, capabilityProfile: "binding-pure-v1" } } }], outputs: [], intrinsic: { kind: "SetOutput", valueType: { alias: "Elsa.Any", collectionKind: "Single" } } }]);
  const innerDraft = await api<ActivityDefinitionDraftView>(`/design/activities/drafts/${inner.draft.draftId}`);
  const review = await api<{ isPublishable: boolean; minimumVersion: string; reviewToken: string; diagnostics: unknown[] }>(`/design/activities/drafts/${innerDraft.draftId}/publication-preflight`, "POST", { expectedDraftRevision: innerDraft.revision, expectedDefinitionHeadVersionId: null });
  expect(review.isPublishable, JSON.stringify(review.diagnostics)).toBe(true);
  const innerVersion = await api<{ outcome: { definitionVersionId: string } }>(`/design/activities/drafts/${innerDraft.draftId}/publish`, "POST", { expectedDraftRevision: innerDraft.revision, expectedDefinitionHeadVersionId: null, version: review.minimumVersion, reviewToken: review.reviewToken, idempotencyKey: crypto.randomUUID() });
  const outer = await graph("private capture", [{ nodeId: "nested-producer", activityVersionId: innerVersion.outcome.definitionVersionId, inputs: ["a", "b"].map(referenceKey => ({ referenceKey, value: { expressionType: "Input", value: { referenceKey } } })), outputs: [{ referenceKey: "sum", value: { expressionType: "Variable", value: reference } }] }], [{ referenceKey: reference.referenceKey, name: "RunningTotal", type, storageDriverKey: "elsa.json", initialValue: { syntax: "Literal", value: 9 } }]);
  await page.goto(outer.route);
  const views = page.getByRole("tablist", { name: "Activity Definition authoring views", exact: true });
  await views.getByRole("tab", { name: /^Outputs/ }).click();
  const source = page.getByRole("region", { name: "Value source for Sum", exact: true });
  await expect(source.getByRole("button", { name: "Choose value", exact: true })).toBeEnabled(); await capture(page, info, "variable-source-unassigned");
  await source.getByRole("button", { name: "Choose value", exact: true }).click();
  await source.getByRole("button", { name: "Change value source", exact: true }).click();
  const syntax = source.getByRole("combobox", { name: "Expression type", exact: true });
  await syntax.focus(); await syntax.press("v"); await syntax.press("Enter"); await expect(syntax).toHaveValue("Variable");
  const picker = source.getByRole("combobox", { name: "Variable reference", exact: true });
  await expect(picker.locator('option[value="workflow::running-total"]')).toBeEnabled();
  await picker.focus(); await picker.press("r"); await picker.press("Enter"); await expect(picker).toHaveValue("workflow::running-total");
  await expect(page.locator(".ad-save-chip")).toHaveText("Saved"); await capture(page, info, "variable-source-selected-light");
  const saved = await api<ActivityDefinitionDraftView>(`/design/activities/drafts/${outer.draft.draftId}`);
  expect((saved.provider.payload as { outputMappings: unknown[] }).outputMappings).toEqual([{ outputReferenceKey: "sum", source: { syntax: "Variable", value: reference } }]);
  await page.reload(); await views.getByRole("tab", { name: /^Outputs/ }).click(); await expect(picker).toHaveValue("workflow::running-total");
  await page.getByRole("radio", { name: "Dark", exact: true }).check(); await page.setViewportSize({ width: 360, height: 1000 }); await source.scrollIntoViewIfNeeded(); await capture(page, info, "variable-source-reopened-dark-narrow");
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.getByRole("button", { name: "Review & publish", exact: true }).click();
  const dialog = page.getByRole("dialog", { name: "Review & publish", exact: true }); const publish = dialog.getByRole("button", { name: /^Publish 1\./ }); await expect(publish).toBeEnabled();
  await capture(page, info, "variable-source-ready-to-publish");
  const publishedResponse = page.waitForResponse(r => r.url().endsWith(`/drafts/${outer.draft.draftId}/publish`) && r.request().method() === "POST"); await publish.click();
  const published = await (await publishedResponse).json() as { outcome: { definitionVersionId: string } }; expect(published.outcome.definitionVersionId).toBeTruthy();
  await expect(dialog.getByText(/Published immutable version/)).toBeVisible(); await dialog.getByRole("button", { name: "Close publication review", exact: true }).click();
  await source.scrollIntoViewIfNeeded(); await expect(picker).toBeDisabled(); await capture(page, info, "variable-source-published-readonly");
  const captured = (referenceKey: string) => ({ referenceKey, declaringScopeId: "workflow" });
  const call = (nodeId: string, a: number, b: number, target: string): ActivityNode => ({ nodeId, activityVersionId: published.outcome.definitionVersionId, inputs: [literal("a", a), literal("b", b)], outputs: [{ referenceKey: "sum", value: { expressionType: "Variable", value: captured(target) } }] });
  const expose = (nodeId: string, name: string, target: string): ActivityNode => ({ nodeId, activityVersionId: "elsa.intrinsic.set-output@1", inputs: [literal("name", name), { referenceKey: "value", value: { expressionType: "Variable", value: captured(target) } }], outputs: [], intrinsic: { kind: "SetOutput", valueType: { alias: "Elsa.Any", collectionKind: "Single" } } });
  const caller = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", { name: `Variable output caller ${Date.now()}`, initialState: { variables: ["first", "second", "running-total"].map(referenceKey => ({ referenceKey, name: referenceKey === "running-total" ? "RunningTotal" : referenceKey, type, storageDriverType: "elsa.json", isNullable: false, default: { expressionType: "Literal", value: 999 } })), inputs: [], outputs: ["CallerSum", "OtherSum"].map(name => ({ referenceKey: name, name, type, isNullable: false })), rootActivity: sequenceNode("caller-root", [call("first-call", 19.25, 23.25, "first"), call("second-call", 1.5, 2.75, "second"), expose("first-output", "CallerSum", "first"), expose("second-output", "OtherSum", "second")]) }, layout: [], activityPresentation: [] });
  const promoted = await api<{ id: string }>(`/design/workflows/drafts/${caller.draft.id}/promote`, "POST", { operationKey: crypto.randomUUID() }); const executable = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
  const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${executable.artifactId}/execute`, "POST", { sourceReferenceId: executable.sourceReferenceId });
  type Run = { instance: { status: string; blockingIncidentCount: number }; outputs: Record<string, { value: { value: number } }> }; let completed!: Run;
  for (const deadline = Date.now() + 30_000;;) { completed = await api<Run>(`/runtime/workflows/instances/${started.workflowExecutionId}`); writeFileSync(info.outputPath("variable-runtime-observation.json"), JSON.stringify(completed, null, 2)); expect(completed.instance.blockingIncidentCount, JSON.stringify(completed)).toBe(0); if (completed.instance.status === "Completed") break; expect(Date.now()).toBeLessThan(deadline); await page.waitForTimeout(250); }
  expect(Object.keys(completed.outputs).sort()).toEqual(["CallerSum", "OtherSum"]); expect(completed.outputs.CallerSum.value.value).toBe(42.5); expect(completed.outputs.OtherSum.value.value).toBe(4.25);
  await page.goto(`/workflows/instances/${started.workflowExecutionId}`); await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible(); await page.getByRole("tab", { name: "Details", exact: true }).click();
  const result = page.getByRole("region", { name: "Workflow outputs", exact: true }); await expect(result).toContainText("42.5"); await expect(result).toContainText("4.25"); await result.scrollIntoViewIfNeeded(); await capture(page, info, "variable-caller-final-values"); expect(errors).toEqual([]);
  const proof = { sourceSha: process.env.VARIABLE_OUTPUT_SOURCE_SHA, inner, outer, saved, published, caller, started, completed, pageErrors: errors };
  writeFileSync(info.outputPath("direct-variable-output-proof.json"), JSON.stringify(proof, null, 2));
});
