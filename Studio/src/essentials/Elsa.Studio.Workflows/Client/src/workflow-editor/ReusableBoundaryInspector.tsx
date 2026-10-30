import { CopyableValue } from "@elsa-workflows/studio-ui";
import { getIntrinsicActivityDisplay } from "../runtimeActivityDisplay";
import {
  useCallback,
  useEffect,
  useId,
  useMemo,
  useRef,
  useState,
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
import "./boundaryInspector.css";
import { getActivityDefinitionVersion } from "../api/activityDesign";
import {
  getActivityExecutionDescendants,
  getActivityExecutionInspection,
  getActivityExecutionLayout
} from "../api/runtime";
import { StudioLink } from "../StudioLink";
import { observeReusableActivity } from "../reusableActivityObservability";
import { runtimeValueTypeLabel } from "../runtimeValueFormatting";
import { RuntimeValueEvidenceContent, RuntimeValueEvidenceResolutionProvider, runtimeValueContextId } from "./RuntimeValueEvidence";
import {
  classifyBoundaryCursorProblem,
  emptyBoundaryEvidenceSnapshot,
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
  inspection,
  activityLabel
}: {
  context: StudioEndpointContext;
  inspection: ActivityExecutionInspection;
  activityLabel?: string;
}) {
  const rootKey = frameKey(inspection);
  const rootLabel = activityLabel ?? boundaryLabel(inspection);
  const [frames, setFrames] = useState<BoundaryFrame[]>(() => [createFrame(inspection, activityLabel)]);
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
  const instanceId = useId();
  const activeFrame = frames.at(-1)!;
  const paneId = `${instanceId}-boundary-pane`;
  const stepsTabId = `${instanceId}-steps-tab`;
  const definitionTabId = `${instanceId}-definition-tab`;

  useEffect(() => {
    setFrames([createFrame(inspection)]);
    setOpenState({ activityExecutionId: null, error: "" });
    openControllerRef.current?.abort();
    selectionControllerRef.current?.abort();
    layoutControllerRef.current?.abort();
    evidenceControllerRef.current?.abort();
  }, [rootKey, inspection]);

  useEffect(() => {
    setFrames(current => current.map((frame, index) =>
      index === 0 && frame.label !== rootLabel ? { ...frame, label: rootLabel } : frame));
  }, [rootKey, rootLabel, inspection]);

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
      activePane: "evidence",
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
    <section
      className="wf-boundary-inspector"
      aria-label="Activity internals"
    >
      {frames.length > 1 ? (
        <nav className="wf-boundary-breadcrumbs" aria-label="Activity boundary path">
          {frames.map((frame, index) => (
            <span key={frame.key} className="wf-boundary-breadcrumb">
              {index > 0 ? <ChevronRight size={13} aria-hidden="true" /> : null}
              <button
                type="button"
                aria-current={index === frames.length - 1 ? "page" : undefined}
                onClick={() => navigateToFrame(index)}
              >
                {frame.label}
              </button>
            </span>
          ))}
          <WorkflowStatusBadge status={activeFrame.inspection.status} subStatus={activeFrame.inspection.subStatus} />
        </nav>
      ) : null}

      <div className="wf-boundary-controls">
        <div className="wf-boundary-pane-tabs" role="tablist" aria-label="Activity details">
          <button
            type="button"
            role="tab"
            id={stepsTabId}
            aria-controls={paneId}
            aria-label="Steps"
            aria-selected={activeFrame.activePane === "evidence"}
            data-selected={activeFrame.activePane === "evidence"}
            tabIndex={activeFrame.activePane === "evidence" ? 0 : -1}
            onClick={() => setPane("evidence")}
            onKeyDown={event => moveBoundaryTab(event, activeFrame.activePane, setPane)}
          >
            <span>Steps</span>
            {activeFrame.evidence.committedThroughSequence !== null
              ? <small aria-hidden="true">{activeFrame.evidence.items.length}</small>
              : null}
          </button>
          <button
            type="button"
            role="tab"
            id={definitionTabId}
            aria-controls={paneId}
            aria-label="Definition"
            aria-selected={activeFrame.activePane === "structure"}
            data-selected={activeFrame.activePane === "structure"}
            tabIndex={activeFrame.activePane === "structure" ? 0 : -1}
            onClick={() => setPane("structure")}
            onKeyDown={event => moveBoundaryTab(event, activeFrame.activePane, setPane)}
          >
            Definition
          </button>
        </div>
        <AuthorizedBoundaryDefinitionLink
          context={context}
          definitionId={boundary.definitionId}
          versionId={boundary.definitionVersionId}
          linkText="View activity definition"
        />
      </div>

      <div
        ref={scrollRef}
        className="wf-boundary-pane-scroll"
        id={paneId}
        role="tabpanel"
        tabIndex={0}
        aria-labelledby={activeFrame.activePane === "evidence" ? stepsTabId : definitionTabId}
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
            context={context}
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

      <BoundaryTechnicalDetails
        inspection={activeFrame.inspection}
        evidence={activeFrame.evidence}
        layout={activeFrame.layout}
        selectedActivityExecutionId={activeFrame.selectedActivityExecutionId}
        selectedInspection={activeFrame.selectedInspection}
        onSelect={activityExecutionId => {
          selectExecution(activityExecutionId);
          requestAnimationFrame(() => scrollRef.current?.focus());
        }}
      />

      {isCancelling(activeFrame.inspection) ? (
        <p className="wf-boundary-nonterminal" role="status">
          Cancellation is still finishing. This activity has not finished yet.
        </p>
      ) : null}
      <span className="wf-boundary-announcement" aria-live="polite">
        {activeFrame.evidenceStatus === "loading"
          ? "Loading activity steps."
          : activeFrame.evidenceStatus === "ready"
            ? `${activeFrame.evidence.items.length} activity steps loaded.`
            : ""}
      </span>
    </section>
  );
}

