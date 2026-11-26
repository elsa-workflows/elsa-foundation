import type {
  StudioGraphAuthoringResourceKind,
  StudioWorkflowDesignerPanelContribution
} from "@elsa-workflows/studio-sdk";

/**
 * Host-level graph contributions are opt-in for Activity Definition graphs. Contributions authored
 * before resource scoping existed remain workflow-only so they cannot receive an incompatible
 * Activity Definition context accidentally.
 */
export function filterGraphAuthoringContributions<TContext>(
  contributions: StudioWorkflowDesignerPanelContribution<TContext>[],
  resourceKind: StudioGraphAuthoringResourceKind
) {
  return contributions.filter(contribution => {
    const supported = contribution.supportedResourceKinds ?? ["workflow-definition"];
    return supported.includes(resourceKind);
  });
}
