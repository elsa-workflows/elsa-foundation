import React, { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { useDialogFocus } from "@elsa-workflows/studio-sdk";
import { StudioButton, StudioDialogFooter } from "@elsa-workflows/studio-ui";
import { CopySecretNameButton } from "./CopySecretNameButton";
import { ScopeMatchingHint } from "./SecretScopePicker";
import { SecretFormField } from "./SecretFormField";
import { SecretEditorTabs, type SecretEditorSection, type SecretEditorState } from "./SecretEditorTabs";
import type { SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";

interface SecretDetailProps {
  secret: SecretMetadata;
  descriptors: SecretDescriptorsResponse | null;
  busy?: boolean;
  presentation?: "dialog" | "page";
  onStateChange?(state: SecretEditorState): void;
  onSave(name: string, request: { displayName: string; description?: string }): Promise<void>;
  onClose(): void;
}
export function SecretDetail(props: SecretDetailProps) {
  return props.presentation === "page" ? <SecretMetadataForm {...props} /> : <SecretDetailDialog {...props} />;
}
function SecretDetailDialog(props: SecretDetailProps) {
  const ref = useRef<HTMLElement>(null);
  const [busy, setBusy] = useState(false);
  useDialogFocus(ref, busy ? null : props.onClose);
  const { onStateChange } = props;
  const report = useCallback((state: SecretEditorState) => { setBusy(state.busy); onStateChange?.(state); }, [onStateChange]);
  return <div className="secrets-editor-backdrop" role="presentation"><section ref={ref} className="secrets-editor-dialog" role="dialog" aria-modal="true" aria-label="Edit secret" tabIndex={-1}>
    <header><h3>Edit secret</h3><StudioButton aria-label="Close editor" onClick={props.onClose} disabled={busy}>Close</StudioButton></header>
    <SecretMetadataForm {...props} onStateChange={report} />
  </section></div>;
}
function SecretMetadataForm({ secret, descriptors, busy = false, onSave, onClose, onStateChange }: SecretDetailProps) {
  const formRef = useRef<HTMLFormElement>(null);
  const initiallyFocused = useRef(false);
  useLayoutEffect(() => { if (!initiallyFocused.current) { initiallyFocused.current = true; formRef.current?.querySelector<HTMLElement>("[data-dialog-initial-focus]")?.focus(); } }, []);
  const [displayName, setDisplayName] = useState(secret.displayName ?? "");
  const [description, setDescription] = useState(secret.description ?? "");
  const [saving, setSaving] = useState(false);
  const savingRef = useRef(false);
  const [error, setError] = useState("");
  const errorRef = useRef<HTMLParagraphElement>(null);
  const [active, setActive] = useState<SecretEditorSection>("details");
  const descriptionInputId = useId();
  const scopeInputId = useId();
  const scopeHelpId = useId();
  const cannotClose = busy || saving;
  const changed = displayName !== (secret.displayName ?? "") || description !== (secret.description ?? "");
  useEffect(() => { onStateChange?.({ dirty: changed, busy: cannotClose }); }, [changed, cannotClose, onStateChange]);
  useEffect(() => { if (error) errorRef.current?.focus(); }, [error]);
  const store = descriptors?.stores.find(item => item.name === secret.storeName);
  const type = descriptors?.types.find(item => item.name === secret.typeName);
  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!changed || busy || savingRef.current) return;
    savingRef.current = true; setSaving(true); setError("");
    try { await onSave(secret.name, { displayName: displayName.trim(), description: description.trim() }); onClose(); }
    catch (failure) { setError(failure instanceof Error ? failure.message : "Secret metadata could not be updated."); }
    finally { savingRef.current = false; setSaving(false); }
  }
  return <form ref={formRef} className="secrets-edit-form secrets-tabbed-form" aria-busy={cannotClose} onSubmit={event => void submit(event)}>
    <p className="secrets-technical-name"><code>{secret.name}</code><CopySecretNameButton name={secret.name} /></p>
    {error ? <p ref={errorRef} className="secrets-field-error" role="alert" tabIndex={-1}>{error}</p> : null}
    <fieldset className="secrets-editor-fields" disabled={cannotClose}>
    <SecretEditorTabs active={active} onSelect={setActive}>
      {{ secret: <>
        <dl className="secrets-metadata-grid"><div><dt>Technical name</dt><dd>{secret.name}</dd></div><div><dt>Type</dt><dd>{type?.displayName ?? secret.typeName}</dd></div>
          <div><dt>Status</dt><dd>{secret.status}</dd></div><div><dt>Current version</dt><dd>{secret.currentVersion ?? "none"}</dd></div></dl>
        <p className="secrets-field-hint">The existing value stays hidden. Rotate the secret to replace it.</p>
      </>, details: <>
        <SecretFormField label="Display name" disabled={cannotClose}>
          {attributes => <input {...attributes} data-dialog-initial-focus value={displayName} onChange={event => setDisplayName(event.target.value)} />}
        </SecretFormField>
        <SecretFormField label="Description" controlId={descriptionInputId} disabled={cannotClose}>
          {attributes => <textarea {...attributes} value={description} onChange={event => setDescription(event.target.value)} rows={3} />}
        </SecretFormField>
        <dl className="secrets-metadata-grid"><div><dt>Tags</dt><dd>{secret.tags.join(", ") || "None"}</dd></div><div><dt>Created</dt><dd>{formatDate(secret.createdAt)}</dd></div><div><dt>Updated</dt><dd>{formatDate(secret.updatedAt)}</dd></div></dl>
      </>, storage: <>
        <dl className="secrets-metadata-grid"><div><dt>Store</dt><dd>{store?.displayName ?? secret.storeName}</dd></div><div><dt>Expires</dt><dd>{secret.expiresAt ? formatDate(secret.expiresAt) : "Never"}</dd></div></dl>
        <SecretFormField label="Scope" controlId={scopeInputId} disabled={cannotClose}>
          {attributes => <input {...attributes} value={secret.scope || "No scope"} readOnly aria-describedby={scopeHelpId} />}
        </SecretFormField>
        <ScopeMatchingHint id={scopeHelpId} readOnly concise />
      </> }}
    </SecretEditorTabs>
    </fieldset>
    <StudioDialogFooter className="secrets-dialog-actions" onCancel={onClose} cancelDisabled={cannotClose} confirmLabel={saving ? "Saving…" : "Save changes"} confirmDisabled={!changed || cannotClose} />
  </form>;
}
function formatDate(value?: string | null) { return value ? new Date(value).toLocaleString() : "-"; }
