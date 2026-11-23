import type {
  AuthSession,
  ElsaStudioModuleApi,
  StudioHostId,
  StudioSettingDescriptor
} from "@elsa-workflows/studio-sdk";

export interface FeatureCatalogResponse {
  revision: string;
  features: FeatureCatalogItem[];
}

export interface FeatureApplyResult {
  catalog: FeatureCatalogResponse;
  featureDescriptorCount: number;
  reloadedShellCount: number;
}

interface FeatureCatalogItem {
  runtimeAvailable?: boolean;
  availability?: {status:string;reasons:{family:string;version:string;kind:string;reason:string}[]} | null;
  id: string;
  displayName: string;
  description?: string | null;
  categories: string[];
  sourceKind: string;
  packageId?: string | null;
  packageVersion?: string | null;
  enabled: boolean;
  configuration: Record<string, unknown>;
  advanced: boolean;
  experimental: boolean;
  manifestPath?: string | null;
  manifestHash?: string | null;
  readError?: string | null;
  settings: StudioSettingDescriptor[];
  dependencies: FeatureDependency[];
}

// A feature dependency carries whether it is optional. Optional dependencies are surfaced for display but are
// excluded from the hard enable/disable cascade so they are never force-toggled like mandatory dependencies.
interface FeatureDependency {
  id: string;
  optional: boolean;
}

export interface DraftFeature extends FeatureCatalogItem {
  configuration: Record<string, unknown>;
}

export interface CategoryFilterItem {
  id: string;
  label: string;
  count: number;
}

interface SettingGroupItem {
  id: string;
  label: string;
  settings: StudioSettingDescriptor[];
}

interface FeatureToggleRoot {
  featureId: string;
  enabled: boolean;
}

interface FeatureStateCause {
  enabled: boolean;
  rootIds: string[];
}

interface FeatureSettingChange {
  name: string;
  displayName: string;
  before: string;
  after: string;
  restartRequired?: boolean;
}

interface FeatureImpactReason {
  rootId: string;
  path: string[];
  historical: boolean;
}

export interface FeatureImpactEntry {
  baseline: DraftFeature;
  desired: DraftFeature;
  enabledChanged: boolean;
  direct: boolean;
  automatic: boolean;
  settingChanges: FeatureSettingChange[];
  reasons: FeatureImpactReason[];
}

export type FeatureHostId = StudioHostId;

type FeatureEndpointContext = Pick<ElsaStudioModuleApi["host"], "http">;

interface FeatureHostConfig {
  key: string;
  session?: AuthSession;
  id: FeatureHostId;
  label: string;
  runtime: string;
  context: FeatureEndpointContext;
  writable: boolean;
}

export interface FeatureHostState {
  catalog: FeatureCatalogResponse | null;
  draft: DraftFeature[];
  selectedId: string;
  checkedFeatureIds: Set<string>;
  selectedCategory: string;
  filterText: string;
  browseView: "overview" | "all" | "group";
  selectedCapability: string;
  featureStateFilter: "all" | "enabled" | "disabled" | "pending";
  includeAdvanced: boolean;
  navigationHistory: FeatureBrowseLocation[];
  loading: boolean;
  applying: boolean;
  status: string | null;
  error: string | null;
  enabledRoots: FeatureToggleRoot[];
  stateCauses: Map<string, FeatureStateCause>;
}

export interface FeatureBrowseLocation {
  browseView: FeatureHostState["browseView"];
  selectedCapability: string;
  selectedCategory: string;
  filterText: string;
  featureStateFilter: FeatureHostState["featureStateFilter"];
  includeAdvanced: boolean;
  selectedId: string;
}

export interface FeatureRelationshipLink {
  id: string;
  feature: DraftFeature | null;
  optional: boolean;
}

export interface FeatureRelationshipGroup {
  direct: FeatureRelationshipLink[];
  indirect: FeatureRelationshipLink[];
}

export interface FeatureRelationships {
  requires: FeatureRelationshipGroup;
  usedBy: FeatureRelationshipGroup;
}

export interface FeatureReviewSnapshot {
  targetKey: string;
  context: FeatureEndpointContext;
  hostId: FeatureHostId;
  catalog: FeatureCatalogResponse;
  catalogSignature: string | undefined;
  draftSignature: string | undefined;
  payload: ReturnType<typeof createApplyPayload>;
}

export const AllCategoriesId = "__all";

const UncategorizedId = "__uncategorized";

