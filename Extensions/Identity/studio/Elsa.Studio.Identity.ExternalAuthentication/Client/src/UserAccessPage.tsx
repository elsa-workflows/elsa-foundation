import React, { useCallback, useEffect, useId, useMemo, useRef, useState } from "react";
import { notifyStudioNavigationGuardReleased, studioNavigationRequestedEvent, useStudioWorkspaceTab,
  type StudioDialogApi, type StudioEndpointContext, type StudioNavigationRequest } from "@elsa-workflows/studio-sdk";
import { CopyableIdentifier, StatusChip, StudioButton, StudioPage, StudioPageHeader } from "@elsa-workflows/studio-ui";
import { ExternalAuthenticationClient, identityAccessError, type IdentityLink, type IdentityLinks,
  type IdentitySession, type IdentitySessions, type IdentityUser, type IdentityUsers } from "./sdk";

import { useCancelledActionFocus } from "./useCancelledActionFocus";

const pageSize = 25;
function date(value: string | null) { return value && Number.isFinite(Date.parse(value)) ? new Date(value).toLocaleString() : "Not recorded"; }
function status(value: number) { return value === 0 ? "Active" : value === 1 ? "Disabled" : value === 2 ? "Locked" : "Unknown status"; }

export function UserAccessPage({ context, dialogs }: { context: StudioEndpointContext; dialogs: StudioDialogApi }) {
  const client = useMemo(() => new ExternalAuthenticationClient(context.http, context.baseUrl), [context]);
  const prefix = useId(); const workspace = useStudioWorkspaceTab();
  const [directory, setDirectory] = useState<{ client: ExternalAuthenticationClient; data: IdentityUsers } | null>(null);
  const [directoryError, setDirectoryError] = useState(""); const [loadingUsers, setLoadingUsers] = useState(true);
  const [search, setSearch] = useState(""); const [appliedSearch, setAppliedSearch] = useState(""); const [userSkip, setUserSkip] = useState(0);
  const [selected, setSelected] = useState<IdentityUser | null>(null);
  const [links, setLinks] = useState<IdentityLinks | null>(null); const [sessions, setSessions] = useState<IdentitySessions | null>(null);
  const [linksError, setLinksError] = useState(""); const [sessionsError, setSessionsError] = useState("");
  const [linkSkip, setLinkSkip] = useState(0); const [sessionSkip, setSessionSkip] = useState(0); const [loadingAccess, setLoadingAccess] = useState(false);
  const [connection, setConnection] = useState(""); const [subject, setSubject] = useState("");
  const [busy, setBusy] = useState(""); const [error, setError] = useState(""); const [notice, setNotice] = useState("");
  const restoreCancelledFocus = useCancelledActionFocus(Boolean(busy), client);
  const [needsRefresh, setNeedsRefresh] = useState(false); const [reauthenticate, setReauthenticate] = useState(false);
  const active = useRef(client); active.current = client;
  const lastClient = useRef(client); const directoryRequest = useRef(0); const panelRequest = useRef(0);
  const selectedRef = useRef(selected); selectedRef.current = selected;
  const directoryRef = useRef(directory); directoryRef.current = directory;
  const busyRef = useRef(false);
  const visible = directory?.client === client ? directory.data : null;
  const target = visible ? selected : null;
  const dirty = connection !== "" || subject !== "";
  const locked = Boolean(busy || loadingUsers || loadingAccess || needsRefresh || reauthenticate);

  const loadUsers = useCallback(async (query: string, skip: number) => {
    const request = ++directoryRequest.current; setLoadingUsers(true); setDirectoryError("");
    try {
      const next = await client.users(query, skip);
      if (active.current !== client || request !== directoryRequest.current) return null;
      setDirectory({ client, data: next }); setAppliedSearch(query); setUserSkip(skip); return next;
    } catch (cause) {
      if (active.current === client && request === directoryRequest.current) {
        setDirectoryError(identityAccessError(cause)); setNeedsRefresh(true);
        const status = cause && typeof cause === "object" && "status" in cause ? cause.status : null;
        if (status === 401 || status === 403 || status === 404) {
          directoryRef.current = null; selectedRef.current = null; setDirectory(null); setSelected(null);
          setLinks(null); setSessions(null); setConnection(""); setSubject("");
        }
      }
      return null;
    } finally { if (active.current === client && request === directoryRequest.current) setLoadingUsers(false); }
  }, [client]);

  const loadAccess = useCallback(async (user: IdentityUser, metadata: IdentityUsers, nextLinks: number, nextSessions: number) => {
    const request = ++panelRequest.current; setLoadingAccess(true); setLinksError(""); setSessionsError("");
    const results = await Promise.allSettled([
      metadata.canReadLinks ? client.links(user.id, nextLinks) : Promise.resolve(null),
      metadata.canReadSessions && !metadata.sessionAdministrationUnavailableReason ? client.sessions(user.id, nextSessions) : Promise.resolve(null)
    ]);
    if (active.current !== client || selectedRef.current?.id !== user.id || request !== panelRequest.current) return;
    const [linkResult, sessionResult] = results;
    setLinks(linkResult.status === "fulfilled" ? linkResult.value : null);
    setSessions(sessionResult.status === "fulfilled" ? sessionResult.value : null);
    if (linkResult.status === "rejected") setLinksError(identityAccessError(linkResult.reason));
    if (sessionResult.status === "rejected") setSessionsError(identityAccessError(sessionResult.reason));
    setNeedsRefresh(false); setLoadingAccess(false);
  }, [client]);

  useEffect(() => {
    if (lastClient.current !== client) {
      lastClient.current = client; directoryRef.current = null; selectedRef.current = null;
      setDirectory(null); setSelected(null); setLinks(null); setSessions(null); setConnection(""); setSubject("");
      setSearch(""); setAppliedSearch(""); setUserSkip(0); setLinkSkip(0); setSessionSkip(0);
      setBusy(""); busyRef.current = false; setError(""); setNotice(""); setNeedsRefresh(false); setReauthenticate(false);
    }
    if (directoryRef.current?.client !== client) void loadUsers("", 0);
  }, [client, loadUsers]);

  useEffect(() => {
    const metadata = directoryRef.current?.client === client ? directoryRef.current.data : null;
    if (selected && metadata) void loadAccess(selected, metadata, linkSkip, sessionSkip);
  }, [client, selected, linkSkip, sessionSkip, loadAccess]);

  useEffect(() => {
    if (!dirty && !busy) { notifyStudioNavigationGuardReleased(); return; }
    const guard = (event: Event) => {
      const intent = (event as CustomEvent<StudioNavigationRequest>).detail?.intent;
      if (intent === "probe" || intent === "close" || !workspace) {
        event.preventDefault();
        if (intent !== "probe") setError("Attach or discard the link draft and finish pending requests before closing this page.");
      }
    };
    const unload = (event: BeforeUnloadEvent) => { event.preventDefault(); event.returnValue = ""; };
    window.addEventListener(studioNavigationRequestedEvent, guard); window.addEventListener("beforeunload", unload);
    return () => { window.removeEventListener(studioNavigationRequestedEvent, guard); window.removeEventListener("beforeunload", unload); };
  }, [busy, dirty, workspace]);

  async function choose(user: IdentityUser) {
    if (busyRef.current || reauthenticate || user.id === selected?.id) return;
    if (dirty && !await dialogs.confirm({ title: "Discard link draft?", message: "Select another user and discard the unattached subject?", confirmLabel: "Discard draft" })) return;
    if (active.current !== client) return;
    selectedRef.current = user; setSelected(user); setLinks(null); setSessions(null); setLinkSkip(0); setSessionSkip(0);
    setConnection(""); setSubject(""); setError(""); setNotice(""); setNeedsRefresh(false);
  }

  async function refresh() {
    if (busyRef.current || reauthenticate) return;
    setError(""); setNotice("");
    const selectedId = selectedRef.current?.id;
    ++panelRequest.current; setLoadingAccess(false);
    const next = await loadUsers(appliedSearch, userSkip);
    if (!next || active.current !== client || selectedRef.current?.id !== selectedId) return;
    const fresh = next.items.find(user => user.id === selectedId);
    if (!fresh && selectedRef.current && dirty) {
      const discard = await dialogs.confirm({ title: "Discard link draft?",
        message: "The selected user is not in these refreshed results. It may be filtered out, on another page or unavailable. Discard the attachment draft and clear the selection?", confirmLabel: "Discard draft" });
      if (active.current !== client || selectedRef.current?.id !== selectedId) return;
      if (!discard) {
        setNeedsRefresh(true); setNotice("Your link draft is kept. Find the selected user and refresh before making changes."); return;
      }
    }
    const current = fresh ? { ...fresh } : null;
    selectedRef.current = current; setSelected(current);
    if (!current) {
      ++panelRequest.current; setLinks(null); setSessions(null); setLoadingAccess(false);
      setConnection(""); setSubject(""); setNeedsRefresh(false);
    }
  }

  async function mutate(title: string, message: string, operation: () => Promise<{ ok: boolean; failure: string | null }>, success: string,
    options: { attached?: boolean; currentSession?: boolean } = {}) {
    if (!target || locked || busyRef.current) return;
    const opener = document.activeElement;
    const user = target; busyRef.current = true; setBusy(title); setError(""); setNotice("");
    try {
      if (!await dialogs.confirm({ title, message, confirmLabel: title, tone: options.attached ? "default" : "danger" })) {
        if (active.current === client && selectedRef.current?.id === user.id) restoreCancelledFocus(opener);
        return;
      }
      if (active.current !== client || selectedRef.current?.id !== user.id) return;
      const result = await operation();
      if (active.current !== client || selectedRef.current?.id !== user.id) return;
      if (!result.ok) throw { status: 409, payload: { failure: result.failure } };
      if (options.attached) { setConnection(""); setSubject(""); }
      setNotice(success);
      if (options.currentSession) {
        setReauthenticate(true);
        setSessions(current => current ? { ...current, items: current.items.map(item => item.current ? { ...item, state: "revoked", canRevoke: false } : item) } : current);
      } else if (visible) await loadAccess(user, visible, linkSkip, sessionSkip);
    } catch (cause) {
      if (active.current === client && selectedRef.current?.id === user.id) { setError(identityAccessError(cause)); setNeedsRefresh(true); }
    } finally { if (active.current === client) { setBusy(""); busyRef.current = false; } }
  }

  function attach() {
    if (!target || !links?.canAttach || !links.userRevision || !connection || !subject.trim()) return;
    const choice = links.attachChoices.find(item => item.id === connection); if (!choice) return;
    const userId = target.id; const snapshot = links;
    void mutate("Attach identity", `Attach ${choice.displayName} (${choice.key}) subject “${subject}” to ${target.userName}?\n\nSaved Elsa roles and permissions stay assigned as they are. An identity already owned by another user will be refused.`, async () => {
      const result = await client.attach(userId, { connectionRecordId: connection, subject, expectedCatalogRevision: snapshot.catalogRevision, expectedUserRevision: snapshot.userRevision! });
      return { ok: result.attached, failure: result.failure };
    }, "Identity attached. Saved Elsa roles are unchanged.", { attached: true });
  }

  function remove(link: IdentityLink) {
    if (!target || !links?.userRevision || !link.canRemove || !link.qualified || !link.issuer || !link.revision) return;
    const userId = target.id; const snapshot = links;
    void mutate("Remove link", `Remove ${link.connectionDisplayName ?? link.provider} subject “${link.subject}” (${link.issuer}) from ${target.userName}?\n\nSessions using this link will stop granting access. Elsa will check that another user sign-in method and administrative recovery remain usable. Saved roles are preserved.`, async () => {
      const result = await client.removeLink(userId, { provider: link.provider, issuer: link.issuer!, subject: link.subject,
        expectedLinkRevision: link.revision!, expectedUserRevision: snapshot.userRevision!, expectedCatalogRevision: snapshot.catalogRevision });
      return { ok: result.removed, failure: result.failure };
    }, "Identity link removed. Other links and saved roles are preserved.");
  }

  function revoke(session: IdentitySession) {
    if (!target || !session.canRevoke) return;
    const userId = target.id;
    void mutate("Revoke session", `Revoke ${target.userName}'s ${session.connectionKey ?? session.provider} session created ${date(session.createdAt)}?\n\nIts Elsa cookie, access and refresh credentials will stop granting access.${session.current ? " This is your current session; you will need to sign in again." : " Other sessions will remain available."}`, async () => {
      const result = await client.revokeSession(userId, session); return { ok: result.revoked, failure: result.failure };
    }, session.current ? "Current session revoked. Sign in to the selected backend again before continuing." : "Session revoked. Other sessions are unchanged.", { currentSession: session.current });
  }

  return <StudioPage className="external-auth-shell"><StudioPageHeader title="User access" headingLevel={2} description="Inspect linked identities and sessions. These actions preserve saved Elsa role assignments."
    actions={<StudioButton onClick={() => void refresh()} disabled={Boolean(busy || loadingUsers || reauthenticate)}>Refresh user access</StudioButton>} />
    {directoryError ? <p role="alert" className="external-auth-error">{directoryError}</p> : null}
    {error ? <p role="alert" className="external-auth-error">{error}{needsRefresh ? " Refresh user access before another change." : ""}</p> : null}
    {notice ? <p role="status" className="external-auth-notice">{notice}</p> : null}
    {loadingUsers && !visible ? <p role="status">Loading users…</p> : null}
    {visible ? <><div className="external-auth-workspace"><section className="external-auth-list" aria-label="Users">
      <form onSubmit={event => { event.preventDefault(); if (!busyRef.current) void loadUsers(search, 0); }}>
        <label htmlFor={`${prefix}-search`}>Exact username or email</label>
        <input id={`${prefix}-search`} value={search} maxLength={400} onChange={event => setSearch(event.target.value)} disabled={Boolean(busy || reauthenticate)} />
        <StudioButton type="submit" disabled={Boolean(busy || loadingUsers || reauthenticate)}>Find users</StudioButton>
      </form>
      {!visible.items.length ? <p className="external-auth-empty">No users match this search.</p> : <ul>{visible.items.map(user => <li key={user.id}>
        <button type="button" className="external-auth-row" aria-pressed={target?.id === user.id} onClick={() => void choose(user)} disabled={Boolean(busy || reauthenticate)}>
          <strong>{user.displayName || user.userName}</strong><span>{user.userName}</span><small>{status(user.status)}</small>
        </button></li>)}</ul>}
      <Pager label="Users" skip={userSkip} count={visible.items.length} total={visible.totalCount} disabled={Boolean(busy || loadingUsers || reauthenticate)}
        previous={() => void loadUsers(appliedSearch, Math.max(0, userSkip - pageSize))} next={() => void loadUsers(appliedSearch, userSkip + pageSize)} />
    </section><section className="external-auth-detail" aria-label="Selected user access">
      {!target ? <p className="external-auth-empty">Select a user to inspect links and sessions.</p> : <>
        <h2>{target.displayName || target.userName}</h2><CopyableIdentifier value={target.id} label="User ID" /><StatusChip>{status(target.status)}</StatusChip>
        {!visible.canManageUsers ? <p className="external-auth-callout">The configured identity authority owns these users. Link changes are read-only.</p> : null}
        {visible.linkManagementUnavailableReason ? <p className="external-auth-callout">{identityAccessError({ payload: { error: visible.linkManagementUnavailableReason } })}</p> : null}
        {loadingAccess ? <p role="status">Loading user access…</p> : null}
        <section className="external-auth-user-section" aria-label="Identity links"><h3>Identity links</h3>
          {!visible.canReadLinks ? <p className="external-auth-callout">You do not have permission to read identity links.</p> : linksError ? <p role="alert" className="external-auth-error">{linksError}</p> : links ? <>
            {!links.items.length ? <p className="external-auth-empty">No identities are linked to this user.</p> : links.items.map(link => <article className="external-auth-user-entry" key={`${link.provider}:${link.issuer}:${link.subject}`}>
              <h4>{link.connectionDisplayName ?? link.provider}</h4><dl><dt>Provider key</dt><dd>{link.provider}</dd><dt>Issuer</dt><dd>{link.issuer ?? "Legacy issuer-less identity"}</dd>
                <dt>Subject</dt><dd><CopyableIdentifier value={link.subject} label="External subject" /></dd><dt>Linked</dt><dd>{date(link.linkedAt)}</dd></dl>
              {!link.connectionConfigured ? <p className="external-auth-muted">This provider is no longer configured on this backend.</p> : null}
              {!link.qualified ? <p className="external-auth-muted">Legacy links are read-only here.</p> : null}
              <StudioButton className="external-auth-danger-action" disabled={locked || !link.canRemove || !visible.canManageUsers || Boolean(visible.sessionAdministrationUnavailableReason)} onClick={() => remove(link)}>Remove link</StudioButton>
            </article>)}
            <Pager label="Links" skip={linkSkip} count={links.items.length} total={links.totalCount} disabled={locked} previous={() => setLinkSkip(Math.max(0, linkSkip - pageSize))} next={() => setLinkSkip(linkSkip + pageSize)} />
            {links.canAttach && visible.canManageUsers ? <form className="external-auth-editor" onSubmit={event => { event.preventDefault(); attach(); }}><fieldset disabled={locked}>
              <legend>Attach external identity</legend><label htmlFor={`${prefix}-connection`}>Connection</label>
              <select id={`${prefix}-connection`} value={connection} onChange={event => setConnection(event.target.value)} required>
                <option value="">Select a configured connection</option>{links.attachChoices.map(choice => <option key={choice.id} value={choice.id}>{choice.displayName} ({choice.key}){!choice.effective ? " · not effective" : !choice.enabled ? " · disabled" : ""}</option>)}
              </select><label htmlFor={`${prefix}-subject`}>External subject</label><input id={`${prefix}-subject`} value={subject} maxLength={400} onChange={event => setSubject(event.target.value)} required />
              <small>Use the exact subject ID from this provider. The issuer comes from the saved connection; saved Elsa roles stay unchanged.</small>
              <StudioButton type="submit" disabled={!connection || !subject.trim() || !links.userRevision}>Attach identity</StudioButton>
              {dirty ? <StudioButton type="button" onClick={() => { setConnection(""); setSubject(""); }}>Discard link draft</StudioButton> : null}
            </fieldset></form> : <p className="external-auth-muted">Link changes are unavailable for your current permissions, this account or the configured identity authority.</p>}
          </> : null}
        </section>
        <section className="external-auth-user-section" aria-label="Sessions"><h3>Sessions</h3>
          {!visible.canReadSessions ? <p className="external-auth-callout">You do not have permission to read sessions.</p> : visible.sessionAdministrationUnavailableReason ?
            <p className="external-auth-callout">{identityAccessError({ payload: { error: visible.sessionAdministrationUnavailableReason } })} Link removal also requires this service.</p> : sessionsError ? <p role="alert" className="external-auth-error">{sessionsError}</p> : sessions ? <>
              {!sessions.items.length ? <p className="external-auth-empty">No retained sessions for this user.</p> : sessions.items.map(session => <article className="external-auth-user-entry" key={session.id}>
                <h4>{session.connectionKey ?? session.provider}{session.current ? " · Current session" : ""}</h4><StatusChip>{session.state}</StatusChip>
                <dl><dt>Session ID</dt><dd><CopyableIdentifier value={session.id} label="Session ID" /></dd><dt>Created</dt><dd>{date(session.createdAt)}</dd>
                  <dt>Expires</dt><dd>{date(session.expiresAt)}</dd>{session.revokedAt ? <><dt>Revoked</dt><dd>{date(session.revokedAt)}</dd></> : null}</dl>
                <StudioButton className="external-auth-danger-action" disabled={locked || !session.canRevoke || !visible.canRevokeTokens} onClick={() => revoke(session)}>Revoke session</StudioButton>
              </article>)}
              <Pager label="Sessions" skip={sessionSkip} count={sessions.items.length} total={sessions.totalCount} disabled={locked} previous={() => setSessionSkip(Math.max(0, sessionSkip - pageSize))} next={() => setSessionSkip(sessionSkip + pageSize)} />
            </> : null}
        </section>
      </>}
    </section></div></> : null}
  </StudioPage>;
}

function Pager({ label, skip, count, total, disabled, previous, next }: { label: string; skip: number; count: number; total: number; disabled: boolean; previous: () => void; next: () => void }) {
  return <nav className="external-auth-actions" aria-label={`${label} pages`}><span>{total ? `${skip + 1}–${skip + count} of ${total}` : "0 results"}</span>
    <StudioButton disabled={disabled || skip === 0} onClick={previous}>Previous {label.toLowerCase()}</StudioButton>
    <StudioButton disabled={disabled || skip + count >= total} onClick={next}>Next {label.toLowerCase()}</StudioButton></nav>;
}
