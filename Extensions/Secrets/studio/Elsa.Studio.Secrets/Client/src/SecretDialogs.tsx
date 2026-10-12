import React, { useMemo, useState } from "react";
import type { CreateSecretRequest, RotateSecretRequest, SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";

const technicalNamePattern = /^[a-z0-9._:-]+$/;

export function CreateSecretDialog({
  descriptors,
  onCancel,
  onSave
}: {
  descriptors: SecretDescriptorsResponse;
  onCancel(): void;
  onSave(request: CreateSecretRequest): Promise<void>;
}) {
  const initialTypeName = descriptors.types[0]?.name ?? "text";
  const initialStoreName = firstSupportedStoreName(descriptors, initialTypeName) ?? descriptors.stores[0]?.name ?? "encrypted";
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
  const normalizedName = normalizeTechnicalName(name);
  const nameInvalid = normalizedName.length > 0 && !technicalNamePattern.test(normalizedName);
  const selectedType = descriptors.types.find(type => type.name === typeName);
  const supportedStores = useMemo(() => storesForType(descriptors, typeName), [descriptors, typeName]);
  const selectedStore = descriptors.stores.find(store => store.name === storeName);
  const usesConfigurationKey = storeName === "configuration";
  const canSubmit = normalizedName.length > 0 && !nameInvalid && typeName.length > 0 && storeName.length > 0 && (usesConfigurationKey ? configurationKey.trim().length > 0 : value.length > 0);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!canSubmit) return;
    setSubmitting(true);
    setError("");
    try {
      await onSave({
        name: normalizedName,
        displayName: displayName.trim() || normalizedName,
        description: description.trim() || undefined,
        typeName,
        storeName,
        scope: scope.trim() || undefined,
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
    <div className="secrets-modal" role="dialog" aria-modal="true" aria-label="Create secret">
      <form onSubmit={event => void submit(event)}>
        <h3>Create secret</h3>
        {error ? <p className="secrets-alert">{error}</p> : null}
        <Field label="Technical name" value={name} onChange={setName} onBlur={() => setName(normalizedName)} autoComplete="off" />
        <p className={nameInvalid ? "secrets-field-error" : "secrets-field-hint"}>Trimmed and lowercased. Use letters, numbers, dot, dash, underscore, or colon.</p>
        <Field label="Display name" value={displayName} onChange={setDisplayName} />
        <label>Description<textarea value={description} onChange={event => setDescription(event.target.value)} rows={3} /></label>
        <label>Type<select value={typeName} onChange={event => changeType(event.target.value)}>{descriptors.types.map(type => <option key={type.name} value={type.name}>{type.displayName || type.name}</option>)}</select></label>
        {selectedType?.description ? <p className="secrets-field-hint">{selectedType.description}</p> : null}
        <label>Store<select value={storeName} onChange={event => setStoreName(event.target.value)}>{supportedStores.map(store => <option key={store.name} value={store.name}>{store.displayName || store.name}</option>)}</select></label>
        {selectedStore?.description ? <p className="secrets-field-hint">{selectedStore.description}</p> : null}
        <Field label="Scope" value={scope} onChange={setScope} />
        <Field label="Tags" value={tags} onChange={setTags} placeholder="comma-separated" />
        {usesConfigurationKey ? (
          <Field label="Configuration key" value={configurationKey} onChange={setConfigurationKey} autoComplete="off" />
        ) : (
          <SecretValueField label="Value" value={value} typeName={typeName} editorHint={selectedType?.editorHint ?? null} onChange={setValue} />
        )}
        <label>Expires<input type="datetime-local" value={expiresAt} onChange={event => setExpiresAt(event.target.value)} /></label>
        <div className="secrets-dialog-actions">
          <button type="button" onClick={onCancel}>Cancel</button>
          <button type="submit" disabled={!canSubmit || submitting}>{submitting ? "Creating..." : "Create"}</button>
        </div>
      </form>
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
  const isConfiguration = secret.storeName === "configuration";
  const type = descriptors?.types.find(item => item.name === secret.typeName);
  const canSubmit = isConfiguration ? configurationKey.trim().length > 0 : value.length > 0;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!canSubmit) return;
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
    <div className="secrets-modal" role="dialog" aria-modal="true" aria-label="Rotate secret">
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
        <div className="secrets-dialog-actions">
          <button type="button" onClick={onCancel}>Cancel</button>
          <button type="submit" disabled={!canSubmit || submitting}>{submitting ? "Rotating..." : "Rotate"}</button>
        </div>
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

function SecretValueField({ label, value, typeName, editorHint, onChange }: { label: string; value: string; typeName: string; editorHint?: string | null; onChange(value: string): void }) {
  const multiline = editorHint === "multiline" || typeName === "rsa-key" || typeName === "x509-certificate";
  return (
    <label>
      {label}
      {multiline ? (
        <textarea value={value} rows={6} autoComplete="new-password" spellCheck={false} onChange={event => onChange(event.target.value)} />
      ) : (
        <input type="password" value={value} autoComplete="new-password" onChange={event => onChange(event.target.value)} />
      )}
    </label>
  );
}

function storesForType(descriptors: SecretDescriptorsResponse, typeName: string) {
  const supportedNames = descriptors.types.find(type => type.name === typeName)?.supportedStoreNames ?? [];
  if (supportedNames.length === 0) return descriptors.stores;
  return descriptors.stores.filter(store => supportedNames.includes(store.name));
}

function firstSupportedStoreName(descriptors: SecretDescriptorsResponse, typeName: string) {
  return storesForType(descriptors, typeName)[0]?.name;
}

function normalizeTechnicalName(value: string) {
  return value.trim().toLowerCase();
}

function parseTags(value: string) {
  return value.split(",").map(tag => tag.trim()).filter(Boolean);
}

function toIsoDate(value: string) {
  return value ? new Date(value).toISOString() : undefined;
}
