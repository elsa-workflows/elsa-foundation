import { useRef, useState } from "react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { saveWorkflowTemplate, templateContent } from "../workflowTemplates";
import { useDialogFocus } from "./useDialogFocus";
export function SaveWorkflowTemplateDialog({ context, draft, name: initialName, description, onClose }: {
  context: StudioEndpointContext; draft: WorkflowDraft; name: string; description?: string | null; onClose(): void;
}) {
  const [name, setName] = useState(initialName), [text, setText] = useState(description ?? "");
  const [error, setError] = useState(""), [saving, setSaving] = useState(false), [saved, setSaved] = useState(false);
  const ref = useRef<HTMLElement>(null); useDialogFocus(ref, saving ? null : onClose);
  return <div className="wf-dialog-backdrop"><section ref={ref} tabIndex={-1} className="wf-dialog" role="dialog" aria-modal="true" aria-labelledby="save-template-title">
    <form onSubmit={event => { event.preventDefault(); if (saving || !name.trim()) return; setSaving(true); setError("");
      void saveWorkflowTemplate(context, { name, description: text, content: templateContent(draft) }).then(() => setSaved(true))
        .catch(error => setError(error instanceof Error ? error.message : String(error))).finally(() => setSaving(false)); }}>
      <h3 id="save-template-title">Save as template</h3>
      {saved ? <><p role="status">Template saved. Find it under Workflows → Templates.</p><div className="wf-dialog-actions"><button type="button" onClick={onClose}>Done</button></div></> : <>
        <p>Save the current design, including unsaved changes, as an independent template.</p>
        <label className="wf-form-field">Name<input value={name} onChange={event => setName(event.target.value)} maxLength={2048} required /></label>
        <label className="wf-form-field">Description<textarea value={text} onChange={event => setText(event.target.value)} maxLength={2048} /></label>
        {error ? <p role="alert">{error}</p> : null}
        <div className="wf-dialog-actions"><button type="button" disabled={saving} onClick={onClose}>Cancel</button><button type="submit" className="wf-primary-button" disabled={saving || !name.trim()}>{saving ? "Saving…" : "Save template"}</button></div>
      </>}
    </form>
  </section></div>;
}
