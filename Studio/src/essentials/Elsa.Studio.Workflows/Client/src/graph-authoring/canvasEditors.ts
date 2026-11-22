import type { StudioGraphAuthoringResourceKind, StudioWorkflowDesignerCanvasContribution } from "@elsa-workflows/studio-sdk";

/** Filter an already governed registry list without loading any canvas or assuming a protocol. */
export function matchingCanvasEditors(contributions: readonly StudioWorkflowDesignerCanvasContribution[],
  resourceKind: StudioGraphAuthoringResourceKind, structure: { kind: string; schemaVersion: string } | null | undefined,
  protocol: { id: string; version: number }): StudioWorkflowDesignerCanvasContribution[] {
  return structure ? contributions.filter(editor => editor.protocol.id === protocol.id && editor.protocol.version === protocol.version
    && editor.supportedResourceKinds.includes(resourceKind)
    && editor.supportedStructures.some(supported => supported.kind === structure.kind && supported.schemaVersions.includes(structure.schemaVersion))) : [];
}
