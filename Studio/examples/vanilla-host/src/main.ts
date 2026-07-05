import { createElement } from "react";
import { createRoot } from "react-dom/client";
import { StudioCodeEditor, javaScriptLanguageAdapter, type StudioCodeDocument } from "@elsa-workflows/studio-code-editor";
import { StatusChip, StudioToolbar, StudioToolbarGroup, StudioDataGrid } from "@elsa-workflows/studio-ui";

/*
 * NO JSX in this file — every widget is created with React.createElement. The page itself
 * (index.html) has zero React knowledge; React is just how these particular widgets happen to
 * render. Style imports mirror the react-host example:
 *
 *   External consumers write:
 *     import "@elsa-workflows/studio-ui/tokens.css";
 *   In-repo the published subpath does not resolve, so we reach the source directly.
 */
import "./theme.css";
// External consumers: import "@elsa-workflows/studio-ui/tokens.css";
import "../../../src/Elsa.Studio.Web/Client/src/app/ui/tokens.css";
import "./studio-components.css";
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
