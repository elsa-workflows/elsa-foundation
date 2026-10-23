import React, { useId } from "react";
import { formatEtlJsonNumber, parseEtlJsonNumber } from "../etlTypes";
import type { EtlConnectorLimits, EtlHttpPolicy, EtlJsonNumber } from "../etlTypes";

const retainedOutputDefault = 1024 * 1024 * 1024;
const retainedMetadataDefault = 64 * 1024 * 1024;

type BudgetField<K extends string> = { key: K; label: string; help: string; wide?: boolean; fallback?: number; max?: number; int64?: boolean };
type BudgetValue = number | EtlJsonNumber | undefined;
type NumericBudgetObject<T> = { [K in keyof T]: BudgetValue };

const connectorLimitFields: Array<BudgetField<keyof EtlConnectorLimits>> = [
  { key: "filePartitionCount", label: "Requested CSV partitions", help: "The source is split into this many disjoint ranges when enough records exist." },
  { key: "maxPartitions", label: "Maximum partitions", help: "The highest partition count accepted by this pipeline." },
  { key: "maxRecordBytes", label: "Maximum record bytes", help: "Largest raw UTF-8 size for one logical source record." },
  { key: "maxFields", label: "Maximum fields per record", help: "Applies to source and optional lookup fields." },
  { key: "maxBatchRecords", label: "Maximum records per batch", help: "Keeps each durable unit of transformation bounded." },
  { key: "maxBatchBytes", label: "Maximum input bytes per batch", help: "Canonical input payload limit for one batch.", wide: true, int64: true },
  { key: "maxOutstandingRecords", label: "Maximum outstanding records", help: "Records admitted but not yet durably settled.", wide: true, int64: true },
  { key: "maxOutstandingBytes", label: "Maximum outstanding input bytes", help: "Canonical input bytes admitted but not yet durably settled.", wide: true, int64: true },
  { key: "maxLookupRecords", label: "Maximum lookup records", help: "Finite bound for the optional pinned lookup table.", wide: true, int64: true },
  { key: "maxLookupBytes", label: "Maximum lookup bytes", help: "Finite byte bound for the optional lookup snapshot.", wide: true, int64: true },
  { key: "maxOutputBatchBytes", label: "Maximum output bytes per batch", help: "Combined accepted and rejected bytes emitted by one batch.", wide: true, int64: true },
  { key: "maxRetainedOutputBytes", label: "Maximum retained route bytes", help: "Total retained main and error route bodies for one run.", wide: true, fallback: retainedOutputDefault, int64: true },
  { key: "maxSnapshotBytes", label: "Maximum bytes per input snapshot", help: "Applies independently to source and lookup snapshots.", wide: true, int64: true },
  { key: "maxArtifactBytes", label: "Maximum artifact bytes", help: "The current format supports up to 256 KiB.", max: 262_144 }
];

const httpPolicyFields: Array<BudgetField<keyof EtlHttpPolicy>> = [
  { key: "maxPageBytes", label: "Maximum HTTP page bytes", help: "Largest raw JSON response body retained for one page.", wide: true, int64: true },
  { key: "maxPageRecords", label: "Maximum records per HTTP page", help: "Largest accepted record count from one response." },
  { key: "maxJsonDepth", label: "Maximum JSON depth", help: "JSON nesting limit; the runtime supports at most 64." },
  { key: "maxContinuationBytes", label: "Maximum continuation bytes", help: "UTF-8 byte limit for an opaque token or next link." },
  { key: "maxPagesPerPartition", label: "Maximum pages per partition", help: "Finite bound on source pagination." },
  { key: "maxRetainedInputBytes", label: "Maximum retained HTTP input bytes", help: "Total raw response bodies retained for one run.", wide: true, int64: true },
  { key: "maxRetainedMetadataBytes", label: "Maximum retained metadata bytes", help: "Bound for retained positions, page history, batches, output plans, and delivery journals.", wide: true, fallback: retainedMetadataDefault, int64: true },
  { key: "maxAttempts", label: "Maximum delivery attempts", help: "Largest persisted attempt count for an HTTP route." },
  { key: "deadlineSeconds", label: "Delivery deadline seconds", help: "Total time allowed for an HTTP route delivery." },
  { key: "requestTimeoutSeconds", label: "Request timeout seconds", help: "Per-request timeout, bounded by the remaining delivery deadline." },
  { key: "initialDelaySeconds", label: "Initial retry delay seconds", help: "Delay used after the first retryable delivery failure." },
  { key: "maxDelaySeconds", label: "Maximum retry delay seconds", help: "Upper bound for retry backoff." }
];

