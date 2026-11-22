import { expect, type Page, type TestInfo } from "@playwright/test";
import { readFileSync } from "node:fs";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Definition = { definition: { id: string }; version: { id: string }; draftId: string };
type Step = { activityExecutionId: string; authoredActivityId: string };

/** Real native and Elsa 3 bodies through the source composition. No intercepted API or synthetic runtime records. */
export async function stepWorkflowStateJourney(page: Page, info: TestInfo, capture: Capture, cleanup: (work: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  const stamp = `${info.project.name}-${crypto.randomUUID().slice(0, 8)}`;
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${base}${path}`, { method, data, timeout: 30_000 });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  await api("/_elsa/identity/login", "POST", { username: "admin", password: "Password123!" });
  async function version(type: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(type)}`);
    const found = result.items.find(item => item.definition.activityTypeKey === type);
    expect(found, `Reconciled source activity ${type}`).toBeTruthy();
    return found!.definition.headVersionId;
  }
  const sequence = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const script = await version("Elsa.Activities.Scripting.Activities.RunJavaScript");
  const native = await version("Elsa.Workbench.Acceptance.StepStateAcceptanceWriter");
  const protectedNative = await version("Elsa.Workbench.Acceptance.StepStateAcceptanceProtectedWriter");
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const tracked: string[] = [];
  cleanup(async () => { for (const id of tracked) await api(`/design/workflows/definitions/${id}`, "DELETE"); });
  async function seed(name: string, activityVersionId: string, inputs: unknown[], initial?: { activityVersionId: string; inputs: unknown[] }): Promise<Definition> {
    const result = await api<Definition>("/design/workflows/definitions/submit", "POST", {
      name: `${name} ${stamp}`, state: { inputs: [], outputs: [], variables: [], rootActivity: {
        nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [],
        structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { variables: [], activities: [
          ...(initial ? [{ nodeId: "initial", ...initial, outputs: [] }] : []),
          { nodeId: "body", activityVersionId, inputs, outputs: [] }
        ] } }
      } }
    });
    tracked.push(result.definition.id);
    return result;
  }
  async function author(definitionId: string, nodeId = "body") {
    await page.goto(`/workflows/definitions?definition=${definitionId}&backend=legacy`);
    const node = page.locator(`.wf-sequence-step[data-id="${nodeId}"], .react-flow__node[data-id="${nodeId}"]`).first();
    await expect(node).toBeAttached();
    await node.press("Enter");
  }
  const inspector = () => page.getByRole("complementary", { name: "Run details panel", exact: true });
  async function run(versionId: string, expected: string, nodeId = "body") {
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${versionId}/publish`, "POST", {});
    const result = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${published.artifactId}/execute`, "POST", { sourceReferenceId: published.sourceReferenceId });
    const path = `/runtime/workflows/instances/${result.workflowExecutionId}`;
    await expect.poll(async () => (await api<{ instance: { status: string } }>(path)).instance.status, { timeout: 30_000 }).toBe(expected);
    const activities = await api<{ items: { execution: Step }[] }>(`${path}/activity-executions`);
    const step = activities.items.find(item => item.execution.authoredActivityId === nodeId)!.execution;
    await page.goto(`/workflows/instances/${result.workflowExecutionId}?selected=${step.activityExecutionId}`);
    await inspector().getByRole("tab", { name: "Activity", exact: true }).click();
    await expect(inspector()).toContainText(expected);
    return { path, step, details: await api<{ instance: { correlationId: string | null; instanceName: string | null }; outputs: Record<string, unknown> }>(path) };
  }
  const nativeDefinition = await seed("Native state", native, [literal("Mode", "complete")]);
  await author(nativeDefinition.definition.id);
  await expect(page.getByRole("note").filter({ hasText: "May change workflow outputs, correlation and instance name" })).toBeVisible();
  await capture(page, info, "native-effects");

  const knownSource = "workflowState.setOutput('Count',42); workflowState.setCorrelationId('order-42'); workflowState.setInstanceName('Ready for review'); return 1;";
  const authored = await seed("State script", script, [literal("script", knownSource)]);
  await author(authored.definition.id);
  const effect = () => page.getByRole("note").filter({ hasText: /May change: output Count/ });
  await expect(effect()).toBeVisible();
  await capture(page, info, "known-effects");
  const row = page.locator(".wf-property-row").filter({ has: page.locator(".wf-property-row-header > label").filter({ hasText: /^Script$/ }) });
  const editor = row.getByRole("textbox").first();
  async function edit(source: string) {
    const analysis = page.waitForResponse(response => response.request().method() === "POST" &&
      new URL(response.url()).pathname.endsWith("/scoped-variables/analyze") &&
      JSON.stringify(response.request().postDataJSON()).includes(JSON.stringify(source)));
    await editor.fill(source); await editor.press("Tab");
    expect((await analysis).ok()).toBe(true);
  }
  await edit("const name = 'Count'; workflowState.setOutput(name,42); return 1;");
  await expect(page.getByRole("note").filter({ hasText: "computed or indirect access" })).toBeVisible();
  await capture(page, info, "computed-effects");
  await edit("return 1;");
  await expect(effect()).toHaveCount(0);
  await expect(page.getByRole("note").filter({ hasText: "computed or indirect access" })).toHaveCount(0);
  await capture(page, info, "empty-effects");

  const completed = await run(nativeDefinition.version.id, "Completed");
  const committed = inspector().getByRole("region", { name: "Committed workflow changes", exact: true });
  await expect(committed).toContainText("Completion accepted");
  expect(completed.details.instance).toMatchObject({ correlationId: "order-42", instanceName: "Ready for review" });
  expect(completed.details.outputs).toHaveProperty("Count");
  await committed.scrollIntoViewIfNeeded();
  await capture(page, info, "native-completed");
  await inspector().getByRole("tab", { name: "Details", exact: true }).press("Enter");
  await expect(inspector()).toContainText("Ready for review");
  await expect(inspector()).toContainText("order-42");
  await expect(inspector().getByRole("region", { name: "Workflow outputs", exact: true })).toContainText("Count");
  await capture(page, info, "current-state");
  await inspector().getByRole("region", { name: "Workflow outputs", exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "current-outputs");
  await inspector().getByRole("tab", { name: "Timeline", exact: true }).press("Enter");
  await expect(inspector()).toContainText("Completion accepted");
  await capture(page, info, "accepted-timeline");
  const clearDefinition = await seed("Cleared state", native, [literal("Mode", "clear")], { activityVersionId: native, inputs: [literal("Mode", "complete")] });
  const cleared = await run(clearDefinition.version.id, "Completed");
  expect(cleared.details.instance.correlationId).toBeNull(); expect(cleared.details.instance.instanceName).toBeNull();
  await inspector().getByRole("tab", { name: "Details", exact: true }).press("Enter");
  await expect(inspector().locator("dl")).toContainText("Instance nameNone");
  await expect(inspector().locator("dl")).toContainText("CorrelationNone");
  await capture(page, info, "cleared-state");

  const source = JSON.parse(readFileSync(new URL("../../../Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/script-workflow-state.json", import.meta.url), "utf8"));
  source.id = `state-version-${stamp}`; source.definitionId = `state-${stamp}`; source.name = `Imported state ${stamp}`;
  // Nested same-body getters exercise the repaired overlapping edit ordering through actual import and execution.
  source.root.activities[0].script.expression.value = source.root.activities[0].script.expression.value.replace("return [", "setCorrelationId(getCorrelationId()); setWorkflowInstanceName(getWorkflowInstanceName()); return [");
  const route = "/migration/elsa3/reusable-activities";
  const uploaded = await api<{ collectionHandle: string }>(`${route}/collections`, "POST", [source]);
  const analysis = await api<{ planId: string }>(`${route}/collections/${uploaded.collectionHandle}/analysis?limit=100`);
  const selection = { planId: analysis.planId, selectedSourceVersionIds: [source.id] };
  await api(`${route}/collections/${uploaded.collectionHandle}/selection`, "POST", selection);
  const applied = await api<{ sources: { workflowVersionId: string; workflowDefinitionId: string }[] }>(`${route}/collections/${uploaded.collectionHandle}/apply`, "POST", { ...selection, idempotencyKey: `state-${stamp}` });
  const imported = applied.sources[0]; tracked.push(imported.workflowDefinitionId);
  await author(imported.workflowDefinitionId, "seed");
  await expect(page.getByRole("note").filter({ hasText: /May change: output Total/ })).toBeVisible();
  await capture(page, info, "imported-effects");
  const importedRun = await run(imported.workflowVersionId, "Completed", "seed");
  expect(importedRun.details.instance).toMatchObject({ correlationId: "new", instanceName: "Ready" });
  expect(importedRun.details.outputs).toHaveProperty("Total");
  await expect(inspector().getByRole("region", { name: "Committed workflow changes", exact: true })).toContainText("Completion accepted");
  await capture(page, info, "imported-completed");

  for (const [mode, status, name] of [["fault", "Faulted", "faulted"], ["cancel", "Cancelled", "cancelled"]]) {
    const definition = await seed(`State ${mode}`, native, [literal("Mode", mode)]);
    const failed = await run(definition.version.id, status);
    expect(failed.details.instance.correlationId).toBeNull(); expect(failed.details.instance.instanceName).toBeNull();
    expect(failed.details.outputs).not.toHaveProperty("Count");
    await expect(inspector().getByRole("region", { name: "Committed workflow changes", exact: true })).toHaveCount(0);
    await capture(page, info, name);
  }
  const secret = "s13-protected-sentinel";
  const protectedDefinition = await seed("Protected state", protectedNative, [literal("Secret", secret)]);
  const protectedRun = await run(protectedDefinition.version.id, "Faulted");
  expect(protectedRun.details.instance.correlationId).toBeNull();
  expect(protectedRun.details.outputs).not.toHaveProperty("ProtectedAttempt");
  await expect(inspector()).not.toContainText(secret);
  await expect(inspector().getByRole("region", { name: "Committed workflow changes", exact: true })).toHaveCount(0);
  await capture(page, info, "protected");
  await info.attach("state-contract.json", { body: JSON.stringify({ native: completed, imported: importedRun, protected: protectedRun }, null, 2), contentType: "application/json" });
}
