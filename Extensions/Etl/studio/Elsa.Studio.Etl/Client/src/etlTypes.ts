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

/** Finite connector definition document persisted and published in artifact format v2. */
export interface EtlPipelineV2 {
  source: EtlSourceV2;
  lookup: EtlCsvLookup | null;
  routes: EtlRoutes;
  mapping: EtlOutputField[];
  filters: EtlFilter[];
  limits: EtlConnectorLimits;
  http: EtlHttpPolicy;
  /** Omitted for a finite pipeline; present when the HTTP shards poll until completion or cancellation. */
  continuous?: EtlContinuousRunPolicy;
}

export interface EtlContinuousRunPolicy {
  pollIntervalSeconds: number;
}

export type EtlSourceV2 = EtlCsvSource | EtlHttpJsonSource | EtlJsonlSource | EtlSqlSource | EtlRabbitMqSource;

/**
 * A host-approved RabbitMQ queue drained up to `maxMessages` when a run prepares; its messages are acknowledged once the
 * snapshot of their JSON bodies is recorded, and each body is read with the declared JSON fields.
 */
export interface EtlRabbitMqSource {
  kind: "rabbitmq";
  binding: string;
  queue: string;
  fields: EtlJsonField[];
  /** 1 to 100,000. */
  maxMessages: number;
}

export interface EtlCsvSource {
  kind: "csv";
  binding: string;
  schema: EtlColumn[];
}

export interface EtlHttpJsonSource {
  kind: "httpJson";
  binding: string;
  recordsPointer: string;
  fields: EtlJsonField[];
  pagination: EtlHttpPagination;
}

/** A host-approved JSON Lines file: one JSON object per line, with fields selected by JSON Pointer. */
export interface EtlJsonlSource {
  kind: "jsonl";
  binding: string;
  fields: EtlJsonField[];
}

/**
 * A host-approved PostgreSQL or SQL Server table, read once as of one point in time when the run is prepared and then
 * processed like a JSON Lines file (finite runs only).
 */
export interface EtlSqlSource {
  kind: "sql";
  binding: string;
  /** `name` or `schema.name`. */
  table: string;
  columns: EtlColumn[];
  /** One to eight required columns ordering the snapshot. */
  key: string[];
  /** A required int64 column; each run then reads only rows above the last completed run's high watermark. */
  watermark?: string;
}

export interface EtlJsonField {
  name: string;
  pointer: string;
  type: EtlColumnType;
  required: boolean;
}

export type EtlHttpPagination =
  | { kind: "singlePage" }
  | { kind: "token"; continuationPointer: string; queryParameter: string }
  | { kind: "nextLink"; continuationPointer: string };

export interface EtlCsvLookup {
  binding: string;
  schema: EtlColumn[];
  sourceKeyField: string;
  lookupKeyField: string;
}

export interface EtlRoutes {
  main: EtlDestination;
  errors: EtlDestination;
}

export type EtlDestination = EtlJsonlDestination | EtlHttpDestination | EtlCsvDestination | EtlSqlDestination | EtlRabbitMqDestination;

export interface EtlJsonlDestination {
  kind: "jsonl";
  binding: string;
}

export interface EtlHttpDestination {
  kind: "http";
  binding: string;
}

/** A host-approved output folder receiving one RFC 4180 CSV file, with a header row, per published batch. */
export interface EtlCsvDestination {
  kind: "csv";
  binding: string;
}

/**
 * A host-approved PostgreSQL or SQL Server table upserted by key, one transaction per batch with an effect ledger that
 * applies each batch once (main route only). Its columns are the output field names.
 */
export interface EtlSqlDestination {
  kind: "sql";
  binding: string;
  /** `name` or `schema.name`. */
  table: string;
  /** One to eight output fields mapped from required source fields. */
  key: string[];
}

/**
 * A host-approved RabbitMQ exchange receiving one persistent JSON message per record, recorded once the broker confirmed
 * every message of the batch; delivery is at least once with stable message ids (main route only).
 */
export interface EtlRabbitMqDestination {
  kind: "rabbitmq";
  binding: string;
  /** The exchange, or empty for the default exchange. */
  exchange: string;
  routingKey: string;
}

export interface EtlConnectorLimits {
  filePartitionCount: number;
  maxPartitions: number;
  maxRecordBytes: number;
  maxFields: number;
  maxBatchRecords: number;
  maxBatchBytes: number | EtlJsonNumber;
  maxOutstandingRecords: number | EtlJsonNumber;
  maxOutstandingBytes: number | EtlJsonNumber;
  maxLookupRecords: number | EtlJsonNumber;
  maxLookupBytes: number | EtlJsonNumber;
  maxOutputBatchBytes: number | EtlJsonNumber;
  /** Omitted when the versioned artifact uses the server's default. */
  maxRetainedOutputBytes?: number | EtlJsonNumber;
  maxSnapshotBytes: number | EtlJsonNumber;
  maxArtifactBytes: number;
}

