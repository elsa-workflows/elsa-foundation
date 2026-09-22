import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent as ReactKeyboardEvent
} from "react";
import {
  AlertTriangle,
  ChevronRight,
  CornerDownRight,
  GitBranch,
  RefreshCw,
  RotateCcw
} from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import {
  getActivityExecutionDescendants,
  getActivityExecutionInspection,
  getActivityExecutionLayout
} from "../api/runtime";
import { observeReusableActivity } from "../reusableActivityObservability";
import {
  classifyBoundaryCursorProblem,
  emptyBoundaryEvidenceSnapshot,
  executionOccurrenceLabel,
  markBoundaryEvidenceStale,
  mergeBoundaryEvidencePage,
  runtimeEvidencePresentation,
  type BoundaryEvidenceSnapshot
} from "../runtimeBoundaryInspectorModel";
import type {
  ActivityExecutionHierarchyItem,
  ActivityExecutionInspection,
  ActivityExecutionInspectionValueSnapshot,
  ActivityExecutionLayout
} from "../workflowTypes";
import { WorkflowStatusBadge } from "./WorkflowStatusBadge";

type BoundaryPane = "structure" | "evidence";
type LoadStatus = "idle" | "loading" | "ready" | "failed";

interface BoundaryFrame {
  key: string;
  label: string;
  inspection: ActivityExecutionInspection;
  activePane: BoundaryPane;
  scrollTop: number;
  hierarchyScrollTop: number;
  layoutStatus: LoadStatus;
  layout: ActivityExecutionLayout | null;
  layoutError: string;
  evidenceStatus: LoadStatus;
  evidence: BoundaryEvidenceSnapshot;
  evidenceError: string;
  selectedActivityExecutionId: string | null;
  selectedStatus: LoadStatus;
  selectedInspection: ActivityExecutionInspection | null;
  selectedError: string;
}

const hierarchyPageLimit = 100;
const hierarchyInclude = ["outcomes", "bookmarks", "incidents"] as const;
const evidenceSummaryLimit = 25;

