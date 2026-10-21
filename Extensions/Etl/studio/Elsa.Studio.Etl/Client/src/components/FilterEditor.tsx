import React, { useId } from "react";
import { formatEtlJsonNumber, parseEtlJsonNumber } from "../etlTypes";
import type { EtlColumn, EtlFilter, EtlJsonNumber, EtlJsonScalar, EtlOutputField } from "../etlTypes";

type FilterType = "string" | "int64" | "decimal" | "boolean";

export function FilterEditor({
  filters,
  mapping,
  sourceSchema,
  lookupSchema,
  disabled,
  onChange
}: {
  filters: readonly EtlFilter[];
  mapping: readonly EtlOutputField[];
  sourceSchema: readonly EtlColumn[];
  lookupSchema: readonly EtlColumn[];
  disabled: boolean;
  onChange(filters: EtlFilter[]): void;
}) {
  const headingId = useId();
  const update = (index: number, changes: Partial<EtlFilter>) =>
    onChange(filters.map((filter, row) => row === index ? { ...filter, ...changes } : filter));

  return (
    <section className="etl-editor-section" aria-labelledby={headingId}>
      <div className="etl-editor-section-heading">
        <div>
          <h3 id={headingId}>Filter mapped output</h3>
          <p>Filters run after mapping. Only equality and inequality comparisons are supported.</p>
        </div>
        <button type="button" disabled={disabled} onClick={() => onChange([...filters, { field: "", comparison: "equal", value: "" }])}>Add filter</button>
      </div>
      {filters.length === 0 ? <p className="etl-editor-empty" role="status">No output filters are configured.</p> : null}
      <div className="etl-editor-table-wrap" tabIndex={0} role="region" aria-label="Output filter table">
        <table className="etl-editor-table etl-editor-filter-table">
          <thead><tr><th scope="col">Mapped field</th><th scope="col">Comparison</th><th scope="col">Value</th><th scope="col"><span className="etl-editor-sr-only">Actions</span></th></tr></thead>
          <tbody>
            {filters.map((filter, index) => {
              const output = mapping.find(field => field.name === filter.field);
              const schema = output?.origin === "lookup" ? lookupSchema : sourceSchema;
              const column = schema.find(field => field.name === output?.field);
              const type: FilterType = column?.type ?? valueType(filter.value);
              const id = `${headingId}-${index}`;
              const valueIsNull = filter.value === null;
              return (
                <tr key={index}>
                  <td>
                    <label className="etl-editor-sr-only" htmlFor={`${id}-field`}>Filter {index + 1} mapped field</label>
                    <select id={`${id}-field`} value={filter.field} disabled={disabled} onChange={event => update(index, { field: event.currentTarget.value })}>
                      <option value="">Choose an output field</option>
                      {filter.field && !mapping.some(field => field.name === filter.field) ? <option value={filter.field}>{filter.field} (unavailable)</option> : null}
                      {mapping.map((field, row) => <option key={`${field.name}-${row}`} value={field.name}>{field.name || `Unnamed output ${row + 1}`}</option>)}
                    </select>
                    {output ? <small className="etl-editor-inline-type">{column?.type ?? "Unknown field type"}{column && !column.required ? " · optional" : " · required"}</small> : null}
                  </td>
                  <td>
                    <label className="etl-editor-sr-only" htmlFor={`${id}-comparison`}>Filter {index + 1} comparison</label>
                    <select id={`${id}-comparison`} value={filter.comparison} disabled={disabled} onChange={event => update(index, { comparison: event.currentTarget.value as EtlFilter["comparison"] })}>
                      <option value="equal">Equals</option>
                      <option value="notEqual">Does not equal</option>
                    </select>
                  </td>
                  <td>
                    {column && !column.required || valueIsNull ? (
                      <label className="etl-editor-checkbox etl-editor-null-filter">
                        <input type="checkbox" aria-label={`Filter ${index + 1} compare with null`} checked={valueIsNull} disabled={disabled} onChange={event => update(index, { value: event.currentTarget.checked ? null : initialValue(type) })} />
                        <span>Compare with null</span>
                      </label>
                    ) : null}
                    {!valueIsNull ? <FilterValueInput id={`${id}-value`} index={index} type={type} value={filter.value} disabled={disabled} onChange={value => update(index, { value })} /> : <span className="etl-editor-null-value">Null</span>}
                  </td>
                  <td className="etl-editor-actions-cell">
                    <button type="button" className="etl-editor-remove" disabled={disabled} aria-label={`Remove filter ${index + 1}`} onClick={() => onChange(filters.filter((_item, row) => row !== index))}>Remove</button>
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

function FilterValueInput({
  id,
  index,
  type,
  value,
  disabled,
  onChange
}: {
  id: string;
  index: number;
  type: FilterType;
  value: EtlJsonScalar;
  disabled: boolean;
  onChange(value: EtlJsonScalar): void;
}) {
  if (type === "boolean") {
    return (
      <div className="etl-editor-field">
        <label className="etl-editor-sr-only" htmlFor={id}>Filter {index + 1} value</label>
        <select id={id} value={value === true ? "true" : value === false ? "false" : ""} disabled={disabled} onChange={event => { if (event.currentTarget.value !== "") onChange(event.currentTarget.value === "true"); }}>
          <option value="">Choose true or false</option>
          <option value="true">True</option>
          <option value="false">False</option>
        </select>
      </div>
    );
  }

  return (
    <div className="etl-editor-field">
      <label className="etl-editor-sr-only" htmlFor={id}>Filter {index + 1} value ({typeLabel(type)})</label>
      <input
        id={id}
        type="text"
        inputMode={type === "string" ? "text" : type === "int64" ? "numeric" : "decimal"}
        value={formatFilterValue(value)}
        disabled={disabled}
        onChange={event => onChange(parseFilterValue(event.currentTarget.value, type))}
      />
    </div>
  );
}

function parseFilterValue(raw: string, type: FilterType): EtlJsonScalar {
  if (type === "string") return raw;
  if (type === "int64") {
    if (!/^-?(?:0|[1-9]\d*)$/.test(raw)) return raw;
    try { return parseEtlJsonNumber(raw); }
    catch { return raw; }
  }
  if (!/^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?$/.test(raw)) return raw;
  try { return parseEtlJsonNumber(raw); }
  catch { return raw; }
}

function formatFilterValue(value: EtlJsonScalar) {
  if (value && typeof value === "object") return formatEtlJsonNumber(value as EtlJsonNumber);
  return value === null ? "" : String(value);
}

function valueType(value: EtlJsonScalar): FilterType {
  if (typeof value === "boolean") return "boolean";
  if (typeof value === "number") return Number.isInteger(value) ? "int64" : "decimal";
  if (value && typeof value === "object") return /[.eE]/.test(formatEtlJsonNumber(value)) ? "decimal" : "int64";
  return "string";
}

function initialValue(type: FilterType): EtlJsonScalar {
  switch (type) {
    case "boolean": return false;
    case "int64":
    case "decimal": return parseEtlJsonNumber("0");
    default: return "";
  }
}

function typeLabel(type: FilterType) {
  switch (type) {
    case "boolean": return "boolean";
    case "int64": return "64-bit integer";
    case "decimal": return "decimal";
    default: return "text";
  }
}
