import "./WorkflowEditorToolbar.css";
import { createContext, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { AlertTriangle, Check, LoaderCircle, MoreHorizontal, Network, Play, Redo2, Save, Undo2 } from "lucide-react";
import { AnchoredPopover, StudioBreadcrumbs } from "@elsa-workflows/studio-ui";
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

export function WorkflowEditorSaveStatus({ status, saving, success, attention }: {
  status: string;
  saving: boolean;
  success?: boolean;
  attention?: boolean;
}) {
  const pending = saving || /^(Autosaving|Saving|Preparing|Starting|Exporting|Promoting|Publishing)/.test(status);
  const needsAttention = attention ?? /offline|conflict|failed|failure|error|unavailable/i.test(status);
  const isSuccess = success ?? (!pending && !needsAttention);

  return (
    <span className="wf-editor-save-status" role="status" aria-live="polite" aria-atomic="true" title={status || undefined} data-pending={pending || undefined} data-attention={needsAttention || undefined}>
      {status ? <>
        {pending ? <LoaderCircle size={12} aria-hidden="true" /> : isSuccess ? <Check size={12} aria-hidden="true" /> : needsAttention ? <AlertTriangle size={12} aria-hidden="true" /> : null}
        <span>{status}</span>
      </> : null}
    </span>
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
  onRunPublished?(): void;
  canRunPublished?: boolean;
  runPublishedTitle?: string;
  canRun: boolean;
  publicationUnavailableReason?: string;
  directBpmnPublication?: ReactNode;
  canvasToolsSlot?: (element: HTMLElement | null) => void;
  runTitle: string;
  moreActions: WorkflowToolbarActionGroup[];
}

export function WorkflowEditorToolbar(props: WorkflowEditorToolbarProps) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const openAtEndRef = useRef(false);
  const menuId = useId();
  const closeMenu = (restoreFocus = true) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  };

  const focusInitialMenuItem = () => {
    const items = menuRef.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)");
    items?.[openAtEndRef.current ? items.length - 1 : 0]?.focus();
  };

  const handleMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape" || event.key === "Tab") {
      if (event.key === "Escape") event.preventDefault();
      closeMenu();
      return;
    }
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)") ?? []);
    const current = items.indexOf(document.activeElement as HTMLButtonElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1
      : (current + (event.key === "ArrowDown" ? 1 : -1) + items.length) % items.length;
    items[next]?.focus();
  };

  return (
    <WorkflowEditorToolbarFrame
      identity={
        <StudioBreadcrumbs
          items={[{ href: props.templateMode ? "/workflows/templates" : "/workflows/definitions", label: props.templateMode ? "Templates" : "Definitions" }]}
          current={<span className="wf-editor-title">
            <strong title={props.name}>{props.name}</strong>
            <span className="wf-chip">{props.templateMode ? "Template" : "Draft"}</span>
          </span>}
          renderLink={item => <StudioLink href={item.href} onClick={event => {
            if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            props.onBack();
          }}>{item.label}</StudioLink>}
        />
      }
      metadata={
        <WorkflowEditorSaveStatus status={props.status} saving={props.saving} />
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
        <button type="button" className="wf-icon-button wf-save-button" title="Save workflow" disabled={props.busy} onClick={props.onSave}><Save size={15} aria-hidden="true" /><span className="wf-toolbar-sr-only">Save</span></button>
        {!props.templateMode ? props.directBpmnPublication : null}
        {!props.templateMode ? <PublishActionButton unavailableReason={props.publicationUnavailableReason} busy={props.busy} saving={props.saving} onPublish={props.onPublish} /> : null}
        {!props.templateMode && props.onRunPublished ? <button type="button" className="wf-run-published-button" disabled={!props.canRunPublished || props.busy} title={props.runPublishedTitle} onClick={props.onRunPublished}><Play size={15} aria-hidden="true" /> Run</button> : null}
        {!props.templateMode ? <button type="button" className="wf-run-button" disabled={!props.canRun} title={props.runTitle} onClick={props.onRun}><Play size={15} aria-hidden="true" /> Test Run</button> : null}
        <button
          ref={triggerRef}
          type="button"
          className="wf-icon-button"
          aria-label="More workflow actions"
          title="More workflow actions"
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={open ? menuId : undefined}
          onClick={() => {
            openAtEndRef.current = false;
            if (open) closeMenu(); else setOpen(true);
          }}
          onKeyDown={event => {
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
              event.preventDefault();
              openAtEndRef.current = event.key === "ArrowUp";
              setOpen(true);
            }
          }}
        ><MoreHorizontal size={18} /></button>
        </>
      }
      afterActions={
      <AnchoredPopover anchorRef={triggerRef} open={open} className="wf-editor-actions-popover" minWidth={240} maxHeight={480} onDismiss={() => closeMenu(false)} onPositioned={focusInitialMenuItem}>
        <div ref={menuRef} id={menuId} role="menu" aria-label="More workflow actions" onKeyDown={handleMenuKeyDown}>
          <div role="group" aria-label="Saving">
            <span className="wf-editor-menu-heading" aria-hidden="true">Saving</span>
            <button type="button" role="menuitemcheckbox" aria-checked={props.autosaveEnabled} tabIndex={-1} onClick={() => props.onAutosaveChange(!props.autosaveEnabled)}>
              <Check size={15} aria-hidden="true" style={{ visibility: props.autosaveEnabled ? "visible" : "hidden" }} /> Autosave
              <span className="wf-editor-menu-value">{props.autosaveEnabled ? "On" : "Off"}</span>
            </button>
          </div>
          {props.moreActions.filter(group => group.actions.length > 0).map(group => (
            <div key={group.label} role="group" aria-label={group.label}>
              <span className="wf-editor-menu-heading" aria-hidden="true">{group.label}</span>
              {group.actions.map(action => (
                <button key={action.label} type="button" role="menuitem" tabIndex={-1} disabled={action.disabled} title={action.title} onClick={() => { closeMenu(); action.onSelect(); }}>
                  {action.icon} {action.label}
                </button>
              ))}
            </div>
          ))}
        </div>
      </AnchoredPopover>
      }
    />
  );
}
