import React, { useCallback, useEffect, useRef, useState } from "react";
import { ArrowLeft } from "lucide-react";
import { notifyStudioNavigationGuardReleased, studioNavigationRequestedEvent, useStudioRouteLocation, useStudioWorkspaceTab, useStudioWorkspaceTitle, useStudioWorkspaceWhenActive, type ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { StudioAlert, StudioButton, StudioPage, StudioPageHeader, StudioResultState } from "@elsa-workflows/studio-ui";
import { CreateSecretForm } from "./SecretDialogs";
import { SecretDetail } from "./SecretDetail";
import { createSecret, getSecret, getSecretDescriptors, updateSecret } from "./secretsApi";
import type { SecretDescriptorsResponse, SecretMetadata } from "./secretTypes";
import type { SecretEditorState } from "./SecretEditorTabs";
import { readSecretBrowse, secretPath } from "./secretNavigation";

export function SecretEditorPage({ api, name, navigate }: { api: ElsaStudioModuleApi; name?: string; navigate(path: string): void }) {
  const location = useStudioRouteLocation();
  const browse = readSecretBrowse(location.search);
  const scope = api.backendSession ?? api.backend;
  const workspace = useStudioWorkspaceTab();
  const currentScope = useRef(scope); currentScope.current = scope;
  const mounted = useRef(true);
  const identity = useRef({ scope, name });
  const [loaded, setLoaded] = useState<{ scope: typeof scope; descriptors: SecretDescriptorsResponse; secret?: SecretMetadata }>();
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);
  const [loading, setLoading] = useState(true);
  const [formState, setFormState] = useState<SecretEditorState>({ dirty: false, busy: false });
  const phase = useRef({ dirty: false, busy: false, accepted: false });
  const lifetime = useRef(0);
  const whenActive = useStudioWorkspaceWhenActive();
  const data = loaded?.scope === scope ? loaded : undefined;
  useStudioWorkspaceTitle(name ? data?.secret?.displayName || data?.secret?.name || "Edit secret" : "Create secret");
  useEffect(() => { const generation = lifetime; mounted.current = true; return () => { mounted.current = false; generation.current++; }; }, []);
  useEffect(() => {
    if (identity.current.scope !== scope || identity.current.name !== name) {
      identity.current = { scope, name }; phase.current = { dirty: false, busy: false, accepted: false }; setFormState({ dirty: false, busy: false });
    }
  }, [scope, name]);
  const current = () => currentScope.current === scope && (mounted.current || workspace?.isActive() === false);
  const route = (path: string) => whenActive(() => { if (mounted.current && currentScope.current === scope) navigate(path); });
  useEffect(() => {
    let current = true;
    setLoading(true);
    void Promise.all([getSecretDescriptors(api.backend), name ? getSecret(api.backend, name) : Promise.resolve(undefined)]).then(([descriptors, secret]) => {
      if (!current) return;
      if (name && !secret) { setLoaded(undefined); setError("Secret not found."); return; }
      setLoaded({ scope, descriptors, secret }); setError("");
    }, failure => {
      if (!current) return;
      const status = typeof failure === "object" && failure && "status" in failure ? Number(failure.status) : 0;
      if ([401, 403, 404].includes(status)) setLoaded(undefined);
      setError(status === 404 ? "Secret not found." : status === 403 ? "You do not have permission to open this editor." : "Secret options could not be loaded. Try again.");
    }).finally(() => { if (current) setLoading(false); });
    return () => { current = false; };
  }, [api.backend, name, scope, revision]);
  const onStateChange = useCallback((state: SecretEditorState) => {
    phase.current.dirty = state.dirty; phase.current.busy = state.busy; setFormState(state);
  }, []);
  const leave = async () => {
    const origin = lifetime.current;
    if (!current()) return;
    if (!phase.current.accepted) {
      if (phase.current.busy) return;
      if (phase.current.dirty && !(await api.dialogs.confirm({ title: "Discard secret edits?", message: "Your unsaved changes will be discarded.", confirmLabel: "Discard changes", cancelLabel: "Keep editing", tone: "danger" }))) return;
      if (origin !== lifetime.current || phase.current.busy || !current()) return;
    }
    phase.current.dirty = false;
    notifyStudioNavigationGuardReleased();
    route(secretPath(browse, undefined, phase.current.accepted ? name ? "updated" : "created" : undefined, data?.secret?.name ?? name, location.href, !name));
  };
  useEffect(() => {
    const guard = (event: Event) => {
      if (!phase.current.accepted && (phase.current.dirty || phase.current.busy)) event.preventDefault();
    };
    window.addEventListener(studioNavigationRequestedEvent, guard);
    window.addEventListener("beforeunload", guard);
    return () => { window.removeEventListener(studioNavigationRequestedEvent, guard); window.removeEventListener("beforeunload", guard); };
  }, []);
  useEffect(() => { if (!formState.dirty && !formState.busy) notifyStudioNavigationGuardReleased(); }, [formState]);
  const accepted = () => { phase.current.accepted = true; phase.current.dirty = false; notifyStudioNavigationGuardReleased(); };
  return <StudioPage role="region" className="secrets-shell secrets-editor-page" aria-label={name ? "Edit secret" : "Create secret"}>
    <StudioPageHeader title={name ? "Edit secret" : "Create secret"} actions={<StudioButton variant="ghost" disabled={formState.busy} onClick={() => void leave()}><ArrowLeft size={16} aria-hidden />Back to secrets</StudioButton>} />
    {error ? <StudioAlert tone="danger">{error}<StudioButton disabled={loading} onClick={() => setRevision(value => value + 1)}>Retry secret options</StudioButton></StudioAlert> : null}
    {!data ? loading ? <p role="status">Loading secret editor…</p> : <StudioResultState title="Secret editor unavailable" description={error} />
      : name && data.secret ? <SecretDetail key={data.secret.id} secret={data.secret} descriptors={data.descriptors} presentation="page" onStateChange={onStateChange}
        onSave={async (key, request) => { phase.current.busy = true; try { await updateSecret(api.backend, key, request); if (current()) accepted(); } finally { if (currentScope.current === scope) phase.current.busy = false; } }} onClose={() => { if (current()) void leave(); }} />
      : <CreateSecretForm descriptors={data.descriptors} scopeCatalogLoading={loading} scopeCatalogError={Boolean(error)} onRetryScopeCatalog={() => setRevision(value => value + 1)} onStateChange={onStateChange}
        onSave={async request => { phase.current.busy = true; try { const created = await createSecret(api.backend, request); if (current()) { accepted(); route(secretPath(browse, undefined, "created", created.name, location.href)); } } finally { if (currentScope.current === scope) phase.current.busy = false; } }} onCancel={() => { if (current() && !phase.current.accepted) void leave(); }} />}
  </StudioPage>;
}
