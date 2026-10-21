import React, { useId } from "react";
import type { EtlColumn, EtlFieldOrigin, EtlOutputField } from "../etlTypes";

export function MappingEditor({
  mapping,
  sourceSchema,
  lookupSchema,
  disabled,
  sourceLabel = "Source CSV",
  onChange
}: {
  mapping: readonly EtlOutputField[];
  sourceSchema: readonly EtlColumn[];
  lookupSchema: readonly EtlColumn[];
  disabled: boolean;
  sourceLabel?: string;
  onChange(mapping: EtlOutputField[]): void;
}) {
  const headingId = useId();
  const update = (index: number, changes: Partial<EtlOutputField>) =>
    onChange(mapping.map((field, row) => row === index ? { ...field, ...changes } : field));

  return (
    <section className="etl-editor-section" aria-labelledby={headingId}>
      <div className="etl-editor-section-heading">
        <div>
          <h3 id={headingId}>Map output fields</h3>
          <p>Choose each output property and the source or matched lookup value that supplies it.</p>
        </div>
        <button type="button" disabled={disabled} onClick={() => onChange([...mapping, { name: "", origin: "source", field: "" }])}>Add mapping</button>
      </div>
      {mapping.length === 0 ? <p className="etl-editor-empty" role="status">No output fields are mapped yet.</p> : null}
      <div className="etl-editor-table-wrap" tabIndex={0} role="region" aria-label="Output field mapping table">
        <table className="etl-editor-table etl-editor-mapping-table">
          <thead><tr><th scope="col">Output field</th><th scope="col">From</th><th scope="col">Input field</th><th scope="col"><span className="etl-editor-sr-only">Actions</span></th></tr></thead>
          <tbody>
            {mapping.map((field, index) => {
              const origin = field.origin === "lookup" ? "lookup" : "source";
              const schema = origin === "source" ? sourceSchema : lookupSchema;
              const id = `${headingId}-${index}`;
              return (
                <tr key={index}>
                  <td>
                    <label className="etl-editor-sr-only" htmlFor={`${id}-name`}>Output field {index + 1} name</label>
                    <input id={`${id}-name`} type="text" value={field.name} disabled={disabled} onChange={event => update(index, { name: event.currentTarget.value })} />
                  </td>
                  <td>
                    <label className="etl-editor-sr-only" htmlFor={`${id}-origin`}>Output field {index + 1} source</label>
                    <select id={`${id}-origin`} value={origin} disabled={disabled} onChange={event => update(index, { origin: event.currentTarget.value as EtlFieldOrigin, field: "" })}>
                      <option value="source">{sourceLabel}</option>
                      <option value="lookup">Matched lookup</option>
                    </select>
                  </td>
                  <td>
                    <label className="etl-editor-sr-only" htmlFor={`${id}-field`}>Output field {index + 1} input field</label>
                    <select id={`${id}-field`} value={field.field} disabled={disabled} onChange={event => update(index, { field: event.currentTarget.value })}>
                      <option value="">Choose a field</option>
                      {field.field && !schema.some(column => column.name === field.field) ? <option value={field.field}>{field.field} (unavailable)</option> : null}
                      {schema.map((column, columnIndex) => <option key={`${column.name}-${columnIndex}`} value={column.name}>{column.name || `Unnamed field ${columnIndex + 1}`}</option>)}
                    </select>
                  </td>
                  <td className="etl-editor-actions-cell">
                    <button type="button" className="etl-editor-remove" disabled={disabled} aria-label={`Remove output mapping ${field.name || index + 1}`} onClick={() => onChange(mapping.filter((_item, row) => row !== index))}>Remove</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </section>
  );
}
