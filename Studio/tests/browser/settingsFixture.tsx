import React, { useEffect, useMemo, useState } from "react";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences, type ThemeModePreference } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { SettingsRoute } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/settings/SettingsRoute";
import type { StudioNavigationContribution } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import { createNotificationController, type StudioNotificationController } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import { StudioField } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/forms/Field";
import { ToastHost } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/feedback/ToastHost";
import { useCompactNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/navigation/useCompactNavigation";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const searchParams = new URLSearchParams(window.location.search);
const appearance = (searchParams.get("appearance") ?? "light") as ThemeModePreference | "dark";
const requestedPath = window.location.pathname.startsWith("/settings") ? window.location.pathname : "/settings/appearance";
const focusNotices = searchParams.has("focus-notices");

/** Isolated layout contract using the real shell, nested content hooks and notification controller. */
function FocusedSettingsFields({ controller }: { controller: StudioNotificationController }) {
  const { contentRef } = useCompactNavigation();
  return <div ref={contentRef}>
    <button type="button" onMouseDown={event => event.preventDefault()} onClick={() => controller.api.notify({
      level: "error", title: "Persistent focus notice",
      message: "A deliberately wrapped notification exercises the real toast stack while the existing field retains keyboard focus and its unsaved value."
    })}>Show focus notice</button>
    {Array.from({ length: 12 }, (_, index) => <StudioField key={index} label={`Setting ${index + 1}`}>
      <input defaultValue={`Draft ${index + 1}`} />
    </StudioField>)}
  </div>;
}

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
  const notifications = useMemo(() => createNotificationController(), []);

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
        toastHost={focusNotices ? <ToastHost controller={notifications} navigate={navigate} /> : undefined}
      >
        <SettingsRoute
          path={path}
          navigation={navigation}
          onNavigate={navigate}
          renderSection={section => (
            <section style={{ padding: 8 }}>
              <h2>{section.label}</h2>
              {focusNotices ? <FocusedSettingsFields controller={notifications} /> : <p>{section.path} host page.</p>}
            </section>
          )}
        />
      </ShellFrame>
    </ThemeProvider>
  );
}
