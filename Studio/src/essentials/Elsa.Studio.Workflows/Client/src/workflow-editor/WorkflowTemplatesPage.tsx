import { builtInWorkflowTemplates } from "../builtInWorkflowTemplates";
import "./WorkflowTemplatesPage.css";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import React, { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ComponentProps } from "react";
import { Plus, RefreshCw, Copy, Trash2 } from "lucide-react";
import { listActivities } from "../api/activityDesign";
import { createDefinition } from "../api/workflowDesign";
import type { ActivityCatalogItem } from "../workflowTypes";
import { deleteWorkflowTemplate, listWorkflowTemplates, saveWorkflowTemplate, templateAvailabilityReason, type WorkflowTemplate } from "../workflowTemplates";
import { getCreateInitialState } from "./editorHelpers";
import type { CreateWorkflowDraft } from "./editorTypes";
import { getHttpStarterAvailabilityReason } from "./httpWorkflowStarter";
import { createTemplateDocumentAdapter } from "./templateDocumentAdapter";
import { getDialogs } from "./dialogs";

const WorkflowEditor = lazy(() => import("./WorkflowEditor").then(module => ({ default: module.WorkflowEditor })));
const CreateWorkflowDialog = lazy(() => import("./CreateWorkflowDialog").then(module => ({ default: module.CreateWorkflowDialog })));
type EditorProps = ComponentProps<typeof import("./WorkflowEditor")["WorkflowEditor"]>;
type Props = Pick<EditorProps, "context" | "ai" | "propertyEditors" | "expressionEditors" | "runInputEditors" | "testStimulusEditors" | "workflowDesignerPanels"> & { navigate(path: string): void };

function TemplateDesigner({ id, onBack, ...props }: Props & { id: string; onBack(): void }) {
  const adapter = useMemo(createTemplateDocumentAdapter, []);
  return <WorkflowEditor {...props} definitionId={id} documentAdapter={adapter} onBack={onBack} />;
}

