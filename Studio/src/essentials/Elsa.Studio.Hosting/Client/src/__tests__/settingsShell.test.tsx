import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { AuthContext } from "../auth/AuthContext";
import type { AuthContextValue } from "../auth/types";
import { ShellFrame, getStudioNavigation } from "../app/App";
import { ThemeProvider, useTheme } from "../app/components/ThemeProvider";
import { AppearancePage } from "../app/settings/AppearancePage";
import { SettingsRoute } from "../app/settings/SettingsRoute";
import { isSettingsSurfacePath, settingsAccessDeniedMessage } from "../app/settings/settingsSections";
import type { StudioNavigationContribution } from "../sdk";

describe("settings shell and appearance", () => {
  let container: HTMLDivElement;
  let root: Root;
  let theme: ReturnType<typeof useTheme> | undefined;

  function Probe() {
    theme = useTheme();
    return null;
  }

  async function act(update: () => void) {
    flushSync(update);
    for (let tick = 0; tick < 3; tick++) {
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }

  beforeEach(() => {
    localStorage.clear();
    container = document.createElement("div");
    document.body.append(container);
    root = createRoot(container);
  });

  afterEach(async () => {
    await act(() => root.unmount());
    container.remove();
    theme = undefined;
  });

  const navigation: StudioNavigationContribution[] = [
    { id: "home", label: "Home", path: "/dashboard", placement: "main" },
    { id: "modules", label: "Modules", path: "/modules", placement: "admin" }
  ];

  it("owns legacy admin paths on getStudioNavigation items, including sub-paths", () => {
    const items = getStudioNavigation(navigation, { includeThemeBuilder: true });
    expect(items.find(item => item.id === "modules")?.activePathPrefix).toBe("/modules");
    expect(items.find(item => item.id === "package-feeds")?.activePathPrefix).toBe("/package-feeds");
    expect(items.find(item => item.id === "theme-builder")?.activePathPrefix).toBe("/theme-builder");
    expect(items.find(item => item.id === "appearance")?.activePathPrefix).toBe("/settings/appearance");
    expect(isSettingsSurfacePath("/modules", items)).toBe(true);
    expect(isSettingsSurfacePath("/modules/logs", items)).toBe(true);
    expect(isSettingsSurfacePath("/package-feeds", items)).toBe(true);
    expect(isSettingsSurfacePath("/theme-builder/preview", items)).toBe(true);
  });

  it("puts a Settings entry in the sidebar and keeps Administration out of it", async () => {
    await act(() => root.render(
      <ThemeProvider>
        <ShellFrame
          navigation={getStudioNavigation(navigation, { includeThemeBuilder: true })}
          panels={[]}
          path="/dashboard"
          title="Home"
          backendBaseUrl="https://backend.example/"
          onNavigate={() => {}}
        >
          <div>content</div>
        </ShellFrame>
      </ThemeProvider>
    ));

    expect(container.querySelector('nav.nav-section[aria-label="Settings"]')?.textContent).toContain("Settings");
    expect(container.querySelector('nav.nav-section[aria-label="Administration"]')).toBeNull();
    expect(container.querySelector('nav.nav-section[aria-label="Settings"]')?.textContent).not.toContain("Modules");
  });

  it("keeps contributed child navigation under its generic parent with a clear active state", async () => {
    const catalog: StudioNavigationContribution[] = [
      { id: "catalog", label: "Catalog", path: "/catalog", placement: "main" },
      { id: "catalog-records", label: "Records", path: "/catalog/records", parentId: "catalog" },
      { id: "catalog-sources", label: "Sources", path: "/catalog/sources", parentId: "catalog" }
    ];

    await act(() => root.render(
      <ThemeProvider>
        <ShellFrame
          navigation={catalog}
          panels={[]}
          path="/catalog/records"
          title="Records"
          backendBaseUrl="https://backend.example/"
          onNavigate={() => {}}
        >
          <div>content</div>
        </ShellFrame>
      </ThemeProvider>
    ));

    const mainNav = container.querySelector('nav[aria-label="Main"]')!;
    const parent = mainNav.querySelector('a[href="/catalog"]')!;
    const activeChild = mainNav.querySelector('a[href="/catalog/records"]')!;

    expect(parent.classList.contains("has-active-child")).toBe(true);
    expect(activeChild.classList.contains("nav-child")).toBe(true);
    expect(activeChild.classList.contains("active")).toBe(true);
    expect(activeChild.getAttribute("aria-current")).toBe("page");
    expect(mainNav.querySelector('a[href="/catalog/sources"]')?.classList.contains("active")).toBe(false);
  });

  it("deep-links Appearance and lists Administration sections in the settings pane", async () => {
    const visited: string[] = [];
    await act(() => root.render(
      <ThemeProvider>
        <SettingsRoute
          path="/settings/appearance"
          navigation={getStudioNavigation(navigation, { includeThemeBuilder: true })}
          onNavigate={path => visited.push(path)}
          renderSection={section => <div>Host {section.label}</div>}
        />
      </ThemeProvider>
    ));

    const sectionNav = container.querySelector('nav[aria-label="Settings sections"]')!;
    expect(sectionNav.textContent).toContain("Appearance");
    expect(sectionNav.textContent).toContain("Administration");
    expect(sectionNav.textContent).toContain("Modules");
    expect(sectionNav.textContent).toContain("Theme builder");
    expect(container.textContent).toContain("Appearance");
    expect(container.querySelector('[aria-current="page"]')?.textContent).toContain("Appearance");

    const modules = Array.from(sectionNav.querySelectorAll("a")).find(link => link.textContent === "Modules")!;
    await act(() => modules.click());
    expect(visited).toEqual(["/settings/modules"]);
  });

  it("offers System, Light and Dark, resets every appearance key, and links to Theme Builder", async () => {
    localStorage.setItem("elsa-studio-theme", "drift");
    localStorage.setItem("elsa-studio-theme-mode", "dark");
    localStorage.setItem("elsa-studio-nav-mode", "top");
    localStorage.setItem("elsa-studio-sidebar-collapsed", "true");
    localStorage.setItem("elsa-studio-bottom-panel-collapsed", "false");
    localStorage.setItem("elsa-studio-bottom-panel-height", "320");
    localStorage.setItem("elsa-studio-bottom-panel-maximized", "true");
    localStorage.setItem("elsa-studio-active-bottom-panel", "console");

    const visited: string[] = [];
    await act(() => root.render(
      <ThemeProvider>
        <Probe />
        <AppearancePage themeBuilderHref="/settings/theme-builder" onNavigate={path => visited.push(path)} />
      </ThemeProvider>
    ));

    const modes = Array.from(container.querySelectorAll<HTMLButtonElement>('[role="radiogroup"][aria-label="Colour mode"] [role="radio"]'));
    expect(modes.map(option => option.getAttribute("aria-label"))).toEqual(expect.arrayContaining([
      "System (default)",
      "Light",
      "Dark"
    ]));

    await act(() => modes.find(option => option.getAttribute("aria-label") === "System (default)")!.click());
    expect(theme!.modePreference).toBe("system");

    await act(() => container.querySelector<HTMLButtonElement>("button.studio-button")!.click());
    expect(localStorage.getItem("elsa-studio-theme")).toBeNull();
    expect(localStorage.getItem("elsa-studio-theme-mode")).toBeNull();
    expect(localStorage.getItem("elsa-studio-nav-mode")).toBeNull();
    expect(localStorage.getItem("elsa-studio-sidebar-collapsed")).toBeNull();
    expect(localStorage.getItem("elsa-studio-bottom-panel-collapsed")).toBeNull();
    expect(localStorage.getItem("elsa-studio-bottom-panel-height")).toBeNull();
    expect(localStorage.getItem("elsa-studio-bottom-panel-maximized")).toBeNull();
    expect(localStorage.getItem("elsa-studio-active-bottom-panel")).toBeNull();

    await act(() => container.querySelector<HTMLAnchorElement>("a.settings-builder-link")!.click());
    expect(visited).toEqual(["/settings/theme-builder"]);
  });

  it("gates legacy admin URLs for an authenticated user without module-management:read", async () => {
    const rendered: string[] = [];
    await act(() => root.render(
      <AuthContext.Provider value={authenticatedWithoutHostControl()}>
        <ThemeProvider>
          <SettingsRoute
            path="/modules"
            navigation={getStudioNavigation(navigation, { includeThemeBuilder: true })}
            onNavigate={() => {}}
            renderSection={section => {
              rendered.push(section.id);
              return <div>Host {section.label}</div>;
            }}
          />
        </ThemeProvider>
      </AuthContext.Provider>
    ));

    const sectionNav = container.querySelector('nav[aria-label="Settings sections"]')!;
    expect(sectionNav.textContent).toContain("Appearance");
    expect(sectionNav.textContent).not.toContain("Modules");
    expect(sectionNav.textContent).not.toContain("Theme builder");
    expect(container.textContent).toContain(settingsAccessDeniedMessage);
    expect(container.textContent).not.toContain("Host Modules");
    expect(container.querySelector(".modules-page")).toBeNull();
    expect(rendered).toEqual([]);

    await act(() => root.render(
      <AuthContext.Provider value={authenticatedWithoutHostControl()}>
        <ThemeProvider>
          <SettingsRoute
            path="/package-feeds"
            navigation={getStudioNavigation(navigation, { includeThemeBuilder: true })}
            onNavigate={() => {}}
            renderSection={section => {
              rendered.push(section.id);
              return <div>Host {section.label}</div>;
            }}
          />
        </ThemeProvider>
      </AuthContext.Provider>
    ));
    expect(container.textContent).toContain(settingsAccessDeniedMessage);
    expect(rendered).toEqual([]);

    await act(() => root.render(
      <AuthContext.Provider value={authenticatedWithoutHostControl()}>
        <ThemeProvider>
          <SettingsRoute
            path="/theme-builder"
            navigation={getStudioNavigation(navigation, { includeThemeBuilder: true })}
            onNavigate={() => {}}
            renderSection={section => {
              rendered.push(section.id);
              return <div>Host {section.label}</div>;
            }}
          />
        </ThemeProvider>
      </AuthContext.Provider>
    ));
    expect(container.textContent).toContain(settingsAccessDeniedMessage);
    expect(rendered).toEqual([]);
  });
});

function authenticatedWithoutHostControl(): AuthContextValue {
  return {
    session: { status: "authenticated", subject: "designer", roles: ["designer"], permissions: [] },
    capabilities: null,
    login: async () => undefined,
    logout: async () => undefined,
    refresh: async () => ({ status: "authenticated", roles: [], permissions: [] })
  };
}
