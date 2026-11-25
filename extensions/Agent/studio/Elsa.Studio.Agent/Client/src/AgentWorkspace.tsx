import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { AlertTriangle, ArrowLeft, FlaskConical } from "lucide-react";
import type {
  StudioActivityDefinitionImplementationEditorContribution,
  StudioEndpointContext,
  StudioRuntimeIdentity,
  StudioActivityDefinitionRecoverySettings
} from "@elsa-workflows/studio-sdk";
import {
  ActivityDefinitionDraftEditor, getActivityDefinitionDraftSummary, getActivityDefinitionVersionSummary,
  listAllActivityDefinitionVersions,
  type ActivityDefinitionWorkspaceContribution
} from "@elsa-workflows/studio-workflows/authoring";
import { StudioButton, StudioPage, StudioPageHeader, StudioResultState, StudioTimestamp } from "@elsa-workflows/studio-ui";
import { AgentPlayground } from "./AgentPlayground";
import { agentProviderKey, listActiveAgentDraftChoices, type ActiveAgentDraftChoice, type AgentVersionChoice } from "./agentsApi";
import { agentWorkspaceDraftPath } from "./agentWorkspaceRoutes";
import "./styles.css";

type WorkspaceRoute = { definitionId: string; draftId?: string; versionId?: string };
type Resolution =
  | { kind: "loading" }
  | { kind: "error"; message: string }
  | { kind: "choices"; drafts: ActiveAgentDraftChoice[]; fallbackVersionId: string | null }
  | { kind: "draft"; draftId: string }
  | { kind: "version"; versionId: string; readOnly: boolean }
  | { kind: "empty" };

function readWorkspaceRoute(pathname: string): WorkspaceRoute | null {
  const match = /^\/agents\/workspace\/([^/]+)(?:\/drafts\/([^/]+)|\/versions\/([^/]+))?\/?$/.exec(pathname);
  if (!match) return null;
  try {
    return { definitionId: decodeURIComponent(match[1]), ...(match[2] ? { draftId: decodeURIComponent(match[2]) } : {}), ...(match[3] ? { versionId: decodeURIComponent(match[3]) } : {}) };
  } catch {
    return null;
  }
}

export function agentWorkspaceVersionPath(definitionId: string, versionId: string) {
  return `/agents/workspace/${encodeURIComponent(definitionId)}/versions/${encodeURIComponent(versionId)}`;
}

async function listAgentVersions(context: StudioEndpointContext, definitionId: string, signal?: AbortSignal): Promise<AgentVersionChoice[]> {
  const items = await listAllActivityDefinitionVersions(context, definitionId, signal);
  return items.flatMap(item => item.providerKey === agentProviderKey ? [{
    versionId: item.version.versionId,
    version: item.version.version,
    lifecycle: item.version.lifecycle,
    publishedAt: item.version.publishedAt
  }] : []);
}

