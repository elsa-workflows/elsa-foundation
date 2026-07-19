import { useEffect, useMemo, useRef, useState } from "react";
import type { StudioActivityDefinitionImplementationEditorContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftView } from "./activityDefinitionTypes";
import { createActivityDefinitionDraft, useActivityAuthoringCapabilities } from "./api/activityDesign";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";
import { ActivityDefinitionManagementDialogActions, ActivityDefinitionManagementDialogShell } from "./ActivityDefinitionManagementDialogShell";
import { ActivityDefinitionVersionPicker } from "./ActivityDefinitionVersionPicker";
import {
  authorableProviderChoices,
  choiceValue,
  createBlankDraft,
  draftMutationError
} from "./activityDefinitionDraftManagementShared";

export function ActivityDefinitionDraftDialog({
  context,
  definitionId,
  activityEditors,
  initialSourceVersionId = null,
  onClose,
  onCreated
}: {
  context: StudioEndpointContext;
  definitionId: string;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  initialSourceVersionId?: string | null;
  onClose(): void;
  onCreated(draft: ActivityDefinitionDraftView): void;
}) {
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const choices = useMemo(
    () => authorableProviderChoices(capabilitiesQuery.data, activityEditors),
    [activityEditors, capabilitiesQuery.data]
  );
  const [mode, setMode] = useState<"blank" | "clone">(initialSourceVersionId ? "clone" : "blank");
  const [sourceVersionId, setSourceVersionId] = useState(initialSourceVersionId ?? "");
  const [provider, setProvider] = useState("");
  const [presentationLabel, setPresentationLabel] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLElement>(null);
  useDialogFocus(dialogRef, submitting ? null : onClose);

  useEffect(() => {
    if (!provider && choices.length === 1) setProvider(choiceValue(choices[0]));
  }, [choices, provider]);

  const choice = choices.find(item => choiceValue(item) === provider);
  const canSubmit = !submitting && (
    mode === "clone"
      ? Boolean(sourceVersionId)
      : Boolean(choice?.contribution && capabilitiesQuery.data?.contractSchemaVersions[0])
  );

  const submit = async () => {
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    try {
      const label = presentationLabel.trim() || null;
      const draft = mode === "clone"
        ? await createActivityDefinitionDraft(context, definitionId, {
            sourceVersionId,
            presentationLabel: label
          })
        : await createBlankDraft(context, definitionId, capabilitiesQuery.data!, choice!, label);
      onCreated(draft);
    } catch (cause) {
      setError(draftMutationError(cause, "The draft was not created. The definition and its existing drafts are unchanged."));
    } finally {
      setSubmitting(false);
    }
  };

  return <ActivityDefinitionManagementDialogShell dialogRef={dialogRef} title="Create parallel draft" kicker="Design-owned mutable work" submitting={submitting} onClose={onClose}>
    <fieldset className="ad-choice-grid">
      <legend>Starting point</legend>
      <label><input type="radio" name="draft-mode" checked={mode === "blank"} onChange={() => setMode("blank")} /> <span><strong>Blank</strong><small>Start from an advertised authoring provider and its required contract outcomes.</small></span></label>
      <label><input type="radio" name="draft-mode" checked={mode === "clone"} onChange={() => setMode("clone")} /> <span><strong>Clone exact version</strong><small>Deep-copy one visible immutable version; the source remains unchanged.</small></span></label>
    </fieldset>
    <label className="ad-dialog-field"><span>Draft label <small>Optional · need not be unique</small></span><input value={presentationLabel} maxLength={200} onChange={event => setPresentationLabel(event.target.value)} placeholder="Generated fallback used when blank" /></label>
    {mode === "blank" ? <>
      {capabilitiesQuery.isPending ? <div className="ad-inline-status" role="status">Loading authorized providers…</div> : null}
      {capabilitiesQuery.isError ? <div className="ad-inline-error" role="alert">Authoring capabilities are unavailable. Studio will not invent a blank provider state.</div> : null}
      {capabilitiesQuery.isSuccess && choices.length === 0 ? <div className="ad-inline-status" role="status">No authorable provider schemas are available for blank draft creation.</div> : null}
      <label className="ad-dialog-field"><span>Implementation provider</span><select value={provider} onChange={event => setProvider(event.target.value)}><option value="" disabled>Select a provider</option>{choices.map(item => <option key={choiceValue(item)} value={choiceValue(item)}>{item.capability.displayName} · schema {item.schemaVersion}</option>)}</select></label>
      {choice && !choice.contribution ? <div className="ad-inline-error" role="alert">The exact Studio editor for this advertised provider schema is unavailable. Blank creation is disabled.</div> : null}
    </> : <ActivityDefinitionVersionPicker context={context} definitionId={definitionId} selectedVersionId={sourceVersionId} onChange={setSourceVersionId} />}
    <p className="ad-dialog-note">Each draft receives a stable server-generated identity. Labels are presentation metadata and may be blank or repeated.</p>
    {error ? <div className="ad-inline-error" role="alert">{error}</div> : null}
    <ActivityDefinitionManagementDialogActions submitting={submitting} submitLabel={mode === "clone" ? "Clone exact version" : "Create blank draft"} canSubmit={canSubmit} onClose={onClose} onSubmit={submit} />
  </ActivityDefinitionManagementDialogShell>;
}
