import { expect, type Page, type TestInfo } from "@playwright/test";
import { writeFileSync } from "node:fs";
import { JobsUx, reportInvocation, type Capture } from "./jobsSupport";
import type { Association, Attempt, Gate, Hold, Job, Page as JobPage, ResultView } from "../../../Extensions/Jobs/studio/Elsa.Studio.Jobs/Client/src/types";

type Pin = { artifactId: string; definitionId: string; definitionVersionId: string; artifactVersion: string; artifactHash: string };
type Submitted = { definition: { id: string }; draftId: string; version: { id: string } };
type Published = Pin & { sourceReferenceId: string; versionId: string };
type NativeDetails = { instance: Pin & { workflowExecutionId: string; status: string; sourceReferenceId: string; tenantId: string | null }; activities: { activityExecutionId: string; activityType: string; status: string }[] };
type Wait = { activityExecutionId: string; workflowExecutionId: string; activityType: string; status: string; lastCheckpointId: string | null;
  bookmarks: { identity: { bookmarkId: string; workflowExecutionId: string; activityExecutionId: string; resumeTargetId: string }; stimulusType: string; stimulusHash: string }[]; outcomeNames: string[] };
type BpmnView = { executionId: string; status: string; revision: number; protocol: { key: string; version: number }; executable: Pin; result: { status: string } | null };
type BpmnInspection = { execution: BpmnView; profile: string; process: { processId: string; elements: { elementId: string; elementType: string }[] };
  source: { sourceId: string; sourceReferenceId: string }; work: { kind: string; elementId: string; invocationId: string; workerExecutionId: string | null }[];
  attempts: { workerExecutionId: string; workerExecutable: Pin; status: string; outcomeKey: string | null }[] };
type AssociatedJob = Omit<Job, "association"> & { association: Association & { invocationId: string; materialId: string; materialSha256: string } };
type Model = { kind: "native" | "bpmn"; executionId: string; workerExecutionId: string; definitionId: string; pin: Pin; worker: Published; authored: Submitted; invocationId?: string; nativeHeaderStatus?: number; initialBpmnInspection?: BpmnInspection };
type Evidence = { kind: "native" | "bpmn"; state: "NOT RUN" | "PASSED" | "FAILED"; facts?: Record<string, unknown>; error?: string };

const runJobType = "Elsa.Jobs.Workflows.Runtime.Activities.RunJob";
const bpmnType = "Elsa.Activities.Bpmn.Activities.BpmnProcess";
const id = encodeURIComponent;

