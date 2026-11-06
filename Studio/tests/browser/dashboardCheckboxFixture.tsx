import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { StudioButton, StudioPage, StudioPageHeader } from "@elsa-workflows/studio-ui";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { QueryControls } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/QueryControls";
import { ShareDialog, TileOptionsDialog } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/ResourceDialogs";
import type { DashboardApi } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/api";
import { initialDefinition, type DashboardResource, type DashboardTile, type DatasetDeclaration } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/types";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/native.css";

const dataset: DatasetDeclaration = {
  id: "checkbox-runs", ownerModuleId: "Checkbox fixture", schemaRevision: "1", title: "Retained workflow runs", description: "Rendered native grouping and parameter controls.",
  fields: [
    { id: "definitionId", label: "Workflow definition", type: "string", filterOperators: ["eq"], nullable: false, groupable: true, sortable: true, projectable: true },
    { id: "status", label: "Current status", type: "string", filterOperators: ["eq"], nullable: false, groupable: true, sortable: true, projectable: true },
    { id: "runKind", label: "Run kind", type: "string", filterOperators: ["eq"], nullable: false, groupable: true, sortable: true, projectable: true }
  ],
  metrics: [{ id: "started", revision: "1", label: "Started runs", unit: "runs", description: "Number of selected runs." }]
};
const parameters = [
  { name: "category", type: "string", required: false, nullable: false, default: { type: "string", literal: "retail" } },
  { name: "minimum", type: "integer", required: false, nullable: false, default: { type: "integer", literal: "1" } }
];
function Fixture() {
  const [definition, setDefinition] = useState(() => ({ ...initialDefinition(dataset), query: { ...initialDefinition(dataset).query, parameters } }));
  const [showTile, setShowTile] = useState(false);
  const [tile, setTile] = useState<DashboardTile>({ id: "tile", questionId: "question", size: "medium", referenceState: "available" });
  const question: DashboardResource = { id: "question", kind: "question", revision: "1", name: "Runs", description: "", ownerId: "viewer", definition,
    capabilities: { canRead: true, canEdit: true, canCopy: true, canDelete: true, canShare: true }, createdAt: "", updatedAt: "" };
  const api = { get: async () => question } as unknown as DashboardApi;
  return <main className="named-workspace" style={{ padding: 24 }}>
    <QueryControls definition={definition} datasets={[dataset]} disabled={false} onChange={setDefinition} />
    <output aria-label="Selected grouping fields">{definition.query.groupBy.join(", ")}</output>
    <button type="button" onClick={() => setShowTile(true)}>Open tile settings</button>
    <output aria-label="Tile parameter overrides">{JSON.stringify(tile.parameters ?? {})}</output>
    {showTile ? <TileOptionsDialog api={api} tile={tile} onClose={() => setShowTile(false)} onApply={value => { setTile(value); setShowTile(false); }} /> : null}
  </main>;
}
function LongNameFixture() {
  const [showShare, setShowShare] = useState(false);
  const params = new URLSearchParams(window.location.search);
  const name = params.has("shortName") ? "Operations" : `Metric${"A".repeat(164)}`;
  const dashboard: DashboardResource = { id: "dashboard", kind: "dashboard", revision: "1", name, description: "A legal dashboard name", ownerId: "viewer", tiles: [],
    capabilities: { canRead: true, canEdit: true, canCopy: true, canDelete: true, canShare: true }, createdAt: "", updatedAt: "" };
  const api = { get: async () => dashboard, audience: async () => ({ items: [] }), candidates: async () => ({ items: [] }), share: async () => dashboard } as unknown as DashboardApi;
  return <main style={{ padding: 24 }}><StudioPage className="named-workspace"><div className="named-editor-content">
    <StudioPageHeader headingLevel={2} title={name} description="Revision 1" actions={<>
      <StudioButton variant="primary">Save layout</StudioButton><StudioButton>Copy saved version</StudioButton>
      <StudioButton onClick={() => setShowShare(true)}>Share</StudioButton><StudioButton>History</StudioButton><StudioButton>Back to dashboards</StudioButton>
    </>} />
    <div className="named-definition-meta"><label>Name<input value={name} readOnly /></label><label>Description<textarea value={dashboard.description} readOnly /></label></div>
    {showShare ? <ShareDialog api={api} resource={dashboard} onClose={() => setShowShare(false)} onSaved={() => setShowShare(false)} /> : null}
  </div></StudioPage></main>;
}
const params = new URLSearchParams(window.location.search);
createRoot(document.getElementById("root")!).render(<ThemeProvider>{params.has("longName") ? <LongNameFixture /> : <Fixture />}</ThemeProvider>);
