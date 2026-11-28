import { StudioAlert, StudioDialog, StudioDialogFooter, StudioField } from "@elsa-workflows/studio-ui";
import { useRef, useState, type FormEvent } from "react";
import { openStudioWorkspaceTab, qualifyStudioBackendPath, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDraft } from "../workflowTypes";
import { saveWorkflowTemplate, templateContent } from "../workflowTemplates";
import { getNotifications, useNotificationGuard } from "./notifications";

export function SaveWorkflowTemplateDialog({ context, draft, name: initialName, description, onClose }: {
  context: StudioEndpointContext; draft: WorkflowDraft; name: string; description?: string | null; onClose(): void;
}) {
  const [name, setName] = useState(initialName), [text, setText] = useState(description ?? "");
  const [error, setError] = useState(""), [saving, setSaving] = useState(false);
  const pending = useRef(false);
  const captureFeedback = useNotificationGuard(context);
  const nameRef = useRef<HTMLInputElement>(null);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (pending.current || !name.trim()) return;
    pending.current = true;
    setSaving(true);
    setError("");
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
      .catch(error => { if (canNotify.isOwned()) setError(error instanceof Error ? error.message : String(error)); }).finally(() => { pending.current = false; if (canNotify.isOwned()) setSaving(false); });
  }

  return (
    <StudioDialog
      open
      title="Save as template"
      description="Save the current design, including unsaved changes, as an independent template."
      className="wf-tokens"
      onClose={onClose}
      onSubmit={submit}
      initialFocusRef={nameRef}
      dismissDisabled={saving}
      dismissOnOutsideClick={false}
      footer={<StudioDialogFooter
        onCancel={onClose}
        cancelDisabled={saving}
        confirmDisabled={saving || !name.trim()}
        confirmLabel={saving ? "Saving…" : "Save template"}
      />}
    >
      <StudioField className="wf-form-field" label="Name">
        <input ref={nameRef} value={name} onChange={event => setName(event.target.value)} maxLength={2048} required />
      </StudioField>
      <StudioField className="wf-form-field" label="Description">
        <textarea value={text} onChange={event => setText(event.target.value)} maxLength={2048} />
      </StudioField>
      {error ? <StudioAlert tone="danger">{error}</StudioAlert> : null}
    </StudioDialog>
  );
}
