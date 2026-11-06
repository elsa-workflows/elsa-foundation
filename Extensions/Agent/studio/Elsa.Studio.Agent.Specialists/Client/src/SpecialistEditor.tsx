import React, { useEffect, useId, useRef, useState } from "react";
import { StudioHttpError, type StudioActivityDefinitionImplementationEditorProps } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton, StudioField } from "@elsa-workflows/studio-ui";
import {
  connectionPickerLimit, listModelConnections, listModelProviders, readModelCapabilities,
  type ModelCapabilities, type ModelConnection, type ModelProvider
} from "./agentsApi";
import {
  continuitySchemaVersion, readSpecialistManifest, record, secondsToMilliseconds, specialistBounds, specialistProblems, unreadableMembers
} from "./specialistManifest";
import { playgroundPath } from "./SpecialistPlayground";

type Remote<T> = { kind: "idle" } | { kind: "loading" } | { kind: "ready"; value: T } | { kind: "denied" } | { kind: "unusable" } | { kind: "unavailable" };
type NumberField = "temperature" | "tokens" | "calls" | "timeout";
type ConnectionGroup = { provider: ModelProvider; items: ModelConnection[] };

/** A refused read is a permission answer, a 422 an unusable model, anything else a failure worth retrying. */
function failed<T>(error: unknown): Remote<T> {
  if (error instanceof StudioHttpError && (error.status === 401 || error.status === 403)) return { kind: "denied" };
  if (error instanceof StudioHttpError && error.status === 422) return { kind: "unusable" };
  return { kind: "unavailable" };
}

const optionValue = (providerKey: string, connectionName: string) => `${encodeURIComponent(providerKey)}|${encodeURIComponent(connectionName)}`;

/**
 * The specialist implementation form inside the generic activity editor. The generic editor owns the public
 * inputs/results, revision, save, conflict and history; this form only patches the implementation payload through
 * onChange, member by member, so values it does not edit stay as saved. Model credentials stay in Connections, and only
 * options the selected model declares are offered.
 */
