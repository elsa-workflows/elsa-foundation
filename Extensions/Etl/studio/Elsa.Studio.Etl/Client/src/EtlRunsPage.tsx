import { appendUnique } from "./paging";
import React, { useEffect, useRef, useState } from "react";
import type { StudioDialogApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  cancelEtlRun,
  classifyEtlApiError,
  EtlApiError,
  getEtlRun,
  invalidateEtlApiCapabilityCache,
  listEtlRunPartitions,
  listEtlRuns,
  type EtlApiFailureKind
} from "./etlApi";
import { EtlPartitionTable, EtlRunInspection, etlRunStatusLabel } from "./EtlRunInspection";
import type { EtlRunDetailView, EtlRunPartitionView, EtlRunSummaryView, EtlRunTerminalResultView } from "./etlTypes";

const pageSize = 50;

interface RequestFailure {
  kind: EtlApiFailureKind;
  missingCapability: boolean;
}

export function EtlRunsPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const currentContext = useRef(context);
  currentContext.current = context;
  const scopeGeneration = useRef(0);
  const selectedRunRef = useRef<string | null>(null);
  const listController = useRef<AbortController | null>(null);
  const listMoreController = useRef<AbortController | null>(null);
  const detailController = useRef<AbortController | null>(null);
  const partitionController = useRef<AbortController | null>(null);
  const partitionMoreController = useRef<AbortController | null>(null);
  const cancelController = useRef<AbortController | null>(null);
  const cancelOperation = useRef<object | null>(null);
  const listGeneration = useRef(0);
  const detailGeneration = useRef(0);
  const partitionGeneration = useRef(0);

  const [loadedContext, setLoadedContext] = useState<StudioEndpointContext | null>(null);
  const [runs, setRuns] = useState<EtlRunSummaryView[]>([]);
  const [runsCursor, setRunsCursor] = useState<string | null>(null);
  const [runsLoading, setRunsLoading] = useState(true);
  const [runsMoreLoading, setRunsMoreLoading] = useState(false);
  const [runsFailure, setRunsFailure] = useState<RequestFailure | null>(null);
  const [runsMoreFailure, setRunsMoreFailure] = useState<RequestFailure | null>(null);
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null);
  const [runDetail, setRunDetail] = useState<EtlRunDetailView | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailFailure, setDetailFailure] = useState<RequestFailure | null>(null);
  const [partitions, setPartitions] = useState<EtlRunPartitionView[]>([]);
  const [partitionsCursor, setPartitionsCursor] = useState<string | null>(null);
  const [partitionsLoading, setPartitionsLoading] = useState(false);
  const [partitionsMoreLoading, setPartitionsMoreLoading] = useState(false);
  const [partitionsFailure, setPartitionsFailure] = useState<RequestFailure | null>(null);
  const [partitionsMoreFailure, setPartitionsMoreFailure] = useState<RequestFailure | null>(null);
  const [refreshSelection, setRefreshSelection] = useState(0);
  const [cancelling, setCancelling] = useState(false);
  const [cancelMessage, setCancelMessage] = useState<string | null>(null);
  const [cancelFailure, setCancelFailure] = useState<RequestFailure | null>(null);

  const contextIsLoaded = loadedContext === context;
  const visibleRuns = contextIsLoaded ? runs : [];
  const visibleDetail = contextIsLoaded && runDetail?.runId === selectedRunId ? runDetail : null;
  const visiblePartitions = contextIsLoaded && runDetail?.runId === selectedRunId ? partitions : [];

  useEffect(() => {
    const generation = ++scopeGeneration.current;
    listController.current?.abort();
    listMoreController.current?.abort();
    detailController.current?.abort();
    partitionController.current?.abort();
    partitionMoreController.current?.abort();
    cancelController.current?.abort();
    cancelOperation.current = null;
    selectedRunRef.current = null;
    setLoadedContext(null);
    setRuns([]);
    setRunsCursor(null);
    setRunsLoading(true);
    setRunsMoreLoading(false);
    setRunsFailure(null);
    setRunsMoreFailure(null);
    setSelectedRunId(null);
    setRunDetail(null);
    setDetailLoading(false);
    setDetailFailure(null);
    setPartitions([]);
    setPartitionsCursor(null);
    setPartitionsLoading(false);
    setPartitionsMoreLoading(false);
    setPartitionsFailure(null);
    setPartitionsMoreFailure(null);
    setCancelling(false);
    setCancelMessage(null);
    setCancelFailure(null);
    void loadFirstRunsPage(context, generation, true);

    return () => {
      listController.current?.abort();
      listMoreController.current?.abort();
      detailController.current?.abort();
      partitionController.current?.abort();
      partitionMoreController.current?.abort();
      cancelController.current?.abort();
      if (scopeGeneration.current === generation) scopeGeneration.current++;
      cancelOperation.current = null;
      selectedRunRef.current = null;
    };
  }, [context]);

  useEffect(() => {
    const runId = selectedRunId;
    if (!contextIsLoaded || !runId) {
      setDetailLoading(false);
      setPartitionsLoading(false);
      return;
    }

    const detailRequest = new AbortController();
    const detailRequestId = ++detailGeneration.current;
    detailController.current?.abort();
    detailController.current = detailRequest;
    setDetailLoading(true);
    setDetailFailure(null);
    void getEtlRun(context, runId, detailRequest.signal)
      .then(detail => {
        if (!isCurrentDetail(context, runId, detailRequest, detailRequestId)) return;
        setRunDetail(detail);
      })
      .catch(error => {
        if (!isCurrentDetail(context, runId, detailRequest, detailRequestId)) return;
        setDetailFailure(failureOf(error));
      })
      .finally(() => {
        if (isCurrentDetail(context, runId, detailRequest, detailRequestId)) setDetailLoading(false);
      });

    const partitionRequest = new AbortController();
    const partitionRequestId = ++partitionGeneration.current;
    partitionController.current?.abort();
    partitionMoreController.current?.abort();
    partitionController.current = partitionRequest;
    setPartitions([]);
    setPartitionsCursor(null);
    setPartitionsLoading(true);
    setPartitionsMoreLoading(false);
    setPartitionsFailure(null);
    setPartitionsMoreFailure(null);
    void listEtlRunPartitions(context, runId, { limit: pageSize }, partitionRequest.signal)
      .then(page => {
        if (!isCurrentPartition(context, runId, partitionRequest, partitionRequestId)) return;
        setPartitions(page.items);
        setPartitionsCursor(page.nextCursor);
      })
      .catch(error => {
        if (!isCurrentPartition(context, runId, partitionRequest, partitionRequestId)) return;
        setPartitionsFailure(failureOf(error));
      })
      .finally(() => {
        if (isCurrentPartition(context, runId, partitionRequest, partitionRequestId)) setPartitionsLoading(false);
      });

    return () => {
      detailRequest.abort();
      partitionRequest.abort();
    };
  }, [context, contextIsLoaded, selectedRunId, refreshSelection]);

  function isCurrentList(requestContext: StudioEndpointContext, generation: number, controller: AbortController) {
    return !controller.signal.aborted && currentContext.current === requestContext && scopeGeneration.current === generation;
  }

  function isCurrentDetail(requestContext: StudioEndpointContext, runId: string, controller: AbortController, generation: number) {
    return !controller.signal.aborted && currentContext.current === requestContext && selectedRunRef.current === runId &&
      selectedRunId === runId && generation === detailGeneration.current;
  }

  function isCurrentPartition(requestContext: StudioEndpointContext, runId: string, controller: AbortController, generation: number) {
    return !controller.signal.aborted && currentContext.current === requestContext && selectedRunRef.current === runId &&
      selectedRunId === runId && generation === partitionGeneration.current;
  }

  async function loadFirstRunsPage(requestContext: StudioEndpointContext, scope: number, markContextLoaded: boolean) {
    const controller = new AbortController();
    listController.current?.abort();
    listMoreController.current?.abort();
    listController.current = controller;
    const requestId = ++listGeneration.current;
    setRunsLoading(true);
    setRunsMoreLoading(false);
    setRunsFailure(null);
    setRunsMoreFailure(null);
    try {
      const page = await listEtlRuns(requestContext, { limit: pageSize }, controller.signal);
      if (!isCurrentList(requestContext, scope, controller) || requestId !== listGeneration.current) return;
      setRuns(page.items);
      setRunsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentList(requestContext, scope, controller) || requestId !== listGeneration.current) return;
      setRunsFailure(failureOf(error));
    } finally {
      if (isCurrentList(requestContext, scope, controller) && requestId === listGeneration.current) {
        setRunsLoading(false);
        if (markContextLoaded) setLoadedContext(requestContext);
      }
    }
  }

  async function loadMoreRuns() {
    if (!runsCursor || runsMoreLoading || runsLoading || !contextIsLoaded) return;
    const requestContext = context;
    const scope = scopeGeneration.current;
    const cursor = runsCursor;
    const controller = new AbortController();
    listMoreController.current?.abort();
    listMoreController.current = controller;
    const requestId = ++listGeneration.current;
    setRunsMoreLoading(true);
    setRunsMoreFailure(null);
    try {
      const page = await listEtlRuns(requestContext, { limit: pageSize, cursor }, controller.signal);
      if (!isCurrentList(requestContext, scope, controller) || requestId !== listGeneration.current) return;
      setRuns(previous => appendUnique(previous, page.items, item => item.runId));
      setRunsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentList(requestContext, scope, controller) || requestId !== listGeneration.current) return;
      setRunsMoreFailure(failureOf(error));
    } finally {
      if (isCurrentList(requestContext, scope, controller) && requestId === listGeneration.current) setRunsMoreLoading(false);
    }
  }

  async function loadMorePartitions() {
    if (!selectedRunId || !partitionsCursor || partitionsMoreLoading || partitionsLoading || !contextIsLoaded) return;
    const requestContext = context;
    const runId = selectedRunId;
    const generation = partitionGeneration.current;
    const cursor = partitionsCursor;
    const controller = new AbortController();
    partitionMoreController.current?.abort();
    partitionMoreController.current = controller;
    setPartitionsMoreLoading(true);
    setPartitionsMoreFailure(null);
    try {
      const page = await listEtlRunPartitions(requestContext, runId, { limit: pageSize, cursor }, controller.signal);
      if (!isCurrentPartition(requestContext, runId, controller, generation)) return;
      setPartitions(previous => appendUnique(previous, page.items, item => String(item.partitionOrdinal)));
      setPartitionsCursor(page.nextCursor);
    } catch (error) {
      if (!isCurrentPartition(requestContext, runId, controller, generation)) return;
      setPartitionsMoreFailure(failureOf(error));
    } finally {
      if (!controller.signal.aborted && currentContext.current === requestContext && selectedRunRef.current === runId && generation === partitionGeneration.current) setPartitionsMoreLoading(false);
    }
  }

  function selectRun(runId: string) {
    cancelController.current?.abort();
    cancelOperation.current = null;
    selectedRunRef.current = runId;
    setSelectedRunId(runId);
    setCancelling(false);
    setCancelMessage(null);
    setCancelFailure(null);
  }

  function refresh() {
    const requestContext = context;
    const scope = scopeGeneration.current;
    if (hasMissingCapabilityFailure()) invalidateEtlApiCapabilityCache(requestContext);
    void loadFirstRunsPage(requestContext, scope, false);
    if (selectedRunRef.current && currentContext.current === requestContext && scopeGeneration.current === scope)
      setRefreshSelection(value => value + 1);
  }

  function hasMissingCapabilityFailure() {
    return runsFailure?.missingCapability === true || runsMoreFailure?.missingCapability === true ||
      detailFailure?.missingCapability === true || partitionsFailure?.missingCapability === true ||
      partitionsMoreFailure?.missingCapability === true;
  }

  function retry(failure: RequestFailure, action: () => void) {
    if (failure.missingCapability) invalidateEtlApiCapabilityCache(context);
    action();
  }

  async function cancelSelectedRun() {
    const run = visibleDetail;
    if (!run || !isActive(run) || cancelOperation.current) return;
    const requestContext = context;
    const runId = run.runId;
    const operation = {};
    cancelOperation.current = operation;
    setCancelling(true);
    setCancelMessage(null);
    setCancelFailure(null);
    try {
      const confirmed = await dialogs.confirm({
        message: `Cancel ETL run ${runId}? Completed output writes are retained, and the run may finish before cancellation takes effect.`,
        confirmLabel: "Cancel run",
        tone: "danger"
      });
      if (!confirmed || !isCurrentOperation(requestContext, runId, operation)) return;

      const controller = new AbortController();
      cancelController.current?.abort();
      cancelController.current = controller;
      const result = await cancelEtlRun(requestContext, runId, controller.signal);
      if (controller.signal.aborted || !isCurrentOperation(requestContext, runId, operation)) return;

      const terminal = result.terminalResult;
      setCancelMessage(result.cancellationAccepted
        ? `Cancellation accepted. Actual terminal outcome: ${etlRunStatusLabel(terminal.outcome)}.`
        : `Cancellation did not win. Actual terminal outcome: ${etlRunStatusLabel(terminal.outcome)}.`);
      setRunDetail(previous => previous?.runId === runId ? detailWithTerminalResult(previous, terminal) : previous);
      setRuns(previous => previous.map(item => item.runId === runId ? summaryWithTerminalResult(item, terminal) : item));
      setRefreshSelection(value => value + 1);
      void loadFirstRunsPage(requestContext, scopeGeneration.current, false);
    } catch (error) {
      if (currentContext.current === requestContext && selectedRunRef.current === runId && cancelOperation.current === operation)
        setCancelFailure(failureOf(error));
    } finally {
      if (currentContext.current === requestContext && selectedRunRef.current === runId && cancelOperation.current === operation) {
        cancelOperation.current = null;
        setCancelling(false);
      }
    }
  }

  function isCurrentOperation(requestContext: StudioEndpointContext, runId: string, operation: object) {
    return currentContext.current === requestContext && selectedRunRef.current === runId && cancelOperation.current === operation;
  }

  return <section className="etl-shell etl-runs-page" aria-label="ETL runs">
    <header className="etl-runs-header">
      <div>
        <p className="etl-editor-eyebrow">Runtime</p>
        <h1>ETL runs</h1>
        <p>Inspect durable run progress, partitions, and cancellation outcomes.</p>
      </div>
      <button type="button" className="etl-refresh-button" onClick={refresh} disabled={!contextIsLoaded || runsLoading}>
        {runsLoading && visibleRuns.length > 0 ? "Refreshing…" : "Refresh"}
      </button>
    </header>

    <div className="etl-runs-layout">
      <section className="etl-runs-list" aria-labelledby="etl-runs-list-heading">
        <div className="etl-runs-section-heading"><h2 id="etl-runs-list-heading">Runs</h2>{contextIsLoaded && !runsLoading ? <span>{visibleRuns.length} loaded</span> : null}</div>
        {!contextIsLoaded || (runsLoading && visibleRuns.length === 0) ? <p role="status" aria-live="polite">Loading ETL runs…</p> : null}
        {runsFailure && contextIsLoaded ? <FailureNotice failure={runsFailure} subject="ETL runs" onRetry={() => retry(runsFailure, () => void loadFirstRunsPage(context, scopeGeneration.current, true))} /> : null}
        {contextIsLoaded && !runsLoading && !runsFailure && visibleRuns.length === 0 ? <p role="status">No ETL runs are available in this scope.</p> : null}
        {visibleRuns.length > 0 ? <ul className="etl-run-list">
          {visibleRuns.map(run => <li key={run.runId}>
            <button type="button" className="etl-run-select" aria-pressed={selectedRunId === run.runId} onClick={() => selectRun(run.runId)}>
              <span><code>{run.runId}</code><small>{run.selection}</small></span>
              <span className="etl-run-status">{etlRunStatusLabel(run.status)}</span>
              <span className="etl-run-counts">{run.acceptedRecords} accepted · {run.rejectedRecords} rejected · {run.filteredRecords} filtered</span>
            </button>
          </li>)}
        </ul> : null}
        {runsCursor && contextIsLoaded ? <button type="button" disabled={runsMoreLoading || runsLoading} onClick={() => void loadMoreRuns()}>
          {runsMoreLoading ? "Loading more runs…" : "Load more runs"}
        </button> : null}
        {runsMoreFailure ? <FailureNotice failure={runsMoreFailure} subject="more ETL runs" onRetry={() => retry(runsMoreFailure, () => void loadMoreRuns())} /> : null}
      </section>

      <section className="etl-run-detail" aria-label="Selected ETL run">
        {!selectedRunId ? <p role="status">Select a run to inspect its details and partition progress.</p> : null}
        {selectedRunId && detailLoading && !visibleDetail ? <p role="status" aria-live="polite">Loading run details…</p> : null}
        {detailFailure && selectedRunId ? <FailureNotice failure={detailFailure} subject="run details" onRetry={() => retry(detailFailure, () => setRefreshSelection(value => value + 1))} /> : null}
        {visibleDetail ? <>
          <div className="etl-run-actions">
            <h2>Run inspection</h2>
            {isActive(visibleDetail) ? <button type="button" className="etl-cancel-run" disabled={cancelling} onClick={() => void cancelSelectedRun()}>
              {cancelling ? "Cancelling…" : "Cancel run"}
            </button> : null}
          </div>
          {detailLoading ? <p role="status" aria-live="polite">Refreshing run details…</p> : null}
          {cancelMessage ? <p className="etl-run-notice" role="status" aria-live="polite">{cancelMessage}</p> : null}
          {cancelFailure ? <FailureNotice failure={cancelFailure} subject="run cancellation" onRetry={() => retry(cancelFailure, () => void cancelSelectedRun())} /> : null}
          <EtlRunInspection run={visibleDetail} />
          <section className="etl-run-partitions" aria-labelledby="etl-run-partitions-heading">
            <div className="etl-runs-section-heading"><h3 id="etl-run-partitions-heading">Partition progress</h3>{partitionsLoading ? <span role="status">Loading…</span> : null}</div>
            {partitionsFailure ? <FailureNotice failure={partitionsFailure} subject="partition progress" onRetry={() => retry(partitionsFailure, () => setRefreshSelection(value => value + 1))} /> : null}
            {partitionsLoading && visiblePartitions.length === 0 ? <p role="status" aria-live="polite">Loading partition progress…</p> : null}
            {!partitionsLoading && !partitionsFailure ? <EtlPartitionTable partitions={visiblePartitions} /> : null}
            {partitionsCursor ? <button type="button" disabled={partitionsMoreLoading || partitionsLoading} onClick={() => void loadMorePartitions()}>
              {partitionsMoreLoading ? "Loading more partitions…" : "Load more partitions"}
            </button> : null}
            {partitionsMoreFailure ? <FailureNotice failure={partitionsMoreFailure} subject="more partition progress" onRetry={() => retry(partitionsMoreFailure, () => void loadMorePartitions())} /> : null}
          </section>
        </> : null}
      </section>
    </div>
  </section>;
}

