import { StudioField } from "@elsa-workflows/studio-ui";
import React, { useId } from "react";
import type { EtlColumn, EtlConnectorBindingSummary, EtlRabbitMqSource, EtlSourceV2, EtlSqlSource } from "../etlTypes";
import { EtlJsonFieldsEditor } from "./EtlJsonFieldsEditor";
import { PipelineBindingSelect } from "./PipelineBindingSelect";
import { SchemaEditor } from "./SchemaEditor";

export function EtlConnectorSourceEditor({
  source,
  bindings,
  maxFields,
  bindingsLoading,
  bindingsUnavailable,
  disabled,
  onChange
}: {
  source: EtlSourceV2;
  bindings: readonly EtlConnectorBindingSummary[];
  maxFields: number;
  bindingsLoading: boolean;
  bindingsUnavailable: boolean;
  disabled: boolean;
  onChange(source: EtlSourceV2): void;
}) {
  const headingId = useId();
  const changeKind = (kind: EtlSourceV2["kind"]) => {
    if (kind === source.kind) return;
    const declared = sourceColumns(source);
    if (kind === "csv") {
      onChange({ kind, binding: source.binding, schema: declared });
      return;
    }
    if (kind === "sql") {
      onChange({ kind, binding: source.binding, table: "", columns: declared, key: [] });
      return;
    }

    const fields = source.kind === "jsonl" || source.kind === "httpJson" || source.kind === "rabbitmq"
      ? source.fields
      : declared.map(({ name, type, required }) => ({ name, pointer: `/${escapePointer(name)}`, type, required }));
    if (kind === "jsonl") {
      onChange({ kind, binding: source.binding, fields });
      return;
    }
    if (kind === "rabbitmq") {
      onChange({ kind, binding: source.binding, queue: "", fields, maxMessages: 1000 });
      return;
    }
    onChange({
      kind,
      binding: source.binding,
      recordsPointer: source.kind === "httpJson" ? source.recordsPointer : "/records",
      fields,
      pagination: source.kind === "httpJson" ? source.pagination : { kind: "singlePage" }
    });
  };

  return (
    <section className="etl-editor-section" aria-labelledby={headingId}>
      <div className="etl-editor-section-heading">
        <div>
          <h3 id={headingId}>Source connector</h3>
          <p>Choose a host-approved CSV, JSON Lines, HTTP, SQL or RabbitMQ source. File paths, endpoint URLs, connection strings and credentials are configured by the host.</p>
        </div>
      </div>
      <div className="etl-editor-binding-grid">
        <div className="etl-editor-field"><StudioField controlId={`${headingId}-kind`} label="Source kind">

          <select id={`${headingId}-kind`} value={source.kind} disabled={disabled} onChange={event => changeKind(event.currentTarget.value as EtlSourceV2["kind"])}>
            <option value="httpJson">HTTP JSON</option>
            <option value="csv">CSV file</option>
            <option value="jsonl">JSON Lines file</option>
            <option value="sql">SQL table</option>
            <option value="rabbitmq">RabbitMQ queue</option>
          </select>
          <small>The source kind determines which approved connector aliases and field declaration are available.</small>
        </StudioField></div>
        <PipelineBindingSelect
          label={source.kind === "csv" ? "Source CSV binding" : source.kind === "jsonl" ? "Source file binding" : source.kind === "sql" ? "Source SQL binding" : source.kind === "rabbitmq" ? "Source RabbitMQ binding" : "Source HTTP binding"}
          role="Source"
          // Hosts list source file approvals under the csv kind; a JSON Lines source selects from the same files.
          kind={source.kind === "httpJson" ? "http" : source.kind === "sql" ? "sql" : source.kind === "rabbitmq" ? "rabbitMq" : "csv"}
          subjectLabel={source.kind === "jsonl" ? "Source file" : undefined}
          value={source.binding}
          bindings={bindings}
          bindingsLoading={bindingsLoading}
          bindingsUnavailable={bindingsUnavailable}
          disabled={disabled}
          onChange={binding => onChange({ ...source, binding })}
        />
      </div>

      {source.kind === "sql" ? (
        <SqlSourceFields source={source} maxFields={maxFields} disabled={disabled} onChange={onChange} />
      ) : source.kind === "rabbitmq" ? (
        <>
          <RabbitMqQueueFields source={source} disabled={disabled} onChange={onChange} />
          <EtlJsonFieldsEditor source={source} maxFields={maxFields} disabled={disabled} onChange={onChange} />
        </>
      ) : source.kind === "csv" ? (
        <SchemaEditor
          title="Source CSV schema"
          description="Field names must match the source CSV header exactly."
          schema={source.schema}
          maxFields={maxFields}
          disabled={disabled}
          onChange={schema => onChange({ ...source, schema })}
        />
      ) : (
        <EtlJsonFieldsEditor source={source} maxFields={maxFields} disabled={disabled} onChange={onChange} />
      )}
    </section>
  );
}

