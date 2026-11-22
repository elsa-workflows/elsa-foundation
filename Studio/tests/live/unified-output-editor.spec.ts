import { expect, test, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { signIn } from "./support/journey";
import type { ActivityDefinitionDraftView, ActivityOutputContract } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityDefinitionTypes";
import type { ActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

test.skip(process.env.OUTPUT_EDITOR_VALIDATION !== "owned", "Requires an explicitly owned separate validation store.");
const backend = process.env.ELSA_WORKBENCH_URL!;
const type = { alias: "Double", collectionKind: "Single" as const };
const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
const output = (referenceKey: string, name: string, required = false): ActivityOutputContract => ({ referenceKey, name, type, isRequired: required, isNullable: false, storageDriverKey: "elsa.json", durability: "required" });
const setter = (nodeId: string, name = "Sum"): ActivityNode => ({ nodeId, activityVersionId: "elsa.intrinsic.set-output@1", inputs: [literal("name", name), literal("value", 42.5)], outputs: [], intrinsic: { kind: "SetOutput", valueType: { alias: "Elsa.Any", collectionKind: "Single" } } });
async function capture(page: Page, info: TestInfo, name: string) {
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  const path = info.outputPath(name + ".png"); await page.screenshot({ path, fullPage: true, animations: "disabled" });
  await info.attach(name, { path, contentType: "image/png" });
  writeFileSync(info.outputPath(name + ".aria.yml"), await page.locator("body").ariaSnapshot());
}

for (const mode of ["light", "dark"] as const) test(`unified output declaration, source recovery and exact step navigation (${mode})`, async ({ page }, info) => {
  test.setTimeout(180_000); page.setDefaultTimeout(15_000);
  await page.addInitScript(mode => { localStorage.setItem("elsa-studio-theme-mode", mode); localStorage.setItem("elsa-studio-workspace-style", "linen"); }, mode);
  await signIn(page);
  const errors: string[] = []; page.on("pageerror", error => errors.push(error.message));
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(backend + path, { method, data }); const text = await response.text();
    expect(response.ok(), method + " " + path + ": " + text).toBe(true); return text ? JSON.parse(text) as T : undefined as T;
  }
  const key = "Elsa.Activities.Sequence.Activities.Sequence";
  const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>("/design/activities/definitions?search=" + encodeURIComponent(key));
  const sequence = catalog.items.find(item => item.definition.activityTypeKey === key)!.definition.headVersionId;
  const sequenceNode = (nodeId: string, activities: ActivityNode[]): ActivityNode => ({ nodeId, activityVersionId: sequence, inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities, variables: [] } } });
  async function createGraph(name: string, outputs: ActivityOutputContract[], nodes: ActivityNode[], mappings: unknown[] = []) {
    const created = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
      displayName: `Output editor validation ${name} ${mode} ${Date.now()}`, category: "Validation",
      contract: { contractSchemaVersion: "1", inputs: ["A", "B"].map(name => ({ referenceKey: name.toLowerCase(), name, type, isRequired: true, isNullable: false, default: null, storageDriverKey: "elsa.json", durability: "required" })), outputs, outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
      provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: sequenceNode("root", [sequenceNode("nested", nodes)]), variables: [{ referenceKey: "total", name: "Total", type, storageDriverKey: "elsa.json", initialValue: { syntax: "Literal", value: 9 } }], outputMappings: mappings, outcomeMappings: [{ sourceOutcomeReferenceKey: "Done", boundaryOutcomeReferenceKey: "done" }] } }, layout: [], activityPresentation: []
    });
    return { ...created, route: `/workflows/activity-definitions?backend=legacy&definition=${created.definition.definitionId}&section=editor&draft=${created.draft.draftId}` };
  }
  const views = () => page.getByRole("tablist", { name: "Activity Definition authoring views", exact: true });
  const source = (name: string) => page.getByRole("region", { name: `Value source for ${name}`, exact: true });
  const saved = () => expect(page.locator(".ad-save-chip")).toHaveText("Saved");
  async function selectOutput(name: string) {
    await views().getByRole("tab", { name: /^Outputs/ }).click();
    await page.locator('.ad-contract-workbench-list button[data-member-kind="output"]').filter({ has: page.getByText(name, { exact: true }) }).click();
    await expect(source(name)).toBeVisible();
  }
  const graph = await createGraph("sources", [output("sum", "Sum", true), output("free", "Free"), output("mapped", "Mapped")], [setter("first"), setter("second")], [{ outputReferenceKey: "mapped", source: { syntax: "Variable", value: { referenceKey: "total", declaringScopeId: "workflow" } } }]);
  await page.goto(graph.route); await selectOutput("Sum");
  await expect(source("Sum").getByText("Assigned by 2 Set Output steps in the designer.", { exact: true })).toBeVisible();
  await expect(page.getByText("Designer connections", { exact: true })).toHaveCount(0);
  const before = await api<ActivityDefinitionDraftView>(`/design/activities/drafts/${graph.draft.draftId}`);
  await source("Sum").getByRole("button", { name: "Change value source", exact: true }).click();
  await expect(source("Sum").getByRole("button", { name: "Choose value", exact: true })).toBeDisabled();
  expect((await api<ActivityDefinitionDraftView>(`/design/activities/drafts/${graph.draft.draftId}`)).provider.payload).toEqual(before.provider.payload);
  await capture(page, info, "multiple-setters-safe-source-change");
  for (const id of ["first", "second"]) {
    await source("Sum").getByRole("button", { name: /^Go to step/ }).first().press("Enter");
    await expect(page.locator(`.wf-sequence-step[data-id="${id}"]`)).toHaveAttribute("aria-pressed", "true");
    await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toHaveValue("Sum");
    await capture(page, info, "nested-" + id);
    await page.getByRole("combobox", { name: "Output Name", exact: true }).selectOption("Free"); await saved(); await selectOutput("Sum");
  }
  await source("Sum").getByRole("button", { name: "Choose value", exact: true }).click();
  await source("Sum").getByRole("combobox", { name: "Input reference", exact: true }).selectOption("a"); await saved();
  await expect(source("Sum").getByText("Input", { exact: true })).toBeVisible();
  await selectOutput("Mapped");
  await expect(source("Mapped").getByRole("combobox", { name: "Variable reference", exact: true })).toHaveValue("workflow::total");
  await expect(source("Mapped").getByRole("combobox", { name: "Variable reference", exact: true }).locator('option[value="workflow::total"]')).toBeEnabled();
  await capture(page, info, "mapped-private-variable");
  await selectOutput("Sum");
  const member = page.locator('[data-contract-kind="output"][data-contract-selected="true"]');
  await member.locator("details").filter({ has: page.getByText("Presentation", { exact: true }) }).locator("summary").press("Enter");
  await expect(member.getByRole("textbox", { name: "Display name", exact: true })).toBeVisible();
  await member.locator("details").filter({ has: page.getByText("Advanced", { exact: true }) }).locator("summary").press("Enter");
  await expect(member.getByRole("textbox", { name: "Stable reference key", exact: true })).toBeVisible();
  await capture(page, info, "technical-disclosures");
  await page.setViewportSize({ width: 360, height: 1000 }); await source("Sum").scrollIntoViewIfNeeded(); await capture(page, info, "mapped-narrow");
  await selectOutput("Free"); await source("Free").getByRole("button", { name: /^Go to step/ }).first().press("Enter");
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toHaveValue("Free"); await capture(page, info, "nested-step-narrow");
  await page.setViewportSize({ width: 1440, height: 1000 });

  const conflict = await createGraph("conflict", [output("alpha", "Alpha"), output("beta", "Beta", true)], [setter("conflicted", "Beta")], [{ outputReferenceKey: "beta", source: { syntax: "Variable", value: { referenceKey: "total", declaringScopeId: "workflow" } } }]);
  await page.goto(conflict.route); await selectOutput("Alpha");
  const rejected = page.waitForResponse(response => response.url().endsWith("/publication-preflight") && response.request().method() === "POST");
  await page.getByRole("button", { name: "Review & publish", exact: true }).click();
  expect((await (await rejected).json()).isPublishable).toBe(false);
  const review = page.getByRole("dialog", { name: "Review & publish", exact: true });
  await review.getByRole("button", { name: "Go to setting", exact: true }).first().click();
  await expect(source("Beta")).toBeVisible();
  await expect(source("Beta").getByText(/Remove this connection or the assignment before publishing/)).toBeVisible();
  await expect(source("Beta").getByRole("combobox", { name: "Variable reference", exact: true })).toBeDisabled(); await capture(page, info, "conflict-targeted-beta");
  await source("Beta").getByRole("button", { name: "Remove connection", exact: true }).click(); await saved();
  await expect(source("Beta").getByRole("button", { name: "Go to step", exact: true })).toBeVisible();
  await member.getByRole("textbox", { name: "Name", exact: true }).fill("RenamedBeta"); await member.getByRole("textbox", { name: "Name", exact: true }).press("Tab"); await saved();
  await expect(source("RenamedBeta").getByText(/removed, renamed or missing output/)).toBeVisible();
  await source("RenamedBeta").getByRole("button", { name: "Go to step", exact: true }).click();
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toHaveAttribute("aria-invalid", "true");
  await page.getByRole("combobox", { name: "Output Name", exact: true }).selectOption("RenamedBeta"); await saved(); await selectOutput("RenamedBeta");
  await capture(page, info, "renamed-repaired");
  await member.getByRole("button", { name: "Remove output", exact: true }).click(); await saved();
  await expect(source("Alpha").getByText(/removed, renamed or missing output/)).toBeVisible();
  const retained = await api<ActivityDefinitionDraftView>(`/design/activities/drafts/${conflict.draft.draftId}`);
  expect(JSON.stringify(retained.provider.payload)).toContain("conflicted"); await capture(page, info, "removed-declaration-preserved-step");

  const ready = await api<ActivityDefinitionDraftView>(`/design/activities/drafts/${graph.draft.draftId}`);
  const preflight = await api<{ isPublishable: boolean; minimumVersion: string; reviewToken: string; definitionHeadVersionId: string | null; diagnostics: unknown[] }>(`/design/activities/drafts/${ready.draftId}/publication-preflight`, "POST", { expectedDraftRevision: ready.revision, expectedDefinitionHeadVersionId: null });
  expect(preflight.isPublishable, JSON.stringify(preflight.diagnostics)).toBe(true);
  const published = await api<{ outcome: { definitionVersionId: string } }>(`/design/activities/drafts/${ready.draftId}/publish`, "POST", { expectedDraftRevision: ready.revision, expectedDefinitionHeadVersionId: null, version: preflight.minimumVersion, reviewToken: preflight.reviewToken, idempotencyKey: crypto.randomUUID() });
  // Exercise the exact graph just authored: Input mapping plus multiple nested Set Output assignments.
  const variable = { referenceKey: "captured", declaringScopeId: "workflow" };
  const caller = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
    name: `Output editor caller ${mode} ${Date.now()}`,
    initialState: { variables: [{ referenceKey: "captured", name: "Captured", type, storageDriverType: "elsa.json", isNullable: false }], inputs: [], outputs: [{ referenceKey: "caller-sum", name: "CallerSum", type, isNullable: false }], rootActivity: sequenceNode("caller-root", [
      { nodeId: "call", activityVersionId: published.outcome.definitionVersionId, inputs: [literal("a", 19.25), literal("b", 23.25)], outputs: [{ referenceKey: "sum", value: { expressionType: "Variable", value: variable } }] },
      { ...setter("caller-output", "CallerSum"), inputs: [literal("name", "CallerSum"), { referenceKey: "value", value: { expressionType: "Variable", value: variable } }] }
    ]) }, layout: [], activityPresentation: []
  });
  const promoted = await api<{ id: string }>(`/design/workflows/drafts/${caller.draft.id}/promote`, "POST", { operationKey: crypto.randomUUID() });
  const executable = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
  const started = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${executable.artifactId}/execute`, "POST", { sourceReferenceId: executable.sourceReferenceId });
  type Run = { instance: { status: string; blockingIncidentCount: number }; outputs: Record<string, { value: { value: number } }> };
  let completed!: Run;
  await expect.poll(async () => { completed = await api<Run>(`/runtime/workflows/instances/${started.workflowExecutionId}`); expect(completed.instance.blockingIncidentCount).toBe(0); return completed.instance.status; }, { timeout: 30_000 }).toBe("Completed");
  expect(Object.keys(completed.outputs)).toEqual(["CallerSum"]); expect(completed.outputs.CallerSum.value.value).toBe(19.25);
  await page.goto(graph.route); await selectOutput("Sum");
  await expect(member.getByRole("textbox", { name: "Name", exact: true })).toBeDisabled();
  await expect(source("Sum").getByRole("button", { name: "Change value source", exact: true })).toBeDisabled();
  await page.setViewportSize({ width: 360, height: 1000 }); await source("Sum").scrollIntoViewIfNeeded(); await capture(page, info, "readonly-narrow");
  await selectOutput("Free"); await source("Free").getByRole("button", { name: /^Go to step/ }).first().press("Enter");
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toBeDisabled();
  await expect(page.getByRole("combobox", { name: "Output Name", exact: true })).toHaveValue("Free");
  expect(errors).toEqual([]);
  const proof = { sourceSha: process.env.OUTPUT_EDITOR_SOURCE_SHA, graph, conflict, published, caller, started, completed, saved: ready, pageErrors: errors };
  const path = info.outputPath("output-editor-proof.json"); writeFileSync(path, JSON.stringify(proof, null, 2)); await info.attach("output-editor-proof", { path, contentType: "application/json" });
});
