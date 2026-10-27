import React from "react";
import { UserMenu } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/UserMenu";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { StudioAuthBoundary, createStudioAuthManager } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/auth/studioAuth";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { ThemeSwitcher } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeSwitcher";
import { AgentLauncher } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/AgentLauncher";
import type { AgentSessionIndicatorSession } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/agent/AgentSessionIndicator";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import type { StudioNavigationContribution } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";

const fixtureQuery = new URLSearchParams(window.location.search);
const dark = fixtureQuery.get("theme") === "dark";
const navigationPreference = (["left", "top", "theme"] as const).find(mode => mode === fixtureQuery.get("navigation")) ?? "left";
const weaverVariant = fixtureQuery.get("weaver") ?? "0";
const showWeaver = weaverVariant !== "0";
const showAppearance = fixtureQuery.get("appearance") !== "off";
const showAccount = fixtureQuery.get("account") !== "off";
const backendBaseUrl = fixtureQuery.get("backend") === "long"
  ? "https://workflow-runtime.internal.example.org:18443"
  : window.location.origin;
const agentSessions: AgentSessionIndicatorSession[] = weaverVariant === "dense"
  ? [
      {
        id: "active",
        title: "Workflow review",
        status: "active",
        pendingProposals: 3,
        pendingArtifacts: 2,
        pendingPrompts: 1,
        childSessions: [{ id: "child-waiting", title: "Waiting child", status: "waiting" }]
      },
      { id: "waiting", title: "Waiting for input", status: "waiting" },
      { id: "blocked", title: "Blocked run", status: "blocked" },
      { id: "failed", title: "Failed run", status: "failed" }
    ]
  : showWeaver
    ? [
        { id: "active", title: "Workflow review", status: "active", pendingProposals: 2 },
        { id: "waiting", title: "Waiting for input", status: "waiting" }
      ]
    : [];

const moduleNavigation: StudioNavigationContribution[] = [
  { id: "home", label: "Home", path: "/dashboard", order: 10, placement: "main" },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", order: 20, placement: "main" }
];

// The shell's real sign-in wiring against this origin; the spec answers `/_elsa/identity/*` itself.
const authManager = createStudioAuthManager({ auth: { enabled: fixtureQuery.get("auth") !== "off" } }, window.location.origin);

export function UserMenuFixture() {
  const [agentOpen, setAgentOpen] = React.useState(false);
  writeAppearancePreferences({ themeId: "classic", mode: dark ? "dark" : "light", navigation: navigationPreference });

  return (
    <StudioAuthBoundary manager={authManager}>
      <ThemeProvider>
        <ShellFrame
          assistantAction={showWeaver ? <AgentLauncher open={agentOpen} sessions={agentSessions} onClick={() => setAgentOpen(open => !open)} /> : undefined}
          accountAction={showAccount ? <UserMenu /> : undefined}
          navigation={getStudioNavigation(moduleNavigation, { includeThemeBuilder: false })}
          panels={[]}
          path="/dashboard"
          title="Home"
          backendBaseUrl={backendBaseUrl}
          themeAction={showAppearance ? <ThemeSwitcher /> : null}
          onNavigate={() => {}}
        >
          <section style={{ padding: 8 }}>Home content</section>
        </ShellFrame>
      </ThemeProvider>
    </StudioAuthBoundary>
  );
}
