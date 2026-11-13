import { StudioField } from "@elsa-workflows/studio-ui";
import { useRef, useState } from "react";
import { openStudioWorkspaceTab, qualifyStudioBackendPath, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { saveWorkflowTemplate, templateContent } from "../workflowTemplates";
import { getNotifications, useNotificationGuard } from "./notifications";
import { useDialogFocus } from "./useDialogFocus";
export function SaveWorkflowTemplateDialog({ context, draft, name: initialName, description, onClose }: {
  context: StudioEndpointContext; draft: WorkflowDraft; name: string; description?: string | null; onClose(): void;
}) {
  const [name, setName] = useState(initialName), [text, setText] = useState(description ?? "");
  const [error, setError] = useState(""), [saving, setSaving] = useState(false);
  const pending = useRef(false);
  const captureFeedback = useNotificationGuard(context);
  const ref = useRef<HTMLElement>(null); useDialogFocus(ref, saving ? null : onClose);
  return <div className="wf-dialog-backdrop"><section ref={ref} tabIndex={-1} className="wf-dialog" role="dialog" aria-modal="true" aria-labelledby="save-template-title">
    <form onSubmit={event => { event.preventDefault(); if (pending.current || !name.trim()) return; pending.current = true; setSaving(true); setError("");
      const canNotify = captureFeedback();
      const notifications = getNotifications();
      const templatePath = qualifyStudioBackendPath("/workflows/templates");
      void saveWorkflowTemplate(context, { name, description: text, content: templateContent(draft) }).then(() => {
        if (canNotify.isOwned()) onClose();
        if (canNotify()) notifications.notify({ level: "success", title: "Template saved",
          action: { label: "View templates", run: () => {
            // Keep the source editor's unsaved changes when opening the saved copy.
            if (!openStudioWorkspaceTab(templatePath)) window.open(templatePath, "_blank", "noopener");
          } } });
      })
        .catch(error => { if (canNotify.isOwned()) setError(error instanceof Error ? error.message : String(error)); }).finally(() => { pending.current = false; if (canNotify.isOwned()) setSaving(false); }); }}>
      <h3 id="save-template-title">Save as template</h3>
      <p>Save the current design, including unsaved changes, as an independent template.</p>
      <StudioField className="wf-form-field" label="Name"><input value={name} onChange={event => setName(event.target.value)} maxLength={2048} required /></StudioField>
      <StudioField className="wf-form-field" label="Description"><textarea value={text} onChange={event => setText(event.target.value)} maxLength={2048} /></StudioField>
      {error ? <p role="alert">{error}</p> : null}
      <div className="wf-dialog-actions"><button type="button" disabled={saving} onClick={onClose}>Cancel</button><button type="submit" className="wf-primary-button" disabled={saving || !name.trim()}>{saving ? "Saving…" : "Save template"}</button></div>
    </form>
  </section></div>;
}
