import React, { useId } from "react";
import type { EtlConnectorBindingSummary, EtlSourceV2 } from "../etlTypes";
import { EtlHttpJsonSourceFieldsEditor } from "./EtlHttpJsonSourceFieldsEditor";
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
    if (kind === "csv") {
      onChange({
        kind,
        binding: source.binding,
        schema: source.kind === "csv" ? source.schema : source.fields.map(({ name, type, required }) => ({ name, type, required }))
      });
      return;
    }

    const fields = source.kind === "httpJson"
      ? source.fields
      : source.schema.map(({ name, type, required }) => ({ name, pointer: `/${escapePointer(name)}`, type, required }));
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
          <p>Choose a host-approved CSV or HTTP source. Endpoint URLs and credentials are configured by the host.</p>
        </div>
      </div>
      <div className="etl-editor-binding-grid">
        <div className="etl-editor-field">
          <label htmlFor={`${headingId}-kind`}>Source kind</label>
          <select id={`${headingId}-kind`} value={source.kind} disabled={disabled} onChange={event => changeKind(event.currentTarget.value as EtlSourceV2["kind"])}>
            <option value="httpJson">HTTP JSON</option>
            <option value="csv">CSV file</option>
          </select>
          <small>The source kind determines which approved connector aliases and field declaration are available.</small>
        </div>
        <PipelineBindingSelect
          label={source.kind === "csv" ? "Source CSV binding" : "Source HTTP binding"}
          role="Source"
          kind={source.kind === "csv" ? "csv" : "http"}
          value={source.binding}
          bindings={bindings}
          bindingsLoading={bindingsLoading}
          bindingsUnavailable={bindingsUnavailable}
          disabled={disabled}
          onChange={binding => onChange({ ...source, binding })}
        />
      </div>

      {source.kind === "csv" ? (
        <SchemaEditor
          title="Source CSV schema"
          description="Field names must match the source CSV header exactly."
          schema={source.schema}
          maxFields={maxFields}
          disabled={disabled}
          onChange={schema => onChange({ ...source, schema })}
        />
      ) : (
        <EtlHttpJsonSourceFieldsEditor source={source} maxFields={maxFields} disabled={disabled} onChange={onChange} />
      )}
    </section>
  );
}

function escapePointer(value: string) {
  return value.replace(/~/g, "~0").replace(/\//g, "~1");
}
