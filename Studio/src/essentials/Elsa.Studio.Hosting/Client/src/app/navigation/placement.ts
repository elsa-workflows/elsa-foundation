import {
  defaultStudioNavigationOrder,
  type StudioNavigationContribution,
  type StudioNavigationPlacement,
  type StudioNavigationRequirement,
  type StudioNavigationSection
} from "../../sdk";

export const navigationGroups = [
  { id: "build", label: "Build" },
  { id: "operate", label: "Operate" },
  { id: "integrations", label: "Integrations" }
] as const;
export type NavigationGroupId = typeof navigationGroups[number]["id"];
export const navigationGroupsStorageKey = "elsa-studio.ui.nav.groups";

const hostAdminPaths = new Set(["/modules", "/theme-builder", "/package-feeds", "/features"]);
const hostAdminIds = new Set(["modules", "theme-builder", "package-feeds", "feature-management"]);

export interface NavigationAccess {
  /** Loaded module ids and capabilities. When omitted, `requiresFeature` is not enforced. */
  enabledFeatures?: ReadonlySet<string> | readonly string[];
  roles?: readonly string[];
  permissions?: readonly string[];
  /** Authenticated users are held to role/permission gates; demo/anonymous is not. */
  authenticated?: boolean;
  /**
   * Host-control read (module-management). `admin` items are hidden without it.
   * `settings` items (Appearance) stay visible: they are per-browser, not host-control.
   * Anonymous/demo stays allowed: when sign-in is off the session is never authenticated, the
   * server accepts admin calls anonymously, and `useHostControlAccess` therefore reports
   * `canReadModuleManagement: true`. Only an authenticated user is held to the permission.
   */
  canReadAdmin?: boolean;
}

export function asRequirementList(value: StudioNavigationRequirement | undefined): string[] {
  if (!value) return [];
  return (typeof value === "string" ? [value] : [...value])
    .map(item => item.trim())
    .filter(Boolean);
}

export function getNavigationSection(item: Pick<StudioNavigationContribution, "id" | "path" | "section">): StudioNavigationSection {
  if (item.section) {
    return item.section;
  }

  if (hostAdminPaths.has(item.path) || hostAdminIds.has(item.id)) {
    return "settings";
  }

  return "workspace";
}

type NavigationPlacementFields = Pick<StudioNavigationContribution, "id" | "path" | "section" | "placement" | "parentId">;

/**
 * Resolves sidebar placement. Declared `placement` wins. Older `section: "settings"` and the host's
 * own admin paths stay under Administration so modules that have not declared placement keep working.
 * A child with no placement inherits its parent when `navigation` is supplied, so it does not vanish.
 * A child that declares a different placement is listed under that placement. Top-level items with
 * no placement are route-only; no catch-all section is created.
 */
export function getNavigationPlacement(item: NavigationPlacementFields): StudioNavigationPlacement | undefined {
  return resolveNavigationPlacement(item);
}

export function resolveNavigationPlacement(
  item: NavigationPlacementFields,
  navigation: readonly NavigationPlacementFields[] = []
): StudioNavigationPlacement | undefined {
  const visited = new Set<string>();
  let current: NavigationPlacementFields | undefined = item;
  while (current && !visited.has(current.id)) {
    visited.add(current.id);
    if (current.placement) return current.placement;
    if (getNavigationSection(current) === "settings") return "admin";
    const parentId: string | undefined = current.parentId;
    current = parentId ? navigation.find(candidate => candidate.id === parentId) : undefined;
  }
  return undefined;
}

export function getNavigationOrder(item: Pick<StudioNavigationContribution, "order">): number {
  return item.order ?? defaultStudioNavigationOrder;
}

export function isNavigationRequirementMet(
  required: StudioNavigationRequirement | undefined,
  granted: Iterable<string> | undefined
): boolean {
  const needed = asRequirementList(required);
  if (needed.length === 0) {
    return true;
  }

  const available = new Set([...(granted ?? [])].map(normalizeRequirement));
  return needed.every(value => available.has(normalizeRequirement(value)));
}

/** Pass `navigation` so unset children inherit placement before the admin/settings gate runs. */
export function isNavigationItemAccessible(
  item: StudioNavigationContribution,
  access: NavigationAccess = {},
  navigation: readonly StudioNavigationContribution[] = []
): boolean {
  const placement = resolveNavigationPlacement(item, navigation);
  if (placement === "admin" && access.canReadAdmin === false) {
    return false;
  }

  if (access.enabledFeatures && !isNavigationRequirementMet(item.requiresFeature, access.enabledFeatures)) {
    return false;
  }

  if (access.authenticated) {
    if (!isNavigationRequirementMet(item.requiresRole, access.roles)) {
      return false;
    }
    if (!isNavigationRequirementMet(item.requiresPermission, access.permissions)) {
      return false;
    }
  }

  return true;
}

