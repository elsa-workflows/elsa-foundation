import type { ActivityNode, DesignMetadataRecord } from "../workflowTypes";

export type GraphAuthoringResourceKind = "workflow-definition" | "activity-definition-graph";

/**
 * Resource-neutral boundary used by graph authoring. It deliberately owns no persistence,
 * lifecycle, validation, or runtime behavior; hosts translate their exact document envelope.
 */
export interface GraphDocumentAdapter<TDocument> {
  readonly resourceKind: GraphAuthoringResourceKind;
  readRoot(document: TDocument): ActivityNode;
  readLayout(document: TDocument): DesignMetadataRecord[];
  replaceRoot(document: TDocument, rootActivity: ActivityNode): TDocument;
  replaceLayout(document: TDocument, layout: DesignMetadataRecord[]): TDocument;
  replaceGraph(document: TDocument, rootActivity: ActivityNode, layout: DesignMetadataRecord[]): TDocument;
}
