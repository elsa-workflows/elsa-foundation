import React, { useState } from "react";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { NavigationViews, findNavigationViews } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/navigation/NavigationViews";
import { filterAccessibleNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/navigation/placement";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import type { StudioNavigationContribution } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import { createEndpointContext } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import { createStudioRegistry } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/registry";
import { register as registerEtl } from "../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/module";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "build", childDisplay: "views" },
  { id: "definitions", label: "Definitions", path: "/workflows/definitions", order: 10, parentId: "workflows" },
  { id: "workflows-executables", label: "Executables", path: "/workflows/executables", order: 20, parentId: "workflows" },
  { id: "agents", label: "Agents", path: "/agents", order: 25, placement: "build" },
  { id: "workflows-activity-definitions", label: "Activity definitions", path: "/workflows/activity-definitions", order: 10, placement: "build", parentId: "library" },
  { id: "workflows-templates", label: "Templates", path: "/workflows/templates", order: 20, placement: "build", parentId: "library" },
  { id: "workflows-runs", label: "Runs", path: "/workflows/instances", order: 30, placement: "operate" },
  { id: "tasks", label: "User tasks", path: "/user-tasks", order: 40, placement: "operate" },
  { id: "connections", label: "Connections", path: "/security/connections", order: 10, placement: "integrations" },
  { id: "secrets", label: "Secrets", path: "/security/secrets", order: 20, placement: "integrations" },
  { id: "console", label: "Console", path: "/diagnostics/console", parentId: "diagnostics", order: 10 },
  { id: "structured-logs", label: "Structured logs", path: "/diagnostics/structured-logs", parentId: "diagnostics", order: 20 },
  { id: "open-telemetry", label: "OpenTelemetry", path: "/diagnostics/open-telemetry", parentId: "diagnostics", order: 30 },
  { id: "workflows-runtime-diagnostics", label: "Runtime evidence settings", path: "/workflows/runtime-diagnostics", placement: "runtime" },
  { id: "workflows-activity-availability", label: "Activity availability", path: "/workflows/activity-availability", placement: "authoring" },
  { id: "feature-management", label: "Features", path: "/features", placement: "admin" }
];

export function NavPlacementFixture() {
  const params = new URLSearchParams(window.location.search);
  const [path, setPath] = useState("/security/connections");
  writeAppearancePreferences({ themeId: params.get("theme") ?? "classic", mode: params.get("appearance") === "dark" ? "dark" : "light", navigation: params.get("navigation") === "top" ? "top" : "left" });
  const overflowNavigation: StudioNavigationContribution[] = params.has("menuOverflow") ? [
    { id: "overflow-parent", label: "Long navigation", path: "/navigation-overflow", placement: "build", order: 100 },
    ...Array.from({ length: 24 }, (_, index) => ({ id: `overflow-${index + 1}`, label: `Overflow destination ${index + 1}`,
      path: `/navigation-overflow/${index + 1}`, parentId: "overflow-parent", order: index + 1 }))
  ] : [];
  // The legacy registry makes actual ETL contributions available in this isolated UI fixture.
  // Composed proof separately enables the real design API and its private SQLite store.
  const registry = createStudioRegistry({ hostVersion: "1", sdkVersion: "1", ...createEndpointContext("https://backend.example/") });
  if (params.has("etlNavigation")) registerEtl(registry);
  const navigation = filterAccessibleNavigation(getStudioNavigation([...moduleNavigation, ...overflowNavigation, ...registry.navigation.list()], { includeThemeBuilder: true }));
  const views = findNavigationViews(path, navigation);
  const title = navigation.find(item => item.path === path && item.childDisplay !== "views")?.label ?? "Studio";
  return <ThemeProvider><ShellFrame navigation={navigation} panels={[]} path={path} title={title} backendBaseUrl="https://backend.example/" onNavigate={setPath}>
    {views ? <NavigationViews {...views} path={path} onNavigate={setPath} /> : null}
    <section className="studio-page"><h2>{title}</h2><p>Navigation composition fixture.</p></section>
  </ShellFrame></ThemeProvider>;
}
