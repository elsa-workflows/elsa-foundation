import React, { useEffect, useId, useMemo, useRef, useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import { useDialogFocus } from "@elsa-workflows/studio-sdk";
import { AdvancedSection, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";
import { SecretFormField } from "./SecretFormField";
import { ScopeMatchingHint, SecretScopePicker } from "./SecretScopePicker";

const technicalNamePattern = /^[a-zA-Z0-9._:-]+$/;

export function CreateSecretDialog({
  descriptors,
  scopeCatalogLoading = false,
  scopeCatalogError = false,
  onRetryScopeCatalog,
  onCancel,
  onSave
}: {
  descriptors: SecretDescriptorsResponse;
  scopeCatalogLoading?: boolean;
  scopeCatalogError?: boolean;
  onRetryScopeCatalog?(): void;
  onCancel(): void;
  onSave(request: CreateSecretRequest): Promise<void>;
}) {
  const initialTypeName = descriptors.types[0]?.name ?? "text";
  const initialStoreName = defaultStoreNameForType(descriptors, initialTypeName);
  const [name, setName] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [typeName, setTypeName] = useState(initialTypeName);
  const [storeName, setStoreName] = useState(initialStoreName);
  const [scope, setScope] = useState("");
  const [tags, setTags] = useState("");
  const [value, setValue] = useState("");
  const [configurationKey, setConfigurationKey] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState("");
  const dialogRef = useRef<HTMLElement>(null);
  const errorRef = useRef<HTMLParagraphElement>(null);
  const descriptionInputId = useId();
  const scopeMatchingHintId = useId();
  const trimmedName = name.trim();
  const nameInvalid = trimmedName.length > 0 && !technicalNamePattern.test(trimmedName);
  const selectedType = descriptors.types.find(type => type.name === typeName);
  const supportedStores = useMemo(() => storesForType(descriptors, typeName), [descriptors, typeName]);
  const selectedStore = descriptors.stores.find(store => store.name === storeName);
  const defaultStoreName = defaultStoreNameForType(descriptors, typeName);
  const configuredOptionalCount = [
    displayName.trim().length > 0,
    description.trim().length > 0,
    parseTags(tags).length > 0,
    scope.length > 0,
    storeName.length > 0 && storeName !== defaultStoreName
  ].filter(Boolean).length;
  const usesConfigurationKey = storeName === "configuration";
  const canSubmit = trimmedName.length > 0 && !nameInvalid && typeName.length > 0 && storeName.length > 0 && (usesConfigurationKey ? configurationKey.trim().length > 0 : value.length > 0);
  useDialogFocus(dialogRef, submitting ? null : onCancel);

  useEffect(() => {
    if (error) errorRef.current?.focus();
  }, [error]);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (submitting || !canSubmit) return;
    setSubmitting(true);
    setError("");
    try {
      await onSave({
        name: trimmedName,
        displayName: displayName.trim() || trimmedName,
        description: description.trim() || undefined,
        typeName,
        storeName,
        scope: scope.length > 0 ? scope : undefined,
        tags: parseTags(tags),
        value: usesConfigurationKey ? undefined : value,
        configurationKey: usesConfigurationKey ? configurationKey.trim() : undefined,
        expiresAt: toIsoDate(expiresAt)
      });
      setValue("");
      setConfigurationKey("");
      onCancel();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret could not be created.");
    } finally {
      setSubmitting(false);
    }
  }

  function changeType(nextTypeName: string) {
    setTypeName(nextTypeName);
    const nextStoreName = storesForType(descriptors, nextTypeName)[0]?.name ?? "";
    setStoreName(nextStoreName);
  }

  return (
    <div className="secrets-modal secrets-create-backdrop">
      <section ref={dialogRef} className="secrets-create-dialog" role="dialog" aria-modal="true" aria-label="Create secret" tabIndex={-1}>
        <form className="secrets-create-form" aria-busy={submitting} onSubmit={event => void submit(event)}>
          <header className="secrets-create-header">
            <h3>Create secret</h3>
          </header>
          <div className="secrets-create-content">
            {error ? <p ref={errorRef} className="secrets-alert" role="alert" tabIndex={0}>{error}</p> : null}
            <fieldset className="secrets-create-fields" disabled={submitting}>
              <SecretFormField label="Technical name" disabled={submitting}
                hint="Casing is preserved; names are matched case-insensitively. Use letters, numbers, dot, dash, underscore, or colon."
                error={nameInvalid ? "Use letters, numbers, dot, dash, underscore, or colon." : undefined}>
                {attributes => <input {...attributes} value={name} autoComplete="off" onChange={event => setName(event.target.value)} onBlur={() => setName(trimmedName)} />}
              </SecretFormField>
              <SecretFormField label="Type" disabled={submitting}>
                {attributes => <select {...attributes} value={typeName} onChange={event => changeType(event.target.value)}>{descriptors.types.map(type => <option key={type.name} value={type.name}>{type.displayName || type.name}</option>)}</select>}
              </SecretFormField>
              {usesConfigurationKey ? (
                <CompactTextField label="Configuration key" value={configurationKey} onChange={setConfigurationKey} disabled={submitting} autoComplete="off" hint={selectedStore?.description ?? undefined} />
              ) : (
                <SecretValueField key={typeName} label="Value" value={value} typeName={typeName} editorHint={selectedType?.editorHint ?? null} onChange={setValue} compact disabled={submitting} hint={selectedType?.description ?? undefined} />
              )}
              <SecretFormField label="Expires" disabled={submitting}>
                {attributes => <input {...attributes} type="datetime-local" value={expiresAt} onChange={event => setExpiresAt(event.target.value)} />}
              </SecretFormField>
              <ScopeMatchingHint id={scopeMatchingHintId} />
              <AdvancedSection label="More options" setCount={configuredOptionalCount} allowCollapseWhenSet forceOpen={!storeName || scopeCatalogError}>
                <div className="secrets-create-options">
                  <CompactTextField label="Display name" value={displayName} onChange={setDisplayName} disabled={submitting} />
                  <SecretFormField label="Description" controlId={descriptionInputId} disabled={submitting}>
                    {attributes => <textarea {...attributes} value={description} onChange={event => setDescription(event.target.value)} rows={3} />}
                  </SecretFormField>
                  <CompactTextField label="Tags" value={tags} onChange={setTags} hint="Separate tags with commas." disabled={submitting} />
                  <SecretFormField label="Store" hint={selectedStore?.description ?? undefined} disabled={submitting}>
                    {attributes => <select {...attributes} value={storeName} onChange={event => setStoreName(event.target.value)}>{supportedStores.map(store => <option key={store.name} value={store.name}>{store.displayName || store.name}</option>)}</select>}
                  </SecretFormField>
                  <SecretScopePicker
                    value={scope}
                    options={descriptors.scopes}
                    loading={scopeCatalogLoading}
                    error={scopeCatalogError}
                    onChange={setScope}
                    onRetry={onRetryScopeCatalog}
                    externalMatchingHintId={scopeMatchingHintId}
                    compact
                    disabled={submitting}
                  />
                </div>
              </AdvancedSection>
            </fieldset>
          </div>
          <StudioDialogFooter
            className="secrets-dialog-actions secrets-create-actions"
            onCancel={onCancel}
            cancelDisabled={submitting}
            confirmLabel={submitting ? "Creating..." : "Create"}
            confirmDisabled={!canSubmit || submitting}
          />
        </form>
      </section>
    </div>
  );
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
  onChange
}: {
  label: string;
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
      <input type={type} value={value} placeholder={placeholder} autoComplete={autoComplete} onBlur={onBlur} onChange={event => onChange(event.target.value)} />
    </label>
  );
}

