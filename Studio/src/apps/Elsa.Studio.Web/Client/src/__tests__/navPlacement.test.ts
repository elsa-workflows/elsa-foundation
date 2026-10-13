import { describe, expect, it } from "vitest";
import {
  filterAccessibleNavigation,
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

  it("hides role-gated items from authenticated users without the role, and hides admin without host-control read", () => {
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