export function WorkflowTemplatesPage(props: Props) {
  const { context, ai, navigate } = props;
  const [templates, setTemplates] = useState<WorkflowTemplate[]>([]), [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [supported, setSupported] = useState(false), [loading, setLoading] = useState(true), [busy, setBusy] = useState(false);
  const [error, setError] = useState(""), [search, setSearch] = useState("");
  const [templateId, setTemplateId] = useState(() => new URLSearchParams(window.location.search).get("template"));
  const [dialog, setDialog] = useState<{ kind: "template" | "workflow"; draft: CreateWorkflowDraft } | null>(null);
  const generation = useRef(0);
  const reload = useCallback(async () => {
    const request = ++generation.current;
    setLoading(true); setError("");
    try {
      const [values, activities] = await Promise.all([listWorkflowTemplates(context), listActivities(context)]);
      if (request !== generation.current) return;
      setSupported(values !== null); setTemplates(values ?? []); setCatalog(activities.activities ?? []);
    } catch (error) { if (request === generation.current) setError(error instanceof Error ? error.message : String(error)); }
    finally { if (request === generation.current) setLoading(false); }
  }, [context]);
  useEffect(() => { setTemplates([]); setSupported(false); setCatalog([]); void reload(); return () => { generation.current++; }; }, [reload]);
  useEffect(() => { const sync = () => setTemplateId(new URLSearchParams(window.location.search).get("template")); window.addEventListener("popstate", sync); return () => window.removeEventListener("popstate", sync); }, []);
  const open = (id: string | null) => { setTemplateId(id); navigate(`/workflows/templates${id ? `?template=${encodeURIComponent(id)}` : ""}`); if (!id) void reload(); };
  const begin = (kind: "template" | "workflow", template?: WorkflowTemplate, starter?: "http") => {
    setError(""); setDialog({ kind, draft: { name: template ? `${template.name}${kind === "template" ? " copy" : ""}` : starter ? "HTTP request and response" : "", description: template?.description ?? "", rootActivityVersionId: null, starter,
      ...(template ? { templateId: template.id, templateContent: structuredClone(template.content) } : {}) } });
  };
  const submit = async () => {
    if (!dialog || busy) return;
    setBusy(true); setError("");
    try {
      const content = dialog.draft.templateContent ?? { state: getCreateInitialState(dialog.draft, catalog)!, layout: [], activityPresentation: [] };
      if (!content.state?.rootActivity) throw new Error("Choose a starting root activity.");
      if (dialog.kind === "workflow") {
        const created = await createDefinition(context, { name: dialog.draft.name.trim(), description: dialog.draft.description,
          initialState: content.state, layout: content.layout, activityPresentation: content.activityPresentation });
        setDialog(null); navigate(`/workflows/definitions?definition=${encodeURIComponent(created.definition.id)}`);
      } else {
        const saved = await saveWorkflowTemplate(context, { name: dialog.draft.name.trim(), description: dialog.draft.description, content });
        setDialog(null); open(saved.id);
      }
    } catch (error) { setError(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(false); }
  };
  const remove = async (template: WorkflowTemplate) => {
    if (!await getDialogs().confirm({ title: "Delete template", message: `Delete template “${template.name}”? Workflows already created from it will remain unchanged.`, confirmLabel: "Delete", tone: "danger" })) return;
    setBusy(true); setError("");
    try { await deleteWorkflowTemplate(context, template); await reload(); }
    catch (error) { setError(error instanceof Error ? error.message : String(error)); }
    finally { setBusy(false); }
  };
  if (templateId) return <section className="wf-page"><Suspense fallback={<p role="status">Loading template designer…</p>}><TemplateDesigner key={`${context.baseUrl}:${templateId}`} {...props} id={templateId} onBack={() => open(null)} /></Suspense></section>;
  return <section className="wf-page wf-templates-page">
    <header className="wf-page-header"><div><h1>Workflow templates</h1><p>Create reusable starting points. Workflows created from a template are independent copies.</p></div><div className="wf-actions">
      <button type="button" className="wf-primary-button" disabled={loading || !supported} onClick={() => begin("template")}><Plus size={15} /> New template</button>
      <button type="button" disabled={loading || busy} onClick={() => void reload()}><RefreshCw size={15} /> Refresh</button>
    </div></header>
    <label className="wf-form-field wf-template-search">Search templates<input type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Name or description" /></label>
    {loading ? <p role="status">Loading templates…</p> : null}
    {!loading && !supported ? <p role="status">Saved template management is unavailable on this server. Built-in starters are still available when creating workflows.</p> : null}
    {error && !dialog ? <p className="wf-alert" role="alert">{error}</p> : null}
    <div className="wf-template-grid">
      {builtInWorkflowTemplates.filter(item => `${item.name} ${item.description}`.toLowerCase().includes(search.toLowerCase())).map(item => {
        const reason = item.id === "http" ? getHttpStarterAvailabilityReason(catalog) : undefined;
        return <article key={item.id} className="wf-template-card"><span className="wf-chip">Built-in · read only</span><h2>{item.name}</h2><p>{item.description}</p>{reason ? <p className="wf-muted">{reason}</p> : null}
          <div className="wf-template-actions wf-actions"><button type="button" disabled={loading || !!reason} onClick={() => begin("workflow", undefined, item.id === "http" ? "http" : undefined)}>Use template</button><button type="button" disabled={loading || !supported || !!reason} onClick={() => begin("template", undefined, item.id === "http" ? "http" : undefined)}><Copy size={14} /> Copy</button></div>
        </article>;
      })}
      {templates.filter(item => `${item.name} ${item.description ?? ""}`.toLowerCase().includes(search.toLowerCase())).map(template => {
        const reason = templateAvailabilityReason(template, catalog);
        return <article key={template.id} className="wf-template-card"><span className="wf-chip">Custom</span><h2><button className="wf-link-button" onClick={() => open(template.id)}>{template.name}</button></h2><p>{template.description || "No description"}</p>{reason ? <p role="status" className="wf-muted">{reason}</p> : null}
          <div className="wf-template-actions wf-actions"><button type="button" disabled={loading || !!reason} onClick={() => begin("workflow", template)}>Use template</button><button type="button" disabled={busy} onClick={() => open(template.id)}>Edit</button><button type="button" disabled={busy} onClick={() => begin("template", template)}><Copy size={14} /> Copy</button><button type="button" disabled={busy} onClick={() => void remove(template)}><Trash2 size={14} /> Delete</button></div>
        </article>;
      })}
    </div>
    {!loading && supported && !templates.length ? <p className="wf-muted">No custom templates yet. Create one here or choose “Save as template” in a workflow.</p> : null}
    {dialog ? <Suspense fallback={<p role="status">Loading template dialog…</p>}><CreateWorkflowDialog context={context} title={dialog.kind === "template" ? "Create template" : "Create Workflow"} submitLabel={dialog.kind === "template" ? "Create template" : "Create"} draft={dialog.draft} catalog={catalog} creating={busy} submitError={error} ai={ai} onChange={draft => setDialog({ ...dialog, draft })} onClose={() => setDialog(null)} onSubmit={() => void submit()} /></Suspense> : null}
  </section>;
}


export function WorkflowTemplatesRoute({ api, navigate }: { api: ElsaStudioModuleApi; navigate(path: string): void }) {
  return <WorkflowTemplatesPage key={`${api.backend.baseUrl}:${api.runtime.identity?.tenantId}:${api.runtime.identity?.subject}`} context={api.backend} ai={api.ai} propertyEditors={api.propertyEditors.list()} expressionEditors={api.expressionEditors.list()}
    runInputEditors={api.workflowRunInputEditors?.list() ?? []} testStimulusEditors={api.workflowTestStimulusEditors?.list() ?? []}
    workflowDesignerPanels={api.workflowDesigner.panels.list()} navigate={navigate} />;
}
