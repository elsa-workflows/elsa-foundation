import { OverflowMenu, StudioPageHeader, StudioButton, StudioSearchInput } from "@elsa-workflows/studio-ui";
import { builtInWorkflowTemplates } from "../builtInWorkflowTemplates";
import "./WorkflowTemplatesPage.css";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import React, { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ComponentProps } from "react";
import { Plus, RefreshCw, Copy } from "lucide-react";
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
  return <section className="studio-page wf-page wf-templates-page">
    <StudioPageHeader title="Workflow templates" description="Create reusable starting points. Workflows created from a template are independent copies." actions={<>
      <StudioButton variant="primary" disabled={loading || !supported} onClick={() => begin("template")}><Plus size={15} /> New template</StudioButton>
      <StudioButton disabled={loading || busy} onClick={() => void reload()}><RefreshCw size={15} /> Refresh</StudioButton>
    </>} />
    <div className="studio-collection-toolbar"><StudioSearchInput aria-label="Search templates" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search templates" /></div>
    {loading ? <p role="status">Loading templates…</p> : null}
    {!loading && !supported ? <p role="status">Saved template management is unavailable on this server. Built-in starters are still available when creating workflows.</p> : null}
    {error && !dialog ? <p className="wf-alert" role="alert">{error}</p> : null}
    <section className="wf-template-section" aria-labelledby="builtin-templates-title">
      <h2 id="builtin-templates-title">Built-in starters</h2>
      <div className="wf-template-grid">
      {builtInWorkflowTemplates.filter(item => `${item.name} ${item.description}`.toLowerCase().includes(search.toLowerCase())).map(item => {
        const reason = item.id === "http" ? getHttpStarterAvailabilityReason(catalog) : undefined;
        return <article key={item.id} className="wf-template-card"><span className="wf-chip">Built-in · read only</span><h3>{item.name}</h3><p>{item.description}</p>{reason ? <p className="wf-muted">{reason}</p> : null}
          <div className="wf-template-preview" aria-hidden="true">{item.id === "http" ? <><span>HTTP endpoint</span><i>→</i><span>HTTP response</span></> : <span className="wf-template-preview-empty"><Plus size={18} /> Choose a root activity</span>}</div>
          <div className="wf-template-actions wf-actions"><StudioButton variant="primary" disabled={loading || !!reason} onClick={() => begin("workflow", undefined, item.id === "http" ? "http" : undefined)}>Use template</StudioButton><StudioButton disabled={loading || !supported || !!reason} onClick={() => begin("template", undefined, item.id === "http" ? "http" : undefined)}><Copy size={14} /> Copy</StudioButton></div>
        </article>;
      })}
      </div>
    </section>
    <section className="wf-template-section" aria-labelledby="custom-templates-title">
      <h2 id="custom-templates-title">Your templates</h2>
      <div className="wf-template-grid">
      {templates.filter(item => `${item.name} ${item.description ?? ""}`.toLowerCase().includes(search.toLowerCase())).map(template => {
        const reason = templateAvailabilityReason(template, catalog);
        const root = template.content.state.rootActivity;
        const rootName = root ? template.content.activityPresentation?.find(item => item.nodeId === root.nodeId)?.displayName ?? catalog.find(item => item.activityVersionId === root.activityVersionId)?.displayName ?? "Root activity" : "No root activity";
        return <article key={template.id} className="wf-template-card"><span className="wf-chip">Custom</span><h3><button className="wf-link-button" onClick={() => open(template.id)}>{template.name}</button></h3><p>{template.description || "No description"}</p>{reason ? <p role="status" className="wf-muted">{reason}</p> : null}
          <div className="wf-template-preview" aria-hidden="true"><span>{rootName}</span></div>
          <div className="wf-template-actions wf-actions"><StudioButton variant="primary" disabled={loading || !!reason} onClick={() => begin("workflow", template)}>Use template</StudioButton><StudioButton disabled={busy} onClick={() => open(template.id)}>Edit</StudioButton><OverflowMenu ariaLabel={`Actions for ${template.name}`} items={[
            { id: "copy", label: "Copy", disabled: busy, onSelect: () => begin("template", template) },
            { id: "delete", label: "Delete", destructive: true, disabled: busy, onSelect: () => void remove(template) }
          ]} /></div>
        </article>;
      })}
      {supported ? <button type="button" className="wf-template-add" disabled={loading || busy} onClick={() => begin("template")}><Plus size={22} /> Create a template</button> : null}
      </div>
    </section>
    {!loading && supported && !templates.length ? <p className="wf-muted">No custom templates yet. Create one here or choose “Save as template” in a workflow.</p> : null}
    {dialog ? <Suspense fallback={<p role="status">Loading template dialog…</p>}><CreateWorkflowDialog context={context} title={dialog.kind === "template" ? "Create template" : "Create Workflow"} submitLabel={dialog.kind === "template" ? "Create template" : "Create"} draft={dialog.draft} catalog={catalog} creating={busy} submitError={error} ai={ai} onChange={draft => setDialog({ ...dialog, draft })} onClose={() => setDialog(null)} onSubmit={() => void submit()} /></Suspense> : null}
  </section>;
}


export function WorkflowTemplatesRoute({ api, navigate }: { api: ElsaStudioModuleApi; navigate(path: string): void }) {
  return <WorkflowTemplatesPage key={`${api.backend.baseUrl}:${api.runtime.identity?.tenantId}:${api.runtime.identity?.subject}`} context={api.backend} ai={api.agent} propertyEditors={api.propertyEditors.list()} expressionEditors={api.expressionEditors.list()}
    runInputEditors={api.workflowRunInputEditors?.list() ?? []} testStimulusEditors={api.workflowTestStimulusEditors?.list() ?? []}
    workflowDesignerPanels={api.workflowDesigner.panels.list()} navigate={navigate} />;
}
