import { collectActivityNodeIds, findActivityNode, updateActivity, type ActivityCatalogLookup } from "../workflowAdapter";
import { collectBpmnElementIds } from "../bpmn/bpmnAdapter";
import type { BpmnDocumentSurfaceHost } from "../bpmn/bpmnDocumentSurfacePort";
import type { AuthoringCheckpointCapture, AuthoringCheckpointCoordinator } from "../graph-authoring/authoringCheckpoint";
import type { WorkflowDraft } from "../workflowTypes";
import { forgetRemovedNodes, type WorkflowDraftRecipe } from "./workflowDocument";

const equal = (left: unknown, right: unknown) => JSON.stringify(left) === JSON.stringify(right);

/** Actual WorkflowEditor adapter: one ordinary draft/history mutation and no second graph authority. */
export function createWorkflowBpmnDocumentHost(options: {
  id: string;
  ownerId: string;
  initial: AuthoringCheckpointCapture<WorkflowDraft>;
  coordinator: AuthoringCheckpointCoordinator<WorkflowDraft>;
  readLive(): AuthoringCheckpointCapture<WorkflowDraft>;
  canEdit(): boolean;
  catalog(): ActivityCatalogLookup;
  /** The caller flushes this ordinary reducer dispatch before returning. */
  editDraft(recipe: WorkflowDraftRecipe): void;
  transitionScope(action: () => undefined): Promise<void>;
  notifyWorkChanged(): void;
}): BpmnDocumentSurfaceHost {
  const identity = options.initial.token;
  const readCanonical = () => {
    const current = options.readLive(), root = current.snapshot.state.rootActivity;
    const owner = root && findActivityNode(root, options.ownerId, options.catalog());
    if (!owner) throw new Error("The BPMN document owner is no longer present in this workflow.");
    return { owner, layout: current.snapshot.layout, token: current.token };
  };
  const canEdit = () => {
    try {
      const current = readCanonical();
      return options.canEdit() && current.token.authorityId === identity.authorityId && current.token.resourceId === identity.resourceId
        && current.token.scopeId === identity.scopeId;
    } catch { return false; }
  };
  return { id: options.id, authorityKey: JSON.stringify([identity.authorityId, identity.resourceId]), coordinator: options.coordinator,
    readCanonical, canEdit, transitionScope: options.transitionScope, notifyWorkChanged: options.notifyWorkChanged,
    admit(expected, candidate, plan, context) {
      if (!canEdit() || !equal(readCanonical(), expected) || candidate.nodeId !== options.ownerId) return false;
      const preparation = context.preparation;
      if (preparation && (!equal(expected.owner, preparation.canonicalBaseOwner) || !equal(expected.layout, preparation.canonicalBaseLayout))) return false;
      if (preparation && equal(candidate, preparation.preparedOwner) || equal(candidate, expected.owner)) return true;
      let accepted = false;
      options.editDraft(({ draft: current }) => {
        if (!current?.state.rootActivity || !canEdit() || !equal(readCanonical(), expected) || !equal(current.layout, expected.layout)) return current;
        const owner = findActivityNode(current.state.rootActivity, options.ownerId, options.catalog());
        if (!equal(owner, expected.owner)) return current;
        const forgotten = forgetRemovedNodes(current, plan.removedNodeIds);
        let layout = forgotten.layout;
        if (preparation) {
          const before = collectBpmnElementIds(expected.owner, options.catalog()), remaining = collectBpmnElementIds(candidate, options.catalog());
          const native = collectActivityNodeIds(candidate, options.catalog());
          layout = layout.filter(record => !before.has(record.nodeId) || remaining.has(record.nodeId) || native.has(record.nodeId));
        }
        accepted = true;
        return { ...current, ...forgotten, layout, state: { ...current.state,
          rootActivity: updateActivity(current.state.rootActivity, options.ownerId, () => candidate, options.catalog()) } };
      });
      const after = readCanonical();
      return accepted && canEdit() && equal(after.owner, candidate) && after.token.revision > expected.token.revision;
    }
  };
}
