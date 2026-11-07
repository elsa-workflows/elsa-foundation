import { createAuthoringCheckpoint, AuthoringCheckpointError, type AuthoringCheckpointCapture } from "../graph-authoring/authoringCheckpoint";
import type { WorkflowDraft } from "../workflowTypes";
import type { WorkflowSaveCheckpoint } from "./useWorkflowPersistence";

/** The resource host owns canonical reads and receipts; renderers receive only their registration. */
export function createWorkflowAuthoringResource(options: {
  readLive(): AuthoringCheckpointCapture<WorkflowDraft>;
  canEdit(): boolean;
}) {
  const initial = options.readLive().token;
  const checkpoint = createAuthoringCheckpoint({ readLive: options.readLive });
  const assertAuthority = () => {
    const live = options.readLive();
    if (!options.canEdit() || live.token.resourceId !== initial.resourceId || live.token.authorityId !== initial.authorityId)
      throw new AuthoringCheckpointError("stale-token", "The workflow authoring resource or authorization changed. The pending canvas work has been retained.");
    return live;
  };
  const prepare: WorkflowSaveCheckpoint["prepare"] = async request => {
    const before = assertAuthority();
    if (request.snapshot.id !== before.snapshot.id || request.snapshot.definitionId !== before.snapshot.definitionId)
      throw new AuthoringCheckpointError("stale-token", "The save belongs to another workflow draft.");
    const capture = await checkpoint.capture(request.intent);
    assertAuthority();
    // A reviewed publication or derived version-change plan must never silently acquire new content.
    if (request.capture === "snapshot" && capture.token.revision !== before.token.revision)
      throw new AuthoringCheckpointError("stale-token", "Canvas edits changed the workflow while preparing this snapshot. Review the updated draft and try again.");
    return {
      draft: request.capture === "current" ? capture.snapshot : request.snapshot,
      lineage: request.capture === "current" ? capture.lineage : request.lineage,
      // Source edits and scope navigation may continue while this snapshot waits for HTTP. A new
      // resource or authorization may not inherit permission to send the old request.
      assertCurrent: () => { assertAuthority(); }
    };
  };
  return { checkpoint, prepare, assertAuthority };
}
