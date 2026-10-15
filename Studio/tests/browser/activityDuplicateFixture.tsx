import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { ReactFlowProvider, type NodeProps } from "@xyflow/react";
import { ActivityCanvasContextMenu } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/ActivityCanvasContextMenu";
import {
  matchActivityClipboardShortcut,
  pasteActivitiesIntoDraft,
  snapshotSelectedActivities,
  type ActivityClipboardSnapshot
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/duplicateActivities";
import { isWorkflowEditorKeyboardTarget } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/activityClipboardShortcuts";
import { WorkflowActivityNode } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/graph";
import { indexActivityPresentation, resolveActivityLabel } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityPresentation";
import {
  flowchartActivity,
  flowchartNode,
  leafNode,
  writeLine
} from "../../src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/fixtures";
import type { ActivityNode, WorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import type { WorkflowNodeData } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowAdapter";

const catalog = new Map([
  [writeLine.activityVersionId, writeLine],
  [flowchartActivity.activityVersionId, flowchartActivity]
]);

function initialDraft(): WorkflowDraft {
  const root = flowchartNode("flow-root", [leafNode("write-one"), leafNode("write-two")]);
  root.structure!.payload.connections = [{
    id: "flow-write-one-write-two",
    source: { nodeId: "write-one", port: "Done" },
    target: { nodeId: "write-two" }
  }];
  return {
    id: "draft-1",
    definitionId: "definition-1",
    state: { rootActivity: root },
    layout: [
      { nodeId: "write-one", x: 80, y: 120 },
      { nodeId: "write-two", x: 360, y: 120 }
    ],
    activityPresentation: [{ nodeId: "write-one", displayName: "Notify buyer" }],
    validationErrors: []
  };
}

export function ActivityDuplicateFixture() {
  const [draft, setDraft] = useState(initialDraft);
  const [selectedNodeIds, setSelectedNodeIds] = useState<string[]>(["write-one"]);
  const [status, setStatus] = useState("");
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; nodeIds: string[] } | null>(null);
  const clipboardRef = useRef<ActivityClipboardSnapshot | null>(null);
  const canvasRef = useRef<HTMLElement | null>(null);

  const scopeActivities = (draft.state.rootActivity?.structure?.payload.activities ?? []) as ActivityNode[];
  const presentationByNodeId = indexActivityPresentation(draft.activityPresentation);

  const snapshotFrom = useCallback((nodeIds: string[]) => snapshotSelectedActivities({
    selectedNodeIds: nodeIds,
    scopeActivities,
    scopeOwner: draft.state.rootActivity ?? null,
    catalog,
    layout: draft.layout,
    presentation: draft.activityPresentation ?? []
  }), [draft, scopeActivities]);

  const applySnapshot = useCallback((snapshot: ActivityClipboardSnapshot, nextStatus: string) => {
    const result = pasteActivitiesIntoDraft({ draft, frames: [], snapshot, catalog });
    if (!result) return;
    setDraft(result.draft);
    setSelectedNodeIds(result.selectedNodeIds);
    setStatus(nextStatus);
  }, [draft]);

  const copy = useCallback(() => {
    const snapshot = snapshotFrom(selectedNodeIds);
    if (!snapshot) return;
    clipboardRef.current = snapshot;
    setStatus(snapshot.activities.length === 1 ? "Copied 1 activity." : `Copied ${snapshot.activities.length} activities.`);
  }, [selectedNodeIds, snapshotFrom]);

  const paste = useCallback(() => {
    if (!clipboardRef.current) return;
    applySnapshot(
      clipboardRef.current,
      clipboardRef.current.activities.length === 1 ? "Pasted 1 activity." : `Pasted ${clipboardRef.current.activities.length} activities.`
    );
  }, [applySnapshot]);

  const duplicate = useCallback((nodeIds = selectedNodeIds) => {
    const snapshot = snapshotFrom(nodeIds);
    if (!snapshot) return;
    clipboardRef.current = snapshot;
    applySnapshot(
      snapshot,
      snapshot.activities.length === 1 ? "Duplicated 1 activity." : `Duplicated ${snapshot.activities.length} activities.`
    );
  }, [applySnapshot, selectedNodeIds, snapshotFrom]);

  const visibleNodes = useMemo(() => scopeActivities.map(activity => {
    const catalogItem = catalog.get(activity.activityVersionId);
    const presentation = presentationByNodeId.get(activity.nodeId);
    const layout = draft.layout.find(record => record.nodeId === activity.nodeId);
    return {
      activity,
      label: resolveActivityLabel(presentation, catalogItem, catalogItem?.activityTypeKey ?? activity.activityVersionId),
      x: layout?.x ?? 80,
      y: layout?.y ?? 120
    };
  }), [draft.layout, presentationByNodeId, scopeActivities]);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (isWorkflowEditorKeyboardTarget(event.target as HTMLElement | null)) return;
      const shortcut = matchActivityClipboardShortcut(event);
      if (shortcut === "copy") {
        event.preventDefault();
        copy();
      } else if (shortcut === "paste") {
        event.preventDefault();
        paste();
      } else if (shortcut === "duplicate") {
        event.preventDefault();
        duplicate();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [copy, duplicate, paste]);

  return (
    <main className="wf-editor wf-page activity-duplicate-fixture" data-testid="activity-duplicate-fixture">
      <header className="wf-page-header">
        <div>
          <span className="wf-kicker">Designer</span>
          <h1>Order confirmation</h1>
        </div>
        <p role="status">{status}</p>
      </header>
      <section
        ref={canvasRef}
        className="wf-canvas-shell"
        aria-label="Workflow canvas"
        style={{ minHeight: 640, position: "relative" }}
        onClick={() => setContextMenu(null)}
      >
        <div className="wf-canvas activity-duplicate-canvas" style={{ position: "relative", minHeight: 720 }}>
          <ReactFlowProvider>
            {visibleNodes.map(node => {
              const data: WorkflowNodeData = {
                label: node.label,
                activityVersionId: node.activity.activityVersionId,
                activityTypeKey: writeLine.activityTypeKey,
                category: writeLine.category,
                executionType: writeLine.executionType,
                sourcePorts: [{ name: "Done", displayName: "Done" }],
                acceptsInbound: true,
                childSlots: []
              };
              return (
                <button
                  key={node.activity.nodeId}
                  type="button"
                  className="activity-duplicate-node"
                  aria-label={node.label}
                  aria-pressed={selectedNodeIds.includes(node.activity.nodeId)}
                  data-node-id={node.activity.nodeId}
                  style={{
                    position: "absolute",
                    left: node.x,
                    top: node.y,
                    padding: 0,
                    border: 0,
                    background: "transparent",
                    cursor: "context-menu"
                  }}
                  onClick={event => {
                    event.stopPropagation();
                    setContextMenu(null);
                    setSelectedNodeIds(event.shiftKey
                      ? selectedNodeIds.includes(node.activity.nodeId)
                        ? selectedNodeIds
                        : [...selectedNodeIds, node.activity.nodeId]
                      : [node.activity.nodeId]);
                  }}
                  onContextMenu={event => {
                    event.preventDefault();
                    event.stopPropagation();
                    const nodeIds = selectedNodeIds.includes(node.activity.nodeId)
                      ? selectedNodeIds
                      : [node.activity.nodeId];
                    setSelectedNodeIds(nodeIds);
                    setContextMenu({ x: event.clientX, y: event.clientY, nodeIds });
                  }}
                >
                  <WorkflowActivityNode {...({
                    id: node.activity.nodeId,
                    data,
                    selected: selectedNodeIds.includes(node.activity.nodeId)
                  } as unknown as NodeProps)} />
                </button>
              );
            })}
          </ReactFlowProvider>
          {contextMenu ? (
            <ActivityCanvasContextMenu
              x={contextMenu.x}
              y={contextMenu.y}
              onDuplicate={() => duplicate(contextMenu.nodeIds)}
              onClose={() => setContextMenu(null)}
            />
          ) : null}
        </div>
      </section>
      <label className="wf-form-field" style={{ maxWidth: 320, margin: 16 }}>
        Display name
        <input
          aria-label="Display name"
          defaultValue=""
          placeholder="Focus here to keep shortcuts idle"
        />
      </label>
    </main>
  );
}
