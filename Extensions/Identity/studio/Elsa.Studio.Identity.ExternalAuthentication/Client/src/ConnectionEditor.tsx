import React, { useEffect, useId, useState } from "react";
import { CopyableIdentifier, StudioButton } from "@elsa-workflows/studio-ui";
import { type ConnectionCatalog, type ConnectionDocument, type ConnectionView, type ExternalAuthenticationClient, type RoleChoice, type SecretChoice } from "./sdk";

export function ConnectionEditor({ client, catalog, view, seed, busy, onSave, onDirty, onDiscard }: {
  client: ExternalAuthenticationClient; catalog: ConnectionCatalog; view: ConnectionView | null; seed: ConnectionDocument | null;
  busy: boolean; onSave(document: ConnectionDocument): Promise<void>; onDirty(dirty: boolean): void; onDiscard(): void;
}) {
  const [form, setForm] = useState(() => structuredClone(view?.document ?? seed ?? emptyDocument()));
  const [initial] = useState(() => JSON.stringify(form));
  const [roles, setRoles] = useState<RoleChoice[]>([]);
  const [roleSkip, setRoleSkip] = useState(0);
  const [roleTotal, setRoleTotal] = useState(0);
  const [roleError, setRoleError] = useState(false);
  const [rolesLoading, setRolesLoading] = useState(false);
  const [secrets, setSecrets] = useState<SecretChoice[]>([]);
  const [secretSearch, setSecretSearch] = useState("");
  const [secretNotice, setSecretNotice] = useState("");
  const [secretsLoading, setSecretsLoading] = useState(false);
  const [localError, setLocalError] = useState("");
  const id = useId();
  const dirty = initial !== JSON.stringify(form);
  const readOnly = !catalog.canManageConnections || !catalog.databaseManagement || Boolean(view && !view.configurationEditable);
  const authReadOnly = readOnly || busy || Boolean(view?.document.enabled);
  const credential = form.secrets.clientSecret;
  const authentication = form.settings.clientAuthentication ?? (credential ? "client_secret_basic" : "none");
  useEffect(() => { onDirty(dirty); }, [dirty, onDirty]);
  useEffect(() => {
    if (readOnly || !catalog.capabilities.canManageUsers) return;
    const abort = new AbortController();
    setRolesLoading(true);
    void client.roles(roleSkip, abort.signal).then(result => {
      if (!abort.signal.aborted) { setRoles(result.items); setRoleTotal(result.totalCount); setRoleError(false); }
    }).catch(() => { if (!abort.signal.aborted) setRoleError(true); })
      .finally(() => { if (!abort.signal.aborted) setRolesLoading(false); });
    return () => abort.abort();
  }, [client, readOnly, catalog.capabilities.canManageUsers, roleSkip]);

  function update(changes: Partial<ConnectionDocument>) { const next = { ...form, ...changes }; setForm(next); onDirty(initial !== JSON.stringify(next)); setLocalError(""); }
  function setting(key: string, value: string) { update({ settings: { ...form.settings, [key]: value } }); }
  function secret(name: string, scope: string | null = credential?.scope ?? null) {
    update({ secrets: name ? { clientSecret: { name, typeName: "text", scope } } : {} });
  }
  async function findSecrets() {
    setSecretsLoading(true); setSecretNotice("");
    try {
      const result = await client.secrets(secretSearch);
      setSecrets(result.items);
      setSecretNotice(result.capped ? "First 50 matches shown. Refine the search to find another secret." : result.items.length ? "Choose a text secret. Only its reference is saved." : "No active text secrets matched. Try another search.");
    } catch { setSecrets([]); setSecretNotice("Secret lookup is unavailable. You can enter an existing text secret reference by name and scope."); }
    finally { setSecretsLoading(false); }
  }
  async function save(event: React.FormEvent) {
    event.preventDefault();
    if (readOnly || busy) return;
    if (!/^[a-z0-9][a-z0-9._-]{0,79}$/.test(form.key)) { setLocalError("Use a key of up to 80 lowercase letters, digits, dots, underscores or hyphens, starting with a letter or digit."); return; }
    if (!form.settings.authority?.trim() || !form.settings.clientId?.trim()) { setLocalError("Enter the provider's issuer URL and registered client ID."); return; }
    try { const issuer = new URL(form.settings.authority.trim()); if (issuer.username || issuer.password) { setLocalError("The issuer URL must not contain credentials. Choose a secret reference for client authentication."); return; } }
    catch { setLocalError("Enter a valid issuer URL."); return; }
    if (authentication !== "none" && !credential?.name.trim()) { setLocalError("Choose an existing text secret reference for this client."); return; }
    const document = structuredClone(form);
    document.settings.authority = document.settings.authority.trim(); document.settings.clientId = document.settings.clientId.trim();
    if (authentication === "none") document.secrets = {};
    await onSave(document);
  }

  if (view && !view.configurationSupported) return <section className="external-auth-editor"><h3>{view.document.displayName || view.document.key}</h3>
    <p className="external-auth-callout">This provider uses a protocol or settings that this editor does not support. Manage its configuration through the deployment or integrator.</p>
    <CopyableIdentifier label="Connection key" value={view.document.key} />
    {view.signInCallback ? <CopyableIdentifier label="Sign-in callback URL" value={view.signInCallback} /> : null}
    {view.previewCallback ? <CopyableIdentifier label="Preview callback URL" value={view.previewCallback} /> : null}
    <StudioButton onClick={onDiscard} disabled={busy}>Close details</StudioButton></section>;

  return <form className="external-auth-editor" onSubmit={event => { void save(event); }}>
    <div className="external-auth-section-heading"><h3>{view ? view.document.displayName || view.document.key : seed?.key ? "Prepare full override" : "New sign-in connection"}</h3>
      {dirty ? <span className="external-auth-muted">Unsaved changes</span> : null}</div>
    {readOnly ? <p className="external-auth-callout">{view?.source === 0 ? "This connection is managed by the deployment. Its settings are read-only here." : view?.document.archivedAt ? "Restore this archived connection before editing its settings." : "This connection is read-only for your account or deployment."}</p> : null}
    {view?.document.enabled && !readOnly ? <p className="external-auth-callout">Disable this connection before changing sign-in or provisioning settings. Display and ordering can be edited while it is enabled.</p> : null}
    {seed?.key && !view ? <p className="external-auth-callout">Save a complete Studio draft, then test and preview it. The deployment connection continues to apply until you explicitly choose “Use as override”.</p> : null}
    <fieldset disabled={readOnly || busy}>
      <legend>Connection</legend>
      <label htmlFor={`${id}-key`}>Connection key</label><input id={`${id}-key`} value={form.key} onChange={event => update({ key: event.target.value })} readOnly={Boolean(view || seed?.key)} required maxLength={80} autoComplete="off" />
      <small>Stable identifier for this provider's Elsa identity links. It cannot be changed after saving.</small>
      <label htmlFor={`${id}-name`}>Display name</label><input id={`${id}-name`} value={form.displayName} onChange={event => update({ displayName: event.target.value })} maxLength={256} required />
      <div className="external-auth-inline-fields"><label>Display order<input type="number" value={form.displayOrder} onChange={event => update({ displayOrder: Number(event.target.value) })} min={-2147483648} max={2147483647} step={1} /></label>
        <label className="external-auth-check"><input type="checkbox" checked={form.preferred} onChange={event => update({ preferred: event.target.checked })} />Offer as a preferred choice</label></div>
    </fieldset>
    <fieldset disabled={authReadOnly}>
      <legend>OpenID Connect</legend>
      <div className="external-auth-field-grid"><div><label htmlFor={`${id}-authority`}>Issuer URL</label><input id={`${id}-authority`} type="url" value={form.settings.authority ?? ""} onChange={event => setting("authority", event.target.value)} placeholder="https://identity.example.com" required maxLength={400} autoComplete="off" /></div>
      <div><label htmlFor={`${id}-client`}>Client ID</label><input id={`${id}-client`} value={form.settings.clientId ?? ""} onChange={event => setting("clientId", event.target.value)} required maxLength={400} autoComplete="off" /></div>
      <div><label htmlFor={`${id}-scopes`}>Requested scopes</label><input id={`${id}-scopes`} value={form.settings.scopes ?? "openid profile email"} onChange={event => setting("scopes", event.target.value)} maxLength={2048} /></div>
      <div><label htmlFor={`${id}-authentication`}>Client authentication</label><select id={`${id}-authentication`} value={authentication} onChange={event => setting("clientAuthentication", event.target.value)}>
        <option value="client_secret_basic">Client secret — HTTP Basic</option><option value="client_secret_post">Client secret — form body</option><option value="none">Public client — no secret</option>
      </select></div></div>
      {authentication !== "none" ? <div className="external-auth-secret">
        <label htmlFor={`${id}-secret`}>Client secret reference</label><input id={`${id}-secret`} value={credential?.name ?? ""} onChange={event => secret(event.target.value)} maxLength={256} autoComplete="off" />
        <small>Enter an existing text secret's name, never the secret value.</small>
        <label htmlFor={`${id}-scope`}>Secret scope (optional)</label><input id={`${id}-scope`} value={credential?.scope ?? ""} onChange={event => secret(credential?.name ?? "", event.target.value || null)} maxLength={256} autoComplete="off" />
        <div className="external-auth-inline-fields"><label htmlFor={`${id}-secret-search`}>Find an existing secret<input id={`${id}-secret-search`} type="search" value={secretSearch} onChange={event => setSecretSearch(event.target.value)} /></label>
          <StudioButton onClick={() => { void findSecrets(); }} disabled={secretsLoading}>Find secrets</StudioButton></div>
        {secretsLoading || secretNotice ? <p role="status" className="external-auth-muted">{secretsLoading ? "Finding text secret references…" : secretNotice}</p> : null}
        {secrets.length ? <ul className="external-auth-picker">{secrets.map(item => <li key={`${item.name}:${item.scope ?? ""}`}><StudioButton onClick={() => { secret(item.name, item.scope); setSecrets([]); }}>
          {item.displayName || item.name}{item.scope ? ` · ${item.scope}` : ""}</StudioButton></li>)}</ul> : null}
      </div> : null}
    </fieldset>
    <fieldset disabled={authReadOnly || !catalog.capabilities.canManageUsers}>
      <legend>Provisioning</legend>
      {!catalog.capabilities.canManageUsers ? <p className="external-auth-callout">User provisioning is managed outside Elsa in this ownership mode. Existing eligible linked users can still sign in.</p> : null}
      <label htmlFor={`${id}-policy`}>Unknown identities</label><select id={`${id}-policy`} value={form.admission.type} onChange={event => update({ admission: { ...form.admission, type: event.target.value,
        matcher: event.target.value === "match" ? "verified-email" : null } })}>
        {Array.from(new Set(["reject", ...catalog.provisioningPolicies, form.admission.type])).map(policy => <option key={policy} value={policy}>{policyLabel(policy)}</option>)}
      </select>
      <p className="external-auth-muted">{form.admission.type === "reject" ? "Only identities already linked to an eligible Elsa user can sign in." : form.admission.type === "match" ? "Match only a unique eligible Elsa user with the same verified email. Existing role assignments are preserved." : "The selected policy decides whether to create an Elsa user and which approved existing Elsa roles to assign."}</p>
      <h4>Existing Elsa roles</h4><p className="external-auth-muted">Assigned when a user is first created. Later sign-ins use roles saved in Elsa and preserve manual assignments. These roles also bound configured matching and custom policy grants.</p>
      {rolesLoading ? <p role="status">Loading roles…</p> : null}
      {roleError ? <p role="status">Role lookup is unavailable or you do not have role-read permission. Existing selections are preserved.</p> : null}
      <ul className="external-auth-picker">{roles.map(role => <li key={role.id}><label className="external-auth-check"><input type="checkbox" checked={form.admission.defaultRoleIds.includes(role.id)} disabled={!role.canDelegate && !form.admission.defaultRoleIds.includes(role.id)}
        onChange={event => update({ admission: { ...form.admission, defaultRoleIds: event.target.checked ? [...form.admission.defaultRoleIds, role.id] : form.admission.defaultRoleIds.filter(value => value !== role.id) } })} />
        {role.name}{!role.canDelegate ? " — outside your delegation boundary" : ""}</label></li>)}</ul>
      {form.admission.defaultRoleIds.filter(value => !roles.some(role => role.id === value)).map(value => <div key={value} className="external-auth-existing-role"><span>Selected role: {value}</span><StudioButton onClick={() => update({ admission: { ...form.admission, defaultRoleIds: form.admission.defaultRoleIds.filter(id => id !== value) } })}>Remove selection</StudioButton></div>)}
      {roleTotal > 100 ? <div className="external-auth-actions"><StudioButton disabled={roleSkip === 0 || rolesLoading} onClick={() => setRoleSkip(skip => Math.max(0, skip - 100))}>Previous roles</StudioButton><StudioButton disabled={roleSkip + 100 >= roleTotal || rolesLoading} onClick={() => setRoleSkip(skip => skip + 100)}>More roles</StudioButton></div> : null}
    </fieldset>
    {view?.signInCallback ? <CopyableIdentifier label="Sign-in callback URL" value={view.signInCallback} /> : null}
    {view?.previewCallback ? <CopyableIdentifier label="Preview callback URL" value={view.previewCallback} /> : null}
    <p className="external-auth-muted">{!view ? "Save this draft to get its callback URLs." : view.signInCallback && view.previewCallback
      ? "Register both callback URLs with the provider using authorization code flow and PKCE." : "Ask your integrator to configure the backend's public URL so it can provide trusted callback addresses."}</p>
    {localError ? <p role="alert" className="external-auth-error">{localError}</p> : null}
    <div className="external-auth-actions"><StudioButton type="submit" variant="primary" disabled={readOnly || busy || (Boolean(view) && !dirty)}>{view ? "Save changes" : "Save draft"}</StudioButton>
      <StudioButton onClick={onDiscard} disabled={busy}>{dirty ? "Discard changes" : "Close editor"}</StudioButton></div>
  </form>;
}

export function emptyDocument(): ConnectionDocument {
  return { id: "", key: "", displayName: "", protocol: "oidc", settings: { authority: "", clientId: "", scopes: "openid profile email", clientAuthentication: "client_secret_basic" },
    secrets: {}, admission: { type: "reject", defaultRoleIds: [], matcher: null }, enabled: false, preferred: false, displayOrder: 0, overridesDeployment: false, archivedAt: null };
}
function policyLabel(value: string) {
  return value === "reject" ? "Reject unknown identities" : value === "jit" ? "Create an Elsa user on first sign-in" :
    value === "match" ? "Match by verified email" : `Integrator policy: ${value}`;
}
