export type ActivityUpgradeRootKind =
  | "ActivityDraft"
  | "ActivityVersion"
  | "WorkflowDraft"
  | "WorkflowVersion";

export interface ActivityUpgradeRoot {
  kind: ActivityUpgradeRootKind;
  id: string;
}

export interface ActivityVersionReplacement {
  fromVersionId: string;
  toVersionId: string;
}

export interface CreateActivityUpgradePlanRequest {
  replacements: ActivityVersionReplacement[];
  roots: ActivityUpgradeRoot[];
  includeTransitiveDependents: boolean;
  createDraftsForPublishedDependents: boolean;
}

export interface ActivityUpgradeVersionIdentity {
  definitionId: string;
  versionId: string;
  version: string;
  templateHash: string;
}

export interface ActivityUpgradeReplacementView {
  from: ActivityUpgradeVersionIdentity;
  to: ActivityUpgradeVersionIdentity;
}

export interface ActivityUpgradeDiagnosticSubject {
  kind: string;
  id: string;
  definitionId?: string | null;
  versionId?: string | null;
  revision?: number | null;
}

export interface ActivityUpgradeDiagnosticLocation {
  providerKey?: string | null;
  jsonPointer?: string | null;
  referenceKey?: string | null;
  nodeOrigin?: Array<{ kind: string; id: string }> | null;
  dependencyPath?: Array<{
    definitionId: string;
    versionId: string;
    version: string;
    templateHash: string;
  }> | null;
}

export interface ActivityUpgradeDiagnostic {
  code: string;
  severity: string | number;
  message: string;
  subject: ActivityUpgradeDiagnosticSubject;
  location?: ActivityUpgradeDiagnosticLocation | null;
  remediation?: string | null;
  metadata?: Record<string, string> | null;
}

export interface ActivityUpgradeReference {
  kind: string;
  definitionId: string;
  versionId?: string | null;
  version?: string | null;
  draftId?: string | null;
  revision?: number | null;
  templateHash?: string | null;
  lifecycle?: string | number | null;
}

export interface ActivityUpgradeExpectedSnapshot {
  kind: string;
  id: string;
  revision?: number | null;
  definitionId: string;
  headVersionId?: string | null;
}

export interface ActivityUpgradeTarget {
  kind: string;
  definitionId: string;
  draftId?: string | null;
  revision?: number | null;
  sourceVersionId?: string | null;
}

export interface ActivityUpgradeOccurrenceReplacement {
  occurrenceId: string;
  fromVersionId: string;
  toVersionId: string;
}

export interface ActivityUpgradeVersionDiff {
  compatibility: string | number;
  requiredBump: string | number;
  behaviorChanged: boolean;
  summary?: {
    breaking: number;
    additive: number;
    nonBehavioral: number;
    warnings: number;
  } | null;
  diagnostics?: ActivityUpgradeDiagnostic[];
}

export interface ActivityUpgradeStep {
  stepId: string;
  order: number;
  target: ActivityUpgradeTarget;
  action: string | number;
  dependsOnStepIds: string[];
  replacements: ActivityUpgradeOccurrenceReplacement[];
  expectedRevision?: number | null;
  expectedDefinitionHeadVersionId?: string | null;
  resultingDiff?: ActivityUpgradeVersionDiff | null;
  diagnostics: ActivityUpgradeDiagnostic[];
  stageId?: string | null;
}

export interface ActivityUpgradeStage {
  stageId: string;
  order: number;
  status: string | number;
  stepIds: string[];
  dependsOnStageIds: string[];
}

export interface ActivityUpgradePlanBinding {
  roots: ActivityUpgradeRoot[];
  includeTransitiveDependents: boolean;
  createDraftsForPublishedDependents: boolean;
  accessProfileFingerprint: string;
  selectedClosure: ActivityUpgradeReference[];
}

export interface ActivityUpgradePlan {
  planId: string;
  createdAt: string;
  expiresAt: string;
  status: string | number;
  replacements: ActivityUpgradeReplacementView[];
  expectedSnapshots: ActivityUpgradeExpectedSnapshot[];
  steps: ActivityUpgradeStep[];
  stages: ActivityUpgradeStage[];
  binding?: ActivityUpgradePlanBinding | null;
  diagnostics: ActivityUpgradeDiagnostic[];
  predecessorPlanId?: string | null;
  successorPlanId?: string | null;
}

export interface ActivityUpgradeAppliedDraft {
  kind: string;
  draftId: string;
  definitionId: string;
  revision: number;
  created: boolean;
  sourceVersionId?: string | null;
}

export interface ActivityUpgradePublicationHandoff {
  draftKind: string;
  draftId: string;
  definitionId: string;
  revision: number;
  sourceVersionId: string;
  requiredByStageIds: string[];
}

export interface ActivityUpgradeApplyResult {
  planId: string;
  status: string | number;
  appliedAt: string;
  drafts: ActivityUpgradeAppliedDraft[];
  diagnostics: ActivityUpgradeDiagnostic[];
  receiptId?: string | null;
  stageId?: string | null;
  awaitingPublications: ActivityUpgradePublicationHandoff[];
}

export interface ActivityUpgradeApplyReceipt {
  receiptId: string;
  planId: string;
  stageId: string;
  status: string | number;
  createdAt: string;
  updatedAt: string;
  result?: ActivityUpgradeApplyResult | null;
  diagnostics: ActivityUpgradeDiagnostic[];
  rejectionStatusCode?: number | null;
  rejectionCode?: string | null;
}

export interface ActivityUpgradePublishedDraftSelection {
  draftId: string;
  publishedVersionId: string;
}

export interface ActivityUpgradePublicationReceipt {
  receiptId: string;
  publishedDrafts: ActivityUpgradePublishedDraftSelection[];
}
