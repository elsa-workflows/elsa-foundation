import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { expect, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Member = { referenceKey: string; name: string; type: { alias: string }; isRequired: boolean; default: { syntax: string; value: unknown } | null };
type Draft = { draftId: string; definitionId: string; revision: number; contract: { inputs: Member[] } };
type Node = Record<string, unknown> & { nodeId: string };

/** Real composed authoring, admission and execution. Only the explicitly recorded legacy seed writes an old owned draft. */
export async function nullPoliciesJourney(page: Page, info: TestInfo, capture: Capture, registerWorkflow: (id: string) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL!;
  const proof: Record<string, unknown> = { sourceSha: process.env.UX_SOURCE_SHA, project: info.project.name, cases: [] };
  const requests: { url: string; body: unknown }[] = [];
  page.on("request", request => {
    if (request.method() === "POST" && request.url().endsWith("/test-runs")) requests.push({ url: request.url(), body: request.postDataJSON() });
  });
  async function api<T>(path: string, method = "GET", data?: unknown): Promise<T> {
    const response = await page.request.fetch(`${backend}${path}`, { method, data });
    const text = await response.text();
    expect(response.ok(), `${method} ${path}: ${response.status()} ${text}`).toBe(true);
    return text ? JSON.parse(text) as T : undefined as T;
  }
  async function completed(id: string) {
    const path = `/runtime/workflows/instances/${id}`;
    await expect.poll(async () => (await api<{ instance: { status: string } }>(path)).instance.status, { timeout: 30_000 }).toBe("Completed");
    const detail = await api<{ instance: { blockingIncidentCount?: number }; activities: unknown[] }>(path);
    expect(detail.instance.blockingIncidentCount ?? 0).toBe(0);
    expect(detail.activities.length).toBeGreaterThan(0);
    return detail;
  }
  async function version(key: string) {
    const result = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    return result.items.find(item => item.definition.activityTypeKey === key)!.definition.headVersionId;
  }
  const sequence = await version("Elsa.Activities.Sequence.Activities.Sequence");
  const write = await version("Elsa.Activities.Primitives.Activities.WriteLine");
  const script = await version("Elsa.Activities.Scripting.Activities.RunJavaScript");
  const publishEvent = await version("Elsa.Activities.Primitives.Activities.PublishEvent");
  const sequenceContract = await api<{ contract: { outcomes: { referenceKey: string; name: string; isEmitted: boolean }[] } }>(`/design/activities/versions/${sequence}`);
  const done = sequenceContract.contract.outcomes.find(item => item.isEmitted && item.name.toLowerCase() === "done")!;
  const type = (alias: string) => ({ alias, collectionKind: "Single" });
  const literal = (referenceKey: string, value: unknown) => ({ referenceKey, value: { expressionType: "Literal", value } });
  const root = (activities: Node[]): Node => ({ nodeId: "root", activityVersionId: sequence, inputs: [], outputs: [], structure: { kind: "elsa.sequence.structure", schemaVersion: "1.0.0", payload: { activities, variables: [] } } });
  const line = (): Node => ({ nodeId: "line", activityVersionId: write, inputs: [literal("text", "S-2 acceptance")], outputs: [] });
  const created = await api<{ definition: { definitionId: string }; draft: Draft }>("/design/activities/definitions", "POST", {
    displayName: `Null contracts ${info.project.name} ${Date.now()}`, category: "Qualification",
    contract: { contractSchemaVersion: "1", inputs: [
      { referenceKey: "note", name: "note", displayName: "Note", type: type("String"), isRequired: true, storageDriverKey: "elsa.json" },
      { referenceKey: "count", name: "count", displayName: "Count", type: type("Int32"), isRequired: false, storageDriverKey: "elsa.json" }
    ], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: root([line()]), variables: [], outputMappings: [], outcomeMappings: [{ sourceOutcomeReferenceKey: done.referenceKey, boundaryOutcomeReferenceKey: "done" }] } }, layout: [], activityPresentation: []
  });
  const activityRoute = `/workflows/activity-definitions?definition=${created.definition.definitionId}&section=editor&draft=${created.draft.draftId}`;
  const saved = () => api<Draft>(`/design/activities/drafts/${created.draft.draftId}`);
  const note = async () => (await saved()).contract.inputs.find(member => member.referenceKey === "note")!;
  const row = () => page.locator('[data-contract-row-kind="input"][data-contract-reference-key="note"]');
  const required = () => row().locator('[data-contract-field="isRequired"]');
  const views = () => page.getByRole("tablist", { name: "Activity Definition authoring views" });
  async function inputs() {
    await views().getByRole("tab", { name: /^Inputs/ }).click();
    const toggle = row().locator('[data-member-reference-key="note"]');
    if (await toggle.getAttribute("aria-expanded") !== "true") await toggle.click();
    await row().scrollIntoViewIfNeeded();
  }
  async function chooseDefault(value: string) {
    const control = page.getByRole("combobox", { name: "Default", exact: true });
    await control.focus();
    await control.selectOption(value);
  }
  async function apply() {
    const button = page.getByRole("button", { name: "Apply default", exact: true });
    await button.focus(); await page.keyboard.press("Enter");
  }
  try {
    await page.goto(activityRoute); await inputs();
    await expect(required()).toBeChecked(); await expect(required()).toBeEnabled();
    await expect(page.getByText("Allow null", { exact: true })).toHaveCount(0);
    await capture(page, info, "reference");
    await chooseDefault("literal");
    const value = page.getByRole("textbox", { name: "Value", exact: true });
    await value.focus(); await page.keyboard.type("saved default"); await apply();
    await expect.poll(async () => (await note()).default?.value).toBe("saved default");
    await expect(required()).not.toBeChecked(); await expect(required()).toBeDisabled();
    await expect(page.getByText("A member with a default is optional", { exact: true })).toBeVisible();
    await capture(page, info, "contract-default");
    await page.reload(); await inputs();
    await expect(required()).not.toBeChecked(); await expect(required()).toBeDisabled();
    await expect(value).toHaveValue("saved default"); await capture(page, info, "contract-reopened");

    // This is a real old stored contract, in the disposable owned database; no HTTP response or UI is substituted.
    proof.legacySeed = JSON.parse(execFileSync("python3", [resolve("../tools/ux-acceptance/seed_s2_legacy_contract.py"), process.env.UX_ACCEPTANCE_OWNED_ROOT!, created.draft.draftId, "note"], { encoding: "utf8" }));
    await page.reload(); await inputs();
    await expect(required()).toBeChecked(); await expect(required()).toBeDisabled();
    const recover = page.getByRole("button", { name: "Make optional", exact: true });
    await recover.focus(); await page.keyboard.press("Enter");
    await expect.poll(async () => (await note()).isRequired).toBe(false);
    expect((await note()).default?.value).toBe("saved default");
    await capture(page, info, "contract-recovery");
    await chooseDefault("none"); await apply(); await expect.poll(async () => (await note()).default).toBeNull();
    await expect(required()).toBeEnabled(); await required().focus(); await page.keyboard.press("Space");
    await expect.poll(async () => (await note()).isRequired).toBe(true);
    // A local candidate is cancelled by restoring No default, without applying it.
    await chooseDefault("literal"); await value.fill("not saved"); await chooseDefault("none");
    expect((await note()).default).toBeNull();
    await expect(page.getByRole("combobox", { name: "Default", exact: true })).toBeFocused();
    await capture(page, info, "contract-required");

    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    const dialog = page.getByRole("dialog");
    const runTest = dialog.getByRole("button", { name: "Run test", exact: true });
    await expect(runTest).toBeEnabled(); const beforeActivity = requests.length;
    await runTest.click(); await expect(dialog.getByRole("alert").filter({ hasText: "Note is required" })).toBeVisible();
    expect(requests.length).toBe(beforeActivity);
    await dialog.getByRole("combobox", { name: "Note", exact: true }).selectOption("Null");
    await expect(dialog.getByRole("combobox", { name: "Count", exact: true }).locator('option[value="Null"]')).toBeDisabled();
    await capture(page, info, "activity-test-inputs");
    await runTest.click(); await expect(dialog.getByRole("region", { name: "Test run result", exact: true })).toContainText("Completed", { timeout: 30_000 });
    proof.activityTestRequest = requests.at(-1);
    await dialog.getByRole("button", { name: "Close", exact: true }).first().click();

    // A type edit must persist and change the reopened test-drive contract.
    const countRow = () => page.locator('[data-contract-row-kind="input"][data-contract-reference-key="count"]');
    await countRow().locator('[data-contract-field="type.alias"]').selectOption("String");
    await expect.poll(async () => (await saved()).contract.inputs.find(member => member.referenceKey === "count")!.type.alias).toBe("String");
    await page.reload(); await inputs();
    await expect(countRow().locator('[data-contract-field="type.alias"]')).toHaveValue("String");
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await dialog.getByRole("combobox", { name: "Note", exact: true }).selectOption("Null");
    const changedCount = dialog.getByRole("combobox", { name: "Count", exact: true });
    await expect(changedCount.locator('option[value="Null"]')).toBeEnabled(); await changedCount.selectOption("Null");
    await capture(page, info, "contract-type-nullable");
    await runTest.click(); await expect(dialog.getByRole("region", { name: "Test run result", exact: true })).toContainText("Completed", { timeout: 30_000 });
    proof.changedTypeRequest = requests.at(-1);
    await dialog.getByRole("button", { name: "Close", exact: true }).first().click();
    await countRow().locator('[data-contract-field="type.alias"]').selectOption("Int32");
    await expect.poll(async () => (await saved()).contract.inputs.find(member => member.referenceKey === "count")!.type.alias).toBe("Int32");
    await page.reload(); await inputs();
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    await expect(dialog.getByRole("combobox", { name: "Count", exact: true }).locator('option[value="Null"]')).toBeDisabled();
    await capture(page, info, "contract-type-nonnullable");
    await dialog.getByRole("button", { name: "Close", exact: true }).first().click();

    const workflow = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
      name: `Null admission ${info.project.name} ${Date.now()}`, state: { variables: [], inputs: [
        { referenceKey: "topic", name: "topic", displayName: "Topic", type: type("String"), isRequired: true },
        { referenceKey: "count", name: "count", displayName: "Count", type: type("Int32"), isRequired: false },
        { referenceKey: "memo", name: "memo", displayName: "Memo", type: type("String"), isRequired: false }
      ], outputs: [], rootActivity: root([line()]) }, layout: [], activityPresentation: []
    });
    registerWorkflow(workflow.definition.id);
    await page.goto(`/workflows/definitions?definition=${workflow.definition.id}&section=editor&draft=${workflow.draft.id}`);
    await page.getByRole("button", { name: "Test Run", exact: true }).click();
    const start = page.getByRole("button", { name: "Start Test Run", exact: true });
    await expect(start).toBeEnabled(); const beforeWorkflow = requests.length;
    await start.click(); await expect(page.getByRole("alert").filter({ hasText: "Topic is required" })).toBeVisible();
    expect(requests.length).toBe(beforeWorkflow);
    await page.getByRole("button", { name: "Send null for Topic", exact: true }).focus(); await page.keyboard.press("Enter");
    await expect(page.getByRole("alert").filter({ hasText: "Topic is required" })).toHaveCount(0);
    await expect(page.getByRole("textbox", { name: "Topic", exact: true })).toHaveValue("null");
    await expect(page.getByRole("button", { name: "Send null for Count", exact: true })).toHaveCount(0);
    await capture(page, info, "workflow-test-inputs");
    const testResponse = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/test-runs"));
    await start.click(); const tested = await testResponse;
    expect(tested.ok(), await tested.text()).toBe(true);
    expect(tested.request().postDataJSON().inputs).toEqual({ topic: null });
    let testRun = await tested.json() as { testRunId: string; workflowExecutionId?: string };
    await expect.poll(async () => {
      testRun = await api<typeof testRun>(`/publishing/workflow-test-runs/${testRun.testRunId}`);
      return Boolean(testRun.workflowExecutionId);
    }, { timeout: 30_000 }).toBe(true);
    proof.workflowTest = { request: tested.request().postDataJSON(), testRun, completed: await completed(testRun.workflowExecutionId!) };

    const promoted = await api<{ id: string }>(`/design/workflows/drafts/${workflow.draft.id}/promote`, "POST", { operationKey: crypto.randomUUID() });
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${promoted.id}/publish`, "POST", {});
    // Deliberate isolated transport failure, followed by a real owned-server recovery.
    // This qualifies metadata loading/refusal UX only; successful admission below uses real responses.
    await page.goto(`/workflows/executables/${published.artifactId}?ref=${encodeURIComponent(published.sourceReferenceId)}`);
    await expect(page.getByRole("button", { name: "Run", exact: true })).toBeEnabled();
    let releaseMetadata!: () => void;
    const metadataGate = new Promise<void>(resolve => { releaseMetadata = resolve; });
    const metadataUrl = (url: URL) => url.pathname.endsWith(`/runtime/workflows/executables/${published.artifactId}`);
    await page.route(metadataUrl, async route => { await metadataGate; await route.abort("failed"); });
    const heldMetadata = page.waitForRequest(request => request.method() === "GET" && metadataUrl(new URL(request.url())));
    await page.getByRole("button", { name: "Run", exact: true }).click(); await heldMetadata;
    await expect(page.getByRole("dialog", { name: "Run workflow", exact: true })).toHaveCount(0);
    await capture(page, info, "published-metadata-loading");
    releaseMetadata(); await expect(page.getByRole("heading", { name: "The executable could not be run", exact: true })).toBeVisible();
    await capture(page, info, "published-metadata-refused"); await page.unroute(metadataUrl);
    proof.metadataTransportControl = { isolatedAbort: true, noRunDialogBeforeContract: true, recoveryUsesRealServer: true };
    const runCases: unknown[] = [];
    for (const memo of [null, "null", ""]) {
      await page.goto(`/workflows/executables/${published.artifactId}?ref=${encodeURIComponent(published.sourceReferenceId)}`);
      await page.getByRole("button", { name: "Run", exact: true }).click();
      await expect(page.getByRole("dialog", { name: "Run workflow", exact: true })).toBeVisible();
      await page.getByRole("button", { name: "Send null for Topic", exact: true }).click();
      await page.getByRole("button", { name: "Send null for Memo", exact: true }).click();
      await page.getByRole("button", { name: "Omit for Memo", exact: true }).click();
      if (memo === null) await page.getByRole("button", { name: "Send null for Memo", exact: true }).click();
      else {
        const field = page.getByRole("textbox", { name: "Memo", exact: true });
        await field.focus(); await field.fill(memo);
      }
      if (memo === null) await capture(page, info, "published-run-inputs");
      const response = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith(`/runtime/workflows/executables/${published.artifactId}/execute`));
      await page.getByRole("button", { name: "Run workflow", exact: true }).click();
      const admitted = await response; expect(admitted.ok(), await admitted.text()).toBe(true);
      expect(admitted.request().postDataJSON().inputs).toEqual({ topic: null, memo });
      const started = await admitted.json() as { workflowExecutionId: string };
      runCases.push({ memo, request: admitted.request().postDataJSON(), started, completed: await completed(started.workflowExecutionId) });
    }
    proof.publishedRunCases = runCases;
    const refusal = await page.request.post(`${backend}/runtime/workflows/executables/${published.artifactId}/execute`, { data: { sourceReferenceId: published.sourceReferenceId, inputs: { topic: null, count: null } } });
    expect(refusal.status()).toBe(400);
    proof.nativeNullRefusal = { status: refusal.status(), body: await refusal.json(), clientRefusalRequests: { activityBefore: beforeActivity, workflowBefore: beforeWorkflow }, noClientRequestAssertionsPassed: true };

    const body = "void args.order; return { customer: { privateKey: undefined }, n: NaN, items: [1, , undefined] };";
    const js = (id: string, protectedInput: boolean): Node => ({ nodeId: id, activityVersionId: script, inputs: [literal("script", body), { ...literal("arguments", { order: {} }), isSensitive: protectedInput }], outputs: [] });
    const diagnostics = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
      name: `Null diagnostics ${info.project.name} ${Date.now()}`, state: { variables: [{ referenceKey: "count", name: "count", type: type("Int32") }], inputs: [], outputs: [],
        strategyOptions: { nullHandling: { defaultOnNull: true, undefinedAsNull: false } },
        rootActivity: root([{ nodeId: "substitute-input", activityVersionId: publishEvent, inputs: [literal("EventName", "s2-no-subscriber"), literal("IsLocalEvent", null)], outputs: [] }, { nodeId: "substitute", activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueType: type("Any"), variable: { referenceKey: "count", declaringScopeId: "workflow" } }, inputs: [literal("value", null)], outputs: [] }, js("boundary", false), js("protected-boundary", true)]) }, layout: [], activityPresentation: []
    });
    registerWorkflow(diagnostics.definition.id);
    const diagVersion = await api<{ id: string }>(`/design/workflows/drafts/${diagnostics.draft.id}/promote`, "POST", { operationKey: crypto.randomUUID() });
    const diagPublication = await api<{ artifactId: string; sourceReferenceId: string }>(`/publishing/workflows/${diagVersion.id}/publish`, "POST", {});
    const run = await api<{ workflowExecutionId: string }>(`/runtime/workflows/executables/${diagPublication.artifactId}/execute`, "POST", { sourceReferenceId: diagPublication.sourceReferenceId });
    const runtimePath = `/runtime/workflows/instances/${run.workflowExecutionId}`;
    await expect.poll(async () => (await api<{ instance: { status: string } }>(runtimePath)).instance.status, { timeout: 30_000 }).toBe("Completed");
    const detail = await api<{ activities: { authoredActivityId: string; activityExecutionId: string }[] }>(runtimePath);
    async function warnings(node: string) {
      const execution = detail.activities.find(item => item.authoredActivityId === node)!;
      expect(execution).toBeTruthy();
      await page.goto(`/workflows/instances/${run.workflowExecutionId}?selected=${encodeURIComponent(execution.activityExecutionId)}`);
      const inspector = page.getByRole("complementary", { name: "Run details panel", exact: true });
      await inspector.getByRole("tab", { name: "Activity", exact: true }).click();
      await inspector.getByRole("tablist", { name: "Activity execution views", exact: true }).getByRole("tab", { name: "Data", exact: true }).click();
      const section = inspector.getByTestId("execution-warnings"); await expect(section).toBeVisible(); await section.scrollIntoViewIfNeeded(); return section;
    }
    let section = await warnings("substitute-input"); await expect(section).toContainText("Information · VF-NULL-001"); await capture(page, info, "run-null-information");
    section = await warnings("substitute"); await expect(section).toContainText("VF-NULL-001"); await expect(section).toContainText("count");
    await capture(page, info, "run-variable-null-information");
    section = await warnings("boundary");
    for (const code of ["VF-UNDEF-002", "VF-UNDEF-003", "VF-NUM-001"]) await expect(section).toContainText(code);
    await expect(section).toContainText("customer.privateKey"); await capture(page, info, "run-boundary-warnings");
    section = await warnings("protected-boundary"); await expect(section).toContainText("VF-UNDEF-002"); await expect(section).not.toContainText("customer.privateKey");
    await capture(page, info, "run-protected-warning"); proof.diagnostics = { publication: diagPublication, run, inspectedNodes: ["substitute-input", "substitute", "boundary", "protected-boundary"] };

    const activity = await saved();
    const preflight = await api<{ isPublishable: boolean; reviewToken: string; minimumVersion: string; definitionHeadVersionId: string | null; diagnostics: unknown[] }>(`/design/activities/drafts/${activity.draftId}/publication-preflight`, "POST", { expectedDraftRevision: activity.revision, expectedDefinitionHeadVersionId: null });
    expect(preflight.isPublishable, JSON.stringify(preflight.diagnostics)).toBe(true);
    await api(`/design/activities/drafts/${activity.draftId}/publish`, "POST", { expectedDraftRevision: activity.revision, expectedDefinitionHeadVersionId: preflight.definitionHeadVersionId, version: preflight.minimumVersion, reviewToken: preflight.reviewToken, idempotencyKey: crypto.randomUUID() });
    await page.goto(activityRoute); await inputs(); await expect(required()).toBeDisabled(); await expect(page.getByRole("combobox", { name: "Default", exact: true })).toBeDisabled();
    await capture(page, info, "contract-readonly");
    proof.status = "passed";
  } finally {
    proof.observedTestRunRequests = requests;
    const path = info.outputPath("null-policies-proof.json"); writeFileSync(path, JSON.stringify(proof, null, 2));
    await info.attach("null-policies-proof.json", { path, contentType: "application/json" });
  }
}
