import { NodeToolbar, Position } from "@xyflow/react";
import { AnchoredPopover } from "@elsa-workflows/studio-ui";
import { Copy, Plus, Power, Trash2 } from "lucide-react";
import React from "react";
import type { KeyboardEvent } from "react";
import type { WorkflowNodeData, WorkflowPortDescriptor } from "../workflowAdapter";
import { WorkflowNodeActionsContext } from "./contexts";
import "./workflowNodePresentation.css";

/** Floating, keyboard-accessible actions for one selected authoring node. */
export function WorkflowNodeContextToolbar({ nodeId, data, selected, offset = 10 }: {
  nodeId: string;
  data: WorkflowNodeData;
  selected: boolean;
  offset?: number;
}) {
  const actions = React.useContext(WorkflowNodeActionsContext);
  const [outcomesOpen, setOutcomesOpen] = React.useState(false);
  const [focusedActionIndex, setFocusedActionIndex] = React.useState(0);
  const toolbarRef = React.useRef<HTMLDivElement>(null);
  const addTriggerRef = React.useRef<HTMLButtonElement>(null);
  const menuRef = React.useRef<HTMLDivElement>(null);
  const openAtEndRef = React.useRef(false);
  const menuId = React.useId();
  const outcomes = actions?.canAddAfter ? data.sourcePorts : [];
  const canToggleStart = Boolean(actions?.setWorkflowStart && data.workflowStartEnabled !== undefined);
  const toggleIndex = outcomes.length > 0 ? 1 : 0;
  const duplicateIndex = toggleIndex + (canToggleStart ? 1 : 0);
  const toolbarActionCount = duplicateIndex + 2;
  const activeActionIndex = Math.min(focusedActionIndex, toolbarActionCount - 1);

  const closeOutcomes = (restoreFocus = true) => {
    setOutcomesOpen(false);
    if (restoreFocus) addTriggerRef.current?.focus();
  };

  React.useEffect(() => {
    if (!outcomesOpen) return;
    const frame = requestAnimationFrame(() => {
      const items = menuRef.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)");
      items?.[openAtEndRef.current ? items.length - 1 : 0]?.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [outcomesOpen]);

  if (!actions || !selected || actions.selectedNodeCount !== 1) return null;

  const anchorFromButton = () => {
    const rect = addTriggerRef.current?.getBoundingClientRect();
    return { clientX: rect ? rect.left + rect.width / 2 : 0, clientY: rect ? rect.top + rect.height / 2 : 0 };
  };

  const addAfter = (port: WorkflowPortDescriptor, anchor: { clientX: number; clientY: number }) => {
    actions.addAfter(nodeId, port.name, anchor);
  };

  const handleMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape" || event.key === "Tab") {
      event.stopPropagation();
      if (event.key === "Escape") event.preventDefault();
      closeOutcomes();
      return;
    }
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)") ?? []);
    const current = items.indexOf(document.activeElement as HTMLButtonElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1
      : (current + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
    items[next]?.focus();
  };

  const handleToolbarKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if ((event.target as HTMLElement).closest("[role='menu']")) return;
    if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    const buttons = Array.from(toolbarRef.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)") ?? []);
    const current = buttons.indexOf(document.activeElement as HTMLButtonElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1
      : (current + (event.key === "ArrowRight" ? 1 : -1) + buttons.length) % buttons.length;
    setFocusedActionIndex(next);
    buttons[next]?.focus();
  };

  const invokeSingleOutcome = (event: React.MouseEvent<HTMLButtonElement>) => {
    event.stopPropagation();
    const port = outcomes[0];
    if (!port) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const keyboardActivation = event.detail === 0;
    addAfter(port, {
      clientX: keyboardActivation ? rect.left + rect.width / 2 : event.clientX,
      clientY: keyboardActivation ? rect.top + rect.height / 2 : event.clientY
    });
  };

  const selectOutcome = (port: WorkflowPortDescriptor) => {
    closeOutcomes();
    addAfter(port, anchorFromButton());
  };

  return (
    <NodeToolbar nodeId={nodeId} isVisible position={Position.Top} offset={offset}>
      <div ref={toolbarRef} className="wf-node-context-toolbar" role="toolbar" aria-label={`Actions for ${data.label}`} aria-orientation="horizontal" onKeyDown={handleToolbarKeyDown}>
        {outcomes.length === 1 ? (
          <button
            ref={addTriggerRef}
            type="button"
            className="wf-node-context-action"
            tabIndex={activeActionIndex === 0 ? 0 : -1}
            aria-label="Add next activity"
            title={`Add activity after ${outcomes[0].displayName}`}
            onFocus={() => setFocusedActionIndex(0)}
            onMouseDown={event => event.stopPropagation()}
            onClick={invokeSingleOutcome}
          >
            <Plus size={14} aria-hidden="true" />
          </button>
        ) : outcomes.length > 1 ? (
          <>
            <button
              ref={addTriggerRef}
              type="button"
              className="wf-node-context-action"
              tabIndex={activeActionIndex === 0 ? 0 : -1}
              aria-label="Add next activity"
              title="Add next activity"
              aria-haspopup="menu"
              aria-expanded={outcomesOpen}
              aria-controls={outcomesOpen ? menuId : undefined}
              onFocus={() => setFocusedActionIndex(0)}
              onMouseDown={event => event.stopPropagation()}
              onClick={event => {
                event.stopPropagation();
                openAtEndRef.current = false;
                if (outcomesOpen) closeOutcomes(); else setOutcomesOpen(true);
              }}
              onKeyDown={event => {
                if (event.key !== "ArrowDown" && event.key !== "ArrowUp") return;
                event.preventDefault();
                event.stopPropagation();
                openAtEndRef.current = event.key === "ArrowUp";
                setOutcomesOpen(true);
              }}
            >
              <Plus size={14} aria-hidden="true" />
            </button>
            <AnchoredPopover anchorRef={addTriggerRef} open={outcomesOpen} className="wf-node-outcomes-popover" minWidth={150} maxHeight={240} onDismiss={() => closeOutcomes(false)}>
              <div ref={menuRef} id={menuId} className="wf-node-outcomes-menu" role="menu" aria-label={`Choose an outcome for ${data.label}`} onKeyDown={handleMenuKeyDown}>
                {outcomes.map(port => (
                  <button key={port.name} type="button" role="menuitem" tabIndex={-1} onClick={() => selectOutcome(port)}>
                    {port.displayName}
                  </button>
                ))}
              </div>
            </AnchoredPopover>
          </>
        ) : null}
        {canToggleStart ? <button
          type="button"
          className="wf-node-context-action"
          tabIndex={activeActionIndex === toggleIndex ? 0 : -1}
          aria-label={`Start new workflows from ${data.label}`}
          aria-pressed={data.workflowStartEnabled}
          title={data.workflowStartEnabled ? "Disable workflow start" : "Enable workflow start"}
          onFocus={() => setFocusedActionIndex(toggleIndex)}
          onMouseDown={event => event.stopPropagation()}
          onClick={event => {
            event.stopPropagation();
            actions.setWorkflowStart?.(nodeId, !data.workflowStartEnabled);
          }}
        ><Power size={14} aria-hidden="true" /></button> : null}
        <button
          type="button"
          className="wf-node-context-action"
          tabIndex={activeActionIndex === duplicateIndex ? 0 : -1}
          aria-label={`Duplicate ${data.label}`}
          title="Duplicate activity"
          onFocus={() => setFocusedActionIndex(duplicateIndex)}
          onMouseDown={event => event.stopPropagation()}
          onClick={event => {
            event.stopPropagation();
            actions.duplicate(nodeId);
          }}
        >
          <Copy size={14} aria-hidden="true" />
        </button>
        <button
          type="button"
          className="wf-node-context-action wf-node-context-action-delete"
          tabIndex={activeActionIndex === duplicateIndex + 1 ? 0 : -1}
          aria-label={`Delete ${data.label}`}
          title="Delete activity"
          onFocus={() => setFocusedActionIndex(duplicateIndex + 1)}
          onMouseDown={event => event.stopPropagation()}
          onClick={event => {
            event.stopPropagation();
            actions.delete(nodeId);
          }}
        >
          <Trash2 size={14} aria-hidden="true" />
        </button>
      </div>
    </NodeToolbar>
  );
}
