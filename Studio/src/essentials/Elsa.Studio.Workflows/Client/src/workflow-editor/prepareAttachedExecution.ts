import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { browseActivityExecutions, getActivityExecutionInspection, type ActivityExecutionBrowsePage } from "../api/runtime";
import { getActivityExecutionValuePayload } from "../api/activityExecutionValuePayload";
import { findExecutableNodeFacts, type ExecutableActivityGraph } from "../executableGraph";
import { buildInputInspectionRows, inputRuntimeSnapshot, readDeclaredActivityInputs } from "../inputInspectionRows";
import type { ActivityCatalogItem, ActivityExecutionInspection, ActivityExecutionStateSummary } from "../workflowTypes";
import type { loadExecutionScope } from "./executionScope";
import { readVisibleActivityExecutions } from "./scopedExecutionEvidence";
import { canAutomaticallyResolveValue, runtimeValueContextId, runtimeValuePayloadKey, type PreparedRuntimeValue } from "./RuntimeValueEvidence";

export interface PreparedAttachedExecution {
  /** Observation revision captured while preparing this selection. */
  observationRevision?: number;
  contextIdentity: number | string;
  workflowExecutionId: string;
  execution: ActivityExecutionStateSummary | null;
  inspection: ActivityExecutionInspection | null;
  values: ReadonlyMap<string, PreparedRuntimeValue>;
  page?: ActivityExecutionBrowsePage;
  scopePage?: { executionId: string; page: ActivityExecutionBrowsePage };
  canvas?: {
    contextIdentity: number | string;
    workflowExecutionId: string;
    ancestorId?: string;
    selectedActivityExecutionId: string;
    evidence: Awaited<ReturnType<typeof readVisibleActivityExecutions>>;
  };
}

/** Prepare one bounded destination while the confirmed view remains interactive. No cross-selection cache. */
export async function prepareAttachedExecution(context: StudioEndpointContext, workflowExecutionId: string,
  selected: ActivityExecutionStateSummary, scope: Awaited<ReturnType<typeof loadExecutionScope>>, graph: ExecutableActivityGraph,
  nodeId: string | null, signal: AbortSignal, canvasOwnerId?: string, visibleNodeIds?: readonly string[], catalog: readonly ActivityCatalogItem[] = []): Promise<PreparedAttachedExecution> {
  const facts = nodeId ? graph.factsByNodeId.get(nodeId) : undefined;
  const owner = [...scope.scopes].reverse().find(item => item.execution.authoredActivityId === canvasOwnerId)?.execution;
  const ancestorId = owner?.activityExecutionId;
  let execution: ActivityExecutionStateSummary | null = selected.authoredActivityId === nodeId ? selected : null;
  let page: ActivityExecutionBrowsePage | undefined;
  if (!execution && facts) {
    page = await browseActivityExecutions(context, workflowExecutionId, { executableNodeId: facts.executableNodeId,
      ...(ancestorId ? { ancestorActivityExecutionId: ancestorId } : {}), limit: 1 }, signal);
    // Repeated activities still require a deliberate occurrence choice.
    if (page.totalCount === 1) execution = page.items[0]?.execution ?? null;
  }
  const pagePromise = execution && !page ? browseActivityExecutions(context, workflowExecutionId, { executableNodeId: execution.executableNodeId,
    ...(ancestorId ? { ancestorActivityExecutionId: ancestorId } : {}), anchorActivityExecutionId: execution.activityExecutionId }, signal) : Promise.resolve(page);
  const scopePagePromise = owner ? Promise.resolve(scope.pages.get(owner.activityExecutionId)
    ?? browseActivityExecutions(context, workflowExecutionId, { executableNodeId: owner.executableNodeId,
      parentActivityExecutionId: owner.parentActivityExecutionId ?? undefined, anchorActivityExecutionId: owner.activityExecutionId }, signal)) : Promise.resolve(undefined);
  const canvasPromise = visibleNodeIds ? readVisibleActivityExecutions(context, workflowExecutionId,
    visibleNodeIds.flatMap(id => graph.factsByNodeId.get(id)?.executableNodeId ?? []), ancestorId, selected, signal) : null;
  const inspectionPromise = execution ? Promise.resolve(scope.inspections.find(item => item.activityExecutionId === execution!.activityExecutionId)
    ?? getActivityExecutionInspection(context, workflowExecutionId, execution.activityExecutionId, signal)) : Promise.resolve(null);
  const [inspection, canvas, executionPage, scopePage] = await Promise.all([inspectionPromise, canvasPromise, pagePromise, scopePagePromise]);
  if (execution && (inspection?.activityExecutionId !== execution.activityExecutionId || inspection.workflowExecutionId !== workflowExecutionId
    || inspection.executableNodeId !== execution.executableNodeId || execution.workflowExecutionId !== workflowExecutionId)) {
    throw Object.assign(new Error("Execution evidence identity did not match the requested occurrence."), { status: 404 });
  }
  if (canvas?.failed) throw Object.assign(new Error("Execution overlays could not be loaded. Retry the selection."), { status: canvas.denied ? 403 : 0 });
  const values = new Map<string, PreparedRuntimeValue>();
  if (inspection) {
    const snapshots = inspection.valueSnapshots ?? [];
    const inputFacts = findExecutableNodeFacts(graph, execution);
    const inputs = buildInputInspectionRows({
      declarations: readDeclaredActivityInputs(catalog.find(item => item.activityTypeKey === inspection.activityType)?.inputs),
      authoredSources: inputFacts?.authoredInputs ?? [], compiledBindings: inputFacts?.inputBindings ?? [],
      authoredSourceAccess: inputFacts?.authoredInputsAccess,
      evaluations: snapshots.filter(item => item.subject === "ActivityInput")
    }).flatMap(row => inputRuntimeSnapshot(row) ?? []);
    const visibleValues = [...inputs, ...snapshots.filter(item => item.subject === "ActivityOutput")];
    await Promise.all([...new Map(visibleValues.filter(canAutomaticallyResolveValue).map(snapshot => [runtimeValuePayloadKey(snapshot), snapshot])).values()].map(async snapshot => {
      const key = runtimeValuePayloadKey(snapshot);
      if (values.has(key)) return;
      try {
        const response = await getActivityExecutionValuePayload(context, workflowExecutionId, inspection.activityExecutionId,
          snapshot.evidenceId!.trim(), snapshot.captureMode, signal);
        values.set(key, { status: "ready", payload: response.payload });
      } catch (error) {
        const status = typeof error === "object" && error !== null && "status" in error ? Number(error.status) : 0;
        if (signal.aborted || status === 401 || status === 403) throw error;
        values.set(key, { status: "failed", error: error instanceof Error ? error.message : "Captured value could not be loaded." });
      }
    }));
  }
  const contextIdentity = runtimeValueContextId(context);
  return { contextIdentity, workflowExecutionId, execution, inspection, values, page: executionPage,
    ...(scopePage && owner ? { scopePage: { executionId: owner.activityExecutionId, page: scopePage } } : {}),
    ...(canvas ? { canvas: { contextIdentity, workflowExecutionId, ancestorId, selectedActivityExecutionId: selected.activityExecutionId, evidence: canvas } } : {}) };
}
