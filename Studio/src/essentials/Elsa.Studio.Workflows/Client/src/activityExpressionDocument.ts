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

/** Binding-only changes invalidate tooling even when the source string is unchanged. */
export function advanceActivityExpressionRevision(previous: { key: string; version: number } | undefined, source: string, ownerValue: unknown) {
  const key = JSON.stringify([source, ownerValue], (_name, value) => value && typeof value === "object" && !Array.isArray(value)
    ? Object.fromEntries(Object.entries(value).sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0)) : value);
  return previous?.key === key ? previous : { key, version: (previous?.version ?? -1) + 1 };
}