export const featureKeys = {
  all: ["features"] as const,
  catalog: (hostId: FeatureHostId) => [...featureKeys.all, hostId, "catalog"] as const
};

function projectEffectiveFeature(feature: Pick<DraftFeature, "id" | "enabled" | "configuration">) {
  return {
    id: feature.id,
    enabled: feature.enabled,
    configuration: feature.enabled ? feature.configuration : {}
  };
}

export function createApplyPayload(revision: string, features: DraftFeature[]) {
  return {
    revision,
    features: features.filter(feature => feature.sourceKind !== "manifest-error").map(projectEffectiveFeature)
  };
}

export function isDirty(current: FeatureCatalogResponse | null, draft: DraftFeature[]) {
  return current ? canonicalize(current.features) !== canonicalize(draft) : false;
}

export function isFeaturePending(baseline: DraftFeature | undefined, desired: DraftFeature) {
  return !!baseline && stableStringify(projectEffectiveFeature(baseline)) !== stableStringify(projectEffectiveFeature(desired));
}

export function featureIndexById(features: DraftFeature[]) {
  return new Map(features.map(feature => [normalizeType(feature.id), feature]));
}

// Direct dependants, indexed by the normalized feature id they require. Used for
// the inspector's "Required by" display and to resolve the disable cascade below.
export function buildDependentsIndex(features: DraftFeature[]): Map<string, string[]> {
  const index = new Map<string, string[]>();

  for (const feature of features) {
    for (const dependency of feature.dependencies) {
      // Optional dependencies form no hard dependant edge: disabling the dependency must not force-disable a feature
      // that only optionally uses it, and it should not appear under the "Required by" list.
      if (dependency.optional)
        continue;
      const key = normalizeType(dependency.id);
      const existing = index.get(key);
      if (existing) {
        existing.push(feature.id);
      } else {
        index.set(key, [feature.id]);
      }
    }
  }

  return index;
}

export function getFeatureRelationships(feature: DraftFeature, features: DraftFeature[]): FeatureRelationships {
  const byId = featureIndexById(features);
  const dependencies = (current: DraftFeature) => current.dependencies.map(dependency => ({
    id: dependency.id,
    feature: byId.get(normalizeType(dependency.id)) ?? null,
    optional: dependency.optional
  }));
  const reverse = new Map<string, FeatureRelationshipLink[]>();
  for (const candidate of features) {
    for (const dependency of candidate.dependencies) {
      const key = normalizeType(dependency.id);
      const links = reverse.get(key) ?? [];
      links.push({ id: candidate.id, feature: candidate, optional: dependency.optional });
      reverse.set(key, links);
    }
  }

  function collectIndirect(initial: FeatureRelationshipLink[], next: (current: DraftFeature) => FeatureRelationshipLink[]) {
    const rootId = normalizeType(feature.id);
    const directIds = new Set(initial.map(link => normalizeType(link.id)));
    const visited = new Set([rootId, ...directIds]);
    const indirect: FeatureRelationshipLink[] = [];
    const pending = initial.flatMap(link => link.feature ? [link.feature] : []);
    for (let cursor = 0; cursor < pending.length; cursor++) {
      for (const link of next(pending[cursor])) {
        const key = normalizeType(link.id);
        if (visited.has(key) || !link.feature) continue;
        visited.add(key);
        indirect.push(link);
        pending.push(link.feature);
      }
    }
    return indirect;
  }

  const directRequires = dependencies(feature);
  const directUsedBy = reverse.get(normalizeType(feature.id)) ?? [];
  return {
    requires: {
      direct: directRequires,
      indirect: collectIndirect(directRequires, dependencies)
    },
    usedBy: {
      direct: directUsedBy,
      indirect: collectIndirect(directUsedBy, current => reverse.get(normalizeType(current.id)) ?? [])
    }
  };
}

// Walks the dependency graph from `root`, adding every reachable feature id to
// `affected`: enabling follows the dependencies a feature requires; disabling
// follows the dependants that require it (via the shared reverse index).
function collectCascade(
  root: DraftFeature,
  enabled: boolean,
  byId: Map<string, DraftFeature>,
  dependents: Map<string, string[]>,
  affected: Set<string>) {
  const stack = [root];
  affected.add(root.id);

  while (stack.length > 0) {
    const current = stack.pop()!;
    const relatedIds = enabled
      ? current.dependencies.filter(dependency => !dependency.optional).map(dependency => dependency.id)
      : dependents.get(normalizeType(current.id)) ?? [];

    for (const relatedId of relatedIds) {
      const feature = byId.get(normalizeType(relatedId));
      if (feature && !affected.has(feature.id)) {
        affected.add(feature.id);
        stack.push(feature);
      }
    }
  }
}

