import type {
  ActivityAvailabilityDiagnosticEntry,
  ActivityAvailabilityDiagnosticState,
  ActivityAvailabilityDiagnostics,
  ActivityAvailabilityMode,
  ActivityAvailabilitySetDiagnostic,
  ActivityAvailabilitySettings
} from "./workflowTypes";
import { groupByCategory } from "./categoryGrouping";
import { argumentNameKeys, isPlainRecord, readArgumentType, readStringField } from "./workflowProperties";

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

// Insertion order mirrors the backend enum so numeric wire values index correctly.
const policyLayerLabels = {
  Catalog: "Catalog",
  HostBaseline: "Host baseline",
  ManagementSettings: "Management settings"
} as const;

const policyLayerNames = Object.keys(policyLayerLabels) as Array<keyof typeof policyLayerLabels>;
const referenceKindNames = ["ActivityType", "ActivitySet"] as const;

function normalizeEnumName<T extends string>(value: unknown, names: readonly T[], fallback: T): T {
  if (typeof value === "number") return names[value] ?? fallback;
  if (typeof value !== "string") return fallback;
  const key = value.replace(/[\s_-]/g, "").toLowerCase();
  return names.find(name => name.toLowerCase() === key) ?? fallback;
}

/** Human label for the policy layer that produced a diagnostic (number or string on the wire). */
export function getAvailabilityLayerLabel(value: unknown): string {
  if (value == null) return "";
  const name = normalizeEnumName(value, policyLayerNames, "Catalog");
  return policyLayerLabels[name];
}

/** Normalizes a diagnostic state (number or string) into its canonical name. */
export function getAvailabilityStateName(value: unknown): ActivityAvailabilityDiagnosticState {
  return normalizeEnumName(value, diagnosticStateNames, "Available");
}

export function getAvailabilityStateLabel(value: unknown): string {
  const state = getAvailabilityStateName(value);
  return stateLabels[state] ?? state;
}

