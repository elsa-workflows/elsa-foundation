import type { Publication, PublicationIntent, PublicationPolicy, PublicationPreflight, PublicationSlot } from "../api/publishing";
import type { ActivityCatalogItem, ActivityNode, WorkflowDefinitionDetails, WorkflowDefinitionState, WorkflowDefinitionVersionDetails, WorkflowDraft } from "../workflowTypes";
import { getChildSlots, readStructureDesignFacet, type ActivityCatalogLookup, type ChildSlot } from "../workflowAdapter";

export type PublicationReviewPhase = "review" | "publishing" | "validationBlocked" | "savedFailure" | "partialFailure" | "success";
export type PublicationProgressStep = "saving" | "promoting" | "preflight" | "publishing";

export interface PublicationChangeCount {
  added: number;
  changed: number;
  removed: number;
}

export interface PublicationChangeSummary {
  activities: PublicationChangeCount;
  inputs: PublicationChangeCount;
  outputs: PublicationChangeCount;
  triggers: PublicationChangeCount;
}

export interface PublicationReviewState {
  phase: PublicationReviewPhase;
  progressStep?: PublicationProgressStep;
  draftSnapshot: WorkflowDraft;
  currentVersion: string;
  proposedVersion: string;
  unsavedChangesIncluded: true;
  executableStatus: "ready" | "blocked";
  validationErrors: string[];
  changes: PublicationChangeSummary;
  policy: PublicationPolicy;
  slots: PublicationSlot[];
  slotVersions: Record<string, WorkflowDefinitionVersionDetails>;
  catalog: ActivityCatalogItem[];
  triggerActivityVersionIds: string[];
  intent: PublicationIntent;
  preflight?: PublicationPreflight;
  promotedVersionId?: string;
  savedDraft?: WorkflowDraft;
  failureMessage?: string;
  published?: Publication;
}

export function createPublicationReview(input: {
  draft: WorkflowDraft;
  details: WorkflowDefinitionDetails | null;
  slotVersions: Record<string, WorkflowDefinitionVersionDetails>;
  policy: PublicationPolicy | null;
  slots: PublicationSlot[];
  catalog: ActivityCatalogItem[];
}): PublicationReviewState {
  const policy = input.policy ?? {
    defaultAction: "replace",
    defaultSlotName: "default",
    source: "host"
  };
  const activeSlot = input.slots.find(slot => slot.slotName === policy.defaultSlotName);
  const currentVersion = activeSlot?.publication?.artifactVersion
    ?? activeSlot?.publication?.versionId
    ?? input.details?.definition.latestVersion
    ?? "None";
  const draftSnapshot = structuredClone(input.draft);
  const validationErrors = draftSnapshot.validationErrors
    .map(error => error.message?.trim())
    .filter((message): message is string => Boolean(message));
  if (!draftSnapshot.state.rootActivity) validationErrors.unshift("Workflow has no root activity.");
  const triggerActivityVersionIds = input.catalog
    .filter(activity => activity.executionType.toLowerCase() === "trigger")
    .map(activity => activity.activityVersionId);
  const initialSlotName = policy.defaultAction === "replace" ? policy.defaultSlotName : "";

  return {
    phase: validationErrors.length ? "validationBlocked" : "review",
    draftSnapshot,
    currentVersion,
    proposedVersion: nextVersionLabel(input.details?.definition.latestVersion ?? currentVersion),
    unsavedChangesIncluded: true,
    executableStatus: validationErrors.length ? "blocked" : "ready",
    validationErrors,
    changes: summarizePublicationChanges(
      input.slotVersions[initialSlotName]?.state ?? null,
      draftSnapshot.state,
      new Set(triggerActivityVersionIds),
      input.catalog),
    policy,
    slots: input.slots,
    slotVersions: input.slotVersions,
    catalog: input.catalog,
    triggerActivityVersionIds,
    intent: policy.defaultAction === "requireExplicitSlot"
      ? { action: "sideBySide", slotName: "" }
      : { action: "replace", slotName: policy.defaultSlotName }
  };
}

export function summarizePublicationChanges(
  before: WorkflowDefinitionState | null,
  after: WorkflowDefinitionState,
  triggerActivityVersionIds: ReadonlySet<string> = new Set(),
  catalog: ActivityCatalogLookup = undefined
): PublicationChangeSummary {
  const beforeActivities = collectActivities(before?.rootActivity, catalog);
  const afterActivities = collectActivities(after.rootActivity, catalog);
  return {
    activities: compareCollections(beforeActivities, afterActivities, activity => activityFingerprint(activity, catalog)),
    inputs: compareCollections(before?.inputs ?? [], after.inputs ?? []),
    outputs: compareCollections(before?.outputs ?? [], after.outputs ?? []),
    triggers: compareCollections(
      beforeActivities.filter(activity => isTriggerActivity(activity, triggerActivityVersionIds)),
      afterActivities.filter(activity => isTriggerActivity(activity, triggerActivityVersionIds)),
      activity => activityFingerprint(activity, catalog))
  };
}

