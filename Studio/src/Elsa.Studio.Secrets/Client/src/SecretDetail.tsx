import React, { useEffect, useState } from "react";
import { Ban, RotateCcw, Save, ShieldCheck, Trash2 } from "lucide-react";
import type { SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";

export function SecretDetail({
  secret,
  descriptors,
  busy = false,
  onSave,
  onRotate,
  onTest,
  onRevoke,
  onDelete
}: {
  secret: SecretMetadata | null;
  descriptors: SecretDescriptorsResponse | null;
  busy?: boolean;
  onSave(name: string, request: { displayName: string; description?: string }): Promise<void>;
  onRotate(secret: SecretMetadata): void;
  onTest(secret: SecretMetadata): void;
  onRevoke(secret: SecretMetadata): void;
  onDelete(secret: SecretMetadata): void;
}) {
  const [displayName, setDisplayName] = useState("");
  const [description, setDescription] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    setDisplayName(secret?.displayName ?? "");
    setDescription(secret?.description ?? "");
  }, [secret?.name]);

  if (!secret) {
    return <section className="secrets-detail secrets-empty">Select a secret.</section>;
  }

  const store = descriptors?.stores.find(x => x.name === secret.storeName);
  const type = descriptors?.types.find(x => x.name === secret.typeName);
  const canMutate = secret.status !== "Deleted";
  const hasMetadataChanges = displayName !== (secret.displayName ?? "") || description !== (secret.description ?? "");

  async function submitMetadata(event: React.FormEvent) {
    event.preventDefault();
    if (!hasMetadataChanges) return;
    setSaving(true);
    try {
      await onSave(secret!.name, {
        displayName: displayName.trim(),
        description: description.trim()
      });
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="secrets-detail">
      <header>
        <div>
          <h3>{secret.displayName || secret.name}</h3>
          <p>{secret.name}</p>
        </div>
        <span className={`secrets-badge ${secret.status.toLowerCase()}`}>{secret.status}</span>
      </header>

      <dl className="secrets-metadata-grid">
        <div><dt>Type</dt><dd>{type?.displayName ?? secret.typeName}</dd></div>
        <div><dt>Store</dt><dd>{store?.displayName ?? secret.storeName}</dd></div>
        <div><dt>Scope</dt><dd>{secret.scope || "-"}</dd></div>
        <div><dt>Current version</dt><dd>{secret.currentVersion ?? "none"}</dd></div>
        <div><dt>Created</dt><dd>{formatDate(secret.createdAt)}</dd></div>
        <div><dt>Updated</dt><dd>{formatDate(secret.updatedAt)}</dd></div>
        <div><dt>Expires</dt><dd>{secret.expiresAt ? formatDate(secret.expiresAt) : "never"}</dd></div>
        <div><dt>Store capability</dt><dd>{store?.isReadOnly ? "Configuration-backed value" : "Managed value"}</dd></div>
      </dl>

      {secret.tags.length > 0 ? (
        <div className="secrets-tags" aria-label="Tags">
          {secret.tags.map(tag => <span key={tag}>{tag}</span>)}
        </div>
      ) : null}

      <form className="secrets-edit-form" onSubmit={event => void submitMetadata(event)}>
        <label>
          <span>Display name</span>
          <input value={displayName} onChange={event => setDisplayName(event.target.value)} />
        </label>
        <label>
          <span>Description</span>
          <textarea value={description} onChange={event => setDescription(event.target.value)} rows={4} />
        </label>
        <button type="submit" disabled={!hasMetadataChanges || saving || busy}>
          <Save size={15} /> Save metadata
        </button>
      </form>

      <div className="secrets-redaction">Values are never displayed. Studio does not reveal, cache, or export current secret material.</div>

      <div className="secrets-detail-actions">
        <button type="button" disabled={!canMutate || busy} onClick={() => onRotate(secret)}><RotateCcw size={15} /> Rotate</button>
        <button type="button" disabled={!canMutate || busy} onClick={() => onTest(secret)}><ShieldCheck size={15} /> Test</button>
        <button type="button" disabled={!canMutate || secret.status === "Revoked" || busy} onClick={() => onRevoke(secret)}><Ban size={15} /> Revoke</button>
        <button type="button" className="danger" disabled={!canMutate || busy} onClick={() => onDelete(secret)}><Trash2 size={15} /> Delete</button>
      </div>
    </section>
  );
}

function formatDate(value?: string | null) {
  return value ? new Date(value).toLocaleString() : "-";
}