// Shared cascade resolver for one or many seed ids. Builds the feature index (and,
// when disabling, the reverse dependants index) once, then folds every seed id
// through the graph so bulk toggles never rebuild those indexes per feature.
function collectToggleTargets(features: DraftFeature[], ids: Iterable<string>, enabled: boolean): Set<string> {
  const byId = featureIndexById(features);
  const dependents = enabled ? new Map<string, string[]>() : buildDependentsIndex(features);
  const affected = new Set<string>();

  for (const id of ids) {
    const root = byId.get(normalizeType(id));
    if (root) {
      collectCascade(root, enabled, byId, dependents, affected);
    }
  }

  return affected;
}

// Resolves the set of feature ids affected when toggling `id` to `enabled`:
// enabling pulls in every transitive dependency, disabling pushes out every
// transitive dependant, so the desired state never leaves a broken dependency edge.
export function computeFeatureCascade(features: DraftFeature[], id: string, enabled: boolean): Set<string> {
  return collectToggleTargets(features, [id], enabled);
}

// Applies `enabled` to every seed id and its cascade in a single pass over the draft.
function applyFeatureTogglesWithChanges(features: DraftFeature[], ids: Iterable<string>, enabled: boolean) {
  const rootIds = Array.from(ids);
  const affected = collectToggleTargets(features, rootIds, enabled);
  if (affected.size === 0) {
    return { draft: features, changedIds: [] as string[], rootIds };
  }

  const changedIds: string[] = [];
  const draft = features.map(feature => {
    if (affected.has(feature.id) && feature.enabled !== enabled) {
      changedIds.push(feature.id);
      return { ...feature, enabled };
    }

    return feature;
  });

  return { draft, changedIds, rootIds };
}

function applyFeatureToggles(features: DraftFeature[], ids: Iterable<string>, enabled: boolean): DraftFeature[] {
  return applyFeatureTogglesWithChanges(features, ids, enabled).draft;
}

// Applies a toggle to the draft, cascading to dependencies/dependants.
export function applyFeatureToggle(features: DraftFeature[], id: string, enabled: boolean): DraftFeature[] {
  return applyFeatureToggles(features, [id], enabled);
}

export function getFeatureHosts(api: ElsaStudioModuleApi): FeatureHostConfig[] {
  return [
    {
      id: "studio",
      key: JSON.stringify(["studio",api.host.baseUrl,api.hostSession?.subject,api.hostSession?.tenantId]),
      session: api.hostSession,
      label: "Studio",
      runtime: "Elsa.Studio.Web",
      context: api.host,
      writable: true
    },
    {
      id: "server",
      key: api.backendSession?.key ?? JSON.stringify(["server",api.backend.baseUrl,api.runtime?.identity]),
      session: api.backendSession?.identity,
      label: api.backendSession ? `Server · ${api.backendSession.profile.name}` : "Server",
      runtime: "Elsa.Server",
      context: api.backend,
      writable: true
    }
  ];
}

export function createInitialHostState(): FeatureHostState {
  return {
    catalog: null,
    draft: [],
    selectedId: "",
    checkedFeatureIds: new Set(),
    selectedCategory: AllCategoriesId,
    filterText: "",
    browseView: "overview",
    selectedCapability: "",
    featureStateFilter: "all",
    includeAdvanced: true,
    navigationHistory: [],
    loading: true,
    applying: false,
    status: null,
    error: null,
    enabledRoots: [],
    stateCauses: new Map()
  };
}

