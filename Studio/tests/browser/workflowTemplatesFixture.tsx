import { lazy, Suspense, useMemo, useState } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ShellFrame, getStudioNavigation } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/App";
import { ThemeProvider } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/components/ThemeProvider";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../src/essentials/Elsa.Studio.Workflows/Client/src/styles.css";
import { createNotificationController, type StudioWeaverRegistry, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowTemplatesPage } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowTemplatesPage";
import { builtInPropertyEditors } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/propertyEditors";
import { setDialogs } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/dialogs";
import { setNotifications } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/notifications";
import { ToastHost } from "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/ui/feedback/ToastHost";
import { createEditorContext } from "./activityDuplicateEditorFixture";
import type { WorkflowTemplate } from "../../src/essentials/Elsa.Studio.Workflows/Client/src/workflowTemplates";

const WorkflowEditor = lazy(() => import("../../src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor").then(module => ({ default: module.WorkflowEditor })));
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
const storage = "workflow-template-fixture";
const read = () => JSON.parse(localStorage.getItem(storage) ?? "[]") as WorkflowTemplate[];
const navigation = getStudioNavigation([
  { id: "home", label: "Home", path: "/dashboard", placement: "main", order: 10 },
  { id: "workflows", label: "Workflows", path: "/workflows/definitions", placement: "main", order: 20 }
], { includeThemeBuilder: false });
const panels = new URLSearchParams(location.search).has("bottom-panel") ? [
  { id: "console", title: "Console", component: () => <p>Console stream connected.</p> },
  { id: "structured-logs", title: "Structured Logs", component: () => <p>Structured log stream connected.</p> },
  { id: "opentelemetry", title: "OpenTelemetry", component: () => <p>OpenTelemetry stream connected.</p> }
] : [];
function context(): StudioEndpointContext {
  const base = createEditorContext();
  return { ...base, baseUrl: "workflow-template-fixture", http: {
    ...base.http,
    getJson: async (path: string) => {
      if (path === "/capabilities") {
        const value = await base.http.getJson<{ capabilities: Array<{ id: string; links: object[] }> }>(path);
        value.capabilities.find(item => item.id === "elsa.api.workflow-design")!.links.push(
          { rel: "workflow-templates", href: "design/workflows/templates" },
          { rel: "workflow-template", href: "design/workflows/templates/{templateId}", templated: true },
          { rel: "workflow-template-delete", href: "design/workflows/templates/{templateId}/delete", templated: true });
        return value;
      }
      if (path === "/design/activities/catalog") {
        const value = await base.http.getJson<{ activities: Array<{ activityVersionId: string; containerStructure?: object; authoringTemplate?: object }> }>(path);
        value.activities = value.activities.map(item => ({ ...item, authoringTemplate: { nodeId: "root", activityVersionId: item.activityVersionId, inputs: [], outputs: [] } }));
        return value;
      }
      if (path === "/design/workflows/templates") return read();
      if (path.startsWith("/design/workflows/templates/")) return read().find(item => item.id === path.split("/").at(-1));
      return base.http.getJson(path);
    },
    postJson: async (path: string, body: unknown) => {
      if (path === "/design/workflows/templates") {
        const value = { ...(body as object), id: "template-" + crypto.randomUUID().replaceAll("-", ""), revision: crypto.randomUUID(), createdAt: new Date().toISOString(), lastModifiedAt: new Date().toISOString() } as WorkflowTemplate;
        localStorage.setItem(storage, JSON.stringify([...read(), value])); return value;
      }
      if (path.endsWith("/delete")) { localStorage.setItem(storage, JSON.stringify(read().filter(item => item.id !== path.split("/").at(-2)))); return true; }
      if (path === "/design/workflows/definitions") { localStorage.setItem("created-template-workflow", JSON.stringify(body)); return { definition: { id: "created-workflow" }, draft: null, versions: [] }; }
      return base.http.postJson(path, body);
    },
    requestJson: async (path: string, options: { body?: string }) => {
      if (path.startsWith("/design/workflows/templates/")) {
        const delay = Number(localStorage.getItem("template-fixture-save-delay"));
        if (delay) await new Promise(resolve => setTimeout(resolve, delay));
        if (localStorage.getItem("template-fixture-fail-saves")) throw new Error("Template save unavailable");
        const values = read(), index = values.findIndex(item => item.id === path.split("/").at(-1));
        const body = JSON.parse(options.body ?? "{}");
        if (values[index].revision !== body.expectedRevision) throw new Error("This template changed in another session. Reload it before saving.");
        const value = { ...values[index], ...body, revision: crypto.randomUUID() };
        values[index] = value; localStorage.setItem(storage, JSON.stringify(values)); return value;
      }
      return base.http.requestJson(path, options);
    }
  } } as unknown as StudioEndpointContext;
}

export function WorkflowTemplatesFixture() {
  const backend = useMemo(context, []), [created, setCreated] = useState(false);
  const [notifications] = useState(() => {
    const controller = createNotificationController(); controller.setScope("template-fixture");
    setNotifications(controller.api); return controller;
  });
  const ai = useMemo(() => ({ promptActions: { list: () => [] }, providerAvailability: { get: () => false, subscribe: () => () => undefined }, dispatchPrompt: () => undefined } as unknown as StudioWeaverRegistry), []);
  setDialogs({ confirm: async () => true, alert: async () => undefined, prompt: async () => null });
  const props = { context: backend, ai, propertyEditors: builtInPropertyEditors, expressionEditors: [], runInputEditors: [], testStimulusEditors: [], workflowDesignerPanels: [] };
  const navigate = (path: string) => {
    if (path.startsWith("/workflows/definitions")) { setCreated(true); return; }
    const url = new URL(path, window.location.origin); url.pathname = "/"; url.searchParams.set("mode", "workflow-templates");
    url.searchParams.set("theme", document.documentElement.dataset.themeMode ?? "light");
    if (new URLSearchParams(window.location.search).has("bottom-panel")) url.searchParams.set("bottom-panel", "");
    window.history.pushState({}, "", url); window.dispatchEvent(new PopStateEvent("popstate"));
  };
  return <ThemeProvider><ShellFrame navigation={navigation} panels={panels} path="/workflows/templates" title="Workflow templates" backendBaseUrl="https://backend.example/" onNavigate={() => undefined}>
    <QueryClientProvider client={queryClient}>
      {created ? <><h1>Workflow created</h1><output data-testid="created-document">{localStorage.getItem("created-template-workflow")}</output></> :
        new URLSearchParams(window.location.search).has("source-workflow") ? <Suspense fallback={<p role="status">Loading workflow designer…</p>}><WorkflowEditor {...props} definitionId="definition-1" autosaveEnabledByDefault={false} onBack={() => navigate("/workflows/templates")} /></Suspense> :
          <WorkflowTemplatesPage {...props} navigate={navigate} />}
    </QueryClientProvider>
    <ToastHost controller={notifications} navigate={navigate} />
  </ShellFrame></ThemeProvider>;
}
