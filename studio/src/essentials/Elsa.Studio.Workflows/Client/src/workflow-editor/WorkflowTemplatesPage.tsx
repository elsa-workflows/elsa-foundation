import { OverflowMenu, StudioPageHeader, StudioButton, StudioSearchInput } from "@elsa-workflows/studio-ui";
import { builtInWorkflowTemplates } from "../builtInWorkflowTemplates";
import "./WorkflowTemplatesPage.css";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import React, { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { ComponentProps } from "react";
import { ChevronDown, Plus, Copy } from "lucide-react";
import { listActivities } from "../api/activityDesign";
import { createDefinition } from "../api/workflowDesign";
import type { ActivityCatalogItem } from "../workflowTypes";
import { deleteWorkflowTemplate, listWorkflowTemplates, saveWorkflowTemplate, templateAvailabilityReason, type WorkflowTemplate } from "../workflowTemplates";
import { getCreateInitialState } from "./createWorkflowInitialDocument";
import type { CreateWorkflowDraft } from "./editorTypes";
import { getHttpStarterAvailabilityReason } from "./httpWorkflowStarter";
import { createTemplateDocumentAdapter } from "./templateDocumentAdapter";
import { getDialogs } from "./dialogs";
import { applyCreateDialogPrefill, createDialogAttachment, createDialogKind, templateListAttachment, templateListKind } from "./workflowLibraryWeaver";
import { useWorkflowPageWeaver } from "./useWorkflowLibraryWeaver";
import { WorkflowLazyBoundary } from "../WorkflowLazyBoundary";

const WorkflowEditor = lazy(() => import("./WorkflowEditor").then(module => ({ default: module.WorkflowEditor })));
const CreateWorkflowDialog = lazy(() => import("./CreateWorkflowDialog").then(module => ({ default: module.CreateWorkflowDialog })));
type EditorProps = ComponentProps<typeof import("./WorkflowEditor")["WorkflowEditor"]>;
type Props = Pick<EditorProps, "context" | "ai" | "weaver" | "propertyEditors" | "expressionEditors" | "runInputEditors" | "testStimulusEditors" | "workflowDesignerPanels"> & { navigate(path: string): void };

function TemplateDesigner({ id, onBack, ...props }: Props & { id: string; onBack(): void }) {
  const adapter = useMemo(createTemplateDocumentAdapter, []);
  return <WorkflowEditor {...props} definitionId={id} documentAdapter={adapter} onBack={onBack} />;
}

export function WorkflowTemplatesPage(props: Props) {
  const { context, ai, navigate } = props;
  const [templates, setTemplates] = useState<WorkflowTemplate[]>([]), [catalog, setCatalog] = useState<ActivityCatalogItem[]>([]);
  const [supported, setSupported] = useState(false), [loading, setLoading] = useState(true), [busy, setBusy] = useState(false);
  const [error, setError] = useState(""), [search, setSearch] = useState("");
  const [starterDisclosureOverride, setStarterDisclosureOverride] = useState<boolean | null>(null);
  const [templateId, setTemplateId] = useState(() => new URLSearchParams(window.location.search).get("template"));
  const [dialog, setDialog] = useState<{ kind: "template" | "workflow"; draft: CreateWorkflowDraft } | null>(null);
  const generation = useRef(0);
  const query = search.trim().toLowerCase();
  const filteredTemplates = useMemo(() => templates.filter(item => `${item.name} ${item.description ?? ""}`.toLowerCase().includes(query)), [query, templates]);
  const starterDisclosureDefaultOpen = !loading && supported && !templates.length && !query;
  const starterDisclosureOpen = starterDisclosureOverride ?? starterDisclosureDefaultOpen;
  // Spec 1193 FR-024: the visible template list as Weaver context, by template identity and persisted revision only.
  const listed = useMemo(() => filteredTemplates.map(item => ({ id: item.id, name: item.name, description: item.description, revision: item.revision })), [filteredTemplates]);
  const listGeneration = useRef(globalThis.crypto?.randomUUID?.() ?? `generation-${Date.now()}`);
  useEffect(() => {
    const library = props.weaver?.library;
    if (!library || templateId || loading || !supported) return;
    const attachment = templateListAttachment(query, null, listed, { clientId: ai.clientId, generationId: listGeneration.current });
    const key = `${templateListKind}:${listGeneration.current}`;
    library.snapshots.set(key, attachment);
    return () => { if (library.snapshots.get(key) === attachment) library.snapshots.delete(key); };
  }, [ai.clientId, listed, loading, props.weaver, query, supported, templateId]);
  // Spec 1193 FR-023: the open "Use template" dialog's values; a prefill may choose a listed template, and Create stays the user's.
  const dialogTemplates = useMemo(() => templates.map(item => ({ id: item.id, name: item.name, description: item.description, revision: item.revision, content: item.content })), [templates]);
  useWorkflowPageWeaver<CreateWorkflowDraft | null>({
    controller: props.weaver, clientId: ai.clientId, kind: createDialogKind, toolId: "workflow_create_dialog_prefill", resultType: "workflows.create-dialog-prefill",
    describe: "Filled in the open New workflow dialog; create the workflow with its Create action.",
    build: (draft, client) => draft && dialog?.kind === "workflow" ? createDialogAttachment(draft, dialogTemplates, client) : null,
    apply: change => dialog?.kind === "workflow" ? applyCreateDialogPrefill(dialog.draft, change, dialogTemplates) : null,
    current: () => dialog?.draft ?? null,
    restore: draft => setDialog(current => current && draft ? { ...current, draft } : current)
  });
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
    setError(""); setDialog({ kind, draft: { name: template ? `${template.name}${kind === "template" ? " copy" : ""}` : starter ? "HTTP request and response" : "", description: template?.description ?? (starter ? builtInWorkflowTemplates.find(item => item.id === starter)!.description : ""), rootActivityVersionId: null, starter,
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
  if (templateId) return (
    <section className="wf-page wf-page--template-workbench">
      <WorkflowLazyBoundary key={`${context.baseUrl}:${templateId}`} label="template designer" recoveryAction={{ label: "Back to templates", onClick: () => open(null) }}>
        <Suspense fallback={<p role="status">Loading template designer…</p>}>
          <TemplateDesigner {...props} id={templateId} onBack={() => open(null)} />
        </Suspense>
      </WorkflowLazyBoundary>
    </section>
  );
  return <section className="studio-page wf-page wf-templates-page">
    <StudioPageHeader title="Workflow templates" description="Create reusable starting points. Workflows created from a template are independent copies."
      refresh={{ id: "workflow-templates", label: "Refresh workflow templates", onRefresh: reload, refreshing: loading, disabled: busy, autoRefresh: !dialog && !busy }} actions={<>
      <StudioButton variant="primary" disabled={loading || !supported} onClick={() => begin("template")}><Plus size={15} /> New template</StudioButton>
    </>} />
    <div className="studio-collection-toolbar">
      <StudioSearchInput aria-label="Search templates" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search templates" />
    </div>
    {loading ? <p role="status">Loading templates…</p> : null}
    {!loading && !supported ? <p role="status">Saved template management is unavailable on this server. Built-in starters are still available when creating workflows.</p> : null}
    {error && !dialog ? <p className="wf-alert" role="alert">{error}</p> : null}
    {!loading && supported && query && !filteredTemplates.length ? <p className="wf-template-search-empty" role="status">No templates match “{search.trim()}”. Try another search.</p> : null}
    {!loading && supported && !query && !filteredTemplates.length ? <section className="wf-template-empty" aria-labelledby="first-template-title">
      <h2 id="first-template-title">Create your first template</h2>
      <p>Use New template to create a reusable starting point, or choose a built-in starter below.</p>
    </section> : null}
    {supported && filteredTemplates.length ? <section className="wf-template-section" aria-labelledby="custom-templates-title">
      <h2 id="custom-templates-title">Your templates</h2>
      <div className="wf-template-grid">
      {filteredTemplates.map(template => {
        const reason = templateAvailabilityReason(template, catalog);
        const root = template.content.state.rootActivity;
        const rootName = root ? template.content.activityPresentation?.find(item => item.nodeId === root.nodeId)?.displayName ?? catalog.find(item => item.activityVersionId === root.activityVersionId)?.displayName ?? "Root activity" : "No root activity";
        return <article key={template.id} className="wf-template-card"><h3><button className="wf-link-button" onClick={() => open(template.id)}>{template.name}</button></h3><p>{template.description || "No description"}</p>{reason ? <p role="status" className="wf-muted">{reason}</p> : null}
          <div className="wf-template-preview" aria-hidden="true"><span>{rootName}</span></div>
          <div className="wf-template-actions wf-actions"><StudioButton variant="primary" disabled={loading || !!reason} onClick={() => begin("workflow", template)}>Use template</StudioButton><StudioButton disabled={busy} onClick={() => open(template.id)}>Edit</StudioButton><OverflowMenu ariaLabel={`Actions for ${template.name}`} items={[
            { id: "copy", label: "Copy", disabled: busy, onSelect: () => begin("template", template) },
            { id: "delete", label: "Delete", destructive: true, disabled: busy, onSelect: () => void remove(template) }
          ]} /></div>
        </article>;
      })}
      </div>
    </section> : null}
    <section className="wf-template-section wf-template-starters-section" aria-labelledby="builtin-templates-title">
      <h2 id="builtin-templates-title"><StudioButton variant="ghost" className="wf-template-starters-toggle" aria-expanded={starterDisclosureOpen} aria-controls="workflow-template-starters" onClick={() => setStarterDisclosureOverride(value => !(value ?? starterDisclosureDefaultOpen))}>
        <ChevronDown size={15} aria-hidden="true" /> Built-in starters ({builtInWorkflowTemplates.length})
      </StudioButton></h2>
      <section id="workflow-template-starters" className="wf-template-starters-region" role="region" aria-label="Built-in starters" hidden={!starterDisclosureOpen}>
        {builtInWorkflowTemplates.map(item => {
          const reason = item.id === "http" ? getHttpStarterAvailabilityReason(catalog) : undefined;
          return <article key={item.id} className="wf-template-starter">
            <div className="wf-template-starter-copy"><h3>{item.name}</h3><p className="wf-template-starter-description">{item.description}</p>{reason ? <p role="status" className="wf-muted">{reason}</p> : null}</div>
            <div className="wf-template-actions wf-actions"><StudioButton variant="primary" disabled={loading || !!reason} onClick={() => begin("workflow", undefined, item.id === "http" ? "http" : undefined)}>Use</StudioButton><StudioButton disabled={loading || !supported || !!reason} onClick={() => begin("template", undefined, item.id === "http" ? "http" : undefined)}><Copy size={14} /> Copy</StudioButton></div>
          </article>;
        })}
      </section>
    </section>
    {dialog ? <Suspense fallback={<p role="status">Loading template dialog…</p>}><CreateWorkflowDialog context={context} title={dialog.kind === "template" ? "Create template" : "New workflow"} submitLabel={dialog.kind === "template" ? "Create template" : "Create"} draft={dialog.draft} catalog={catalog} creating={busy} submitError={error} ai={ai} onChange={draft => setDialog({ ...dialog, draft })} onClose={() => setDialog(null)} onSubmit={() => void submit()} /></Suspense> : null}
  </section>;
}


export function WorkflowTemplatesRoute({ api, navigate, weaver }: { api: ElsaStudioModuleApi; navigate(path: string): void; weaver?: Props["weaver"] }) {
  return <WorkflowTemplatesPage key={`${api.backend.baseUrl}:${api.runtime.identity?.tenantId}:${api.runtime.identity?.subject}`} context={api.backend} ai={api.weaver} weaver={weaver} propertyEditors={api.propertyEditors.list()} expressionEditors={api.expressionEditors.list()}
    runInputEditors={api.workflowRunInputEditors?.list() ?? []} testStimulusEditors={api.workflowTestStimulusEditors?.list() ?? []}
    workflowDesignerPanels={api.workflowDesigner.panels.list()} navigate={navigate} />;
}