export function stageFeatureToggle(state: FeatureHostState, rootIds: Iterable<string>, enabled: boolean): FeatureHostState {
  const result = applyFeatureTogglesWithChanges(state.draft, rootIds, enabled);
  if (!state.catalog) {
    return { ...state, draft: result.draft };
  }

  const baseline = featureIndexById(state.catalog.features.map(toDraftFeature));
  const desired = featureIndexById(result.draft);
  const stateDiffersFromBaseline = (featureId: string) => {
    const initial = baseline.get(normalizeType(featureId));
    const current = desired.get(normalizeType(featureId));
    return initial && current ? initial.enabled !== current.enabled : false;
  };

  const enabledRoots = state.enabledRoots.filter(root => {
    const initial = baseline.get(normalizeType(root.featureId));
    const current = desired.get(normalizeType(root.featureId));
    return initial && current && current.enabled !== initial.enabled && current.enabled === root.enabled;
  });

  for (const rootId of result.rootIds) {
    const current = desired.get(normalizeType(rootId));
    if (!current || !stateDiffersFromBaseline(current.id) || current.enabled !== enabled) {
      continue;
    }

    const existing = enabledRoots.findIndex(root => normalizeType(root.featureId) === normalizeType(current.id));
    const root = { featureId: current.id, enabled };
    if (existing >= 0) {
      enabledRoots[existing] = root;
    } else {
      enabledRoots.push(root);
    }
  }

  const stateCauses = new Map(state.stateCauses);
  for (const initial of baseline.values()) {
    const current = desired.get(normalizeType(initial.id));
    if (!current || current.enabled === initial.enabled) {
      stateCauses.delete(initial.id);
    }
  }

  for (const featureId of result.changedIds) {
    const current = desired.get(normalizeType(featureId));
    if (current && stateDiffersFromBaseline(current.id)) {
      stateCauses.set(current.id, { enabled, rootIds: result.rootIds });
    }
  }

  for (const rootId of result.rootIds) {
    const current = desired.get(normalizeType(rootId));
    if (current && stateDiffersFromBaseline(current.id)) {
      stateCauses.set(current.id, { enabled: current.enabled, rootIds: [current.id] });
    }
  }

  return { ...state, draft: result.draft, enabledRoots, stateCauses };
}

export function getFeatureImpactEntries(
  catalog: FeatureCatalogResponse | null,
  draft: DraftFeature[],
  roots: FeatureToggleRoot[],
  causes: ReadonlyMap<string, FeatureStateCause>): FeatureImpactEntry[] {
  if (!catalog) {
    return [];
  }

  const baseline = catalog.features.map(toDraftFeature);
  const baselineById = featureIndexById(baseline);
  const desiredById = featureIndexById(draft);
  const dependents = buildDependentsIndex(baseline);

  return baseline.flatMap(initial => {
    const desired = desiredById.get(normalizeType(initial.id));
    if (!desired) {
      return [];
    }

    const enabledChanged = initial.enabled !== desired.enabled;
    const settingChanges = getSettingChanges(initial, desired);
    if (!enabledChanged && settingChanges.length === 0) {
      return [];
    }

    const directRoot = roots.some(root => normalizeType(root.featureId) === normalizeType(desired.id) && root.enabled === desired.enabled);
    const directSettingChange = desired.enabled
      && stableStringify(initial.configuration) !== stableStringify(desired.configuration);
    const currentReasons = enabledChanged
      ? roots.flatMap(root => {
        if (root.enabled !== desired.enabled || normalizeType(root.featureId) === normalizeType(desired.id)) {
          return [];
        }

        const path = findRequiredPath(baselineById, root.featureId, desired.id, root.enabled, dependents);
        return path ? [{ rootId: root.featureId, path, historical: false }] : [];
      })
      : [];
    const cause = causes.get(desired.id);
    const historicalReasons = enabledChanged && !directRoot && currentReasons.length === 0 && cause?.enabled === desired.enabled
      ? cause.rootIds.flatMap(rootId => {
        if (roots.some(root => normalizeType(root.featureId) === normalizeType(rootId) && root.enabled === cause.enabled)) {
          return [];
        }

        const path = findRequiredPath(baselineById, rootId, desired.id, cause.enabled, dependents);
        return path ? [{ rootId, path, historical: true }] : [];
      })
      : [];
    const automatic = enabledChanged && (!directRoot || currentReasons.length > 0 || historicalReasons.length > 0);

    return [{
      baseline: initial,
      desired,
      enabledChanged,
      direct: directRoot || directSettingChange,
      automatic,
      settingChanges,
      reasons: currentReasons.length > 0 ? currentReasons : historicalReasons
    }];
  });
}

function getSettingChanges(baseline: DraftFeature, desired: DraftFeature): FeatureSettingChange[] {
  const descriptors = new Map([...baseline.settings, ...desired.settings].map(setting => [setting.name, setting]));
  const baselineConfiguration = projectEffectiveFeature(baseline).configuration;
  const desiredConfiguration = projectEffectiveFeature(desired).configuration;
  const names = new Set([...Object.keys(baselineConfiguration), ...Object.keys(desiredConfiguration)]);

  return Array.from(names).flatMap(name => {
    const beforePresent = Object.prototype.hasOwnProperty.call(baselineConfiguration, name)
      && !isOmittedJsonObjectMember(baselineConfiguration[name]);
    const afterPresent = Object.prototype.hasOwnProperty.call(desiredConfiguration, name)
      && !isOmittedJsonObjectMember(desiredConfiguration[name]);
    const before = baselineConfiguration[name];
    const after = desiredConfiguration[name];
    if (beforePresent === afterPresent && stableStringify(before) === stableStringify(after)) {
      return [];
    }

    const setting = descriptors.get(name);
    return [{
      name,
      displayName: setting?.displayName || name,
      before: formatImpactSettingValue(setting, beforePresent, before),
      after: formatImpactSettingValue(setting, afterPresent, after),
      restartRequired: setting?.restartRequired
    }];
  });
}

