import { builtInWorkflowTemplates } from "../builtInWorkflowTemplates";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
import type { WorkflowTemplate } from "../workflowTemplates";
import { useCallback, useEffect, useRef, useState } from "react";
import { Sparkles } from "lucide-react";
import type { StudioWeaverRegistry, StudioAiPromptActionContribution } from "@elsa-workflows/studio-sdk";
import type { ActivityCatalogItem } from "../workflowTypes";
import { type CreateWorkflowDraft, type WorkflowMetadataSuggestion } from "./editorTypes";
import { getCreateRootActivity, parseWorkflowMetadataSuggestion } from "./editorHelpers";
import { WorkflowRootRadioCards } from "./WorkflowRootCards";
import { getHttpStarterAvailabilityReason } from "./httpWorkflowStarter";
import { useDialogFocus } from "./useDialogFocus";
import { getCreateMetadataEdits } from "./createWorkflowMetadata";

export function CreateWorkflowDialog({ context, title = "Create Workflow", submitLabel = "Create", draft, catalog, catalogState = "ready", catalogError = "", creating, submitError = "", ai, suggestMetadataAction, onChange, onClose, onRetryCatalog = () => {}, onSubmit, onTemplates }: {
  context?: StudioEndpointContext;
  title?: string;
  submitLabel?: string;
  draft: CreateWorkflowDraft;
  catalog: ActivityCatalogItem[];
  catalogState?: "idle" | "loading" | "ready" | "failed";
  catalogError?: string;
  creating: boolean;
  submitError?: string;
  ai: StudioWeaverRegistry;
  suggestMetadataAction?: StudioAiPromptActionContribution | null;
  onChange(draft: CreateWorkflowDraft): void;
  onClose(): void;
  onRetryCatalog?(): void;
  onSubmit(): void;
  /**
   * The saved templates this dialog offers (those it can use now), so the page can disclose them to Weaver; null until the
   * dialog has read them for its settled activity catalog, so a disclosed list is never one about to change.
   */
  onTemplates?(templates: WorkflowTemplate[] | null): void;
}) {
  // The templates as last read, with the catalog they were judged against (their availability depends on it).
  const [templateRead, setTemplateRead] = useState<{ catalog: ActivityCatalogItem[]; items: Array<{ template: WorkflowTemplate; reason?: string }> } | null>(null);
  // A failed read, with the catalog it was made for: it settles (and is shown for) that catalog only, so a failure while the
  // catalog was still loading neither settles the dialog nor outlives the read that follows it.
  const [templateFailure, setTemplateFailure] = useState<{ catalog: ActivityCatalogItem[] } | null>(null);
  const templateError = templateFailure?.catalog === catalog
    ? "Saved templates could not be loaded. Close and reopen this dialog to retry; built-in starters remain available." : "";
  const templates = templateRead?.items ?? [];
  useEffect(() => {
    if (!context) return;
    let active = true;
    void import("../workflowTemplates").then(async module => {
      const values = await module.listWorkflowTemplates(context);
      if (active) setTemplateRead({ catalog, items: (values ?? []).map(template => ({ template, reason: module.templateAvailabilityReason(template, catalog) })) });
    }).catch(() => { if (active) setTemplateFailure({ catalog }); });
    return () => { active = false; };
  }, [context, catalog]);
  const templatesSettled = catalogState !== "idle" && catalogState !== "loading" && (!context || templateError !== "" || templateRead?.catalog === catalog);
  // Only a read judged against the current catalog is offered; a failed read for it offers none.
  const offered = templatesSettled ? (templateRead?.catalog === catalog ? templates : []).filter(item => !item.reason).map(item => item.template) : null;
  const offeredKey = offered ? JSON.stringify(offered.map(template => [template.id, template.revision])) : null;
  // Reported when the offered set changes (not on every render), so the page's disclosed revision moves only with it.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { onTemplates?.(offered); }, [onTemplates, offeredKey]);
  const dialogRef = useRef<HTMLElement>(null);
  useDialogFocus(dialogRef, creating ? null : onClose);
  const submitErrorRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (submitError) submitErrorRef.current?.focus();
  }, [submitError]);
  const [intentOpen, setIntentOpen] = useState(false);
  const httpStarterAvailabilityReason = getHttpStarterAvailabilityReason(catalog);
  const httpStarterAvailable = !httpStarterAvailabilityReason;
  const rootActivity = getCreateRootActivity(draft.rootActivityVersionId, catalog);
  const starterReady = draft.templateId ? templates.some(item => item.template.id === draft.templateId && !item.reason) : draft.starter === "http"
    ? httpStarterAvailable
    : Boolean(rootActivity?.authoringTemplate);
  const canCreate = catalogState === "ready" && starterReady && !creating && Boolean(draft.name.trim());
  const catalogDescriptionId = catalogState === "ready"
    ? undefined
    : catalogState === "failed"
      ? "wf-activity-catalog-error"
      : "wf-activity-catalog-loading";
  const createReadinessDescriptionId = catalogDescriptionId ?? (!starterReady
    ? draft.starter === "http" ? "wf-http-starter-unavailable" : "wf-root-create-readiness"
    : undefined);
  const [intent, setIntent] = useState("");
  const [suggesting, setSuggesting] = useState(false);
  const [suggestion, setSuggestion] = useState<WorkflowMetadataSuggestion | null>(null);
  const [suggestError, setSuggestError] = useState<string | null>(null);
  const pendingRequestIdRef = useRef<string | null>(null);
  // Apply onto the latest draft without re-subscribing the result listener on every keystroke.
  const draftRef = useRef(draft);
  draftRef.current = draft;
  const onChangeRef = useRef(onChange);
  onChangeRef.current = onChange;

  const changeStartingPoint = (id: string) => {
    const selected = templates.find(item => item.template.id === id)?.template;
    const starter = builtInWorkflowTemplates.find(template => template.id === id);
    const defaults = {
      name: selected?.name ?? (starter?.id === "http" ? starter.name : ""),
      description: selected?.description ?? (starter?.id === "http" ? starter.description : "")
    };
    const edited = getCreateMetadataEdits(draft);
    onChange({ ...draft,
      name: edited.name ? draft.name : defaults.name,
      description: edited.description ? draft.description : defaults.description,
      metadataEdited: edited,
      starter: id === "http" ? "http" : undefined,
      templateId: selected?.id,
      templateContent: selected ? structuredClone(selected.content) : undefined
    });
  };

  const applySuggestion = useCallback((value: WorkflowMetadataSuggestion) => {
    const next = { ...draftRef.current, metadataEdited: getCreateMetadataEdits(draftRef.current) };
    if (value.name) { next.name = value.name; next.metadataEdited.name = true; }
    if (value.description) { next.description = value.description; next.metadataEdited.description = true; }
    onChangeRef.current(next);
    setSuggestion(null);
    setSuggestError(null);
  }, []);

  useEffect(() => {
    if (!suggestMetadataAction) return;
    return ai.onPromptResult(result => {
      if (result.requestId !== pendingRequestIdRef.current) return;
      pendingRequestIdRef.current = null;
      setSuggesting(false);
      if (result.status !== "completed") {
        setSuggestError(result.status === "cancelled"
          ? "Weaver needs more detail — continue in the assistant panel."
          : "Weaver couldn't generate a suggestion. Try again or fill the fields manually.");
        return;
      }
      const parsed = parseWorkflowMetadataSuggestion(result.text);
      if (!parsed) {
        setSuggestError("Couldn't read a suggestion from Weaver's reply. See the assistant panel.");
        return;
      }
      // Autopilot fills the fields automatically; otherwise the user applies it explicitly.
      if (result.autoApply) applySuggestion(parsed);
      else setSuggestion(parsed);
    });
  }, [ai, suggestMetadataAction, applySuggestion]);

  const runSuggest = () => {
    if (!suggestMetadataAction) return;
    const prompt = suggestMetadataAction.createPrompt({ draft: draftRef.current, intent });
    if (!prompt) return;
    const requestId = `wf-suggest-${Date.now()}-${Math.round(Math.random() * 1e6)}`;
    pendingRequestIdRef.current = requestId;
    setSuggesting(true);
    setSuggestion(null);
    setSuggestError(null);
    ai.dispatchPrompt({ ...prompt, requestId });
  };

  return (
    <div className="wf-dialog-backdrop" role="presentation">
      <section ref={dialogRef} tabIndex={-1} className="wf-dialog wf-create-dialog" role="dialog" aria-modal="true" aria-labelledby="workflow-create-title">
        <form
          onSubmit={event => {
            event.preventDefault();
            if (canCreate) onSubmit();
          }}
        >
          <div className="wf-dialog-heading">
            <h3 id="workflow-create-title">{title}</h3>
            {suggestMetadataAction ? (
              <button
                type="button"
                className="wf-ai-action"
                aria-expanded={intentOpen}
                onClick={() => setIntentOpen(open => !open)}
                title={suggestMetadataAction.description ?? suggestMetadataAction.label}
              >
                <Sparkles size={13} /> {suggestMetadataAction.label}
              </button>
            ) : null}
          </div>
          {suggestMetadataAction && intentOpen ? (
            <div className="wf-ai-suggest" role="group" aria-label="Suggest name and description">
              <StudioField className="wf-form-field" label={<><span>What should this workflow do?</span></>}><textarea
                  autoFocus
                  aria-label="Workflow intent"
                  rows={2}
                  placeholder="e.g. Route incoming support tickets to the right team and notify on SLA breach (optional)"
                  value={intent}
                  disabled={suggesting}
                  onChange={event => setIntent(event.target.value)}
                  onKeyDown={event => {
                    if ((event.metaKey || event.ctrlKey) && event.key === "Enter") {
                      event.preventDefault();
                      runSuggest();
                    }
                  }}
                /></StudioField>
              <div className="wf-ai-suggest-actions">
                <button type="button" className="wf-ai-action" onClick={runSuggest} disabled={suggesting}>
                  <Sparkles size={13} /> {suggesting ? "Generating…" : "Generate"}
                </button>
              </div>
              {suggestError ? <p className="wf-ai-suggest-error" role="alert">{suggestError}</p> : null}
              {suggestion ? (
                <div className="wf-ai-suggest-preview">
                  {suggestion.name ? <p><strong>{suggestion.name}</strong></p> : null}
                  {suggestion.description ? <p>{suggestion.description}</p> : null}
                  <div className="wf-ai-suggest-actions">
                    <button type="button" onClick={() => applySuggestion(suggestion)}>Apply</button>
                    <button type="button" onClick={() => setSuggestion(null)}>Dismiss</button>
                  </div>
                </div>
              ) : null}
            </div>
          ) : null}
          <StudioField className="wf-form-field" label={<><span>Starting point</span>{draft.starter === "http" ? <small className="wf-muted">POST /hello returns 200 with JSON. Publish it, then use Send HTTP test request to try it.</small> : null}{catalogState === "ready" && httpStarterAvailabilityReason ? <small className="wf-muted" id="wf-http-starter-unavailable">{httpStarterAvailabilityReason}</small> : null}</>}><select
              data-dialog-initial-focus
              aria-label="Starting point"
              aria-busy={!templatesSettled}
              aria-describedby={catalogDescriptionId ?? (httpStarterAvailabilityReason ? "wf-http-starter-unavailable" : undefined)}
              value={draft.templateId ?? draft.starter ?? "blank"}
              disabled={creating}
              onChange={event => changeStartingPoint(event.target.value)}
            >
              {builtInWorkflowTemplates.map(template => <option key={template.id} value={template.id} disabled={template.id === "http" && (catalogState !== "ready" || !httpStarterAvailable)}>{template.name}</option>)}
              {templates.map(({ template, reason }) => <option key={template.id} value={template.id} disabled={!!reason} title={reason}>{template.name}{reason ? " (unavailable)" : ""}</option>)}
            </select></StudioField>
          <StudioField className="wf-form-field" label={<><span>Display name</span></>}><input
              aria-label="Display name"
              value={draft.name}
              onChange={event => onChange({ ...draft, name: event.target.value, metadataEdited: { ...getCreateMetadataEdits(draft), name: true } })}
            /></StudioField>
          <StudioField className="wf-form-field" label={<><span>Description</span></>}><textarea
              aria-label="Description"
              rows={3}
              value={draft.description}
              onChange={event => onChange({ ...draft, description: event.target.value, metadataEdited: { ...getCreateMetadataEdits(draft), description: true } })}
            /></StudioField>
          {catalogState === "failed" ? (
            <div className="wf-alert" id="wf-activity-catalog-error" role="alert">
              <span>{catalogError || "Couldn't load starting activities."}</span>
              <button type="button" onClick={onRetryCatalog}>Retry</button>
            </div>
          ) : catalogState !== "ready" ? (
            <p className="wf-muted" id="wf-activity-catalog-loading" role="status">Loading available starting activities…</p>
          ) : null}
          {templateError ? <p role="status" className="wf-muted">{templateError}</p> : null}
          {draft.starter !== "http" && !draft.templateId ? <fieldset className="wf-form-field wf-root-field">
            <legend>Root activity</legend>
            <WorkflowRootRadioCards
              catalog={catalog}
              value={draft.rootActivityVersionId}
              availabilityKnown={catalogState === "ready"}
              onChange={rootActivityVersionId => onChange({ ...draft, rootActivityVersionId })}
            />
          </fieldset> : null}
          {catalogState === "ready" && draft.starter !== "http" && !starterReady ? (
            <p className="wf-muted" id="wf-root-create-readiness">No compatible root activity is available. Each disabled choice explains why.</p>
          ) : null}
          {submitError ? <div ref={submitErrorRef} className="wf-alert" role="alert" tabIndex={-1}>{submitError}</div> : null}
          <StudioDialogFooter
            className="wf-dialog-actions"
            onCancel={onClose}
            cancelDisabled={creating}
            confirmLabel={creating ? "Creating..." : submitLabel}
            confirmDisabled={creating || !draft.name.trim()}
            confirmProps={{ "aria-disabled": !canCreate, "aria-describedby": createReadinessDescriptionId }}
          />
        </form>
      </section>
    </div>
  );
}
