import { createWorkflowRootOptions, type CreateWorkflowKind } from "./editorTypes";

// The Flowchart / Sequence title + hint block shared by the two "choose a container" surfaces:
// CreateWorkflowDialog's radio card and the empty-slot picker's action button. Keeping the copy in one
// place means both stay in sync with createWorkflowRootOptions.
function RootOptionBody({ label, hint }: { label: string; hint: string }) {
  return (
    <>
      <span className="wf-root-card-title">{label}</span>
      <span className="wf-root-card-hint">{hint}</span>
    </>
  );
}

// Radio-group variant used inside the Create Workflow dialog (one root activity is selected).
export function WorkflowRootRadioCards({ value, onChange }: {
  value: CreateWorkflowKind;
  onChange(value: CreateWorkflowKind): void;
}) {
  return (
    <div className="wf-root-cards" role="radiogroup" aria-label="Root activity">
      {createWorkflowRootOptions.map(option => {
        const checked = value === option.value;
        return (
          <label key={option.value} className="wf-root-card" data-checked={checked || undefined}>
            <input
              type="radio"
              name="wf-root-kind"
              aria-label={option.label}
              value={option.value}
              checked={checked}
              onChange={() => onChange(option.value)}
            />
            <RootOptionBody label={option.label} hint={option.hint} />
          </label>
        );
      })}
    </div>
  );
}

// Button variant used by the empty-slot picker (clicking a card immediately drops that container into
// the current slot). Reuses the same card visual as the dialog.
export function WorkflowRootButtonCards({ onPick }: { onPick(value: CreateWorkflowKind): void }) {
  return (
    <div className="wf-root-cards" role="group" aria-label="Fill this slot">
      {createWorkflowRootOptions.map(option => (
        <button
          key={option.value}
          type="button"
          className="wf-root-card"
          aria-label={option.label}
          onClick={() => onPick(option.value)}
        >
          <RootOptionBody label={option.label} hint={option.hint} />
        </button>
      ))}
    </div>
  );
}
