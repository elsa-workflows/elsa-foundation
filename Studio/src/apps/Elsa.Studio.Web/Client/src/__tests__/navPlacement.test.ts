import { describe, expect, it } from "vitest";
import {
  filterAccessibleNavigation,
  getChildNavigationItems,
  getNavigationOrder,
  getNavigationPlacement,
  getTopLevelNavigationItemsByPlacement,
  isNavigationItemAccessible,
  moreToolsStorageKey,
  readMoreToolsOpen,
  writeMoreToolsOpen
} from "../app/navigation/placement";
import type { StudioNavigationContribution } from "../sdk";

const home: StudioNavigationContribution = { id: "home", label: "Home", path: "/dashboard", placement: "main", order: 10 };
const workflows: StudioNavigationContribution = { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main", order: 20 };
const weather: StudioNavigationContribution = { id: "weather", label: "Weather", path: "/weather" };
const modules: StudioNavigationContribution = { id: "modules", label: "Modules", path: "/modules" };
const diagnostics: StudioNavigationContribution = { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", placement: "more" };
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
  it("defaults unset contributions to More tools and honours an explicit placement", () => {
    expect(getNavigationPlacement(weather)).toBe("more");
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

  it("hides role-gated items from authenticated users without the role, and hides admin and settings without host-control read", () => {
    expect(isNavigationItemAccessible(adminOnly, { authenticated: true, roles: ["designer"], canReadAdmin: true })).toBe(false);
    expect(isNavigationItemAccessible(adminOnly, { authenticated: true, roles: ["administrator"], canReadAdmin: true })).toBe(true);
    expect(isNavigationItemAccessible(adminOnly, { authenticated: false, canReadAdmin: true })).toBe(true);
    expect(isNavigationItemAccessible(modules, { canReadAdmin: false })).toBe(false);
  });

  it("groups today's entries into main, more and admin without nesting lifted children", () => {
    const navigation = filterAccessibleNavigation(
      [home, workflows, weather, modules, diagnostics, logs, tasks],
      { enabledFeatures: ["user-tasks"], canReadAdmin: true }
    );

    expect(getTopLevelNavigationItemsByPlacement(navigation, "main").map(item => item.id)).toEqual(["home", "workflows", "tasks"]);
    expect(getTopLevelNavigationItemsByPlacement(navigation, "more").map(item => item.id)).toEqual(["diagnostics", "weather"]);
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
      placement: "more"
    };
    const navigation = [parent, nested, lifted];

    expect(getTopLevelNavigationItemsByPlacement(navigation, "main").map(item => item.id)).toEqual(["workflows"]);
    expect(getChildNavigationItems(navigation, parent).map(item => item.id)).toEqual(["definitions"]);
    expect(getTopLevelNavigationItemsByPlacement(navigation, "more").map(item => item.id)).toEqual(["executables"]);
    expect(getChildNavigationItems(navigation, parent).map(item => item.id)).not.toContain("executables");
  });

  it("hides an unset child of an Administration item when the parent is gated out", () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "modules", label: "Modules", path: "/modules", placement: "admin" },
      { id: "mod-logs", label: "Module logs", path: "/modules/logs", parentId: "modules" }
    ];

    const visible = filterAccessibleNavigation(navigation, { authenticated: true, canReadAdmin: false });
    expect(visible.map(item => item.id)).toEqual([]);
    expect(getTopLevelNavigationItemsByPlacement(visible, "more").map(item => item.id)).toEqual([]);

    const allowed = filterAccessibleNavigation(navigation, { authenticated: true, canReadAdmin: true });
    expect(allowed.map(item => item.id)).toEqual(["modules", "mod-logs"]);
    expect(getTopLevelNavigationItemsByPlacement(allowed, "admin").map(item => item.id)).toEqual(["modules"]);
    expect(getChildNavigationItems(allowed, navigation[0]).map(item => item.id)).toEqual(["mod-logs"]);
  });

  it("hides settings placement without host-control read, the same gate as admin", () => {
    const appearance: StudioNavigationContribution = { id: "appearance", label: "Appearance", path: "/settings/appearance", placement: "settings" };
    expect(isNavigationItemAccessible(appearance, { canReadAdmin: false })).toBe(false);
    expect(isNavigationItemAccessible(appearance, { canReadAdmin: true })).toBe(true);
    expect(isNavigationItemAccessible(appearance, { authenticated: false, canReadAdmin: true })).toBe(true);
  });

  it("remembers More tools open/closed through the appearance-style storage helper", () => {
    const storage = memoryStorage();
    expect(readMoreToolsOpen(storage)).toBe(false);
    writeMoreToolsOpen(true, storage);
    expect(storage.data.get(moreToolsStorageKey)).toBe("true");
    expect(readMoreToolsOpen(storage)).toBe(true);
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
