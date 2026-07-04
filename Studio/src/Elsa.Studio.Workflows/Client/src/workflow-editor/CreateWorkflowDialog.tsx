import { useCallback, useEffect, useRef, useState } from "react";
import { Sparkles } from "lucide-react";
import type { StudioAiContributionApi, StudioAiPromptActionContribution } from "@elsa-workflows/studio-sdk";
import { createWorkflowRootOptions, type CreateWorkflowDraft, type WorkflowMetadataSuggestion } from "./editorTypes";
import { parseWorkflowMetadataSuggestion } from "./editorHelpers";

export function CreateWorkflowDialog({ draft, creating, ai, suggestMetadataAction, onChange, onClose, onSubmit }: {
  draft: CreateWorkflowDraft;
  creating: boolean;
  ai: StudioAiContributionApi;
  suggestMetadataAction?: StudioAiPromptActionContribution | null;
  onChange(draft: CreateWorkflowDraft): void;
  onClose(): void;
  onSubmit(): void;
}) {
  const [intentOpen, setIntentOpen] = useState(false);
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

  const applySuggestion = useCallback((value: WorkflowMetadataSuggestion) => {
    const next = { ...draftRef.current };
    if (value.name) next.name = value.name;
    if (value.description) next.description = value.description;
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
      <section className="wf-dialog" role="dialog" aria-modal="true" aria-labelledby="workflow-create-title">
        <form
          onSubmit={event => {
            event.preventDefault();
            onSubmit();
          }}
        >
          <div className="wf-dialog-heading">
            <h3 id="workflow-create-title">Create Workflow</h3>
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
              <label className="wf-form-field">
                <span>What should this workflow do?</span>
                <textarea
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
                />
              </label>
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
          <label className="wf-form-field">
            <span>Display name</span>
            <input
              autoFocus
              aria-label="Display name"
              value={draft.name}
              onChange={event => onChange({ ...draft, name: event.target.value })}
            />
          </label>
          <label className="wf-form-field">
            <span>Description</span>
            <textarea
              aria-label="Description"
              rows={3}
              value={draft.description}
              onChange={event => onChange({ ...draft, description: event.target.value })}
            />
          </label>
          <fieldset className="wf-form-field wf-root-field">
            <legend>Root activity</legend>
            <div className="wf-root-cards" role="radiogroup" aria-label="Root activity">
              {createWorkflowRootOptions.map(option => {
                const checked = draft.rootKind === option.value;
                return (
                  <label key={option.value} className="wf-root-card" data-checked={checked || undefined}>
                    <input
                      type="radio"
                      name="wf-root-kind"
                      aria-label={option.label}
                      value={option.value}
                      checked={checked}
                      onChange={() => onChange({ ...draft, rootKind: option.value, rootActivityVersionId: null })}
                    />
                    <span className="wf-root-card-title">{option.label}</span>
                    <span className="wf-root-card-hint">{option.hint}</span>
                  </label>
                );
              })}
            </div>
          </fieldset>
          <div className="wf-dialog-actions">
            <button type="button" onClick={onClose} disabled={creating}>Cancel</button>
            <button type="submit" disabled={creating || !draft.name.trim()}>{creating ? "Creating..." : "Create"}</button>
          </div>
        </form>
      </section>
    </div>
  );
}
