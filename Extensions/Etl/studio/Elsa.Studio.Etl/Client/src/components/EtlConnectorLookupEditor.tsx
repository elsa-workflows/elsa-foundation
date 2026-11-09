import React, { useId } from "react";
import type { EtlColumn, EtlConnectorBindingSummary, EtlCsvLookup } from "../etlTypes";
import { PipelineBindingSelect } from "./PipelineBindingSelect";
import { SchemaEditor } from "./SchemaEditor";

export function EtlConnectorLookupEditor({ lookup, sourceSchema, bindings, maxFields, bindingsLoading, bindingsUnavailable, disabled, onChange }: {
  lookup: EtlCsvLookup | null;
  sourceSchema: readonly EtlColumn[];
  bindings: readonly EtlConnectorBindingSummary[];
  maxFields: number;
  bindingsLoading: boolean;
  bindingsUnavailable: boolean;
  disabled: boolean;
  onChange(lookup: EtlCsvLookup | null): void;
}) {
  const headingId = useId();
  const toggleId = `${headingId}-enabled`;
  const enable = (checked: boolean) => {
    if (!checked) { onChange(null); return; }
    const key = sourceSchema.find(field => field.required) ?? sourceSchema[0];
    const schema = key ? [{ ...key, required: true }] : [];
    onChange({
      binding: "lookup",
      schema,
      sourceKeyField: key?.name ?? "",
      lookupKeyField: key?.name ?? ""
    });
  };
  const update = <K extends keyof EtlCsvLookup>(key: K, value: EtlCsvLookup[K]) => {
    if (lookup) onChange({ ...lookup, [key]: value });
  };

  return (
    <section className="etl-editor-section" aria-labelledby={headingId}>
      <div className="etl-editor-section-heading">
        <div>
          <h3 id={headingId}>Optional CSV lookup</h3>
          <p>Join source records to a host-approved CSV lookup by required keys with matching scalar types.</p>
        </div>
      </div>
      <label className="etl-editor-checkbox" htmlFor={toggleId}>
        <input id={toggleId} type="checkbox" checked={lookup !== null} disabled={disabled} onChange={event => enable(event.currentTarget.checked)} />
        <span>Use a CSV lookup</span>
      </label>

      {lookup ? (
        <>
          <div className="etl-editor-binding-grid">
            <PipelineBindingSelect label="Lookup CSV binding" role="Lookup" kind="csv" value={lookup.binding} bindings={bindings} bindingsLoading={bindingsLoading} bindingsUnavailable={bindingsUnavailable} disabled={disabled} onChange={binding => update("binding", binding)} />
          </div>
          <SchemaEditor title="Lookup CSV fields" description="Field names must match the lookup CSV header exactly." schema={lookup.schema} maxFields={maxFields} disabled={disabled} onChange={schema => update("schema", schema)} />
          <div className="etl-editor-binding-grid etl-editor-key-grid">
            <KeyChoice label="Source lookup field" value={lookup.sourceKeyField} fields={sourceSchema} counterpartFields={lookup.schema} counterpart={lookup.lookupKeyField} disabled={disabled} onChange={sourceKeyField => update("sourceKeyField", sourceKeyField)} />
            <KeyChoice label="Lookup key field" value={lookup.lookupKeyField} fields={lookup.schema} counterpartFields={sourceSchema} counterpart={lookup.sourceKeyField} disabled={disabled} onChange={lookupKeyField => update("lookupKeyField", lookupKeyField)} />
          </div>
        </>
      ) : <p className="etl-editor-empty" role="status">No lookup data will be joined to source records.</p>}
    </section>
  );
}

function KeyChoice({ label, value, fields, counterpartFields, counterpart, disabled, onChange }: {
  label: string;
  value: string;
  fields: readonly EtlColumn[];
  counterpartFields: readonly EtlColumn[];
  counterpart: string;
  disabled: boolean;
  onChange(value: string): void;
}) {
  const id = useId();
  const selected = fields.find(field => field.name === value);
  const matching = counterpartFields.find(field => field.name === counterpart);
  const validPair = !!selected && !!matching && selected.required && matching.required && selected.type === matching.type;
  const helpId = `${id}-help`;
  return (
    <div className="etl-editor-field">
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value} disabled={disabled} aria-describedby={helpId} aria-invalid={value !== "" && !validPair} onChange={event => onChange(event.currentTarget.value)}>
        <option value="">Choose a required field</option>
        {value && !selected ? <option value={value}>{value} (unavailable)</option> : null}
        {fields.map((field, index) => <option key={`${field.name}-${index}`} value={field.name} disabled={!field.required}>{field.name || `Unnamed field ${index + 1}`}{field.required ? ` (${field.type})` : " (optional)"}</option>)}
      </select>
      <small id={helpId}>{validPair ? `Key type: ${selected.type}. Both key fields are required.` : "Choose required source and lookup key fields with the same type."}</small>
    </div>
  );
}
