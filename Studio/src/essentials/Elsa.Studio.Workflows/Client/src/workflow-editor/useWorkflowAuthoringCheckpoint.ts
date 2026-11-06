import { useCallback, useEffect, useId, useLayoutEffect, useRef, useState } from "react";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { AuthoringCheckpointError, type AuthoringCheckpointCapture, type AuthoringCheckpointIntent } from "../graph-authoring/authoringCheckpoint";
import type { WorkflowDraft } from "../workflowTypes";
import type { WorkflowDocumentApi } from "./workflowDocument";
import type { WorkflowSaveCheckpoint } from "./useWorkflowPersistence";
import { createWorkflowAuthoringResource } from "./workflowAuthoringResource";
import { runtimeValueContextId } from "./RuntimeValueEvidence";

/** Retains resource controllers independently of a fallible canvas view. */
export function useWorkflowAuthoringCheckpoint(options: {
  context: StudioEndpointContext;
  definitionId: string;
  document: WorkflowDocumentApi;
  canEdit(): boolean;
  lineage(): Iterable<string>;
}) {
  const hostId = useId();
  const committed = useRef(options);
  useLayoutEffect(() => { committed.current = options; });
  const authorization = useRef({ authorized: true, generation: 0 });
  const [workRevision, setWorkRevision] = useState(0);
  const notifyWorkChanged = useCallback(() => setWorkRevision(value => value + 1), []);
  useEffect(() => {
    const change = (authorized: boolean) => {
      authorization.current = { authorized, generation: authorization.current.generation + 1 };
      notifyWorkChanged();
    };
    const end = () => change(false);
    const start = () => change(true);
    window.addEventListener(authSessionEndedEvent, end);
    window.addEventListener(authSessionStartedEvent, start);
    return () => {
      window.removeEventListener(authSessionEndedEvent, end);
      window.removeEventListener(authSessionStartedEvent, start);
    };
  }, [notifyWorkChanged]);
  const resources = useRef(new Map<string, ReturnType<typeof createWorkflowAuthoringResource>>());
  const readLive = useCallback((): AuthoringCheckpointCapture<WorkflowDraft> => {
    const current = committed.current;
    const document = current.document.readCommitted();
    const draft = document.state.draft;
    if (!draft || draft.definitionId !== current.definitionId)
      throw new AuthoringCheckpointError("stale-token", "The workflow draft has not finished loading.");
    return {
      token: {
        authorityId: JSON.stringify([hostId, runtimeValueContextId(current.context), authorization.current.generation]),
        resourceId: JSON.stringify([runtimeValueContextId(current.context), current.definitionId, draft.id]),
        scopeId: JSON.stringify(document.state.frames),
        revision: document.revision
      },
      snapshot: draft,
      lineage: [...current.lineage()]
    };
  }, [hostId]);
  const getResource = useCallback(() => {
    const token = readLive().token;
    const key = JSON.stringify([token.resourceId, token.authorityId, token.scopeId]);
    let resource = resources.current.get(key);
    if (!resource) {
      resource = createWorkflowAuthoringResource({ readLive,
        canEdit: () => authorization.current.authorized && committed.current.canEdit() });
      resources.current.set(key, resource);
    }
    return resource;
  }, [readLive]);
  const probe = useCallback(() => [...resources.current.values()].some(resource => resource.checkpoint.probe()), []);
  const activeResource = useCallback(() => {
    const active = getResource();
    if ([...resources.current.values()].some(resource => resource !== active && resource.checkpoint.probe()))
      throw new AuthoringCheckpointError("pending-work", "Another authoring scope has retained canvas edits. Return to that scope to finish them before continuing.");
    active.assertAuthority();
    return active;
  }, [getResource]);
  const prepare = useCallback<WorkflowSaveCheckpoint["prepare"]>(async request => activeResource().prepare(request), [activeResource]);
  const capture = useCallback(async (intent: AuthoringCheckpointIntent) => {
    const active = activeResource();
    const result = await active.checkpoint.capture(intent);
    active.assertAuthority();
    return result;
  }, [activeResource]);
  const transition = useCallback(async (intent: AuthoringCheckpointIntent, action: (capture: AuthoringCheckpointCapture<WorkflowDraft>) => undefined) => {
    const active = activeResource();
    return active.checkpoint.transition(intent, live => { active.assertAuthority(); return action(live); });
  }, [activeResource]);
  return { getResource, readLive, capture, transition, notifyWorkChanged, saveCheckpoint: { prepare, probe, revision: workRevision } satisfies WorkflowSaveCheckpoint };
}
