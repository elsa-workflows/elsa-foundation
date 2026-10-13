import React, { useEffect } from "react";
import { ShellFrame, getStudioNavigation } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import { moreToolsStorageKey } from "../../src/apps/Elsa.Studio.Web/Client/src/app/navigation/placement";
import type { StudioNavigationContribution } from "../../src/apps/Elsa.Studio.Web/Client/src/sdk";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", order: 30, placement: "main" },
  { id: "workflows-executables", label: "Executables", path: "/workflows/executables", order: 10, placement: "more" },
  { id: "diagnostics", label: "Diagnostics", path: "/diagnostics", order: 60, placement: "more" },
  { id: "feature-management", label: "Features", path: "/features", order: 40, placement: "admin" }
];

export function SettingsBeforeFixture() {
  writeAppearancePreferences({ themeId: "classic", mode: "light", navigation: "left" });
  window.localStorage.setItem(moreToolsStorageKey, "true");
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
        path="/settings"
        title="Studio"
        backendBaseUrl="https://backend.example/"
        onNavigate={() => undefined}
      >
        <div className="empty-state">No Studio route is registered for /settings.</div>
      </ShellFrame>
    </ThemeProvider>
  );
}
