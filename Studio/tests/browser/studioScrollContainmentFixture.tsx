import React from "react";
import { ShellFrame } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import type { StudioNavigationContribution } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/sdk";
import { StudioCodeEditor } from "../../src/essentials/Elsa.Studio.CodeEditor/Client/src";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/activityDefinitions.css";

const parameters = new URLSearchParams(window.location.search);
const isShortPage = parameters.get("length") === "short";
const profile = parameters.get("profile") === "compact" ? "compact" : "expanded";
const navigation: StudioNavigationContribution[] = Array.from({ length: 32 }, (_, index) => {
  const number = index + 1;
  return {
    id: `activity-section-${number}`,
    label: `Activity section ${number}`,
    path: `/workflows/activity-definitions/section-${number}`,
    order: number,
    placement: "main"
  };
});

const longDocument = JSON.stringify({
  definitionId: "long-activity-definition",
  contract: { inputs: [], outputs: [], outcomes: [] },
  implementation: {
    providerKey: "elsa.activity-graph",
    schemaVersion: "2",
    payload: {
      rootActivity: { nodeId: "root", activityVersionId: "sequence-v1" },
      sourceNotes: Array.from({ length: 120 }, (_, index) => ({
        line: index + 1,
        content: `Activity Definition authoring source line ${index + 1}`
      }))
    }
  },
  layout: []
}, null, 2);

const shortDocument = JSON.stringify({
  definitionId: "short-activity-definition",
  contract: { inputs: [], outputs: [], outcomes: [] },
  implementation: { providerKey: "elsa.activity-graph", schemaVersion: "2", payload: {} },
  layout: []
}, null, 2);

export function StudioScrollContainmentFixture() {
  writeAppearancePreferences({ themeId: "classic", mode: "light", navigation: "left" });

  return (
    <ThemeProvider>
      <ShellFrame
        navigation={navigation}
        panels={[]}
        path="/workflows/activity-definitions"
        title="Activity Definition"
        backendBaseUrl="https://backend.example/"
        enabledFeatures={["Elsa.Studio.Workflows"]}
        onNavigate={() => undefined}
      >
        <section className="ad-page activity-scroll-fixture" aria-labelledby="activity-scroll-title">
          <header>
            <span className="ad-kicker">Activity Definition</span>
            <h1 id="activity-scroll-title">Long Activity Definition editor</h1>
            <p>The code editor lives inside the Studio content scrollport.</p>
          </header>
          {profile === "compact" && !isShortPage ? (
            <section aria-labelledby="public-mappings-title">
              <h2 id="public-mappings-title">Public interface mappings</h2>
              <dl style={{ display: "grid", gap: "0.75rem", margin: "1.5rem 0" }}>
                {Array.from({ length: 36 }, (_, index) => (
                  <div key={index}>
                    <dt>Output mapping {index + 1}</dt>
                    <dd>Maps the activity result to the public interface value {index + 1}.</dd>
                  </div>
                ))}
              </dl>
            </section>
          ) : null}
          <StudioCodeEditor
            profile={profile}
            ariaLabel="Activity Definition JSON"
            document={{
              uri: "elsa://activity-definitions/long-activity-definition/draft.json",
              language: "json",
              value: isShortPage ? shortDocument : longDocument
            }}
            minHeight={isShortPage ? "220px" : "1800px"}
            theme="studio"
            readOnly
          />
        </section>
      </ShellFrame>
    </ThemeProvider>
  );
}
