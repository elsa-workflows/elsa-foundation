import type { Publication, PublicationPreflight, PublicationSlot, PublicationSlotOwner } from "../../api/publishing";
import type { WorkflowActivationSlot } from "../../api/runtime";
import type { WorkflowDefinitionVersionDetails } from "../../workflowTypes";

// Publication slot fixtures in the shapes the backend serves since elsa-foundation#1498: Runtime owns
// the (unjoined) activation slot view, Publishing serves one publication record per publication id.

export function activationSlot(slotName: string, overrides: Partial<WorkflowActivationSlot> = {}): WorkflowActivationSlot {
  return {
    slotId: `activation-slot:definition-1:${slotName}`,
    definitionId: "definition-1",
    slotName,
    activeActivationId: null,
    sourceKind: null,
    sourceId: null,
    revision: 1,
    updatedAt: "2026-09-01T00:00:00Z",
    ...overrides
  };
}

/**
 * A slot that was activated and later deactivated. Both activation authorities clear the source on
 * deactivation (`ActiveActivationId = null, Source = null`) and bump the revision, so a deactivated
 * slot can never be told apart from "never activated" by anything other than its revision.
 */
export function deactivatedActivationSlot(slotName: string, overrides: Partial<WorkflowActivationSlot> = {}): WorkflowActivationSlot {
  return activationSlot(slotName, { revision: 2, ...overrides });
}

/** A slot occupied by an imported artifact: it has an active activation but no publication record. */
export function importedActivationSlot(): WorkflowActivationSlot {
  return activationSlot("imported", {
    activeActivationId: "activation-imported",
    sourceKind: "artifact-reconciliation",
    sourceId: "orders-bundle"
  });
}

/** A fetched design version, as `slotVersions` serves it for a publication's baseline. */
export function versionDetails(id: string, overrides: Partial<WorkflowDefinitionVersionDetails> = {}): WorkflowDefinitionVersionDetails {
  const { state, ...rest } = overrides;
  return {
    id,
    version: "1.0.0",
    definition: { id: "definition-1", name: "Orders", createdAt: "2026-07-01T00:00:00Z", lastModifiedAt: "2026-07-01T00:00:00Z" },
    layout: [],
    ...rest,
    state: { rootActivity: { nodeId: "root", activityVersionId: "activity-root", inputs: [], outputs: [] }, ...state }
  };
}

export function publicationRecord(slotName: string, overrides: Partial<Publication> = {}): Publication {
  return {
    publicationId: `publication-${slotName}`,
    definitionId: "definition-1",
    versionId: `version-${slotName}`,
    artifactId: `artifact-${slotName}`,
    slotName,
    sourceReferenceId: `reference-${slotName}`,
    status: "active",
    createdAt: "2026-09-01T00:00:00Z",
    activatedAt: "2026-09-01T00:00:01Z",
    retiredAt: null,
    ...overrides
  };
}

/** A Runtime slot occupied by a publication, as the review sees it once joined to its record. */
export function publishedSlot(slotName: string, overrides: Partial<Publication> = {}): PublicationSlot {
  const publication = publicationRecord(slotName, overrides);
  return {
    ...activationSlot(slotName, { activeActivationId: publication.publicationId, sourceKind: "publishing" }),
    publication
  };
}

/** A slot without a publication record: empty, or occupied by another activation source. */
export function withoutPublication(slot: WorkflowActivationSlot): PublicationSlot {
  return { ...slot, publication: null };
}

/**
 * An authoritative snapshot/version preflight, as elsa-foundation serves it for a clean, activatable target.
 *
 * `targetSlotOwner` defaults to `null` (the field present, unowned). To model a host predating
 * elsa-foundation#1659 — where the field is absent from the payload entirely, not merely `null` — pass
 * `targetSlotOwner: undefined` explicitly; this deletes the key rather than leaving it `undefined` in place.
 */
export function publicationPreflight(overrides: Partial<PublicationPreflight> = {}): PublicationPreflight {
  const preflight: PublicationPreflight = {
    preflightToken: "preflight-token-1",
    candidateHash: "candidate-hash-1",
    definitionId: "definition-1",
    versionId: null,
    slotName: "default",
    resolvedAction: "replace",
    policySource: "host",
    canActivate: true,
    claims: [],
    triggers: [],
    conflicts: [],
    targetSlotOwner: null,
    ...overrides
  };
  if ("targetSlotOwner" in overrides && overrides.targetSlotOwner === undefined) delete preflight.targetSlotOwner;
  return preflight;
}

/**
 * A preflight's `targetSlotOwner`, the shape elsa-foundation#1659 added: the resolved target slot's
 * active activation, owned by a non-publishing source Studio cannot take over from the review.
 */
export function foreignSlotOwner(overrides: Partial<PublicationSlotOwner> = {}): PublicationSlotOwner {
  return { sourceKind: "artifact-reconciliation", sourceId: "mounted-artifacts", ...overrides };
}