export function AgentWorkspace({ context, navigate, activityEditors, identity, recoverySettings, testRunAvailable = false }: {
  context: StudioEndpointContext;
  navigate(path: string): void;
  activityEditors: StudioActivityDefinitionImplementationEditorContribution[];
  identity?: StudioRuntimeIdentity;
  recoverySettings?: StudioActivityDefinitionRecoverySettings;
  testRunAvailable?: boolean;
}) {
  const route = readWorkspaceRoute(window.location.pathname);
  const fallbackVersionId = new URLSearchParams(window.location.search).get("fallbackVersion");
  const [resolution, setResolution] = useState<Resolution>({ kind: "loading" });
  const [showVersions, setShowVersions] = useState(false);
  const navigationBlocked = useRef(false);

  const load = useCallback(() => {
    if (!route) { setResolution({ kind: "error", message: "This Agent workspace link is invalid." }); return; }
    const abort = new AbortController();
    setResolution({ kind: "loading" });
    void (async () => {
      try {
        if (route.draftId) {
          const draft = await getActivityDefinitionDraftSummary(context, route.definitionId, route.draftId, abort.signal);
          if (draft.draft.draftId !== route.draftId || draft.draft.providerKey !== agentProviderKey)
            throw new Error("This exact draft is not an Agent draft for the requested definition.");
          if (!abort.signal.aborted) setResolution({ kind: "draft", draftId: route.draftId! });
          return;
        }
        if (route.versionId) {
          const version = await getActivityDefinitionVersionSummary(context, route.definitionId, route.versionId, abort.signal);
          if (version.version.versionId !== route.versionId || version.providerKey !== agentProviderKey)
            throw new Error("This exact version is not an Agent version for the requested definition.");
          if (!abort.signal.aborted) setResolution({ kind: "version", versionId: route.versionId!, readOnly: true });
          return;
        }

        const drafts = await listActiveAgentDraftChoices(context, route.definitionId, abort.signal);
        if (abort.signal.aborted) return;
        if (drafts.length === 1) {
          const draft = await getActivityDefinitionDraftSummary(context, route.definitionId, drafts[0].draftId, abort.signal);
          if (draft.draft.draftId !== drafts[0].draftId || draft.draft.providerKey !== agentProviderKey)
            throw new Error("The current draft could not be confirmed as an Agent draft.");
          if (!abort.signal.aborted) navigate(agentWorkspaceDraftPath(route.definitionId, drafts[0].draftId));
          return;
        }
        if (drafts.length > 1) {
          setResolution({ kind: "choices", drafts, fallbackVersionId });
          return;
        }
        if (fallbackVersionId) {
          const version = await getActivityDefinitionVersionSummary(context, route.definitionId, fallbackVersionId, abort.signal);
          if (version.version.versionId !== fallbackVersionId || version.providerKey !== agentProviderKey)
            throw new Error("The published Agent version could not be confirmed.");
          if (!abort.signal.aborted) setResolution({ kind: "version", versionId: fallbackVersionId, readOnly: false });
          return;
        }
        if (!abort.signal.aborted) setResolution({ kind: "empty" });
      } catch (error) {
        if (!abort.signal.aborted) setResolution({ kind: "error", message: error instanceof Error ? error.message : "Studio could not confirm the Agent workspace." });
      }
    })();
    return () => abort.abort();
  }, [context, fallbackVersionId, navigate, route?.definitionId, route?.draftId, route?.versionId]);

  useEffect(() => load(), [load]);

  const workspaceContribution = useMemo<ActivityDefinitionWorkspaceContribution>(() => ({
    homeLabel: "Agents",
    homePath: "/agents",
    ...(testRunAvailable ? { testHost: {
      label: "Test Run",
      render: ({ context: hostContext, draftId, expectedRevision, currentRevision, canRun, onClose }) => <AgentPlayground
        context={hostContext} draftId={draftId} embedded expectedRevision={expectedRevision} currentRevision={currentRevision} canRun={canRun} onClose={onClose} />
    } } : {})
  }), [testRunAvailable]);

  if (!route) return <StudioPage><StudioResultState tone="danger" icon={<AlertTriangle size={22} aria-hidden />} title="Agent workspace unavailable" description="This Agent workspace link is invalid." actions={<StudioButton onClick={() => navigate("/agents")}>Back to Agents</StudioButton>} /></StudioPage>;
  if (resolution.kind === "loading") return <StudioPage aria-busy="true"><p role="status">Opening the latest Agent draft…</p></StudioPage>;
  if (resolution.kind === "error") return <StudioPage><StudioResultState tone="danger" icon={<AlertTriangle size={22} aria-hidden />} title="Agent workspace unavailable" description={resolution.message} actions={<><StudioButton onClick={load}>Try again</StudioButton><StudioButton onClick={() => navigate("/agents")}>Back to Agents</StudioButton></>} /></StudioPage>;
  if (resolution.kind === "empty") return <StudioPage><StudioPageHeader icon={<FlaskConical aria-hidden="true" size={18} />} eyebrow="Agent workspace" title="No current draft" description="There is no active draft or published Agent version to open." actions={<StudioButton onClick={() => navigate("/agents")}>Back to Agents</StudioButton>} /></StudioPage>;
  if (resolution.kind === "choices") return <StudioPage>
    <StudioPageHeader icon={<FlaskConical aria-hidden="true" size={18} />} eyebrow="Agent workspace" title="Choose a draft" description="This Agent has more than one active draft. Choose which revision to continue; Studio will not guess from timestamps." actions={<StudioButton onClick={() => navigate("/agents")}>Back to Agents</StudioButton>} />
    <section className="agent-workspace-choices" aria-label="Active Agent drafts">
      {resolution.drafts.map(draft => <article key={draft.draftId}>
        <div><h2>{draft.presentationLabel?.trim() || `Draft ${draft.draftId}`}</h2><p>Saved revision {draft.revision}</p>{draft.updatedAt ? <StudioTimestamp value={draft.updatedAt} /> : null}</div>
        <StudioButton variant="primary" onClick={() => navigate(agentWorkspaceDraftPath(route.definitionId, draft.draftId))}>Open draft</StudioButton>
      </article>)}
      {resolution.fallbackVersionId ? <article className="agent-workspace-published-choice"><div><h2>Published version</h2><p>Open the published Agent without choosing an active draft.</p></div><StudioButton onClick={() => setResolution({ kind: "version", versionId: resolution.fallbackVersionId!, readOnly: false })}>Open published version</StudioButton></article> : null}
    </section>
  </StudioPage>;

  const definitionId = route.definitionId;
  const draftId = resolution.kind === "draft" ? resolution.draftId : undefined;
  const versionId = resolution.kind === "version" ? resolution.versionId : undefined;
  const readOnly = resolution.kind === "version" && resolution.readOnly;
  const allowStartEditing = resolution.kind === "version" && !resolution.readOnly;
  if (showVersions) return <AgentVersions context={context} definitionId={definitionId} navigate={navigate} onBack={() => setShowVersions(false)} />;

  return <div className="agent-workspace">
    <ActivityDefinitionDraftEditor
      context={context}
      definitionId={definitionId}
      draftId={draftId}
      versionId={versionId}
      readOnly={readOnly}
      allowStartEditing={allowStartEditing}
      activityEditors={activityEditors}
      recoverySettings={recoverySettings}
      identity={identity}
      workspaceContribution={workspaceContribution}
      onNavigationGuardChange={blocked => { navigationBlocked.current = blocked; }}
      onBack={force => { if (navigationBlocked.current && !force) return; navigationBlocked.current = false; navigate("/agents"); }}
      onOpenDraft={(nextDefinitionId, nextDraftId) => navigate(agentWorkspaceDraftPath(nextDefinitionId, nextDraftId))}
      onOpenVersion={(nextDefinitionId, nextVersionId) => navigate(agentWorkspaceVersionPath(nextDefinitionId, nextVersionId))}
      onOpenStudioPath={navigate}
      onManage={section => { if (section === "versions") setShowVersions(true); }}
    />
  </div>;
}

