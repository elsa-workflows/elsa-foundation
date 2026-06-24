import React, { useEffect, useMemo, useState } from "react";
import { KeyRound, Plus, RotateCcw, Search, ShieldCheck, Trash2 } from "lucide-react";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { createSecret, deleteSecret, getSecretDescriptors, listSecrets, revokeSecret, rotateSecret, testSecret } from "./secretsApi";
import type { SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";
import { SecretDetail } from "./SecretDetail";
import { CreateSecretDialog, RotateSecretDialog } from "./SecretDialogs";

export function SecretsPage({ context }: { context: StudioEndpointContext }) {
  const [search, setSearch] = useState("");
  const [secrets, setSecrets] = useState<SecretMetadata[]>([]);
  const [selectedName, setSelectedName] = useState<string | null>(null);
  const [descriptors, setDescriptors] = useState<SecretDescriptorsResponse | null>(null);
  const [dialog, setDialog] = useState<"create" | "rotate" | null>(null);
  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const selected = useMemo(() => secrets.find(secret => secret.name === selectedName) ?? secrets[0] ?? null, [secrets, selectedName]);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const [list, descriptorResponse] = await Promise.all([listSecrets(context, search), getSecretDescriptors(context)]);
      setSecrets(list.items);
      setDescriptors(descriptorResponse);
      setSelectedName(current => current && list.items.some(secret => secret.name === current) ? current : list.items[0]?.name ?? null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Secrets could not be loaded.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  async function refreshWithStatus(message: string) {
    await load();
    setStatus(message);
  }

  return (
    <main className="secrets-shell">
      <header className="secrets-header">
        <div>
          <h2><KeyRound size={18} /> Secrets</h2>
          <p>{secrets.length} configured</p>
        </div>
        <button type="button" className="secrets-primary" onClick={() => setDialog("create")}>
          <Plus size={16} /> New
        </button>
      </header>

      <section className="secrets-toolbar">
        <label>
          <Search size={16} />
          <input value={search} onChange={event => setSearch(event.target.value)} onKeyDown={event => event.key === "Enter" && void load()} placeholder="Search secrets" />
        </label>
        <button type="button" onClick={() => void load()}>Search</button>
      </section>

      {error && <p className="secrets-alert">{error}</p>}
      {status && <p className="secrets-status">{status}</p>}

      <section className="secrets-layout">
        <div className="secrets-list" aria-label="Secrets">
          {loading ? <div className="secrets-empty">Loading</div> : null}
          {!loading && secrets.length === 0 ? <div className="secrets-empty">No secrets found.</div> : null}
          {secrets.map(secret => (
            <button key={secret.name} type="button" className={secret.name === selected?.name ? "active" : ""} onClick={() => setSelectedName(secret.name)}>
              <span>{secret.displayName || secret.name}</span>
              <small>{secret.name} · {secret.typeName} · {secret.status}</small>
            </button>
          ))}
        </div>

        <SecretDetail secret={selected} descriptors={descriptors} />
      </section>

      {selected && (
        <footer className="secrets-actions">
          <button type="button" onClick={() => setDialog("rotate")}><RotateCcw size={16} /> Rotate</button>
          <button type="button" onClick={async () => {
            const result = await testSecret(context, selected.name);
            setStatus(result.message ?? (result.succeeded ? "Secret resolved successfully." : "Secret test failed."));
          }}><ShieldCheck size={16} /> Test</button>
          <button type="button" onClick={() => void revokeSecret(context, selected.name).then(() => refreshWithStatus("Secret revoked."))}>Revoke</button>
          <button type="button" className="danger" onClick={() => void deleteSecret(context, selected.name).then(() => refreshWithStatus("Secret deleted."))}><Trash2 size={16} /> Delete</button>
        </footer>
      )}

      {dialog === "create" && descriptors && (
        <CreateSecretDialog
          descriptors={descriptors}
          onCancel={() => setDialog(null)}
          onSave={request => createSecret(context, request).then(() => refreshWithStatus("Secret created.")).finally(() => setDialog(null))}
        />
      )}
      {dialog === "rotate" && selected && (
        <RotateSecretDialog
          secret={selected}
          onCancel={() => setDialog(null)}
          onSave={request => rotateSecret(context, selected.name, request).then(() => refreshWithStatus("Secret rotated.")).finally(() => setDialog(null))}
        />
      )}
    </main>
  );
}
