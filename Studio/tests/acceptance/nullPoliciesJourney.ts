import { execFileSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { resolve } from "node:path";
import { expect, type Locator, type Page, type TestInfo } from "@playwright/test";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Member = { referenceKey: string; name: string; type: { alias: string }; isRequired: boolean; default: { syntax: string; value: unknown } | null };
type OutputMember = { referenceKey: string; name: string; type: { alias: string }; isRequired: boolean };
type Draft = { draftId: string; definitionId: string; revision: number; contract: { inputs: Member[]; outputs: OutputMember[] } };
type Node = Record<string, unknown> & { nodeId: string };

/** Real composed authoring, admission and execution. Only the explicitly recorded legacy seed writes an old owned draft. */
export async function nullPoliciesJourney(page: Page, info: TestInfo, capture: Capture, registerWorkflow: (id: string) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL!;
  const proof: Record<string, unknown> = { sourceSha: process.env.UX_SOURCE_SHA, project: info.project.name, cases: [] };
  const keyboardActions: string[] = [];
  proof.keyboardActions = keyboardActions;
  async function captureAt(target: Locator, name: string) {
    await target.scrollIntoViewIfNeeded();
    await expect(target).toBeInViewport({ ratio: 1 });
    await capture(page, info, name);
  }
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
  async function runWorkflowTest(expectedInputs: Record<string, unknown>) {
    const response = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith("/test-runs"));
    await activate(page.getByRole("button", { name: "Start Test Run", exact: true }), "Start workflow test run");
    const tested = await response; expect(tested.ok(), await tested.text()).toBe(true);
    expect(tested.request().postDataJSON().inputs ?? {}).toEqual(expectedInputs);
    let run = await tested.json() as { testRunId: string; workflowExecutionId?: string };
    await expect.poll(async () => {
      run = await api<typeof run>(`/publishing/workflow-test-runs/${run.testRunId}`);
      return Boolean(run.workflowExecutionId);
    }, { timeout: 30_000 }).toBe(true);
    return { request: tested.request().postDataJSON(), run, completed: await completed(run.workflowExecutionId!) };
  }
  async function runActivityTest(control: Locator, name: string) {
    const response = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.includes("/publishing/activity-") && new URL(response.url()).pathname.endsWith("/test-runs"));
    await activate(control, name);
    const started = await response; expect(started.ok(), await started.text()).toBe(true);
    const initial = await started.json() as { testRunId: string; workflowExecutionId: string };
    const execution = await completed(initial.workflowExecutionId);
    const receipt = await api<{ testRunId: string; workflowExecutionId: string; failure?: unknown }>(`/publishing/activity-test-runs/${initial.testRunId}`);
    expect(receipt.testRunId).toBe(initial.testRunId); expect(receipt.failure ?? null).toBeNull();
    const result = page.getByRole("dialog").getByRole("region", { name: "Test run result", exact: true });
    await expect.poll(async () => {
      if (await result.count() === 0) return null;
      const details = result.locator("details.ad-test-run-details");
      const open = await details.evaluateAll(items => items[0]?.hasAttribute("open") ?? false);
      if (!open) await activate(details.locator("summary"), "Open current activity run details");
      // A rerun remounts this disclosure. Keep opening only the closed current result until its exact ID arrives.
      return await result.locator(".ad-test-run-technical code").evaluateAll(items => items[0]?.getAttribute("title") ?? null);
    }, { timeout: 30_000 }).toBe(initial.testRunId);
    // Rerun history also contains earlier Completed labels; qualify the current result's heading alone.
    await expect(result.locator(".ad-test-run-result-heading .ad-test-run-stage")).toHaveText("Completed", { timeout: 30_000 });
    return { request: { url: started.url(), body: started.request().postDataJSON() }, receipt, completed: execution };
  }
  const definitionIds = new Map<string, string>();
  async function version(key: string) {
    const result = await api<{ items: { definition: { definitionId: string; activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const definition = result.items.find(item => item.definition.activityTypeKey === key)!.definition;
    definitionIds.set(key, definition.definitionId);
    return definition.headVersionId;
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
    ], outputs: [
      { referenceKey: "nullable-result", name: "nullableResult", displayName: "Nullable result", type: type("String"), isRequired: false, storageDriverKey: "elsa.json" },
      { referenceKey: "number-result", name: "numberResult", displayName: "Number result", type: type("Int32"), isRequired: false, storageDriverKey: "elsa.json" }
    ], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
    provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: root([line()]), variables: [], outputMappings: [
      { outputReferenceKey: "nullable-result", source: { syntax: "Literal", value: "ok" } },
      { outputReferenceKey: "number-result", source: { syntax: "Literal", value: 0 } }
    ], outcomeMappings: [{ sourceOutcomeReferenceKey: done.referenceKey, boundaryOutcomeReferenceKey: "done" }] } }, layout: [], activityPresentation: []
  });
  const activityRoute = `/workflows/activity-definitions?definition=${created.definition.definitionId}&section=editor&draft=${created.draft.draftId}`;
  const saved = () => api<Draft>(`/design/activities/drafts/${created.draft.draftId}`);
  const note = async () => (await saved()).contract.inputs.find(member => member.referenceKey === "note")!;
  const row = () => page.locator('[data-contract-row-kind="input"][data-contract-reference-key="note"]');
  const required = () => row().locator('[data-contract-field="isRequired"]');
  const views = () => page.getByRole("tablist", { name: "Activity Definition authoring views" });
  async function activate(control: Locator, name: string) {
    await expect(control).toBeEnabled(); await control.focus(); await expect(control).toBeFocused(); await page.keyboard.press("Enter");
    keyboardActions.push(`Enter: ${name}`);
  }
  async function selectWithKeyboard(control: Locator, value: string) {
    await expect(control).toBeVisible(); await expect(control).toBeEnabled();
    const options = await control.locator("option:not([disabled])").evaluateAll(items => items.map(item => (item as HTMLOptionElement).value));
    expect(options).toContain(value);
    await control.focus(); await page.keyboard.press("Home");
    for (let index = 0; index < options.indexOf(value); index++) await page.keyboard.press("ArrowDown");
    await expect(control).toHaveValue(value);
    keyboardActions.push(`Home/ArrowDown: ${value}`);
  }
  async function inputs() {
    await activate(views().getByRole("tab", { name: /^Inputs/ }), "Inputs tab");
    const toggle = row().locator('[data-member-reference-key="note"]');
    if (await toggle.getAttribute("aria-expanded") !== "true") await activate(toggle, "Expand Note");
    await row().scrollIntoViewIfNeeded();
  }
  async function chooseDefault(value: string) {
    const control = page.getByRole("combobox", { name: "Default", exact: true });
    await selectWithKeyboard(control, value);
  }
  async function apply() {
    const button = page.getByRole("button", { name: "Apply default", exact: true });
    await expect(button).toBeEnabled(); await page.keyboard.press("Tab"); await expect(button).toBeFocused(); await page.keyboard.press("Enter");
    keyboardActions.push("Tab to Apply default; Enter");
  }
  try {
    const emptyActivity = await api<{ definition: { definitionId: string }; draft: { draftId: string } }>("/design/activities/definitions", "POST", {
      displayName: `Empty null contract ${info.project.name} ${Date.now()}`, category: "Qualification",
      contract: { contractSchemaVersion: "1", inputs: [], outputs: [], outcomes: [{ referenceKey: "done", name: "Done", isEmitted: true }] },
      provider: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: { rootActivity: root([line()]), variables: [], outputMappings: [], outcomeMappings: [{ sourceOutcomeReferenceKey: done.referenceKey, boundaryOutcomeReferenceKey: "done" }] } }, layout: [], activityPresentation: []
    });
    await page.goto(`/workflows/activity-definitions?definition=${emptyActivity.definition.definitionId}&section=editor&draft=${emptyActivity.draft.draftId}`);
    await activate(views().getByRole("tab", { name: /^Inputs/ }), "Empty Inputs tab");
    await expect(page.locator('[data-contract-row-kind="input"]')).toHaveCount(0);
    await expect(page.getByText("No inputs defined.", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add input", exact: true })).toBeEnabled();
    await capture(page, info, "contract-empty-inputs");
    await activate(views().getByRole("tab", { name: /^Outputs/ }), "Empty Outputs tab");
    await expect(page.locator('[data-contract-row-kind="output"]')).toHaveCount(0);
    await expect(page.getByText("No outputs defined.", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: "Add output", exact: true })).toBeEnabled();
    await capture(page, info, "contract-empty-outputs");

    const emptyWorkflow = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
      name: `Empty null test ${info.project.name} ${Date.now()}`, initialState: { variables: [], inputs: [], outputs: [], rootActivity: root([line()]) }, layout: [], activityPresentation: []
    });
    registerWorkflow(emptyWorkflow.definition.id);
    await page.goto(`/workflows/definitions?definition=${emptyWorkflow.definition.id}&section=editor&draft=${emptyWorkflow.draft.id}`);
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Open empty Test Run");
    await expect(page.getByRole("button", { name: "Start Test Run", exact: true })).toBeEnabled();
    await capture(page, info, "workflow-empty-test");
    proof.emptyWorkflow = await runWorkflowTest({});

    await page.goto(activityRoute); await inputs();
    await expect(required()).toBeChecked(); await expect(required()).toBeEnabled();
    await expect(page.getByText("Allow null", { exact: true })).toHaveCount(0);
    await capture(page, info, "reference");
    await chooseDefault("literal");
    const value = page.getByRole("textbox", { name: "Value", exact: true });
    await page.keyboard.press("Tab"); await expect(value).toBeFocused();
    keyboardActions.push("Tab from Default to Value");
    await page.keyboard.type("saved default"); await apply();
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
    await captureAt(recover, "contract-legacy-recovery");
    await activate(recover, "Make optional");
    await expect.poll(async () => (await note()).isRequired).toBe(false);
    expect((await note()).default?.value).toBe("saved default");
    await capture(page, info, "contract-recovery");
    await chooseDefault("none"); await apply(); await expect.poll(async () => (await note()).default).toBeNull();
    await expect(required()).toBeEnabled(); await required().focus(); await page.keyboard.press("Space");
    await expect.poll(async () => (await note()).isRequired).toBe(true);
    // A local candidate is cancelled by restoring No default, without applying it.
    await chooseDefault("literal"); await page.keyboard.press("Tab"); await expect(value).toBeFocused();
    await page.keyboard.type("not saved"); await page.keyboard.press("Shift+Tab");
    await expect(page.getByRole("combobox", { name: "Default", exact: true })).toBeFocused();
    keyboardActions.push("Tab to Value; Shift+Tab to Default; cancel without applying");
    await chooseDefault("none");
    expect((await note()).default).toBeNull();
    await expect(page.getByRole("combobox", { name: "Default", exact: true })).toBeFocused();
    await capture(page, info, "contract-required");

    await activate(views().getByRole("tab", { name: /^Outputs/ }), "Activity Outputs tab");
    const nullableOutput = () => page.locator('[data-contract-row-kind="output"][data-contract-reference-key="nullable-result"]');
    await expect(nullableOutput().locator('[data-contract-field="type.alias"]')).toHaveValue("String");
    await expect(page.getByText("Allow null", { exact: true })).toHaveCount(0);
    await nullableOutput().locator('[data-contract-field="type.alias"]').selectOption("Any");
    await expect.poll(async () => (await saved()).contract.outputs.find(output => output.referenceKey === "nullable-result")?.type.alias).toBe("Any");
    await page.reload(); await activate(views().getByRole("tab", { name: /^Outputs/ }), "Reopen Activity Outputs");
    await expect(nullableOutput().locator('[data-contract-field="type.alias"]')).toHaveValue("Any");
    await nullableOutput().locator('[data-contract-field="type.alias"]').selectOption("String");
    await expect.poll(async () => (await saved()).contract.outputs.find(output => output.referenceKey === "nullable-result")?.type.alias).toBe("String");
    await page.reload(); await activate(views().getByRole("tab", { name: /^Outputs/ }), "Reopen restored Activity Outputs");
    await expect(nullableOutput().locator('[data-contract-field="type.alias"]')).toHaveValue("String");
    await expect(page.locator('[data-contract-row-kind="output"][data-contract-reference-key="number-result"]').locator('[data-contract-field="type.alias"]')).toHaveValue("Int32");
    await captureAt(nullableOutput().locator('[data-contract-field="type.alias"]'), "activity-outputs-reopened");
    await captureAt(page.locator('[data-contract-row-kind="output"][data-contract-reference-key="number-result"]').locator('[data-contract-field="type.alias"]'), "activity-number-output-reopened");
    await inputs();

    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Open Test Run");
    const dialog = page.getByRole("dialog");
    const runTest = dialog.getByRole("button", { name: "Run test", exact: true });
    await expect(runTest).toBeEnabled(); const beforeActivity = requests.length;
    await activate(runTest, "Run activity test"); await expect(dialog.getByRole("alert").filter({ hasText: "Note is required" })).toBeVisible();
    expect(requests.length).toBe(beforeActivity);
    await captureAt(dialog.getByRole("alert").filter({ hasText: "Note is required" }), "activity-required-refusal");
    await selectWithKeyboard(dialog.getByRole("combobox", { name: "Note", exact: true }), "Null");
    await expect(dialog.getByRole("combobox", { name: "Count", exact: true }).locator('option[value="Null"]')).toBeDisabled();
    await capture(page, info, "activity-test-inputs");
    const initialActivityRun = await runActivityTest(runTest, "Run activity test");
    proof.activityTestRequest = initialActivityRun.request; proof.activityTestCompletion = initialActivityRun;
    await activate(dialog.getByRole("button", { name: "Close", exact: true }).first(), "Close Test Run");

    // A type edit must persist and change the reopened test-drive contract.
    const countRow = () => page.locator('[data-contract-row-kind="input"][data-contract-reference-key="count"]');
    await countRow().locator('[data-contract-field="type.alias"]').selectOption("String");
    await expect.poll(async () => (await saved()).contract.inputs.find(member => member.referenceKey === "count")!.type.alias).toBe("String");
    await page.reload(); await inputs();
    await expect(countRow().locator('[data-contract-field="type.alias"]')).toHaveValue("String");
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Open Test Run");
    await selectWithKeyboard(dialog.getByRole("combobox", { name: "Note", exact: true }), "Null");
    const changedCount = dialog.getByRole("combobox", { name: "Count", exact: true });
    await expect(changedCount.locator('option[value="Null"]')).toBeEnabled();
    await expect(changedCount).toHaveValue("Absent");
    const nullableOmission = await runActivityTest(runTest, "Run nullable omission");
    proof.activityNullableOmission = nullableOmission;
    expect(nullableOmission.request.body).toMatchObject({ inputs: { count: { state: "Absent" }, note: { state: "Present", value: null } } });
    // A completed Activity Test Run displays result evidence instead of editable inputs. Reopen to enter another value.
    await activate(dialog.getByRole("button", { name: "Close", exact: true }).first(), "Close omission Test Run");
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Reopen nullable Test Run");
    await selectWithKeyboard(dialog.getByRole("combobox", { name: "Note", exact: true }), "Null");
    await selectWithKeyboard(changedCount, "Null");
    await capture(page, info, "contract-type-nullable");
    const changedActivityRun = await runActivityTest(runTest, "Run nullable value after reopening");
    expect(changedActivityRun.receipt.testRunId).not.toBe(nullableOmission.receipt.testRunId);
    proof.changedTypeRequest = changedActivityRun.request; proof.changedTypeCompletion = changedActivityRun;
    await activate(dialog.getByRole("button", { name: "Close", exact: true }).first(), "Close Test Run");
    await countRow().locator('[data-contract-field="type.alias"]').selectOption("Int32");
    await expect.poll(async () => (await saved()).contract.inputs.find(member => member.referenceKey === "count")!.type.alias).toBe("Int32");
    await page.reload(); await inputs();
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Open Test Run");
    await expect(dialog.getByRole("combobox", { name: "Count", exact: true }).locator('option[value="Null"]')).toBeDisabled();
    await capture(page, info, "contract-type-nonnullable");
    await activate(dialog.getByRole("button", { name: "Close", exact: true }).first(), "Close Test Run");

    const invalidActivityKey = crypto.randomUUID();
    const invalidActivity = await api<{ testRunId: string; status: string; artifactId: string | null; sourceReferenceId: string | null; commandDispatchStatus: string | null; expiration: { evidenceRetained: boolean }; failure: { code: string } }>(
      `/publishing/activity-drafts/${created.draft.draftId}/test-runs`, "POST", {
        expectedRevision: (await saved()).revision, idempotencyKey: invalidActivityKey,
        inputs: { note: { state: "Present", value: null }, count: { state: "Present", value: null } }
      });
    expect(invalidActivity).toMatchObject({ status: "ValidationRejected",
      expiration: { evidenceRetained: false }, failure: { code: "activity.test-run.preparation-failed" } });
    expect(invalidActivity.artifactId ?? null).toBeNull(); expect(invalidActivity.sourceReferenceId ?? null).toBeNull();
    expect(invalidActivity.commandDispatchStatus ?? null).toBeNull();
    const invalidReplay = await api<typeof invalidActivity>(`/publishing/activity-drafts/${created.draft.draftId}/test-runs/idempotency/${invalidActivityKey}`);
    expect(invalidReplay.testRunId).toBe(invalidActivity.testRunId); expect(invalidReplay.status).toBe("ValidationRejected");
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Reopen after API null refusal");
    await selectWithKeyboard(dialog.getByRole("combobox", { name: "Note", exact: true }), "Null");
    const correctedActivity = await runActivityTest(runTest, "Run corrected activity inputs");
    proof.activityNullRefusal = { receipt: invalidActivity, replay: invalidReplay, correction: correctedActivity };
    await activate(dialog.getByRole("button", { name: "Close", exact: true }).first(), "Close corrected Test Run");

    const workflow = await api<{ definition: { id: string }; draft: { id: string } }>("/design/workflows/definitions", "POST", {
      name: `Null admission ${info.project.name} ${Date.now()}`, initialState: { variables: [], inputs: [
        { referenceKey: "topic", name: "topic", displayName: "Topic", type: type("String"), isRequired: true },
        { referenceKey: "count", name: "count", displayName: "Count", type: type("Int32"), isRequired: false },
        { referenceKey: "memo", name: "memo", displayName: "Memo", type: type("String"), isRequired: false },
        { referenceKey: "payload", name: "payload", displayName: "Payload", type: type("Any"), isRequired: false },
        { referenceKey: "flag", name: "flag", displayName: "Flag", type: type("Boolean"), isRequired: false }
      ], outputs: [], rootActivity: root([line()]) }, layout: [], activityPresentation: []
    });
    registerWorkflow(workflow.definition.id);
    const admissionDraft = await api<{ state: { rootActivity: Node; inputs: { referenceKey: string }[] } }>(`/design/workflows/drafts/${workflow.draft.id}`);
    expect(admissionDraft.state.rootActivity.nodeId).toBe("root");
    expect(admissionDraft.state.inputs.map(input => input.referenceKey)).toEqual(["topic", "count", "memo", "payload", "flag"]);
    await page.goto(`/workflows/definitions?definition=${workflow.definition.id}&section=editor&draft=${workflow.draft.id}`);
    const workflowViews = () => page.getByRole("tablist", { name: "Editor view tabs", exact: true });
    const workflowSaved = () => api<{ state: { inputs: { referenceKey: string; name: string; type: { alias: string } }[]; outputs: { referenceKey: string; name: string; type: { alias: string } }[] } }>(`/design/workflows/drafts/${workflow.draft.id}`);
    const workflowInputRow = (name: string) => page.locator(".wf-properties-table tbody tr").filter({ has: page.locator(`input[aria-label="Input name"][value="${name}"]`) });
    async function workflowTab(name: "Inputs" | "Outputs") {
      await activate(workflowViews().getByRole("tab", { name, exact: true }), `Workflow ${name}`);
    }
    async function setWorkflowInputType(name: string, alias: string) {
      await workflowTab("Inputs");
      const field = workflowInputRow(name).getByRole("combobox", { name: "Input type", exact: true });
      await expect(field).toBeVisible(); await field.selectOption(alias);
      await expect.poll(async () => (await workflowSaved()).state.inputs.find(input => input.referenceKey === name)?.type.alias).toBe(alias);
    }
    await workflowTab("Outputs");
    const outputSave = page.waitForResponse(response => response.request().method() === "PUT" && response.url().includes("/design/workflows/drafts/") && (response.request().postDataJSON().state?.outputs?.length ?? 0) > 0);
    await activate(page.getByRole("button", { name: "Add output", exact: true }), "Add workflow output");
    const addedOutput = await outputSave; expect(addedOutput.ok()).toBe(true);
    expect(addedOutput.request().postDataJSON().state.outputs[0]).not.toHaveProperty("isNullable");
    await page.getByRole("textbox", { name: "Output name", exact: true }).fill("answer");
    await expect.poll(async () => (await workflowSaved()).state.outputs[0]?.name).toBe("answer");
    const outputType = () => page.getByRole("combobox", { name: "Output type", exact: true });
    await expect(outputType()).toHaveValue("String"); await outputType().selectOption("Int32");
    await expect.poll(async () => (await workflowSaved()).state.outputs[0]?.type.alias).toBe("Int32");
    await page.reload(); await workflowTab("Outputs");
    await expect(outputType()).toHaveValue("Int32"); await expect(page.getByText("Allow null", { exact: true })).toHaveCount(0);
    await capture(page, info, "workflow-outputs-reopened");
    await outputType().selectOption("String");
    await expect.poll(async () => (await workflowSaved()).state.outputs[0]?.type.alias).toBe("String");
    proof.workflowOutput = { addedRequest: addedOutput.request().postDataJSON(), restored: (await workflowSaved()).state.outputs[0] };
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Open Test Run");
    const start = page.getByRole("button", { name: "Start Test Run", exact: true });
    await expect(start).toBeEnabled(); const beforeWorkflow = requests.length;
    await activate(start, "Start workflow test run"); await expect(page.getByRole("alert").filter({ hasText: "Topic is required" })).toBeVisible();
    expect(requests.length).toBe(beforeWorkflow);
    await captureAt(page.getByRole("alert").filter({ hasText: "Topic is required" }), "workflow-required-refusal");
    await page.getByRole("button", { name: "Send null for Topic", exact: true }).focus(); await page.keyboard.press("Enter");
    await expect(page.getByRole("alert").filter({ hasText: "Topic is required" })).toHaveCount(0);
    await expect(page.getByRole("textbox", { name: "Topic", exact: true })).toHaveValue("null");
    await expect(page.getByRole("button", { name: "Send null for Count", exact: true })).toHaveCount(0);
    await capture(page, info, "workflow-test-inputs");
    await activate(page.getByRole("button", { name: "Send null for Payload", exact: true }), "Send null for Any");
    await expect(page.getByRole("textbox", { name: "Payload", exact: true })).toHaveValue("null");
    await expect(page.getByRole("button", { name: "Send null for Flag", exact: true })).toHaveCount(0);
    await selectWithKeyboard(page.getByRole("combobox", { name: "Flag", exact: true }), "false");
    await capture(page, info, "workflow-type-matrix");
    proof.workflowTest = await runWorkflowTest({ topic: null, payload: null, flag: false });

    await activate(page.getByRole("button", { name: "Return to editing", exact: true }), "Return to workflow editing");
    await setWorkflowInputType("count", "String");
    await page.reload(); await workflowTab("Inputs");
    await expect(workflowInputRow("count").getByRole("combobox", { name: "Input type", exact: true })).toHaveValue("String");
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Reopen changed workflow Test Run");
    await activate(page.getByRole("button", { name: "Send null for Topic", exact: true }), "Send null for Topic");
    await activate(page.getByRole("button", { name: "Send null for Count", exact: true }), "Send null for changed Count");
    await activate(page.getByRole("button", { name: "Send null for Payload", exact: true }), "Send null for Any");
    await selectWithKeyboard(page.getByRole("combobox", { name: "Flag", exact: true }), "false");
    await captureAt(page.getByRole("textbox", { name: "Count", exact: true }), "workflow-type-nullable");
    const changedWorkflow = await runWorkflowTest({ topic: null, count: null, payload: null, flag: false });
    await activate(page.getByRole("button", { name: "Return to editing", exact: true }), "Return to workflow editing");
    await setWorkflowInputType("count", "Int32");
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Reopen nonnullable Test Run");
    await activate(page.getByRole("button", { name: "Refresh setup", exact: true }), "Refresh pinned input setup");
    await expect(page.getByRole("button", { name: "Send null for Count", exact: true })).toHaveCount(0);
    const beforeTypeRefusal = requests.length;
    await activate(start, "Reject retained null for Int32");
    await expect(page.getByRole("alert").filter({ hasText: "Count cannot be null." })).toBeVisible();
    expect(requests.length).toBe(beforeTypeRefusal);
    await captureAt(page.getByRole("alert").filter({ hasText: "Count cannot be null." }), "workflow-type-nonnullable-refusal");
    await activate(page.getByRole("button", { name: "Omit for Count", exact: true }), "Correct Count by omission");
    const correctedCount = await runWorkflowTest({ topic: null, payload: null, flag: false });

    await activate(page.getByRole("button", { name: "Return to editing", exact: true }), "Return to workflow editing");
    await setWorkflowInputType("payload", "Boolean");
    await activate(page.getByRole("button", { name: "Test Run", exact: true }), "Reopen Boolean Test Run");
    await activate(page.getByRole("button", { name: "Refresh setup", exact: true }), "Refresh Boolean setup");
    await expect(page.getByRole("button", { name: "Send null for Payload", exact: true })).toHaveCount(0);
    const beforeBooleanRefusal = requests.length;
    await activate(start, "Reject retained null for Boolean");
    await expect(page.getByRole("alert").filter({ hasText: "Payload cannot be null." })).toBeVisible();
    expect(requests.length).toBe(beforeBooleanRefusal); await capture(page, info, "workflow-boolean-refusal");
    await activate(page.getByRole("button", { name: "Omit for Payload", exact: true }), "Correct Boolean by omission");
    const correctedBoolean = await runWorkflowTest({ topic: null, flag: false });
    await activate(page.getByRole("button", { name: "Return to editing", exact: true }), "Return to workflow editing");
    await setWorkflowInputType("payload", "Any");
    await page.reload(); await workflowTab("Inputs");
    await expect(workflowInputRow("count").getByRole("combobox", { name: "Input type", exact: true })).toHaveValue("Int32");
    await expect(workflowInputRow("payload").getByRole("combobox", { name: "Input type", exact: true })).toHaveValue("Any");
    await expect(page.getByText("Allow null", { exact: true })).toHaveCount(0);
    const persistedWorkflow = await workflowSaved();
    for (const member of [...persistedWorkflow.state.inputs, ...persistedWorkflow.state.outputs]) expect(member).not.toHaveProperty("isNullable");
    const collapseInspector = page.getByRole("button", { name: "Collapse inspector panel", exact: true });
    if (await collapseInspector.isVisible()) await activate(collapseInspector, "Close runtime pane before contract inspection");
    await captureAt(workflowInputRow("count").getByRole("combobox", { name: "Input type", exact: true }), "workflow-inputs-reopened");
    await captureAt(workflowInputRow("payload").getByRole("combobox", { name: "Input type", exact: true }), "workflow-any-input-reopened");
    await captureAt(workflowInputRow("flag").getByRole("combobox", { name: "Input type", exact: true }), "workflow-boolean-input-reopened");
    proof.workflowTypeRecovery = { changedWorkflow, beforeTypeRefusal, correctedCount, beforeBooleanRefusal, correctedBoolean, persistedWorkflow };

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
    await activate(page.getByRole("button", { name: "Run", exact: true }), "Open published Run"); await heldMetadata;
    await expect(page.getByRole("dialog", { name: "Run workflow", exact: true })).toHaveCount(0);
    await capture(page, info, "published-metadata-loading");
    releaseMetadata(); await expect(page.getByRole("heading", { name: "The executable could not be run", exact: true })).toBeVisible();
    await capture(page, info, "published-metadata-refused"); await page.unroute(metadataUrl);
    proof.metadataTransportControl = { isolatedAbort: true, noRunDialogBeforeContract: true, recoveryUsesRealServer: true };
    const runCases: unknown[] = [];
    for (const memo of [null, "null", ""]) {
      await page.goto(`/workflows/executables/${published.artifactId}?ref=${encodeURIComponent(published.sourceReferenceId)}`);
      await activate(page.getByRole("button", { name: "Run", exact: true }), "Open published Run");
      await expect(page.getByRole("dialog", { name: "Run workflow", exact: true })).toBeVisible();
      await activate(page.getByRole("button", { name: "Send null for Topic", exact: true }), "Send null for Topic");
      await activate(page.getByRole("button", { name: "Send null for Memo", exact: true }), "Send null for Memo");
      await activate(page.getByRole("button", { name: "Omit for Memo", exact: true }), "Omit for Memo");
      if (memo === null) await activate(page.getByRole("button", { name: "Send null for Memo", exact: true }), "Send null for Memo");
      else {
        const field = page.getByRole("textbox", { name: "Memo", exact: true });
        await field.focus();
        // Omit already displays an empty field. Make a real edit before clearing it so React receives the change
        // that distinguishes an explicitly supplied empty string from an untouched omitted value.
        if (memo === "") await field.fill("clear this value");
        await field.fill(memo);
      }
      await captureAt(page.getByRole("textbox", { name: "Memo", exact: true }), memo === null ? "published-run-inputs" : memo === "null" ? "published-run-text-null" : "published-run-empty-string");
      const response = page.waitForResponse(response => response.request().method() === "POST" && new URL(response.url()).pathname.endsWith(`/runtime/workflows/executables/${published.artifactId}/execute`));
      await activate(page.getByRole("button", { name: "Run workflow", exact: true }), "Run published workflow");
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
      name: `Null diagnostics ${info.project.name} ${Date.now()}`, initialState: { variables: [{ referenceKey: "count", name: "count", type: type("Int32") }], inputs: [], outputs: [],
        strategyOptions: { nullHandling: { defaultOnNull: true, undefinedAsNull: false } },
        rootActivity: root([{ nodeId: "substitute-input", activityVersionId: publishEvent, inputs: [literal("EventName", "s2-no-subscriber"), literal("IsLocalEvent", null)], outputs: [] }, { nodeId: "substitute", activityVersionId: "elsa.intrinsic.set@1", intrinsic: { kind: "Set", valueType: type("Any"), variable: { referenceKey: "count", declaringScopeId: "workflow" } }, inputs: [literal("value", null)], outputs: [] }, js("boundary", false), js("protected-boundary", true)]) }, layout: [], activityPresentation: []
    });
    registerWorkflow(diagnostics.definition.id);
    const diagnosticsDraft = await api<{ state: { rootActivity: Node; strategyOptions: { nullHandling: { defaultOnNull: boolean; undefinedAsNull: boolean } } } }>(`/design/workflows/drafts/${diagnostics.draft.id}`);
    expect(diagnosticsDraft.state.rootActivity.nodeId).toBe("root");
    expect(diagnosticsDraft.state.strategyOptions.nullHandling).toEqual({ defaultOnNull: true, undefinedAsNull: false });
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
    await expect(section).toContainText("customer.privateKey");
    await captureAt(section.getByRole("listitem").first(), "run-boundary-warnings");
    await captureAt(section.getByRole("listitem").last(), "run-boundary-warnings-end");
    section = await warnings("protected-boundary"); await expect(section).toContainText("VF-UNDEF-002"); await expect(section).not.toContainText("customer.privateKey");
    await captureAt(section.getByRole("listitem").first(), "run-protected-warning");
    await captureAt(section.getByRole("listitem").last(), "run-protected-warning-end");
    proof.diagnostics = { publication: diagPublication, run, inspectedNodes: ["substitute-input", "substitute", "boundary", "protected-boundary"] };

    const activity = await saved();
    const preflight = await api<{ isPublishable: boolean; reviewToken: string; minimumVersion: string; definitionHeadVersionId: string | null; diagnostics: unknown[] }>(`/design/activities/drafts/${activity.draftId}/publication-preflight`, "POST", { expectedDraftRevision: activity.revision, expectedDefinitionHeadVersionId: null });
    expect(preflight.isPublishable, JSON.stringify(preflight.diagnostics)).toBe(true);
    const activityPublication = await api<{ outcome: { definitionVersionId: string } }>(`/design/activities/drafts/${activity.draftId}/publish`, "POST", { expectedDraftRevision: activity.revision, expectedDefinitionHeadVersionId: preflight.definitionHeadVersionId, version: preflight.minimumVersion, reviewToken: preflight.reviewToken, idempotencyKey: crypto.randomUUID() });
    await page.goto(activityRoute); await inputs(); await expect(required()).toBeDisabled(); await expect(page.getByRole("combobox", { name: "Default", exact: true })).toBeDisabled();
    await capture(page, info, "contract-readonly");
    await activate(views().getByRole("tab", { name: /^Outputs/ }), "Read-only Activity Outputs");
    await expect(nullableOutput().locator('[data-contract-field="type.alias"]')).toBeDisabled();
    await expect(page.locator('[data-contract-row-kind="output"][data-contract-reference-key="number-result"]').locator('[data-contract-field="type.alias"]')).toBeDisabled();
    await captureAt(nullableOutput().locator('[data-contract-field="type.alias"]'), "contract-output-readonly");
    await captureAt(page.locator('[data-contract-row-kind="output"][data-contract-reference-key="number-result"]').locator('[data-contract-field="type.alias"]'), "contract-number-output-readonly");

    await page.goto(`/workflows/activity-definitions?definition=${created.definition.definitionId}&section=versions&version=${activityPublication.outcome.definitionVersionId}`);
    const historical = () => page.getByRole("region", { name: "Selected exact version", exact: true }).locator(".ad-historical-contract");
    await expect(historical()).toBeVisible();
    for (const name of ["Note", "Nullable result"]) await expect(historical().locator("li").filter({ hasText: name })).toContainText("Allows null");
    for (const name of ["Count", "Number result"]) await expect(historical().locator("li").filter({ hasText: name })).toContainText("Non-null");
    await expect(historical().locator("input, select, textarea")).toHaveCount(0);
    await captureAt(historical().locator(".ad-historical-contract-groups > section").filter({ has: page.getByRole("heading", { name: "Inputs", exact: true }) }), "contract-historical");
    await captureAt(historical().locator(".ad-historical-contract-groups > section").filter({ has: page.getByRole("heading", { name: "Outputs", exact: true }) }), "contract-historical-outputs");

    async function clrContract(key: string, versionId: string, inputKey: string, nullable: boolean, captureName: string) {
      const exact = await api<{ contract: { inputs: { referenceKey: string; name: string; displayName: string | null; type: { alias: string; supportsNull: boolean } }[] } }>(`/design/activities/versions/${versionId}`);
      const input = exact.contract.inputs.find(input => input.referenceKey === inputKey)!;
      expect(input).toBeTruthy(); expect(input.type.alias).toBe("String"); expect(input.type.supportsNull).toBe(nullable);
      const definitionId = definitionIds.get(key); expect(definitionId).toBeTruthy();
      await page.goto(`/workflows/activity-definitions?definition=${definitionId}&section=versions&version=${versionId}`);
      await expect(historical()).toBeVisible();
      await expect(historical().locator("li").filter({ hasText: input.displayName || input.name })).toContainText(nullable ? "Allows null" : "Non-null");
      await expect(historical().locator("input, select, textarea")).toHaveCount(0);
      await captureAt(historical().locator("li").filter({ hasText: input.displayName || input.name }), captureName);
      return { definitionId, versionId, input };
    }
    proof.clrContracts = [
      await clrContract("Elsa.Activities.Primitives.Activities.WriteLine", write, "text", true, "clr-nullable-contract"),
      await clrContract("Elsa.Activities.Primitives.Activities.PublishEvent", publishEvent, "EventName", false, "clr-nonnullable-contract")
    ];
    proof.status = "passed";
  } finally {
    proof.observedTestRunRequests = requests;
    const path = info.outputPath("null-policies-proof.json"); writeFileSync(path, JSON.stringify(proof, null, 2));
    await info.attach("null-policies-proof.json", { path, contentType: "application/json" });
  }
}
