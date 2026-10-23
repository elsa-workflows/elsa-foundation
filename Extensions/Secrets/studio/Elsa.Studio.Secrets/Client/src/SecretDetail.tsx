import React, { useId, useLayoutEffect, useRef, useState } from "react";
import { Save, X } from "lucide-react";
import { useDialogFocus } from "@elsa-workflows/studio-sdk";
import { CopySecretNameButton } from "./CopySecretNameButton";
import { ScopeMatchingHint } from "./SecretScopePicker";
import type { SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";

export function SecretDetail({
  secret,
  descriptors,
  busy = false,
  onSave,
  onClose
}: {
  secret: SecretMetadata;
  descriptors: SecretDescriptorsResponse | null;
  busy?: boolean;
  onSave(name: string, request: { displayName: string; description?: string }): Promise<void>;
  onClose(): void;
}) {
  const dialogRef = useRef<HTMLElement>(null);
  const [displayName, setDisplayName] = useState(secret.displayName ?? "");
  const [description, setDescription] = useState(secret.description ?? "");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const titleId = useId();
  const descriptionInputId = useId();
  const scopeInputId = useId();
  const scopeHelpId = useId();
  const cannotClose = busy || saving;
  useDialogFocus(dialogRef, cannotClose ? null : onClose, false, () => {
    const fallback = Array.from(document.querySelectorAll<HTMLElement>("[data-secret-edit]"))
      .find(element => element.dataset.secretEdit === secret.name);
    fallback?.focus();
  });
  useLayoutEffect(() => {
    dialogRef.current?.querySelector<HTMLElement>("[data-dialog-initial-focus]")?.focus();
  }, [secret.name]);

  const store = descriptors?.stores.find(item => item.name === secret.storeName);
  const type = descriptors?.types.find(item => item.name === secret.typeName);
  const hasMetadataChanges = displayName !== (secret.displayName ?? "") || description !== (secret.description ?? "");
  async function submitMetadata(event: React.FormEvent) {
    event.preventDefault();
    if (!hasMetadataChanges || cannotClose) return;
    setSaving(true);
    setError(null);
    try {
      await onSave(secret.name, {
        displayName: displayName.trim(),
        description: description.trim()
      });
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secret metadata could not be updated.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div
      className="secrets-editor-backdrop"
      role="presentation"
      onClick={event => {
        if (event.target === event.currentTarget && !cannotClose) onClose();
      }}
    >
      <section
        ref={dialogRef}
        className="secrets-editor-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <header>
          <div>
            <span className="secrets-kicker">Secret editor</span>
            <h3 id={titleId}>Edit secret</h3>
            <p className="secrets-technical-name"><code>{secret.name}</code><CopySecretNameButton key={secret.name} name={secret.name} /></p>
          </div>
          <div className="secrets-editor-header-actions">
            <span className={`secrets-badge ${secret.status.toLowerCase()}`}>{secret.status}</span>
            <button type="button" className="secrets-editor-close" aria-label="Close editor" title="Close" onClick={onClose} disabled={cannotClose}><X size={16} aria-hidden /></button>
          </div>
        </header>

        <form className="secrets-edit-form" onSubmit={event => void submitMetadata(event)}>
          <label>
            <span>Display name</span>
            <input data-dialog-initial-focus value={displayName} readOnly={cannotClose} onChange={event => setDisplayName(event.target.value)} />
          </label>
          <div className="secrets-description-field">
            <label htmlFor={descriptionInputId}>Description</label>
            <textarea id={descriptionInputId} value={description} readOnly={cannotClose} onChange={event => setDescription(event.target.value)} rows={4} />
          </div>
          <div className="secrets-editor-scope">
            <label htmlFor={scopeInputId}>Scope</label>
            <input id={scopeInputId} value={secret.scope || "No scope"} readOnly aria-describedby={scopeHelpId} />
            <ScopeMatchingHint id={scopeHelpId} readOnly />
          </div>
          <dl className="secrets-metadata-grid secrets-editor-summary" aria-label="Secret summary">
            <div><dt>Type</dt><dd>{type?.displayName ?? secret.typeName}</dd></div>
            <div><dt>Store</dt><dd>{store?.displayName ?? secret.storeName}</dd></div>
            <div><dt>Current version</dt><dd>{secret.currentVersion ?? "none"}</dd></div>
            <div><dt>Expires</dt><dd>{secret.expiresAt ? formatDate(secret.expiresAt) : "never"}</dd></div>
          </dl>
          <details className="secrets-editor-details">
            <summary>Details</summary>
            <dl className="secrets-metadata-grid">
              <div><dt>Created</dt><dd>{formatDate(secret.createdAt)}</dd></div>
              <div><dt>Updated</dt><dd>{formatDate(secret.updatedAt)}</dd></div>
              <div><dt>Store capability</dt><dd>{store?.isReadOnly ? "Configuration-backed value" : "Managed value"}</dd></div>
            </dl>
          </details>
          {secret.tags.length > 0 ? (
            <div className="secrets-tags" aria-label="Tags">
              {secret.tags.map(tag => <span key={tag}>{tag}</span>)}
            </div>
          ) : null}
          {error ? <p className="secrets-field-error" role="alert">{error}</p> : null}
          <div className="secrets-dialog-actions">
            <button type="button" onClick={onClose} disabled={cannotClose}>Cancel</button>
            <button type="submit" disabled={!hasMetadataChanges || cannotClose}>
              <Save size={15} aria-hidden /> {saving ? "Saving…" : "Save changes"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleString() : "-";
}
