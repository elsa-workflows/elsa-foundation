import { waitCapturedPlaybackReady } from "./waitCapturedPlaybackReady";
import { expect, request, type APIRequestContext, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";
import type { WorkflowInstanceDetails } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";

type Capture = (page: Page, info: TestInfo, name: string) => Promise<void>;
interface Occurrence {
  position: string; activityExecutionId: string; historyEventId: string; sequence: string;
  workflowExecutionId: string; recordedAt?: string | null; occurredAt?: string | null;
}
interface OccurrencePage {
  state: string; sequenceToken: string | null; upperSequence: string; totalCount: string | null;
  selectedPosition: string | null; items: Occurrence[]; hasNewerHistory: boolean;
  coverage: { capturedFromStart: boolean; hasGaps: boolean; lastSequence: string; purgedThroughSequence: string };
}

/** Real Design → publication → approval → Runtime → native history; no intercepted responses. */
export async function historyOccurrenceQualificationJourney(page: Page, info: TestInfo, capture: Capture,
  registerCleanup: (cleanup: () => Promise<void>) => void) {
  const backend = process.env.ELSA_WORKBENCH_URL;
  if (!backend || process.env.UX_ACCEPTANCE_OWNED !== "1") throw new Error("Owned Workbench is required");
  const api = async <T>(client: APIRequestContext, path: string, method = "GET", data?: unknown): Promise<T> => {
    const response = await client.fetch(`${backend}${path}`, { method, data, timeout: 15_000 });
    expect(response.ok(), `${method} ${path}: ${response.status()} ${await response.text()}`).toBe(true);
    return response.status() === 204 ? undefined as T : await response.json() as T;
  };
  const signIn = async (username: string) => {
    const response = await page.request.post(`${backend}/_elsa/identity/login`, { data: { username, password: "Password123!" } });
    expect(response.ok(), `Native cookie sign-in: ${response.status()}`).toBe(true);
  };
  const admin = await request.newContext({ storageState: await page.context().storageState() });
  try {
    if (info.project.name.startsWith("narrow")) await page.setViewportSize({ width: 320, height: 900 });
    const version = async (key: string) => {
      const catalog = await api<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(admin,
        `/design/activities/definitions?search=${encodeURIComponent(key)}`);
      const result = catalog.items.find(item => item.definition.activityTypeKey === key)?.definition.headVersionId;
      expect(result, key).toBeTruthy(); return result!;
    };
    const [flow, line, task] = await Promise.all([
      version("Elsa.Activities.Flowchart.Activities.Flowchart"), version("Elsa.Activities.Primitives.Activities.WriteLine"),
      version("Elsa.Activities.Primitives.Activities.CreateUserTask")
    ]);
    const who = await api<{ userId: string }>(admin, "/runtime/user-task-identity");
    const title = `History approval ${info.project.name} ${crypto.randomUUID().slice(0, 8)}`;
    const inputs = (values: Record<string, unknown>) => Object.entries(values).map(([referenceKey, value]) =>
      ({ referenceKey, value: { expressionType: "Literal", value } }));
    let ownedRunId: string | null = null;
    // Registered before Design cleanup so a failed assertion cannot leak an open approval.
    registerCleanup(async () => {
      if (!ownedRunId) return;
      for (let attempt = 0; attempt < 10; attempt++) {
        const state = await page.request.get(`${backend}/runtime/workflows/instances/${ownedRunId}`, { timeout: 5_000 });
        expect(state.ok(), `Owned run cleanup: HTTP ${state.status()}`).toBe(true);
        if (["Completed", "Faulted", "Cancelled"].includes((await state.json() as WorkflowInstanceDetails).instance.status)) return;
        const listed = await page.request.get(`${backend}/runtime/user-tasks?view=mine&limit=100`, { timeout: 5_000 });
        expect(listed.ok(), `Owned task cleanup: HTTP ${listed.status()}`).toBe(true);
        const open = (await listed.json() as { items: { id: string; workflowExecutionId: string; status: string; revision: number }[] }).items
          .filter(item => item.workflowExecutionId === ownedRunId && ["Open", "Claimed"].includes(item.status));
        for (const task of open) {
          const closed = await page.request.post(`${backend}/runtime/user-tasks/${encodeURIComponent(task.id)}/complete`, {
            timeout: 5_000, data: { revision: task.revision, requestId: `history-cleanup-${task.id}`, action: "Approve", data: {} }
          });
          expect(closed.ok(), `Owned approval cleanup: HTTP ${closed.status()}`).toBe(true);
        }
        await new Promise(resolve => setTimeout(resolve, 100));
      }
      throw new Error("The owned approval run did not settle during bounded cleanup.");
    });
    const definition = await api<{ definition: { id: string }; version: { id: string } }>(admin,
      "/design/workflows/definitions/submit", "POST", { name: title, state: {
        variables: [], inputs: [], outputs: [], strategyOptions: { persistenceMode: "Durable" },
        rootActivity: { nodeId: "root", activityVersionId: flow, inputs: [], outputs: [], structure: {
          kind: "elsa.flowchart.structure", schemaVersion: "1.0.0", payload: {
            activities: [
              { nodeId: "before", activityVersionId: line, inputs: inputs({ text: "Before approval" }), outputs: [] },
              { nodeId: "review", activityVersionId: task, inputs: inputs({ Title: title, AssigneeId: who.userId, Actions: ["Approve"] }), outputs: [] },
              { nodeId: "after", activityVersionId: line, inputs: inputs({ text: "After approval" }), outputs: [] }
            ], startNodeId: "before", connections: [
              { id: "before-review", source: { nodeId: "before", port: "Done" }, target: { nodeId: "review" } },
              { id: "review-after", source: { nodeId: "review", port: "Approve" }, target: { nodeId: "after" } }
            ], nodeMetadata: {}, connectionMetadata: {}
          }
        } }
      } });
    registerCleanup(async () => { await api(page.request, `/design/workflows/definitions/${definition.definition.id}`, "DELETE"); });
    const publication = await api<{ artifactId: string; sourceReferenceId: string }>(admin,
      `/publishing/workflows/${definition.version.id}/publish`, "POST", {});
    const run = await api<{ workflowExecutionId: string }>(admin,
      `/runtime/workflows/executables/${publication.artifactId}/execute`, "POST", { sourceReferenceId: publication.sourceReferenceId });
    ownedRunId = run.workflowExecutionId;
    const runPath = `/runtime/workflows/instances/${run.workflowExecutionId}`;
    const occurrencePath = `${runPath}/history/occurrences`;
    let taskId = "";
    await expect.poll(async () => {
      const tasks = await api<{ items: { id: string; workflowExecutionId: string }[] }>(admin, "/runtime/user-tasks?view=mine&limit=100");
      taskId = tasks.items.find(item => item.workflowExecutionId === run.workflowExecutionId)?.id ?? "";
      return taskId;
    }, { timeout: 30_000 }).not.toBe("");
    const details = await api<WorkflowInstanceDetails>(admin, runPath);
    const review = details.activities.find(item => item.authoredActivityId === "review");
    expect(review?.status).toBe("Suspended");
    const frozen = await api<OccurrencePage>(admin, `${occurrencePath}?limit=50`);
    expect(["available", "partial"]).toContain(frozen.state);
    expect(frozen.sequenceToken).toBeTruthy(); expect(frozen.totalCount).not.toBeNull();
    expect(frozen.coverage.capturedFromStart).toBe(true);
    expect(frozen.coverage.hasGaps).toBe(false); expect(frozen.items.length).toBeGreaterThanOrEqual(3);
    expect(frozen.items.every(item => item.workflowExecutionId === run.workflowExecutionId)).toBe(true);
    expect(new Set(frozen.items.map(item => item.activityExecutionId)).size).toBe(frozen.items.length);
    const selected = frozen.items.find(item => item.activityExecutionId === review!.activityExecutionId)!;
    expect(selected).toBeTruthy();
    const frozenQuery = new URLSearchParams({ limit: "50", sequenceToken: frozen.sequenceToken!, position: selected.position });
    const sought = await api<OccurrencePage>(admin, `${occurrencePath}?${frozenQuery}`);
    expect(sought.items).toEqual(frozen.items); expect(sought.totalCount).toBe(frozen.totalCount);
    expect(sought.selectedPosition).toBe(selected.position);
    await page.goto(`/workflows/instances/${run.workflowExecutionId}?view=timeline`);
    await waitCapturedPlaybackReady(page);
    const timeline = page.getByRole("region", { name: "Run timeline", exact: true });
    await timeline.getByRole("combobox", { name: "History detail", exact: true }).selectOption("all");
    await expect(timeline.getByRole("list", { name: "Run history events", exact: true })).toBeVisible();
    const waitingHandle = page.locator(".wf-history-waits button").filter({ hasText: review!.activityExecutionId });
    await expect(waitingHandle).toHaveCount(1);
    await waitingHandle.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest" }));
    await expect(waitingHandle).toBeInViewport({ ratio: 1 });
    await capture(page, info, "history-wait");
    await page.goto(`/user-tasks/${taskId}`);
    await page.getByRole("region", { name: title, exact: true }).getByRole("button", { name: "Approve", exact: true }).press("Enter");
    await expect.poll(async () => (await api<WorkflowInstanceDetails>(admin, runPath)).instance.status, { timeout: 30_000 }).toBe("Completed");
    const retained = await api<OccurrencePage>(admin, `${occurrencePath}?${frozenQuery}`);
    expect(retained.items).toEqual(frozen.items); expect(retained.upperSequence).toBe(frozen.upperSequence);
    expect(retained.totalCount).toBe(frozen.totalCount); expect(retained.hasNewerHistory).toBe(true);
    const refreshed = await api<OccurrencePage>(admin, `${occurrencePath}?limit=50&anchorActivityExecutionId=${encodeURIComponent(selected.activityExecutionId)}`);
    expect(BigInt(refreshed.upperSequence)).toBeGreaterThan(BigInt(frozen.upperSequence));
    expect(BigInt(refreshed.totalCount!)).toBe(BigInt(frozen.totalCount!) + 1n);
    expect(refreshed.items.find(item => item.activityExecutionId === selected.activityExecutionId)).toMatchObject({
      activityExecutionId: selected.activityExecutionId, historyEventId: selected.historyEventId,
      sequence: selected.sequence, position: selected.position, recordedAt: selected.recordedAt, occurredAt: selected.occurredAt
    });
    await page.goto(`/workflows/instances/${run.workflowExecutionId}?view=timeline`);
    await waitCapturedPlaybackReady(page);
    await expect(timeline.getByRole("list", { name: "Run history events", exact: true })).toBeVisible();
    const completedRow = timeline.getByRole("button").filter({ has: page.getByText("Run completed", { exact: true }) });
    await completedRow.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest" }));
    await expect(completedRow).toBeInViewport({ ratio: 1 });
    await capture(page, info, "history-completed");

    const faultVersion = await version("Elsa.Activities.Primitives.Activities.Fault");
    const faultDefinition = await api<{ definition: { id: string }; version: { id: string } }>(admin,
      "/design/workflows/definitions/submit", "POST", { name: `${title} fault`, state: {
        variables: [], inputs: [], outputs: [], strategyOptions: { persistenceMode: "Durable" },
        rootActivity: { nodeId: "fault", activityVersionId: faultVersion,
          inputs: inputs({ code: "history.qualification", message: "Deliberate history qualification fault" }), outputs: [] }
      } });
    registerCleanup(async () => { await api(page.request, `/design/workflows/definitions/${faultDefinition.definition.id}`, "DELETE"); });
    const faultPublication = await api<{ artifactId: string; sourceReferenceId: string }>(admin,
      `/publishing/workflows/${faultDefinition.version.id}/publish`, "POST", {});
    const faultRun = await api<{ workflowExecutionId: string }>(admin,
      `/runtime/workflows/executables/${faultPublication.artifactId}/execute`, "POST", { sourceReferenceId: faultPublication.sourceReferenceId });
    await expect.poll(async () => (await api<WorkflowInstanceDetails>(admin,
      `/runtime/workflows/instances/${faultRun.workflowExecutionId}`)).instance.status, { timeout: 30_000 }).toBe("Faulted");
    const faultOccurrences = await api<OccurrencePage>(admin,
      `/runtime/workflows/instances/${faultRun.workflowExecutionId}/history/occurrences?limit=5`);
    expect(faultOccurrences.items).toHaveLength(1);
    await page.goto(`/workflows/instances/${faultRun.workflowExecutionId}?view=timeline`);
    await waitCapturedPlaybackReady(page);
    await expect(timeline.getByText("Activity faulted", { exact: true })).toBeVisible();
    const faultedRow = timeline.getByRole("button").filter({ has: page.getByText("Activity faulted", { exact: true }) });
    await faultedRow.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest" }));
    await expect(faultedRow).toBeInViewport({ ratio: 1 });
    await capture(page, info, "history-fault");

    // Sign in through the real cookie handler, then retire its grants through native IAM CAS.
    await signIn("history-reader");
    const reader = await api<OccurrencePage>(page.request, `${occurrencePath}?limit=1`);
    expect(reader.items).toHaveLength(1);
    expect(reader.items[0]).not.toHaveProperty("metadata"); expect(reader.items[0]).not.toHaveProperty("payload");
    const authority = await api<{ revision: string }>(admin, "/__qualification/principals/1/authority");
    const revoked = await api<{ revision: string; directPermissions: string[] }>(admin, "/__qualification/principals/1/grant-profile", "POST",
      { profile: "history-no-structure", revision: authority.revision });
    expect(revoked.revision).not.toBe(authority.revision);
    expect(revoked.directPermissions).not.toContain("workflows/activity-executions:inspect");
    const denied = await page.request.get(`${backend}${occurrencePath}?${new URLSearchParams({ sequenceToken: reader.sequenceToken!, limit: "1" })}`);
    expect(denied.status()).toBe(404);
    const deniedText = await denied.text();
    expect(deniedText).not.toContain(selected.activityExecutionId); expect(deniedText).not.toContain(reader.sequenceToken!);

    await signIn("admin");
    const purged = await api<{ purgedThrough: string; afterPurgedThrough: string }>(admin,
      `/__qualification/history/${run.workflowExecutionId}/purge`, "POST", {});
    expect(BigInt(purged.purgedThrough)).toBeGreaterThanOrEqual(BigInt(refreshed.upperSequence));
    expect(purged.afterPurgedThrough).toBe(purged.purgedThrough);
    const invalidated = await api<OccurrencePage>(admin, `${occurrencePath}?${frozenQuery}`);
    expect(invalidated.state).toBe("range-invalidated"); expect(invalidated.items).toEqual([]);
    await page.goto(`/workflows/instances/${run.workflowExecutionId}?view=timeline`);
    await expect(timeline).toBeVisible();
    await expect(timeline).toHaveAttribute("aria-busy", "false");
    await expect(timeline.getByText("No recorded events in this range.", { exact: true })).toBeVisible();
    const coverage = timeline.locator(".wf-history-coverage");
    await expect(coverage).toHaveAttribute("data-state", "partial");
    await expect(timeline.getByRole("button", { name: "Retry history", exact: true })).toHaveCount(0);
    await coverage.getByText("Recording coverage", { exact: true }).click();
    await expect(coverage.getByText("Purged through", { exact: true })).toBeVisible();
    const purgedPosition = coverage.locator("dt").filter({ hasText: /^Purged through$/ }).locator("+ dd");
    await expect(purgedPosition).toHaveText(purged.purgedThrough);
    const retainedRange = coverage.getByText("No retained events", { exact: true });
    await retainedRange.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest" }));
    await expect(retainedRange).toBeInViewport({ ratio: 1 });
    await expect(purgedPosition).toBeInViewport({ ratio: 1 });
    await capture(page, info, "history-retained-boundary");
    const oraclePath = info.outputPath("history-production-oracle.json");
    writeFileSync(oraclePath, JSON.stringify({ definitionId: definition.definition.id,
      ...publication, ...run, taskId, frozen, sought, retained, refreshed, faultRun, faultOccurrences, reader, revoked, deniedStatus: denied.status(), purged, invalidated }, null, 2));
    await info.attach("history-production-oracle.json", { path: oraclePath, contentType: "application/json" });
  } finally {
    try {
      const current = await api<{ revision: string }>(admin, "/__qualification/principals/1/authority");
      await api(admin, "/__qualification/principals/1/grant-profile", "POST", { profile: "history-reader", revision: current.revision });
    } finally {
      try {
        // Existing suite cleanups retain admin authority after this private cookie test.
        await signIn("admin");
      } finally { await admin.dispose(); }
    }
  }
}
