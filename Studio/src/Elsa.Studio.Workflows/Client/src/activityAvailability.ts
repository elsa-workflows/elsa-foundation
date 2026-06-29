import type {
  ActivityAvailabilityDiagnosticEntry,
  ActivityAvailabilityDiagnosticState,
  ActivityAvailabilityDiagnostics,
  ActivityAvailabilityMode,
  ActivityAvailabilitySettings
} from "./workflowTypes";

const diagnosticStateNames: ActivityAvailabilityDiagnosticState[] = [
  "Available",
  "BlockedByHostBaseline",
  "HiddenByManagementSettings",
  "RemovedFromCatalog",
  "UnresolvedReference"
];

const stateLabels: Record<ActivityAvailabilityDiagnosticState, string> = {
  Available: "Available",
  BlockedByHostBaseline: "Host blocked",
  HiddenByManagementSettings: "Management hidden",
  RemovedFromCatalog: "Removed",
  UnresolvedReference: "Unresolved"
};

/** Normalizes a diagnostic state (number or string) into its canonical name. */
export function getAvailabilityStateName(value: ActivityAvailabilityDiagnosticState | number | undefined | null): ActivityAvailabilityDiagnosticState {
  if (typeof value === "string") return value;
  if (typeof value === "number") return diagnosticStateNames[value] ?? "Available";
  return "Available";
}

export function getAvailabilityStateLabel(value: ActivityAvailabilityDiagnosticState | number | undefined | null): string {
  const state = getAvailabilityStateName(value);
  return stateLabels[state] ?? state;
}

/** Derives a CSS modifier class from a state, e.g. `BlockedByHostBaseline` -> `blocked-by-host-baseline`. */
export function getAvailabilityStateClass(value: ActivityAvailabilityDiagnosticState | number | undefined | null): string {
  return getAvailabilityStateName(value).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

/** True when a diagnostic state means the activity is not addable to new workflows. */
export function isUnavailableState(value: ActivityAvailabilityDiagnosticState | number | undefined | null): boolean {
  return getAvailabilityStateName(value) !== "Available";
}

export function normalizeAvailabilityMode(value: ActivityAvailabilityMode | number | undefined | null): ActivityAvailabilityMode {
  return value === "Only" || value === 1 ? "Only" : "AllExcept";
}

/** 0 = AllExcept, 1 = Only — the numeric wire form the backend enum binds from unambiguously. */
export function getAvailabilityModePayload(mode: ActivityAvailabilityMode): number {
  return mode === "Only" ? 1 : 0;
}

export interface AvailabilityDraft {
  mode: ActivityAvailabilityMode;
  activityTypes: string[];
  sets: string[];
}

export function createAvailabilityDraft(settings: ActivityAvailabilitySettings | null | undefined): AvailabilityDraft {
  const rules = settings?.rules;
  return {
    mode: normalizeAvailabilityMode(settings?.mode),
    activityTypes: rules?.activityTypes ?? [],
    sets: rules?.sets ?? []
  };
}

function isActivityTypeReference(entry: ActivityAvailabilityDiagnosticEntry): boolean {
  return entry.referenceKind === 0 || entry.referenceKind === "ActivityType";
}

/** Catalog activity rows (resolved activity-type references) sorted by display name. */
export function getAvailabilityActivityEntries(diagnostics: ActivityAvailabilityDiagnostics | null | undefined): ActivityAvailabilityDiagnosticEntry[] {
  return [...(diagnostics?.items ?? [])]
    .filter(isActivityTypeReference)
    .filter(entry => entry.activityTypeKey && entry.activityDefinitionId)
    .sort((left, right) => getActivityDisplayName(left).localeCompare(getActivityDisplayName(right)));
}

/** Removed/unresolved references, surfaced separately so the policy intent stays visible. */
export function getUnresolvedAvailabilityEntries(diagnostics: ActivityAvailabilityDiagnostics | null | undefined): ActivityAvailabilityDiagnosticEntry[] {
  return [...(diagnostics?.items ?? [])]
    .filter(entry => {
      const state = getAvailabilityStateName(entry.state);
      return state === "RemovedFromCatalog" || state === "UnresolvedReference";
    })
    .sort((left, right) => (left.referenceName ?? "").localeCompare(right.referenceName ?? ""));
}

export function toggleListValue(values: string[], value: string): string[] {
  return values.includes(value)
    ? values.filter(item => item !== value)
    : [...values, value].sort((left, right) => left.localeCompare(right));
}

export function getActivityDisplayName(activity: { displayName?: string | null; activityTypeKey?: string | null } | null | undefined): string {
  const displayName = activity?.displayName?.trim();
  if (displayName) return displayName;
  const typeName = activity?.activityTypeKey?.split(".").filter(Boolean).at(-1) ?? "";
  return humanizeIdentifier(typeName) || activity?.activityTypeKey || "Activity";
}

export function humanizeIdentifier(value: string): string {
  return value
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .trim();
}

/**
 * Finds a non-blocking availability warning for an authored activity, matching on any of its
 * identifiers (version id, type key) against the diagnostics. Returns null when the activity is
 * still available. `typeByVersionId` maps a node's activityVersionId to its activity type key.
 */
export function findActivityAvailabilityDiagnostic(
  candidateValues: Array<string | null | undefined>,
  diagnostics: ActivityAvailabilityDiagnostics | null | undefined
): ActivityAvailabilityDiagnosticEntry | null {
  const candidates = new Set(candidateValues.filter((value): value is string => !!value));
  return (
    (diagnostics?.items ?? []).find(entry => {
      if (!isUnavailableState(entry.state)) return false;
      return [entry.activityDefinitionId, entry.activityTypeKey, entry.referenceName].some(value => value && candidates.has(value));
    }) ?? null
  );
}
