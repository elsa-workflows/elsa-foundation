import type { StudioActivityDiagnostic } from "@elsa-workflows/studio-sdk";

export type ActivityContentAuthorityKind = "Design" | "ProviderSource" | string;

export function isActivityDefinitionEnumValue(value: string | null | undefined, expected: string) {
  return value?.toLowerCase() === expected.toLowerCase();
}

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

export interface RecommendedActivityDefinition {
  definitionId: string;
  activityTypeKey: string;
  tenantId?: string | null;
  category: string;
  displayName: string;
  description?: string | null;
  versionId: string;
  version: string;
  isAvailable: boolean;
  unavailableReason?: string | null;
}

export interface RecommendedActivityDefinitionPage {
  items: RecommendedActivityDefinition[];
  nextOffset?: number | null;
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
  contract?: ActivityContract;
}

export type ActivityDefinitionVersionLifecycle = "Active" | "Retired" | "Revoked";
export type ActivityDefinitionVersionLifecycleAction = "recommend" | "retire" | "restore" | "revoke";

export interface SetActivityDefinitionRecommendationRequest {
  expectedDefinitionHeadVersionId: string | null;
  expectedRecommendedVersionId: string | null;
  recommendedVersionId: string | null;
  expectedRecommendedVersionLifecycle: ActivityDefinitionVersionLifecycle | null;
  reason: string;
}

export interface ActivityRecommendationDecision {
  expectedDefinitionHeadVersionId: string | null;
  expectedRecommendedVersionId: string | null;
  disposition: "Clear" | "Replace";
  replacementVersionId?: string | null;
  expectedReplacementLifecycle?: ActivityDefinitionVersionLifecycle | null;
}

export interface ChangeActivityDefinitionVersionLifecycleRequest {
  expectedLifecycle: ActivityDefinitionVersionLifecycle;
  reason: string;
  recommendationDecision?: ActivityRecommendationDecision | null;
}

export interface ActivityDefinitionRecommendationMutationView {
  definitionId: string;
  headVersionId?: string | null;
  recommendedVersionId?: string | null;
  changedAt: string;
  reason: string;
}

export interface ActivityDefinitionVersionLifecycleMutationView {
  versionId: string;
  lifecycle: ActivityDefinitionVersionLifecycle;
  reason: string;
  changedAt: string;
}

export interface ActivityDefinitionDependencyReference {
  kind: string;
  definitionId: string;
  versionId?: string | null;
  version?: string | null;
  draftId?: string | null;
  revision?: number | null;
  templateHash?: string | null;
  tenantId?: string | null;
  lifecycle?: string | null;
}

export interface ActivityDefinitionDependencyItem {
  relationshipId: string;
  owner: ActivityDefinitionDependencyReference;
  dependency: ActivityDefinitionDependencyReference;
  occurrence: {
    occurrenceId: string;
    nodeOrigin: unknown[];
  };
  isDirect: boolean;
  depth: number;
  path: ActivityDefinitionDependencyReference[];
}

export interface ActivityDefinitionUsageEvidence {
  root: ActivityDefinitionDependencyReference;
  query: {
    direction: string;
    transitive: boolean;
    include: string[];
  };
  consistency: {
    kind: string;
    isAuthoritative: boolean;
    asOfSequence?: number | null;
    asOf?: string | null;
    rebuildId?: string | null;
  };
  items: ActivityDefinitionDependencyItem[];
  nextCursor?: string | null;
}