export function EtlConnectorBudgetsEditor({ limits, http, disabled, onLimitsChange, onHttpChange }: {
  limits: EtlConnectorLimits;
  http: EtlHttpPolicy;
  disabled: boolean;
  onLimitsChange(limits: EtlConnectorLimits): void;
  onHttpChange(http: EtlHttpPolicy): void;
}) {
  const limitsHeadingId = useId();
  const httpHeadingId = useId();
  return (
    <>
      <BudgetSection
        headingId={limitsHeadingId}
        title="Connector limits"
        description="These limits bound records, batches, lookups, partitions, retained output, and the published artifact."
        fields={connectorLimitFields}
        values={limits}
        disabled={disabled}
        onChange={onLimitsChange}
      />
      <BudgetSection
        headingId={httpHeadingId}
        title="HTTP policy"
        description="Configure finite response, pagination, retry, and request-time limits."
        fields={httpPolicyFields}
        values={http}
        disabled={disabled}
        onChange={onHttpChange}
      />
    </>
  );
}

function BudgetSection<T extends NumericBudgetObject<T>>({ headingId, title, description, fields, values, disabled, onChange }: {
  headingId: string;
  title: string;
  description: string;
  fields: Array<BudgetField<Extract<keyof T, string>>>;
  values: T;
  disabled: boolean;
  onChange(values: T): void;
}) {
  return (
    <section className="etl-editor-section" aria-labelledby={headingId}>
      <div className="etl-editor-section-heading"><div><h3 id={headingId}>{title}</h3><p>{description}</p></div></div>
      <div className="etl-editor-limits-grid">
        {fields.map(field => {
          const id = `${headingId}-${String(field.key)}`;
          const raw: BudgetValue = values[field.key];
          const usesDefault = raw === undefined && field.fallback !== undefined;
          const effective: number | EtlJsonNumber = raw ?? field.fallback ?? 0;
          return (
            <div className="etl-editor-field" key={String(field.key)}>
              <label htmlFor={id}>{field.label}</label>
              <input
                id={id}
                type="text"
                inputMode="numeric"
                pattern="-?[0-9]+"
                value={formatInteger(effective)}
                disabled={disabled}
                aria-describedby={`${id}-help`}
                onChange={event => {
                  const next = parseInteger(event.currentTarget.value, field.int64 === true, field.max);
                  if (next === undefined) return;
                  onChange({ ...values, [field.key]: next });
                }}
              />
              <small id={`${id}-help`}>{field.help}{usesDefault ? ` The current server default is ${field.fallback} until this field is changed.` : ""}</small>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function parseInteger(raw: string, int64: boolean, maximum?: number): number | EtlJsonNumber | undefined {
  if (raw === "") return 0;
  if (!/^-?\d+$/.test(raw)) return undefined;
  try {
    const parsed = BigInt(raw);
    const minimum = int64 ? -(2n ** 63n) : -(2n ** 31n);
    const maximumValue = int64 ? 2n ** 63n - 1n : 2n ** 31n - 1n;
    if (parsed < minimum || parsed > maximumValue) return undefined;
    if (maximum !== undefined && parsed > BigInt(maximum)) return undefined;
    if (parsed >= 0n && parsed <= BigInt(Number.MAX_SAFE_INTEGER)) return Number(parsed);
    if (parsed < 0n && parsed >= BigInt(Number.MIN_SAFE_INTEGER)) return Number(parsed);
    return parseEtlJsonNumber(parsed.toString());
  } catch {
    return undefined;
  }
}

function formatInteger(value: number | EtlJsonNumber) {
  return value && typeof value === "object" ? formatEtlJsonNumber(value) : String(value);
}