function formatImpactSettingValue(setting: StudioSettingDescriptor | undefined, present: boolean, value: unknown) {
  if (!present) {
    return "Not set";
  }

  return setting ? formatSettingPreview(setting, value) : "Changed";
}

export function stableStringify(value: unknown): string | undefined {
  return JSON.stringify(value, (_key, item) => {
    if (!item || typeof item !== "object" || Array.isArray(item)) {
      return item;
    }

    const entries = Object.entries(item).sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0);
    return Object.fromEntries(entries);
  });
}

function isOmittedJsonObjectMember(value: unknown) {
  return value === undefined || typeof value === "function" || typeof value === "symbol";
}

function findRequiredPath(
  byId: Map<string, DraftFeature>,
  rootId: string,
  targetId: string,
  enabled: boolean,
  dependents: Map<string, string[]>): string[] | null {
  const root = byId.get(normalizeType(rootId));
  if (!root || !byId.has(normalizeType(targetId))) {
    return null;
  }

  if (normalizeType(root.id) === normalizeType(targetId)) {
    return [root.id];
  }

  const pending: Array<{ feature: DraftFeature; path: string[] }> = [{ feature: root, path: [root.id] }];
  const visited = new Set([normalizeType(root.id)]);
  for (let cursor = 0; cursor < pending.length; cursor++) {
    const current = pending[cursor];
    const relatedIds = enabled
      ? current.feature.dependencies.filter(dependency => !dependency.optional).map(dependency => dependency.id)
      : dependents.get(normalizeType(current.feature.id)) ?? [];

    for (const relatedId of relatedIds) {
      const related = byId.get(normalizeType(relatedId));
      if (!related || visited.has(normalizeType(related.id))) {
        continue;
      }

      const path = [...current.path, related.id];
      if (normalizeType(related.id) === normalizeType(targetId)) {
        return path;
      }

      visited.add(normalizeType(related.id));
      pending.push({ feature: related, path });
    }
  }

  return null;
}

export function getReloadImplication(entry: FeatureImpactEntry) {
  const restartRequired = entry.settingChanges.filter(setting => setting.restartRequired === true);
  const uncertainSettings = entry.settingChanges.some(setting => setting.restartRequired == null);

  if (restartRequired.length > 0) {
    const names = restartRequired.map(setting => setting.displayName).join(", ");
    return `Restart required for ${names}${uncertainSettings || entry.enabledChanged ? "; other reload impact is unknown." : "."}`;
  }

  if (!entry.enabledChanged && entry.settingChanges.length > 0 && entry.settingChanges.every(setting => setting.restartRequired === false)) {
    return `No restart required for ${entry.settingChanges.map(setting => setting.displayName).join(", ")}.`;
  }

  return "Unknown.";
}

// Coerce the wire dependency list into FeatureDependency objects. Tolerates a legacy `string[]` payload (treated as
// mandatory) so an older backend still cascades correctly.
function normalizeDependencies(raw: unknown): FeatureDependency[] {
  if (!Array.isArray(raw))
    return [];

  const dependencies: FeatureDependency[] = [];
  for (const entry of raw) {
    if (typeof entry === "string") {
      dependencies.push({ id: entry, optional: false });
    } else if (entry && typeof entry === "object" && typeof (entry as { id?: unknown }).id === "string") {
      dependencies.push({ id: (entry as { id: string }).id, optional: (entry as { optional?: unknown }).optional === true });
    }
  }

  return dependencies;
}

export function toDraftFeature(feature: FeatureCatalogItem): DraftFeature {
  return {
    ...feature,
    dependencies: normalizeDependencies(feature.dependencies),
    configuration: feature.configuration && typeof feature.configuration === "object" && !Array.isArray(feature.configuration)
      ? { ...feature.configuration }
      : {}
  };
}