function CompactTextField({ label, value, onChange, hint, disabled, autoComplete }: {
  label: string; value: string; onChange(value: string): void; hint?: React.ReactNode; disabled?: boolean; autoComplete?: string;
}) {
  return <SecretFormField label={label} hint={hint} disabled={disabled}>
    {attributes => <input {...attributes} value={value} autoComplete={autoComplete} onChange={event => onChange(event.target.value)} />}
  </SecretFormField>;
}

function SecretValueField({ label, value, typeName, editorHint, onChange, compact = false, disabled, hint }: {
  label: string; value: string; typeName: string; editorHint?: string | null; onChange(value: string): void;
  compact?: boolean; disabled?: boolean; hint?: React.ReactNode;
}) {
  const inputId = useId();
  const [visible, setVisible] = useState(false);
  const multiline = editorHint === "multiline" || typeName === "rsa-key" || typeName === "x509-certificate";
  if (compact) {
    const toggle = multiline ? undefined : <button type="button" disabled={disabled} aria-label={visible ? "Hide value" : "Show value"} aria-pressed={visible} aria-controls={inputId} onClick={() => setVisible(current => !current)}>
      {visible ? <EyeOff size={16} aria-hidden="true" /> : <Eye size={16} aria-hidden="true" />}
    </button>;
    return <SecretFormField label={label} controlId={inputId} hint={hint} disabled={disabled} trailing={toggle}>
      {attributes => multiline
        ? <textarea {...attributes} value={value} rows={6} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />
        : <input {...attributes} type={visible ? "text" : "password"} value={value} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />}
    </SecretFormField>;
  }
  return (
    <div className="secrets-value-field">
      <label htmlFor={inputId}>{label}</label>
      {multiline ? (
        <textarea id={inputId} value={value} rows={6} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />
      ) : (
        <div className="secrets-value-input">
          <input id={inputId} type={visible ? "text" : "password"} value={value} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />
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
