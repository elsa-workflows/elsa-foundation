import React, { useId } from "react";
import type { EtlColumn, EtlColumnType } from "../etlTypes";

const columnTypes: EtlColumnType[] = ["string", "int64", "decimal", "boolean"];

export function SchemaEditor({
  title,
  description,
  schema,
  maxFields,
  disabled,
  onChange
}: {
  title: string;
  description: string;
  schema: readonly EtlColumn[];
  maxFields: number;
  disabled: boolean;
  onChange(schema: EtlColumn[]): void;
}) {
  const headingId = useId();
  const update = (index: number, changes: Partial<EtlColumn>) =>
    onChange(schema.map((column, row) => row === index ? { ...column, ...changes } : column));

  return (
    <section className="etl-editor-section" aria-labelledby={headingId}>
      <div className="etl-editor-section-heading">
        <div>
          <h3 id={headingId}>{title}</h3>
          <p>{description}</p>
        </div>
        <button type="button" disabled={disabled || schema.length >= maxFields} onClick={() => onChange([...schema, { name: "", type: "string", required: false }])}>
          Add field
        </button>
      </div>

      {schema.length === 0 ? <p className="etl-editor-empty" role="status">Add the CSV header fields expected by this stage.</p> : null}
      <div className="studio-table-container etl-editor-table-wrap" tabIndex={0} role="region" aria-label={`${title} fields table`}>
        <table className="studio-table etl-editor-table">
          <thead>
            <tr><th scope="col">CSV field</th><th scope="col">Type</th><th scope="col">Required</th><th scope="col"><span className="etl-editor-sr-only">Actions</span></th></tr>
          </thead>
          <tbody>
            {schema.map((column, index) => (
              <tr key={index}>
                <td>
                  <label className="etl-editor-sr-only" htmlFor={`${headingId}-name-${index}`}>{title} field {index + 1} name</label>
                  <input id={`${headingId}-name-${index}`} type="text" value={column.name} disabled={disabled} onChange={event => update(index, { name: event.currentTarget.value })} />
                </td>
                <td>
                  <label className="etl-editor-sr-only" htmlFor={`${headingId}-type-${index}`}>{title} field {index + 1} type</label>
                  <select id={`${headingId}-type-${index}`} value={column.type} disabled={disabled} onChange={event => update(index, { type: event.currentTarget.value as EtlColumnType })}>
                    {columnTypes.map(type => <option key={type} value={type}>{typeLabel(type)}</option>)}
                  </select>
                </td>
                <td>
                  <label className="etl-editor-checkbox" htmlFor={`${headingId}-required-${index}`}>
                    <input id={`${headingId}-required-${index}`} type="checkbox" aria-label={`${title} field ${index + 1} required`} checked={column.required} disabled={disabled} onChange={event => update(index, { required: event.currentTarget.checked })} />
                    <span>Required</span>
                  </label>
                </td>
                <td className="etl-editor-actions-cell">
                  <button type="button" className="etl-editor-remove" disabled={disabled} aria-label={`Remove ${title.toLowerCase()} field ${column.name || index + 1}`} onClick={() => onChange(schema.filter((_item, row) => row !== index))}>
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {schema.length >= maxFields ? <small className="etl-editor-limit-note">The configured field limit is {maxFields}.</small> : null}
    </section>
  );
}

export function typeLabel(type: EtlColumnType) {
  switch (type) {
    case "int64": return "64-bit integer";
    case "decimal": return "Decimal";
    case "boolean": return "Boolean";
    default: return "Text";
  }
}
