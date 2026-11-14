import { BasicTaskForm } from "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/TaskForm";
import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { StudioField, StudioFormField, StudioFieldAdornmentProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/forms/Field";
import { applyThemeStyleVariables } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { getTheme, getThemeColorScheme, getThemeModeDefinition, isThemeMode, themeTokenNames } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/presets";
import { applyWorkspaceStyle, isWorkspaceStyle } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/workspaceStyles";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../Extensions/Agent/studio/Elsa.Studio.Agent/Client/src/styles.css";
import "../../../Extensions/UserTasks/studio/Elsa.Studio.UserTasks/Client/src/styles.css";
import "../../../Extensions/Secrets/studio/Elsa.Studio.Secrets/Client/src/styles.css";
import "../../../Extensions/FeatureManagement/studio/Elsa.Studio.FeatureManagement/Client/src/styles.css";
import "../../../Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/src/styles.css";
import "../../../Extensions/Connections/studio/Elsa.Studio.Connections/Client/src/styles.css";
import "../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/styles.css";

const params = new URLSearchParams(location.search);
const theme = getTheme(params.get("theme") ?? "classic")!;
const modeParam = params.get("mode"); const mode = isThemeMode(modeParam) ? modeParam : "light";
const styleParam = params.get("style"); const style = isWorkspaceStyle(styleParam) ? styleParam : null;
const root = document.documentElement;
root.dataset.theme = theme.id; root.dataset.themeMode = getThemeColorScheme(mode); root.dataset.themeAppearance = mode;
root.dataset.themeLayout = applyWorkspaceStyle(theme, style).layout ?? "classic";
if (style) root.dataset.workspaceStyle = style;
root.dir = params.get("rtl") ? "rtl" : "ltr";
applyThemeStyleVariables(root, applyWorkspaceStyle(theme, style));
const palette = getThemeModeDefinition(theme, mode)!;
for (const token of themeTokenNames) root.style.setProperty(`--${token.replace(/([A-Z])/g, "-$1").toLowerCase()}`, palette[token]);

function OwnedEditor() {
  return <StudioFormField label="Host actions" hint="Actions stay beside the value, above help." trailing={<button type="button" aria-label="Editor action">More</button>}>
    {attributes => <input {...attributes} defaultValue="Opaque editor value" />}
  </StudioFormField>;
}

function Fixture() {
  const [value, setValue] = useState("Draft value");
  const [invalid, setInvalid] = useState(true); const [revealed, setRevealed] = useState(false);
  return <main style={{ maxWidth: 1020, margin: "auto", padding: 20 }}>
    <h1>Outlined fields</h1><p>{theme.name} · {mode} · {style ?? "preset"}</p>
    <section aria-label="Field states" style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit,minmax(min(100%,280px),1fr))", gap: 18 }}>
      <StudioField label="Empty"><input /></StudioField>
      <StudioField label="Populated" description="Drafts retain their value and focus."><input value={value} onChange={event => setValue(event.target.value)} /></StudioField>
      <StudioField label="Focused"><input defaultValue="Keyboard focus" /></StudioField>
      <StudioField label="Invalid" error={invalid ? "Enter a valid address" : undefined}><input defaultValue="draft" onChange={() => setInvalid(false)} /></StudioField>
      <StudioField label="Disabled" description="Unavailable in this context."><input defaultValue="Disabled value" disabled /></StudioField>
      <StudioField label="Read only"><input defaultValue="Readable and copyable" readOnly /></StudioField>
      <StudioField label="Select"><select defaultValue="text"><option value="text">Text</option><option value="json">JSON</option></select></StudioField>
      <StudioField label="Description"><textarea rows={3} defaultValue="Multiline content" /></StudioField>
      <StudioFormField label="Secret value" hint="Visibility does not change the stored value." trailing={<button type="button" aria-label={revealed ? "Hide value" : "Show value"} onClick={() => setRevealed(!revealed)}>{revealed ? "Hide" : "Show"}</button>}>
        {attributes => <input {...attributes} type={revealed ? "text" : "password"} defaultValue="protected draft" />}
      </StudioFormField>
      <StudioField label="Calculated version"><output>7</output></StudioField>
      <StudioField label="A long translated label that must wrap without clipping the outline or the value" meta="Optional"><input defaultValue="Long label value" /></StudioField>
      <StudioFieldAdornmentProvider trailing={<button type="button" aria-label="Host action">Syntax</button>}><OwnedEditor /></StudioFieldAdornmentProvider>
      <StudioField label="Options"><label><input type="checkbox" defaultChecked /> Enabled</label><label><input type="radio" name="option" defaultChecked /> Default</label></StudioField>
      <div className="agent-editor"><StudioField label="Agent setting"><input defaultValue="Module style collision" /></StudioField></div>
      <div className="user-task-detail"><StudioField label="Task response"><input defaultValue="User Tasks collision" /></StudioField></div>
      <div className="decisions-page"><StudioField label="Decision value"><input disabled defaultValue="Decision collision" /></StudioField></div>
      <div className="connections-page"><StudioField label="Connection setting"><input defaultValue="Connection collision" /></StudioField></div>
      <div className="feature-management-setting"><StudioField label="Feature setting"><input type="text" defaultValue="Settings collision" /></StudioField></div>
      <div className="etl-editor-field"><StudioField label="ETL setting"><input defaultValue="ETL collision" /></StudioField></div>
      <div style={{ background: "repeating-linear-gradient(45deg,transparent,transparent 6px,#8882 6px,#8882 12px)", padding: 12, "--studio-field-surface": "transparent" } as React.CSSProperties}>
        <StudioField label="Transparent surface"><input defaultValue="Real legend gap" /></StudioField>
      </div>
    </section>
    <section aria-label="Completed response" className="user-task-detail" style={{ marginTop: 20 }}>
      <h2>Completed response</h2>
      <div className="user-task-response" data-final>
        <BasicTaskForm form={{ fields: [{ key: "Note", label: "Note", kind: "multiline", required: true }] }} value={{ Note: "Reviewed purchase request" }} readOnly onChange={() => {}} />
      </div>
    </section>
  </main>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
