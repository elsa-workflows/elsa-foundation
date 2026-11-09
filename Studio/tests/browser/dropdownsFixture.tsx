import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences, type ThemeModePreference } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { VariableTargetSelect } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/variableTargetPicker";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

const query = new URLSearchParams(location.search);
writeAppearancePreferences({ themeId: query.get("theme") ?? "classic", mode: (query.get("appearance") ?? "light") as ThemeModePreference, navigation: "left" });

function DropdownsFixture() {
  const [target, setTarget] = useState("");
  return <main style={{ maxWidth: 960, margin: "0 auto", padding: 24, display: "grid", gap: 24 }}>
    <h1>Dropdowns</h1>
    <section aria-label="Activity outputs" style={{ maxWidth: 360 }}>
      <h2>Request</h2>
      <label id="capture-label">Capture into</label>
      <VariableTargetSelect ariaLabelledBy="capture-label" selected={target} status="ready"
        visibleVariables={[{ referenceKey: "request", name: "Request", scopeId: "workflow", isWorkflowScope: true }, { referenceKey: "payload", name: "Payload", scopeId: "workflow", isWorkflowScope: true }]}
        onSelect={selection => setTarget(selection ? `${selection.scopeId}::${selection.referenceKey}` : "")} />
    </section>
    <section aria-label="Settings" className="modules-form" style={{ maxWidth: 360 }}>
      <label>Response mode<select aria-label="Response mode" defaultValue="sync"><option value="sync">Wait for workflow response</option><option value="async">Accept and run in background</option></select></label>
    </section>
    <section aria-label="Toolbar" className="wf-toolbar-field" style={{ maxWidth: 360 }}>
      <label>Page size<select aria-label="Page size" defaultValue="25"><option>25</option><option>50</option><option>100</option></select></label>
    </section>
    <label>Unavailable<select aria-label="Unavailable" disabled><option>Loading options…</option></select></label>
    <label>Choose several<select aria-label="Choose several" multiple defaultValue={["Request"]}><option>Request</option><option>Payload</option></select></label>
    <label dir="rtl">Right to left<select aria-label="Right to left"><option>Request</option><option>Payload</option></select></label>
  </main>;
}

createRoot(document.getElementById("root")!).render(<ThemeProvider><DropdownsFixture /></ThemeProvider>);
