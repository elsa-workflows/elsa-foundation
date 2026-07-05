import { useMemo, useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StudioCodeEditor, javaScriptLanguageAdapter, type StudioCodeDocument } from "@elsa-workflows/studio-code-editor";
import {
  StudioDataGrid,
  StatusChip,
  StudioToolbar,
  StudioToolbarGroup,
  StudioTabs,
  StudioAlert,
  type StudioDataGridColumn,
  type StudioStatusTone
} from "@elsa-workflows/studio-ui";
import { register } from "@elsa-workflows/studio-workflows";
import { createStubModuleApi } from "./stubModuleApi";

// A backend is not required for the app to run. When there is no Elsa backend at this URL,
// the workflows page renders its own empty/error state gracefully (see README).
const BACKEND_BASE_URL = "http://localhost:5000";

const queryClient = new QueryClient();

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <div className="example-shell">
        <header className="example-header">
          <h1>Elsa Studio packages — React host</h1>
          <p>
            A plain Vite + React 19 app (not Elsa.Studio.Web) consuming the four published Studio
            packages. Each section below is powered by one of them.
          </p>
        </header>
        <CodeEditorSection />
        <PrimitivesSection />
        <WorkflowsModuleSection />
      </div>
    </QueryClientProvider>
  );
}

/** Section 1 — `@elsa-workflows/studio-code-editor`. */
function CodeEditorSection() {
  const [document, setDocument] = useState<StudioCodeDocument>({
    uri: "elsa://functions/greeting.js",
    language: "javascript",
    value: "function greeting(name) {\n  return `Hello, ${name}!`;\n}\n"
  });

  return (
    <section className="example-section">
      <h2>1. Code editor</h2>
      <p className="example-note">
        <code>StudioCodeEditor</code> with the <code>javaScriptLanguageAdapter</code>. The adapter
        lazy-loads a CodeMirror engine; the fallback textarea renders until it resolves.
      </p>
      <StudioCodeEditor
        document={document}
        languageAdapter={javaScriptLanguageAdapter}
        ariaLabel="JavaScript snippet"
        diagnostics={[
          { severity: "info", message: "Return a string to expose it to the workflow.", startLineNumber: 2 }
        ]}
        onChange={setDocument}
      />
    </section>
  );
}

interface WorkflowRow {
  id: string;
  name: string;
  status: string;
  tone: StudioStatusTone;
}

const rows: WorkflowRow[] = [
  { id: "wf-1", name: "Order fulfilment", status: "Running", tone: "accent" },
  { id: "wf-2", name: "Invoice approval", status: "Completed", tone: "success" },
  { id: "wf-3", name: "Nightly export", status: "Faulted", tone: "danger" },
  { id: "wf-4", name: "Welcome email", status: "Idle", tone: "neutral" }
];

/** Section 2 — `@elsa-workflows/studio-ui` primitives + tokens.css. */
function PrimitivesSection() {
  const [tab, setTab] = useState("all");
  const [selected, setSelected] = useState<string | undefined>("wf-1");

  const columns: StudioDataGridColumn<WorkflowRow>[] = useMemo(
    () => [
      { id: "name", header: "Workflow", render: row => row.name },
      { id: "status", header: "Status", render: row => <StatusChip tone={row.tone}>{row.status}</StatusChip> }
    ],
    []
  );

  const visibleRows = tab === "all" ? rows : rows.filter(row => row.tone === "accent");

  return (
    <section className="example-section">
      <h2>2. UI primitives</h2>
      <p className="example-note">
        <code>Toolbar</code>, <code>Tabs</code>, <code>StatusChip</code> and <code>DataGrid</code>,
        styled via the <code>--studio-*</code> tokens from <code>tokens.css</code>.
      </p>
      <div className="example-panel">
        <StudioToolbar>
          <StudioToolbarGroup>
            <strong>Workflows</strong>
            <StatusChip tone="neutral">{visibleRows.length}</StatusChip>
          </StudioToolbarGroup>
          <StudioToolbarGroup>
            <StatusChip tone="success">Live</StatusChip>
          </StudioToolbarGroup>
        </StudioToolbar>
        <StudioTabs
          ariaLabel="Filter workflows"
          activeTab={tab}
          onSelect={setTab}
          tabs={[
            { id: "all", label: "All" },
            { id: "running", label: "Running" }
          ]}
        />
        <StudioDataGrid
          columns={columns}
          items={visibleRows}
          getKey={row => row.id}
          gridColumns="minmax(160px, 2fr) minmax(120px, 1fr)"
          selectedKey={selected}
          onSelect={row => setSelected(row.id)}
          emptyState="No workflows match this filter."
        />
      </div>
    </section>
  );
}

/** Section 3 — `@elsa-workflows/studio-workflows` module registered against a stub host API. */
function WorkflowsModuleSection() {
  // Build a real (SDK-factory-backed) module API, register the workflows module into it, then
  // pull the first registered route and render its component. `register` is idempotent enough
  // for a demo, but we memoize so it runs once.
  const RegisteredPage = useMemo(() => {
    const api = createStubModuleApi(BACKEND_BASE_URL);
    register(api);
    const route = api.routes.list()[0];
    return route?.component ?? null;
  }, []);

  return (
    <section className="example-section">
      <h2>3. Workflows module</h2>
      <p className="example-note">
        We construct an <code>ElsaStudioModuleApi</code> from SDK factories, call the module's{" "}
        <code>register(api)</code>, then render the first route component it registered. Live data
        needs a running Elsa backend; without one, the page shows its own error/empty state.
      </p>
      <div className="example-panel">
        <StudioAlert tone="info">
          Rendering <code>{"api.routes.list()[0].component"}</code> — the "Workflow runs" page. It
          will attempt to reach the backend at <code>{BACKEND_BASE_URL}</code>.
        </StudioAlert>
        {RegisteredPage ? <RegisteredPage /> : <StudioAlert tone="warning">No route was registered.</StudioAlert>}
      </div>
    </section>
  );
}
