import type { ActivityCatalogItem } from "../workflowTypes";
import { findCompositionActivity, getCompositionKind, getCreateRootActivity } from "./editorHelpers";
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
export function WorkflowRootRadioCards({ catalog, value, onChange, availabilityKnown = true }: {
  catalog: ActivityCatalogItem[];
  value: string | null;
  onChange(value: string): void;
  availabilityKnown?: boolean;
}) {
  const options = catalog.filter(activity => activity.available !== false && activity.authoringTemplate);
  const selected = getCreateRootActivity(value, catalog);
  return (
    <div className="wf-root-cards" role="radiogroup" aria-label="Root activity">
      {createWorkflowRootOptions.map(option => {
        const activity = findCompositionActivity(options, option.value);
        const reason = availabilityKnown ? getRootAvailabilityReason(catalog, option.value, option.label) : undefined;
        const checked = Boolean(activity && selected?.activityVersionId === activity.activityVersionId);
        const reasonId = `wf-root-availability-${option.value}`;
        return (
          <label key={option.value} className="wf-root-card" data-checked={checked || undefined} aria-disabled={!availabilityKnown || !activity || undefined}>
            <input
              type="radio"
              name="wf-root-kind"
              aria-label={option.label}
              value={activity?.activityVersionId ?? ""}
              checked={checked}
              disabled={!availabilityKnown || !activity}
              aria-describedby={reason ? reasonId : undefined}
              onChange={() => activity && onChange(activity.activityVersionId)}
            />
            <RootOptionBody label={option.label} hint={option.hint} />
            {reason ? <span className="wf-root-card-unavailable" id={reasonId}>{reason}</span> : null}
          </label>
        );
      })}
    </div>
  );
}

function getRootAvailabilityReason(catalog: ActivityCatalogItem[], kind: CreateWorkflowKind, label: string) {
  const candidates = catalog.filter(activity => getCompositionKind(activity) === kind);
  if (!candidates.length) return `${label} isn't included in this activity catalog.`;
  if (candidates.some(activity => activity.available !== false && activity.authoringTemplate)) return undefined;
  if (candidates.some(activity => activity.available !== false)) {
    return `${label} has no available authoring template in this catalog.`;
  }
  const suppliedReason = candidates.find(activity => activity.availabilityReason?.trim())?.availabilityReason?.trim();
  return suppliedReason ?? `${label} is listed but unavailable in this activity catalog.`;
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
