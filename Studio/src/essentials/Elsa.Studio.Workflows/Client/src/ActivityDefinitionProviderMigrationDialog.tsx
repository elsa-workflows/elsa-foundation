import { useEffect, useMemo, useRef, useState } from "react";
import type { StudioActivityDefinitionImplementationEditorContribution, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftView } from "./activityDefinitionTypes";
import { migrateActivityDefinitionDraft, useActivityAuthoringCapabilities } from "./api/activityDesign";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";
import { ActivityDefinitionManagementDialogActions, ActivityDefinitionManagementDialogShell } from "./ActivityDefinitionManagementDialogShell";
import {
  choiceValue,
  contractSummary,
  draftMutationError,
  migrationProviderChoices
} from "./activityDefinitionDraftManagementShared";

export function ActivityDefinitionProviderMigrationDialog({
  context,
  draft,
  activityEditors,
  onClose,
  onCreated
}: {
  context: StudioEndpointContext;
  draft: ActivityDefinitionDraftView;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  onClose(): void;
  onCreated(draft: ActivityDefinitionDraftView): void;
}) {
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const choices = useMemo(
    () => migrationProviderChoices(capabilitiesQuery.data, activityEditors, draft.provider.schemaVersion)
      .filter(item => item.providerKey !== draft.provider.providerKey || item.schemaVersion !== draft.provider.schemaVersion),
    [activityEditors, capabilitiesQuery.data, draft.provider.providerKey, draft.provider.schemaVersion]
  );
  const [target, setTarget] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLElement>(null);
  useDialogFocus(dialogRef, submitting ? null : onClose);

  useEffect(() => {
    if (!choices.some(item => choiceValue(item) === target)) setTarget(choices[0] ? choiceValue(choices[0]) : "");
  }, [choices, target]);

  const choice = choices.find(item => choiceValue(item) === target);
  const canSubmit = Boolean(!submitting && choice?.contribution);
  const submit = async () => {
    if (!canSubmit || !choice) return;
    setSubmitting(true);
    setError(null);
    try {
      const created = await migrateActivityDefinitionDraft(context, draft.draftId, {
        expectedRevision: draft.revision,
        targetProviderKey: choice.providerKey,
        targetSchemaVersion: choice.schemaVersion
      });
      onCreated(created);
    } catch (cause) {
      setError(draftMutationError(cause, "Provider migration created no draft. The exact source draft remains unchanged."));
    } finally {
      setSubmitting(false);
    }
  };

  return <ActivityDefinitionManagementDialogShell dialogRef={dialogRef} title="Review provider migration" kicker="Atomic parallel draft" submitting={submitting} onClose={onClose}>
    <dl className="ad-review-facts">
      <div><dt>Source draft</dt><dd><code>{draft.draftId}</code> · revision {draft.revision}</dd></div>
      <div><dt>Source provider</dt><dd><code>{draft.provider.providerKey}</code> · schema {draft.provider.schemaVersion}</dd></div>
      <div><dt>Contract</dt><dd>{contractSummary(draft.contract)}</dd></div>
      <div><dt>Provenance</dt><dd>{draft.sourceVersionId ? <>Exact version <code>{draft.sourceVersionId}</code></> : "Blank lineage draft"}</dd></div>
    </dl>
    <label className="ad-dialog-field"><span>Advertised migration target</span><select value={target} onChange={event => setTarget(event.target.value)}><option value="" disabled>Select a provider schema</option>{choices.map(item => <option key={choiceValue(item)} value={choiceValue(item)}>{item.capability.displayName} · schema {item.schemaVersion}</option>)}</select></label>
    <div className="ad-atomic-review"><strong>Atomic result</strong><span>The backend creates one new parallel draft with the unchanged public contract and migrated opaque provider manifest. It never rewrites this source draft.</span></div>
    {capabilitiesQuery.isPending ? <div className="ad-inline-status" role="status">Loading advertised provider migrations…</div> : null}
    {capabilitiesQuery.isError || choices.length === 0 ? <div className="ad-inline-error" role="alert">No supported authorable migration target is currently advertised. The existing draft remains available with its exact provider facts.</div> : null}
    {error ? <div className="ad-inline-error" role="alert">{error}</div> : null}
    <ActivityDefinitionManagementDialogActions submitting={submitting} submitLabel="Create migrated draft" canSubmit={canSubmit} onClose={onClose} onSubmit={submit} />
  </ActivityDefinitionManagementDialogShell>;
}
