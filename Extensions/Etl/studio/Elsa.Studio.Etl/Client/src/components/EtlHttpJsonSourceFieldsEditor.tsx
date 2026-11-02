import React, { useId } from "react";
import type { EtlColumnType, EtlHttpJsonSource, EtlHttpPagination, EtlJsonField } from "../etlTypes";

export function EtlHttpJsonSourceFieldsEditor({ source, maxFields, disabled, onChange }: {
  source: EtlHttpJsonSource;
  maxFields: number;
  disabled: boolean;
  onChange(source: EtlHttpJsonSource): void;
}) {
  const id = useId();
  const updatePagination = (pagination: EtlHttpPagination) => onChange({ ...source, pagination });
  const selectPagination = (kind: EtlHttpPagination["kind"]) => {
    if (kind === source.pagination.kind) return;
    const continuationPointer = source.pagination.kind === "singlePage" ? "/next" : source.pagination.continuationPointer;
    switch (kind) {
      case "singlePage": updatePagination({ kind }); break;
      case "token": updatePagination({ kind, continuationPointer, queryParameter: "cursor" }); break;
      case "nextLink": updatePagination({ kind, continuationPointer }); break;
    }
  };
  const updateField = (index: number, changes: Partial<EtlJsonField>) =>
    onChange({ ...source, fields: source.fields.map((field, row) => row === index ? { ...field, ...changes } : field) });

  return (
    <>
      <div className="etl-editor-binding-grid">
        <TextField
          label="Records JSON Pointer"
          value={source.recordsPointer}
          disabled={disabled}
          help="Select the array of records in each JSON response, for example /data/items."
          onChange={recordsPointer => onChange({ ...source, recordsPointer })}
        />
        <div className="etl-editor-field">
          <label htmlFor={`${id}-pagination`}>Pagination strategy</label>
          <select id={`${id}-pagination`} value={source.pagination.kind} disabled={disabled} onChange={event => selectPagination(event.currentTarget.value as EtlHttpPagination["kind"])}>
            <option value="singlePage">Single page</option>
            <option value="token">Continuation token in query</option>
            <option value="nextLink">Next link</option>
          </select>
          <small>Continuation values are read from the current page using a JSON Pointer.</small>
        </div>
      </div>
      {source.pagination.kind !== "singlePage" ? (
        <div className="etl-editor-binding-grid">
          <TextField
            label="Continuation JSON Pointer"
            value={source.pagination.continuationPointer}
            disabled={disabled}
            help="Select the token or next-link value in each page response."
            onChange={continuationPointer => {
              const current = source.pagination;
              if (current.kind === "token") updatePagination({ kind: "token", continuationPointer, queryParameter: current.queryParameter });
              else if (current.kind === "nextLink") updatePagination({ kind: "nextLink", continuationPointer });
            }}
          />
          {source.pagination.kind === "token" ? (
            <TextField
              label="Continuation query parameter"
              value={source.pagination.queryParameter}
              disabled={disabled}
              help="The request builder replaces this named query parameter with the next token."
              onChange={queryParameter => {
                const current = source.pagination;
                if (current.kind === "token") updatePagination({ kind: "token", continuationPointer: current.continuationPointer, queryParameter });
              }}
            />
          ) : null}
        </div>
      ) : null}

      <section className="etl-editor-section" aria-labelledby={`${id}-fields`}>
        <div className="etl-editor-section-heading">
          <div>
            <h4 id={`${id}-fields`}>HTTP JSON fields</h4>
            <p>Declare each scalar value with a JSON Pointer relative to one record.</p>
          </div>
          <button type="button" disabled={disabled || source.fields.length >= maxFields} onClick={() => onChange({ ...source, fields: [...source.fields, { name: "", pointer: "", type: "string", required: false }] })}>Add field</button>
        </div>
        {source.fields.length === 0 ? <p className="etl-editor-empty" role="status">No HTTP JSON fields are declared yet.</p> : null}
        <div className="studio-table-container etl-editor-table-wrap" tabIndex={0} role="region" aria-label="HTTP JSON source fields table">
          <table className="studio-table etl-editor-table">
            <thead><tr><th scope="col">Field name</th><th scope="col">JSON Pointer</th><th scope="col">Type</th><th scope="col">Required</th><th scope="col"><span className="etl-editor-sr-only">Actions</span></th></tr></thead>
            <tbody>
              {source.fields.map((field, index) => (
                <tr key={index}>
                  <td><label className="etl-editor-sr-only" htmlFor={`${id}-field-name-${index}`}>HTTP JSON field {index + 1} name</label><input id={`${id}-field-name-${index}`} type="text" value={field.name} disabled={disabled} onChange={event => updateField(index, { name: event.currentTarget.value })} /></td>
                  <td><label className="etl-editor-sr-only" htmlFor={`${id}-field-pointer-${index}`}>HTTP JSON field {index + 1} pointer</label><input id={`${id}-field-pointer-${index}`} type="text" value={field.pointer} disabled={disabled} onChange={event => updateField(index, { pointer: event.currentTarget.value })} /></td>
                  <td><label className="etl-editor-sr-only" htmlFor={`${id}-field-type-${index}`}>HTTP JSON field {index + 1} type</label><select id={`${id}-field-type-${index}`} value={field.type} disabled={disabled} onChange={event => updateField(index, { type: event.currentTarget.value as EtlColumnType })}><option value="string">Text</option><option value="int64">64-bit integer</option><option value="decimal">Decimal</option><option value="boolean">Boolean</option></select></td>
                  <td><label className="etl-editor-checkbox"><input type="checkbox" aria-label={`HTTP JSON field ${index + 1} required`} checked={field.required} disabled={disabled} onChange={event => updateField(index, { required: event.currentTarget.checked })} /><span>Required</span></label></td>
                  <td className="etl-editor-actions-cell"><button type="button" className="etl-editor-remove" disabled={disabled} aria-label={`Remove HTTP JSON field ${field.name || index + 1}`} onClick={() => onChange({ ...source, fields: source.fields.filter((_item, row) => row !== index) })}>Remove</button></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {source.fields.length >= maxFields ? <small className="etl-editor-limit-note">The configured field limit is {maxFields}.</small> : null}
      </section>
    </>
  );
}

function TextField({ label, value, disabled, help, onChange }: {
  label: string;
  value: string;
  disabled: boolean;
  help: string;
  onChange(value: string): void;
}) {
  const id = useId();
  return (
    <div className="etl-editor-field">
      <label htmlFor={id}>{label}</label>
      <input id={id} type="text" value={value} disabled={disabled} aria-describedby={`${id}-help`} onChange={event => onChange(event.currentTarget.value)} />
      <small id={`${id}-help`}>{help}</small>
    </div>
  );
}
