import { createElement } from "react";
import { createRoot } from "react-dom/client";
import { StudioCodeEditor, javaScriptLanguageAdapter, type StudioCodeDocument } from "@elsa-workflows/studio-code-editor";
import { StatusChip, StudioToolbar, StudioToolbarGroup, StudioDataGrid } from "@elsa-workflows/studio-ui";

// Public styles first; the consuming host can then override semantic tokens.
import "@elsa-workflows/studio-ui/styles.css";
import "@elsa-workflows/studio-code-editor/style.css";
import "./theme.css";
import "./page.css";

function mount(id: string, element: ReturnType<typeof createElement>) {
  const host = document.getElementById(id);
  if (!host) throw new Error(`Missing mount point #${id}`);
  createRoot(host).render(element);
}

// --- Code editor ---------------------------------------------------------------------------
const document0: StudioCodeDocument = {
  uri: "elsa://functions/total.js",
  language: "javascript",
  value: "function total(items) {\n  return items.reduce((sum, i) => sum + i.price, 0);\n}\n"
};

mount(
  "code-editor-mount",
  createElement(StudioCodeEditor, {
    document: document0,
    languageAdapter: javaScriptLanguageAdapter,
    ariaLabel: "JavaScript snippet",
    // A no-op onChange keeps the demo stateless; a real host would persist the document.
    onChange: () => {}
  })
);

// --- Toolbar with a status chip ------------------------------------------------------------
mount(
  "toolbar-mount",
  createElement(
    StudioToolbar,
    null,
    createElement(
      StudioToolbarGroup,
      null,
      createElement("strong", null, "Deployment"),
      // StatusChip declares `children` as a REQUIRED prop. createElement only accepts
      // rest-argument children when the props argument is null/optional-children, so a
      // non-null props object must carry required children itself.
      createElement(StatusChip, { tone: "success", children: "Healthy" })
    ),
    createElement(
      StudioToolbarGroup,
      null,
      createElement(StatusChip, { tone: "warning", children: "1 warning" })
    )
  )
);

// --- A small data grid ---------------------------------------------------------------------
interface ServiceRow {
  id: string;
  name: string;
  state: string;
}

const services: ServiceRow[] = [
  { id: "api", name: "API", state: "Running" },
  { id: "worker", name: "Worker", state: "Running" },
  { id: "scheduler", name: "Scheduler", state: "Stopped" }
];

mount(
  "grid-mount",
  createElement(StudioDataGrid<ServiceRow>, {
    items: services,
    getKey: row => row.id,
    gridColumns: "minmax(160px, 2fr) minmax(120px, 1fr)",
    columns: [
      { id: "name", header: "Service", render: row => row.name },
      {
        id: "state",
        header: "State",
        render: row =>
          createElement(StatusChip, { tone: row.state === "Running" ? "success" : "neutral", children: row.state })
      }
    ]
  })
);
