import { useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, X } from "lucide-react";
import { StudioHttpError, type StudioActivityDefinitionImplementationEditorContribution, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityProviderAuthoringCapability, CreateActivityDefinitionResponse } from "./activityDefinitionTypes";
import { createActivityDefinition, useActivityAuthoringCapabilities } from "./api/activityDesign";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";

interface ProviderChoice {
  capability: ActivityProviderAuthoringCapability;
  providerKey: string;
  schemaVersion: string;
  contribution?: StudioActivityDefinitionImplementationEditorContribution;
}

export function ActivityDefinitionCreateDialog({ context, activityEditors, onClose, onCreated }: {
  context: StudioEndpointContext;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  onClose(): void;
  onCreated(response: CreateActivityDefinitionResponse): void;
}) {
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const choices = useMemo(() => flattenProviderChoices(capabilitiesQuery.data?.providers ?? [], activityEditors), [activityEditors, capabilitiesQuery.data?.providers]);
  const [selected, setSelected] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [category, setCategory] = useState("Custom");
  const [description, setDescription] = useState("");
  const [advanced, setAdvanced] = useState(false);
  const [activityTypeKey, setActivityTypeKey] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const displayNameRef = useRef<HTMLInputElement>(null);
  const dialogRef = useRef<HTMLElement>(null);

  useDialogFocus(dialogRef, submitting ? null : onClose);

  useEffect(() => {
    if (choices.length === 1 && choices[0].providerKey === "elsa.activity-graph") setSelected(choiceValue(choices[0]));
    if (choices.length > 0) displayNameRef.current?.focus();
  }, [choices]);

  const choice = choices.find(item => choiceValue(item) === selected);
  const rules = capabilitiesQuery.data?.activityTypeKeyRules;
  const canSubmit = Boolean(choice?.contribution && displayName.trim() && category.trim() && !submitting);

  const submit = async () => {
    if (!choice?.contribution || !capabilitiesQuery.data || !canSubmit) return;
    setSubmitting(true);
    setError(null);
    observeActivityDefinitions({ event: "create-start", surface: "creation", providerKey: choice.providerKey, providerSchemaVersion: choice.schemaVersion });
    try {
      const implementation = choice.contribution.createInitialImplementation();
      const contractSchemaVersion = capabilitiesQuery.data.contractSchemaVersions[0];
      if (!contractSchemaVersion) throw new Error("No authorable public contract schema is available.");
      const exactKey = activityTypeKey.trim();
      const response = await createActivityDefinition(context, {
        category: category.trim(),
        displayName: displayName.trim(),
        description: description.trim() || null,
        ...(advanced && rules?.allowsPreCreationOverride && exactKey ? { activityTypeKey: exactKey } : {}),
        provider: {
          providerKey: choice.providerKey,
          schemaVersion: choice.schemaVersion,
          payload: implementation.payload
        },
        contract: {
          contractSchemaVersion,
          inputs: [],
          outputs: [],
          outcomes: choice.capability.requiredOutcomes.map(outcome => ({ ...outcome, description: outcome.description ?? null }))
        },
        layout: implementation.layout
      });
      observeActivityDefinitions({ event: "create-success", surface: "creation", outcome: "ready", providerKey: choice.providerKey, providerSchemaVersion: choice.schemaVersion });
      onCreated(response);
    } catch (cause) {
      observeActivityDefinitions({ event: "create-failure", surface: "creation", outcome: "failed", providerKey: choice.providerKey, providerSchemaVersion: choice.schemaVersion });
      setError(createErrorCopy(cause));
    } finally {
      setSubmitting(false);
    }
  };

  return <div className="ad-dialog-backdrop" role="presentation" onMouseDown={event => { if (event.target === event.currentTarget && !submitting) onClose(); }}>
    <section ref={dialogRef} className="ad-dialog" role="dialog" aria-modal="true" aria-labelledby="create-activity-definition-title" tabIndex={-1}>
      <header><div><span className="ad-kicker">Design-owned reusable activity</span><h2 id="create-activity-definition-title">Create Activity Definition</h2></div><button type="button" className="ad-icon-button" aria-label="Close" onClick={onClose} disabled={submitting}><X size={18} /></button></header>
      {capabilitiesQuery.isPending ? <div className="ad-inline-status" role="status">Loading authorized authoring providers…</div> : null}
      {capabilitiesQuery.isError ? <div className="ad-inline-error" role="alert">Authoring capabilities are unavailable. Studio will not guess a provider or contract.</div> : null}
      {capabilitiesQuery.data && choices.length === 0 ? <div className="ad-inline-error" role="alert">No authorable implementation provider is currently advertised.</div> : null}
      {choices.length ? <div className="ad-form-grid">
        <label><span>Implementation provider</span><select name="provider" value={selected} onChange={event => setSelected(event.target.value)}><option value="" disabled>Select a provider</option>{choices.map(item => <option key={choiceValue(item)} value={choiceValue(item)}>{item.capability.displayName} · schema {item.schemaVersion}</option>)}</select></label>
        {choice && !choice.contribution ? <div className="ad-inline-error" role="alert"><AlertTriangle size={17} aria-hidden /> The backend advertises this provider, but its exact Studio editor contribution is unavailable. Creation is disabled.</div> : null}
        <label><span>Display name</span><input ref={displayNameRef} name="displayName" value={displayName} onChange={event => setDisplayName(event.target.value)} /></label>
        <label><span>Category</span><input name="category" value={category} onChange={event => setCategory(event.target.value)} /></label>
        <label className="ad-form-wide"><span>Description <small>Optional</small></span><textarea name="description" value={description} onChange={event => setDescription(event.target.value)} rows={3} /></label>
        <div className="ad-form-wide ad-key-summary"><strong>Activity Type Key</strong><span>{rules?.serverGenerated ? `Generated by the backend under ${rules.prefix}.` : "Defined by the backend authoring contract."} It is immutable after creation.</span>{rules?.allowsPreCreationOverride ? <button type="button" className="ad-link-button" onClick={() => setAdvanced(current => { if (current) setActivityTypeKey(""); return !current; })}>{advanced ? "Use generated key" : "Set an exact key"}</button> : null}</div>
        {advanced && rules?.allowsPreCreationOverride ? <label className="ad-form-wide"><span>Exact Activity Type Key</span><input name="activityTypeKey" value={activityTypeKey} onChange={event => setActivityTypeKey(event.target.value)} placeholder={`${rules.prefix}.team-name.activity-name`} maxLength={rules.maximumLength} /><small>Must match the advertised namespace rules. Collisions fail; Studio never adds a suffix.</small></label> : null}
      </div> : null}
      {error ? <div className="ad-inline-error" role="alert">{error}</div> : null}
      <footer><button type="button" onClick={onClose} disabled={submitting}>Cancel</button><button type="button" className="ad-primary-action" onClick={() => void submit()} disabled={!canSubmit}>{submitting ? "Creating…" : "Create definition"}</button></footer>
    </section>
  </div>;
}

