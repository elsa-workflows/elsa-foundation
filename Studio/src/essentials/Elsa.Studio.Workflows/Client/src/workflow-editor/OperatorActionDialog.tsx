import { useId, useRef, useState } from "react";
import { AlertTriangle } from "lucide-react";
import { useDialogFocus } from "./useDialogFocus";
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
  const dialogRef = useRef<HTMLElement | null>(null);
  const id = useId();
  const titleId = `${id}-title`;
  const reasonId = `${id}-reason`;
  const hintId = `${id}-hint`;
  const reasonErrorId = `${id}-reason-error`;
  const outcomeErrorId = `${id}-outcome-error`;
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);
  const [decision, setDecision] = useState<string | null>(model.initialDecision ?? null);
  useDialogFocus(dialogRef, busy ? null : onCancel, true, restoreFocus);

  const reasonError = validateOperatorReason(reason, model.reasonRequired);
  const needsOutcome = !!model.outcomes && model.outcomes.length > 0;
  const valid = !reasonError && (!needsOutcome || !!decision);
  const showReasonError = touched && !!reasonError;
  const describedBy = [hintId, showReasonError ? reasonErrorId : null].filter(Boolean).join(" ");

  return (
    <div className="wf-dialog-backdrop" role="presentation" onMouseDown={event => {
      if (event.target === event.currentTarget && !busy) onCancel();
    }}>
      <section
        ref={dialogRef}
        className="wf-dialog wf-operator-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        data-tone={model.tone}
      >
        <form
          noValidate
          onSubmit={event => {
            event.preventDefault();
            setTouched(true);
            if (!valid || busy) return;
            onConfirm({ reason: reason.trim(), decision });
          }}
        >
          <div className="wf-dialog-heading">
            <h3 id={titleId}>{model.title}</h3>
          </div>

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
                      data-dialog-initial-focus={initial ? "" : undefined}
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

          <div className="wf-operator-reason">
            <label htmlFor={reasonId}>Reason {model.reasonRequired ? "(required)" : "(optional)"}</label>
            <textarea
              id={reasonId}
              rows={2}
              value={reason}
              disabled={busy}
              aria-required={model.reasonRequired || undefined}
              aria-invalid={showReasonError || undefined}
              aria-describedby={describedBy}
              data-dialog-initial-focus={needsOutcome ? undefined : ""}
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
          </div>

          {error ? (
            <p className="wf-operator-submit-error" role="alert">
              <AlertTriangle size={14} aria-hidden="true" /> {error}
            </p>
          ) : null}

          <div className="wf-dialog-actions">
            <button type="button" onClick={onCancel} disabled={busy}>Cancel</button>
            <button type="submit" disabled={!valid || busy} data-tone={model.tone}>
              {busy ? "Submitting…" : model.confirmLabel}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}