export function ReusableBoundaryInspector({
  context,
  inspection
}: {
  context: StudioEndpointContext;
  inspection: ActivityExecutionInspection;
}) {
  const rootKey = frameKey(inspection);
  const [frames, setFrames] = useState<BoundaryFrame[]>(() => [createFrame(inspection)]);
  const [openState, setOpenState] = useState<{ activityExecutionId: string | null; error: string }>({
    activityExecutionId: null,
    error: ""
  });
  const scrollRef = useRef<HTMLDivElement>(null);
  const restoreScrollRef = useRef<number | null>(null);
  const openControllerRef = useRef<AbortController | null>(null);
  const selectionControllerRef = useRef<AbortController | null>(null);
  const layoutControllerRef = useRef<AbortController | null>(null);
  const evidenceControllerRef = useRef<AbortController | null>(null);
  const activeFrame = frames.at(-1)!;

  useEffect(() => {
    setFrames([createFrame(inspection)]);
    setOpenState({ activityExecutionId: null, error: "" });
    openControllerRef.current?.abort();
    selectionControllerRef.current?.abort();
    layoutControllerRef.current?.abort();
    evidenceControllerRef.current?.abort();
  }, [rootKey, inspection]);

  const updateFrame = useCallback((key: string, update: (frame: BoundaryFrame) => BoundaryFrame) => {
    setFrames(current => current.map(frame => frame.key === key ? update(frame) : frame));
  }, []);

  const loadLayout = useCallback((frame: BoundaryFrame) => {
    if (frame.layoutStatus !== "idle") return;
    const key = frame.key;
    const boundary = frame.inspection.boundary;
    if (!boundary?.layoutAvailable) {
      updateFrame(key, current => ({ ...current, layoutStatus: "ready", layout: null }));
      return;
    }

    layoutControllerRef.current?.abort();
    const controller = new AbortController();
    layoutControllerRef.current = controller;
    updateFrame(key, current => ({ ...current, layoutStatus: "loading", layoutError: "" }));
    getActivityExecutionLayout(
      context,
      frame.inspection.workflowExecutionId,
      frame.inspection.activityExecutionId,
      controller.signal
    ).then(
      layout => {
        if (controller.signal.aborted) return;
        updateFrame(key, current => ({ ...current, layoutStatus: "ready", layout }));
        observeReusableActivity({
          event: "boundary-inspection",
          surface: "run-workbench",
          outcome: "ready",
          layoutMode: layout.nodes.some(node => node.hasPinnedGeometry) ? "pinned" : "automatic"
        });
      },
      error => {
        if (controller.signal.aborted || isAbortError(error)) return;
        updateFrame(key, current => ({
          ...current,
          layoutStatus: "failed",
          layoutError: errorMessage(error)
        }));
        observeReusableActivity({
          event: "boundary-inspection",
          surface: "run-workbench",
          outcome: "failed",
          layoutMode: "unavailable"
        });
      }
    );
  }, [context, updateFrame]);

  const loadFirstEvidencePage = useCallback((frame: BoundaryFrame) => {
    if (frame.evidenceStatus !== "idle") return;
    const key = frame.key;
    evidenceControllerRef.current?.abort();
    const controller = new AbortController();
    evidenceControllerRef.current = controller;
    updateFrame(key, current => ({ ...current, evidenceStatus: "loading", evidenceError: "" }));
    getActivityExecutionDescendants(
      context,
      frame.inspection.workflowExecutionId,
      frame.inspection.activityExecutionId,
      { limit: hierarchyPageLimit, include: [...hierarchyInclude] },
      controller.signal
    ).then(
      page => {
        if (controller.signal.aborted) return;
        updateFrame(key, current => ({
          ...current,
          evidenceStatus: "ready",
          evidence: mergeBoundaryEvidencePage(current.evidence, page)
        }));
      },
      error => {
        if (controller.signal.aborted || isAbortError(error)) return;
        updateFrame(key, current => ({
          ...current,
          evidenceStatus: "failed",
          evidenceError: errorMessage(error)
        }));
      }
    );
  }, [context, updateFrame]);

  useEffect(() => {
    loadLayout(activeFrame);
    loadFirstEvidencePage(activeFrame);
    return () => {
      layoutControllerRef.current?.abort();
      evidenceControllerRef.current?.abort();
    };
    // Each occurrence owns one independently cancellable layout request and hierarchy request.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeFrame.key, loadFirstEvidencePage, loadLayout]);

  useEffect(() => {
    const scrollTop = restoreScrollRef.current ?? activeFrame.scrollTop;
    restoreScrollRef.current = null;
    requestAnimationFrame(() => {
      if (scrollRef.current) scrollRef.current.scrollTop = scrollTop;
    });
    // Scroll position is restored when the occurrence or pane changes, not while the user scrolls.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeFrame.key, activeFrame.activePane]);

  useEffect(() => () => {
    openControllerRef.current?.abort();
    selectionControllerRef.current?.abort();
    layoutControllerRef.current?.abort();
    evidenceControllerRef.current?.abort();
  }, []);

  const setPane = (activePane: BoundaryPane) => {
    updateFrame(activeFrame.key, frame => ({ ...frame, activePane }));
  };

  const restartSnapshot = () => {
    selectionControllerRef.current?.abort();
    evidenceControllerRef.current?.abort();
    const nextFrame = {
      ...activeFrame,
      evidenceStatus: "idle",
      evidence: emptyBoundaryEvidenceSnapshot(),
      evidenceError: "",
      selectedActivityExecutionId: null,
      selectedStatus: "idle",
      selectedInspection: null,
      selectedError: ""
    } satisfies BoundaryFrame;
    updateFrame(activeFrame.key, () => nextFrame);
    loadFirstEvidencePage(nextFrame);
  };

  const loadMore = () => {
    const cursor = activeFrame.evidence.nextCursor;
    if (!cursor || activeFrame.evidenceStatus === "loading") return;
    const key = activeFrame.key;
    evidenceControllerRef.current?.abort();
    const controller = new AbortController();
    evidenceControllerRef.current = controller;
    updateFrame(key, frame => ({ ...frame, evidenceStatus: "loading", evidenceError: "" }));
    getActivityExecutionDescendants(
      context,
      activeFrame.inspection.workflowExecutionId,
      activeFrame.inspection.activityExecutionId,
      { cursor, limit: activeFrame.evidence.effectiveLimit, include: [...hierarchyInclude] },
      controller.signal
    ).then(
      page => {
        if (controller.signal.aborted) return;
        updateFrame(key, frame => ({
          ...frame,
          evidenceStatus: "ready",
          evidence: mergeBoundaryEvidencePage(frame.evidence, page)
        }));
      },
      error => {
        if (controller.signal.aborted || isAbortError(error)) return;
        const cursorProblem = classifyBoundaryCursorProblem(error);
        updateFrame(key, frame => ({
          ...frame,
          evidenceStatus: cursorProblem ? "ready" : "failed",
          evidence: cursorProblem ? markBoundaryEvidenceStale(frame.evidence, cursorProblem) : frame.evidence,
          evidenceError: cursorProblem ? "" : errorMessage(error)
        }));
      }
    );
  };

  const selectExecution = (activityExecutionId: string) => {
    selectionControllerRef.current?.abort();
    const controller = new AbortController();
    selectionControllerRef.current = controller;
    const key = activeFrame.key;
    updateFrame(key, frame => ({
      ...frame,
      selectedActivityExecutionId: activityExecutionId,
      selectedStatus: "loading",
      selectedInspection: null,
      selectedError: ""
    }));
    getActivityExecutionInspection(
      context,
      activeFrame.inspection.workflowExecutionId,
      activityExecutionId,
      controller.signal
    ).then(
      selectedInspection => {
        if (controller.signal.aborted) return;
        updateFrame(key, frame => ({
          ...frame,
          selectedStatus: "ready",
          selectedInspection
        }));
      },
      error => {
        if (controller.signal.aborted || isAbortError(error)) return;
        updateFrame(key, frame => ({
          ...frame,
          selectedStatus: "failed",
          selectedError: errorMessage(error)
        }));
      }
    );
  };

  const openBoundary = (item: ActivityExecutionHierarchyItem) => {
    if (!item.boundary || openState.activityExecutionId) return;
    openControllerRef.current?.abort();
    const controller = new AbortController();
    openControllerRef.current = controller;
    setOpenState({ activityExecutionId: item.activityExecutionId, error: "" });
    getActivityExecutionInspection(
      context,
      item.workflowExecutionId,
      item.activityExecutionId,
      controller.signal
    ).then(
      nestedInspection => {
        if (controller.signal.aborted) return;
        if (!nestedInspection.boundary) {
          setOpenState({
            activityExecutionId: null,
            error: "The selected occurrence is no longer an inspectable reusable boundary."
          });
          return;
        }
        restoreScrollRef.current = 0;
        setFrames(current => {
          const parent = current.at(-1);
          return parent
            ? [...current.slice(0, -1), prepareFrameForLeave(parent), createFrame(nestedInspection, hierarchyItemLabel(item))]
            : [createFrame(nestedInspection, hierarchyItemLabel(item))];
        });
        setOpenState({ activityExecutionId: null, error: "" });
      },
      error => {
        if (controller.signal.aborted || isAbortError(error)) return;
        setOpenState({ activityExecutionId: null, error: errorMessage(error) });
      }
    );
  };

  const navigateToFrame = (index: number) => {
    const target = frames[index];
    if (!target || index === frames.length - 1) return;
    openControllerRef.current?.abort();
    selectionControllerRef.current?.abort();
    restoreScrollRef.current = target.scrollTop;
    setFrames(current => current.slice(0, index + 1).map((frame, frameIndex) =>
      frameIndex === index ? prepareFrameForLeave(frame) : frame));
  };

  const boundary = activeFrame.inspection.boundary!;
  return (
    <section className="wf-instance-section wf-boundary-evidence wf-boundary-inspector" style={boundaryStyles.inspector} aria-label="Reusable boundary Runtime Evidence">
      <header className="wf-boundary-inspector-heading" style={boundaryStyles.splitHeader}>
        <div>
          <span className="wf-boundary-eyebrow">Reusable activity boundary</span>
          <h4>{activeFrame.label}</h4>
        </div>
        <WorkflowStatusBadge
          status={activeFrame.inspection.status}
          subStatus={activeFrame.inspection.subStatus}
        />
      </header>

      <nav className="wf-boundary-breadcrumbs" style={boundaryStyles.breadcrumbs} aria-label="Reusable boundary occurrence breadcrumbs">
        {frames.map((frame, index) => (
          <span key={frame.key} style={boundaryStyles.inlineCluster}>
            {index > 0 ? <ChevronRight size={12} aria-hidden="true" /> : null}
            <button
              type="button"
              aria-current={index === frames.length - 1 ? "page" : undefined}
              onClick={() => navigateToFrame(index)}
            >
              {frame.label}
              <small>Execution {frame.inspection.activityExecutionId}</small>
            </button>
          </span>
        ))}
      </nav>

      <div className="wf-boundary-summary" style={boundaryStyles.responsiveGrid}>
        <BoundaryLifecycle inspection={activeFrame.inspection} />
        <DescendantAggregate inspection={activeFrame.inspection} />
      </div>

      <div className="wf-boundary-pane-tabs" style={boundaryStyles.twoColumnTabs} role="tablist" aria-label="Boundary inspector panes">
        <button
          type="button"
          role="tab"
          aria-selected={activeFrame.activePane === "structure"}
          style={paneButtonStyle(activeFrame.activePane === "structure")}
          onClick={() => setPane("structure")}
        >
          Pinned structure
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeFrame.activePane === "evidence"}
          style={paneButtonStyle(activeFrame.activePane === "evidence")}
          onClick={() => setPane("evidence")}
        >
          Runtime Evidence
          {activeFrame.evidence.committedThroughSequence !== null
            ? ` (${activeFrame.evidence.items.length})`
            : ""}
        </button>
      </div>

      <div
        ref={scrollRef}
        className="wf-boundary-pane-scroll"
        style={boundaryStyles.paneScroll}
        onScroll={event => {
          const scrollTop = event.currentTarget.scrollTop;
          updateFrame(activeFrame.key, frame => ({ ...frame, scrollTop }));
        }}
      >
        {activeFrame.activePane === "structure" ? (
          <BoundaryStructurePane frame={activeFrame} onRetry={() => {
            const nextFrame = {
              ...activeFrame,
              layoutStatus: "idle" as const,
              layoutError: ""
            };
            updateFrame(activeFrame.key, () => nextFrame);
            loadLayout(nextFrame);
          }} />
        ) : (
          <BoundaryEvidencePane
            frame={activeFrame}
            openingActivityExecutionId={openState.activityExecutionId}
            openError={openState.error}
            onHierarchyScroll={hierarchyScrollTop => {
              updateFrame(activeFrame.key, frame => ({ ...frame, hierarchyScrollTop }));
            }}
            onSelect={selectExecution}
            onOpenBoundary={openBoundary}
            onLoadMore={loadMore}
            onRestart={restartSnapshot}
          />
        )}
      </div>

      {isCancelling(activeFrame.inspection) ? (
        <p className="wf-boundary-nonterminal" role="status">
          Cancellation cleanup is still committing. This boundary remains nonterminal until cleanup completes.
        </p>
      ) : null}
      <span className="wf-sr-only" style={boundaryStyles.visuallyHidden} aria-live="polite">
        {activeFrame.evidenceStatus === "loading"
          ? "Loading boundary Runtime Evidence."
          : activeFrame.evidenceStatus === "ready"
            ? `${activeFrame.evidence.items.length} committed descendant occurrences loaded.`
            : ""}
      </span>
      <span className="wf-sr-only" style={boundaryStyles.visuallyHidden}>
        Exact boundary {boundary.definitionId}, version {boundary.version}, execution {activeFrame.inspection.activityExecutionId}.
      </span>
    </section>
  );
}

function BoundaryLifecycle({ inspection }: { inspection: ActivityExecutionInspection }) {
  const boundary = inspection.boundary!;
  return (
    <section className="wf-boundary-layout wf-boundary-summary-card" aria-label="Boundary lifecycle">
      <h5>Boundary lifecycle</h5>
      <dl>
        <dt>Status</dt>
        <dd>{[inspection.status, inspection.subStatus].filter(Boolean).join(" · ")}</dd>
        <dt>Definition</dt>
        <dd>{boundary.definitionId}</dd>
        <dt>Exact version</dt>
        <dd>{boundary.version} <small>{boundary.definitionVersionId}</small></dd>
        <dt>Execution scope</dt>
        <dd>{boundary.executionScopeId}</dd>
        <dt>Attempt</dt>
        <dd>{inspection.attempt ? `${inspection.attempt.attemptNumber} of ${inspection.attempt.totalAttempts ?? "?"}` : "1"}</dd>
      </dl>
    </section>
  );
}

function DescendantAggregate({ inspection }: { inspection: ActivityExecutionInspection }) {
  const boundary = inspection.boundary!;
  const aggregate = boundary.aggregate;
  return (
    <section className="wf-boundary-layout wf-boundary-summary-card" aria-label="Descendant aggregate">
      <h5>Descendant aggregate</h5>
      <dl>
        <dt>Status</dt>
        <dd>{aggregate.status}</dd>
        <dt>Committed</dt>
        <dd>{boundary.committedDescendantCount}</dd>
        <dt>Completed / faulted</dt>
        <dd>{aggregate.completed} / {aggregate.faulted}</dd>
        <dt>Incidents / retries</dt>
        <dd>{aggregate.blockingIncidentCount} / {aggregate.retryCount}</dd>
        <dt>Last sequence</dt>
        <dd>{aggregate.lastExecutionSequence}</dd>
      </dl>
    </section>
  );
}

function BoundaryStructurePane({
  frame,
  onRetry
}: {
  frame: BoundaryFrame;
  onRetry(): void;
}) {
  if (frame.layoutStatus === "loading" || frame.layoutStatus === "idle") {
    return <p className="wf-muted" role="status">Loading pinned historical layout independently...</p>;
  }
  if (frame.layoutStatus === "failed") {
    return (
      <div className="wf-boundary-panel-message" style={boundaryStyles.panel} role="alert">
        <p>Authorized historical structure is unavailable. {frame.layoutError}</p>
        <button type="button" onClick={onRetry}><RotateCcw size={13} /> Retry structure</button>
      </div>
    );
  }
  if (!frame.layout) {
    return (
      <div className="wf-boundary-panel-message" style={boundaryStyles.panel}>
        <strong>No Layout Sidecar was captured.</strong>
        <p>The exact Executable remains inspectable with automatic layout; no Design version was substituted.</p>
      </div>
    );
  }

  return <HistoricalBoundaryLayout layout={frame.layout} />;
}

function HistoricalBoundaryLayout({ layout }: { layout: ActivityExecutionLayout }) {
  const positioned = layout.nodes.filter(node => node.hasPinnedGeometry);
  if (positioned.length === 0) {
    return (
      <div className="wf-boundary-layout wf-boundary-layout-list">
        <header style={boundaryStyles.stack}>
          <strong>Automatic historical structure</strong>
          <small>{layout.sourceReferenceId}</small>
        </header>
        {layout.nodes.map(node => (
          <div key={node.executableNodeId}>
            <span>{historicalNodeLabel(node)}</span>
            <code>{node.executableNodeId}</code>
          </div>
        ))}
      </div>
    );
  }

  const nodeById = new Map(positioned.map(node => [node.executableNodeId, node]));
  const minX = Math.min(...positioned.map(node => node.x));
  const minY = Math.min(...positioned.map(node => node.y));
  const maxX = Math.max(...positioned.map(node => node.x + (node.width ?? 180)));
  const maxY = Math.max(...positioned.map(node => node.y + (node.height ?? 64)));
  const padding = 28;
  const width = Math.max(320, maxX - minX + padding * 2);
  const height = Math.max(180, maxY - minY + padding * 2);
  const translateX = padding - minX;
  const translateY = padding - minY;

  return (
    <figure className="wf-boundary-layout-canvas" style={{ ...boundaryStyles.stack, margin: 0 }}>
      <figcaption style={boundaryStyles.splitHeader}>
        <span style={boundaryStyles.stack}>
          <strong>Pinned historical layout</strong>
          <small>{layout.selection} · {layout.sourceReferenceId}</small>
        </span>
        <small>{layout.nodes.length} nodes · {layout.connections.length} connections</small>
      </figcaption>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label={`Pinned historical boundary layout with ${layout.nodes.length} nodes`}
        style={boundaryStyles.layoutCanvas}
      >
        <g transform={`translate(${translateX} ${translateY})`}>
          {layout.connections.map((connection, index) => {
            const source = nodeById.get(connection.source.executableNodeId);
            const target = nodeById.get(connection.target.executableNodeId);
            if (!source || !target) return null;
            const sourceX = source.x + (source.width ?? 180);
            const sourceY = source.y + (source.height ?? 64) / 2;
            const targetX = target.x;
            const targetY = target.y + (target.height ?? 64) / 2;
            return (
              <path
                key={`${connection.source.executableNodeId}:${connection.target.executableNodeId}:${index}`}
                className="wf-boundary-layout-edge"
                d={`M ${sourceX} ${sourceY} C ${sourceX + 44} ${sourceY}, ${targetX - 44} ${targetY}, ${targetX} ${targetY}`}
                fill="none"
                stroke="var(--wf-border-strong)"
                strokeWidth="2"
              />
            );
          })}
          {positioned.map(node => {
            const nodeWidth = node.width ?? 180;
            const nodeHeight = node.height ?? 64;
            return (
              <g
                key={node.executableNodeId}
                className="wf-boundary-layout-node"
                transform={`translate(${node.x} ${node.y})`}
              >
                <title>{historicalNodeLabel(node)}</title>
                <rect width={nodeWidth} height={nodeHeight} rx="8" fill="var(--wf-panel)" stroke="var(--wf-border-strong)" />
                <text x="12" y="25" fill="var(--wf-text)" fontSize="11">{truncate(historicalNodeLabel(node), 24)}</text>
                <text className="wf-boundary-layout-node-id" x="12" y="45" fill="var(--wf-muted-text)" fontSize="8">
                  {truncate(node.executableNodeId, 28)}
                </text>
              </g>
            );
          })}
        </g>
      </svg>
      {layout.nestedBoundaries.length > 0 ? (
        <p className="wf-muted">
          {layout.nestedBoundaries.length} nested boundary occurrence{layout.nestedBoundaries.length === 1 ? "" : "s"} represented as one node each. Open one from Runtime Evidence to inspect it.
        </p>
      ) : null}
    </figure>
  );
}

function BoundaryEvidencePane({
  frame,
  openingActivityExecutionId,
  openError,
  onHierarchyScroll,
  onSelect,
  onOpenBoundary,
  onLoadMore,
  onRestart
}: {
  frame: BoundaryFrame;
  openingActivityExecutionId: string | null;
  openError: string;
  onHierarchyScroll(scrollTop: number): void;
  onSelect(activityExecutionId: string): void;
  onOpenBoundary(item: ActivityExecutionHierarchyItem): void;
  onLoadMore(): void;
  onRestart(): void;
}) {
  const evidence = frame.evidence;
  const hasLoadedEvidence = evidence.committedThroughSequence !== null;

  return (
    <div className="wf-boundary-evidence-pane" style={boundaryStyles.stack}>
      <header className="wf-boundary-evidence-toolbar" style={boundaryStyles.splitHeader}>
        <span style={boundaryStyles.stack}>
          <strong>Committed descendants</strong>
          <small>
            {hasLoadedEvidence
              ? `Committed through sequence ${evidence.committedThroughSequence}`
              : "Waiting for the first committed snapshot"}
          </small>
        </span>
        <button type="button" onClick={onRestart} disabled={frame.evidenceStatus === "loading"}>
          <RefreshCw size={13} /> Refresh snapshot
        </button>
      </header>

      {evidence.staleReason ? (
        <div className="wf-boundary-stale" style={boundaryStyles.warningPanel} role="alert">
          <AlertTriangle size={15} aria-hidden="true" />
          <span style={boundaryStyles.stack}>
            <strong>Loaded evidence is stale; it has not been mixed with a new snapshot.</strong>
            <small>{evidence.staleMessage}</small>
          </span>
          <button type="button" onClick={onRestart}>Restart from first page</button>
        </div>
      ) : null}
      {openError ? <p className="wf-boundary-error" role="alert">{openError}</p> : null}
      {frame.evidenceStatus === "failed" ? (
        <div className="wf-boundary-panel-message" style={boundaryStyles.panel} role="alert">
          <p>Descendant Runtime Evidence is unavailable. {frame.evidenceError}</p>
          <button type="button" onClick={onRestart}><RotateCcw size={13} /> Retry evidence</button>
        </div>
      ) : null}
      {!hasLoadedEvidence && frame.evidenceStatus === "loading"
        ? <p className="wf-muted" role="status">Loading the first descendant snapshot...</p>
        : null}
      {hasLoadedEvidence && evidence.items.length === 0
        ? <p className="wf-muted">No committed descendant occurrences are present in this snapshot.</p>
        : null}
      {evidence.items.length > 0 ? (
        <VirtualizedHierarchy
          items={evidence.items}
          selectedActivityExecutionId={frame.selectedActivityExecutionId}
          openingActivityExecutionId={openingActivityExecutionId}
          scrollTop={frame.hierarchyScrollTop}
          onScroll={onHierarchyScroll}
          onSelect={onSelect}
          onOpenBoundary={onOpenBoundary}
        />
      ) : null}
      {evidence.nextCursor && !evidence.staleReason ? (
        <button
          type="button"
          className="wf-boundary-load-more"
          disabled={frame.evidenceStatus === "loading"}
          onClick={onLoadMore}
        >
          {frame.evidenceStatus === "loading" ? "Loading next committed page..." : "Load next committed page"}
        </button>
      ) : null}
      {frame.selectedActivityExecutionId ? (
        <SelectedExecutionEvidence
          status={frame.selectedStatus}
          inspection={frame.selectedInspection}
          error={frame.selectedError}
          loadedItems={evidence.items}
          onSelect={onSelect}
        />
      ) : null}
    </div>
  );
}

const hierarchyRowHeight = 104;
const hierarchyViewportHeight = 416;
const hierarchyOverscan = 3;

function VirtualizedHierarchy({
  items,
  selectedActivityExecutionId,
  openingActivityExecutionId,
  scrollTop: restoredScrollTop,
  onScroll,
  onSelect,
  onOpenBoundary
}: {
  items: ActivityExecutionHierarchyItem[];
  selectedActivityExecutionId: string | null;
  openingActivityExecutionId: string | null;
  scrollTop: number;
  onScroll(scrollTop: number): void;
  onSelect(activityExecutionId: string): void;
  onOpenBoundary(item: ActivityExecutionHierarchyItem): void;
}) {
  const [scrollTop, setScrollTop] = useState(restoredScrollTop);
  const [activeIndex, setActiveIndex] = useState(() => Math.max(0, items.findIndex(item => item.activityExecutionId === selectedActivityExecutionId)));
  const viewportRef = useRef<HTMLDivElement>(null);
  const start = Math.max(0, Math.floor(scrollTop / hierarchyRowHeight) - hierarchyOverscan);
  const count = Math.ceil(hierarchyViewportHeight / hierarchyRowHeight) + hierarchyOverscan * 2;
  const visible = items.slice(start, start + count);

  useEffect(() => {
    setActiveIndex(current => Math.min(current, Math.max(0, items.length - 1)));
  }, [items.length]);

  useEffect(() => {
    const viewport = viewportRef.current;
    if (viewport && viewport.scrollTop !== restoredScrollTop) viewport.scrollTop = restoredScrollTop;
    setScrollTop(restoredScrollTop);
  }, [restoredScrollTop]);

  const focusIndex = (nextIndex: number) => {
    const bounded = Math.min(Math.max(0, nextIndex), items.length - 1);
    setActiveIndex(bounded);
    const nextTop = bounded * hierarchyRowHeight;
    const viewport = viewportRef.current;
    if (!viewport) return;
    if (nextTop < viewport.scrollTop) viewport.scrollTop = nextTop;
    else if (nextTop + hierarchyRowHeight > viewport.scrollTop + hierarchyViewportHeight) {
      viewport.scrollTop = nextTop + hierarchyRowHeight - hierarchyViewportHeight;
    }
    requestAnimationFrame(() => {
      viewport.querySelector<HTMLElement>(`[data-hierarchy-index="${bounded}"]`)?.focus();
    });
  };

  const onKeyDown = (event: ReactKeyboardEvent<HTMLElement>, index: number) => {
    if (event.currentTarget !== event.target) return;
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      onSelect(items[index].activityExecutionId);
      return;
    }
    const nextIndex =
      event.key === "ArrowDown" ? index + 1 :
        event.key === "ArrowUp" ? index - 1 :
          event.key === "Home" ? 0 :
            event.key === "End" ? items.length - 1 :
              null;
    if (nextIndex === null) return;
    event.preventDefault();
    focusIndex(nextIndex);
  };

  return (
    <div
      ref={viewportRef}
      className="wf-boundary-hierarchy-viewport"
      role="tree"
      aria-label="Committed descendant execution occurrences"
      style={{ ...boundaryStyles.hierarchyViewport, height: hierarchyViewportHeight }}
      onScroll={event => {
        const nextScrollTop = event.currentTarget.scrollTop;
        setScrollTop(nextScrollTop);
        onScroll(nextScrollTop);
      }}
    >
      <div className="wf-boundary-hierarchy-spacer" style={{ ...boundaryStyles.hierarchySpacer, height: items.length * hierarchyRowHeight }}>
        {visible.map((item, visibleIndex) => {
          const index = start + visibleIndex;
          const selected = selectedActivityExecutionId === item.activityExecutionId;
          return (
            <div
              key={item.activityExecutionId}
              className="wf-boundary-hierarchy-row"
              data-selected={selected}
              data-hierarchy-index={index}
              role="treeitem"
              aria-level={Math.max(1, item.relativeDepth)}
              aria-selected={selected}
              tabIndex={index === activeIndex ? 0 : -1}
              style={{
                ...boundaryStyles.hierarchyRow,
                height: hierarchyRowHeight,
                transform: `translateY(${index * hierarchyRowHeight}px)`,
                background: selected ? "var(--wf-accent-soft)" : "var(--wf-panel)"
              }}
              onFocus={() => setActiveIndex(index)}
              onClick={() => onSelect(item.activityExecutionId)}
              onKeyDown={event => onKeyDown(event, index)}
            >
              <span className="wf-boundary-hierarchy-sequence">{item.executionSequence}</span>
              <span className="wf-boundary-hierarchy-identity" style={boundaryStyles.stack}>
                <strong>{historicalActivityLabel(item.activityType)}</strong>
                <small>{executionOccurrenceLabel(item)}</small>
                <small>{item.activityType} · {item.activityTypeVersion}</small>
              </span>
              <span className="wf-boundary-hierarchy-state" style={boundaryStyles.stack}>
                <WorkflowStatusBadge status={item.status} subStatus={item.subStatus} />
                <small>{item.bookmarkCount} bookmarks · {item.incidentCount} incidents</small>
              </span>
              {item.boundary ? (
                <button
                  type="button"
                  className="wf-boundary-open"
                  disabled={openingActivityExecutionId !== null}
                  onClick={event => {
                    event.stopPropagation();
                    onOpenBoundary(item);
                  }}
                >
                  <CornerDownRight size={13} />
                  {openingActivityExecutionId === item.activityExecutionId ? "Opening..." : "Open boundary"}
                </button>
              ) : null}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function SelectedExecutionEvidence({
  status,
  inspection,
  error,
  loadedItems,
  onSelect
}: {
  status: LoadStatus;
  inspection: ActivityExecutionInspection | null;
  error: string;
  loadedItems: ActivityExecutionHierarchyItem[];
  onSelect(activityExecutionId: string): void;
}) {
  if (status === "loading") {
    return <section className="wf-boundary-selected" role="status">Loading canonical execution detail...</section>;
  }
  if (status === "failed") {
    return <section className="wf-boundary-selected" role="alert">Canonical execution detail is unavailable. {error}</section>;
  }
  if (!inspection) return null;

  const attempts = [
    inspection.attempt?.previousAttemptActivityExecutionId
      ? { label: "Previous attempt", id: inspection.attempt.previousAttemptActivityExecutionId }
      : null,
    inspection.attempt?.nextAttemptActivityExecutionId
      ? { label: "Next attempt", id: inspection.attempt.nextAttemptActivityExecutionId }
      : null
  ].filter((item): item is { label: string; id: string } => !!item);
  const causal = [
    inspection.provenance.parentActivityExecutionId
      ? { label: "Parent execution", id: inspection.provenance.parentActivityExecutionId }
      : null,
    inspection.provenance.schedulingActivityExecutionId
      ? { label: "Scheduling execution", id: inspection.provenance.schedulingActivityExecutionId }
      : null
  ].filter((item): item is { label: string; id: string } => !!item);
  const loadedIds = new Set(loadedItems.map(item => item.activityExecutionId));

  return (
    <section className="wf-boundary-selected" style={{ ...boundaryStyles.panel, ...boundaryStyles.stack }} aria-label="Selected descendant canonical detail">
      <header style={boundaryStyles.splitHeader}>
        <span style={boundaryStyles.stack}>
          <strong>{historicalActivityLabel(inspection.activityType)}</strong>
          <small>Canonical execution {inspection.activityExecutionId}</small>
        </span>
        <WorkflowStatusBadge status={inspection.status} subStatus={inspection.subStatus} />
      </header>
      <dl>
        <dt>Execution sequence</dt>
        <dd>{inspection.executionSequence}</dd>
        <dt>Exact activity type</dt>
        <dd>{inspection.activityType} · {inspection.activityTypeVersion}</dd>
        <dt>Attempt</dt>
        <dd>{inspection.attempt ? `${inspection.attempt.attemptNumber} of ${inspection.attempt.totalAttempts ?? "?"}` : "1"}</dd>
        <dt>Last committed</dt>
        <dd>{inspection.lastCommittedAt ?? "Not committed"}</dd>
      </dl>

      {attempts.length > 0 || causal.length > 0 ? (
        <div className="wf-boundary-causal-links" style={boundaryStyles.responsiveGrid} aria-label="Attempt and causal execution links">
          {[...attempts, ...causal].map(link => (
            <button type="button" key={`${link.label}:${link.id}`} onClick={() => onSelect(link.id)}>
              <GitBranch size={12} />
              {link.label}
              <small>{link.id}{loadedIds.has(link.id) ? " · loaded occurrence" : ""}</small>
            </button>
          ))}
        </div>
      ) : null}

      <BoundedEvidenceSummary
        title="Bookmarks"
        total={inspection.bookmarks.length}
        items={inspection.bookmarks.slice(0, evidenceSummaryLimit).map(bookmark => ({
          id: bookmark.bookmarkId,
          title: bookmark.stimulusType,
          detail: `Resume target ${bookmark.resumeTargetId} · created ${bookmark.createdAt}`
        }))}
      />
      <BoundedEvidenceSummary
        title="Incidents"
        total={inspection.incidents.length}
        items={inspection.incidents.slice(0, evidenceSummaryLimit).map(incident => ({
          id: incident.incidentId,
          title: `${incident.failureType} · ${incident.status}`,
          detail: incident.message
        }))}
      />

      <section className="wf-boundary-value-evidence" style={boundaryStyles.sectionDivider} aria-label="Selected execution Runtime Evidence values">
        <h6>Runtime Evidence values</h6>
        {inspection.valueSnapshots.length === 0
          ? <p>No value evidence was captured for this execution.</p>
          : inspection.valueSnapshots.map((snapshot, index) => (
            <SelectedValueEvidence
              key={`${snapshot.subject}:${snapshot.name}:${snapshot.capturedAt}:${index}`}
              snapshot={snapshot}
            />
          ))}
      </section>
      {inspection.boundary ? (
        <p className="wf-muted">
          This occurrence is a nested reusable boundary. Its descendants remain collapsed until “Open boundary” is used on its row.
        </p>
      ) : null}
      {isCancelling(inspection) ? (
        <p className="wf-boundary-nonterminal">
          Cancelling is nonterminal; cleanup has not committed completion yet.
        </p>
      ) : null}
    </section>
  );
}

function BoundedEvidenceSummary({
  title,
  total,
  items
}: {
  title: string;
  total: number;
  items: Array<{ id: string; title: string; detail: string }>;
}) {
  return (
    <section className="wf-boundary-bounded-summary" style={boundaryStyles.sectionDivider}>
      <h6>{title} <small>{total}</small></h6>
      {items.length === 0 ? <p>No {title.toLowerCase()} on this attempt.</p> : (
        <ul>
          {items.map(item => (
            <li key={item.id} style={boundaryStyles.panel}>
              <strong>{item.title}</strong>
              <small>{item.id}</small>
              <span>{item.detail}</span>
            </li>
          ))}
        </ul>
      )}
      {total > evidenceSummaryLimit
        ? <p>Showing the first {evidenceSummaryLimit} of {total} bounded summaries.</p>
        : null}
    </section>
  );
}

function SelectedValueEvidence({ snapshot }: { snapshot: ActivityExecutionInspectionValueSnapshot }) {
  const presentation = runtimeEvidencePresentation(snapshot);
  const labels = {
    captured: "Captured",
    redacted: "Redacted",
    "not-captured": "Not captured",
    "capture-failed": "Capture failed",
    "payload-reference": "Payload reference"
  } as const;
  return (
    <article className="wf-boundary-value-card" style={boundaryStyles.panel} data-presentation={presentation}>
      <header style={boundaryStyles.splitHeader}>
        <span style={boundaryStyles.stack}>
          <strong>{snapshot.name}</strong>
          <small>{snapshot.subject} · {snapshot.type?.displayName ?? snapshot.type?.typeName ?? "Unknown type"}</small>
        </span>
        <span style={boundaryStyles.badge}>{labels[presentation]}</span>
      </header>
      <p>{selectedValueDescription(snapshot, presentation)}</p>
    </article>
  );
}

function selectedValueDescription(
  snapshot: ActivityExecutionInspectionValueSnapshot,
  presentation: ReturnType<typeof runtimeEvidencePresentation>
) {
  if (presentation === "capture-failed") {
    return snapshot.failure?.message || snapshot.failure?.code || snapshot.captureReason || "Runtime capture failed.";
  }
  if (presentation === "not-captured") {
    return snapshot.captureReason || "The runtime capture policy did not capture this value.";
  }
  const node = snapshot.snapshot && typeof snapshot.snapshot === "object"
    ? snapshot.snapshot as Record<string, unknown>
    : null;
  if (presentation === "redacted") {
    return readDisplayText(node, ["reason", "requiredPermission", "displayName"]) || "The value is protected by runtime evidence permissions.";
  }
  if (presentation === "payload-reference") {
    const resolution = node?.resolution && typeof node.resolution === "object"
      ? node.resolution as Record<string, unknown>
      : null;
    return [
      readDisplayText(node, ["displayName", "referenceKind"]) || "Protected runtime payload",
      typeof node?.contentType === "string" ? node.contentType : null,
      typeof node?.size === "number" ? `${node.size} bytes` : null,
      readDisplayText(resolution, ["reason"]) || "Separate audited authorization is required to resolve this reference."
    ].filter(Boolean).join(" · ");
  }
  return capturedValuePreview(node, snapshot);
}

function capturedValuePreview(
  node: Record<string, unknown> | null,
  snapshot: ActivityExecutionInspectionValueSnapshot
) {
  if (node) {
    if (typeof node.preview === "string") return truncate(node.preview, 240);
    if ("value" in node) return truncate(stringify(node.value), 240);
    return `${formatKind(typeof node.kind === "string" ? node.kind : "Diagnostic Snapshot")} captured.`;
  }
  if (snapshot.payload !== undefined) return truncate(stringify(snapshot.payload), 240);
  return snapshot.captureReason || "Diagnostic Snapshot captured.";
}

function createFrame(inspection: ActivityExecutionInspection, label?: string): BoundaryFrame {
  return {
    key: frameKey(inspection),
    label: label ?? boundaryLabel(inspection),
    inspection,
    activePane: "structure",
    scrollTop: 0,
    hierarchyScrollTop: 0,
    layoutStatus: "idle",
    layout: null,
    layoutError: "",
    evidenceStatus: "idle",
    evidence: emptyBoundaryEvidenceSnapshot(),
    evidenceError: "",
    selectedActivityExecutionId: null,
    selectedStatus: "idle",
    selectedInspection: null,
    selectedError: ""
  };
}

function prepareFrameForLeave(frame: BoundaryFrame): BoundaryFrame {
  return {
    ...frame,
    layoutStatus: frame.layoutStatus === "loading"
      ? frame.layout ? "ready" : "idle"
      : frame.layoutStatus,
    evidenceStatus: frame.evidenceStatus === "loading"
      ? frame.evidence.committedThroughSequence === null ? "idle" : "ready"
      : frame.evidenceStatus
  };
}

function frameKey(inspection: ActivityExecutionInspection) {
  return [
    inspection.workflowExecutionId,
    inspection.activityExecutionId,
    `attempt-${inspection.attempt?.attemptNumber ?? 1}`,
    inspection.status,
    inspection.subStatus ?? "",
    inspection.lastCommittedAt ?? "",
    inspection.boundary?.aggregate.lastExecutionSequence ?? 0
  ].join(":");
}

function boundaryLabel(inspection: ActivityExecutionInspection) {
  const name = historicalActivityLabel(inspection.activityType);
  return `${name} · attempt ${inspection.attempt?.attemptNumber ?? 1}`;
}

function hierarchyItemLabel(item: ActivityExecutionHierarchyItem) {
  return `${historicalActivityLabel(item.activityType)} · attempt ${item.attempt?.attemptNumber ?? 1}`;
}

function historicalActivityLabel(activityType: string) {
  return activityType.split(".").filter(Boolean).at(-1) || activityType || "Historical activity";
}

function historicalNodeLabel(node: {
  activityType?: string | null;
  activityTypeVersion?: string | null;
  authoredActivityId: string;
}) {
  return node.activityType
    ? `${historicalActivityLabel(node.activityType)}${node.activityTypeVersion ? ` ${node.activityTypeVersion}` : ""}`
    : `${node.authoredActivityId} · historical descriptor unavailable`;
}

function isCancelling(inspection: ActivityExecutionInspection) {
  const status = `${inspection.status} ${inspection.subStatus ?? ""}`.replace(/[\s_-]+/g, "").toLowerCase();
  return status.includes("cancelling") || status.includes("canceling");
}

function readDisplayText(record: Record<string, unknown> | null, keys: string[]) {
  if (!record) return "";
  for (const key of keys) {
    const value = record[key];
    if (typeof value === "string" && value.trim()) return value;
  }
  return "";
}

function formatKind(value: string) {
  return value.replace(/([a-z])([A-Z])/g, "$1 $2");
}

function truncate(value: string, length: number) {
  return value.length > length ? `${value.slice(0, Math.max(0, length - 1))}…` : value;
}

function stringify(value: unknown) {
  if (typeof value === "string") return value;
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

const boundaryStyles = {
  inspector: {
    display: "grid",
    gap: 12,
    overflow: "hidden"
  },
  splitHeader: {
    display: "flex",
    minWidth: 0,
    alignItems: "flex-start",
    justifyContent: "space-between",
    gap: 10
  },
  stack: {
    display: "grid",
    minWidth: 0,
    gap: 4
  },
  breadcrumbs: {
    display: "flex",
    minWidth: 0,
    alignItems: "center",
    gap: 4,
    overflowX: "auto"
  },
  inlineCluster: {
    display: "inline-flex",
    minWidth: "max-content",
    alignItems: "center",
    gap: 4
  },
  responsiveGrid: {
    display: "grid",
    gap: 8,
    gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))"
  },
  twoColumnTabs: {
    display: "grid",
    gap: 4,
    gridTemplateColumns: "repeat(2, minmax(0, 1fr))"
  },
  paneScroll: {
    maxHeight: "min(68vh, 760px)",
    minHeight: 150,
    overflow: "auto",
    overscrollBehavior: "contain"
  },
  panel: {
    border: "1px solid var(--wf-border)",
    borderRadius: "var(--wf-radius)",
    background: "var(--wf-panel-muted)",
    padding: 10
  },
  warningPanel: {
    display: "grid",
    alignItems: "start",
    gap: 8,
    border: "1px solid var(--wf-warning)",
    borderRadius: "var(--wf-radius)",
    background: "var(--wf-warning-soft)",
    padding: 10,
    gridTemplateColumns: "auto minmax(0, 1fr)"
  },
  layoutCanvas: {
    display: "block",
    width: "100%",
    minHeight: 220,
    maxHeight: 420,
    border: "1px solid var(--wf-border)",
    borderRadius: "var(--wf-radius)",
    background: "var(--wf-canvas)"
  },
  hierarchyViewport: {
    position: "relative",
    overflow: "auto",
    overscrollBehavior: "contain",
    border: "1px solid var(--wf-border)",
    borderRadius: "var(--wf-radius)",
    background: "var(--wf-panel-muted)"
  },
  hierarchySpacer: {
    position: "relative",
    minWidth: 620
  },
  hierarchyRow: {
    position: "absolute",
    top: 0,
    right: 0,
    left: 0,
    display: "grid",
    minWidth: 0,
    alignItems: "center",
    gap: 8,
    borderBottom: "1px solid var(--wf-border)",
    cursor: "pointer",
    gridTemplateColumns: "36px minmax(190px, 1fr) minmax(130px, auto) auto",
    padding: 8
  },
  sectionDivider: {
    display: "grid",
    gap: 6,
    borderTop: "1px solid var(--wf-border)",
    paddingTop: 9
  },
  badge: {
    flex: "0 0 auto",
    borderRadius: 999,
    background: "var(--wf-accent-soft)",
    color: "var(--wf-accent)",
    fontSize: 9,
    fontWeight: 800,
    padding: "3px 7px"
  },
  visuallyHidden: {
    position: "absolute",
    width: 1,
    height: 1,
    overflow: "hidden",
    clipPath: "inset(50%)",
    whiteSpace: "nowrap"
  }
} satisfies Record<string, CSSProperties>;

function paneButtonStyle(selected: boolean): CSSProperties {
  return {
    border: 0,
    borderRadius: "var(--wf-radius)",
    background: selected ? "var(--wf-accent-soft)" : "var(--wf-panel-muted)",
    color: selected ? "var(--wf-accent)" : "var(--wf-muted-text)",
    fontWeight: 700,
    padding: 7
  };
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}