export function SpecialistEditor({ context, value, readOnly, onChange, providerSchemaVersion, historyResetKey, draftId }: StudioActivityDefinitionImplementationEditorProps) {
  const raw = record(value.payload);
  const manifest = readSpecialistManifest(raw, providerSchemaVersion);
  const patch = (changes: Record<string, unknown>) => onChange({ ...value, payload: { ...raw, ...changes } });
  const patchLimits = (changes: Record<string, unknown>) => patch({ limits: { ...record(raw.limits), ...changes } });
  const id = useId();

  const [reload, setReload] = useState(0);
  const [providers, setProviders] = useState<Remote<ModelProvider[]>>({ kind: "loading" });
  useEffect(() => {
    const abort = new AbortController();
    setProviders({ kind: "loading" });
    listModelProviders(context, abort.signal)
      .then(items => setProviders({ kind: "ready", value: items }))
      .catch(error => { if (!abort.signal.aborted) setProviders(failed(error)); });
    return () => abort.abort();
  }, [context, reload]);

  // One list of connections grouped by provider, so choosing a connection chooses its provider too.
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  useEffect(() => { const handle = window.setTimeout(() => setQuery(search.trim()), 250); return () => window.clearTimeout(handle); }, [search]);
  const providerList = providers.kind === "ready" ? providers.value : null;
  const [connections, setConnections] = useState<Remote<ConnectionGroup[]>>({ kind: "idle" });
  useEffect(() => {
    if (!providerList) { setConnections({ kind: "idle" }); return; }
    const abort = new AbortController();
    setConnections({ kind: "loading" });
    Promise.all(providerList.map(async provider => ({ provider, items: await listModelConnections(context, provider.connectionTypeName, query, abort.signal) })))
      .then(groups => setConnections({ kind: "ready", value: groups }))
      .catch(error => { if (!abort.signal.aborted) setConnections(failed(error)); });
    return () => abort.abort();
  }, [context, providerList, query, reload]);

  const { providerKey, connectionName, modelId } = manifest.model;
  const [capabilities, setCapabilities] = useState<Remote<ModelCapabilities>>({ kind: "idle" });
  useEffect(() => {
    if (!providerKey || !connectionName || !modelId) { setCapabilities({ kind: "idle" }); return; }
    const abort = new AbortController();
    setCapabilities({ kind: "loading" });
    readModelCapabilities(context, { providerKey, connectionName, modelId }, abort.signal)
      .then(result => setCapabilities({ kind: "ready", value: result }))
      .catch(error => { if (!abort.signal.aborted) setCapabilities(failed(error)); });
    return () => abort.abort();
  }, [context, providerKey, connectionName, modelId, reload]);

  const provider = providerList?.find(item => item.providerKey === providerKey);
  // The last listed connections stay visible while a search loads, so the list and its search box do not flicker away.
  const lastGroups = useRef<ConnectionGroup[]>([]);
  if (connections.kind === "ready") lastGroups.current = connections.value;
  const groups = connections.kind === "ready" || connections.kind === "loading" ? lastGroups.current : [];
  const selected = groups.find(group => group.provider.providerKey === providerKey)?.items.find(item => item.name === connectionName);
  const savedMissing = Boolean(connectionName) && !selected;
  // Once offered, the search stays: clearing or narrowing it must not unmount the field being typed in.
  const searchOffered = useRef(false);
  if (groups.some(group => group.items.length >= connectionPickerLimit) || search !== "" || query !== "") searchOffered.current = true;
  const searchable = searchOffered.current;
  const model = capabilities.kind === "ready" ? capabilities.value : null;
  const settingProperties = model?.settingsSchema?.properties ?? {};
  const temperature = settingProperties.temperature;
  const savedSettings = record(raw.settings);
  const unsupportedSettings = model ? Object.entries(savedSettings).filter(([key]) => !Object.hasOwn(settingProperties, key)) : [];
  const problems = specialistProblems(manifest, model ? { maximumOutputTokens: model.maximumOutputTokens, temperature } : null);
  const unreadable = unreadableMembers(raw);

  // Numbers keep their local text, so input that cannot be saved is explained rather than silently replaced.
  const saved = numberText(raw);
  const savedKey = JSON.stringify(saved);
  const [numbers, setNumbers] = useState(saved);
  const previousSaved = useRef(saved);
  useEffect(() => {
    const next = JSON.parse(savedKey) as typeof saved;
    const previous = previousSaved.current;
    previousSaved.current = next;
    // Only a field whose saved value changed takes it, and not when its text already means that value (such as "0.").
    setNumbers(current => Object.fromEntries((Object.keys(next) as NumberField[]).map(field =>
      [field, next[field] === previous[field] || (current[field].trim() !== "" && next[field] !== "" && Number(current[field]) === Number(next[field])) ? current[field] : next[field]])) as typeof saved);
  }, [savedKey]);
  const savedKeyRef = useRef(savedKey);
  savedKeyRef.current = savedKey;
  useEffect(() => { previousSaved.current = JSON.parse(savedKeyRef.current); setNumbers(JSON.parse(savedKeyRef.current)); }, [historyResetKey]);

  const parse = (field: NumberField, input: string): number | null | undefined => {
    const trimmed = input.trim();
    if (!trimmed) return field === "tokens" || field === "temperature" ? null : undefined;
    if (field === "timeout") return secondsToMilliseconds(trimmed) ?? undefined;
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed)) return undefined;
    return field === "temperature" || Number.isInteger(parsed) ? parsed : undefined;
  };
  const setNumber = (field: NumberField, input: string) => {
    setNumbers(current => ({ ...current, [field]: input }));
    const parsed = parse(field, input);
    if (parsed === undefined) return;
    if (field === "temperature") {
      const { temperature: _, ...settings } = savedSettings;
      patch({ settings: parsed === null ? settings : { ...savedSettings, temperature: parsed } });
    } else if (field === "tokens") {
      const { maximumOutputTokens: _, ...limits } = record(raw.limits);
      patch({ limits: parsed === null ? limits : { ...limits, maximumOutputTokens: parsed } });
    } else patchLimits(field === "calls" ? { maximumModelCalls: parsed } : { timeoutMilliseconds: parsed });
  };
  const notSaved = (field: NumberField) => parse(field, numbers[field]) === undefined
    ? `Not saved: enter ${field === "temperature" ? "a number" : field === "timeout" ? "a number of seconds" : "a whole number"}.${saved[field] ? ` The saved value ${saved[field]} is kept.` : ""}`
    : null;
  const fieldMessage = (field: NumberField, problem?: string) => notSaved(field) ?? problem ?? null;

  const numberInput = (field: NumberField, label: string, description: React.ReactNode, problem: string | undefined, attributes: React.InputHTMLAttributes<HTMLInputElement>) => {
    const message = fieldMessage(field, problem);
    return (
      <StudioField label={label} description={description}>
        <input {...attributes} data-specialist-field={field} type="text" inputMode={field === "temperature" || field === "timeout" ? "decimal" : "numeric"} value={numbers[field]} disabled={readOnly}
          aria-invalid={message ? true : undefined} aria-describedby={message ? `${id}-${field}-problem` : undefined}
          onChange={event => setNumber(field, event.target.value)} />
        {message ? <small id={`${id}-${field}-problem`} className="agent-field-problem">{message}</small> : null}
      </StudioField>
    );
  };

  const advancedHasProblems = ["tokens", "calls", "timeout"].some(field => fieldMessage(field as NumberField,
    field === "tokens" ? problems.outputTokens : field === "calls" ? problems.modelCalls : problems.timeout));
  const [advancedOpen, setAdvancedOpen] = useState(false);
  useEffect(() => { if (advancedHasProblems) setAdvancedOpen(true); }, [advancedHasProblems]);

  const retry = <StudioButton size="sm" onClick={() => setReload(current => current + 1)}>Retry</StudioButton>;

  return (
    <div className="agent-specialist-editor" data-testid="specialist-editor">
      {draftId ? <p className="agent-specialist-editor__playground"><a href={playgroundPath(draftId)}>Test the saved revision in the playground</a></p> : null}
      {unreadable.unknown.length > 0 || unreadable.malformed.length > 0 ? (
        <StudioAlert tone="warning">
          The saved settings contain {[...unreadable.unknown, ...unreadable.malformed].join(", ")}, which agents do not accept, so validation and publication will refuse them.{" "}
          {!readOnly && unreadable.unknown.length > 0 ? <StudioButton size="sm" onClick={() => onChange({ ...value, payload: Object.fromEntries(Object.entries(raw).filter(([key]) => !unreadable.unknown.includes(key))) })}>Remove unknown settings</StudioButton> : null}
        </StudioAlert>
      ) : null}

      <div className="agent-specialist-main">
      <div className="agent-specialist-instructions">
      <StudioField label="Instructions" description="Describe the task, tone and constraints. Workflow inputs supply the data for each run.">
        <textarea data-specialist-field="instructions" value={manifest.instructions} disabled={readOnly} rows={8} maxLength={specialistBounds.instructionsLength}
          aria-invalid={problems.instructions ? true : undefined} aria-describedby={problems.instructions ? `${id}-instructions-problem` : undefined}
          onChange={event => patch({ instructions: event.target.value })} />
        {problems.instructions ? <small id={`${id}-instructions-problem`} className="agent-field-problem">{problems.instructions}</small> : null}
      </StudioField>

      </div>
      {/* Not disabled as a whole: Retry and search must work in read-only mode, and every editing control is disabled on its own. */}
      <fieldset className="agent-specialist-model" data-specialist-field="model-group" tabIndex={-1}>
        <legend>Model</legend>
        {providers.kind === "denied" ? <StudioAlert tone="info">You can see the saved model, but choosing a model needs permission to author agents.</StudioAlert> : null}
        {providers.kind === "unavailable" ? <StudioAlert tone="warning">Model providers could not be loaded. The saved model is kept. {retry}</StudioAlert> : null}
        {providerList?.length === 0 ? <StudioAlert tone="info">No model provider for agents is registered on this server.</StudioAlert> : null}
        {searchable ? (
          <StudioField label="Search connections" description="Only the first matches are listed.">
            <input type="search" value={search} onChange={event => setSearch(event.target.value)} />
          </StudioField>
        ) : null}
        <StudioField label="Model connection" description="Credentials and the model or deployment are managed in Connections.">
          <select data-specialist-field="model" value={connectionName ? optionValue(providerKey, connectionName) : ""} disabled={readOnly || connections.kind !== "ready"}
            aria-invalid={problems.model ? true : undefined} aria-describedby={problems.model ? `${id}-model-problem` : undefined}
            onChange={event => {
              const choice = groups.flatMap(group => group.items.map(item => ({ group, item }))).find(({ group, item }) => optionValue(group.provider.providerKey, item.name) === event.target.value);
              if (choice) patch({ model: { ...record(raw.model), providerKey: choice.group.provider.providerKey, connectionName: choice.item.name, modelId: choice.item.modelId ?? "" } });
              else if (!event.target.value) patch({ model: { ...record(raw.model), providerKey: "", connectionName: "", modelId: "" } });
            }}>
            <option value="">Choose a connection</option>
            {groups.filter(group => group.items.length > 0).map(group => (
              <optgroup key={group.provider.providerKey} label={group.provider.displayName}>
                {group.items.map(item => (
                  <option key={item.name} value={optionValue(group.provider.providerKey, item.name)} disabled={!item.modelId}>
                    {item.displayName}{item.modelId ? ` · ${item.modelId}` : " · no model"}{item.status && item.status.toLowerCase() !== "ready" ? ` (${item.status})` : ""}
                  </option>
                ))}
              </optgroup>
            ))}
            {savedMissing ? <option value={optionValue(providerKey, connectionName)}>{connectionName} (saved)</option> : null}
          </select>
          {problems.model ? <small id={`${id}-model-problem`} className="agent-field-problem">{problems.model}</small> : null}
        </StudioField>
        {connections.kind === "ready" && groups.every(group => group.items.length === 0) ? <StudioAlert tone="info">{query ? `No model connections match “${query}”.` : "There are no model connections you can use yet. Create one in Connections, then choose it here."}</StudioAlert> : null}
        {connections.kind === "denied" ? <StudioAlert tone="info">Listing connections needs permission to read workflow designs or manage connections. The saved connection is kept.</StudioAlert> : null}
        {connections.kind === "unavailable" ? <StudioAlert tone="warning">Connections could not be loaded. The saved connection is kept. {retry}</StudioAlert> : null}
        {savedMissing && connections.kind === "ready" ? <StudioAlert tone="warning">The saved connection “{connectionName}” is not {query ? "among the connections matching this search" : "available to you"}. It is kept until you choose another one.</StudioAlert> : null}
        {selected && selected.modelId && selected.modelId !== modelId ? (
          <StudioAlert tone="warning">
            This connection now uses {selected.modelId}, but the agent still names {modelId || "no model"}.{" "}
            {!readOnly ? <StudioButton size="sm" onClick={() => patch({ model: { ...record(raw.model), modelId: selected.modelId } })}>Use {selected.modelId}</StudioButton> : null}
          </StudioAlert>
        ) : null}
        {capabilities.kind === "denied" ? <StudioAlert tone="info">Reading what this model supports needs permission to author agents.</StudioAlert> : null}
        {capabilities.kind === "unusable" ? <StudioAlert tone="warning">This model connection cannot be used by agents right now, so testing and publishing are unavailable. Check the connection in Connections, or choose another one. {retry}</StudioAlert> : null}
        {capabilities.kind === "unavailable" ? <StudioAlert tone="warning">What this model supports could not be read. {retry}</StudioAlert> : null}
        {model && !model.supportsJsonSchema ? <StudioAlert tone="warning">This model does not declare structured JSON results, so the agent cannot be published with it.</StudioAlert> : null}
        {modelId || connectionName ? (
          <p className="agent-model-summary" data-testid="specialist-model">
            Model: <code>{modelId || "none"}</code>{provider ? ` from ${provider.displayName}` : ""}{model ? ` · up to ${model.maximumOutputTokens.toLocaleString()} output tokens` : ""}
          </p>
        ) : null}
        {temperature ? numberInput("temperature", "Temperature", `Optional.${temperature.minimum != null || temperature.maximum != null ? ` This model accepts ${[temperature.minimum != null ? `from ${temperature.minimum}` : "", temperature.maximum != null ? `up to ${temperature.maximum}` : ""].filter(Boolean).join(" ")}.` : ""}`,
          problems.temperature, { min: temperature.minimum, max: temperature.maximum }) : null}
        {unsupportedSettings.length > 0 ? (
          <StudioAlert tone="warning">
            This model does not support the saved {unsupportedSettings.map(([key, setting]) => `${key} (${JSON.stringify(setting)})`).join(", ")} setting.{" "}
            {!readOnly ? <StudioButton size="sm" onClick={() => patch({ settings: Object.fromEntries(Object.entries(savedSettings).filter(([key]) => Object.hasOwn(settingProperties, key))) })}>Remove unsupported settings</StudioButton> : null}
          </StudioAlert>
        ) : null}
        {!model && Object.keys(savedSettings).length > 0 ? <p className="agent-model-summary">Saved model settings: {Object.entries(savedSettings).map(([key, setting]) => `${key} ${JSON.stringify(setting)}`).join(", ")}</p> : null}
      </fieldset>

      </div>
      <details className="agent-specialist-advanced" open={advancedOpen} onToggle={event => setAdvancedOpen(event.currentTarget.open)}>
        <summary>Advanced settings<span>Limits and conversation</span></summary>
      <fieldset className="agent-specialist-limits" disabled={readOnly}>
        <legend>Limits</legend>
        {numberInput("tokens", "Maximum output tokens", model ? `Optional. At most ${model.maximumOutputTokens.toLocaleString()} for this model.` : "Optional.", problems.outputTokens, { min: 1, max: model?.maximumOutputTokens })}
        {numberInput("calls", "Maximum model calls", "Per invocation.", problems.modelCalls, { min: specialistBounds.modelCalls.minimum, max: specialistBounds.modelCalls.maximum })}
        {numberInput("timeout", "Timeout (seconds)", undefined, problems.timeout, { min: specialistBounds.timeoutSeconds.minimum, max: specialistBounds.timeoutSeconds.maximum })}
      </fieldset>

      {providerSchemaVersion === continuitySchemaVersion ? (
        <label className="agent-specialist-continuity">
          <input type="checkbox" checked={manifest.allowContinuity === true} disabled={readOnly}
            onChange={event => patch({ allowContinuity: event.target.checked })} />
          <span>Allow callers to continue a conversation. Each invocation is fresh unless a caller explicitly continues one.</span>
        </label>
      ) : null}
      </details>
    </div>
  );
}

/** The saved values as text, including saved values the form cannot accept, so they stay inspectable. */
function numberText(raw: Record<string, unknown>): Record<NumberField, string> {
  const limits = record(raw.limits);
  const settings = record(raw.settings);
  const show = (value: unknown, fallback = "") => value === undefined || value === null ? fallback : typeof value === "number" ? String(value) : JSON.stringify(value);
  return {
    temperature: show(settings.temperature),
    tokens: show(limits.maximumOutputTokens),
    calls: show(limits.maximumModelCalls, String(specialistBounds.modelCalls.default)),
    timeout: typeof limits.timeoutMilliseconds === "number" ? String(limits.timeoutMilliseconds / 1000) : show(limits.timeoutMilliseconds, String(specialistBounds.timeoutSeconds.default))
  };
}