export function publicationIntentFor(
  review: PublicationReviewState,
  action: "replace" | "sideBySide",
  slotName: string
): PublicationIntent {
  const normalizedSlot = slotName.trim();
  const current = review.slots.find(slot => slot.slotName === normalizedSlot)?.publication;
  return {
    action,
    slotName: normalizedSlot || (action === "replace" ? review.policy.defaultSlotName : ""),
    ...(current?.publicationId ? { expectedPublicationId: current.publicationId } : {})
  };
}

export function publicationPreflightMatchesIntent(
  preflight: PublicationReviewState["preflight"],
  intent: PublicationIntent
): preflight is PublicationPreflight {
  return Boolean(preflight
    && preflight.resolvedAction === intent.action
    && preflight.slotName === intent.slotName);
}

export function publicationChangesFor(review: PublicationReviewState, slotName: string) {
  return summarizePublicationChanges(
    review.slotVersions[slotName]?.state ?? null,
    review.draftSnapshot.state,
    new Set(review.triggerActivityVersionIds),
    review.catalog);
}

function nextVersionLabel(value: string) {
  const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(value);
  return match ? `${Number(match[1]) + 1}.0.0` : "Next promoted version";
}

function collectActivities(root: ActivityNode | null | undefined, catalog: ActivityCatalogLookup) {
  const activities: ActivityNode[] = [];
  const visited = new Set<object>();
  const visit = (activity: ActivityNode) => {
    if (visited.has(activity)) return;
    visited.add(activity);
    activities.push(activity);
    for (const slot of publicationChildSlots(activity, catalog)) {
      for (const child of slot.activities) visit(child);
    }
  };
  if (root) visit(root);
  return activities;
}

function compareCollections<T>(
  before: T[],
  after: T[],
  serialize: (value: T) => string = stableJson
): PublicationChangeCount {
  const beforeByKey = indexValues(before, serialize);
  const afterByKey = indexValues(after, serialize);
  let added = 0;
  let changed = 0;
  let removed = 0;
  for (const [key, value] of afterByKey) {
    const previous = beforeByKey.get(key);
    if (previous == null) added += 1;
    else if (previous !== value) changed += 1;
  }
  for (const key of beforeByKey.keys()) if (!afterByKey.has(key)) removed += 1;
  return { added, changed, removed };
}

function indexValues<T>(values: T[], serialize: (value: T) => string) {
  return new Map(values.map((value, index) => [valueKey(value, index), serialize(value)]));
}

function isTriggerActivity(value: unknown, triggerActivityVersionIds: ReadonlySet<string>) {
  return Boolean(value && typeof value === "object"
    && "activityVersionId" in value
    && typeof value.activityVersionId === "string"
    && triggerActivityVersionIds.has(value.activityVersionId));
}

function activityFingerprint(activity: ActivityNode, catalog: ActivityCatalogLookup) {
  const ownedState = structuredClone(activity);
  for (const slot of publicationChildSlots(activity, catalog)) clearChildSlot(ownedState, slot);
  return stableJson(ownedState);
}

function publicationChildSlots(activity: ActivityNode, catalog: ActivityCatalogLookup): ChildSlot[] {
  const structure = activity.structure;
  if (!structure) return [];
  const catalogItem = resolveCatalogItem(activity, catalog);
  const facet = readStructureDesignFacet(catalogItem);
  if (facet?.kind === structure.kind) return getChildSlots(activity, catalogItem);

  // Flowchart and Sequence used `activities` before structure facets became part of the catalog.
  // Preserve that explicit legacy child contract without scanning arbitrary payload properties for
  // objects that merely happen to look like activities.
  if (!Array.isArray(structure.payload.activities)) return [];
  return getChildSlots(activity).filter(slot => slot.property === "activities");
}

function resolveCatalogItem(activity: ActivityNode, catalog: ActivityCatalogLookup) {
  if (!catalog) return undefined;
  if (catalog instanceof Map) return catalog.get(activity.activityVersionId);
  if (Array.isArray(catalog)) return catalog.find(item => item.activityVersionId === activity.activityVersionId);
  return catalog.activityVersionId === activity.activityVersionId ? catalog : undefined;
}

function clearChildSlot(activity: ActivityNode, slot: ChildSlot) {
  const payload = activity.structure?.payload;
  if (!payload) return;
  const emptyValue = slot.cardinality === "many" ? [] : null;
  if (!slot.collectionProperty || !slot.childProperty || slot.collectionIndex === undefined) {
    payload[slot.property] = emptyValue;
    return;
  }

  const collection = payload[slot.collectionProperty];
  const item = Array.isArray(collection) ? collection[slot.collectionIndex] : null;
  if (item && typeof item === "object") (item as Record<string, unknown>)[slot.childProperty] = emptyValue;
}

function valueKey(value: unknown, index: number) {
  if (value && typeof value === "object") {
    const record = value as Record<string, unknown>;
    for (const key of ["nodeId", "id", "name"]) {
      if (typeof record[key] === "string" && record[key]) return `${key}:${record[key]}`;
    }
  }
  return `index:${index}`;
}

function stableJson(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(stableJson).join(",")}]`;
  if (!value || typeof value !== "object") return JSON.stringify(value);
  return `{${Object.entries(value)
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, child]) => `${JSON.stringify(key)}:${stableJson(child)}`)
    .join(",")}}`;
}
