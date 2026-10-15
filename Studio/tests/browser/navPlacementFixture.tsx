import React, { useEffect } from "react";
import { ShellFrame, getStudioNavigation } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import { moreToolsStorageKey } from "../../src/apps/Elsa.Studio.Web/Client/src/app/navigation/placement";
import type { StudioNavigationContribution } from "../../src/apps/Elsa.Studio.Web/Client/src/sdk";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";

const searchParams = new URLSearchParams(window.location.search);
const moreOpen = searchParams.get("more") === "open";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", order: 30, placement: "main" },
  { id: "workflows-executables", label: "Executables", path: "/workflows/executables", order: 10, placement: "more" },
  { id: "workflows-activity-definitions", label: "Activity definitions", path: "/workflows/activity-definitions", order: 20, placement: "more" },
  { id: "secrets", label: "Secrets", path: "/security/secrets", order: 30, placement: "more" },
  { id: "workflows-runtime-diagnostics", label: "Runtime diagnostics", path: "/workflows/runtime-diagnostics", order: 40, placement: "more" },
  { id: "workflows-activity-availability", label: "Activity availability", path: "/workflows/activity-availability", order: 50, placement: "more" },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", order: 60, placement: "more" },
  { id: "feature-management", label: "Features", path: "/features", order: 40, placement: "admin" }
];

export function NavPlacementFixture() {
  writeAppearancePreferences({ themeId: "classic", mode: "light", navigation: "left" });
  window.localStorage.setItem(moreToolsStorageKey, String(moreOpen));
  const navigation = getStudioNavigation(moduleNavigation, { includeThemeBuilder: true });

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", "classic");
    document.documentElement.setAttribute("data-theme-mode", "light");
    document.documentElement.setAttribute("data-theme-appearance", "light");
  }, []);

  return (
    <ThemeProvider>
      <ShellFrame
        navigation={navigation}
        panels={[]}
        path="/workflows/definitions"
        title="Workflows"
        backendBaseUrl="https://backend.example/"
        onNavigate={() => undefined}
      >
        <section style={{ padding: 24 }}>
          <h2>Workflows</h2>
          <p>Navigation placement fixture.</p>
        </section>
      </ShellFrame>
    </ThemeProvider>
  );
}