/** The queue and the most messages one run takes. */
function RabbitMqQueueFields({ source, disabled, onChange }: {
  source: EtlRabbitMqSource;
  disabled: boolean;
  onChange(source: EtlSourceV2): void;
}) {
  const id = useId();
  return (
    <div className="etl-editor-binding-grid">
      <div className="etl-editor-field"><StudioField controlId={`${id}-queue`} label="Source queue">

        <input id={`${id}-queue`} value={source.queue} disabled={disabled} aria-describedby={`${id}-queue-help`}
          onChange={event => onChange({ ...source, queue: event.currentTarget.value })} />
        <small id={`${id}-queue-help`}>The host binding must allow this queue. Messages are acknowledged only once the run has recorded them.</small>
      </StudioField></div>
      <div className="etl-editor-field"><StudioField controlId={`${id}-max`} label="Messages per run">

        <input id={`${id}-max`} type="number" min={1} max={100000} value={source.maxMessages} disabled={disabled}
          aria-describedby={`${id}-max-help`}
          // A cleared or fractional value becomes a whole number, so the compiler judges it instead of the request failing.
          onChange={event => onChange({ ...source, maxMessages: Math.trunc(Number(event.currentTarget.value)) || 0 })} />
        <small id={`${id}-max-help`}>A run takes at most this many messages, 1 to 100,000; the rest stay queued for the next run.</small>
      </StudioField></div>
    </div>
  );
}

/** The table, its declared columns and the key that orders the snapshot. */
function SqlSourceFields({ source, maxFields, disabled, onChange }: {
  source: EtlSqlSource;
  maxFields: number;
  disabled: boolean;
  onChange(source: EtlSourceV2): void;
}) {
  const id = useId();
  const required = source.columns.filter(column => column.required && column.name).map(column => column.name);
  const watermarks = source.columns.filter(column => column.required && column.type === "int64" && column.name).map(column => column.name);
  return (
    <>
      <div className="etl-editor-field"><StudioField controlId={`${id}-table`} label="Source table">

        <input id={`${id}-table`} value={source.table} disabled={disabled} placeholder="schema.table" aria-describedby={`${id}-table-help`}
          onChange={event => onChange({ ...source, table: event.currentTarget.value })} />
        <small id={`${id}-table-help`}>The host binding must allow this table. The run reads it once, as of one point in time, when it is prepared.</small>
      </StudioField></div>
      <SchemaEditor
        title="Source table columns"
        description="Column names must match the table's columns. Floating-point columns are not supported."
        fieldLabel="Column"
        emptyMessage="Add the table columns this pipeline reads."
        schema={source.columns}
        maxFields={maxFields}
        disabled={disabled}
        onChange={columns => {
          const key = keyAfterColumnEdit(source, columns);
          // The watermark follows a rename like the key, and is dropped when its column is no longer a required int64.
          const [watermark] = source.watermark !== undefined ? keyAfterColumnEdit({ ...source, key: [source.watermark] }, columns) : [];
          const { watermark: _, ...rest } = source;
          // Mid-rename the name may be empty; the watermark follows its row until it is typed again.
          const valid = watermark !== undefined && columns.some(column => column.name === watermark && column.type === "int64");
          onChange(valid ? { ...rest, columns, key, watermark } : { ...rest, columns, key });
        }}
      />
      <fieldset className="etl-editor-field">
        <legend>Source key</legend>
        <small>Records follow this order. Choose required columns; a unique key keeps record numbers reproducible.</small>
        {required.length === 0 ? <small role="status">Mark at least one column required to choose a key.</small> : null}
        {required.map(name => (
          <label key={name}>
            <input type="checkbox" checked={source.key.includes(name)} disabled={disabled}
              onChange={event => onChange({
                ...source,
                // The key orders the snapshot, so an existing key keeps its order and a new column is appended.
                key: event.currentTarget.checked
                  ? [...source.key.filter(column => column !== name), name]
                  : source.key.filter(column => column !== name)
              })} />
            {name}
          </label>
        ))}
      </fieldset>
      <div className="etl-editor-field"><StudioField controlId={`${id}-watermark`} label="Watermark column">

        <select id={`${id}-watermark`} value={source.watermark ?? ""} disabled={disabled} aria-describedby={`${id}-watermark-help`}
          onChange={event => {
            const { watermark: _, ...rest } = source;
            onChange(event.currentTarget.value ? { ...rest, watermark: event.currentTarget.value } : rest);
          }}>
          <option value="">None: read every row</option>
          {watermarks.map(name => <option key={name} value={name}>{name}</option>)}
        </select>
        <small id={`${id}-watermark-help`}>A required 64-bit integer column whose values become visible in increasing order, such as one stamped by a single writer. Each run then reads only rows above the last completed run's highest value; a sequence, identity or per-row version can skip rows.</small>
      </StudioField></div>
    </>
  );
}

/**
 * The key after a column edit, in the same order. A rename follows the edited row only, so a name another row briefly
 * shares mid-edit never moves the key to that row; any edit drops a key column that is gone or no longer required.
 */
function keyAfterColumnEdit(source: EtlSqlSource, columns: EtlColumn[]): string[] {
  const edited = columns.length === source.columns.length
    ? columns.findIndex((column, row) => column.name !== source.columns[row].name)
    : -1;
  const renamed = edited >= 0
    ? source.key.map(name => name === source.columns[edited].name ? columns[edited].name : name)
    : source.key;
  return renamed.filter(name => columns.some(column => column.name === name && column.required));
}

function sourceColumns(source: EtlSourceV2): EtlColumn[] {
  if (source.kind === "csv") return source.schema;
  if (source.kind === "sql") return source.columns;
  return source.fields.map(({ name, type, required }) => ({ name, type, required }));
}

function escapePointer(value: string) {
  return value.replace(/~/g, "~0").replace(/\//g, "~1");
}