function flattenProviderChoices(
  providers: ActivityProviderAuthoringCapability[],
  contributions: StudioActivityDefinitionImplementationEditorContribution[]
) {
  return providers.flatMap(capability => capability.manifestSchemas
    .filter(schema => schema.isAuthorable)
    .map(schema => ({
      capability,
      providerKey: capability.providerKey,
      schemaVersion: schema.schemaVersion,
      contribution: contributions.find(item => item.providerKey === capability.providerKey && item.providerSchemaVersion === schema.schemaVersion)
    })));
}

function choiceValue(choice: Pick<ProviderChoice, "providerKey" | "schemaVersion">) {
  return `${choice.providerKey}|${choice.schemaVersion}`;
}

function createErrorCopy(error: unknown) {
  if (error instanceof StudioHttpError) {
    const payload = error.payload as { errorCode?: string } | null;
    if (payload?.errorCode === "activity.definition.key-conflict") return "That Activity Type Key already exists in this scope. Choose another exact key; Studio will not suffix it.";
    if (payload?.errorCode === "activity.definition.key-invalid") return "The exact Activity Type Key does not satisfy the advertised namespace rules.";
    if (error.status === 403) return "This account is not authorized to create with the selected provider.";
  }
  return "The Activity Definition was not created. No partial identity is shown; review the fields and try again.";
}
