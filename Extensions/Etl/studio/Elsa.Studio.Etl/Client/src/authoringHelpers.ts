import { stringify } from "lossless-json";
import type { EtlPipeline, EtlPipelineV2 } from "./etlTypes";

/** Mirrors the finite, compiler-valid starter pipeline used by the ETL contract tests. */
export function createDefaultEtlPipeline(): EtlPipeline {
  return {
    bindings: { source: "source", lookup: "lookup", output: "output", errors: "errors" },
    sourceSchema: [
      { name: "id", type: "int64", required: true },
      { name: "name", type: "string", required: false }
    ],
    lookupSchema: [
      { name: "id", type: "int64", required: true },
      { name: "category", type: "string", required: false }
    ],
    sourceLookupField: "id",
    lookupKeyField: "id",
    mapping: [{ name: "name", origin: "source", field: "name" }],
    filters: [],
    limits: {
      partitionCount: 2,
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
    }
  };
}

export function sameEtlPipeline(left: EtlPipeline | EtlPipelineV2, right: EtlPipeline | EtlPipelineV2): boolean {
  return stringify(left) === stringify(right);
}

export function isValidEtlDefinitionId(value: string): boolean {
  return /^[A-Za-z0-9][A-Za-z0-9._-]{0,255}$/.test(value);
}
