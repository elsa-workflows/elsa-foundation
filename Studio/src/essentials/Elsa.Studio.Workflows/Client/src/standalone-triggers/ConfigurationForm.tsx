import { Component, useState, type FormEvent, type ReactNode } from "react";
import type { StudioTriggerConfigurationEditorContribution } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton, StudioField } from "@elsa-workflows/studio-ui";
import { Findings } from "./shared";
import { parseLiteralJson, serializeTriggerPayload } from "./json";
import type { AdmissionPolicy, ConfigurationField, HttpMode, JsonValue, TriggerTypeDescriptor, TriggerView } from "./types";

export interface ConfigurationDraft { name: string; description: string; configuration: Record<string, JsonValue>; http?: { mode: HttpMode; admissionPolicy?: AdmissionPolicy } }
export function ConfigurationForm({ descriptor, trigger, initialDraft, editors = [], busy, onCancel, onSubmit }: {
  descriptor: TriggerTypeDescriptor; trigger?: TriggerView; initialDraft?: ConfigurationDraft; editors?: readonly StudioTriggerConfigurationEditorContribution[]; busy?: boolean; onCancel(): void; onSubmit(draft: ConfigurationDraft): void;
}) {
  const [name, setName] = useState(initialDraft?.name ?? trigger?.name ?? "");
  const [description, setDescription] = useState(initialDraft?.description ?? trigger?.description ?? "");
  const [fields, setFields] = useState<Record<string, string>>(() => Object.fromEntries(descriptor.configurationFields.map(field => [field.key, fieldText(initialDraft?.configuration[field.key] ?? trigger?.configuration[field.key] ?? field.defaultValue)])));
  const [mode, setMode] = useState<HttpMode | "">(initialDraft?.http?.mode ?? trigger?.http?.mode ?? "");
  const [policy, setPolicy] = useState<AdmissionPolicy>(initialDraft?.http?.admissionPolicy ?? trigger?.http?.admissionPolicy ?? "RequireAll");
  const [errors, setErrors] = useState<Record<string, string>>(() => Object.create(null) as Record<string, string>);
  const [editorFailed, setEditorFailed] = useState(false);
  const [editorConfiguration, setEditorConfiguration] = useState<Record<string, JsonValue>>(() => ({
    ...Object.fromEntries(descriptor.configurationFields.filter(field => field.defaultValue !== undefined).map(field => [field.key, field.defaultValue!])),
    ...trigger?.configuration, ...initialDraft?.configuration
  }));
  const matches = editors.filter(editor => editor.typeKey === descriptor.typeKey && editor.schemaVersion === descriptor.schemaVersion &&
    (!descriptor.configurationEditorId || editor.id === descriptor.configurationEditorId));
  const editor = !editorFailed && matches.length === 1 ? matches[0] : undefined;
  const Editor = editor?.component;
  const fallback = editorFailed ? "The optional configuration editor failed. Your values are preserved; use the standard fields below."
    : matches.length > 1 ? "Several optional configuration editors match this type. Use the standard fields below until the module conflict is resolved."
    : descriptor.configurationEditorId && !editor ? "The optional configuration editor is unavailable. Your saved values are preserved; use the standard fields below or install the matching editor module." : undefined;
  function changeEditor(value: Record<string, JsonValue>) {
    const next = { ...value };
    for (const field of descriptor.configurationFields.filter(field => field.readOnly || field.sensitive && trigger)) {
      if (Object.hasOwn(editorConfiguration, field.key)) next[field.key] = editorConfiguration[field.key]; else delete next[field.key];
    }
    setEditorConfiguration(next);
    setFields(Object.fromEntries(descriptor.configurationFields.map(field => [field.key, fieldText(next[field.key])])));
  }
  const isHttp = descriptor.family === "Http";
  function submit(event: FormEvent) {
    event.preventDefault();
    const nextErrors = Object.create(null) as Record<string, string>;
    const configuration = { ...trigger?.configuration, ...editorConfiguration };
    for (const field of descriptor.configurationFields) {
      if (field.readOnly || field.sensitive && trigger) continue;
      const raw = fields[field.key] ?? "";
      if (!raw && field.required) { nextErrors[field.key] = `${field.label} is required.`; continue; }
      if (raw === "" && field.type !== "string") { delete configuration[field.key]; continue; }
      try { configuration[field.key] = parseField(raw, field); }
      catch (error) { nextErrors[field.key] = error instanceof Error ? error.message : "Enter a valid value."; }
    }
    if (!name.trim()) nextErrors.name = "Enter a trigger name.";
    if (isHttp && !mode) nextErrors.mode = "Choose who responds to HTTP requests.";
    setErrors(nextErrors);
    if (!Object.keys(nextErrors).length) onSubmit({ name: name.trim(), description, configuration, ...(isHttp && mode ? { http: { mode, ...(mode === "AsyncFanOut" ? { admissionPolicy: policy } : {}) } } : {}) });
  }
  return <form className="wf-trigger-form" onSubmit={submit} noValidate>
    <StudioField label="Name"><input aria-label="Trigger name" value={name} maxLength={200} disabled={busy} aria-invalid={!!errors.name} onChange={event => setName(event.currentTarget.value)} />{errors.name ? <small role="alert">{errors.name}</small> : null}</StudioField>
    <StudioField label="Description"><textarea aria-label="Trigger description" value={description} maxLength={2000} disabled={busy} onChange={event => setDescription(event.currentTarget.value)} /></StudioField>
    <p>{descriptor.description}</p><Findings items={descriptor.findings} />
    {fallback ? <StudioAlert tone="warning">{fallback}</StudioAlert> : null}
    {Editor && editor ? <ConfigurationEditorBoundary key={editor.id} onFailure={() => setEditorFailed(true)}><Editor typeKey={descriptor.typeKey} schemaVersion={descriptor.schemaVersion}
      configuration={editorConfiguration} disabled={!!busy} findings={[...(descriptor.findings ?? []), ...Object.entries(errors).map(([field, message]) => ({ code: "LocalValidation", field, message }))]} onChange={changeEditor} /></ConfigurationEditorBoundary>
      : descriptor.configurationFields.map(field => <StudioField key={field.key} label={`${field.label}${field.required ? " *" : ""}`} description={field.description}>
      {field.readOnly || field.sensitive && trigger ? <span>{field.sensitive ? "Protected value · managed by its source" : fields[field.key] || "Not configured"}</span> : field.type === "boolean" ? <select aria-label={field.label} value={fields[field.key] ?? ""} disabled={busy} onChange={event => setFields(current => ({ ...current, [field.key]: event.target.value }))}><option value="">Not set</option><option value="true">Yes</option><option value="false">No</option></select>
        : field.choices ? <select aria-label={field.label} value={fields[field.key] ?? ""} disabled={busy} onChange={event => setFields(current => ({ ...current, [field.key]: event.target.value }))}><option value="">Choose…</option>{field.choices.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}</select>
          : field.type === "json" ? <textarea aria-label={field.label} value={fields[field.key] ?? ""} disabled={busy} aria-invalid={!!errors[field.key]} onChange={event => setFields(current => ({ ...current, [field.key]: event.target.value }))} />
            : <input aria-label={field.label} type={field.sensitive ? "password" : "text"} inputMode={field.type === "integer" ? "numeric" : field.type === "number" ? "decimal" : undefined} value={fields[field.key] ?? ""} disabled={busy} aria-invalid={!!errors[field.key]} onChange={event => setFields(current => ({ ...current, [field.key]: event.target.value }))} />}
      {errors[field.key] ? <small role="alert">{errors[field.key]}</small> : null}
    </StudioField>)}
    {isHttp ? <>
      <StudioField label="HTTP response" description="Choose how the original request receives its response."><select aria-label="HTTP response mode" value={mode} disabled={busy} aria-invalid={!!errors.mode} onChange={event => setMode(event.currentTarget.value as HttpMode)}><option value="">Choose response mode…</option><option value="WorkflowResponse">One workflow owns the response</option><option value="AsyncFanOut">Acknowledge and start subscribers</option></select>{errors.mode ? <small role="alert">{errors.mode}</small> : null}</StudioField>
      {mode === "WorkflowResponse" ? <p>Exactly one compatible workflow owns the response. A second response owner prevents activation.</p> : mode === "AsyncFanOut" ? <StudioField label="Admission policy" description="This policy belongs to the shared trigger and applies to every request. It governs acceptance; later dispatch and recovery remain independent."><select aria-label="HTTP admission policy" value={policy} disabled={busy} onChange={event => setPolicy(event.currentTarget.value as AdmissionPolicy)}><option value="RequireAll">Require all subscribers</option><option value="AcceptAvailable">Accept available subscribers</option></select><small>{policy === "RequireAll" ? "Refuse the request if any enabled subscriber cannot be accepted." : "Explicitly report partial acceptance and each refused subscriber. Accepting no subscribers fails."}</small></StudioField> : null}
    </> : null}
    {!trigger ? <StudioAlert>The trigger is created paused. Review its subscriptions and enable it when ready. An HTTP route is reserved on its first successful enable.</StudioAlert> : null}
    <div className="studio-page-actions"><StudioButton type="button" disabled={busy} onClick={onCancel}>Cancel</StudioButton><StudioButton type="submit" variant="primary" disabled={busy || !descriptor.canCreate && !trigger}>{busy ? "Saving…" : trigger ? "Review changes" : "Create trigger"}</StudioButton></div>
  </form>;
}
class ConfigurationEditorBoundary extends Component<{ children: ReactNode; onFailure(): void }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch() { this.props.onFailure(); }
  render() { return this.state.failed ? null : this.props.children; }
}
function fieldText(value: JsonValue | undefined) { return value === undefined ? "" : typeof value === "string" ? value : serializeTriggerPayload(value); }
function parseField(raw: string, field: ConfigurationField): JsonValue {
  if (field.type === "json") return parseLiteralJson(raw);
  if (field.type === "boolean") { if (raw !== "true" && raw !== "false") throw new Error("Choose yes or no."); return raw === "true"; }
  if (field.type === "number" || field.type === "integer") { const value = Number(raw); if (!Number.isFinite(value) || field.type === "integer" && !Number.isSafeInteger(value)) throw new Error("Enter a supported number."); return value; }
  return raw;
}