export function getSettingValue(feature: DraftFeature, setting: StudioSettingDescriptor) {
  return Object.prototype.hasOwnProperty.call(feature.configuration, setting.name)
    ? feature.configuration[setting.name]
    : setting.defaultValue ?? defaultSettingValue(setting);
}

export function getFeatureSettingValues(feature: DraftFeature) {
  return Object.fromEntries(feature.settings.map(setting => [setting.name, getSettingValue(feature, setting)]));
}

export function isBooleanSetting(setting: StudioSettingDescriptor) {
  return normalizeType(setting.jsonType) === "boolean";
}

export function formatSettingPreview(setting: StudioSettingDescriptor, value: unknown) {
  if (setting.secret || setting.sensitive) {
    return value == null || value === "" ? "Not set" : "********";
  }

  if (normalizeType(setting.jsonType) === "boolean") {
    return value ? "Enabled" : "Disabled";
  }

  if (value == null || value === "") {
    return "Not set";
  }

  const text = typeof value === "object" ? JSON.stringify(value) : String(value);
  return text.length > 96 ? `${text.slice(0, 93)}...` : text;
}

function defaultSettingValue(setting: StudioSettingDescriptor) {
  const jsonType = normalizeType(setting.jsonType);
  if (jsonType === "boolean") return false;
  if (jsonType === "integer" || jsonType === "number") return 0;
  if (jsonType === "array") return [];
  if (jsonType === "object") return {};
  return "";
}

export function getCategoryFilters(features: DraftFeature[]): CategoryFilterItem[] {
  const counts = new Map<string, number>();

  for (const feature of features) {
    const categories = getFeatureCategoryIds(feature);
    for (const category of categories) {
      counts.set(category, (counts.get(category) ?? 0) + 1);
    }
  }

  return [
    { id: AllCategoriesId, label: "All categories", count: features.length },
    ...Array.from(counts.entries())
      .sort(([a], [b]) => getCategoryLabel(a).localeCompare(getCategoryLabel(b)))
      .map(([id, count]) => ({ id, label: getCategoryLabel(id), count }))
  ];
}

export function filterFeaturesByCategory(features: DraftFeature[], category: string) {
  if (category === AllCategoriesId) {
    return features;
  }

  return features.filter(feature => getFeatureCategoryIds(feature).includes(category));
}

export function filterFeaturesByText(features: DraftFeature[], filterText: string) {
  const terms = filterText
    .trim()
    .toLowerCase()
    .split(/\s+/)
    .filter(Boolean);

  if (terms.length === 0) {
    return features;
  }

  return features.filter(feature => {
    const searchable = [
      feature.id,
      feature.displayName,
      feature.description,
      feature.sourceKind,
      feature.packageId,
      feature.packageVersion,
      ...feature.categories,
      ...feature.settings.flatMap(setting => [
        setting.name,
        setting.displayName,
        setting.description,
        setting.category,
        setting.group,
        setting.uiHint
      ])
    ]
      .filter((value): value is string => typeof value === "string")
      .join(" ")
      .toLowerCase();

    return terms.every(term => searchable.includes(term));
  });
}

function getFeatureCategoryIds(feature: DraftFeature) {
  return feature.categories.length > 0 ? feature.categories : [UncategorizedId];
}

function getCategoryLabel(category: string) {
  return category === UncategorizedId ? "Uncategorized" : category;
}

export function getSettingGroups(settings: StudioSettingDescriptor[]): SettingGroupItem[] {
  const groups = new Map<string, SettingGroupItem>();

  for (const setting of settings) {
    const id = setting.group || setting.category || "General";
    const existing = groups.get(id);
    if (existing) {
      existing.settings.push(setting);
    } else {
      groups.set(id, { id, label: id, settings: [setting] });
    }
  }

  return Array.from(groups.values());
}

export function normalizeType(value?: string | null) {
  return (value ?? "").trim().toLowerCase();
}

function canonicalize(features: FeatureCatalogItem[] | DraftFeature[]) {
  return stableStringify(features
    .map(projectEffectiveFeature)
    .sort((a, b) => a.id.localeCompare(b.id))) ?? "undefined";
}

export function getErrorMessage(error: unknown) {
  if (isHttpError(error)) {
    if (error.status === 400) {
      return `The feature configuration was rejected. ${error.message}`;
    }
  }

  return error instanceof Error ? error.message : String(error);
}

function isHttpError(error: unknown): error is { status: number; message: string } {
  return error instanceof Error && typeof (error as { status?: unknown }).status === "number";
}
