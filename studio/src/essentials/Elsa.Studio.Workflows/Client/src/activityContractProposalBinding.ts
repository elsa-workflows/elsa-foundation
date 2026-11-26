import type {
  ActivityContractProposalView,
  ActivityDefinitionDraftView,
  ProposeActivityContractRequest
} from "./activityDefinitionTypes";

export interface ActivityContractProposalBinding {
  draftId: string;
  revision: number;
  providerKey: string;
  providerSchemaVersion: string;
  manifestFingerprint: string;
}

export function activityContractProposalBinding(draft: ActivityDefinitionDraftView): ActivityContractProposalBinding {
  return {
    draftId: draft.draftId,
    revision: draft.revision,
    providerKey: draft.provider.providerKey,
    providerSchemaVersion: draft.provider.schemaVersion,
    manifestFingerprint: draft.provider.manifestFingerprint
  };
}

export function proposalRequest(binding: ActivityContractProposalBinding): ProposeActivityContractRequest {
  return {
    expectedRevision: binding.revision,
    expectedProviderKey: binding.providerKey,
    expectedProviderSchemaVersion: binding.providerSchemaVersion,
    expectedManifestFingerprint: binding.manifestFingerprint
  };
}

export function proposalMatchesBinding(
  proposal: ActivityContractProposalView,
  binding: ActivityContractProposalBinding
) {
  return proposal.draftId === binding.draftId &&
    proposal.revision === binding.revision &&
    proposal.providerKey === binding.providerKey &&
    proposal.providerSchemaVersion === binding.providerSchemaVersion &&
    proposal.manifestFingerprint === binding.manifestFingerprint;
}

export function proposalBindingKey(binding: ActivityContractProposalBinding) {
  return [
    binding.draftId,
    binding.revision,
    binding.providerKey,
    binding.providerSchemaVersion,
    binding.manifestFingerprint
  ].join("\u001f");
}
