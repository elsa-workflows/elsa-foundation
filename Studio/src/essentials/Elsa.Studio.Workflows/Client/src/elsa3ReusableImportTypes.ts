export interface Elsa3ReusableImportUploadResult {
  collectionHandle: string;
  createdAt: string;
  expiresAt: string;
  sourceVersionCount: number;
  contentLength: number;
}

export interface Elsa3MigrationPathSegment {
  kind: string | number;
  identity: string;
  location?: string | null;
}

export interface Elsa3MigrationDiagnostic {
  severity: string | number;
  code: string;
  message: string;
  path?: string | null;
  guidance?: string | null;
  metadata: Record<string, string>;
  pathSegments: Elsa3MigrationPathSegment[];
  cycle: string[];
  isError?: boolean;
}

export interface Elsa3ReusableImportDependency {
  ownerSourceVersionId: string;
  targetSourceDefinitionId: string;
  targetSourceVersionId: string;
  targetActivityDefinitionVersionId: string;
  nodeId: string;
}

export interface Elsa3ReusableImportRewrite {
  ownerSourceVersionId: string;
  nodeId: string;
  targetSourceVersionId: string;
  targetActivityDefinitionVersionId: string;
}

export interface Elsa3ReusableImportDirectStart {
  nodeId: string;
  activityType: string;
}

export interface Elsa3ReusableImportItem {
  sourceDefinitionId: string;
  sourceVersionId: string;
  sourceVersion: number;
  sourceFingerprint: string;
  isReusable: boolean;
  workflowDefinitionId: string;
  workflowVersionId: string;
  activityDefinitionId?: string | null;
  activityDefinitionVersionId?: string | null;
  activityTypeKey?: string | null;
  dependencies: Elsa3ReusableImportDependency[];
  rewrites: Elsa3ReusableImportRewrite[];
  directStarts: Elsa3ReusableImportDirectStart[];
  diagnostics: Elsa3MigrationDiagnostic[];
  canApply?: boolean;
}

export interface Elsa3ReusableImportAnalysisPage {
  collectionHandle: string;
  planId: string;
  offset: number;
  limit: number;
  processed: number;
  total: number;
  processedDiagnostics: number;
  totalDiagnostics: number;
  isComplete: boolean;
  nextOffset?: number | null;
  items: Elsa3ReusableImportItem[];
  diagnostics: Elsa3MigrationDiagnostic[];
}

export interface Elsa3ReusableImportSelectionReadiness {
  collectionHandle: string;
  planId: string;
  requestedSourceVersionIds: string[];
  expandedSourceVersionIds: string[];
  addedDependencySourceVersionIds: string[];
  isReady: boolean;
  diagnostics: Elsa3MigrationDiagnostic[];
}

export interface Elsa3ReusableImportAccessScope {
  tenantId?: string | null;
  userId: string;
  tenantScope: string;
}

export type Elsa3ReusableImportResourceDisposition = "Created" | "Reused" | string | number;

export interface Elsa3ReusableImportSourceReceipt {
  sourceDefinitionId: string;
  sourceVersionId: string;
  workflowDefinitionId: string;
  workflowVersionId: string;
  workflowDisposition: Elsa3ReusableImportResourceDisposition;
  workflowNavigationIdentity: string;
  activityDefinitionId?: string | null;
  activityDefinitionVersionId?: string | null;
  activityDefinitionDisposition?: Elsa3ReusableImportResourceDisposition | null;
  activityVersionDisposition?: Elsa3ReusableImportResourceDisposition | null;
  activityDefinitionNavigationIdentity?: string | null;
  activityVersionNavigationIdentity?: string | null;
}

export interface Elsa3ReusableImportReceipt {
  receiptId: string;
  collectionHandle: string;
  planId: string;
  idempotencyKey: string;
  selectionFingerprint: string;
  accessScope: Elsa3ReusableImportAccessScope;
  status: "Applied" | "AlreadyImported" | string | number;
  completedAt: string;
  sources: Elsa3ReusableImportSourceReceipt[];
}
