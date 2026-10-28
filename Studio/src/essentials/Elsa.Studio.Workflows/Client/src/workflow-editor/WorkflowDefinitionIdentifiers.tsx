import { useId, useRef, useState, type ReactNode } from "react";
import { Fingerprint, X } from "lucide-react";
import { AnchoredPopover, CopyableIdentifier } from "@elsa-workflows/studio-ui";
import type { WorkflowListIdentifier } from "./WorkflowListIdentity";
import "./workflowListIdentity.css";

export function WorkflowDefinitionIdentifiers({ name, identifiers, description, children }: {
  name: string;
  identifiers: readonly WorkflowListIdentifier[];
  description?: string | null;
  children?: ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const dialog = useRef<HTMLDivElement>(null);
  const id = useId();
  const close = (restoreFocus = true) => {
    setOpen(false);
    if (restoreFocus) trigger.current?.focus();
  };

  return <>
    <button
      ref={trigger}
      type="button"
      className="wf-icon-button wf-definition-identifiers-trigger"
      aria-label={`Identifiers for ${name}`}
      title="View identifiers and details"
      aria-haspopup="dialog"
      aria-expanded={open}
      aria-controls={open ? id : undefined}
      onClick={() => setOpen(value => !value)}
    ><Fingerprint size={16} aria-hidden="true" /></button>
    <AnchoredPopover
      anchorRef={trigger}
      open={open}
      id={id}
      className="wf-definition-identifiers-popover"
      minWidth={360}
      maxHeight={480}
      onPositioned={() => dialog.current?.querySelector<HTMLButtonElement>("button")?.focus()}
      onDismiss={() => close(false)}
    >
      <div
        ref={dialog}
        role="dialog"
        aria-label={`Workflow definition identifiers for ${name}`}
        tabIndex={-1}
        onClick={event => event.stopPropagation()}
        onKeyDownCapture={event => {
          if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            close();
          }
        }}
        onBlur={event => {
          const next = event.relatedTarget;
          if (next instanceof Node && !event.currentTarget.contains(next) && !trigger.current?.contains(next)) close(false);
        }}
      >
        <header>
          <strong>Identifiers</strong>
          <button type="button" className="wf-icon-button" aria-label="Close identifiers" onClick={() => close()}><X size={16} aria-hidden="true" /></button>
        </header>
        <div className="wf-list-identity-details wf-definition-identifiers">
          {identifiers.map(identifier => <CopyableIdentifier key={identifier.label} label={identifier.label} value={identifier.value} />)}
          {!identifiers.length ? <p>No other identifiers are available.</p> : null}
        </div>
        {description || children ? <div className="wf-definition-extra-details">
          {description ? <p>{description}</p> : null}
          {children}
        </div> : null}
      </div>
    </AnchoredPopover>
  </>;
}