export interface ActivityDefinitionRevocationEvidence {
  directDependencies: ActivityDefinitionUsageEvidence;
  inboundUsage: ActivityDefinitionUsageEvidence;
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

export interface ActivityTypeKeyRules {
  serverGenerated: boolean;
  allowsPreCreationOverride: boolean;
  immutable: boolean;
  prefix: string;
  pattern: string;
  maximumLength: number;
  collisionScope: string;
}

export interface ActivityTypeReference {
  alias: string;
  collectionKind: string;
  [key: string]: unknown;
}

export interface ActivityInputDefault {
  syntax: string;
  value: unknown;
  [key: string]: unknown;
}

interface ActivityContractMemberPresentation {
  referenceKey: string;
  name: string;
  displayName?: string | null;
  description?: string | null;
  category?: string | null;
  order?: number;
  uiHint?: string | null;
  uiSpecifications?: unknown | null;
  [key: string]: unknown;
}

export interface ActivityInputContract extends ActivityContractMemberPresentation {
  type: ActivityTypeReference;
  isRequired: boolean;
  isNullable: boolean;
  default: ActivityInputDefault | null;
  storageDriverKey: string;
  durability: string;
}

export interface ActivityOutputContract extends ActivityContractMemberPresentation {
  type: ActivityTypeReference;
  isRequired: boolean;
  isNullable: boolean;
  storageDriverKey: string;
  durability: string;
}

export interface ActivityOutcomeContract {
  referenceKey: string;
  name: string;
  isEmitted: boolean;
  description?: string | null;
  [key: string]: unknown;
}

export interface ActivityContract {
  contractSchemaVersion: string;
  inputs: ActivityInputContract[];
  outputs: ActivityOutputContract[];
  outcomes: ActivityOutcomeContract[];
  [key: string]: unknown;
}

export interface ActivityProviderAuthoringCapability {
  providerKey: string;
  displayName: string;
  manifestSchemas: Array<{
    schemaVersion: string;
    isAuthorable: boolean;
    migratableFromSchemaVersions: string[];
  }>;
  requiredOutcomes: ActivityOutcomeContract[];
}

export interface ActivityAuthoringCapabilities {
  contractSchemaVersions: string[];
  activityTypeKeyRules: ActivityTypeKeyRules;
  providers: ActivityProviderAuthoringCapability[];
  types: ActivityContractTypeCapability[];
  storageDriverKeys: string[];
  snapshotFingerprint: string;
  [key: string]: unknown;
}

export interface ActivityContractTypeCapability {
  alias: string;
  displayName: string;
  category: string;
  defaultEditor: string;
  supportedCollectionKinds: string[];
  supportsNull: boolean;
  supportsDurability: boolean;
  compatibleStorageDriverKeys: string[];
  [key: string]: unknown;
}

export interface ActivityProviderManifest {
  providerKey: string;
  schemaVersion: string;
  payload: unknown;
}

export interface ActivityDefinitionLayoutRecord {
  nodeId: string;
  data: unknown;
}

export interface ActivityDefinitionDraftView {
  draftId: string;
  definitionId: string;
  tenantId?: string | null;
  revision: number;
  sourceVersionId?: string | null;
  status: string;
  contract: ActivityContract;
  provider: {
    providerKey: string;
    schemaVersion: string;
    manifestFingerprint: string;
    payload?: unknown;
  };
  layout: ActivityDefinitionLayoutRecord[];
  validation?: ActivityDraftValidationView | null;
  createdAt: string;
  updatedAt: string;
  presentationLabel?: string | null;
  [key: string]: unknown;
}

export interface CreateActivityDefinitionRequest {
  category: string;
  displayName: string;
  description: string | null;
  activityTypeKey?: string;
  provider: ActivityProviderManifest;
  contract: ActivityContract;
  layout: ActivityDefinitionLayoutRecord[];
}

export interface CreateActivityDefinitionResponse {
  definition: ActivityDefinitionIdentity;
  draft: ActivityDefinitionDraftManagementView["draft"];
}

export interface CreateActivityDefinitionDraftRequest {
  sourceVersionId: string | null;
  presentationLabel?: string | null;
  provider?: ActivityProviderManifest;
  contract?: ActivityContract;
  layout?: ActivityDefinitionLayoutRecord[];
}

export interface MigrateActivityDefinitionDraftRequest {
  expectedRevision: number;
  targetProviderKey: string;
  targetSchemaVersion: string;
}

export interface ReplaceActivityDefinitionDraftRequest {
  expectedRevision: number;
  contract: ActivityContract;
  provider: ActivityProviderManifest;
  layout: ActivityDefinitionLayoutRecord[];
  presentationLabel?: string | null;
}

export interface ActivityDraftValidationView {
  draftId: string;
  revision: number;
  isValid: boolean;
  validatedAt: string;
  diagnostics: StudioActivityDiagnostic[];
}

export interface ActivityContractProposalChange {
  changeId: string;
  operation: "Add" | "Replace" | "Remove" | string;
  memberKind: "Input" | "Output" | "Outcome" | string;
  referenceKey: string;
  input?: ActivityInputContract | null;
  output?: ActivityOutputContract | null;
  outcome?: ActivityOutcomeContract | null;
  [key: string]: unknown;
}

export interface ActivityContractProposalView {
  draftId: string;
  revision: number;
  providerKey: string;
  providerSchemaVersion: string;
  manifestFingerprint: string;
  proposalFingerprint: string;
  changes: ActivityContractProposalChange[];
  diagnostics: StudioActivityDiagnostic[];
  [key: string]: unknown;
}

export interface ProposeActivityContractRequest {
  expectedRevision: number;
  expectedProviderKey: string;
  expectedProviderSchemaVersion: string;
  expectedManifestFingerprint: string;
}

export interface ApplyActivityContractProposalRequest extends ProposeActivityContractRequest {
  proposalFingerprint: string;
  selectedChangeIds: string[];
}

export interface ActivityDefinitionVersionView {
  definition: ActivityDefinitionIdentity;
  versionId: string;
  version: string;
  sourceDraftId?: string | null;
  sourceVersionId?: string | null;
  contract: ActivityContract;
  provider: {
    providerKey: string;
    schemaVersion: string;
    manifestFingerprint: string;
    payload?: unknown;
    [key: string]: unknown;
  };
  lifecycle: string;
  publishedAt: string;
  [key: string]: unknown;
}

export interface PreviewActivityDefinitionForkRequest {
  idempotencyKey: string;
  sourceVersionId: string;
  category: string;
  displayName: string;
  description: string | null;
  targetProviderKey: string;
  targetProviderSchemaVersion: string;
}

export interface ActivityDefinitionForkPreview {
  candidateId: string;
  requestFingerprint: string;
  status: string;
  accessBinding: { fingerprint: string };
  source: {
    definitionId: string;
    versionId: string;
    version: string;
    lifecycle: string;
    providerKey: string;
    providerSchemaVersion: string;
    providerFingerprint: string;
  };
  presentation: {
    category: string;
    displayName: string;
    description?: string | null;
  };
  target: {
    definitionId: string;
    activityTypeKey: string;
    draftId: string;
    providerKey: string;
    providerSchemaVersion: string;
    manifestFingerprint: string;
    contract: ActivityContract;
  };
  providerMigration: {
    sourceProviderKey: string;
    sourceProviderSchemaVersion: string;
    targetProviderKey: string;
    targetProviderSchemaVersion: string;
    targetManifestFingerprint: string;
    diagnostics: StudioActivityDiagnostic[];
  };
  contractComparison: {
    sourceFingerprint: string;
    targetFingerprint: string;
    isCompatible: boolean;
    changes: Array<{ kind: string; referenceKey?: string | null; detail: string }>;
  };
  createdAt: string;
  expiresAt: string;
}

export interface ActivityDefinitionForkReceipt {
  idempotencyKey: string;
  candidateId: string;
  requestFingerprint: string;
  outcome: string;
  accessBinding: { fingerprint: string };
  definition: ActivityDefinitionIdentity;
  draft: ActivityDefinitionDraftManagementView["draft"];
  appliedAt: string;
}
