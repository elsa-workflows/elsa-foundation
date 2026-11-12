import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Diagnostics, getStudioNavigation, ShellFrame } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { filterAccessibleNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/navigation/placement";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import type { ElsaStudioModuleApi, StudioNavigationContribution } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const theme = new URLSearchParams(window.location.search).get("theme") === "dark" ? "dark" : "light";
writeAppearancePreferences({ themeId: "classic", mode: theme, navigation: "left" });

const contributedNavigation: StudioNavigationContribution[] = [
  { id: "open-telemetry", label: "OpenTelemetry", path: "/diagnostics/open-telemetry", parentId: "diagnostics", order: 880 },
  { id: "structured-logs", label: "Structured logs", path: "/diagnostics/structured-logs", parentId: "diagnostics", order: 860 },
  { id: "console", label: "Console", path: "/diagnostics/console", parentId: "diagnostics", order: 840 },
  { id: "custom-audit", label: "Database audit", path: "/diagnostics/audit", parentId: "diagnostics", order: 900 },
  { id: "workflows-runtime-diagnostics", label: "Runtime evidence settings", path: "/workflows/runtime-diagnostics", order: 940 }
];

const hostNavigation = getStudioNavigation(contributedNavigation);
const filteredNavigation = filterAccessibleNavigation(hostNavigation, {
  enabledFeatures: new Set(["open-telemetry", "structured-logs", "console", "workflows"]),
  authenticated: true,
  roles: [],
  permissions: []
});

function FixtureWidget() {
  return <p>Host diagnostics widget remains visible.</p>;
}

const api = {
  diagnosticsWidgets: {
    list: () => [{ id: "fixture-widget", title: "Fixture widget", component: FixtureWidget }]
  }
} as unknown as ElsaStudioModuleApi;

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

export function DiagnosticsIndexFixture() {
  const [path, setPath] = useState("/diagnostics");
  const navigate = (nextPath: string) => {
    window.history.pushState({}, "", nextPath);
    setPath(nextPath);
  };

  return (
    <ThemeProvider>
      <ShellFrame
        navigation={hostNavigation}
        panels={[]}
        path={path}
        title="Diagnostics"
        backendBaseUrl="https://backend.example/"
        onNavigate={navigate}
      >
        <output data-testid="diagnostics-route" aria-live="polite">{path}</output>
        <Diagnostics api={api} navigation={filteredNavigation} onNavigate={navigate} />
      </ShellFrame>
    </ThemeProvider>
  );
}

createRoot(document.getElementById("root")!).render(
  <QueryClientProvider client={queryClient}>
    <DiagnosticsIndexFixture />
  </QueryClientProvider>
);
