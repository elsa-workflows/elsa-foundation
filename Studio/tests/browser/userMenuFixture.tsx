import React from "react";
import { UserMenu } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/UserMenu";
import { ShellFrame, getStudioNavigation } from "../../src/apps/Elsa.Studio.Web/Client/src/app/App";
import { StudioAuthBoundary, createStudioAuthManager } from "../../src/apps/Elsa.Studio.Web/Client/src/app/auth/studioAuth";
import { ThemeProvider } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeProvider";
import { ThemeSwitcher } from "../../src/apps/Elsa.Studio.Web/Client/src/app/components/ThemeSwitcher";
import { writeAppearancePreferences } from "../../src/apps/Elsa.Studio.Web/Client/src/app/themes/appearance";
import type { StudioNavigationContribution } from "../../src/apps/Elsa.Studio.Web/Client/src/sdk";
import "../../src/apps/Elsa.Studio.Web/Client/src/app/styles.css";

const dark = new URLSearchParams(window.location.search).get("theme") === "dark";

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" }
];

// The shell's real sign-in wiring against this origin; the spec answers `/_elsa/identity/*` itself.
const authManager = createStudioAuthManager({ auth: { enabled: true } }, window.location.origin);

export function UserMenuFixture() {
  writeAppearancePreferences({ themeId: "classic", mode: dark ? "dark" : "light", navigation: "left" });

  return (
    <StudioAuthBoundary manager={authManager}>
      <ThemeProvider>
        <ShellFrame
          accountAction={<UserMenu />}
          navigation={getStudioNavigation(moduleNavigation, { includeThemeBuilder: false })}
          panels={[]}
          path="/dashboard"
          title="Home"
          backendBaseUrl={window.location.origin}
          themeAction={<ThemeSwitcher />}
          onNavigate={() => {}}
        >
          <section style={{ padding: 8 }}>Home content</section>
        </ShellFrame>
      </ThemeProvider>
    </StudioAuthBoundary>
  );
}