function AuthorizedBoundaryDefinitionLink({
  context,
  definitionId,
  versionId,
  linkText = "View activity definition"
}: {
  context: StudioEndpointContext;
  definitionId: string;
  versionId: string;
  linkText?: string;
}) {
  const contextIdentity = runtimeValueContextId(context);
  const [resolved, setResolved] = useState<{ contextIdentity: number | string; definitionId: string; versionId: string } | null>(null);
  const [status, setStatus] = useState<"loading" | "ready" | "unavailable">("loading");

  useEffect(() => {
    const controller = new AbortController();
    setResolved(null);
    setStatus("loading");

    void Promise.resolve()
      .then(() => getActivityDefinitionVersion(context, versionId, controller.signal))
      .then(version => {
        if (controller.signal.aborted) return;
        if (version.versionId !== versionId || version.definition.definitionId !== definitionId) {
          setStatus("unavailable");
          return;
        }
        setResolved({ contextIdentity, definitionId: version.definition.definitionId, versionId: version.versionId });
        setStatus("ready");
      })
      .catch(() => {
        if (!controller.signal.aborted) setStatus("unavailable");
      });

    return () => controller.abort();
  }, [context, contextIdentity, definitionId, versionId]);

  if (status === "unavailable") return <span className="wf-boundary-source-unavailable">Activity definition link unavailable.</span>;
  if (status === "loading" || !resolved || resolved.contextIdentity !== contextIdentity || resolved.definitionId !== definitionId || resolved.versionId !== versionId) {
    return <span className="wf-boundary-source-pending" role="status">Resolving activity definition…</span>;
  }

  const href = `/workflows/activity-definitions?definition=${encodeURIComponent(resolved.definitionId)}&section=versions&version=${encodeURIComponent(resolved.versionId)}`;
  return <StudioLink href={href}>{linkText}</StudioLink>;
}

