export const reusableActivityObservationEvent = "elsa:reusable-activity:observation";

export type ReusableActivityObservation = {
  event: "picker-load" | "placement" | "boundary-inspection";
  surface: "workflow-designer" | "run-workbench";
  outcome: "ready" | "empty" | "unavailable" | "failed";
  hasUpgrade?: boolean;
  layoutMode?: "pinned" | "automatic" | "unavailable";
};

const events = new Set<ReusableActivityObservation["event"]>(["picker-load", "placement", "boundary-inspection"]);
const surfaces = new Set<ReusableActivityObservation["surface"]>(["workflow-designer", "run-workbench"]);
const outcomes = new Set<ReusableActivityObservation["outcome"]>(["ready", "empty", "unavailable", "failed"]);
const layoutModes = new Set<NonNullable<ReusableActivityObservation["layoutMode"]>>(["pinned", "automatic", "unavailable"]);

/**
 * Emits the closed reusable-activity observation contract. Exact definition/version identities,
 * provider data, tenant data, and free-form diagnostics are intentionally not part of this contract.
 */
export function observeReusableActivity(candidate: ReusableActivityObservation) {
  if (
    typeof window === "undefined"
    || !events.has(candidate.event)
    || !surfaces.has(candidate.surface)
    || !outcomes.has(candidate.outcome)
  ) return;

  const detail: ReusableActivityObservation = {
    event: candidate.event,
    surface: candidate.surface,
    outcome: candidate.outcome
  };
  if (typeof candidate.hasUpgrade === "boolean") detail.hasUpgrade = candidate.hasUpgrade;
  if (candidate.layoutMode && layoutModes.has(candidate.layoutMode)) detail.layoutMode = candidate.layoutMode;

  window.dispatchEvent(new CustomEvent<ReusableActivityObservation>(reusableActivityObservationEvent, { detail }));
}
