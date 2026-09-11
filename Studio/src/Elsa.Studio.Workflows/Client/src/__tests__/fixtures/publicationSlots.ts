import type { Publication, PublicationSlot } from "../../api/publishing";
import type { WorkflowActivationSlot } from "../../api/runtime";

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
