export type ActivityContentAuthorityKind = "Design" | "ProviderSource" | string;

export interface ActivityContentAuthority {
  kind: ActivityContentAuthorityKind;
  authorityKey: string;
  sourceId?: string | null;
}

export interface ActivityDefinitionVersionReference {
  versionId: string;
  version: string;
  lifecycle: string;
  providerKey: string;
  providerSchemaVersion: string;
}

export interface ActivityDefinitionIdentity {
  definitionId: string;
  activityTypeKey: string;
  tenantId?: string | null;
  category: string;
  displayName: string;
  description?: string | null;
  contentAuthority: ActivityContentAuthority;
  forkedFrom?: {
    definitionId: string;
    versionId: string;
    version: string;
  } | null;
  headVersionId?: string | null;
  recommendedVersionId?: string | null;
}

export interface ActivityActionAvailability {
  action: string;
  allowed: boolean;
  unavailableCode?: string | null;
}

export interface ActivityDefinitionManagementView {
  definition: ActivityDefinitionIdentity;
  lifecycle: {
    draftCount: number;
    versionCount: number;
    head?: ActivityDefinitionVersionReference | null;
    recommendation?: ActivityDefinitionVersionReference | null;
  };
  actions: ActivityActionAvailability[];
  updatedAt: string;
}

export interface ActivityDefinitionDraftManagementView {
  draft: {
    draftId: string;
    definitionId: string;
    revision: number;
    sourceVersionId?: string | null;
    status: string;
    providerKey: string;
    providerSchemaVersion: string;
    updatedAt: string;
    presentationLabel?: string | null;
  };
  actions: ActivityActionAvailability[];
}

export interface ActivityDefinitionVersionManagementView {
  version: {
    versionId: string;
    definitionId: string;
    version: string;
    lifecycle: string;
    publishedAt: string;
  };
  providerKey: string;
  providerSchemaVersion: string;
  isRecommended: boolean;
  actions: ActivityActionAvailability[];
}

export interface ActivityManagementSnapshot {
  snapshotId: string;
  asOf: string;
}

export interface ActivityManagementPage<T> {
  items: T[];
  count: number;
  totalCount: number;
  hasMore: boolean;
  continuation?: string | null;
  snapshot: ActivityManagementSnapshot;
}

export interface ActivityDefinitionCollectionRequest {
  limit: number;
  cursor?: string | null;
  search?: string;
  authority?: "Design" | "ProviderSource" | "";
  providerKey?: string;
}

export interface ActivityDefinitionChildCollectionRequest {
  definitionId: string;
  limit: number;
  cursor?: string | null;
  search?: string;
}
