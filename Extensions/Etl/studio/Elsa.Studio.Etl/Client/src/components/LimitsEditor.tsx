import React, { useId } from "react";
import { parseEtlJsonNumber } from "../etlTypes";
import type { EtlLimits } from "../etlTypes";

const limitFields: Array<{ key: keyof EtlLimits; label: string; description: string; step?: number; maximum?: number }> = [
  { key: "partitionCount", label: "Requested partitions", description: "The source is split into this many disjoint ranges when enough records exist.", step: 1 },
  { key: "maxRecordBytes", label: "Maximum CSV record bytes", description: "Maximum raw UTF-8 bytes in one logical record.", step: 1 },
  { key: "maxFields", label: "Maximum fields per CSV record", description: "Applies to both source and lookup schemas.", step: 1 },
  { key: "maxBatchRecords", label: "Maximum records per batch", description: "Keeps each durable unit of ETL work bounded.", step: 1 },
  { key: "maxBatchBytes", label: "Maximum input bytes per batch", description: "Canonical input payload byte limit for one batch.", step: 1 },
  { key: "maxOutstandingRecords", label: "Maximum outstanding records", description: "Records admitted but not yet durably settled.", step: 1 },
  { key: "maxOutstandingBytes", label: "Maximum outstanding bytes", description: "Canonical input bytes admitted but not yet durably settled.", step: 1 },
  { key: "maxLookupRecords", label: "Maximum lookup records", description: "Finite bound for the pinned lookup table.", step: 1 },
  { key: "maxLookupBytes", label: "Maximum lookup bytes", description: "Finite byte bound for the pinned lookup snapshot.", step: 1 },
  { key: "maxOutputBatchBytes", label: "Maximum output bytes per batch", description: "Combined accepted and rejected JSONL bytes emitted by one batch.", step: 1 },
  { key: "maxSnapshotBytes", label: "Maximum bytes per input snapshot", description: "Applies independently to the source and lookup snapshots.", step: 1 },
  { key: "maxArtifactBytes", label: "Maximum artifact bytes", description: "The current format has a hard limit of 256 KiB.", step: 1, maximum: 262_144 }
];

export function LimitsEditor({ limits, sourceSchemaCount, lookupSchemaCount, disabled, onChange }: {
  limits: EtlLimits;
  sourceSchemaCount: number;
  lookupSchemaCount: number;
  disabled: boolean;
  onChange(limits: EtlLimits): void;
}) {
  const headingId = useId();
  return (
    <section className="etl-editor-section" aria-labelledby={headingId}>
      <div className="etl-editor-section-heading">
        <div>
          <h3 id={headingId}>Finite limits</h3>
          <p>These limits bound logical records, bytes, partitions, and the published artifact.</p>
        </div>
      </div>
      <div className="etl-editor-limits-grid">
        {limitFields.map(field => {
          const id = `${headingId}-${field.key}`;
          const current = limits[field.key];
          const schemaCount = field.key === "maxFields" ? Math.max(sourceSchemaCount, lookupSchemaCount) : undefined;
          return (
            <div className="etl-editor-field" key={field.key}>
              <label htmlFor={id}>{field.label}</label>
              <input
                id={id}
                type="number"
                min={1}
                max={field.maximum}
                step={field.step}
                value={String(current)}
                disabled={disabled}
                aria-describedby={`${id}-help`}
                onChange={event => {
                  const raw = event.currentTarget.value;
                  if (raw === "") { onChange({ ...limits, [field.key]: 0 }); return; }
                  if (!/^-?(?:0|[1-9]\d*)$/.test(raw)) return;
                  const numeric = Number(raw);
                  const parsed = Number.isSafeInteger(numeric) ? numeric : parseEtlJsonNumber(raw);
                  if (typeof parsed !== "number" && ["partitionCount", "maxRecordBytes", "maxFields", "maxBatchRecords", "maxArtifactBytes"].includes(field.key)) return;
                  onChange({ ...limits, [field.key]: parsed });
                }}
              />
              <small id={`${id}-help`}>{field.description}{schemaCount ? ` Current schemas need at least ${schemaCount} fields.` : ""}</small>
            </div>
          );
        })}
      </div>
    </section>
  );
}
