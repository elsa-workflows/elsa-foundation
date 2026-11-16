import type { EtlPipelineV2 } from "./etlTypes";

/** Mirrors the compiler-valid connector pipeline used by the v2 contract tests. */
export function createDefaultEtlConnectorPipeline(): EtlPipelineV2 {
  return {
    source: {
      kind: "httpJson",
      binding: "source",
      recordsPointer: "/records",
      fields: [
        { name: "id", pointer: "/id", type: "int64", required: true },
        { name: "name", pointer: "/name", type: "string", required: true }
      ],
      pagination: { kind: "token", continuationPointer: "/next", queryParameter: "cursor" }
    },
    lookup: null,
    routes: {
      main: { kind: "http", binding: "output" },
      errors: { kind: "http", binding: "errors" }
    },
    mapping: [{ name: "name", origin: "source", field: "name" }],
    filters: [],
    limits: {
      filePartitionCount: 2,
      maxPartitions: 32,
      maxRecordBytes: 64 * 1024,
      maxFields: 256,
      maxBatchRecords: 1_000,
      maxBatchBytes: 4 * 1024 * 1024,
      maxOutstandingRecords: 10_000,
      maxOutstandingBytes: 32 * 1024 * 1024,
      maxLookupRecords: 100_000,
      maxLookupBytes: 16 * 1024 * 1024,
      maxOutputBatchBytes: 8 * 1024 * 1024,
      maxSnapshotBytes: 1024 * 1024 * 1024,
      maxArtifactBytes: 262_144
    },
    http: {
      maxPageBytes: 8 * 1024 * 1024,
      maxPageRecords: 10_000,
      maxJsonDepth: 32,
      maxContinuationBytes: 8 * 1024,
      maxPagesPerPartition: 10_000,
      maxRetainedInputBytes: 1024 * 1024 * 1024,
      maxAttempts: 5,
      deadlineSeconds: 900,
      requestTimeoutSeconds: 30,
      initialDelaySeconds: 1,
      maxDelaySeconds: 60
    }
  };
}