export interface EtlHttpPolicy {
  maxPageBytes: number | EtlJsonNumber;
  maxPageRecords: number;
  maxJsonDepth: number;
  maxContinuationBytes: number;
  maxPagesPerPartition: number;
  maxRetainedInputBytes: number | EtlJsonNumber;
  /** Omitted when the versioned artifact uses the server's default. */
  maxRetainedMetadataBytes?: number | EtlJsonNumber;
  maxAttempts: number;
  deadlineSeconds: number;
  requestTimeoutSeconds: number;
  initialDelaySeconds: number;
  maxDelaySeconds: number;
}

export interface EtlPage<T> {
  items: T[];
  nextCursor: string | null;
}

export interface EtlPageRequest {
  cursor?: string | null;
  limit?: number;
}

export interface EtlCreateDefinitionRequest<TPipeline = EtlPipeline> {
  id: string;
  pipeline: TPipeline;
}

export interface EtlUpdateDraftRequest<TPipeline = EtlPipeline> {
  expectedRevision: number;
  pipeline: TPipeline;
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

/** A current v2 connector alias without endpoint, storage, or credential details. */
export interface EtlConnectorBindingSummary extends EtlBindingSummary {
  kind: "csv" | "http" | "jsonl" | "sql" | "rabbitMq";
}

export interface EtlDefinitionSummaryView {
  definitionId: string;
  revision: number;
  lastPublishedVersion: number;
  updatedAt: string;
}

export interface EtlDraftView<TPipeline = EtlPipeline> {
  definitionId: string;
  revision: number;
  lastPublishedVersion: number;
  pipeline: TPipeline;
  updatedAt: string;
}

export interface EtlPublishedVersionView {
  definitionId: string;
  definitionVersion: number;
  contentHash: string;
  selection: string;
  publishedAt: string | null;
}

export interface EtlPublishedArtifactView<TPipeline = EtlPipeline> {
  version: EtlPublishedVersionView;
  pipeline: TPipeline;
}

export type EtlConnectorCreateDefinitionRequest = EtlCreateDefinitionRequest<EtlPipelineV2>;
export type EtlConnectorUpdateDraftRequest = EtlUpdateDraftRequest<EtlPipelineV2>;
export type EtlConnectorDraftView = EtlDraftView<EtlPipelineV2>;
export type EtlConnectorPublishedArtifactView = EtlPublishedArtifactView<EtlPipelineV2>;

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
  lookup: string | null;
  output: string;
  errors: string;
}

export interface EtlRunConnectorView {
  sourceKind: string;
  mainKind: string;
  errorKind: string;
  acceptedMainEffects: number;
  acceptedErrorEffects: number;
  /** Absent on hosts that predate continuous runs, which only run finitely. */
  mode?: "finite" | "continuous";
  completionRequestedAt?: string | null;
}

export interface EtlRunDetailView extends EtlRunSummaryView {
  bindingAliases: EtlRunBindingAliasesView | null;
  terminalResult: EtlRunTerminalResultView | null;
  sourceInput?: EtlRunInputIdentityView | null;
  lookupInput?: EtlRunInputIdentityView | null;
  connector?: EtlRunConnectorView | null;
  formatVersion?: number;
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

export interface EtlRunConnectorPartitionView {
  partitionOrdinal: number;
  file: EtlRunFileProgressView | null;
  http: EtlRunHttpProgressView | null;
  isClaimed: boolean;
  claimFence: number | null;
  claimExpiresAt: string | null;
  notBefore: string | null;
  owedBatch: EtlRunConnectorOwedView | null;
  acceptedMainEffects: number;
  acceptedErrorEffects: number;
  /** A caught-up continuous shard's next poll; null or absent for retry waits and finite runs. */
  nextPollAt?: string | null;
}

export interface EtlRunFileProgressView {
  startOrdinal: number;
  endOrdinalExclusive: number;
  nextOrdinal: number;
}

export interface EtlRunHttpProgressView {
  pageSequence: number;
  nextItem: number;
  pageRecordCount: number | null;
  exhausted: boolean;
  fetch: EtlRunRetryView | null;
}

export interface EtlRunRetryView {
  phase: string;
  attemptCount: number;
  deadline: string;
  notBefore: string | null;
  lastStatusCode: number | null;
}

export interface EtlRunConnectorOwedView {
  recordCount: number;
  inputBytes: number;
  outputRetained: boolean;
  main: EtlRunEffectView | null;
  errors: EtlRunEffectView | null;
}

export interface EtlRunEffectView {
  kind: string;
  recordCount: number;
  state: string;
  retry: EtlRunRetryView | null;
}

export interface EtlRunCancellationView {
  cancellationAccepted: boolean;
  terminalResult: EtlRunTerminalResultView;
}

export interface EtlRunCompletionView {
  result: "accepted" | "alreadyRequested" | "terminal" | "notContinuous";
}

export type EtlRunPageRequest = EtlPageRequest;
