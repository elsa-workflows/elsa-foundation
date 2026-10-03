import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { Check, ChevronRight, GitBranch, LoaderCircle, MoreHorizontal, Network, Play, Redo2, Save, Undo2 } from "lucide-react";
import { AnchoredPopover } from "@elsa-workflows/studio-ui";

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

interface WorkflowEditorToolbarProps {
  name: string;
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
  onPublish(): void;
  onRun(): void;
  canRun: boolean;
  runTitle: string;
  runStatus?: ReactNode;
  moreActions: WorkflowToolbarActionGroup[];
}

export function WorkflowEditorToolbar(props: WorkflowEditorToolbarProps) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const openAtEndRef = useRef(false);
  const menuId = useId();
  const pending = props.saving || /^(Autosaving|Saving|Preparing|Starting|Exporting|Promoting|Publishing)/.test(props.status);

  const closeMenu = (restoreFocus = true) => {
    setOpen(false);
    if (restoreFocus) triggerRef.current?.focus();
  };

  useEffect(() => {
    if (!open) return;
    // The anchored surface starts hidden until its layout effect has positioned the portal.
    const frame = requestAnimationFrame(() => {
      const items = menuRef.current?.querySelectorAll<HTMLButtonElement>("button:not(:disabled)");
      items?.[openAtEndRef.current ? items.length - 1 : 0]?.focus();
    });
    return () => cancelAnimationFrame(frame);
  }, [open]);

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
    <header className="wf-editor-top wf-editor-toolbar">
      <div className="wf-editor-identity">
        <button type="button" className="wf-link-button" onClick={props.onBack}>Definitions</button>
        <ChevronRight size={14} aria-hidden="true" />
        <div className="wf-editor-heading">
          <strong title={props.name}>{props.name}</strong>
          <div className="wf-editor-metadata">
            <span className="wf-chip">Draft</span>
            <span className="wf-editor-save-status" role="status" aria-live="polite" aria-atomic="true" title={props.status || undefined} data-pending={pending || undefined}>
              {props.status ? <>
                {pending ? <LoaderCircle size={12} aria-hidden="true" /> : <Check size={12} aria-hidden="true" />}
                <span>{props.status}</span>
              </> : null}
            </span>
          </div>
        </div>
      </div>
      <div className="wf-editor-actions">
        <div className="wf-canvas-tools" role="group" aria-label="Canvas tools">
          <button type="button" className="wf-icon-button" aria-label="Undo" title="Undo (Ctrl+Z)" disabled={!props.canUndo} onClick={props.onUndo}><Undo2 size={16} /></button>
          <button type="button" className="wf-icon-button" aria-label="Redo" title="Redo (Ctrl+Shift+Z)" disabled={!props.canRedo} onClick={props.onRedo}><Redo2 size={16} /></button>
          <button type="button" className="wf-icon-button" aria-label="Auto-layout" title="Auto-layout the canvas" disabled={!props.canAutoLayout} onClick={props.onAutoLayout}><Network size={16} /></button>
        </div>
        <button type="button" disabled={props.busy} onClick={props.onSave}><Save size={15} /> Save</button>
        <button type="button" className="wf-review-button" disabled={props.busy} title={props.saving ? "Finishing the current save; the review will open once it settles." : undefined} onClick={props.onPublish}><GitBranch size={15} /> Review &amp; publish</button>
        {props.runStatus}
        <button type="button" className="wf-run-button" disabled={!props.canRun} title={props.runTitle} onClick={props.onRun}><Play size={15} /> Run</button>
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
      </div>
      <AnchoredPopover anchorRef={triggerRef} open={open} className="wf-editor-actions-popover" minWidth={240} maxHeight={480} onDismiss={() => closeMenu(false)}>
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
    </header>
  );
}