function AgentVersions({ context, definitionId, navigate, onBack }: { context: StudioEndpointContext; definitionId: string; navigate(path: string): void; onBack(): void }) {
  const [state, setState] = useState<{ kind: "loading" } | { kind: "error" } | { kind: "ready"; versions: AgentVersionChoice[] }>({ kind: "loading" });
  useEffect(() => {
    const abort = new AbortController();
    setState({ kind: "loading" });
    void listAgentVersions(context, definitionId, abort.signal).then(versions => {
      if (!abort.signal.aborted) setState({ kind: "ready", versions });
    }).catch(() => { if (!abort.signal.aborted) setState({ kind: "error" }); });
    return () => abort.abort();
  }, [context, definitionId]);

  return <StudioPage>
    <StudioPageHeader icon={<FlaskConical aria-hidden="true" size={18} />} eyebrow="Agent workspace" title="Versions" description="Open a published Agent revision read-only." actions={<StudioButton onClick={onBack}><ArrowLeft aria-hidden="true" size={15} /> Instructions</StudioButton>} />
    {state.kind === "loading" ? <p role="status">Loading Agent versions…</p> : null}
    {state.kind === "error" ? <StudioResultState tone="danger" title="Versions unavailable" description="Studio could not confirm the published Agent versions." /> : null}
    {state.kind === "ready" && state.versions.length === 0 ? <StudioResultState title="No published versions" description="Published Agent revisions will appear here." /> : null}
    {state.kind === "ready" && state.versions.length > 0 ? <section className="agent-workspace-versions" aria-label="Published Agent versions">{state.versions.map(version => <article key={version.versionId}>
      <div><h2>Version {version.version || version.versionId}</h2><p>{version.lifecycle || "Published"}</p><StudioTimestamp value={version.publishedAt} /></div>
      <StudioButton onClick={() => navigate(agentWorkspaceVersionPath(definitionId, version.versionId))}>Open read-only</StudioButton>
    </article>)}</section> : null}
  </StudioPage>;
}