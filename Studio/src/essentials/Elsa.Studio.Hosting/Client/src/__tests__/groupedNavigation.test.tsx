import React from "react";
import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ShellFrame, getStudioNavigation } from "../app/App";
import { ThemeProvider } from "../app/components/ThemeProvider";
import { filterAccessibleNavigation, getNavigationPlacement } from "../app/navigation/placement";
import { findNavigationViews } from "../app/navigation/NavigationViews";
import { getSettingsSectionGroups, isSettingsSurfacePath } from "../app/settings/settingsSections";
import type { StudioNavigationContribution } from "../sdk";

afterEach(() => localStorage.clear());

async function act(update: () => void) {
  flushSync(update);
  await new Promise(resolve => setTimeout(resolve, 0));
}

describe("grouped navigation", () => {
  it("does not silently place an undeclared extension into a catch-all", () => {
    expect(getNavigationPlacement({ id: "example", path: "/example" })).toBeUndefined();
  });

  it("links Library to its first permitted view and removes an empty Library", () => {
    const entries: StudioNavigationContribution[] = [
      { id: "activities", label: "Activity definitions", path: "/activities", parentId: "library", placement: "build", requiresPermission: "activities:read", order: 10 },
      { id: "templates", label: "Templates", path: "/templates", parentId: "library", placement: "build", requiresPermission: "templates:read", order: 20 }
    ];
    const navigation = getStudioNavigation(entries);
    const visible = filterAccessibleNavigation(navigation, { authenticated: true, permissions: ["templates:read"] });
    expect(visible.find(item => item.id === "library")?.path).toBe("/templates");
    expect(findNavigationViews("/templates", visible)?.views.map(item => item.id)).toEqual(["templates"]);
    expect(filterAccessibleNavigation(navigation, { authenticated: true, permissions: [] }).some(item => item.id === "library")).toBe(false);
  });

  it("retains executable views on runtime-only hosts without a dead Definitions link", () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "workflows", label: "Workflows", path: "/definitions", placement: "build", childDisplay: "views" },
      { id: "definitions", label: "Definitions", path: "/definitions", parentId: "workflows", requiresFeature: "design" },
      { id: "executables", label: "Executables", path: "/executables", parentId: "workflows", requiresFeature: "runtime" }
    ];
    const visible = filterAccessibleNavigation(navigation, { enabledFeatures: ["runtime"] });
    expect(visible.find(item => item.id === "workflows")?.path).toBe("/executables");
    expect(findNavigationViews("/executables", visible)?.views.map(item => item.id)).toEqual(["executables"]);
  });

  it("keeps moved settings deep links and their own permissions without adding the host-control gate", () => {
    const navigation: StudioNavigationContribution[] = [
      { id: "authoring-policy", label: "Activity availability", path: "/workflows/activity-availability", placement: "authoring", requiresPermission: "authoring:manage" },
      { id: "runtime-policy", label: "Runtime evidence settings", path: "/workflows/runtime-diagnostics", placement: "runtime" },
      { id: "modules", label: "Modules", path: "/modules", placement: "admin" }
    ];
    expect(isSettingsSurfacePath("/workflows/runtime-diagnostics", navigation)).toBe(true);
    expect(getSettingsSectionGroups(navigation, { authenticated: true, canReadAdmin: false, permissions: [] }).map(group => group.id)).toEqual(["runtime"]);
    expect(getSettingsSectionGroups(navigation, { authenticated: true, canReadAdmin: false, permissions: ["authoring:manage"] }).map(group => group.id)).toEqual(["authoring", "runtime"]);
  });

  it.each(["/workflows/tags", "/workflows/tags/new", "/workflows/tags/region"])("reveals a saved collapsed Build group at %s", async path => {
    localStorage.setItem("elsa-studio.ui.nav.groups", JSON.stringify({ build: false }));
    const entries: StudioNavigationContribution[] = [
      { id: "workflows", label: "Workflows", path: "/workflows/definitions", activePathPrefix: "/workflows", placement: "build", childDisplay: "views" },
      { id: "definitions", label: "Definitions", path: "/workflows/definitions", parentId: "workflows" }
    ];
    const container = document.createElement("div"); document.body.append(container);
    const root = createRoot(container);
    try {
      await act(() => root.render(<ThemeProvider><ShellFrame navigation={getStudioNavigation(entries)} panels={[]} path={path} title="Workflow tags" backendBaseUrl="https://backend.example" onNavigate={vi.fn()}><p>Workflow tags</p></ShellFrame></ThemeProvider>));
      expect(container.querySelector('nav[aria-label="Build"] button')?.getAttribute("aria-expanded")).toBe("true");
      expect(container.querySelector('nav[aria-label="Build"] a[aria-label="Workflows"]')?.getAttribute("aria-current")).toBe("page");
    } finally { await act(() => root.unmount()); container.remove(); }
  });

  it.each(["/workflows/tags/region", "/workflows/definitions"])("reveals Build when SPA navigation returns to deliberately collapsed %s", async path => {
    const entries: StudioNavigationContribution[] = [
      { id: "home", label: "Home", path: "/dashboard", placement: "main" },
      { id: "workflows", label: "Workflows", path: "/workflows/definitions", activePathPrefix: "/workflows", placement: "build", childDisplay: "views" },
      { id: "definitions", label: "Definitions", path: "/workflows/definitions", parentId: "workflows" }
    ];
    const container = document.createElement("div"); document.body.append(container);
    const root = createRoot(container);
    const render = (currentPath: string) => act(() => root.render(<ThemeProvider><ShellFrame navigation={getStudioNavigation(entries)} panels={[]} path={currentPath} title="Studio" backendBaseUrl="https://backend.example" onNavigate={vi.fn()}><p>Retained page</p></ShellFrame></ThemeProvider>));
    const build = () => container.querySelector<HTMLButtonElement>('nav[aria-label="Build"] button')!;
    try {
      await render(path);
      expect(build().getAttribute("aria-expanded")).toBe("true");
      await act(() => build().click());
      expect(build().getAttribute("aria-expanded")).toBe("false");
      await render(path); // Deliberate collapse must survive rerenders of the current route.
      expect(build().getAttribute("aria-expanded")).toBe("false");
      await render("/dashboard");
      expect(build().getAttribute("aria-expanded")).toBe("false");
      // Back and retained-tab selection both return through the same path prop without remounting.
      await render(path);
      expect(build().getAttribute("aria-expanded")).toBe("true");
      expect(container.querySelector('nav[aria-label="Build"] a[aria-label="Workflows"]')?.getAttribute("aria-current")).toBe("page");
    } finally { await act(() => root.unmount()); container.remove(); }
  });

  it("opens named groups and displays User tasks directly", async () => {
    const container = document.createElement("div");
    document.body.append(container);
    const root = createRoot(container);
    const entries = [
      { id: "home", label: "Home", path: "/", placement: "main" },
      { id: "workflows", label: "Workflows", path: "/workflows", placement: "build" },
      { id: "tasks", label: "User tasks", path: "/tasks", placement: "operate" },
      { id: "connections", label: "Connections", path: "/connections", placement: "integrations" }
    ] as StudioNavigationContribution[];
    try {
      await act(() => root.render(<ThemeProvider><ShellFrame navigation={getStudioNavigation(entries)} panels={[]} path="/connections" title="Connections" backendBaseUrl="https://backend.example" onNavigate={vi.fn()}><p>Connections page</p></ShellFrame></ThemeProvider>));
      for (const label of ["Build", "Operate", "Integrations"]) {
        const group = container.querySelector(`nav[aria-label="${label}"]`);
        expect(group).not.toBeNull();
        expect(group?.querySelector("button")?.getAttribute("aria-expanded")).toBe("true");
      }
      expect(container.querySelector('nav[aria-label="Operate"] a[aria-label="User tasks"]')?.textContent).toBe("User tasks");
      expect(container.textContent).not.toContain("More tools");
    } finally {
      await act(() => root.unmount());
      container.remove();
    }
  });
});