function BoundaryStructurePane({
  frame,
  onRetry
}: {
  frame: BoundaryFrame;
  onRetry(): void;
}) {
  if (frame.layoutStatus === "loading" || frame.layoutStatus === "idle") {
    return <p className="wf-muted" role="status">Loading the saved activity layout…</p>;
  }
  if (frame.layoutStatus === "failed") {
    return (
      <div className="wf-boundary-panel-message" role="alert">
        <p>Authorized historical structure is unavailable. {frame.layoutError}</p>
        <button type="button" onClick={onRetry}><RotateCcw size={13} /> Retry structure</button>
      </div>
    );
  }
  if (!frame.layout) {
    return (
      <div className="wf-boundary-panel-message">
        <strong>No saved layout was captured for this run.</strong>
        <p>The pinned activity definition remains available. No newer version has been substituted.</p>
      </div>
    );
  }

  return <HistoricalBoundaryLayout layout={frame.layout} />;
}

export function HistoricalBoundaryLayout({ layout, onSelectNode, selectedExecutableNodeId, statusByNode }: {
  layout: ActivityExecutionLayout;
  onSelectNode?(executableNodeId: string): void;
  selectedExecutableNodeId?: string;
  statusByNode?: ReadonlyMap<string, string>;
}) {
  const positioned = layout.nodes.filter(node => node.hasPinnedGeometry);
  if (positioned.length === 0) {
    return (
      <div className="wf-boundary-layout wf-boundary-layout-list">
        <header className="wf-boundary-layout-heading">
          <strong>Activity structure</strong>
        </header>
        {layout.nodes.map(node => (
          <div className="wf-boundary-layout-list-row" key={node.executableNodeId}>
            {onSelectNode ? <button type="button" aria-pressed={selectedExecutableNodeId === node.executableNodeId} onClick={() => onSelectNode(node.executableNodeId)}>{historicalNodeLabel(node)}{statusByNode?.get(node.executableNodeId) ? ` · ${statusByNode.get(node.executableNodeId)}` : ""}</button> : <span>{historicalNodeLabel(node)}</span>}
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
    <figure className="wf-boundary-layout-canvas">
      <figcaption className="wf-boundary-layout-caption">
        <span className="wf-boundary-layout-heading">
          <strong>Saved activity layout</strong>
        </span>
        <small>{layout.nodes.length} nodes · {layout.connections.length} connections</small>
      </figcaption>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        role="img"
        aria-label={`Pinned historical boundary layout with ${layout.nodes.length} nodes`}
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
                role={onSelectNode ? "button" : undefined}
                tabIndex={onSelectNode ? 0 : undefined}
                aria-label={onSelectNode ? `${historicalNodeLabel(node)} · ${statusByNode?.get(node.executableNodeId) ?? "No recorded execution"}` : undefined}
                aria-pressed={onSelectNode ? selectedExecutableNodeId === node.executableNodeId : undefined}
                onClick={() => onSelectNode?.(node.executableNodeId)}
                onKeyDown={event => {
                  if (onSelectNode && (event.key === "Enter" || event.key === " ")) { event.preventDefault(); onSelectNode(node.executableNodeId); }
                }}
              >
                <title>{historicalNodeLabel(node)}</title>
                <rect className="wf-boundary-layout-node-shape" width={nodeWidth} height={nodeHeight} rx="8" />
                <text className="wf-boundary-layout-node-label" x="12" y="25">{truncate(historicalNodeLabel(node), 24)}</text>
              </g>
            );
          })}
        </g>
      </svg>
      {layout.nestedBoundaries.length > 0 ? (
        <p className="wf-muted">
          {layout.nestedBoundaries.length} nested boundary occurrence{layout.nestedBoundaries.length === 1 ? "" : "s"} represented as one node each. Open one from Steps to inspect it.
        </p>
      ) : null}
    </figure>
  );
}

