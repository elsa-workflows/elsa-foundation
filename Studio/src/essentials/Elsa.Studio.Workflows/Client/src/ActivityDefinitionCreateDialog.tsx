import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { AlertTriangle, X } from "lucide-react";
import { StudioHttpError, type StudioActivityDefinitionImplementationEditorContribution, type StudioActivityDefinitionImplementationState, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityProviderAuthoringCapability, CreateActivityDefinitionResponse } from "./activityDefinitionTypes";
import type { ActivityCatalogItem } from "./workflowTypes";
import { createActivityDefinition, useActivityAuthoringCapabilities, useWorkflowActivities } from "./api/activityDesign";
import { observeActivityDefinitions } from "./activityDefinitionObservability";
import { deriveActivityCategorySuggestions } from "./activityCategories";
import { getCreateInitialState } from "./workflow-editor/editorHelpers";
import { WorkflowRootRadioCards } from "./workflow-editor/WorkflowRootCards";
import { useDialogFocus } from "./workflow-editor/useDialogFocus";

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
    if (defaultChoice) setSelected(choiceValue(defaultChoice));
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
      const implementation = isGraphCreation
        ? createGraphInitialImplementation(baseImplementation, catalog, rootActivityVersionId)
        : baseImplementation;
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
        {!isActivityGraphOnlyChoice(choices) ? <label><span>Implementation type</span><select name="provider" value={selected} onChange={event => setSelected(event.target.value)}><option value="" disabled>Select an implementation type</option>{choices.map(item => <option key={choiceValue(item)} value={choiceValue(item)}>{item.label}</option>)}</select></label> : null}
        {choice && !choice.contribution ? <div className="ad-inline-error" role="alert"><AlertTriangle size={17} aria-hidden /> The backend advertises this provider, but its exact Studio editor contribution is unavailable. Creation is disabled.</div> : null}
        <label><span>Display name</span><input ref={displayNameRef} name="displayName" value={displayName} onChange={event => setDisplayName(event.target.value)} aria-label="Display name" placeholder="e.g. Send invoice reminder" /></label>
        <ActivityCategoryCombobox value={category} suggestions={categorySuggestions} onChange={setCategory} />
        {isGraphCreation ? <fieldset className="ad-form-wide ad-composition-picker"><legend>Graph composition</legend>{catalogQuery.isPending ? <p className="ad-inline-status" role="status">Loading the authorized graph composition catalog…</p> : <WorkflowRootRadioCards catalog={catalog} value={rootActivityVersionId} onChange={setRootActivityVersionId} />}</fieldset> : null}
        <label className="ad-form-wide"><span>Description <small>Optional</small></span><textarea name="description" value={description} onChange={event => setDescription(event.target.value)} rows={3} aria-label="Description" placeholder="Optional summary of what this activity does" /></label>
        <div className="ad-form-wide ad-key-summary"><strong>Activity Type Key</strong><span>{rules?.serverGenerated ? `Generated by the backend under ${rules.prefix}.` : "Defined by the backend authoring contract."} It is immutable after creation.</span>{rules?.allowsPreCreationOverride ? <button type="button" className="ad-link-button" onClick={() => setAdvanced(current => { if (current) setActivityTypeKey(""); return !current; })}>{advanced ? "Use generated key" : "Set an exact key"}</button> : null}</div>
        {advanced && rules?.allowsPreCreationOverride ? <label className="ad-form-wide"><span>Exact Activity Type Key</span><input name="activityTypeKey" value={activityTypeKey} onChange={event => setActivityTypeKey(event.target.value)} placeholder={`${rules.prefix}.team-name.activity-name`} maxLength={rules.maximumLength} /><small>Must match the advertised namespace rules. Collisions fail; Studio never adds a suffix.</small></label> : null}
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
  return isActivityGraphOnlyChoice(choices) ? choices[0] : null;
}

export function isActivityGraphOnlyChoice(choices: ProviderChoice[]) {
  return choices.length === 1 && choices[0].providerKey === "elsa.activity-graph";
}

export function createGraphInitialImplementation(
  implementation: StudioActivityDefinitionImplementationState,
  catalog: ActivityCatalogItem[],
  rootActivityVersionId: string | null
): StudioActivityDefinitionImplementationState | null {
  if (!rootActivityVersionId) return null;
  const initialState = getCreateInitialState({ name: "", description: "", rootActivityVersionId }, catalog);
  if (!initialState?.rootActivity) return null;
  const payload = implementation.payload && typeof implementation.payload === "object" && !Array.isArray(implementation.payload)
    ? implementation.payload as Record<string, unknown>
    : {};
  return {
    ...implementation,
    payload: {
      ...payload,
      rootActivity: { ...initialState.rootActivity, nodeId: "root" }
    }
  };
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
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const listboxId = useId();
  const filteredSuggestions = useMemo(() => {
    const normalized = value.trim().toLocaleLowerCase();
    return normalized
      ? suggestions.filter(suggestion => suggestion.toLocaleLowerCase().includes(normalized))
      : suggestions;
  }, [suggestions, value]);
  const activeSuggestion = filteredSuggestions[activeIndex];
  const choose = (suggestion: string) => {
    onChange(suggestion);
    setOpen(false);
    setActiveIndex(-1);
  };
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setOpen(true);
      setActiveIndex(current => Math.min(Math.max(current + 1, 0), filteredSuggestions.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setOpen(true);
      setActiveIndex(current => current <= 0 ? filteredSuggestions.length - 1 : current - 1);
    } else if (event.key === "Enter" && open && activeSuggestion) {
      event.preventDefault();
      choose(activeSuggestion);
    } else if (event.key === "Escape") {
      setOpen(false);
      setActiveIndex(-1);
    }
  };

  return <label className="ad-category-combobox"><span>Category</span><div>
    <input
      name="category"
      role="combobox"
      aria-label="Category"
      aria-autocomplete="list"
      aria-controls={listboxId}
      aria-haspopup="listbox"
      aria-expanded={open && filteredSuggestions.length > 0}
      aria-activedescendant={activeSuggestion ? `${listboxId}-${activeIndex}` : undefined}
      value={value}
      onChange={event => { onChange(event.target.value); setOpen(true); setActiveIndex(-1); }}
      onFocus={() => setOpen(true)}
      onBlur={() => { setOpen(false); setActiveIndex(-1); }}
      onKeyDown={onKeyDown}
      placeholder="e.g. Custom"
    />
    {open && filteredSuggestions.length ? <ul id={listboxId} role="listbox" aria-label="Existing categories">{filteredSuggestions.map((suggestion, index) => <li key={suggestion} role="none"><button id={`${listboxId}-${index}`} role="option" aria-selected={index === activeIndex} tabIndex={-1} type="button" onMouseDown={event => event.preventDefault()} onClick={() => choose(suggestion)}>{suggestion}</button></li>)}</ul> : null}
  </div></label>;
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
