import { type ReactNode, type RefObject } from "react";
import { X } from "lucide-react";

export function ActivityDefinitionManagementDialogShell({
  dialogRef,
  title,
  kicker,
  submitting,
  onClose,
  children
}: {
  dialogRef: RefObject<HTMLElement | null>;
  title: string;
  kicker: string;
  submitting: boolean;
  onClose(): void;
  children: ReactNode;
}) {
  const titleId = `activity-definition-dialog-${title.toLowerCase().replaceAll(/[^a-z0-9]+/g, "-")}`;
  return <div className="ad-dialog-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !submitting) onClose(); }}>
    <section ref={dialogRef} className="ad-dialog ad-management-dialog" role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
      <header><div><span className="ad-kicker">{kicker}</span><h2 id={titleId}>{title}</h2></div><button type="button" className="ad-icon-button" aria-label="Close" onClick={onClose} disabled={submitting}><X size={18} /></button></header>
      {children}
    </section>
  </div>;
}

export function ActivityDefinitionManagementDialogActions({
  submitting,
  submitLabel,
  canSubmit,
  onClose,
  onSubmit
}: {
  submitting: boolean;
  submitLabel: string;
  canSubmit: boolean;
  onClose(): void;
  onSubmit(): Promise<void>;
}) {
  return <footer><button type="button" onClick={onClose} disabled={submitting}>Cancel</button><button type="button" className="ad-primary-action" onClick={() => void onSubmit()} disabled={!canSubmit}>{submitting ? "Applying atomically…" : submitLabel}</button></footer>;
}
