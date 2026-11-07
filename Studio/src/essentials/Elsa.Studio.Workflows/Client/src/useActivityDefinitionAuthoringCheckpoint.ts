import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { ActivityDefinitionDraftView } from "./activityDefinitionTypes";
import { AuthoringCheckpointError, createAuthoringCheckpoint, type AuthoringCheckpointCapture, type AuthoringCheckpointIntent } from "./graph-authoring/authoringCheckpoint";
import type { ActivityGraphAuthoringHost } from "./graph-authoring/activityGraphAuthoringHost";
import { runtimeValueContextId } from "./workflow-editor/RuntimeValueEvidence";

/** Lives with the actual draft parent, above provider render failures and tab changes. */
export function useActivityDefinitionAuthoringCheckpoint(options: {
  context: StudioEndpointContext;
  readCanonical(): { snapshot: ActivityDefinitionDraftView; revision: number };
  invalidateScope(): void;
  /** Mounted resource lifetime, independently of a read-only draft or temporary authoring lock. */
  isAvailable(): boolean;
  canEdit(): boolean;
}) {
  const hostId = useId();
  const committed = useRef(options);
  useLayoutEffect(() => { committed.current = options; });
  const authorization = useRef({ authorized: true, generation: 0 });
  const [revision, refresh] = useState(0);
  const notifyWorkChanged = useCallback(() => refresh(value => value + 1), []);
  const resources = useRef(new Map<string, ReturnType<typeof createAuthoringCheckpoint<ActivityDefinitionDraftView>>>());
  useEffect(() => {
    const change = (authorized: boolean) => { authorization.current = { authorized, generation: authorization.current.generation + 1 }; notifyWorkChanged(); };
    const end = () => change(false), start = () => change(true);
    window.addEventListener(authSessionEndedEvent, end); window.addEventListener(authSessionStartedEvent, start);
    return () => { window.removeEventListener(authSessionEndedEvent, end); window.removeEventListener(authSessionStartedEvent, start); };
  }, [notifyWorkChanged]);
  const readCanonical = useCallback((): AuthoringCheckpointCapture<ActivityDefinitionDraftView> => {
    const current = committed.current, live = current.readCanonical(), draft = live.snapshot;
    return { snapshot: draft, lineage: [], token: {
      authorityId: JSON.stringify([hostId, runtimeValueContextId(current.context), authorization.current.generation]),
      resourceId: JSON.stringify([runtimeValueContextId(current.context), draft.tenantId, draft.definitionId, draft.draftId]),
      scopeId: "activity-definition", revision: live.revision
    } };
  }, [hostId]);
  const canEdit = useCallback(() => authorization.current.authorized && committed.current.canEdit(), []);
  const getResource = useCallback(() => {
    const token = readCanonical().token, key = JSON.stringify([token.resourceId, token.authorityId]);
    let resource = resources.current.get(key);
    if (!resource) { resource = createAuthoringCheckpoint({ readLive: readCanonical }); resources.current.set(key, resource); }
    return resource;
  }, [readCanonical]);
  const probe = useCallback(() => [...resources.current.values()].some(resource => resource.probe()), []);
  const activeResource = useCallback(() => {
    if (!canEdit()) throw new AuthoringCheckpointError("stale-token", "The Activity Definition authoring authority is unavailable. Canvas work is retained.");
    const resource = getResource();
    if ([...resources.current.values()].some(other => other !== resource && other.probe()))
      throw new AuthoringCheckpointError("pending-work", "Another authoring authority has retained Activity Definition canvas work.");
    return resource;
  }, [canEdit, getResource]);
  const assertCurrent = useCallback((expected: AuthoringCheckpointCapture<ActivityDefinitionDraftView>["token"]) => {
    const live = readCanonical().token;
    if (!canEdit() || live.authorityId !== expected.authorityId || live.resourceId !== expected.resourceId)
      throw new AuthoringCheckpointError("stale-token", "The Activity Definition authoring resource or authorization changed.");
  }, [canEdit, readCanonical]);
  const capture = useCallback(async (intent: AuthoringCheckpointIntent) => {
    const captured = await activeResource().capture(intent);
    assertCurrent(captured.token);
    return captured;
  }, [activeResource, assertCurrent]);
  const transition = useCallback<ActivityGraphAuthoringHost["transition"]>(async (intent, action) => {
    if (["scope-change", "view-change", "switch-editor"].includes(intent) && !canEdit() && !probe()
      && authorization.current.authorized && committed.current.isAvailable()) { action(); return { applied: true }; }
    return activeResource().transition(intent, () => { if (!canEdit()) throw new AuthoringCheckpointError("stale-token", "The authoring authority changed."); return action(); });
  }, [activeResource, canEdit, probe]);
  const readLive = useCallback<ActivityGraphAuthoringHost["readLive"]>(() => {
    const live = readCanonical(), draft = live.snapshot;
    return { token: live.token, lineage: [], snapshot: { payload: draft.provider.payload, layout: draft.layout, activityPresentation: draft.activityPresentation ?? [] } };
  }, [readCanonical]);
  const invalidateScope = useCallback(() => { committed.current.invalidateScope(); notifyWorkChanged(); }, [notifyWorkChanged]);
  const host = useMemo(() => ({ getResource, readLive, canEdit, probe, invalidateScope, notifyWorkChanged, transition } satisfies ActivityGraphAuthoringHost),
    [getResource, readLive, canEdit, probe, invalidateScope, notifyWorkChanged, transition]);
  return { capture, assertCurrent, revision, host };
}
