import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";
import { readFileSync } from "node:fs";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Definition = { definition: { id: string }; version: { id: string }; draftId: string };
type Step = { activityExecutionId: string; authoredActivityId: string };

/** Real native and Elsa 3 bodies through the source composition. No intercepted API or synthetic runtime records. */
export async function stepWorkflowStateJourney(page: Page, info: TestInfo, capture: Capture, cleanup: (work: () => Promise<void>) => void) {
  const base = process.env.ELSA_WORKBENCH_URL!;
  const signedInUrl = new URL(page.url());
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
    const editorUrl = new URL(signedInUrl);
    editorUrl.pathname = "/workflows/definitions";
    editorUrl.searchParams.set("definition", definitionId);
    await page.goto(editorUrl.toString());
    const node = page.locator(`.wf-sequence-step[data-id="${nodeId}"], .react-flow__node[data-id="${nodeId}"]`).first();
    await expect(node).toBeAttached();
    await node.press("Enter");
    const inspector = page.getByRole("complementary", { name: "Inspector panel", exact: true });
    const expand = inspector.getByRole("button", { name: "Expand inspector panel", exact: true });
    if (await expand.isVisible()) await expand.press("Enter");
  }
  const inspector = () => page.getByRole("complementary", { name: "Run details panel", exact: true });
  async function fullyVisibleText(locator: Locator) {
    await locator.scrollIntoViewIfNeeded();
    await expect.poll(() => locator.evaluate(element => {
      const bounds = element.getBoundingClientRect();
      const range = document.createRange(); range.selectNodeContents(element);
      const lines = [...range.getClientRects()];
      const clips: { bounds: DOMRect; x: boolean; y: boolean }[] = [];
      for (let ancestor: Element | null = element; ancestor; ancestor = ancestor.parentElement) {
        const style = getComputedStyle(ancestor);
        if (style.overflowX !== "visible" || style.overflowY !== "visible")
          clips.push({ bounds: ancestor.getBoundingClientRect(), x: style.overflowX !== "visible", y: style.overflowY !== "visible" });
      }
      // Manrope's visible ink extends above its line box. Check actual clipping,
      // while still requiring the full text to wrap within its available width.
      return lines.length > 0 && lines.every(line => line.left >= bounds.left - 1 && line.right <= bounds.right + 1 &&
        clips.every(clip => (!clip.x || line.left >= clip.bounds.left - 1 && line.right <= clip.bounds.right + 1) &&
          (!clip.y || line.top >= clip.bounds.top - 1 && line.bottom <= clip.bounds.bottom + 1)));
    }), { message: "The complete activity name or state summary must be readable without ellipsis" }).toBe(true);
  }
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
    await inspector().getByRole("tablist", { name: "Activity execution views", exact: true }).getByRole("tab", { name: "Data", exact: true }).press("Enter");
    await expect(inspector().locator(".wf-activity-outcomes")).toHaveAttribute("data-state", /^(recorded|empty)$/);
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
  const row = page.getByRole("group", { name: "Script property", exact: true });
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
  await inspector().getByText("Correlation", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "current-state");
  await inspector().getByRole("region", { name: "Workflow outputs", exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "current-outputs");
  await page.getByRole("navigation", { name: "Run workspace views", exact: true }).getByRole("button", { name: "Timeline", exact: true }).press("Enter");
  const timeline = page.getByRole("region", { name: "Run timeline", exact: true });
  await expect(timeline).toContainText("Completion accepted");
  await fullyVisibleText(timeline.getByText(/^Committed changes:/).first());
  await fullyVisibleText(timeline.getByText(/^Completion accepted/).first());
  await capture(page, info, "accepted-timeline");
  const clearDefinition = await seed("Cleared state", native, [literal("Mode", "clear")], { activityVersionId: native, inputs: [literal("Mode", "complete")] });
  const cleared = await run(clearDefinition.version.id, "Completed");
  expect(cleared.details.instance.correlationId).toBeNull(); expect(cleared.details.instance.instanceName).toBeNull();
  await inspector().getByRole("tab", { name: "Details", exact: true }).press("Enter");
  await expect(inspector().locator("dl")).toContainText("Instance nameNone");
  await expect(inspector().locator("dl")).toContainText("CorrelationNone");
  await inspector().getByText("Correlation", { exact: true }).scrollIntoViewIfNeeded();
  await capture(page, info, "cleared-state");

  const source = JSON.parse(readFileSync(new URL("../../../extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/script-workflow-state.json", import.meta.url), "utf8"));
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
  // Import materializes an immutable version. Inspect its exact body through a supported editable copy;
  // the runtime proof below still publishes and executes the original imported version.
  const importedVersion = await api<{ state: unknown }>(`/design/workflows/versions/${imported.workflowVersionId}`);
  const editableImport = await api<Definition>("/design/workflows/definitions/submit", "POST", { name: `Imported body draft ${stamp}`, state: importedVersion.state });
  tracked.push(editableImport.definition.id);
  const copiedVersion = await api<{ state: unknown }>(`/design/workflows/versions/${editableImport.version.id}`);
  expect(copiedVersion.state).toEqual(importedVersion.state);
  await info.attach("imported-body-provenance.json", { body: JSON.stringify({ original: imported, editableDefinitionId: editableImport.definition.id,
    editableVersionId: editableImport.version.id, state: importedVersion.state }, null, 2), contentType: "application/json" });
  await author(editableImport.definition.id, "seed");
  await expect(page.getByRole("note").filter({ hasText: /May change: output Total/ })).toBeVisible();
  await capture(page, info, "imported-effects");
  const importedRun = await run(imported.workflowVersionId, "Completed", "seed");
  expect(importedRun.details.instance).toMatchObject({ correlationId: "new", instanceName: "Ready" });
  expect(importedRun.details.outputs).toHaveProperty("Total");
  const importedCommitted = inspector().getByRole("region", { name: "Committed workflow changes", exact: true });
  await expect(importedCommitted).toContainText("Completion accepted");
  await fullyVisibleText(importedCommitted.getByText(/^Completion accepted/).first());
  await capture(page, info, "imported-completed");

  for (const [mode, status, name] of [["fault", "Faulted", "faulted"], ["cancel", "Cancelled", "cancelled"]]) {
    const definition = await seed(`State ${mode}`, native, [literal("Mode", mode)]);
    const failed = await run(definition.version.id, status);
    expect(failed.details.instance.correlationId).toBeNull(); expect(failed.details.instance.instanceName).toBeNull();
    expect(failed.details.outputs).not.toHaveProperty("Count");
    await expect(inspector().getByRole("region", { name: "Committed workflow changes", exact: true })).toHaveCount(0);
    await fullyVisibleText(inspector().getByRole("heading", { level: 4 }).first());
    await capture(page, info, name);
    if (mode === "fault") {
      const faultIncident = inspector().getByRole("region", { name: "Activity incidents", exact: true })
        .getByText("Review failed", { exact: true });
      await fullyVisibleText(faultIncident);
      await capture(page, info, "faulted-incidents");
    }
  }
  const secret = "s13-protected-sentinel";
  const protectedDefinition = await seed("Protected state", protectedNative, [literal("Secret", secret)]);
  const protectedRun = await run(protectedDefinition.version.id, "Faulted");
  expect(protectedRun.details.instance.correlationId).toBeNull();
  expect(protectedRun.details.outputs).not.toHaveProperty("ProtectedAttempt");
  await expect(inspector()).not.toContainText(secret);
  await expect(inspector().getByRole("region", { name: "Committed workflow changes", exact: true })).toHaveCount(0);
  await fullyVisibleText(inspector().getByRole("heading", { level: 4 }).first());
  await capture(page, info, "protected-state");
  const protectedIncident = inspector().getByRole("region", { name: "Activity incidents", exact: true })
    .getByText("VF-STATE-003: Protected sources cannot flow into public identity fields or output names.", { exact: true });
  await fullyVisibleText(protectedIncident);
  await capture(page, info, "protected");
  await info.attach("state-contract.json", { body: JSON.stringify({ native: completed, imported: importedRun, protected: protectedRun }, null, 2), contentType: "application/json" });
}
