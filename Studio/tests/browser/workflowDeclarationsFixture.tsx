import { useMemo, useState, type CSSProperties } from "react";
import { createRoot } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { writeAppearancePreferences } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/themes/appearance";
import { ScopedVariablesEditor, WorkflowDataView, type WorkflowDataSection } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/WorkflowPropertiesView";
import type { VariableDefinition, WorkflowDraft } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTypes";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";

const params = new URLSearchParams(window.location.search);
const offline = params.get("catalog") === "offline";
const alias = offline ? "Legacy.Json" : "System.Text.Json.JsonElement";
const driver = offline ? "old" : "elsa.json";
const variable: VariableDefinition = { referenceKey: "payload", name: "ReviewData", type: { alias, collectionKind: "Single" }, storageDriverType: driver, default: { value: '{"approved":true}', expressionType: "Object" } };
writeAppearancePreferences({ themeId: "classic", mode: params.get("theme") === "dark" ? "dark" : "light", navigation: "left" });

function Fixture() {
  const [epoch, setEpoch] = useState(0);
  const [draft, setDraft] = useState<WorkflowDraft>({ id: "declaration-fixture", definitionId: "declaration-fixture", layout: [], validationErrors: [], state: {
    inputs: [{ ...variable, isRequired: false }], outputs: [variable], variables: [variable]
  } });
  const [scoped, setScoped] = useState<unknown[]>([variable]);
  const context = useMemo(() => ({ baseUrl: `declaration-fixture-${offline}`, http: { getJson: async (raw: string) => {
    if (offline) throw new Error("Owned fixture catalogue unavailable");
    const path = raw.replace(/^\//, "");
    if (path === "capabilities") return { capabilities: [
      { id: "elsa.api.expressions", contractVersion: "1", links: [{ rel: "variable-types", href: "types" }] },
      { id: "elsa.api.activity-design", contractVersion: "1", links: [{ rel: "activity-authoring-capabilities", href: "activity-options" }] }
    ] };
    if (path === "types") return [
      { alias, displayName: "JSON", defaultEditor: "json", category: "Portable" },
      { alias: "String", displayName: "String", defaultEditor: "text", category: "Primitives" }
    ];
    if (path === "activity-options") return { storageDriverKeys: [driver], types: [
      { alias, supportsDurability: true, compatibleStorageDriverKeys: [driver] },
      { alias: "String", supportsDurability: true, compatibleStorageDriverKeys: [driver] }
    ] };
    throw new Error(`Unexpected owned fixture read ${path}`);
  } } }) as unknown as StudioEndpointContext, []);
  const pane: CSSProperties = { width: Number(params.get("paneWidth") ?? 244), maxWidth: "100%", height: 450, overflow: "auto" };
  return <ThemeProvider><main className="wf-tokens" style={{ height: "100dvh", overflow: "auto", padding: 16 }}>
    <h1>Isolated declaration editors</h1>
    <p>Owned mocked descriptors and in-memory draft; no backend persistence claim.</p>
    <button onClick={() => setEpoch(value => value + 1)}>Remount fixture editors</button>
    {(["inputs", "outputs", "variables"] as WorkflowDataSection[]).map(section => <section key={section} aria-label={`Workflow ${section}`} style={pane}>
      <WorkflowDataView key={epoch} section={section} draft={draft} context={context} onStateChange={producer => setDraft(current => ({ ...current, state: producer(current.state) }))} />
    </section>)}
    <section aria-label="Scoped variables" style={pane}><fieldset disabled={params.has("readOnly")} className="wf-container-variables" style={{ minWidth: 0 }}>
      <ScopedVariablesEditor key={epoch} context={context} variables={scoped} onChange={setScoped} />
    </fieldset></section>
    <output aria-label="Fixture values" style={{ display: "block", overflowWrap: "anywhere" }}>{JSON.stringify({ state: draft.state, scoped })}</output>
  </main></ThemeProvider>;
}
createRoot(document.getElementById("root")!).render(<Fixture />);