function BoundaryEvidencePane({
  context,
  frame,
  openingActivityExecutionId,
  openError,
  onHierarchyScroll,
  onSelect,
  onOpenBoundary,
  onLoadMore,
  onRestart
}: {
  context: StudioEndpointContext;
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
    <div className="wf-boundary-evidence-pane">
      <header className="wf-boundary-evidence-toolbar">
        <small>
          {hasLoadedEvidence
            ? `${evidence.items.length} recorded step${evidence.items.length === 1 ? "" : "s"}`
            : "Waiting for recorded steps"}
        </small>
        <button type="button" aria-label="Refresh activity steps" onClick={onRestart} disabled={frame.evidenceStatus === "loading"}>
          <RefreshCw size={13} /> Refresh
        </button>
      </header>

      {evidence.staleReason ? (
        <div className="wf-boundary-stale" role="alert">
          <AlertTriangle size={15} aria-hidden="true" />
          <span>
            <strong>This steps view is out of date.</strong>
            <small>Refresh from the first page to load a new snapshot. Steps from different snapshots are kept separate.</small>
          </span>
          <button type="button" onClick={onRestart}>Refresh steps</button>
        </div>
      ) : null}
      {openError ? <p className="wf-boundary-error" role="alert">{openError}</p> : null}
      {frame.evidenceStatus === "failed" ? (
        <div className="wf-boundary-panel-message" role="alert">
          <p>Activity steps are unavailable. {frame.evidenceError}</p>
          <button type="button" onClick={onRestart}><RotateCcw size={13} /> Retry evidence</button>
        </div>
      ) : null}
      {!hasLoadedEvidence && frame.evidenceStatus === "loading"
        ? <p className="wf-muted" role="status">Loading the first activity steps...</p>
        : null}
      {hasLoadedEvidence && evidence.items.length === 0
        ? <p className="wf-muted">No steps have been recorded yet.</p>
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
          {frame.evidenceStatus === "loading" ? "Loading more steps..." : "Load more steps"}
        </button>
      ) : null}
      {frame.selectedActivityExecutionId ? (
        <SelectedExecutionEvidence
          context={context}
          status={frame.selectedStatus}
          inspection={frame.selectedInspection}
          error={frame.selectedError}
        />
      ) : null}
    </div>
  );
}

