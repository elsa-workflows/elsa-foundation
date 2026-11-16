import { describe, expect, it } from "vitest";
import {
  filterAccessibleNavigation,
  getChildNavigationItems,
  getNavigationOrder,
  getNavigationPlacement,
  getTopLevelNavigationItemsByPlacement,
  isNavigationItemAccessible,
  resolveNavigationPlacement,
  navigationGroupsStorageKey,
  readNavigationGroups,
  writeNavigationGroups
} from "../app/navigation/placement";
import type { StudioNavigationContribution } from "../sdk";

const home: StudioNavigationContribution = { id: "home", label: "Home", path: "/dashboard", placement: "main", order: 10 };
const workflows: StudioNavigationContribution = { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main", order: 20 };
const weather: StudioNavigationContribution = { id: "weather", label: "Weather", path: "/weather" };
const modules: StudioNavigationContribution = { id: "modules", label: "Modules", path: "/modules" };
const diagnostics: StudioNavigationContribution = { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", placement: "build" };
const logs: StudioNavigationContribution = { id: "structured-logs", label: "Structured logs", path: "/diagnostics/structured-logs", parentId: "diagnostics" };
const tasks: StudioNavigationContribution = {
  id: "tasks",
  label: "Tasks",
  path: "/tasks",
  placement: "main",
  order: 40,
  requiresFeature: "user-tasks"
};
const adminOnly: StudioNavigationContribution = {
  id: "package-feeds",
  label: "Package feeds",
  path: "/package-feeds",
  placement: "admin",
  requiresRole: "administrator"
};

describe("navigation placement", () => {
  it("inherits through every ancestor and terminates cycles without inventing placement", () => {
    const parent: StudioNavigationContribution = { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", placement: "operate" };
    const child: StudioNavigationContribution = { id: "audit", label: "Audit", path: "/audit", parentId: parent.id };
    const grandchild: StudioNavigationContribution = { id: "recent", label: "Recent", path: "/audit/recent", parentId: child.id };
    expect(resolveNavigationPlacement(grandchild, [parent, child, grandchild])).toBe("operate");
    const cycle = [{ ...child, parentId: grandchild.id }, grandchild];
    expect(resolveNavigationPlacement(grandchild, cycle)).toBeUndefined();
  });
  it("keeps undeclared contributions unplaced and honours explicit placement", () => {
    expect(getNavigationPlacement(weather)).toBeUndefined();
    expect(getNavigationPlacement(home)).toBe("main");
    expect(getNavigationPlacement(modules)).toBe("admin");
    expect(getNavigationPlacement({ id: "tools", path: "/tools", section: "settings" })).toBe("admin");
    expect(getNavigationOrder(weather)).toBe(1000);
    expect(getNavigationOrder(home)).toBe(10);
  });

  it("hides feature-gated items when the feature is not enabled and keeps them when it is", () => {
    expect(isNavigationItemAccessible(tasks, { enabledFeatures: [] })).toBe(false);
    expect(isNavigationItemAccessible(tasks, { enabledFeatures: ["user-tasks"] })).toBe(true);
    expect(isNavigationItemAccessible(tasks)).toBe(true);
  });

  it("hides role-gated items from authenticated users without the role, and hides admin without host-control read", () => {
    expect(isNavigationItemAccessible(adminOnly, { authenticated: true, roles: ["designer"], canReadAdmin: true })).toBe(false);
    expect(isNavigationItemAccessible(adminOnly, { authenticated: true, roles: ["administrator"], canReadAdmin: true })).toBe(true);
    expect(isNavigationItemAccessible(adminOnly, { authenticated: false, canReadAdmin: true })).toBe(true);
    expect(isNavigationItemAccessible(modules, { canReadAdmin: false })).toBe(false);
  });

  it("groups today's entries into main, named groups and admin without nesting lifted children", () => {
    const navigation = filterAccessibleNavigation(
      [home, workflows, weather, modules, diagnostics, logs, tasks],
      { enabledFeatures: ["user-tasks"], canReadAdmin: true }
    );

    expect(getTopLevelNavigationItemsByPlacement(navigation, "main").map(item => item.id)).toEqual(["home", "workflows", "tasks"]);
    expect(getTopLevelNavigationItemsByPlacement(navigation, "build").map(item => item.id)).toEqual(["diagnostics"]);
    expect(getTopLevelNavigationItemsByPlacement(navigation, "admin").map(item => item.id)).toEqual(["modules"]);
  });

  it("lifts a child whose placement differs from its parent and keeps unset children nested", () => {
    const parent: StudioNavigationContribution = { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main" };
    const nested: StudioNavigationContribution = { id: "definitions", label: "Definitions", path: "/workflows/definitions/list", parentId: "workflows" };
    const lifted: StudioNavigationContribution = {
      id: "executables",
      label: "Executables",
      path: "/workflows/executables",
      parentId: "workflows",
      placement: "build"
    };
    const navigation = [parent, nested, lifted];

    expect(getTopLevelNavigationItemsByPlacement(navigation, "main").map(item => item.id)).toEqual(["workflows"]);
    expect(getChildNavigationItems(navigation, parent).map(item => item.id)).toEqual(["definitions"]);
    expect(getTopLevelNavigationItemsByPlacement(navigation, "build").map(item => item.id)).toEqual(["executables"]);
    expect(getChildNavigationItems(navigation, parent).map(item => item.id)).not.toContain("executables");
  });

  it("hides an unset child of an Administration item when the parent is gated out", () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "modules", label: "Modules", path: "/modules", placement: "admin" },
      { id: "mod-logs", label: "Module logs", path: "/modules/logs", parentId: "modules" }
    ];

    const visible = filterAccessibleNavigation(navigation, { authenticated: true, canReadAdmin: false });
    expect(visible.map(item => item.id)).toEqual([]);
    expect(getTopLevelNavigationItemsByPlacement(visible, "build").map(item => item.id)).toEqual([]);

    const allowed = filterAccessibleNavigation(navigation, { authenticated: true, canReadAdmin: true });
    expect(allowed.map(item => item.id)).toEqual(["modules", "mod-logs"]);
    expect(getTopLevelNavigationItemsByPlacement(allowed, "admin").map(item => item.id)).toEqual(["modules"]);
    expect(getChildNavigationItems(allowed, navigation[0]).map(item => item.id)).toEqual(["mod-logs"]);
  });

  it("keeps settings placement without host-control read; admin stays gated", () => {
    const appearance: StudioNavigationContribution = { id: "appearance", label: "Appearance", path: "/settings/appearance", placement: "settings" };
    expect(isNavigationItemAccessible(appearance, { canReadAdmin: false })).toBe(true);
    expect(isNavigationItemAccessible(appearance, { canReadAdmin: true })).toBe(true);
    expect(isNavigationItemAccessible(modules, { canReadAdmin: false })).toBe(false);
  });

  it("remembers independent group choices and tolerates malformed storage", () => {
    const storage = memoryStorage();
    expect(readNavigationGroups(storage)).toEqual({});
    writeNavigationGroups({ build: false, operate: true }, storage);
    expect(readNavigationGroups(storage)).toEqual({ build: false, operate: true });
    storage.setItem(navigationGroupsStorageKey, '{"build":"false","unknown":false,"integrations":false}');
    expect(readNavigationGroups(storage)).toEqual({ integrations: false });
    storage.setItem(navigationGroupsStorageKey, 'broken');
    expect(readNavigationGroups(storage)).toEqual({});
    writeNavigationGroups({}, storage);
    expect(storage.getItem(navigationGroupsStorageKey)).toBeNull();
  });
});

function memoryStorage(initial: Record<string, string> = {}) {
  const data = new Map(Object.entries(initial));
  return {
    data,
    getItem: (key: string) => data.get(key) ?? null,
    setItem: (key: string, value: string) => { data.set(key, value); },
    removeItem: (key: string) => { data.delete(key); }
  };
}
