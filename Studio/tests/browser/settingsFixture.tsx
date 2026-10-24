import React, { useEffect, useState } from "react";
import { ShellFrame, getStudioNavigation } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences, type ThemeModePreference } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import { SettingsRoute } from "../../src/apps/Elsa.Studio.Web/Client/src/app/settings/SettingsRoute";
import type { StudioNavigationContribution } from "../../src/apps/Elsa.Studio.Web/Client/src/sdk";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";

const searchParams = new URLSearchParams(window.location.search);
const appearance = (searchParams.get("appearance") ?? "light") as ThemeModePreference | "dark";
const requestedPath = window.location.pathname.startsWith("/settings") ? window.location.pathname : "/settings/appearance";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", order: 30, placement: "main" },
  { id: "workflows-executables", label: "Executables", path: "/workflows/executables", order: 10, placement: "more" },
  { id: "feature-management", label: "Features", path: "/features", order: 40, placement: "admin" }
];

export function SettingsFixture() {
  writeAppearancePreferences({
    themeId: "classic",
    mode: appearance === "system" ? "system" : appearance === "dark" ? "dark" : "light",
    navigation: "left"
  });
  const navigation = getStudioNavigation(moduleNavigation, { includeThemeBuilder: true });
  const [path, setPath] = useState(requestedPath);

  useEffect(() => {
    const onPopState = () => {
      const next = window.location.pathname.startsWith("/settings") ? window.location.pathname : "/settings/appearance";
      setPath(next);
    };
    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, []);

  const navigate = (nextPath: string) => {
    window.history.pushState({}, "", `${nextPath}${window.location.search}`);
    setPath(nextPath);
  };

  return (
    <ThemeProvider>
      <ShellFrame
        navigation={navigation}
        panels={[]}
        path={path}
        title="Settings"
        backendBaseUrl="https://backend.example/"
        onNavigate={navigate}
      >
        <SettingsRoute
          path={path}
          navigation={navigation}
          onNavigate={navigate}
          renderSection={section => (
            <section style={{ padding: 8 }}>
              <h2>{section.label}</h2>
              <p>{section.path} host page.</p>
            </section>
          )}
        />
      </ShellFrame>
    </ThemeProvider>
  );
}
