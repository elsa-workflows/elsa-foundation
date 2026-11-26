import { StudioDialog, StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
import { useId, useRef, useState } from "react";
import { AlertTriangle } from "lucide-react";
import { maxOperatorReasonLength, validateOperatorReason, type OperatorActionModel } from "../operatorActions";
import "./operatorActionDialog.css";

export interface OperatorActionForm {
  reason: string;
  decision: string | null;
}

/**
 * The one confirmation dialog for Retry, Resolve and Abandon (spec 260 contracts/studio-interaction.md): what the action
 * targets, what it will do, the outcome for a resolve, and a reason. Escape cancels; the confirm button stays disabled
 * until the form is valid.
 */
export function OperatorActionDialog({
  model,
  busy = false,
  error = null,
  onConfirm,
  onCancel,
  restoreFocus
}: {
  model: OperatorActionModel;
  /** The submission is in flight: the form is locked. */
  busy?: boolean;
  /** Why the last submission failed, when the dialog stays open so it can be sent again with the same key. */
  error?: string | null;
  onConfirm(form: OperatorActionForm): void;
  onCancel(): void;
  /** Where focus goes when the dialog closes and the control that opened it is gone. */
  restoreFocus?(): void;
}) {
  const firstControl = useRef<HTMLInputElement | HTMLTextAreaElement | null>(null);
  const [opener] = useState(() => document.activeElement instanceof HTMLElement ? document.activeElement : null);
  const id = useId();
  const reasonId = `${id}-reason`;
  const hintId = `${id}-hint`;
  const reasonErrorId = `${id}-reason-error`;
  const outcomeErrorId = `${id}-outcome-error`;
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);
  const [decision, setDecision] = useState<string | null>(model.initialDecision ?? null);

  const reasonError = validateOperatorReason(reason, model.reasonRequired);
  const needsOutcome = !!model.outcomes && model.outcomes.length > 0;
  const valid = !reasonError && (!needsOutcome || !!decision);
  const showReasonError = touched && !!reasonError;
  const describedBy = [hintId, showReasonError ? reasonErrorId : null].filter(Boolean).join(" ");

  return (
    <StudioDialog open title={model.title} className="wf-operator-dialog" onClose={onCancel} dismissDisabled={busy}
      initialFocusRef={firstControl} noValidate onCloseAutoFocus={event => {
        event.preventDefault();
        if (document.activeElement !== document.body) return;
        if (opener?.isConnected && !opener.matches(":disabled")) opener.focus();
        else restoreFocus?.();
      }} onSubmit={event => {
        event.preventDefault();
        setTouched(true);
        if (valid && !busy) onConfirm({ reason: reason.trim(), decision });
      }}
      footer={<StudioDialogFooter onCancel={onCancel} cancelDisabled={busy} confirmDisabled={!valid || busy}
        confirmVariant={model.tone === "danger" ? "danger" : "primary"} confirmLabel={busy ? "Submitting…" : model.confirmLabel} />}>
      <dl className="wf-operator-facts">
        <div>
          <dt>Target</dt>
          <dd>{model.target.map(line => <span key={line}>{line}</span>)}</dd>
        </div>
      </dl>

      {needsOutcome ? (
        <fieldset className="wf-operator-outcomes" aria-describedby={!decision && touched ? outcomeErrorId : undefined}>
          <legend>Outcome</legend>
          {model.outcomes!.map((outcome, index) => {
            const optionId = `${id}-outcome-${index}`;
            const initial = decision ? decision === outcome.decision : index === 0;
            return (
              <div key={outcome.decision} className="wf-operator-outcome">
                <input
                  id={optionId}
                  type="radio"
                  name={`${id}-outcome`}
                  value={outcome.decision}
                  checked={decision === outcome.decision}
                  disabled={busy}
                  aria-describedby={`${optionId}-description`}
                  ref={element => { if (initial) firstControl.current = element; }}
                  onChange={() => setDecision(outcome.decision)}
                />
                <label htmlFor={optionId}>{outcome.label}</label>
                <small id={`${optionId}-description`}>{outcome.description}</small>
              </div>
            );
          })}
          {!decision && touched ? <small id={outcomeErrorId} className="wf-operator-error">Choose an outcome.</small> : null}
        </fieldset>
      ) : null}

      <dl className="wf-operator-facts">
        <div>
          <dt>Effect</dt>
          <dd>
            {model.effect.map(line => <span key={line}>{line}</span>)}
            {model.notUndone && model.notUndone.length > 0 ? (
              <ul aria-label="Steps that stay not undone">
                {model.notUndone.map(item => <li key={item}>{item}</li>)}
              </ul>
            ) : null}
            {model.effectAfter?.map(line => <span key={line}>{line}</span>)}
          </dd>
        </div>
      </dl>

      <div className="wf-operator-reason"><StudioField controlId={reasonId} label={<>Reason {model.reasonRequired ? "(required)" : "(optional)"}</>}>

        <textarea
          id={reasonId}
          rows={2}
          value={reason}
          disabled={busy}
          aria-required={model.reasonRequired || undefined}
          aria-invalid={showReasonError || undefined}
          aria-describedby={describedBy}
          ref={element => { if (!needsOutcome) firstControl.current = element; }}
          onChange={event => {
            setReason(event.target.value);
            if (event.target.value.trim()) setTouched(true);
          }}
          onBlur={() => setTouched(true)}
        />
        <small id={hintId}>
          Visible to everyone who can view this run. Do not include secrets. Up to {maxOperatorReasonLength} characters.
        </small>
        {showReasonError ? <small id={reasonErrorId} className="wf-operator-error">{reasonError}</small> : null}
      </StudioField></div>

      {error ? (
        <p className="wf-operator-submit-error" role="alert">
          <AlertTriangle size={14} aria-hidden="true" /> {error}
        </p>
      ) : null}
    </StudioDialog>
  );
}
