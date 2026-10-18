import { useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, X } from "lucide-react";
import { StudioHttpError, type StudioActivityDefinitionImplementationEditorContribution, type StudioActivityDefinitionImplementationState, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityOutcomeContract, ActivityProviderAuthoringCapability, CreateActivityDefinitionResponse } from "./activityDefinitionTypes";
import type { ActivityCatalogItem, ActivityNode } from "./workflowTypes";
import { createActivityDefinition, useActivityAuthoringCapabilities, useWorkflowActivities } from "./api/activityDesign";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { EditableCombobox } from "./EditableCombobox";
import { deriveActivityCategorySuggestions } from "./activityCategories";
import { createActivityNode } from "./workflowAdapter";
import { canonicalizeStateForWire } from "./activityInputWire";
import { WorkflowRootRadioCards } from "./workflow-editor/WorkflowRootCards";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";
import { defaultActivityGraphOutcomeChoice, getActivityGraphOutcomeMappingOptions } from "./activityGraphOutcomeMappingOptions";

export interface ProviderChoice {
  capability: ActivityProviderAuthoringCapability;
  providerKey: string;
  schemaVersion: string;
  contribution?: StudioActivityDefinitionImplementationEditorContribution;
  label: string;
}

export function ActivityDefinitionCreateDialog({ context, activityEditors, onClose, onCreated }: {
  context: StudioEndpointContext;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  onClose(): void;
  onCreated(response: CreateActivityDefinitionResponse): void;
}) {
  const capabilitiesQuery = useActivityAuthoringCapabilities(context);
  const catalogQuery = useWorkflowActivities(context);
  const catalog = useMemo(() => catalogQuery.data?.activities ?? [], [catalogQuery.data?.activities]);
  const categorySuggestions = useMemo(() => deriveActivityCategorySuggestions(catalog), [catalog]);
  const choices = useMemo(() => createProviderChoices(capabilitiesQuery.data?.providers ?? [], activityEditors), [activityEditors, capabilitiesQuery.data?.providers]);
  const hasSingleUsableProvider = choices.filter(choice => Boolean(choice.contribution)).length === 1;
  const [selected, setSelected] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [category, setCategory] = useState("Custom");
  const [rootActivityVersionId, setRootActivityVersionId] = useState<string | null>(null);
  const [description, setDescription] = useState("");
  const [advanced, setAdvanced] = useState(false);
  const [activityTypeKey, setActivityTypeKey] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const displayNameRef = useRef<HTMLInputElement>(null);
  const dialogRef = useRef<HTMLElement>(null);

  useDialogFocus(dialogRef, submitting ? null : onClose);

  useEffect(() => {
    const defaultChoice = defaultProviderChoice(choices);
    setSelected(defaultChoice ? choiceValue(defaultChoice) : "");
    if (choices.length > 0) displayNameRef.current?.focus();
  }, [choices]);

  const choice = choices.find(item => choiceValue(item) === selected);
  const isGraphCreation = choice?.providerKey === "elsa.activity-graph";
  useEffect(() => {
    if (!isGraphCreation) {
      setRootActivityVersionId(null);
      return;
    }
    setRootActivityVersionId(current => current && catalog.some(activity => activity.activityVersionId === current)
      ? current
      : catalog.find(activity => activity.activityTypeKey.endsWith(".Flowchart") || activity.displayName === "Flowchart")?.activityVersionId ?? null);
  }, [catalog, isGraphCreation]);
  const rules = capabilitiesQuery.data?.activityTypeKeyRules;
  const canSubmit = Boolean(choice?.contribution && displayName.trim() && category.trim() && !submitting && (!isGraphCreation || rootActivityVersionId));

  const submit = async () => {
    if (!choice?.contribution || !capabilitiesQuery.data || !canSubmit) return;
    setSubmitting(true);
    setError(null);
    observeActivityDefinitions({ event: "create-start", surface: "creation", providerKey: choice.providerKey, providerSchemaVersion: choice.schemaVersion });
    try {
      const baseImplementation = choice.contribution.createInitialImplementation();
      const initialGraph = isGraphCreation
        ? createGraphInitialState(baseImplementation, catalog, rootActivityVersionId, choice.capability.requiredOutcomes)
        : null;
      const implementation = isGraphCreation ? initialGraph?.implementation ?? null : baseImplementation;
      if (!implementation) throw new Error("The selected graph composition is unavailable in the authorized activity catalog.");
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
          outcomes: initialGraph?.outcomes ?? choice.capability.requiredOutcomes.map(outcome => ({ ...outcome, description: outcome.description ?? null }))
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
    <section ref={dialogRef} className="ad-dialog ad-create-dialog" role="dialog" aria-modal="true" aria-labelledby="create-activity-definition-title" tabIndex={-1}>
      <header><div><span className="ad-kicker">Design-owned reusable activity</span><h2 id="create-activity-definition-title">Create Activity Definition</h2></div><button type="button" className="ad-icon-button" aria-label="Close" onClick={onClose} disabled={submitting}><X size={18} /></button></header>
      {capabilitiesQuery.isPending ? <div className="ad-inline-status" role="status">Loading authorized authoring providers…</div> : null}
      {capabilitiesQuery.isError ? <div className="ad-inline-error" role="alert">Authoring capabilities are unavailable. Studio will not guess a provider or contract.</div> : null}
      {capabilitiesQuery.data && choices.length === 0 ? <div className="ad-inline-error" role="alert">No authorable implementation provider is currently advertised.</div> : null}
      {choices.length ? <div className="ad-form-grid">
        {!hasSingleUsableProvider ? <label><span>Implementation type</span><select name="provider" value={selected} onChange={event => setSelected(event.target.value)}><option value="" disabled>Select an implementation type</option>{choices.map(item => <option key={choiceValue(item)} value={choiceValue(item)}>{item.label}</option>)}</select></label> : null}
        {choice && !choice.contribution ? <div className="ad-inline-error" role="alert"><AlertTriangle size={17} aria-hidden /> The backend advertises this provider, but its exact Studio editor contribution is unavailable. Creation is disabled.</div> : null}
        <label><span>Display name</span><input ref={displayNameRef} name="displayName" value={displayName} onChange={event => setDisplayName(event.target.value)} aria-label="Display name" placeholder="e.g. Send invoice reminder" /></label>
        <ActivityCategoryCombobox value={category} suggestions={categorySuggestions} onChange={setCategory} />
        {isGraphCreation ? <fieldset className="ad-form-wide ad-composition-picker wf-tokens"><legend>Graph composition</legend>{catalogQuery.isPending ? <p className="ad-inline-status" role="status">Loading the authorized graph composition catalog…</p> : catalogQuery.isError ? <p className="ad-inline-error" role="alert">Graph compositions could not be loaded. Creation is disabled until the authorized catalog is available.</p> : <WorkflowRootRadioCards catalog={catalog} value={rootActivityVersionId} onChange={setRootActivityVersionId} />}</fieldset> : null}
        <label className="ad-form-wide"><span>Description <small>Optional</small></span><textarea name="description" value={description} onChange={event => setDescription(event.target.value)} rows={3} aria-label="Description" placeholder="Optional summary of what this activity does" /></label>
        <details className="ad-form-wide ad-create-advanced" onToggle={event => { const isOpen = event.currentTarget.open; setAdvanced(isOpen); if (!isOpen) setActivityTypeKey(""); }}>
          <summary>Advanced</summary>
          <div className="ad-create-advanced-content">
            <div className="ad-key-summary"><strong>Activity Type Key</strong><span>{rules?.serverGenerated ? `Generated by the backend under ${rules.prefix}.` : "Defined by the backend authoring contract."} It is immutable after creation.</span></div>
            {rules?.allowsPreCreationOverride ? <label className="ad-form-wide"><span>Exact Activity Type Key</span><input name="activityTypeKey" value={activityTypeKey} onChange={event => setActivityTypeKey(event.target.value)} placeholder={`${rules.prefix}.team-name.activity-name`} maxLength={rules.maximumLength} /><small>Must match the advertised namespace rules. Collisions fail; Studio never adds a suffix.</small></label> : null}
          </div>
        </details>
      </div> : null}
      {error ? <div className="ad-inline-error" role="alert">{error}</div> : null}
      <footer><button type="button" onClick={onClose} disabled={submitting}>Cancel</button><button type="button" className="ad-primary-action" onClick={() => void submit()} disabled={!canSubmit}>{submitting ? "Creating…" : "Create definition"}</button></footer>
    </section>
  </div>;
}

export function createProviderChoices(
  providers: ActivityProviderAuthoringCapability[],
  contributions: StudioActivityDefinitionImplementationEditorContribution[]
) {
  return providers.flatMap(capability => {
    const authorableSchemas = capability.manifestSchemas
      .filter(schema => schema.isAuthorable)
      .sort((left, right) => compareSchemaVersions(right.schemaVersion, left.schemaVersion));
    const supportedSchema = authorableSchemas.find(schema => contributions.some(item =>
      item.providerKey === capability.providerKey && item.providerSchemaVersion === schema.schemaVersion));
    const schema = supportedSchema ?? authorableSchemas[0];
    if (!schema) return [];
    return [{
      capability,
      providerKey: capability.providerKey,
      schemaVersion: schema.schemaVersion,
      contribution: contributions.find(item => item.providerKey === capability.providerKey && item.providerSchemaVersion === schema.schemaVersion),
      label: capability.displayName.trim() || capability.providerKey
    }];
  });
}

export function defaultProviderChoice(choices: ProviderChoice[]) {
  const usableChoices = choices.filter(choice => Boolean(choice.contribution));
  return usableChoices.length === 1 ? usableChoices[0] : null;
}

export function isActivityGraphOnlyChoice(choices: ProviderChoice[]) {
  return choices.length === 1 && choices[0].providerKey === "elsa.activity-graph";
}

export function createGraphInitialImplementation(
  implementation: StudioActivityDefinitionImplementationState,
  catalog: ActivityCatalogItem[],
  rootActivityVersionId: string | null
): StudioActivityDefinitionImplementationState | null {
  return createGraphInitialState(implementation, catalog, rootActivityVersionId, [])?.implementation ?? null;
}

export function createGraphInitialState(
  implementation: StudioActivityDefinitionImplementationState,
  catalog: ActivityCatalogItem[],
  rootActivityVersionId: string | null,
  requiredOutcomes: ActivityOutcomeContract[]
): { implementation: StudioActivityDefinitionImplementationState; outcomes: ActivityOutcomeContract[] } | null {
  if (!rootActivityVersionId) return null;
  const rootDescriptor = catalog.find(item => item.activityVersionId === rootActivityVersionId);
  if (!rootDescriptor?.authoringTemplate) return null;
  const rootActivity = canonicalizeStateForWire({ rootActivity: createActivityNode(rootDescriptor, "root") }).rootActivity!;
  const payload = implementation.payload && typeof implementation.payload === "object" && !Array.isArray(implementation.payload)
    ? implementation.payload as Record<string, unknown>
    : {};
  const supportsOutcomeMappings = Object.prototype.hasOwnProperty.call(payload, "outcomeMappings");
  const graphOutcomes = supportsOutcomeMappings
    ? initializeGraphBoundaryOutcomes(rootActivity, catalog.find(item => item.activityVersionId === rootActivity.activityVersionId), requiredOutcomes)
    : null;
  return {
    implementation: {
      ...implementation,
      payload: {
        ...payload,
        rootActivity,
        ...(graphOutcomes ? { outcomeMappings: graphOutcomes.mappings } : {})
      }
    },
    outcomes: graphOutcomes?.outcomes ?? requiredOutcomes.map(outcome => ({ ...outcome, description: outcome.description ?? null }))
  };
}

export function initializeGraphBoundaryOutcomes(
  rootActivity: ActivityNode,
  catalogItem: ActivityCatalogItem | undefined,
  requiredOutcomes: ActivityOutcomeContract[]
) {
  const outcomes = requiredOutcomes.map(outcome => ({ ...outcome, description: outcome.description ?? null }));
  const sourceOutcomes = getActivityGraphOutcomeMappingOptions(rootActivity, catalogItem, { outcomes }).sourceOutcomes;
  const mappings = outcomes.flatMap(outcome => outcome.isEmitted && sourceOutcomes.some(source => source.referenceKey === outcome.referenceKey)
    ? [{ sourceOutcomeReferenceKey: outcome.referenceKey, boundaryOutcomeReferenceKey: outcome.referenceKey }]
    : []);

  if (outcomes.length === 0) {
    const choice = defaultActivityGraphOutcomeChoice(sourceOutcomes);
    if (choice) {
      outcomes.push({ referenceKey: choice.referenceKey, name: choice.name, isEmitted: true, description: null });
      mappings.push({ sourceOutcomeReferenceKey: choice.referenceKey, boundaryOutcomeReferenceKey: choice.referenceKey });
    }
  }

  return { outcomes, mappings };
}

function choiceValue(choice: Pick<ProviderChoice, "providerKey" | "schemaVersion">) {
  return `${choice.providerKey}|${choice.schemaVersion}`;
}

function compareSchemaVersions(left: string, right: string) {
  return left.localeCompare(right, undefined, { numeric: true, sensitivity: "base" });
}

export function ActivityCategoryCombobox({ value, suggestions, onChange }: {
  value: string;
  suggestions: string[];
  onChange(value: string): void;
}) {
  return <EditableCombobox
    label="Category"
    name="category"
    value={value}
    suggestions={suggestions}
    onChange={onChange}
    placeholder="e.g. Custom"
  />;
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
