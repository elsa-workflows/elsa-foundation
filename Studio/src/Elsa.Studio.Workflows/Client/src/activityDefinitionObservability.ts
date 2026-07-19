export const activityDefinitionsObservationEvent = "elsa:activity-definitions:observation";

export type ActivityDefinitionsObservation = {
  event: "route-view" | "query-start" | "query-success" | "query-failure" | "refresh" | "open-definition" | "create-start" | "create-success" | "create-failure" | "provider-editor" | "autosave" | "test-run" | "upgrade-plan" | "upgrade-apply";
  surface: "collection" | "workbench" | "creation" | "editor" | "upgrade-workbench";
  outcome?: "ready" | "empty" | "stale" | "unavailable" | "forbidden" | "not-found" | "expired" | "failed" | "pending" | "saving" | "saved" | "offline" | "conflict" | "dispatching" | "reconciling" | "accepted" | "waiting" | "running" | "suspended" | "completed" | "faulted" | "cancelled" | "validation-rejected" | "dispatch-rejected";
  providerKey?: string;
  providerSchemaVersion?: string;
  hasSearch?: boolean;
  hasAuthorityFilter?: boolean;
  hasProviderFilter?: boolean;
  pageSize?: 10 | 25 | 50;
  resultBand?: "none" | "one-to-ten" | "eleven-to-fifty" | "more-than-fifty";
  durationBucket?: "under-250ms" | "250ms-to-1s" | "1s-to-5s" | "over-5s";
};

const events = new Set<ActivityDefinitionsObservation["event"]>([
  "route-view", "query-start", "query-success", "query-failure", "refresh", "open-definition", "create-start", "create-success", "create-failure", "provider-editor", "autosave", "test-run", "upgrade-plan", "upgrade-apply"
]);
const surfaces = new Set<ActivityDefinitionsObservation["surface"]>(["collection", "workbench", "creation", "editor", "upgrade-workbench"]);
const outcomes = new Set<NonNullable<ActivityDefinitionsObservation["outcome"]>>([
  "ready", "empty", "stale", "unavailable", "forbidden", "not-found", "expired", "failed", "pending", "saving", "saved", "offline", "conflict", "dispatching", "reconciling", "accepted", "waiting", "running", "suspended", "completed", "faulted", "cancelled", "validation-rejected", "dispatch-rejected"
]);
const pageSizes = new Set<NonNullable<ActivityDefinitionsObservation["pageSize"]>>([10, 25, 50]);
const resultBands = new Set<NonNullable<ActivityDefinitionsObservation["resultBand"]>>([
  "none", "one-to-ten", "eleven-to-fifty", "more-than-fifty"
]);
const durationBuckets = new Set<NonNullable<ActivityDefinitionsObservation["durationBucket"]>>([
  "under-250ms", "250ms-to-1s", "1s-to-5s", "over-5s"
]);

/**
 * Emits only the closed, privacy-reviewed Activity Definitions observation contract. The Studio host
 * decides whether to listen and where to route events; this module never talks to a product analytics
 * endpoint directly.
 */
export function observeActivityDefinitions(candidate: ActivityDefinitionsObservation) {
  if (typeof window === "undefined" || !events.has(candidate.event) || !surfaces.has(candidate.surface)) return;

  const detail: ActivityDefinitionsObservation = {
    event: candidate.event,
    surface: candidate.surface
  };
  if (candidate.outcome && outcomes.has(candidate.outcome)) detail.outcome = candidate.outcome;
  if (isSafeCapabilityIdentity(candidate.providerKey)) detail.providerKey = candidate.providerKey;
  if (isSafeCapabilityIdentity(candidate.providerSchemaVersion)) detail.providerSchemaVersion = candidate.providerSchemaVersion;
  if (typeof candidate.hasSearch === "boolean") detail.hasSearch = candidate.hasSearch;
  if (typeof candidate.hasAuthorityFilter === "boolean") detail.hasAuthorityFilter = candidate.hasAuthorityFilter;
  if (typeof candidate.hasProviderFilter === "boolean") detail.hasProviderFilter = candidate.hasProviderFilter;
  if (candidate.pageSize && pageSizes.has(candidate.pageSize)) detail.pageSize = candidate.pageSize;
  if (candidate.resultBand && resultBands.has(candidate.resultBand)) detail.resultBand = candidate.resultBand;
  if (candidate.durationBucket && durationBuckets.has(candidate.durationBucket)) detail.durationBucket = candidate.durationBucket;

  window.dispatchEvent(new CustomEvent<ActivityDefinitionsObservation>(activityDefinitionsObservationEvent, { detail }));
}

function isSafeCapabilityIdentity(value: string | undefined): value is string {
  return Boolean(value && value.length <= 128 && /^[A-Za-z0-9._-]+$/.test(value));
}

export function activityDefinitionResultBand(count: number): NonNullable<ActivityDefinitionsObservation["resultBand"]> {
  if (count <= 0) return "none";
  if (count <= 10) return "one-to-ten";
  if (count <= 50) return "eleven-to-fifty";
  return "more-than-fifty";
}

export function activityDefinitionDurationBucket(durationMs: number): NonNullable<ActivityDefinitionsObservation["durationBucket"]> {
  if (durationMs < 250) return "under-250ms";
  if (durationMs < 1_000) return "250ms-to-1s";
  if (durationMs < 5_000) return "1s-to-5s";
  return "over-5s";
}
