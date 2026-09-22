import type { StudioExpressionDocument } from "@elsa-workflows/studio-sdk";

export interface CreateActivityExpressionDocumentOptions {
  draftId?: string;
  activityId: string;
  propertyKey: string;
  expressionType: string;
  source: string;
  sourceVersion: number;
  contextVersion?: string;
}

export function createActivityExpressionDocument({
  draftId = "transient",
  activityId,
  propertyKey,
  expressionType,
  source,
  sourceVersion,
  contextVersion
}: CreateActivityExpressionDocumentOptions): StudioExpressionDocument {
  const identity = [draftId, activityId, propertyKey, expressionType]
    .map(part => encodeURIComponent(part))
    .join("/");
  const uri = `elsa://workflow-expressions/${identity}`;

  return {
    id: uri,
    uri,
    draftId,
    activityId,
    propertyKey,
    expressionType,
    source,
    sourceVersion,
    contextVersion
  };
}