/** Real authored versions and public publication/start APIs, with no execution-state seeding or HTTP interception. */
export async function jobsModelJourney(page: Page, info: TestInfo, capture: Capture) {
  const ux = new JobsUx(page, info, capture);
  const evidence: Evidence[] = [{ kind: "native", state: "NOT RUN" }, { kind: "bpmn", state: "NOT RUN" }];
  const cleanupErrors: string[] = [];
  const outstanding = new Map<string, Hold>();
  const barriers = new Set<string>();
  let active: Evidence | undefined;
  let overallState: Evidence["state"] = "NOT RUN";
  let failure: string | undefined;

  async function catalogVersion(type: string) {
    const catalog = await ux.get<{ items: { definition: { activityTypeKey: string; headVersionId: string } }[] }>(`/design/activities/definitions?search=${id(type)}`);
    const exact = catalog.items.filter(item => item.definition.activityTypeKey === type);
    expect(exact, `The actual ${type} catalog entry is required`).toHaveLength(1);
    expect(exact[0].definition.headVersionId).toBeTruthy();
    return exact[0].definition.headVersionId;
  }
  async function submit(rootActivity: unknown, name: string) {
    const submitted = await ux.write<Submitted>("POST", "/design/workflows/definitions/submit", {
      operationKey: crypto.randomUUID(), name, state: { rootActivity, variables: [], inputs: [], outputs: [] }
    });
    expect(submitted.definition.id).toBeTruthy(); expect(submitted.version.id).toBeTruthy();
    return submitted;
  }
  async function publishWorker(title: string, tag: string, activityVersionId: string) {
    const invocation = reportInvocation(ux.queue, title, { mode: "barrier", tag, delayMilliseconds: 0 });
    const values = { Handler: invocation.handler, InputSchema: invocation.input.schema, ResultSchema: invocation.resultSchema,
      Input: invocation.input.value, Queue: invocation.queue, Policy: invocation.policy, NotBefore: null,
      // The pinned selection uses the declared numeric CLR enum; ordinary handler JSON remains unchanged.
      AssociationMode: 0 };
    const authored = await submit({ nodeId: "report-job", activityVersionId,
      inputs: Object.entries(values).map(([referenceKey, value]) => ({ referenceKey, value: { expressionType: "Literal", value } })), outputs: [] }, title);
    const worker = await ux.write<Published>("POST", `/publishing/workflows/${id(authored.version.id)}/publish`, {});
    expect(worker.versionId).toBe(authored.version.id); expect(worker.definitionId).toBe(authored.definition.id);
    expect(worker.artifactHash).toMatch(/^[0-9a-f]{64}$/);
    const compiled = await ux.get<{ rootActivityType: string; nodeCount: number; resumeTargetCount: number;
      references: { sourceReferenceId: string; sourceId: string; live: boolean }[] }>(`/runtime/workflows/executables/${id(worker.artifactId)}?ref=${id(worker.sourceReferenceId)}`);
    expect(compiled.rootActivityType).toBe(runJobType); expect(compiled.nodeCount).toBe(1); expect(compiled.resumeTargetCount).toBe(1);
    expect(compiled.references).toContainEqual(expect.objectContaining({ sourceReferenceId: worker.sourceReferenceId, sourceId: authored.version.id, live: true }));
    return { authored, worker, invocation };
  }
  async function create(kind: Model["kind"], title: string, tag: string, runJobVersion: string, bpmnVersion?: string): Promise<Model> {
    const { authored, worker } = await publishWorker(title + " worker", tag, runJobVersion);
    if (kind === "native") {
      const started = await ux.write<{ workflowExecutionId: string; commandDispatchStatus: string }>("POST",
        `/runtime/workflows/executables/${id(worker.artifactId)}/execute`, { sourceReferenceId: worker.sourceReferenceId });
      expect(started.commandDispatchStatus).toBe("Accepted"); expect(started.workflowExecutionId).toBeTruthy();
      return { kind, executionId: started.workflowExecutionId, workerExecutionId: started.workflowExecutionId, definitionId: authored.definition.id, pin: worker, worker, authored };
    }
    expect(bpmnVersion, "A genuine authored BpmnProcess catalog version is required").toBeTruthy();
    const executable: Pin = { artifactId: worker.artifactId, definitionId: worker.definitionId, definitionVersionId: worker.definitionVersionId,
      artifactVersion: worker.artifactVersion, artifactHash: worker.artifactHash };
    const binding = JSON.stringify({ formatVersion: 1, kind: "nativeService", executable, sourceReferenceId: worker.sourceReferenceId,
      businessErrorCode: "ux.report.failed", faultPolicy: { formatVersion: 1, disposition: "FinalFault", maxAttempts: 1 } });
    const bpmn = await submit({ nodeId: "report-process", activityVersionId: bpmnVersion, inputs: [], outputs: [],
      structure: { kind: "elsa.bpmn.structure", schemaVersion: "1.0.0", payload: { activities: [], elements: [
        { elementId: "start", elementType: "startEvent" },
        { elementId: "report", elementType: "serviceTask", name: "Produce retained report", properties: { "elsa.direct.binding": binding } },
        { elementId: "end", elementType: "endEvent" }
      ], sequenceFlows: [{ flowId: "start-report", sourceRef: "start", targetRef: "report" }, { flowId: "report-end", sourceRef: "report", targetRef: "end" }] } } }, title);
    const published = await ux.write<{ executable: Pin; sourceReferenceId: string }>("POST",
      `/interchange/bpmn/versions/${id(bpmn.version.id)}/publish-direct`, { requestId: crypto.randomUUID() });
    const requestId = crypto.randomUUID();
    const started = await ux.write<{ executionId: string; pending: boolean; result: { status: string } | null }>("POST", "/bpmn/executions", { sourceReferenceId: published.sourceReferenceId, requestId });
    let startOutcome = started;
    await expect.poll(async () => {
      if (startOutcome.pending) startOutcome = await ux.get<typeof started>(`/bpmn/executions/${id(started.executionId)}/commands/${id(requestId)}`);
      return startOutcome.pending ? "Pending" : startOutcome.result?.status;
    }, { timeout: 45_000 }).toBe("Accepted");
    let inspection!: BpmnInspection;
    await expect.poll(async () => {
      inspection = await ux.get<BpmnInspection>(`/bpmn/executions/${id(started.executionId)}/inspection`);
      return inspection.work.filter(work => work.kind === "Native" && work.elementId === "report" && work.workerExecutionId).length;
    }, { timeout: 45_000 }).toBe(1);
    expect(inspection.profile).toBe("foundation-native-work-v1"); expect(inspection.execution.protocol).toEqual({ key: "elsa.bpmn", version: 1 });
    expect(inspection.source.sourceId).toBe(bpmn.version.id); expect(inspection.source.sourceReferenceId).toBe(published.sourceReferenceId);
    expect(inspection.process.processId).toBe(bpmn.definition.id);
    expect(inspection.process.elements.map(element => element.elementType)).toEqual(["startEvent", "serviceTask", "endEvent"]);
    const nativeHeader = await page.request.get(`${ux.base}/runtime/workflows/instances/${id(started.executionId)}`);
    expect(nativeHeader.status(), "A genuine BPMN root has no native workflow header").toBe(404);
    const work = inspection.work.find(work => work.kind === "Native" && work.elementId === "report")!;
    return { kind, executionId: started.executionId, workerExecutionId: work.workerExecutionId!, definitionId: bpmn.definition.id,
      pin: published.executable, worker, authored: bpmn, invocationId: work.invocationId,
      nativeHeaderStatus: nativeHeader.status(), initialBpmnInspection: inspection };
  }
  async function savedWait(model: Model) {
    const details = await ux.get<NativeDetails>(`/runtime/workflows/instances/${id(model.workerExecutionId)}`);
    expect(details.instance.status).toBe("Running"); expect(details.instance.artifactId).toBe(model.worker.artifactId);
    expect(details.instance.tenantId).toBe("default");
    const activities = details.activities.filter(item => item.activityType === runJobType);
    expect(activities).toHaveLength(1);
    const activity = await ux.get<Wait>(`/runtime/workflows/instances/${id(model.workerExecutionId)}/activity-executions/${id(activities[0].activityExecutionId)}`);
    expect(activity.status).toBe("Suspended"); expect(activity.lastCheckpointId).toBeTruthy(); expect(activity.bookmarks).toHaveLength(1);
    expect(activity.bookmarks[0].identity).toMatchObject({ workflowExecutionId: model.workerExecutionId, activityExecutionId: activity.activityExecutionId });
    expect(activity.bookmarks[0].identity.bookmarkId).toBeTruthy(); expect(activity.bookmarks[0].identity.resumeTargetId).toBeTruthy();
    return activity;
  }
  async function parentWaiting(model: Model, initial: Wait, revision?: number) {
    const current = await savedWait(model);
    expect(current.activityExecutionId).toBe(initial.activityExecutionId); expect(current.bookmarks).toEqual(initial.bookmarks);
    expect(current.lastCheckpointId).toBe(initial.lastCheckpointId);
    if (model.kind === "bpmn") {
      const root = await ux.get<BpmnInspection>(`/bpmn/executions/${id(model.executionId)}/inspection`);
      expect(root.execution.status).toBe("Running"); expect(root.execution.result).toBeNull();
      if (revision !== undefined) expect(root.execution.revision).toBe(revision);
      expect(root.work).toContainEqual(expect.objectContaining({ kind: "Native", invocationId: model.invocationId, workerExecutionId: model.workerExecutionId }));
    }
  }
  function holdPath(gate: Gate) { return "/jobs/holds?" + new URLSearchParams({ kind: gate.kind, name: gate.name, modelKind: gate.modelKind!, limit: "100" }); }
  async function pauseVisible(gate: Gate, reason: string) {
    const label = gate.kind === "workflowInstance" ? "Workflow instance" : "Workflow definition (all versions)";
    await ux.prompt(page.getByRole("button", { name: `Pause ${label}: ${gate.name} · ${gate.modelKind}…`, exact: true }), "Pause jobs", reason, "Add hold");
    let selected!: Hold;
    await expect.poll(async () => {
      const holds = await ux.get<JobPage<Hold>>(holdPath(gate)); expect(holds.next).toBeFalsy();
      const matches = holds.items.filter(hold => hold.reason === reason && !hold.releasedAt);
      if (matches.length === 1) selected = matches[0];
      return matches.length;
    }).toBe(1);
    outstanding.set(selected.holdId, selected);
    return selected;
  }
  async function releaseVisible(hold: Hold) {
    const row = page.getByRole("region", { name: "All matching hold reasons", exact: true }).getByRole("listitem").filter({ hasText: hold.holdId });
    await expect(row).toHaveCount(1);
    await ux.prompt(row.getByRole("button", { name: "Release this hold…", exact: true }), "Release this hold", "Release exact model continuation hold", "Release hold");
    await expect.poll(async () => (await ux.get<JobPage<Hold>>(holdPath(hold.gate))).items.find(item => item.holdId === hold.holdId)?.releasedAt ?? null).not.toBeNull();
    outstanding.delete(hold.holdId);
    await ux.refresh();
    await expect(row).toContainText("Released by");
    await expect(row.getByRole("button", { name: "Release this hold…", exact: true })).toHaveCount(0);
  }
  async function attempts(jobId: string) {
    const result = await ux.get<JobPage<Attempt>>(`/jobs/${id(jobId)}/attempts?limit=100`); expect(result.next).toBeFalsy(); return result.items;
  }
  async function report(jobId: string, title: string) {
    const result = await ux.get<ResultView>(`/jobs/${id(jobId)}/result`);
    expect(result.state).toBe("available"); expect(result.outcome).toBe("succeeded"); expect(result.resultId).toBeTruthy();
    expect(result.payload?.schema).toEqual({ alias: "ux.report.result", version: 1 });
    const useful = result.valueJson ? JSON.parse(result.valueJson) : result.payload?.value;
    expect(useful).toMatchObject({ title, total: 50, count: 3 });
    expect(useful.lines).toHaveLength(3);
    for (const [index, line] of useful.lines.entries()) expect(line).toMatch(new RegExp(`^Line ${index + 1}: `));
    return result;
  }
  try {
    expect(await ux.get<{ tenantId: string }>("/jobs/access")).toMatchObject({ tenantId: "default" });
    const runJobVersion = await catalogVersion(runJobType);
    const bpmnVersion = await catalogVersion(bpmnType);
    expect((await ux.get<{ admitNewExecutions: boolean }>("/bpmn/admission")).admitNewExecutions).toBe(true);
    for (const current of evidence) {
      active = current;
      const tag = `${current.kind}-${ux.stamp}`; barriers.add(tag);
      const title = `${current.kind} authored report ${ux.stamp}`;
      const model = await create(current.kind, title, tag, runJobVersion, bpmnVersion);
      current.facts = { model, handlerBarrierTag: tag };
      let job!: AssociatedJob;
      await expect.poll(async () => {
        const matches = (await ux.allJobs()).filter(item => item.association?.executionId === model.executionId);
        if (matches.length === 1) job = await ux.get<AssociatedJob>(`/jobs/${id(matches[0].jobId)}`);
        return matches.length;
      }, { timeout: 45_000 }).toBe(1);
      expect(job.association).toMatchObject({ modelKind: `elsa.${current.kind}`, definitionId: model.definitionId,
        definitionVersion: model.pin.definitionVersionId, executionId: model.executionId, mode: "awaited", materialId: model.pin.artifactId, materialSha256: model.pin.artifactHash });
      expect(job.handler).toEqual({ jobType: "ux.report", version: "1" });
      const wait = await savedWait(model);
      expect(job.association.invocationId).toBe(model.invocationId ?? wait.activityExecutionId);
      await expect.poll(async () => (await attempts(job.jobId)).filter(attempt => attempt.physicalStartedAt && !attempt.physicalExitedAt).length, { timeout: 30_000 }).toBe(1);
      const physicalAttempt = (await attempts(job.jobId))[0].permission.attemptId;
      await ux.open(job.jobId);
      const instanceGate: Gate = { kind: "workflowInstance", name: model.executionId, modelKind: `elsa.${current.kind}` };
      const definitionGate: Gate = { kind: "workflowDefinition", name: model.definitionId, modelKind: `elsa.${current.kind}` };
      const instanceHold = await pauseVisible(instanceGate, "Hold this model instance " + tag);
      const definitionHold = await pauseVisible(definitionGate, "Hold all versions of this model definition " + tag);
      const rootRevision = model.kind === "bpmn" ? (await ux.get<BpmnView>(`/bpmn/executions/${id(model.executionId)}`)).revision : undefined;
      expect((await ux.job(job.jobId)).resultId).toBeFalsy();
      await ux.write("POST", `/_ux/jobs/recover/${id(tag)}`, {}); barriers.delete(tag);
      await ux.finish(job.jobId);
      await expect.poll(async () => (await ux.job(job.jobId)).consumptionState).toBe("owed");
      const selected = await report(job.jobId, title + " worker");
      await parentWaiting(model, wait, rootRevision);
      current.facts = { ...current.facts, jobId: job.jobId, savedWait: wait, physicalAttempt, instanceHold, definitionHold, selectedResult: selected, heldRootRevision: rootRevision };
      await ux.refresh();
      const parent = page.getByRole("region", { name: "Parent and result delivery", exact: true });
      await expect(parent).toContainText("The result is saved. Delivery to its model owner remains owed");
      const parentPath = (current.kind === "bpmn" ? "/workflows/bpmn/runs/" : "/workflows/instances/") + id(model.executionId);
      const parentLink = parent.getByRole("link", { name: `Open parent run ${model.executionId}`, exact: true });
      await expect(parentLink).toBeVisible();
      expect(new URL((await parentLink.getAttribute("href"))!, page.url()).pathname).toBe(parentPath);
      await page.getByRole("button", { name: "Load permitted result", exact: true }).press("Enter");
      await expect(page.getByRole("region", { name: "Permitted result JSON", exact: true })).toContainText(/"total"\s*:\s*50/);
      await capture(page, info, `${current.kind}-held-result`);
      await releaseVisible(instanceHold);
      expect((await ux.get<JobPage<Hold>>(holdPath(definitionGate))).items.find(hold => hold.holdId === definitionHold.holdId)?.releasedAt).toBeFalsy();
      const remaining = page.getByRole("region", { name: "All matching hold reasons", exact: true }).getByRole("listitem").filter({ hasText: definitionHold.holdId });
      await expect(remaining).toContainText("Active hold");
      await expect(remaining.getByRole("button", { name: "Release this hold…", exact: true })).toBeVisible();
      expect((await ux.job(job.jobId)).consumptionState).toBe("owed");
      expect(await report(job.jobId, title + " worker")).toEqual(selected);
      await parentWaiting(model, wait, rootRevision);
      await releaseVisible(definitionHold);
      await expect.poll(async () => (await ux.job(job.jobId)).consumptionState, { timeout: 45_000 }).toBe("settled");
      let completed: NativeDetails | BpmnView;
      if (model.kind === "native") {
        await expect.poll(async () => (await ux.get<NativeDetails>(`/runtime/workflows/instances/${id(model.executionId)}`)).instance.status, { timeout: 45_000 }).toBe("Completed");
        completed = await ux.get<NativeDetails>(`/runtime/workflows/instances/${id(model.executionId)}`);
      } else {
        await expect.poll(async () => (await ux.get<BpmnView>(`/bpmn/executions/${id(model.executionId)}`)).status, { timeout: 45_000 }).toBe("TerminalSettled");
        completed = await ux.get<BpmnView>(`/bpmn/executions/${id(model.executionId)}`); expect(completed.result?.status).toBe("Completed");
      }
      await expect.poll(async () => (await attempts(job.jobId))[0]?.physicalExitedAt ?? null, { timeout: 30_000 }).not.toBeNull();
      const finalAttempts = await attempts(job.jobId); expect(finalAttempts).toHaveLength(1);
      expect(finalAttempts[0].permission.attemptId).toBe(physicalAttempt); expect(finalAttempts[0].physicalExitedAt).toBeTruthy();
      expect(await report(job.jobId, title + " worker")).toEqual(selected);
      const consumed = await ux.get<Wait>(`/runtime/workflows/instances/${id(model.workerExecutionId)}/activity-executions/${id(wait.activityExecutionId)}`);
      expect(consumed.status).toBe("Completed"); expect(consumed.outcomeNames).toContain("Done"); expect(consumed.bookmarks).toHaveLength(0);
      await ux.refresh();
      await page.getByRole("link", { name: `Open parent run ${model.executionId}`, exact: true }).press("Enter");
      await expect.poll(() => new URL(page.url()).pathname).toBe(parentPath);
      if (model.kind === "bpmn") {
        await expect(page.getByRole("heading", { name: "BPMN run", exact: true })).toBeVisible();
        await expect(page.locator(".wf-bpmn-run-heading").getByRole("status")).toHaveText("Completed");
      } else {
        await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true })).toBeVisible();
        await expect(page.getByRole("region", { name: "Workflow run canvas", exact: true }).locator("header").getByText("Completed", { exact: true })).toBeVisible();
      }
      await capture(page, info, `${current.kind}-parent`);
      current.facts = { ...current.facts, completedParent: completed, completedActivity: consumed, finalAttempts, sameResultAfterResume: selected };
      current.state = "PASSED"; active = undefined;
    }
    overallState = "PASSED";
  } catch (error) {
    overallState = "FAILED"; failure = error instanceof Error ? error.message : String(error);
    if (active) { active.state = "FAILED"; active.error = error instanceof Error ? error.message : String(error); }
    throw error;
  } finally {
    for (const tag of barriers) {
      try { await ux.write("POST", `/_ux/jobs/recover/${id(tag)}`, {}); }
      catch (error) { cleanupErrors.push(`Handler barrier ${tag}: ${String(error)}`); }
    }
    for (const hold of outstanding.values()) {
      try { await ux.release(hold); }
      catch (error) { cleanupErrors.push(`Hold ${hold.holdId}: ${String(error)}`); }
    }
    const receipt = info.outputPath("authored-native-genuine-bpmn-jobs.json");
    writeFileSync(receipt, JSON.stringify({ state: cleanupErrors.length ? "FAILED" : overallState, failure, evidenceKind: "ActualPublicApiAndBrowserExecution",
      cases: evidence, cleanupErrors, definitionsRetainedForInspection: true,
      configuredScope: { persistencePartition: "default", tenantId: "default" },
      publicProjectionLimitation: "The native summary exposes tenantId, and current native/BPMN public inspection does not expose the retained persistence partition. Configured default scope, canonical association and actual held/result/resume behavior are asserted; this receipt does not claim a separate raw retained-partition read.",
      boundaries: "These two actual authored model journeys do not qualify every Jobs model/provider/recovery policy or secondary-tenant model execution." }, null, 2));
    await info.attach("authored-native-genuine-bpmn-jobs.json", { path: receipt, contentType: "application/json" });
    expect(cleanupErrors, "Owned barrier/hold cleanup must be confirmed").toEqual([]);
  }
}