/** Derives a CSS modifier class from a state, e.g. `BlockedByHostBaseline` -> `blocked-by-host-baseline`. */
export function getAvailabilityStateClass(value: unknown): string {
  return getAvailabilityStateName(value).replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

/** True when a diagnostic state means the activity is not addable to new workflows. */
export function isUnavailableState(value: unknown): boolean {
  return getAvailabilityStateName(value) !== "Available";
}

/** True when the host baseline blocks the activity — the management UI cannot override it. */
export function isHostBlockedEntry(entry: ActivityAvailabilityDiagnosticEntry): boolean {
  return getAvailabilityStateName(entry.state) === "BlockedByHostBaseline";
}

export function normalizeAvailabilityMode(value: unknown): ActivityAvailabilityMode {
  return normalizeEnumName(value, ["AllExcept", "Only"], "AllExcept");
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
  return normalizeEnumName(entry.referenceKind, referenceKindNames, "ActivityType") === "ActivityType";
}

export function normalizeActivityAvailabilitySettings(settings: unknown): ActivityAvailabilitySettings {
  const wire = isWireRecord(settings) ? settings : {};
  const rules = (wire.rules ?? wire.Rules ?? {}) as Record<string, unknown>;
  return {
    scope: String(wire.scope ?? wire.Scope ?? "host-default"),
    mode: normalizeAvailabilityMode(wire.mode ?? wire.Mode),
    rules: {
      activityTypes: normalizeStringList(rules.activityTypes ?? rules.ActivityTypes),
      sets: normalizeStringList(rules.sets ?? rules.Sets)
    }
  };
}

export function normalizeActivityAvailabilityDiagnostics(diagnostics: unknown): ActivityAvailabilityDiagnostics {
  const wire = isWireRecord(diagnostics) ? diagnostics : {};
  const items = wire.items ?? wire.Items;
  const sets = wire.sets ?? wire.Sets;
  return {
    items: Array.isArray(items) ? items.filter(isWireRecord).map(normalizeDiagnosticEntry) : [],
    sets: Array.isArray(sets) ? sets.filter(isWireRecord).map(set => ({
      name: String(set.name ?? set.Name ?? ""),
      activityTypeKeys: normalizeStringList(set.activityTypeKeys ?? set.ActivityTypeKeys)
    })) : []
  };
}

function normalizeDiagnosticEntry(entry: Record<string, unknown>): ActivityAvailabilityDiagnosticEntry {
  const read = (name: string) => entry[name] ?? entry[`${name[0].toUpperCase()}${name.slice(1)}`];
  return {
    activityDefinitionId: normalizeOptionalString(read("activityDefinitionId")),
    activityTypeKey: normalizeOptionalString(read("activityTypeKey")),
    displayName: normalizeOptionalString(read("displayName")),
    description: normalizeOptionalString(read("description")),
    category: normalizeOptionalString(read("category")),
    state: getAvailabilityStateName(read("state")),
    layer: normalizeEnumName(read("layer"), policyLayerNames, "Catalog"),
    referenceKind: normalizeEnumName(read("referenceKind"), referenceKindNames, "ActivityType"),
    referenceName: normalizeOptionalString(read("referenceName")),
    reason: String(read("reason") ?? "")
  };
}

function isWireRecord(value: unknown): value is Record<string, unknown> {
  return Boolean(value) && typeof value === "object" && !Array.isArray(value);
}

function normalizeOptionalString(value: unknown): string | null {
  return value == null ? null : String(value);
}

function normalizeStringList(value: unknown): string[] {
  return Array.isArray(value) ? value.filter(item => typeof item === "string") : [];
}

/** The identity an activity row contributes to the management rules list. */
export function getAvailabilityRuleKey(entry: ActivityAvailabilityDiagnosticEntry): string {
  return entry.activityTypeKey ?? entry.activityDefinitionId ?? "";
}

/** Catalog activity rows (resolved activity-type references) sorted by display name. */
export function getAvailabilityActivityEntries(diagnostics: ActivityAvailabilityDiagnostics | null | undefined): ActivityAvailabilityDiagnosticEntry[] {
  return [...(diagnostics?.items ?? [])]
    .filter(isActivityTypeReference)
    .filter(entry => entry.activityTypeKey && entry.activityDefinitionId)
    .sort((left, right) => getAvailabilityActivityName(left).localeCompare(getAvailabilityActivityName(right)));
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

/** Activity rows bucketed by category for the grouped availability list. */
export interface AvailabilityCategoryGroup {
  category: string;
  entries: ActivityAvailabilityDiagnosticEntry[];
}

export function groupAvailabilityEntriesByCategory(entries: ActivityAvailabilityDiagnosticEntry[]): AvailabilityCategoryGroup[] {
  return groupByCategory(entries, entry => entry.category).map(group => ({ category: group.category, entries: group.items }));
}

/**
 * Whether the management draft leaves a rule target (activity type or set) available to new
 * workflows: in "AllExcept" mode the list holds exclusions, in "Only" mode it holds inclusions.
 * For activity rows prefer computeEffectiveAvailability, which also layers set rules and the
 * host baseline.
 */
export function isRuleTargetEnabled(draft: AvailabilityDraft, list: "activityTypes" | "sets", key: string): boolean {
  const listed = draft[list].includes(key);
  return draft.mode === "Only" ? listed : !listed;
}

/** Returns a draft where the rule target's effective availability equals `enabled`. */
export function setRuleTargetEnabled(draft: AvailabilityDraft, list: "activityTypes" | "sets", key: string, enabled: boolean): AvailabilityDraft {
  const listed = draft.mode === "Only" ? enabled : !enabled;
  if (draft[list].includes(key) === listed) return draft;
  return { ...draft, [list]: toggleListValue(draft[list], key) };
}

export interface EffectiveActivityAvailability {
  /** Whether the draft leaves the activity available to new workflows. */
  enabled: boolean;
  /** What prevents the row switch from changing the state, if anything. */
  lockedBy: "host-baseline" | "set-rule" | null;
  /** The selected set that governs the activity when lockedBy is "set-rule". */
  governingSet?: string;
}

/**
 * Resolves each activity's draft availability the way the backend rule expander does: sets
 * selected in the rules contribute their member activity types to the mode's exclusion/inclusion
 * list, and the host baseline overrides everything. A set-governed activity is locked because an
 * individual activity rule cannot override its set's contribution. Keyed by the rule key.
 */
export function computeEffectiveAvailability(
  entries: ActivityAvailabilityDiagnosticEntry[],
  sets: ActivityAvailabilitySetDiagnostic[],
  draft: AvailabilityDraft
): Map<string, EffectiveActivityAvailability> {
  const listedTypes = new Set(draft.activityTypes);
  const selectedSets = new Set(draft.sets);
  const governingSetByKey = new Map<string, string>();
  for (const set of sets) {
    if (!selectedSets.has(set.name)) continue;
    for (const key of set.activityTypeKeys ?? []) {
      if (!governingSetByKey.has(key)) governingSetByKey.set(key, set.name);
    }
  }

  const result = new Map<string, EffectiveActivityAvailability>();
  for (const entry of entries) {
    const key = getAvailabilityRuleKey(entry);
    if (isHostBlockedEntry(entry)) {
      result.set(key, { enabled: false, lockedBy: "host-baseline" });
      continue;
    }
    const governingSet = governingSetByKey.get(key);
    const listed = listedTypes.has(key) || governingSet !== undefined;
    const enabled = draft.mode === "Only" ? listed : !listed;
    result.set(key, governingSet ? { enabled, lockedBy: "set-rule", governingSet } : { enabled, lockedBy: null });
  }
  return result;
}

export function isAvailabilityDraftDirty(draft: AvailabilityDraft, settings: ActivityAvailabilitySettings | null | undefined): boolean {
  const baseline = createAvailabilityDraft(settings);
  const normalize = (values: string[]) => [...values].sort((left, right) => left.localeCompare(right)).join("\n");
  return draft.mode !== baseline.mode
    || normalize(draft.activityTypes) !== normalize(baseline.activityTypes)
    || normalize(draft.sets) !== normalize(baseline.sets);
}

export function toggleListValue(values: string[], value: string): string[] {
  return values.includes(value)
    ? values.filter(item => item !== value)
    : [...values, value].sort((left, right) => left.localeCompare(right));
}

export function getActivityDisplayName(activity: { displayName?: string | null; activityTypeKey?: string | null } | null | undefined): string {
  const displayName = activity?.displayName?.trim();
  if (displayName) return displayName;
  const typeName = getActivityTypeName(activity?.activityTypeKey);
  return humanizeIdentifier(typeName) || activity?.activityTypeKey || "Activity";
}

export function getAvailabilityActivityName(activity: { displayName?: string | null; activityTypeKey?: string | null } | null | undefined): string {
  const displayName = activity?.displayName?.trim();
  if (displayName && displayName.toLowerCase() !== "activity") return displayName;
  const typeName = getActivityTypeName(activity?.activityTypeKey);
  return humanizeIdentifier(typeName) || displayName || activity?.activityTypeKey || "Activity";
}

export function getAvailabilityActivityTypeLabel(activity: { activityTypeKey?: string | null } | null | undefined): string {
  const segments = getActivityTypeSegments(activity?.activityTypeKey);
  if (segments.length === 0) return "";

  const typeName = segments[segments.length - 1];
  const parentName = segments[segments.length - 2];
  return typeName === "Activity" && parentName ? `${parentName}.${typeName}` : typeName;
}

export function getAvailabilityActivityDescription(activity: ActivityAvailabilityDiagnosticEntry): string {
  const description = activity.description?.trim();
  if (description) return description;

  const reason = activity.reason?.trim();
  if (!reason || !isUnavailableState(activity.state)) return "";
  return reason;
}

export function humanizeIdentifier(value: string): string {
  return value
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/\bHttp\b/g, "HTTP")
    .replace(/\bJson\b/g, "JSON")
    .replace(/\bJava Script\b/g, "JavaScript")
    .replace(/\bUrl\b/g, "URL")
    .replace(/\bXml\b/g, "XML")
    .trim();
}

function getActivityTypeName(activityTypeKey: string | null | undefined): string {
  const segments = getActivityTypeSegments(activityTypeKey);
  const typeName = segments[segments.length - 1] ?? "";
  return typeName === "Activity" && segments.length > 1 ? segments[segments.length - 2] : typeName;
}

function getActivityTypeSegments(activityTypeKey: string | null | undefined): string[] {
  return activityTypeKey?.split(".").filter(Boolean) ?? [];
}

/** A catalog input/output rendered in the availability details panel. */
export interface AvailabilityArgumentSummary {
  name: string;
  typeName: string;
  description: string;
}

/**
 * Summarizes catalog argument metadata for display. Catalog inputs/outputs are `unknown[]` on the
 * wire, so this leans on the shared tolerant readers from workflowProperties (casing aliases +
 * legacy typeInformation shapes) and skips anything that has no name.
 */
export function summarizeAvailabilityArguments(values: unknown[] | null | undefined): AvailabilityArgumentSummary[] {
  return (values ?? []).flatMap(value => {
    if (!isPlainRecord(value)) return [];
    const name = (readStringField(value, ["displayName", "DisplayName"]) || readStringField(value, argumentNameKeys)).trim();
    if (!name) return [];
    const typeName = (readArgumentType(value).alias || readStringField(value, ["typeName", "TypeName", "alias", "Alias"])).trim();
    return [{ name, typeName, description: readStringField(value, ["description", "Description"]).trim() }];
  });
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