/** Gates against the full tree, then drops children whose parent was filtered out. */
export function filterAccessibleNavigation(
  navigation: StudioNavigationContribution[],
  access: NavigationAccess = {}
): StudioNavigationContribution[] {
  const allowed = navigation.filter(item => isNavigationItemAccessible(item, access, navigation));
  const allowedIds = new Set(allowed.map(item => item.id));
  const originalById = new Map(navigation.map(item => [item.id, item]));
  const visible = allowed.filter(item => ancestorsAreAccessible(item, allowedIds, originalById));
  // View-only parents link to their first accessible view, never a hidden child's route.
  // Keep the default view's wider route ownership only while that view remains accessible.
  return visible.flatMap(item => {
    if (item.childDisplay !== "views" || item.viewLabel) return [item];
    const first = getChildNavigationItems(visible, item).sort(compareNavigationItems)[0];
    return first ? [{ ...item, path: first.path, activePathPrefix: first.path === item.path ? item.activePathPrefix : undefined }] : [];
  });
}

function ancestorsAreAccessible(
  item: StudioNavigationContribution,
  allowedIds: ReadonlySet<string>,
  originalById: ReadonlyMap<string, StudioNavigationContribution>
): boolean {
  let parentId = item.parentId;
  const seen = new Set<string>();
  while (parentId && originalById.has(parentId) && !seen.has(parentId)) {
    if (!allowedIds.has(parentId)) {
      return false;
    }
    seen.add(parentId);
    parentId = originalById.get(parentId)?.parentId;
  }
  return true;
}

export function getTopLevelNavigationItems(navigation: StudioNavigationContribution[], section: StudioNavigationSection) {
  const ids = new Set(navigation.map(item => item.id));
  return navigation.filter(item => getNavigationSection(item) === section && (!item.parentId || !ids.has(item.parentId)));
}

export function getTopLevelNavigationItemsByPlacement(
  navigation: StudioNavigationContribution[],
  placement: StudioNavigationPlacement
) {
  const ids = new Set(navigation.map(item => item.id));
  return navigation
    .filter(item => {
      if (resolveNavigationPlacement(item, navigation) !== placement) {
        return false;
      }
      const parent = item.parentId ? navigation.find(candidate => candidate.id === item.parentId) : undefined;
      if (!parent || !ids.has(parent.id)) {
        return true;
      }
      return resolveNavigationPlacement(item, navigation) !== resolveNavigationPlacement(parent, navigation);
    })
    .sort(compareNavigationItems);
}

/** Children that share the parent's resolved placement. Differently placed children are top-level instead. */
export function getChildNavigationItems(
  navigation: readonly StudioNavigationContribution[],
  parent: NavigationPlacementFields
) {
  const parentPlacement = resolveNavigationPlacement(parent, navigation);
  return navigation.filter(item =>
    item.parentId === parent.id && resolveNavigationPlacement(item, navigation) === parentPlacement
  );
}

export function compareNavigationItems(
  a: Pick<StudioNavigationContribution, "id" | "label" | "order">,
  b: Pick<StudioNavigationContribution, "id" | "label" | "order">
) {
  const orderComparison = getNavigationOrder(a) - getNavigationOrder(b);
  if (orderComparison !== 0) {
    return orderComparison;
  }

  return a.label.localeCompare(b.label) || a.id.localeCompare(b.id);
}

export function readNavigationGroups(storage: PreferenceStorage | undefined = defaultStorage()): Partial<Record<NavigationGroupId, boolean>> {
  return safely(() => {
    const stored: unknown = JSON.parse(storage?.getItem(navigationGroupsStorageKey) ?? "{}");
    if (!stored || typeof stored !== "object") return {};
    return Object.fromEntries(navigationGroups.flatMap(({ id }) => {
      const value = (stored as Record<string, unknown>)[id];
      return typeof value === "boolean" ? [[id, value]] : [];
    }));
  }, {});
}

export function writeNavigationGroups(groups: Partial<Record<NavigationGroupId, boolean>>, storage: PreferenceStorage | undefined = defaultStorage()) {
  safely(() => Object.keys(groups).length ? storage?.setItem(navigationGroupsStorageKey, JSON.stringify(groups)) : storage?.removeItem(navigationGroupsStorageKey), undefined);
}

interface PreferenceStorage {
  getItem(key: string): string | null;
  setItem(key: string, value: string): void;
  removeItem(key: string): void;
}

function defaultStorage(): PreferenceStorage | undefined {
  try {
    return typeof window === "undefined" ? undefined : window.localStorage;
  } catch {
    return undefined;
  }
}

function safely<T>(action: () => T, fallback: T): T {
  try {
    return action();
  } catch {
    return fallback;
  }
}

function normalizeRequirement(value: string) {
  return value.trim().toLowerCase();
}