const hierarchyRowHeight = 88;
const hierarchyViewportHeight = 380;
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
  const viewportHeight = Math.min(hierarchyViewportHeight, items.length * hierarchyRowHeight);
  const start = Math.max(0, Math.floor(scrollTop / hierarchyRowHeight) - hierarchyOverscan);
  const count = Math.ceil(viewportHeight / hierarchyRowHeight) + hierarchyOverscan * 2;
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
    else if (nextTop + hierarchyRowHeight > viewport.scrollTop + viewport.clientHeight) {
      viewport.scrollTop = nextTop + hierarchyRowHeight - viewport.clientHeight;
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
      aria-label="Activity steps"
      style={{ height: viewportHeight }}
      onScroll={event => {
        const nextScrollTop = event.currentTarget.scrollTop;
        setScrollTop(nextScrollTop);
        onScroll(nextScrollTop);
      }}
    >
      <div className="wf-boundary-hierarchy-spacer" style={{ height: items.length * hierarchyRowHeight }}>
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
                height: hierarchyRowHeight,
                transform: `translateY(${index * hierarchyRowHeight}px)`
              }}
              onFocus={() => setActiveIndex(index)}
              onClick={() => onSelect(item.activityExecutionId)}
              onKeyDown={event => onKeyDown(event, index)}
            >
              <span className="wf-boundary-hierarchy-identity">
                <strong>{historicalActivityLabel(item.activityType)}</strong>
                {item.relativeDepth > 1 ? <small>Nested step</small> : null}
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
                    {openingActivityExecutionId === item.activityExecutionId ? "Opening…" : "View steps"}
                  </button>
                ) : null}
              </span>
              <span className="wf-boundary-hierarchy-state">
                <WorkflowStatusBadge status={item.status} subStatus={item.subStatus} />
                {item.bookmarkCount > 0 || item.incidentCount > 0 ? (
                  <small>
                    {item.bookmarkCount > 0 ? `${item.bookmarkCount} bookmark${item.bookmarkCount === 1 ? "" : "s"}` : ""}
                    {item.bookmarkCount > 0 && item.incidentCount > 0 ? " · " : ""}
                    {item.incidentCount > 0 ? `${item.incidentCount} incident${item.incidentCount === 1 ? "" : "s"}` : ""}
                  </small>
                ) : null}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function BoundaryTechnicalDetails({
  inspection,
  evidence,
  layout,
  selectedActivityExecutionId,
  selectedInspection,
  onSelect
}: {
  inspection: ActivityExecutionInspection;
  evidence: BoundaryEvidenceSnapshot;
  layout: ActivityExecutionLayout | null;
  selectedActivityExecutionId: string | null;
  selectedInspection: ActivityExecutionInspection | null;
  onSelect(activityExecutionId: string): void;
}) {
  const boundary = inspection.boundary!;
  const aggregate = boundary.aggregate;
  const selectedLinks = selectedInspection ? [
    selectedInspection.attempt?.previousAttemptActivityExecutionId
      ? { label: "Previous attempt", id: selectedInspection.attempt.previousAttemptActivityExecutionId }
      : null,
    selectedInspection.attempt?.nextAttemptActivityExecutionId
      ? { label: "Next attempt", id: selectedInspection.attempt.nextAttemptActivityExecutionId }
      : null,
    selectedInspection.provenance.parentActivityExecutionId
      ? { label: "Parent execution", id: selectedInspection.provenance.parentActivityExecutionId }
      : null,
    selectedInspection.provenance.schedulingActivityExecutionId
      ? { label: "Scheduling execution", id: selectedInspection.provenance.schedulingActivityExecutionId }
      : null
  ].filter((item): item is { label: string; id: string } => !!item) : [];
  const loadedIds = new Set(evidence.items.map(item => item.activityExecutionId));

  return (
    <details key={selectedActivityExecutionId ?? "unselected"} className="wf-boundary-technical">
      <summary>Technical details</summary>
      <dl className="wf-boundary-technical-grid">
        <dt>Definition ID</dt><dd><CopyableValue label="Definition ID" value={boundary.definitionId} /></dd>
        <dt>Definition version</dt>
        <dd>{boundary.version} · {boundary.definitionVersionId}</dd>
        <dt>Boundary execution</dt><dd><CopyableValue label="Boundary execution" value={inspection.activityExecutionId} /></dd>
        <dt>Execution scope</dt><dd><CopyableValue label="Execution scope" value={boundary.executionScopeId} /></dd>
        <dt>Attempt</dt>
        <dd>{inspection.attempt ? `${inspection.attempt.attemptNumber} of ${inspection.attempt.totalAttempts ?? "?"}` : "1"}</dd>
        <dt>Descendant aggregate</dt>
        <dd>{aggregate.status} · {boundary.committedDescendantCount} committed · {aggregate.completed} completed · {aggregate.faulted} faulted</dd>
        <dt>Incidents / retries</dt>
        <dd>{aggregate.blockingIncidentCount} / {aggregate.retryCount}</dd>
        <dt>Last descendant sequence</dt>
        <dd>{aggregate.lastExecutionSequence}</dd>
        <dt>Snapshot through sequence</dt>
        <dd>{evidence.committedThroughSequence ?? "Not loaded"}</dd>
        {layout ? (
          <>
            <dt>Layout source</dt>
            <dd>{layout.selection} · {layout.sourceReferenceId}</dd>
          </>
        ) : null}
        {selectedActivityExecutionId ? (
          <>
            <dt>Selected activity execution</dt><dd><CopyableValue label="Selected activity execution" value={selectedActivityExecutionId} /></dd>
          </>
        ) : null}
        {selectedInspection ? (
          <>
            <dt>Selected executable node</dt><dd><CopyableValue label="Selected executable node" value={selectedInspection.executableNodeId} /></dd>
            <dt>Selected activity type</dt>
            <dd>{selectedInspection.activityType} · {selectedInspection.activityTypeVersion}</dd>
            <dt>Selected sequence</dt>
            <dd>{selectedInspection.executionSequence}</dd>
            <dt>Selected attempt</dt>
            <dd>{selectedInspection.attempt ? `${selectedInspection.attempt.attemptNumber} of ${selectedInspection.attempt.totalAttempts ?? "?"}` : "1"}</dd>
            <dt>Last committed</dt>
            <dd>{selectedInspection.lastCommittedAt ?? "Not committed"}</dd>
            {selectedInspection.incidents.length > 0 ? (
              <>
                <dt>Incident IDs</dt>
                <dd>
                  {selectedInspection.incidents.slice(0, evidenceSummaryLimit).map(incident => incident.incidentId).join(", ")}
                  {selectedInspection.incidents.length > evidenceSummaryLimit
                    ? ` · ${selectedInspection.incidents.length - evidenceSummaryLimit} more`
                    : ""}
                </dd>
              </>
            ) : null}
          </>
        ) : null}
      </dl>

      {selectedLinks.length > 0 ? (
        <div className="wf-boundary-causal-links" aria-label="Related execution links">
          {selectedLinks.map(link => (
            <button type="button" key={`${link.label}:${link.id}`} onClick={() => onSelect(link.id)}>
              <GitBranch size={12} aria-hidden="true" />
              <span>{link.label}</span>
              <small>{link.id}{loadedIds.has(link.id) ? " · loaded step" : ""}</small>
            </button>
          ))}
        </div>
      ) : null}

      {selectedInspection?.bookmarks.length ? (
        <div className="wf-boundary-technical-evidence">
          <BoundedEvidenceSummary
            title="Bookmarks"
            total={selectedInspection.bookmarks.length}
            items={selectedInspection.bookmarks.slice(0, evidenceSummaryLimit).map(bookmark => ({
              id: bookmark.bookmarkId,
              title: bookmark.stimulusType,
              detail: `Resume target ${bookmark.resumeTargetId} · created ${bookmark.createdAt}`
            }))}
          />
        </div>
      ) : null}
    </details>
  );
}

