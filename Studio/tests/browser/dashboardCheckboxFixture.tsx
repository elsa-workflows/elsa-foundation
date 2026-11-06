import React, { useState } from "react";
import { createRoot } from "react-dom/client";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import { QueryControls } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/QueryControls";
import { TileOptionsDialog } from "../../src/essentials/Elsa.Studio.Dashboard/Client/src/native/ResourceDialogs";
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
createRoot(document.getElementById("root")!).render(<ThemeProvider><Fixture /></ThemeProvider>);
