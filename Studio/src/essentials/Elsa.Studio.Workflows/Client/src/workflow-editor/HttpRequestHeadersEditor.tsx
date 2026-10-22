import { Plus, Trash2 } from "lucide-react";
import type { HttpTestRequestHeader } from "./httpTestRequestHeaders";

export function HttpRequestHeadersEditor({ rows, disabled, onChange }: {
  rows: HttpTestRequestHeader[];
  disabled: boolean;
  onChange(rows: HttpTestRequestHeader[]): void;
}) {
  const update = (id: number, changes: Partial<HttpTestRequestHeader>) =>
    onChange(rows.map(row => row.id === id ? { ...row, ...changes } : row));

  return (
    <details className="wf-http-test-request-headers">
      <summary>Request headers{rows.length ? ` (${rows.length})` : ""}</summary>
      <div className="wf-http-test-header-fields">
        {rows.map((row, index) => (
          <div className="wf-http-test-header-row" key={row.id}>
            <label className="wf-form-field">
              <span>Name</span>
              <input aria-label={`Header ${index + 1} name`} value={row.name} disabled={disabled}
                placeholder="X-Correlation-ID" autoComplete="off" spellCheck={false}
                onChange={event => update(row.id, { name: event.target.value })} />
            </label>
            <label className="wf-form-field">
              <span>Value</span>
              <input aria-label={`Header ${index + 1} value`} value={row.value} disabled={disabled}
                placeholder="demo-request" autoComplete="off" spellCheck={false}
                onChange={event => update(row.id, { value: event.target.value })} />
            </label>
            <button type="button" className="wf-icon-button" disabled={disabled}
              aria-label={`Remove header ${index + 1}`} onClick={() => onChange(rows.filter(item => item.id !== row.id))}>
              <Trash2 size={15} aria-hidden="true" />
            </button>
          </div>
        ))}
        <button type="button" className="studio-button wf-http-test-add-header" disabled={disabled}
          onClick={() => onChange([...rows, { id: Math.max(0, ...rows.map(row => row.id)) + 1, name: "", value: "" }])}>
          <Plus size={14} aria-hidden="true" /> Add header
        </button>
      </div>
    </details>
  );
}
