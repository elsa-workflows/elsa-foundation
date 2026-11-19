import React, { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import { useDialogFocus } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";
import { SecretFormField } from "./SecretFormField";
import { ScopeMatchingHint, SecretScopePicker } from "./SecretScopePicker";

import { SecretEditorTabs, type SecretEditorSection, type SecretEditorState } from "./SecretEditorTabs";

const technicalNamePattern = /^[a-zA-Z0-9._:-]+$/;

interface CreateSecretFormProps {
  descriptors: SecretDescriptorsResponse;
  scopeCatalogLoading?: boolean;
  scopeCatalogError?: boolean;
  onRetryScopeCatalog?(): void;
  onCancel(): void;
  onSave(request: CreateSecretRequest): Promise<void>;
  onStateChange?(state: SecretEditorState): void;
}

/** Retained for contextual creation hosts; collection navigation uses the standalone form page. */
export function CreateSecretDialog(props: CreateSecretFormProps) {
  const dialogRef = useRef<HTMLElement>(null);
  const [busy, setBusy] = useState(false);
  useDialogFocus(dialogRef, busy ? null : props.onCancel);
  const { onStateChange } = props;
  const report = useCallback((state: SecretEditorState) => { setBusy(state.busy); onStateChange?.(state); }, [onStateChange]);
  return <div className="secrets-modal secrets-create-backdrop"><section ref={dialogRef} className="secrets-create-dialog" role="dialog" aria-modal="true" aria-label="Create secret" tabIndex={-1}>
    <header className="secrets-create-header"><h3>Create secret</h3></header>
    <CreateSecretForm {...props} onStateChange={report} />
  </section></div>;
}

export function CreateSecretForm({ descriptors, scopeCatalogLoading = false, scopeCatalogError = false, onRetryScopeCatalog, onCancel, onSave, onStateChange }: CreateSecretFormProps) {
  const initialTypeName = descriptors.types[0]?.name ?? "";
  const [name, setName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [typeName, setTypeName] = useState(initialTypeName);
  const [storeName, setStoreName] = useState(() => defaultStoreNameForType(descriptors, initialTypeName));
  const [scope, setScope] = useState("");
  const [tags, setTags] = useState("");
  const [value, setValue] = useState("");
  const [configurationKey, setConfigurationKey] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const submittingRef = useRef(false);
  const [error, setError] = useState("");
  const [active, setActive] = useState<SecretEditorSection>("secret");
  const [invalid, setInvalid] = useState("");
  const [invalidField, setInvalidField] = useState("");
  const [focusRequest, setFocusRequest] = useState<{ field: string }>();
  const formRef = useRef<HTMLFormElement>(null);
  const initiallyFocused = useRef(false);
  useLayoutEffect(() => { if (!initiallyFocused.current) { initiallyFocused.current = true; formRef.current?.querySelector<HTMLElement>('[data-secret-field="name"]')?.focus(); } }, []);
  const errorRef = useRef<HTMLParagraphElement>(null);
  const descriptionInputId = useId();
  const scopeMatchingHintId = useId();
  const trimmedName = name.trim();
  const nameInvalid = trimmedName.length > 0 && !technicalNamePattern.test(trimmedName);
  const selectedType = descriptors.types.find(type => type.name === typeName);
  const supportedStores = useMemo(() => storesForType(descriptors, typeName), [descriptors, typeName]);
  const compatible = supportedStores.some(store => store.name === storeName);
  const usesConfigurationKey = storeName === "configuration";
  const dirty = Boolean(name || displayName || description || scope || tags || value || configurationKey || expiresAt || typeName !== initialTypeName || storeName !== defaultStoreNameForType(descriptors, initialTypeName));
  useEffect(() => { onStateChange?.({ dirty, busy: submitting }); }, [dirty, submitting, onStateChange]);
  useLayoutEffect(() => { if (focusRequest) formRef.current?.querySelector<HTMLElement>(`[data-secret-field="${focusRequest.field}"]`)?.focus(); }, [focusRequest]);
  useEffect(() => { if (error) errorRef.current?.focus(); }, [error]);
  function fail(section: SecretEditorSection, field: string, message: string) { setActive(section); setInvalid(message); setInvalidField(field); setFocusRequest({ field }); }
  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (submittingRef.current) return;
    if (!trimmedName || !technicalNamePattern.test(trimmedName)) { fail("secret", "name", trimmedName ? "Use letters, numbers, dot, dash, underscore or colon." : "Enter a technical name."); return; }
    if (!selectedType) { fail("secret", "type", "No secret type is available. Refresh secret options."); return; }
    if (!compatible) { fail("storage", "store", "No compatible secret store is available. Refresh secret options or choose another type."); return; }
    if (usesConfigurationKey ? !configurationKey.trim() : !value) { fail("secret", usesConfigurationKey ? "configuration" : "value", usesConfigurationKey ? "Enter a configuration key." : "Enter a secret value."); return; }
    if (expiresAt && Number.isNaN(new Date(expiresAt).getTime())) { fail("storage", "expires", "Enter a valid expiry date."); return; }
    submittingRef.current = true; setSubmitting(true); setError(""); setInvalid(""); setInvalidField("");
    try {
      await onSave({ name: trimmedName, displayName: displayName.trim() || trimmedName, description: description.trim() || undefined, typeName, storeName,
        scope: scope.length > 0 ? scope : undefined, tags: parseTags(tags), value: usesConfigurationKey ? undefined : value,
        configurationKey: usesConfigurationKey ? configurationKey.trim() : undefined, expiresAt: toIsoDate(expiresAt) });
      setValue(""); setConfigurationKey(""); onCancel();
    } catch (failure) { setError(failure instanceof Error ? failure.message : "Secret could not be created."); }
    finally { submittingRef.current = false; setSubmitting(false); }
  }
  const fieldChanged = () => { setInvalid(""); setInvalidField(""); };
  const fieldError = (field: string) => invalidField === field ? invalid : undefined;
  return <form ref={formRef} className="secrets-create-form secrets-tabbed-form" aria-busy={submitting} noValidate onSubmit={event => void submit(event)} onChange={fieldChanged}>
    {error ? <p ref={errorRef} className="secrets-field-error" role="alert" tabIndex={-1}>{error}</p> : null}
    {invalid ? <p className="secrets-field-error" role="alert">{invalid}</p> : null}
    <fieldset className="secrets-editor-fields" disabled={submitting}>
    <SecretEditorTabs active={active} onSelect={setActive}
      detailsCount={[displayName.trim(), description.trim(), parseTags(tags).length].filter(Boolean).length}
      storageCount={[scope, expiresAt, storeName !== defaultStoreNameForType(descriptors, typeName)].filter(Boolean).length}
      storageNeedsAttention={scopeCatalogError || !compatible || invalidField === "store" || invalidField === "expires"}>
      {{ secret: <>
        <div className="secrets-editor-grid"><SecretFormField label="Technical name" disabled={submitting}
          hint="Casing is preserved; names are matched case-insensitively. Use letters, numbers, dot, dash, underscore, or colon."
          error={nameInvalid ? "Use letters, numbers, dot, dash, underscore, or colon." : fieldError("name")}>
          {attributes => <input {...attributes} data-secret-field="name" data-dialog-initial-focus value={name} autoComplete="off" onChange={event => setName(event.target.value)} onBlur={() => setName(trimmedName)} />}
        </SecretFormField>
          <SecretFormField label="Type" disabled={submitting} error={fieldError("type")}>
          {attributes => <select {...attributes} data-secret-field="type" value={typeName} onChange={event => { const next = event.target.value; setTypeName(next); setStoreName(defaultStoreNameForType(descriptors, next)); }}>
            {descriptors.types.map(type => <option key={type.name} value={type.name}>{type.displayName || type.name}</option>)}
          </select>}
          </SecretFormField></div>
        {usesConfigurationKey ? <CompactTextField label="Configuration key" value={configurationKey} onChange={setConfigurationKey} autoComplete="off" field="configuration" disabled={submitting} error={fieldError("configuration")} hint={typeName === "text" ? undefined : selectedType?.description ?? undefined} />
          : <SecretValueField key={typeName} label="Value" value={value} typeName={typeName} editorHint={selectedType?.editorHint ?? null} onChange={setValue} compact disabled={submitting} error={fieldError("value")} hint={typeName === "text" ? undefined : selectedType?.description ?? undefined} />}
      </>, details: <>
        <div className="secrets-editor-grid"><CompactTextField label="Display name" value={displayName} onChange={setDisplayName} disabled={submitting} /><CompactTextField label="Tags" value={tags} onChange={setTags} hint="Separate tags with commas." disabled={submitting} /></div>
        <SecretFormField label="Description" controlId={descriptionInputId} disabled={submitting}>
          {attributes => <textarea {...attributes} value={description} onChange={event => setDescription(event.target.value)} rows={3} />}
        </SecretFormField>
      </>, storage: <>
        {supportedStores.length > 0 && (supportedStores.length > 1 || !compatible) ? <SecretFormField label="Store" disabled={submitting} error={fieldError("store")}>
          {attributes => <select {...attributes} data-secret-field="store" value={storeName} onChange={event => setStoreName(event.target.value)}>
          {!compatible ? <option value={storeName} disabled>{storeName || "Selected store"} (unavailable)</option> : null}
          {supportedStores.map(store => <option key={store.name} value={store.name}>{store.displayName || store.name}</option>)}
        </select>}
        </SecretFormField> : null}
        {!compatible ? <p data-secret-field="store" tabIndex={-1} className="secrets-field-error" role="alert">No compatible secret store is available for this type.</p> : null}
        {scopeCatalogError ? <p className="secrets-field-error" role="alert">Secret options could not be refreshed. Previously loaded options remain available.</p> : null}
        {(scopeCatalogError || !compatible) && onRetryScopeCatalog ? <StudioButton data-secret-field={!compatible ? "store" : undefined} onClick={onRetryScopeCatalog} disabled={scopeCatalogLoading}>Retry secret options</StudioButton> : null}
        <SecretScopePicker value={scope} options={descriptors.scopes} loading={scopeCatalogLoading} error={scopeCatalogError} onChange={setScope} onRetry={onRetryScopeCatalog} externalMatchingHintId={scopeMatchingHintId} compact disabled={submitting} />
        <ScopeMatchingHint id={scopeMatchingHintId} concise />
        <SecretFormField label="Expires" disabled={submitting} error={fieldError("expires")}>
          {attributes => <input {...attributes} data-secret-field="expires" type="datetime-local" value={expiresAt} onChange={event => setExpiresAt(event.target.value)} />}
        </SecretFormField>
      </> }}
    </SecretEditorTabs>
    </fieldset>
    <StudioDialogFooter className="secrets-dialog-actions secrets-create-actions" onCancel={onCancel} cancelDisabled={submitting} confirmLabel={submitting ? "Creating..." : "Create"} confirmDisabled={submitting} />
  </form>;
}

export function RotateSecretDialog({
  secret,
  descriptors,
  onCancel,
  onSave
}: {
  secret: SecretMetadata;
  descriptors: SecretDescriptorsResponse | null;
  onCancel(): void;
  onSave(request: RotateSecretRequest): Promise<void>;
}) {
  const [value, setValue] = useState("");
  const [configurationKey, setConfigurationKey] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  const dialogRef = useRef<HTMLDivElement>(null);
  const isConfiguration = secret.storeName === "configuration";
  const type = descriptors?.types.find(item => item.name === secret.typeName);
  const canSubmit = isConfiguration ? configurationKey.trim().length > 0 : value.length > 0;
  useDialogFocus(dialogRef, submitting ? null : onCancel);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (submitting || !canSubmit) return;
    setSubmitting(true);
    setError("");
    try {
      await onSave({
        value: isConfiguration ? undefined : value,
        configurationKey: isConfiguration ? configurationKey.trim() : undefined,
        expiresAt: toIsoDate(expiresAt)
      });
      setValue("");
      setConfigurationKey("");
      onCancel();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret could not be rotated.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div ref={dialogRef} className="secrets-modal" role="dialog" aria-modal="true" aria-label="Rotate secret" tabIndex={-1}>
      <form onSubmit={event => void submit(event)}>
        <h3>Rotate {secret.name}</h3>
        {error ? <p className="secrets-alert">{error}</p> : null}
        <p className="secrets-field-hint">Rotation replaces the backing material or configuration lookup. The current value is never shown.</p>
        {isConfiguration ? (
          <Field label="Configuration key" value={configurationKey} onChange={setConfigurationKey} autoComplete="off" />
        ) : (
          <SecretValueField label="New value" value={value} typeName={secret.typeName} editorHint={type?.editorHint ?? null} onChange={setValue} />
        )}
        <label>Expires<input type="datetime-local" value={expiresAt} onChange={event => setExpiresAt(event.target.value)} /></label>
        <StudioDialogFooter
          className="secrets-dialog-actions"
          onCancel={onCancel}
          cancelDisabled={submitting}
          confirmLabel={submitting ? "Rotating..." : "Rotate"}
          confirmDisabled={!canSubmit || submitting}
        />
      </form>
    </div>
  );
}

function Field({
  label,
  value,
  type = "text",
  placeholder,
  autoComplete,
  onBlur,
  onChange,
  field
}: {
  label: string;
  field?: string;
  value: string;
  type?: string;
  placeholder?: string;
  autoComplete?: string;
  onBlur?(): void;
  onChange(value: string): void;
}) {
  return (
    <label>
      {label}
      <input data-secret-field={field} data-dialog-initial-focus={field === "name" || undefined} type={type} value={value} placeholder={placeholder} autoComplete={autoComplete} onBlur={onBlur} onChange={event => onChange(event.target.value)} />
    </label>
  );
}

function CompactTextField({ label, value, onChange, hint, disabled, autoComplete, field, error }: {
  label: string; value: string; onChange(value: string): void; hint?: React.ReactNode; disabled?: boolean; autoComplete?: string; field?: string; error?: React.ReactNode;
}) {
  return <SecretFormField label={label} hint={hint} error={error} disabled={disabled}>
    {attributes => <input {...attributes} data-secret-field={field} value={value} autoComplete={autoComplete} onChange={event => onChange(event.target.value)} />}
  </SecretFormField>;
}

function SecretValueField({ label, value, typeName, editorHint, onChange, compact = false, disabled, hint, error }: {
  label: string; value: string; typeName: string; editorHint?: string | null; onChange(value: string): void;
  compact?: boolean; disabled?: boolean; hint?: React.ReactNode; error?: React.ReactNode;
}) {
  const inputId = useId();
  const [visible, setVisible] = useState(false);
  const multiline = editorHint === "multiline" || typeName === "rsa-key" || typeName === "x509-certificate";
  if (compact) {
    const toggle = multiline ? undefined : <button type="button" disabled={disabled} aria-label={visible ? "Hide value" : "Show value"} aria-pressed={visible} aria-controls={inputId} title={visible ? "Hide value" : "Show value"} onClick={() => setVisible(current => !current)}>
      {visible ? <EyeOff size={16} aria-hidden="true" /> : <Eye size={16} aria-hidden="true" />}
    </button>;
    return <SecretFormField label={label} controlId={inputId} hint={hint} error={error} disabled={disabled} trailing={toggle}>
      {attributes => multiline
        ? <textarea {...attributes} data-secret-field="value" value={value} rows={6} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />
        : <input {...attributes} data-secret-field="value" type={visible ? "text" : "password"} value={value} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />}
    </SecretFormField>;
  }
  return (
    <div className="secrets-value-field">
      <label htmlFor={inputId}>{label}</label>
      {multiline ? (
        <textarea data-secret-field="value" id={inputId} value={value} rows={6} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />
      ) : (
        <div className="secrets-value-input">
          <input data-secret-field="value" id={inputId} type={visible ? "text" : "password"} value={value} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />
          <button type="button" className="secrets-value-toggle" aria-label={visible ? "Hide value" : "Show value"} aria-pressed={visible} aria-controls={inputId} title={visible ? "Hide value" : "Show value"} onClick={() => setVisible(current => !current)}>
            {visible ? <EyeOff size={16} aria-hidden="true" /> : <Eye size={16} aria-hidden="true" />}
          </button>
        </div>
      )}
    </div>
  );
}

function storesForType(descriptors: SecretDescriptorsResponse, typeName: string) {
  const supportedNames = descriptors.types.find(type => type.name === typeName)?.supportedStoreNames ?? [];
  if (supportedNames.length === 0) return descriptors.stores;
  return descriptors.stores.filter(store => supportedNames.includes(store.name));
}

function defaultStoreNameForType(descriptors: SecretDescriptorsResponse, typeName: string) {
  return storesForType(descriptors, typeName)[0]?.name ?? descriptors.stores[0]?.name ?? "encrypted";
}

function parseTags(value: string) {
  return value.split(",").map(tag => tag.trim()).filter(Boolean);
}

function toIsoDate(value: string) {
  return value ? new Date(value).toISOString() : undefined;
}
