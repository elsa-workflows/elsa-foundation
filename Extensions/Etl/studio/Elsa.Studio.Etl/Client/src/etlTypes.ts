import { parseLosslessNumber, type LosslessNumber } from "lossless-json";

/** The scalar column types accepted by the finite ETL contract (camel-cased on the wire). */
export type EtlColumnType = "string" | "int64" | "decimal" | "boolean";

/** A field can be read from the source row or its pinned lookup row. */
export type EtlFieldOrigin = "source" | "lookup";

/** The deterministic comparisons supported by ETL filters (camel-cased on the wire). */
export type EtlComparison = "equal" | "notEqual";

/** A JSON numeric literal retained without JavaScript's IEEE-754 rounding. */
export type EtlJsonNumber = LosslessNumber;

export type EtlJsonScalar = string | number | EtlJsonNumber | boolean | null;

/** Parse a validated numeric literal without converting it through a JavaScript number. */
export function parseEtlJsonNumber(value: string): EtlJsonNumber {
  return parseLosslessNumber(value);
}

/** Render an ETL numeric literal exactly as it appeared in the JSON document. */
export function formatEtlJsonNumber(value: EtlJsonNumber): string {
  return value.toString();
}

export interface EtlFileBindings {
  source: string;
  lookup: string;
  output: string;
  errors: string;
}

export interface EtlColumn {
  name: string;
  type: EtlColumnType;
  required: boolean;
}

export interface EtlOutputField {
  name: string;
  origin: EtlFieldOrigin;
  field: string;
}

export interface EtlFilter {
  field: string;
  comparison: EtlComparison;
  value: EtlJsonScalar;
}

export interface EtlLimits {
  partitionCount: number;
  maxRecordBytes: number;
  maxFields: number;
  maxBatchRecords: number;
  maxBatchBytes: number | EtlJsonNumber;
  maxOutstandingRecords: number | EtlJsonNumber;
  maxOutstandingBytes: number | EtlJsonNumber;
  maxLookupRecords: number | EtlJsonNumber;
  maxLookupBytes: number | EtlJsonNumber;
  maxOutputBatchBytes: number | EtlJsonNumber;
  maxSnapshotBytes: number | EtlJsonNumber;
  maxArtifactBytes: number;
}

/** Exact authoring document persisted in an ETL draft and compiled into an immutable version. */
export interface EtlPipeline {
  bindings: EtlFileBindings;
  sourceSchema: EtlColumn[];
  lookupSchema: EtlColumn[];
  sourceLookupField: string;
  lookupKeyField: string;
  mapping: EtlOutputField[];
  filters: EtlFilter[];
  limits: EtlLimits;
}

export interface EtlPage<T> {
  items: T[];
  nextCursor: string | null;
}

export interface EtlPageRequest {
  cursor?: string | null;
  limit?: number;
}

export interface EtlCreateDefinitionRequest {
  id: string;
  pipeline: EtlPipeline;
}

export interface EtlUpdateDraftRequest {
  expectedRevision: number;
  pipeline: EtlPipeline;
}

export interface EtlValidateDraftRequest {
  expectedRevision: number;
}

export interface EtlPublishDefinitionRequest {
  expectedRevision: number;
  definitionVersion: number;
  contentHash: string;
}

/** Safe host-configured alias and role. Paths and binding handles are never returned. */
export interface EtlBindingSummary {
  alias: string;
  role: "Source" | "Lookup" | "MainOutput" | "ErrorOutput";
}

export interface EtlDefinitionSummaryView {
  definitionId: string;
  revision: number;
  lastPublishedVersion: number;
  updatedAt: string;
}

export interface EtlDraftView {
  definitionId: string;
  revision: number;
  lastPublishedVersion: number;
  pipeline: EtlPipeline;
  updatedAt: string;
}

export interface EtlPublishedVersionView {
  definitionId: string;
  definitionVersion: number;
  contentHash: string;
  selection: string;
  publishedAt: string | null;
}

export interface EtlPublishedArtifactView {
  version: EtlPublishedVersionView;
  pipeline: EtlPipeline;
}

export interface EtlDiagnostic {
  code: string;
  path: string;
  message: string;
}

export interface EtlValidationView {
  revision: number;
  definitionVersion: number;
  contentHash: string | null;
  diagnostics: EtlDiagnostic[];
}

export interface EtlPublicationView {
  status: "published" | "replay" | (string & {});
  version: EtlPublishedVersionView;
}

export interface EtlApiProblem {
  status: number;
  code: string;
}

export interface EtlApiCapabilityLink {
  rel: string;
  href: string;
  templated?: boolean;
}

export interface EtlApiCapability {
  id: string;
  contractVersion: string;
  links: EtlApiCapabilityLink[];
}

export interface EtlApiCapabilityDocument {
  capabilities: EtlApiCapability[];
}

export type EtlRunStatus = "preparing" | "running" | "succeeded" | "businessError" | "faulted" | "cancelled" | (string & {});
export type EtlTerminalOutcome = "succeeded" | "businessError" | "faulted" | "cancelled" | (string & {});

export interface EtlRunDiagnosticView {
  code: string;
  recordOrdinal: number | null;
  field: string | null;
}

export interface EtlRunTerminalResultView {
  outcome: EtlTerminalOutcome;
  acceptedRecords: number;
  rejectedRecords: number;
  filteredRecords: number;
  diagnostic: EtlRunDiagnosticView | null;
  settledAt: string;
}

export interface EtlRunSummaryView {
  runId: string;
  selection: string;
  status: EtlRunStatus;
  sourceRecordCount: number | null;
  outstandingRecords: number;
  outstandingBytes: number;
  acceptedRecords: number;
  rejectedRecords: number;
  filteredRecords: number;
  diagnostic: EtlRunDiagnosticView | null;
}

/** Last verified preparation metadata; it is not a new integrity check at read time. */
export interface EtlRunInputIdentityView {
  bindingRevision: string;
  expectedSha256: string | null;
  snapshotSha256: string | null;
  snapshotByteLength: number | null;
}

export interface EtlRunBindingAliasesView {
  source: string;
  lookup: string;
  output: string;
  errors: string;
}

export interface EtlRunDetailView extends EtlRunSummaryView {
  bindingAliases: EtlRunBindingAliasesView | null;
  terminalResult: EtlRunTerminalResultView | null;
  sourceInput?: EtlRunInputIdentityView | null;
  lookupInput?: EtlRunInputIdentityView | null;
}

export interface EtlRunOwedBatchView {
  startOrdinal: number;
  endOrdinalExclusive: number;
  inputBytes: number;
}

export interface EtlRunPartitionView {
  partitionOrdinal: number;
  startOrdinal: number;
  endOrdinalExclusive: number;
  nextOrdinal: number;
  isClaimed: boolean;
  claimFence: number | null;
  claimExpiresAt: string | null;
  owedBatch: EtlRunOwedBatchView | null;
}

export interface EtlRunCancellationView {
  cancellationAccepted: boolean;
  terminalResult: EtlRunTerminalResultView;
}

export type EtlRunPageRequest = EtlPageRequest;
