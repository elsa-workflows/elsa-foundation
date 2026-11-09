import React, { useId } from "react";
import type { EtlBindingSummary, EtlDiagnostic, EtlPipeline } from "../etlTypes";
import { FilterEditor } from "./FilterEditor";
import { LimitsEditor } from "./LimitsEditor";
import { MappingEditor } from "./MappingEditor";
import { PipelineBindingSelect } from "./PipelineBindingSelect";
import { SchemaEditor } from "./SchemaEditor";

export function EtlPipelineEditor({
  pipeline,
  onChange,
  bindings,
  readOnly = false,
  diagnostics = []
}: {
  pipeline: EtlPipeline;
  onChange(pipeline: EtlPipeline): void;
  bindings: readonly EtlBindingSummary[];
  readOnly?: boolean;
  diagnostics?: readonly EtlDiagnostic[];
}) {
  const titleId = useId();
  const update = <K extends keyof EtlPipeline>(key: K, value: EtlPipeline[K]) =>
    onChange({ ...pipeline, [key]: value });
  const bindingsHeadingId = useId();

  return (
    <div className="etl-pipeline-editor" aria-labelledby={titleId}>
      <header className="etl-editor-header">
        <div>
          <p className="etl-editor-eyebrow">Finite CSV to JSONL</p>
          <h2 id={titleId}>Pipeline stages</h2>
          <p>Configure the supported file transform as a fixed, reviewable sequence.</p>
        </div>
      </header>

      <ol className="etl-editor-order" aria-label="ETL operation order">
        <li><span>1</span><div><strong>Read and validate CSV</strong><small>Source and lookup schemas</small></div></li>
        <li><span>2</span><div><strong>Match lookup rows</strong><small>Exact key with matching types</small></div></li>
        <li><span>3</span><div><strong>Map output fields</strong><small>Source or matched lookup values</small></div></li>
        <li><span>4</span><div><strong>Apply output filters</strong><small>Equality or inequality</small></div></li>
        <li><span>5</span><div><strong>Route JSONL records</strong><small>Accepted output and safe errors</small></div></li>
      </ol>

      {diagnostics.length > 0 ? (
        <div className="etl-editor-diagnostics" role="status" aria-live="polite">
          <h3>Validation feedback</h3>
          <ul>
            {diagnostics.map((diagnostic, index) => (
              <li key={`${diagnostic.code}:${diagnostic.path}:${index}`}>
                <code>{diagnostic.code}</code>
                {diagnostic.path ? <span className="etl-editor-diagnostic-path">{diagnostic.path}</span> : null}
                <span>{diagnostic.message}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <section className="etl-editor-section" aria-labelledby={bindingsHeadingId}>
        <div className="etl-editor-section-heading">
          <div>
            <h3 id={bindingsHeadingId}>Approved file bindings</h3>
            <p>Only host-approved aliases are selectable. File paths and storage details are not part of the definition.</p>
          </div>
        </div>
        <div className="etl-editor-binding-grid">
          <PipelineBindingSelect label="Source file" role="Source" value={pipeline.bindings.source} bindings={bindings} disabled={readOnly} onChange={source => update("bindings", { ...pipeline.bindings, source })} />
          <PipelineBindingSelect label="Lookup file" role="Lookup" value={pipeline.bindings.lookup} bindings={bindings} disabled={readOnly} onChange={lookup => update("bindings", { ...pipeline.bindings, lookup })} />
          <PipelineBindingSelect label="Accepted output" role="MainOutput" value={pipeline.bindings.output} bindings={bindings} disabled={readOnly} onChange={output => update("bindings", { ...pipeline.bindings, output })} />
          <PipelineBindingSelect label="Rejected records" role="ErrorOutput" value={pipeline.bindings.errors} bindings={bindings} disabled={readOnly} onChange={errors => update("bindings", { ...pipeline.bindings, errors })} />
        </div>
      </section>

      <SchemaEditor title="Source CSV schema" description="Field names must match the source CSV header exactly." schema={pipeline.sourceSchema} maxFields={pipeline.limits.maxFields} disabled={readOnly} onChange={sourceSchema => update("sourceSchema", sourceSchema)} />

      <section className="etl-editor-section" aria-labelledby={`${titleId}-lookup`}>
        <div className="etl-editor-section-heading">
          <div>
            <h3 id={`${titleId}-lookup`}>Lookup schema and exact key</h3>
            <p>Choose required key fields with identical scalar types. Lookup rows are matched by exact value.</p>
          </div>
        </div>
        <SchemaEditor title="Lookup CSV fields" description="Field names must match the lookup CSV header exactly." schema={pipeline.lookupSchema} maxFields={pipeline.limits.maxFields} disabled={readOnly} onChange={lookupSchema => update("lookupSchema", lookupSchema)} />
        <div className="etl-editor-binding-grid etl-editor-key-grid">
          <FieldChoice label="Source lookup field" value={pipeline.sourceLookupField} fields={pipeline.sourceSchema} otherFields={pipeline.lookupSchema} counterpart={pipeline.lookupKeyField} disabled={readOnly} onChange={sourceLookupField => update("sourceLookupField", sourceLookupField)} />
          <FieldChoice label="Lookup key field" value={pipeline.lookupKeyField} fields={pipeline.lookupSchema} otherFields={pipeline.sourceSchema} counterpart={pipeline.sourceLookupField} disabled={readOnly} onChange={lookupKeyField => update("lookupKeyField", lookupKeyField)} />
        </div>
      </section>

      <MappingEditor mapping={pipeline.mapping} sourceSchema={pipeline.sourceSchema} lookupSchema={pipeline.lookupSchema} disabled={readOnly} onChange={mapping => update("mapping", mapping)} />
      <FilterEditor filters={pipeline.filters} mapping={pipeline.mapping} sourceSchema={pipeline.sourceSchema} lookupSchema={pipeline.lookupSchema} disabled={readOnly} onChange={filters => update("filters", filters)} />
      <LimitsEditor limits={pipeline.limits} sourceSchemaCount={pipeline.sourceSchema.length} lookupSchemaCount={pipeline.lookupSchema.length} disabled={readOnly} onChange={limits => update("limits", limits)} />
    </div>
  );
}

function FieldChoice({ label, value, fields, otherFields, counterpart, disabled, onChange }: {
  label: string;
  value: string;
  fields: readonly { name: string; type: string; required: boolean }[];
  otherFields: readonly { name: string; type: string; required: boolean }[];
  counterpart: string;
  disabled: boolean;
  onChange(value: string): void;
}) {
  const id = useId();
  const selected = fields.find(field => field.name === value);
  const matchingField = otherFields.find(field => field.name === counterpart);
  const validKeyPair = !!selected && !!matchingField && selected.required && matchingField.required && selected.type === matchingField.type;
  const helpId = `${id}-help`;
  return (
    <div className="etl-editor-field">
      <label htmlFor={id}>{label}</label>
      <select id={id} value={value} disabled={disabled} aria-describedby={helpId} aria-invalid={value !== "" && !validKeyPair} onChange={event => onChange(event.currentTarget.value)}>
        <option value="">Choose a required field</option>
        {value && !selected ? <option value={value}>{value} (unavailable)</option> : null}
        {fields.map((field, index) => {
          const suffix = !field.required ? " (optional)" : ` (${field.type})`;
          return <option key={`${field.name}-${index}`} value={field.name} disabled={!field.required}>{field.name || `Unnamed field ${index + 1}`}{suffix}</option>;
        })}
      </select>
      <small id={helpId}>{validKeyPair
        ? `Key type: ${selected.type}. Both key fields are required.`
        : "Choose required source and lookup key fields with the same type."}</small>
    </div>
  );
}
