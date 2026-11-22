import "./WorkflowEditorToolbar.css";
import { createContext, type ReactNode } from "react";
import { Check, LoaderCircle, MoreHorizontal, Network, Play, Redo2, Save, Undo2 } from "lucide-react";
import { StatusChip, StudioButton, OverflowMenu, StudioShellBreadcrumbs } from "@elsa-workflows/studio-ui";
import { StudioLink } from "../StudioLink";
import { PublishActionButton, type WorkflowPublishMode } from "./PublishActionButton";

export interface WorkflowToolbarActionGroup {
  label: string;
  actions: {
    label: string;
    icon: ReactNode;
    disabled?: boolean;
    title?: string;
    onSelect(): void;
  }[];
}

export const WorkflowCanvasToolsHostContext = createContext<HTMLElement | null>(null);

export function WorkflowEditorToolbarFrame({ identity, metadata, actions, afterActions, className }: {
  identity: ReactNode;
  metadata: ReactNode;
  actions: ReactNode;
  afterActions?: ReactNode;
  className?: string;
}) {
  return (
    <header className={`wf-editor-top wf-editor-toolbar${className ? ` ${className}` : ""}`}>
      <div className="wf-editor-identity">{identity}</div>
      <div className="wf-editor-metadata">{metadata}</div>
      <div className="wf-editor-actions">{actions}</div>
      {afterActions}
    </header>
  );
}

export function WorkflowCanvasCommandBar({
  canUndo,
  canRedo,
  canAutoLayout,
  onUndo,
  onRedo,
  onAutoLayout,
  canvasToolsSlot,
  ariaLabel = "Canvas tools",
  undoLabel = "Undo",
  redoLabel = "Redo",
  autoLayoutLabel = "Auto-layout",
  undoTitle = "Undo (Ctrl+Z)",
  redoTitle = "Redo (Ctrl+Shift+Z)",
  autoLayoutTitle = "Auto-layout the canvas"
}: {
  canUndo: boolean;
  canRedo: boolean;
  canAutoLayout: boolean;
  onUndo(): void;
  onRedo(): void;
  onAutoLayout(): void;
  canvasToolsSlot?: (element: HTMLElement | null) => void;
  ariaLabel?: string;
  undoLabel?: string;
  redoLabel?: string;
  autoLayoutLabel?: string;
  undoTitle?: string;
  redoTitle?: string;
  autoLayoutTitle?: string;
}) {
  return (
    <div className="wf-canvas-tools" role="group" aria-label={ariaLabel}>
      <button type="button" className="wf-icon-button" aria-label={undoLabel} title={undoTitle} disabled={!canUndo} onClick={onUndo}><Undo2 size={16} /></button>
      <button type="button" className="wf-icon-button" aria-label={redoLabel} title={redoTitle} disabled={!canRedo} onClick={onRedo}><Redo2 size={16} /></button>
      <button type="button" className="wf-icon-button" aria-label={autoLayoutLabel} title={autoLayoutTitle} disabled={!canAutoLayout} onClick={onAutoLayout}><Network size={16} /></button>
      <span ref={canvasToolsSlot} className="wf-canvas-tools-slot" />
    </div>
  );
}

interface WorkflowEditorToolbarProps {
  name: string;
  templateMode?: boolean;
  status: string;
  saving: boolean;
  busy: boolean;
  autosaveEnabled: boolean;
  onAutosaveChange(enabled: boolean): void;
  canUndo: boolean;
  canRedo: boolean;
  canAutoLayout: boolean;
  onUndo(): void;
  onRedo(): void;
  onAutoLayout(): void;
  onBack(): void;
  onSave(): void;
  onPublish(mode: WorkflowPublishMode): void;
  onRun(): void;
  canRun: boolean;
  publicationUnavailableReason?: string;
  directBpmnPublication?: ReactNode;
  canvasToolsSlot?: (element: HTMLElement | null) => void;
  runTitle: string;
  moreActions: WorkflowToolbarActionGroup[];
}

export function WorkflowEditorToolbar(props: WorkflowEditorToolbarProps) {
  const pending = props.saving || /^(Autosaving|Saving|Preparing|Starting|Exporting|Promoting|Publishing)/.test(props.status);

  return (
    <WorkflowEditorToolbarFrame
      identity={
        <><StudioShellBreadcrumbs
          items={[{ href: props.templateMode ? "/workflows/templates" : "/workflows/definitions", label: props.templateMode ? "Templates" : "Definitions" }]}
          current={props.name}
          renderLink={item => <StudioLink href={item.href} onClick={event => {
            if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            props.onBack();
          }}>{item.label}</StudioLink>} />
          <span className="wf-editor-title">
            <strong role="heading" aria-level={2} title={props.name}>{props.name}</strong>
            <StatusChip tone={props.templateMode ? "neutral" : "warning"}>{props.templateMode ? "Template" : "Draft"}</StatusChip>
          </span></>
      }
      metadata={
        <span className="wf-editor-save-status" role="status" aria-live="polite" aria-atomic="true" title={props.status || undefined} data-pending={pending || undefined}>
          {props.status ? <>
            {pending ? <LoaderCircle size={12} aria-hidden="true" /> : <Check size={12} aria-hidden="true" />}
            <span>{props.status}</span>
          </> : null}
        </span>
      }
      actions={
        <>
        <WorkflowCanvasCommandBar
          canUndo={props.canUndo}
          canRedo={props.canRedo}
          canAutoLayout={props.canAutoLayout}
          onUndo={props.onUndo}
          onRedo={props.onRedo}
          onAutoLayout={props.onAutoLayout}
          canvasToolsSlot={props.canvasToolsSlot}
        />
        <button type="button" className="wf-icon-button wf-save-button" title="Save workflow" disabled={props.busy} onClick={props.onSave}><Save size={16} aria-hidden="true" /><span className="wf-toolbar-sr-only">Save</span></button>
        {!props.templateMode ? props.directBpmnPublication : null}
        {!props.templateMode ? <PublishActionButton unavailableReason={props.publicationUnavailableReason} busy={props.busy} saving={props.saving} onPublish={props.onPublish} /> : null}
        {!props.templateMode ? <StudioButton size="sm" type="button" aria-label="Test Run" disabled={!props.canRun} title={props.runTitle} onClick={props.onRun}><Play size={16} aria-hidden="true" /> Test Run</StudioButton> : null}
        <OverflowMenu ariaLabel="More workflow actions" triggerIcon={<MoreHorizontal size={18} aria-hidden="true" />}
          popoverClassName="wf-editor-actions-popover" minWidth={240} maxHeight={480}
          items={[
            { id: "autosave", group: "Saving", label: "Autosave", detail: props.autosaveEnabled ? "On" : "Off",
              selection: { type: "checkbox", checked: props.autosaveEnabled },
              onSelect: () => props.onAutosaveChange(!props.autosaveEnabled) },
            ...props.moreActions.flatMap(group => group.actions.map(action => ({
              ...action, id: `${group.label}-${action.label}`, group: group.label
            })))
          ]} />
        </>
      }
    />
  );
}