function FailureNotice({ failure, subject, onRetry }: { failure: RequestFailure; subject: string; onRetry(): void }) {
  const message = failure.missingCapability
    ? "This host does not advertise the required ETL runtime capability."
    : failure.kind === "forbidden"
      ? `You do not have permission to access ${subject}.`
      : failure.kind === "unavailable"
        ? `The ETL runtime provider is unavailable for ${subject}.`
        : failure.kind === "not-found"
          ? `The requested ${subject} is no longer available in this scope.`
          : `Could not load ${subject}.`;
  return <div className="etl-run-error" role="alert"><p>{message}</p><button type="button" onClick={onRetry}>Retry</button></div>;
}

function failureOf(error: unknown): RequestFailure {
  const missingCapability = error instanceof EtlApiError && (
    error.code === "etl.capability-unavailable" || error.code === "etl.capability-version-unsupported"
  );
  return { kind: classifyEtlApiError(error), missingCapability };
}

function isActive(run: EtlRunDetailView) {
  return (run.status === "preparing" || run.status === "running") && run.terminalResult === null;
}

function detailWithTerminalResult(run: EtlRunDetailView, terminal: EtlRunTerminalResultView): EtlRunDetailView {
  return {
    ...run,
    status: terminal.outcome,
    acceptedRecords: terminal.acceptedRecords,
    rejectedRecords: terminal.rejectedRecords,
    filteredRecords: terminal.filteredRecords,
    diagnostic: terminal.diagnostic,
    terminalResult: terminal
  };
}

function summaryWithTerminalResult(run: EtlRunSummaryView, terminal: EtlRunTerminalResultView): EtlRunSummaryView {
  return {
    ...run,
    status: terminal.outcome,
    acceptedRecords: terminal.acceptedRecords,
    rejectedRecords: terminal.rejectedRecords,
    filteredRecords: terminal.filteredRecords,
    diagnostic: terminal.diagnostic
  };
}
