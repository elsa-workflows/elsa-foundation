import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { notifyStudioNavigationGuardReleased, studioNavigationRequestedEvent, useStudioRouteLocation, useStudioWorkspaceTab, useStudioWorkspaceWhenActive,
  type StudioDialogApi, type StudioEndpointContext, type StudioNavigationRequest } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StatusChip, StudioButton, StudioPage, StudioPageHeader } from "@elsa-workflows/studio-ui";
import { ConnectionEditor, emptyDocument } from "./ConnectionEditor";
import { connectionChanges, ExternalAuthenticationClient, ExternalAuthenticationClientError, externalAuthenticationError,
  type ConnectionCatalog, type ConnectionCommand, type ConnectionDocument, type ConnectionPreview, type ConnectionTest, type ConnectionView, type Readiness } from "./sdk";
import { connectionState, previewCurrent, previewDescription, readinessDescription, sourceEffect, testDescription, testSucceeded } from "./presentation";
import { cleanPreviewUrl, preparePreviewReturn, takePreviewReturn } from "./previewReturn";
import { useCancelledActionFocus } from "./useCancelledActionFocus";

export function ExternalAuthenticationPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const client = useMemo(() => new ExternalAuthenticationClient(context.http, context.baseUrl), [context]);
  const [catalog, setCatalog] = useState<ConnectionCatalog | null>(null);
  const [loadedClient, setLoadedClient] = useState(client);
  const [loading, setLoading] = useState(true);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [seed, setSeed] = useState<ConnectionDocument | null>(null);
  const [creating, setCreating] = useState(false);
  const [editorVersion, setEditorVersion] = useState(0);
  const [dirty, setDirty] = useState(false);
  const [busy, setBusy] = useState<string | null>(null);
  const restoreCancelledFocus = useCancelledActionFocus(Boolean(busy), client);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [search, setSearch] = useState("");
  const [test, setTest] = useState<ConnectionTest | null>(null);
  const [preview, setPreview] = useState<ConnectionPreview | null>(null);
  const [readiness, setReadiness] = useState<{ value?: Readiness | null; error?: string } | null>(null);
  const currentClient = useRef(client); currentClient.current = client;
  const catalogRef = useRef(catalog); catalogRef.current = catalog;
  const lastClient = useRef(client);
  const requestNumber = useRef(0);
  const readinessRequest = useRef(0);
  const busyRef = useRef(false);
  const allowLeave = useRef(false);
  const returned = useRef(false);
  const routeLocation = useStudioRouteLocation();
  const locationRef = useRef(routeLocation); locationRef.current = routeLocation;
  const workspace = useStudioWorkspaceTab();
  const whenActive = useStudioWorkspaceWhenActive();
  const visible = loadedClient === client ? catalog : null;
  const selected = visible?.connections.find(view => view.document.id === selectedId) ?? null;
  const writable = Boolean(visible?.databaseManagement && visible.canManageConnections);
  const liveTest = testSucceeded(test, selected);
  const livePreview = previewCurrent(preview, selected);
  const configurationReady = readiness?.value?.ready === true;

  const clearProtectedState = useCallback(() => {
    ++readinessRequest.current;
    catalogRef.current = null; setCatalog(null); setSelectedId(null); setSeed(null); setCreating(false);
    setDirty(false); setTest(null); setPreview(null); setReadiness(null); setNotice("");
  }, []);

  const load = useCallback(async (preferred?: string | null) => {
    const request = ++requestNumber.current;
    setLoading(true); setError("");
    try {
      const next = await client.list();
      if (currentClient.current !== client || request !== requestNumber.current) return null;
      setCatalog(next); setLoadedClient(client);
      setSelectedId(current => preferred !== undefined ? preferred : next.connections.some(view => view.document.id === current) ? current : null);
      return next;
    } catch (cause) {
      if (currentClient.current === client && request === requestNumber.current) {
        setError(externalAuthenticationError(cause));
        const status = cause && typeof cause === "object" && "status" in cause ? cause.status : null;
        if (status === 401 || status === 403 || status === 404) clearProtectedState();
      }
      return null;
    } finally { if (currentClient.current === client && request === requestNumber.current) setLoading(false); }
  }, [client, clearProtectedState]);

  useEffect(() => {
    const request = ++readinessRequest.current;
    setReadiness(null);
    if (!selected || !visible) return;
    const abort = new AbortController();
    void (async () => {
      try {
        const detail = await client.detail(selected.document.id, abort.signal);
        if (abort.signal.aborted || currentClient.current !== client || request !== readinessRequest.current) return;
        if (detail.revision !== visible.revision || detail.connection.document.id !== selected.document.id || detail.connection.materialRevision !== selected.materialRevision)
          throw new ExternalAuthenticationClientError("Connection changed. Refresh connections to check the current configuration.");
        setReadiness({ value: detail.connection.readiness });
      } catch (cause) {
        if (abort.signal.aborted || currentClient.current !== client || request !== readinessRequest.current) return;
        setReadiness({ error: externalAuthenticationError(cause) });
        const status = cause && typeof cause === "object" && "status" in cause ? cause.status : null;
        if (status === 401 || status === 403 || status === 404) { setError(externalAuthenticationError(cause)); clearProtectedState(); }
      }
    })();
    return () => abort.abort();
  }, [client, selected, visible, clearProtectedState]);

  useEffect(() => {
    if (lastClient.current !== client) {
      lastClient.current = client; catalogRef.current = null;
      setCatalog(null); setSelectedId(null); setSeed(null); setCreating(false); setDirty(false); setTest(null); setPreview(null); setBusy(null); setNotice(""); setSearch("");
      busyRef.current = false; returned.current = false;
    }
    // Retained workspace tabs keep their editor and observations; resuming an effect must not reset a draft.
    if (!catalogRef.current) void load();
  }, [client, load]);

  useEffect(() => {
    if (!visible || returned.current || !locationRef.current.searchParams.has("externalPreviewNonce")) return;
    returned.current = true;
    const returnUrl = new URL(locationRef.current);
    // Remove the one-time result handle from the address bar before starting any follow-up requests.
    whenActive(() => {
      if (new URL(window.location.href).searchParams.get("externalPreviewNonce") === returnUrl.searchParams.get("externalPreviewNonce"))
        window.history.replaceState(window.history.state, "", cleanPreviewUrl(new URL(returnUrl)));
    });
    void (async () => {
      setBusy("Restoring preview"); busyRef.current = true;
      try {
        const result = takePreviewReturn(returnUrl, context.baseUrl, window.sessionStorage);
        if (!result) return;
        if (result.error || !result.handle) {
          setSelectedId(result.pending.recordId); setCreating(false); setSeed(null);
          setTest(result.pending.test); setPreview(null); setError(result.error || "Start another preview.");
          return;
        }
        const observation = await client.readPreview(result.key, result.handle);
        if (currentClient.current !== client) return;
        if (observation.connectionRecordId !== result.pending.recordId || observation.connectionKey !== result.key ||
          observation.materialRevision !== result.pending.materialRevision) throw new ExternalAuthenticationClientError("The preview does not match this saved draft. Test and preview again.");
        setSelectedId(observation.connectionRecordId); setCreating(false); setSeed(null);
        setTest(result.pending.test); setPreview(observation); setNotice("Preview complete. No user, role assignment or normal session was changed.");
      } catch (cause) { if (currentClient.current === client) setError(externalAuthenticationError(cause)); }
      finally { if (currentClient.current === client) { setBusy(null); busyRef.current = false; } }
    })();
  }, [visible, client, context.baseUrl, whenActive]);

  useEffect(() => {
    if (!dirty && !busy) { notifyStudioNavigationGuardReleased(); return; }
    const guard = (event: Event) => {
      const intent = (event as CustomEvent<StudioNavigationRequest>).detail?.intent;
      if (intent === "probe" || intent === "close" || !workspace) {
        event.preventDefault();
        if (intent !== "probe") setError("Save or discard your draft and finish pending requests before closing this page.");
      }
    };
    const unload = (event: BeforeUnloadEvent) => { if (!allowLeave.current) { event.preventDefault(); event.returnValue = ""; } };
    window.addEventListener(studioNavigationRequestedEvent, guard); window.addEventListener("beforeunload", unload);
    return () => { window.removeEventListener(studioNavigationRequestedEvent, guard); window.removeEventListener("beforeunload", unload); };
  }, [dirty, busy, workspace]);

  async function canDiscard() { return !dirty || await dialogs.confirm({ title: "Discard unsaved changes?", message: "Your saved connection will be kept.", confirmLabel: "Discard changes" }); }
  async function choose(view: ConnectionView) {
    if (busyRef.current || !await canDiscard() || currentClient.current !== client) return;
    setSelectedId(view.document.id); setCreating(false); setSeed(null); setDirty(false); setError(""); setEditorVersion(version => version + 1);
  }
  async function create(source?: ConnectionView) {
    if (busyRef.current || !writable || !await canDiscard() || currentClient.current !== client) return;
    setSeed(source ? { ...structuredClone(source.document), id: "", enabled: false, overridesDeployment: false, archivedAt: null } : emptyDocument());
    setSelectedId(null); setCreating(true); setDirty(false); setTest(null); setPreview(null); setError(""); setEditorVersion(version => version + 1);
  }
  async function refresh() {
    if (busyRef.current || !await canDiscard() || currentClient.current !== client) return;
    setDirty(false); setCreating(false); setSeed(null); setTest(null); setPreview(null); setEditorVersion(version => version + 1); await load();
  }
  async function change(command: ConnectionCommand, title: string) {
    if (busyRef.current || !writable) return;
    const opener = document.activeElement;
    busyRef.current = true; setBusy(title); setError(""); setNotice("");
    try {
      const assessed = await client.assess(command);
      if (currentClient.current !== client) return;
      if (!assessed.canApply || !assessed.proposed) throw { status: 409, payload: { failure: assessed.failure } };
      const effect = sourceEffect(assessed.proposed, command.record.key) || "The Studio record will be removed. Deployment configuration is retained.";
      if (!await dialogs.confirm({ title, message: `${effect}\n\nApply this change?`, confirmLabel: title,
        tone: [connectionChanges.disable, connectionChanges.archive, connectionChanges.remove, connectionChanges.promoteOverride].includes(command.change as 3 | 5 | 6 | 8) ? "danger" : "default" })) {
        if (currentClient.current === client) restoreCancelledFocus(opener);
        return;
      }
      if (currentClient.current !== client) return;
      const result = await client.apply(command);
      if (currentClient.current !== client) return;
      if (!result.saved) throw { status: 409, payload: result };
      setCreating(false); setSeed(null); setDirty(false); setTest(null); setPreview(null); setEditorVersion(version => version + 1);
      await load(command.change === connectionChanges.remove ? null : result.recordId ?? command.record.id);
      if (currentClient.current === client) setNotice("Connection updated. The sign-in chooser uses the current effective configuration.");
    } catch (cause) { if (currentClient.current === client) setError(externalAuthenticationError(cause)); }
    finally { if (currentClient.current === client) { busyRef.current = false; setBusy(null); } }
  }
  async function save(document: ConnectionDocument) {
    if (!visible) return;
    await change({ change: creating ? connectionChanges.create : connectionChanges.update, expectedRevision: visible.revision, record: document }, creating ? "Save draft" : "Save changes");
  }
  async function testConnection() {
    if (!selected || !visible || dirty || busyRef.current) return;
    const request = ++readinessRequest.current;
    busyRef.current = true; setBusy("Testing provider"); setError(""); setPreview(null);
    try {
      const result = await client.test(selected.document.id, visible.revision);
      if (currentClient.current === client && request === readinessRequest.current) {
        setTest(result);
        setReadiness(!result.stale && result.connectionRecordId === selected.document.id && result.materialRevision === selected.materialRevision
          ? { value: result.readiness } : { error: "Connection changed. Refresh connections to check the current configuration." });
      }
    }
    catch (cause) { if (currentClient.current === client) setError(externalAuthenticationError(cause)); }
    finally { if (currentClient.current === client) { busyRef.current = false; setBusy(null); } }
  }
  async function previewConnection() {
    if (!selected || !liveTest || dirty || busyRef.current) return;
    const opener = document.activeElement;
    busyRef.current = true; setBusy("Preparing preview"); setError("");
    try {
      if (!await dialogs.confirm({ title: "Preview provider sign-in", message: "You will leave this page to sign in with the provider, then return to this saved draft. Your Elsa administrator session stays signed in. No users or role assignments will be changed.", confirmLabel: "Continue to provider" })) {
        if (currentClient.current === client) restoreCancelledFocus(opener);
        return;
      }
      if (currentClient.current !== client) return;
      const returnUrl = preparePreviewReturn(new URL(locationRef.current), context.baseUrl, selected, test, window.sessionStorage);
      const submission = await client.previewSubmission(selected, returnUrl);
      whenActive(() => {
        if (currentClient.current !== client) return;
        const form = document.createElement("form"); form.method = "post"; form.action = submission.action;
        for (const [name, value] of Object.entries(submission.fields)) { const input = document.createElement("input"); input.type = "hidden"; input.name = name; input.value = value; form.appendChild(input); }
        document.body.appendChild(form); allowLeave.current = true;
        try { HTMLFormElement.prototype.submit.call(form); } finally { form.remove(); }
      });
    } catch (cause) { if (currentClient.current === client) { allowLeave.current = false; setError(externalAuthenticationError(cause)); } }
    finally { if (currentClient.current === client) { busyRef.current = false; setBusy(null); } }
  }
  const action = (changeKind: ConnectionCommand["change"], label: string, record = selected?.document) => {
    if (record && visible && !dirty) void change({ change: changeKind, expectedRevision: visible.revision, record }, label);
  };
  const collision = Boolean(selected && visible?.connections.some(view => view.source === 0 && view.document.key === selected.document.key));
  const matches = visible?.connections.filter(view => `${view.document.key} ${view.document.displayName}`.toLocaleLowerCase().includes(search.trim().toLocaleLowerCase())) ?? [];

  return <StudioPage className="external-auth-shell">
    <StudioPageHeader title="External sign-in" headingLevel={2} description="Connect identity providers while Elsa keeps control of its users, roles and permissions."
      actions={<><StudioButton onClick={() => { void refresh(); }} disabled={Boolean(busy) || loading}>Refresh</StudioButton><StudioButton variant="primary" disabled={!writable || Boolean(busy) || loading} onClick={() => { void create(); }}>Add connection</StudioButton></>} />
    {error ? <div role="alert" className="external-auth-error">{error}<StudioButton disabled={Boolean(busy)} onClick={() => { void refresh(); }}>Refresh connections</StudioButton></div> : null}
    {notice ? <p role="status" className="external-auth-notice">{notice}</p> : null}
    {busy ? <p role="status">{busy}…</p> : null}
    {loading && !visible ? <p role="status">Loading external sign-in connections…</p> : null}
    {visible ? <>
      {!visible.databaseManagement ? <p className="external-auth-callout">This deployment manages connections in configuration. Studio changes are disabled.</p> : visible.managementUnavailableReason === "identity-coordination-unavailable" ? <p className="external-auth-callout">Connection changes are unavailable on this host. Ask the host administrator to finish configuring identity management.</p> : !visible.canManageConnections ? <p className="external-auth-callout">You have read-only access to these connections.</p> : null}
      <div className="external-auth-workspace"><section className="external-auth-list" aria-label="Sign-in connections">
        <label htmlFor="external-auth-search">Find a connection</label><input id="external-auth-search" type="search" value={search} onChange={event => setSearch(event.target.value)} />
        {!matches.length ? <p>{visible.connections.length ? "No connections match this search." : "No external sign-in connections yet. Add one to prepare a disabled draft."}</p> : <ul>{matches.map(view => <li key={view.document.id}>
          <button type="button" className="external-auth-row" aria-pressed={selectedId === view.document.id && !creating} disabled={Boolean(busy)} onClick={() => { void choose(view); }}>
            <strong>{view.document.displayName || view.document.key}</strong><span>{view.document.key}</span>
            <span className="external-auth-row-status"><StatusChip>{connectionState(view)}</StatusChip><span>{view.source === 0 ? "Deployment" : "Studio"}</span></span>
            {view.document.preferred && view.effective && view.document.enabled ? <small>Preferred choice</small> : null}
          </button></li>)}</ul>}
      </section><section className="external-auth-detail" aria-label="Connection configuration">
        {selected || creating ? <>
          {selected ? <div className="external-auth-actions">
            {selected.source === 0 && selected.configurationSupported && selected.overridePermitted && writable && !visible.connections.some(view => view.source === 1 && view.document.key === selected.document.key) ? <StudioButton disabled={Boolean(busy)} onClick={() => { void create(selected); }}>Prepare full override</StudioButton> : null}
            {selected.source === 1 && writable ? <>
              {selected.document.archivedAt ? <StudioButton disabled={Boolean(busy) || dirty} onClick={() => action(connectionChanges.restore, "Restore connection")}>Restore</StudioButton> : <>
                {selected.document.enabled ? <StudioButton disabled={Boolean(busy) || dirty} onClick={() => action(connectionChanges.disable, "Disable sign-in")}>Disable sign-in</StudioButton> : null}
                <StudioButton disabled={Boolean(busy) || dirty} onClick={() => action(connectionChanges.archive, "Archive connection")}>Archive</StudioButton>
              </>}
              <StudioButton disabled={Boolean(busy) || dirty} onClick={() => action(connectionChanges.remove, "Remove Studio connection")}>Remove</StudioButton>
            </> : null}
          </div> : null}
          {selected ? <section className="external-auth-callout" aria-label="Current configuration readiness">
            <h3>{readiness?.value ? readiness.value.ready ? "Current configuration ready" : "Current configuration needs attention" : "Current configuration readiness"}</h3>
            <p>{!readiness ? "Checking current configuration…" : readiness.error || (readiness.value ? readinessDescription(readiness.value) : "Readiness is unavailable on this backend.")}</p>
            <small>This assessment is separate from configured enablement and the last observed provider test.</small>
          </section> : null}
          <ConnectionEditor key={`${context.baseUrl}:${creating ? "create" : selected?.document.id}:${editorVersion}`} client={client} catalog={visible} view={selected} seed={seed} busy={Boolean(busy)} onSave={save} onDirty={setDirty}
            onDiscard={() => { void (async () => { if (await canDiscard() && currentClient.current === client) { setDirty(false); setCreating(false); setSeed(null); setSelectedId(null); } })(); }} />
          {selected && !selected.document.archivedAt && visible.canManageConnections ? <section className="external-auth-observations" aria-label="Test and preview">
            <h3>Test, preview, then enable</h3><p>Save configuration changes first. Tests verify provider metadata; preview verifies a real sign-in and shows its prospective Elsa access.</p>
            <div className="external-auth-actions"><StudioButton disabled={Boolean(busy) || dirty} onClick={() => { void testConnection(); }}>Test configuration</StudioButton><StudioButton disabled={Boolean(busy) || dirty || !liveTest || !configurationReady} onClick={() => { void previewConnection(); }}>Preview sign-in</StudioButton></div>
            {test?.connectionRecordId === selected.document.id ? <div role="status" className="external-auth-callout"><strong>{testSucceeded(test, selected) ? "Configuration test passed" : "Configuration test needs attention"}</strong><p>{testDescription(test)}</p><small>Observed {new Date(test.observedAt).toLocaleString()}. Results describe that observation, not future availability.</small></div> : null}
            {preview?.connectionRecordId === selected.document.id ? <div className="external-auth-preview"><h4>Preview result</h4><p>{previewDescription(preview)}</p>
              <CopyableIdentifier label="Validated issuer" value={preview.issuer} /><CopyableIdentifier label="External subject" value={preview.subject} />
              <dl><dt>Elsa user</dt><dd>{preview.existingUserId || (preview.admission === 2 ? "Would be created" : "No user admitted")}</dd><dt>Roles</dt><dd>{preview.roleIds.join(", ") || "None"}</dd><dt>Permissions</dt><dd>{preview.permissions.map(value => value === "*" ? "All permissions" : value).join(", ") || "None"}</dd></dl>
              <p className="external-auth-muted">No user, link, role assignment or normal session was changed by this preview.</p>
            </div> : null}
            {selected.source === 1 && writable && !selected.document.enabled ? <div className="external-auth-actions"><StudioButton variant="primary" disabled={Boolean(busy) || dirty || !liveTest || !livePreview || !configurationReady || (collision && !selected.document.overridesDeployment && !selected.overridePermitted)}
              onClick={() => action(collision && !selected.document.overridesDeployment ? connectionChanges.promoteOverride : connectionChanges.enable,
                collision && !selected.document.overridesDeployment ? "Use as override" : "Enable sign-in", { ...selected.document, enabled: true })}>{collision && !selected.document.overridesDeployment ? "Use as override" : "Enable sign-in"}</StudioButton>
              {!liveTest || !livePreview ? <span className="external-auth-muted">Test and preview this saved draft before enabling it.</span> : null}</div> : null}
          </section> : null}
        </> : <div className="external-auth-empty"><h3>Choose a connection</h3><p>Inspect its source, settings and callbacks, or prepare a new disabled draft.</p></div>}
      </section></div>
    </> : null}
  </StudioPage>;
}