function SelectedExecutionEvidence({
  context,
  status,
  inspection,
  error
}: {
  context: StudioEndpointContext;
  status: LoadStatus;
  inspection: ActivityExecutionInspection | null;
  error: string;
}) {
  const workflowExecutionId = inspection?.workflowExecutionId;
  const activityExecutionId = inspection?.activityExecutionId;
  const resolutionScope = useMemo(
    () => workflowExecutionId && activityExecutionId
      ? { context, workflowExecutionId, activityExecutionId }
      : null,
    [context, workflowExecutionId, activityExecutionId]);

  if (status === "loading") {
    return <section className="wf-boundary-selected" role="status">Loading selected step…</section>;
  }
  if (status === "failed") {
    return <section className="wf-boundary-selected" role="alert">Selected step detail is unavailable. {error}</section>;
  }
  if (!inspection) return null;

  return (
    <section className="wf-boundary-selected" aria-label="Selected activity step">
      <header className="wf-boundary-selected-heading">
        <strong>{historicalActivityLabel(inspection.activityType)}</strong>
        <WorkflowStatusBadge status={inspection.status} subStatus={inspection.subStatus} />
      </header>

      <RuntimeValueEvidenceResolutionProvider scope={resolutionScope}>
        <section className="wf-boundary-value-evidence" aria-label="Selected step values">
          <h5>Values</h5>
          {inspection.valueSnapshots.length === 0
            ? <p>No value evidence was captured for this step.</p>
            : inspection.valueSnapshots.map((snapshot, index) => (
              <SelectedValueEvidence
                key={`${snapshot.subject}:${snapshot.name}:${snapshot.capturedAt}:${index}`}
                snapshot={snapshot}
              />
            ))}
        </section>
      </RuntimeValueEvidenceResolutionProvider>
      {inspection.boundary ? (
        <p className="wf-muted">
          This is a nested activity. Select View steps to inspect it.
        </p>
      ) : null}
      {isCancelling(inspection) ? (
        <p className="wf-boundary-nonterminal" role="status">
          Cancellation is still finishing. This activity has not finished yet.
        </p>
      ) : null}
      {inspection.incidents.length > 0 ? <VisibleIncidentEvidence incidents={inspection.incidents} /> : null}
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
    <section className="wf-boundary-bounded-summary">
      <h6>{title} <small>{total}</small></h6>
      {items.length === 0 ? <p>No {title.toLowerCase()} on this attempt.</p> : (
        <ul>
          {items.map(item => (
            <li key={item.id}>
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
    <article className="wf-boundary-value-card" data-presentation={presentation}>
      <header className="wf-boundary-value-heading">
        <span>
          <strong>{snapshot.name}</strong>
          <small>{valueSubjectLabel(snapshot.subject)} · {runtimeValueTypeLabel(snapshot.type) || "Unknown type"}</small>
        </span>
        <span className="wf-boundary-value-state">{labels[presentation]}</span>
      </header>
      <RuntimeValueEvidenceContent snapshot={snapshot} />
    </article>
  );
}

function VisibleIncidentEvidence({ incidents }: { incidents: ActivityExecutionInspection["incidents"] }) {
  return (
    <section className="wf-boundary-visible-incidents" aria-label="Activity incidents">
      <h5>Incidents <small>{incidents.length}</small></h5>
      <ul>
        {incidents.slice(0, evidenceSummaryLimit).map(incident => (
          <li key={incident.incidentId}>
            <strong>{incident.failureType} · {incident.status}</strong>
            <p>{incident.message}</p>
          </li>
        ))}
      </ul>
      {incidents.length > evidenceSummaryLimit
        ? <p>Showing the first {evidenceSummaryLimit} of {incidents.length} incidents.</p>
        : null}
    </section>
  );
}

function valueSubjectLabel(subject: string) {
  switch (subject.toLowerCase()) {
    case "activityinput": return "Input";
    case "activityoutput": return "Output";
    default: return subject.replace(/([a-z0-9])([A-Z])/g, "$1 $2");
  }
}

function createFrame(inspection: ActivityExecutionInspection, label?: string): BoundaryFrame {
  return {
    key: frameKey(inspection),
    label: label ?? boundaryLabel(inspection),
    inspection,
    activePane: "evidence",
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
  return historicalActivityLabel(inspection.activityType);
}

function hierarchyItemLabel(item: ActivityExecutionHierarchyItem) {
  return historicalActivityLabel(item.activityType);
}

function historicalActivityLabel(activityType: string) {
  if (!activityType || activityType.toLowerCase().startsWith("elsa.user.")) return "Reusable activity";
  const intrinsicLabel = getIntrinsicActivityDisplay(activityType);
  if (intrinsicLabel) return intrinsicLabel;
  const typeName = activityType.split(".").filter(Boolean).at(-1);
  return typeName?.replace(/([a-z0-9])([A-Z])/g, "$1 $2") || "Activity";
}

function historicalNodeLabel(node: {
  activityType?: string | null;
}) {
  return node.activityType
    ? historicalActivityLabel(node.activityType)
    : "Historical activity descriptor unavailable";
}

function isCancelling(inspection: ActivityExecutionInspection) {
  const status = `${inspection.status} ${inspection.subStatus ?? ""}`.replace(/[\s_-]+/g, "").toLowerCase();
  return status.includes("cancelling") || status.includes("canceling");
}

function truncate(value: string, length: number) {
  return value.length > length ? `${value.slice(0, Math.max(0, length - 1))}…` : value;
}

function moveBoundaryTab(
  event: ReactKeyboardEvent<HTMLButtonElement>,
  activePane: BoundaryPane,
  selectPane: (pane: BoundaryPane) => void
) {
  const panes: BoundaryPane[] = ["evidence", "structure"];
  const currentIndex = panes.indexOf(activePane);
  const nextIndex = event.key === "ArrowRight" || event.key === "ArrowDown"
    ? (currentIndex + 1) % panes.length
    : event.key === "ArrowLeft" || event.key === "ArrowUp"
      ? (currentIndex + panes.length - 1) % panes.length
      : event.key === "Home"
        ? 0
        : event.key === "End"
          ? panes.length - 1
          : null;
  const nextPane = nextIndex === null ? null : panes[nextIndex];
  if (!nextPane) return;
  event.preventDefault();
  if (nextPane !== activePane) selectPane(nextPane);
  const tabIndex = nextPane === "evidence" ? 0 : 1;
  event.currentTarget.parentElement?.querySelectorAll<HTMLButtonElement>("[role='tab']")[tabIndex]?.focus();
}

function errorMessage(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

function isAbortError(error: unknown) {
  return error instanceof DOMException && error.name === "AbortError";
}
