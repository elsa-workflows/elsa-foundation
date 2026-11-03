import React, { useId } from "react";
import type { EtlColumn, EtlConnectorBindingSummary, EtlSourceV2, EtlSqlSource } from "../etlTypes";
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

    const fields = source.kind === "jsonl" || source.kind === "httpJson"
      ? source.fields
      : declared.map(({ name, type, required }) => ({ name, pointer: `/${escapePointer(name)}`, type, required }));
    if (kind === "jsonl") {
      onChange({ kind, binding: source.binding, fields });
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
          <p>Choose a host-approved CSV, JSON Lines, HTTP or SQL source. File paths, endpoint URLs, connection strings and credentials are configured by the host.</p>
        </div>
      </div>
      <div className="etl-editor-binding-grid">
        <div className="etl-editor-field">
          <label htmlFor={`${headingId}-kind`}>Source kind</label>
          <select id={`${headingId}-kind`} value={source.kind} disabled={disabled} onChange={event => changeKind(event.currentTarget.value as EtlSourceV2["kind"])}>
            <option value="httpJson">HTTP JSON</option>
            <option value="csv">CSV file</option>
            <option value="jsonl">JSON Lines file</option>
            <option value="sql">SQL table</option>
          </select>
          <small>The source kind determines which approved connector aliases and field declaration are available.</small>
        </div>
        <PipelineBindingSelect
          label={source.kind === "csv" ? "Source CSV binding" : source.kind === "jsonl" ? "Source file binding" : source.kind === "sql" ? "Source SQL binding" : "Source HTTP binding"}
          role="Source"
          // Hosts list source file approvals under the csv kind; a JSON Lines source selects from the same files.
          kind={source.kind === "httpJson" ? "http" : source.kind === "sql" ? "sql" : "csv"}
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

/** The table, its declared columns and the key that orders the snapshot. */
function SqlSourceFields({ source, maxFields, disabled, onChange }: {
  source: EtlSqlSource;
  maxFields: number;
  disabled: boolean;
  onChange(source: EtlSourceV2): void;
}) {
  const id = useId();
  const required = source.columns.filter(column => column.required && column.name).map(column => column.name);
  return (
    <>
      <div className="etl-editor-field">
        <label htmlFor={`${id}-table`}>Source table</label>
        <input id={`${id}-table`} value={source.table} disabled={disabled} placeholder="schema.table" aria-describedby={`${id}-table-help`}
          onChange={event => onChange({ ...source, table: event.currentTarget.value })} />
        <small id={`${id}-table-help`}>The host binding must allow this table. The run reads it once, as of one point in time, when it is prepared.</small>
      </div>
      <SchemaEditor
        title="Source table columns"
        description="Column names must match the table's columns. Floating-point columns are not supported."
        fieldLabel="Column"
        emptyMessage="Add the table columns this pipeline reads."
        schema={source.columns}
        maxFields={maxFields}
        disabled={disabled}
        onChange={columns => onChange({ ...source, columns, key: keyAfterColumnEdit(source, columns) })}
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
