import { expect, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";
import type { RuntimeHistoryBookmarkPage, RuntimeHistoryEvent, RuntimeHistoryPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/runtimeHistoryTypes";
import type { WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import { expectQualifiedTimelineCoverage } from "./expectQualifiedTimelineCoverage";
import { waitCapturedPlaybackReady } from "./waitCapturedPlaybackReady";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
type Node = { nodeId: string; activityVersionId: string; inputs: ReturnType<typeof inputs>; outputs: never[]; structure?: unknown };
const inputs = (values: Record<string, unknown>) => Object.entries(values).map(([referenceKey, value]) =>
  ({ referenceKey, value: { expressionType: "Literal", value } }));

/** Native Design → publication → waited child/compensation → history. No seeded events or intercepted responses. */
export async function nativeHistoryOwnersJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  if (!backend || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned Workbench is required");
  if (info.project.name === "narrow-light" || info.project.name === "narrow-dark")
    await page.setViewportSize({ width: 320, height: 900 });
  const api = async <T>(path: string, method = "GET", data?: unknown): Promise<T> => {
    const response = await page.request.fetch(`${backend}${path}`, { method, data, timeout: 15_000 });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  };
  const version = async (key: string) => {
    const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(
      `/design/activities/definitions?search=${encodeURIComponent(key)}`);
    const result = catalog.items.find(item => item.definition.activityTypeKey === key)?.definition.headVersionId;
    expect(result, `Actual native activity ${key}`).toBeTruthy(); return result!;
  };
  const [line, dispatchType, compensationType, faultType] = await Promise.all([
    version("Elsa.Activities.Primitives.Activities.WriteLine"), version("Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow"),
    version("Elsa.Activities.Compensation.Activities.CompensationScope"), version("Elsa.Activities.Primitives.Activities.Fault")
  ]);
  const node = (id: string, type: string, values: Record<string, unknown> = {}): Node =>
    ({ nodeId: id, activityVersionId: type, inputs: inputs(values), outputs: [] });
  const definitions: string[] = [];
  registerCleanup(async () => {
    for (const id of [...definitions].reverse()) await api(`/design/workflows/definitions/${id}`, "DELETE");
  });
  const publish = async (label: string, rootActivity: Node) => {
    const submitted = await api<{ definition: { id: string }; version: { id: string } }>(
      "/design/workflows/definitions/submit", "POST", {
        name: `Native history ${label} ${info.project.name} ${crypto.randomUUID().slice(0, 8)}`,
        state: { variables: [], inputs: [], outputs: [], strategyOptions: { persistenceMode: "Durable" }, rootActivity }
      });
    definitions.push(submitted.definition.id);
    const published = await api<{ artifactId: string; sourceReferenceId: string }>(
      `/publishing/workflows/${submitted.version.id}/publish`, "POST", {});
    return { submitted, published };
  };
  const execute = async (publication: Awaited<ReturnType<typeof publish>>) => {
    const run = await api<{ workflowExecutionId: string }>(
      `/runtime/workflows/executables/${publication.published.artifactId}/execute`, "POST", {
        sourceReferenceId: publication.published.sourceReferenceId
      });
    await expect.poll(async () => (await api<WorkflowInstanceDetails>(
      `/runtime/workflows/instances/${run.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Completed");
    return run;
  };
  const history = async (runId: string) => {
    let value!: RuntimeHistoryPage;
    await expect.poll(async () => {
      value = await api<RuntimeHistoryPage>(`/runtime/workflows/instances/${runId}/history?limit=100`);
      return { pending: value.coverage.sourceHistoryPending, limitation: value.coverage.sourceHistoryLimitation };
    }, { timeout: 20_000 }).toEqual({ pending: false, limitation: null });
    expectQualifiedTimelineCoverage(value.coverage);
    expect(value.nextContinuationToken).toBeNull();
    expect(new Set(value.items.map(item => item.historyEventId)).size).toBe(value.items.length);
    return value;
  };
  const one = (value: RuntimeHistoryPage, name: string) => {
    const events = value.items.filter(item => item.name === name);
    expect(events, name).toHaveLength(1); return events[0];
  };
  const details = page.getByRole("region", { name: "History event details", exact: true });
  const openEvent = async (event: RuntimeHistoryEvent, label: string) => {
    await page.goto(`/workflows/instances/${event.workflowExecutionId}?view=timeline&historyEvent=${encodeURIComponent(event.historyEventId)}`);
    await waitCapturedPlaybackReady(page);
    const heading = details.getByRole("heading", { name: label, exact: true });
    await expect(heading).toBeVisible(); await heading.scrollIntoViewIfNeeded(); await expect(heading).toBeInViewport();
  };

  const child = await publish("child", node("child-line", line, { text: "Native child completed" }));
  const parent = await publish("parent", node("dispatch", dispatchType, {
    WorkflowDefinitionId: child.submitted.definition.id, WaitForCompletion: true
  }));
  const parentRun = await execute(parent);
  const parentHistory = await history(parentRun.workflowExecutionId);
  const requested = one(parentHistory, "ChildWorkflowDispatchRequested");
  const childRunId = requested.metadata["history.relatedWorkflowExecutionId"];
  expect(childRunId).toBeTruthy(); expect(childRunId).not.toBe(parentRun.workflowExecutionId);
  const childDetails = await api<WorkflowInstanceDetails>(`/runtime/workflows/instances/${childRunId}`);
  expect(childDetails.instance).toMatchObject({ parentWorkflowExecutionId: parentRun.workflowExecutionId, sourceReferenceId: null });
  const childHistory = await history(childRunId);
  const childStarted = one(childHistory, "WorkflowStarted");
  const settled = one(childHistory, "ChildWorkflowSettled");
  const receipt = one(parentHistory, "DeliveryReceived");
  expect(childStarted.causation).toMatchObject({ historyEventId: requested.historyEventId, workflowExecutionId: parentRun.workflowExecutionId });
  expect(settled.metadata["history.relatedWorkflowExecutionId"]).toBe(parentRun.workflowExecutionId);
  expect(receipt.causation).toMatchObject({ historyEventId: settled.historyEventId, workflowExecutionId: childRunId });
  one(parentHistory, "ChildStartAttemptObserved"); one(parentHistory, "ChildStartDispositionObserved");
  const consumed = one(parentHistory, "BookmarkConsumed");
  expect(consumed.activityExecutionId).toBe(requested.activityExecutionId);
  const bookmarks = await api<RuntimeHistoryBookmarkPage>(
    `/runtime/workflows/instances/${parentRun.workflowExecutionId}/history/bookmarks?activityExecutionId=${encodeURIComponent(requested.activityExecutionId!)}&limit=100`);
  expect(bookmarks.items).toHaveLength(1);
  expect(bookmarks.items[0]).toMatchObject({ status: "Consumed", bookmark: { identity: { bookmarkId: consumed.bookmarkId } } });

  await openEvent(requested, "Child run dispatch requested");
  if (info.project.name.startsWith("desktop-")) {
    const events = page.getByRole("list", { name: "Run history events", exact: true }).getByRole("button");
    await expect(events.nth(2), "The primary chronology keeps several neighboring events visible without dismissing chrome").toBeInViewport();
  }
  await expect(details.getByRole("button", { name: "Open related run →", exact: true })).toBeVisible();
  await capture(page, info, "child-dispatch");
  await details.getByRole("button", { name: "Inspect owning execution →", exact: true }).click();
  const views = page.getByRole("tablist", { name: "Activity execution views", exact: true });
  await views.getByRole("tab", { name: "Journal", exact: true }).click();
  const journal = page.getByRole("region", { name: "Activity journal", exact: true });
  const dispatchRow = journal.getByText("Child run dispatch requested", { exact: true });
  await dispatchRow.scrollIntoViewIfNeeded(); await expect(dispatchRow).toBeInViewport();
  await expect(journal).toContainText("Bookmark created"); await expect(journal).toContainText("Bookmark consumed");
  await capture(page, info, "child-journal");
  await views.getByRole("tab", { name: "Bookmarks", exact: true }).click();
  const inventory = page.getByRole("region", { name: "Activity bookmark history", exact: true });
  await expect(inventory).toContainText(consumed.bookmarkId!);
  await inventory.getByRole("button", { name: "View lifecycle →", exact: true }).click();
  const lifecycle = page.getByRole("region", { name: "Bookmark lifecycle", exact: true });
  const consumedBadge = lifecycle.locator('[data-status="Consumed"]');
  await expect(consumedBadge).toHaveCount(1); await consumedBadge.scrollIntoViewIfNeeded();
  await expect(consumedBadge).toBeInViewport();
  await expect(lifecycle.getByText("Created", { exact: true }), "Creation time is part of the primary lifecycle answer").toBeInViewport();
  await expect(lifecycle.getByRole("button", { name: "Inspect consumed event →", exact: true }), "Consumption evidence is available before technical identifiers").toBeInViewport();
  await capture(page, info, "child-consumed-bookmark");
  await openEvent(requested, "Child run dispatch requested");
  await details.getByRole("button", { name: "Open related run →", exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/workflows/instances/${encodeURIComponent(childRunId)}\\?view=timeline`));
  await waitCapturedPlaybackReady(page);
  await openEvent(settled, "Child run settled");
  await expect(page.getByRole("heading", { name: `Child run · ${childRunId.slice(-8)}`, exact: true, level: 2 })).toBeVisible();
  await expect(page.getByRole("group", { name: "Run workspace actions", exact: true }).getByRole("button", { name: "Open definition", exact: true })).toHaveCount(0);
  await capture(page, info, "child-settlement");
  await openEvent(receipt, "Delivery received");
  await details.getByRole("button", { name: "Inspect causal event →", exact: true }).click();
  const settledHeading = details.getByRole("heading", { name: "Child run settled", exact: true });
  await expect(settledHeading).toBeVisible(); await settledHeading.scrollIntoViewIfNeeded();
  await expect(page).toHaveURL(new RegExp(`/workflows/instances/${encodeURIComponent(childRunId)}\\?`));
  await expect(settledHeading).toBeInViewport(); await capture(page, info, "child-cause");

  const scope = (id: string, confirm: boolean): Node => ({ ...node(id, compensationType, {
    confirmOnComplete: confirm, faultAfterCompensation: false
  }), structure: { kind: "elsa.compensation-scope.structure", schemaVersion: "1.0.0", payload: { steps: [
    { name: "Reserve", activity: node(`${id}-reserve`, line, { text: "Reservation recorded" }),
      compensation: node(`${id}-undo`, line, { text: "Reservation compensated" }) },
    ...(confirm ? [] : [{ name: "Reject", activity: node(`${id}-fault`, faultType, { code: "history.native.rejected", message: "Deliberate recovery qualification" }) }])
  ] } } });
  const confirmed = await publish("confirm", scope("confirm-scope", true));
  const confirmRun = await execute(confirmed); const confirmHistory = await history(confirmRun.workflowExecutionId);
  one(confirmHistory, "CompensationRegistered"); one(confirmHistory, "CompensationCompleted");
  const confirmation = one(confirmHistory, "CompensationConfirmed");
  expect(confirmHistory.items.some(item => item.name === "CompensationStarted")).toBe(false);
  await openEvent(confirmation, "Compensation confirmed"); await capture(page, info, "compensation-confirmed");
  const recovery = await publish("recover", scope("recovery-scope", false));
  const recoveryRun = await execute(recovery); const recoveryHistory = await history(recoveryRun.workflowExecutionId);
  for (const kind of ["CompensationRegistered", "CompensationStarted", "CompensationHandlerScheduled", "CompensationHandlerCompleted"])
    one(recoveryHistory, kind);
  const completed = one(recoveryHistory, "CompensationCompleted");
  await openEvent(completed, "Compensation completed"); await capture(page, info, "compensation-recovery");
  await details.getByRole("button", { name: "Inspect owning execution →", exact: true }).click();
  await views.getByRole("tab", { name: "Journal", exact: true }).click();
  await expect(journal).toContainText("Compensation registered");
  await expect(journal).toContainText("Compensation started");
  const recoveredRow = journal.getByText("Compensation handler completed", { exact: true });
  await recoveredRow.scrollIntoViewIfNeeded(); await expect(recoveredRow).toBeInViewport();
  await capture(page, info, "compensation-journal");
  const oracle = info.outputPath("native-history-owners-oracle.json");
  writeFileSync(oracle, JSON.stringify({ sourceSha: process.env.UX_SOURCE_SHA, child, parent, parentRun, parentHistory,
    childRunId, childDetails, childHistory, bookmarks, confirmed, confirmRun, confirmHistory, recovery, recoveryRun, recoveryHistory }, null, 2));
  await info.attach("native-history-owners-oracle.json", { path: oracle, contentType: "application/json" });
}
